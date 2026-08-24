// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

#if NET48

namespace Scim.EduPass
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using Microsoft.IdentityModel.Protocols;
    using Microsoft.IdentityModel.Protocols.OpenIdConnect;
    using Microsoft.IdentityModel.Tokens;
    using Microsoft.Owin.Security.OAuth;
    using Microsoft.Owin.Security.Jwt;
    using Owin;

    /// <summary>
    /// Supplies Edupass's signing keys to the OWIN JWT middleware.
    /// </summary>
    /// <remarks>
    /// OWIN's <c>JwtFormat</c> takes an <see cref="IIssuerSecurityKeyProvider"/> and has no
    /// discovery of its own, so the JWKS handling that ASP.NET Core gets from
    /// <c>JwtBearerOptions</c> has to be supplied here. It is the same
    /// <c>ConfigurationManager</c> underneath - so caching, background refresh and
    /// last-known-good behaviour match the other leg rather than being reimplemented.
    ///
    /// One difference remains and it matters: OWIN does not tell us that validation failed
    /// because of an unknown <c>kid</c>, so there is no equivalent of
    /// <c>RefreshOnIssuerKeyNotFound</c>. Rotation is therefore handled by returning every
    /// currently published key and letting <c>AutomaticRefreshInterval</c> pick up new ones;
    /// set that interval shorter than the window Edupass allows between publishing a key and
    /// signing with it.
    /// </remarks>
    public class EduPassIssuerSecurityKeyProvider : IIssuerSecurityKeyProvider
    {
        private readonly ConfigurationManager<OpenIdConnectConfiguration> configurationManager;
        private readonly EduPassAuthenticationOptions options;

        public EduPassIssuerSecurityKeyProvider(EduPassAuthenticationOptions options)
        {
            if (null == options)
            {
                throw new ArgumentNullException(nameof(options));
            }

            options.Validate();

            this.options = options;
            this.configurationManager = EduPassKeySetRetriever.CreateConfigurationManager(options);
        }

        public string Issuer
        {
            get
            {
                return this.options.Issuer;
            }
        }

        public IEnumerable<SecurityKey> SecurityKeys
        {
            get
            {
                // Synchronous by contract. GetConfigurationAsync serves a cached configuration
                // once warm, so this blocks only on the first call and after a refresh.
                OpenIdConnectConfiguration configuration =
                    this.configurationManager
                        .GetConfigurationAsync(CancellationToken.None)
                        .ConfigureAwait(false)
                        .GetAwaiter()
                        .GetResult();

                return configuration.SigningKeys.ToArray();
            }
        }

        /// <summary>Forces a key set re-fetch on the next read.</summary>
        public void RequestRefresh()
        {
            this.configurationManager.RequestRefresh();
        }
    }

    /// <summary>
    /// Wires Edupass bearer token validation into an OWIN pipeline.
    /// </summary>
    public static class EduPassAuthenticationExtensions
    {
        /// <summary>
        /// Adds bearer token authentication configured for Edupass. Call before the SCIM
        /// endpoints run - every SCIM controller carries <c>[Authorize]</c>, so without it every
        /// request is a 401.
        /// </summary>
        public static IAppBuilder UseEduPassAuthentication(
            this IAppBuilder app,
            EduPassAuthenticationOptions options)
        {
            if (null == app)
            {
                throw new ArgumentNullException(nameof(app));
            }

            if (null == options)
            {
                throw new ArgumentNullException(nameof(options));
            }

            options.Validate();

            EduPassIssuerSecurityKeyProvider keyProvider =
                new EduPassIssuerSecurityKeyProvider(options);

            TokenValidationParameters validationParameters =
                EduPassKeySetRetriever.CreateValidationParameters(options);

            app.UseOAuthBearerAuthentication(
                new OAuthBearerAuthenticationOptions
                {
                    AccessTokenFormat =
                        new JwtFormat(
                            validationParameters,
                            keyProvider),
                });

            return app;
        }
    }
}

#endif
