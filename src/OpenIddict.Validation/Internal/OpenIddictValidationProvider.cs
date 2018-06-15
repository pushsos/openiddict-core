﻿/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/openiddict/openiddict-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System;
using System.ComponentModel;
using System.Text;
using System.Threading.Tasks;
using AspNet.Security.OAuth.Validation;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace OpenIddict.Validation
{
    /// <summary>
    /// Provides the logic necessary to extract, validate and handle OAuth2 requests.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public class OpenIddictValidationProvider : OAuthValidationEvents
    {
        public override Task ApplyChallenge([NotNull] ApplyChallengeContext context)
            => context.HttpContext.RequestServices.GetRequiredService<IOpenIddictValidationEventService>()
                .PublishAsync(new OpenIddictValidationEvents.ApplyChallenge(context));

        public override Task CreateTicket([NotNull] CreateTicketContext context)
            => context.HttpContext.RequestServices.GetRequiredService<IOpenIddictValidationEventService>()
                .PublishAsync(new OpenIddictValidationEvents.CreateTicket(context));

        public override async Task DecryptToken([NotNull] DecryptTokenContext context)
        {
            var options = (OpenIddictValidationOptions) context.Options;

            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<OpenIddictValidationProvider>>();

            if (options.UseReferenceTokens)
            {
                // Note: the token manager is deliberately not injected using constructor injection
                // to allow using the validation handler without having to register the core services.
                var manager = context.HttpContext.RequestServices.GetService<IOpenIddictTokenManager>();
                if (manager == null)
                {
                    throw new InvalidOperationException(new StringBuilder()
                        .AppendLine("The core services must be registered when enabling reference tokens support.")
                        .Append("To register the OpenIddict core services, use 'services.AddOpenIddict().AddCore()'.")
                        .ToString());
                }

                // Retrieve the token entry from the database. If it
                // cannot be found, assume the token is not valid.
                var token = await manager.FindByReferenceIdAsync(context.Token);
                if (token == null)
                {
                    logger.LogError("Authentication failed because the access token cannot be found in the database.");

                    context.HandleResponse();
                    return;
                }

                // Extract the encrypted payload from the token. If it's null or empty,
                // assume the token is not a reference token and consider it as invalid.
                var payload = await manager.GetPayloadAsync(token);
                if (string.IsNullOrEmpty(payload))
                {
                    logger.LogError("Authentication failed because the access token is not a reference token.");

                    context.HandleResponse();
                    return;
                }

                var ticket = context.DataFormat.Unprotect(payload);
                if (ticket == null)
                {
                    logger.LogError("Authentication failed because the reference token cannot be decrypted. " +
                                    "This may indicate that the token entry is corrupted or tampered.");

                    context.HandleResponse();
                    return;
                }

                // Dynamically set the creation and expiration dates.
                ticket.Properties.IssuedUtc = await manager.GetCreationDateAsync(token);
                ticket.Properties.ExpiresUtc = await manager.GetExpirationDateAsync(token);

                // Restore the token and authorization identifiers attached with the database entry.
                ticket.Properties.SetProperty(OpenIddictConstants.Properties.TokenId, await manager.GetIdAsync(token));
                ticket.Properties.SetProperty(OpenIddictConstants.Properties.AuthorizationId,
                    await manager.GetAuthorizationIdAsync(token));

                context.Ticket = ticket;
                context.HandleResponse();
            }

            await context.HttpContext.RequestServices.GetRequiredService<IOpenIddictValidationEventService>()
                .PublishAsync(new OpenIddictValidationEvents.DecryptToken(context));
        }

        public override Task RetrieveToken([NotNull] RetrieveTokenContext context)
            => context.HttpContext.RequestServices.GetRequiredService<IOpenIddictValidationEventService>()
                .PublishAsync(new OpenIddictValidationEvents.RetrieveToken(context));

        public override Task ValidateToken([NotNull] ValidateTokenContext context)
            => context.HttpContext.RequestServices.GetRequiredService<IOpenIddictValidationEventService>()
                .PublishAsync(new OpenIddictValidationEvents.ValidateToken(context));
    }
}