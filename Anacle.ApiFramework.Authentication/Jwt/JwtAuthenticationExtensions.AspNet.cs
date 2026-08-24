// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

#if NET48

namespace Anacle.ApiFramework.Authentication.Jwt
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using Microsoft.IdentityModel.Protocols;
    using Microsoft.IdentityModel.Protocols.OpenIdConnect;
    using Microsoft.IdentityModel.Tokens;
    using Microsoft.Owin.Security.Jwt;
    using Microsoft.Owin.Security.OAuth;
    using Owin;

    /// <summary>
    /// Supplies an authority's published signing keys to the OWIN JWT middleware.
    /// </summary>
    /// <remarks>
    /// OWIN's <c>JwtFormat</c> takes an <see cref="IIssuerSecurityKeyProvider"/> and has no
    /// discovery of its own, so the key set handling that ASP.NET Core gets from
    /// <c>JwtBearerOptions</c> has to be supplied here. It is the same
    /// <c>ConfigurationManager</c> underneath, so caching, background refresh and
    /// last-known-good behaviour match the other leg rather than being reimplemented.
    ///
    /// One difference remains and it matters. OWIN does not report that validation failed
    /// because of an unrecognised key identifier, so there is no equivalent of
    /// <c>RefreshOnIssuerKeyNotFound</c>. Rotation is handled instead by returning every
    /// currently published key and letting <c>AutomaticRefreshInterval</c> pick up new ones -
    /// so set that interval shorter than the window the authority allows between publishing a
    /// key and signing with it.
    /// </remarks>
    public class JsonWebKeySetIssuerSecurityKeyProvider : IIssuerSecurityKeyProvider
    {
        private readonly ConfigurationManager<OpenIdConnectConfiguration> configurationManager;
        private readonly JsonWebKeySetOptions options;

        public JsonWebKeySetIssuerSecurityKeyProvider(JsonWebKeySetOptions options)
        {
            if (null == options)
            {
                throw new ArgumentNullException(nameof(options));
            }

            options.Validate();

            this.options = options;
            this.configurationManager = JsonWebKeySetRetriever.CreateConfigurationManager(options);
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
    /// Wires JWKS-backed bearer token validation into an OWIN pipeline.
    /// </summary>
    public static class JwtAuthenticationExtensions
    {
        /// <summary>
        /// Adds bearer token authentication against an authority that publishes a bare key set.
        /// Call before the protected endpoints run.
        /// </summary>
        public static IAppBuilder UseJsonWebKeySetAuthentication(
            this IAppBuilder app,
            JsonWebKeySetOptions options)
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

            JsonWebKeySetIssuerSecurityKeyProvider keyProvider =
                new JsonWebKeySetIssuerSecurityKeyProvider(options);

            TokenValidationParameters validationParameters =
                JsonWebKeySetRetriever.CreateValidationParameters(options);

            // UseOAuthBearerAuthentication rather than UseJwtBearerAuthentication: only the
            // former accepts an AccessTokenFormat, which is how the validation parameters above
            // get used. UseJwtBearerAuthentication builds its own format from AllowedAudiences
            // plus the key providers, silently discarding the algorithm pin.
            app.UseOAuthBearerAuthentication(
                new OAuthBearerAuthenticationOptions
                {
                    AccessTokenFormat = new JwtFormat(validationParameters, keyProvider),
                });

            return app;
        }
    }
}

#endif
