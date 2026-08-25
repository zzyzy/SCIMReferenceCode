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
    using Microsoft.Owin.Security;
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
    /// The OWIN middleware itself does not report <i>why</i> validation failed, but the token
    /// format it wraps does: an unrecognised key identifier surfaces as
    /// <see cref="SecurityTokenSignatureKeyNotFoundException"/>. <see cref="RefreshingJwtFormat"/>
    /// catches exactly that and re-fetches, which is the <c>RefreshOnIssuerKeyNotFound</c>
    /// behaviour the other leg gets from <c>JwtBearerOptions</c>.
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
    /// Re-fetches the key set when a token names a key identifier the cache does not hold,
    /// then validates once more.
    /// </summary>
    /// <remarks>
    /// The authority rotates its signing keys: a new key appears at the key set endpoint with
    /// a new <c>kid</c>, and tokens start naming it. A cache that only refreshes on a timer
    /// rejects every token signed with the new key until the timer happens to fire, which is an
    /// outage of up to that interval on each rotation.
    ///
    /// <c>JwtFormat</c> throws <see cref="SecurityTokenSignatureKeyNotFoundException"/> for
    /// precisely this case - no key matched, as distinct from a key matched and the signature
    /// was wrong. Retrying on any other failure would turn a bad signature into a request to
    /// the authority, which is a denial-of-service amplifier, so only this one is caught.
    ///
    /// Once, not in a loop: <c>ConfigurationManager.RequestRefresh</c> is rate-limited
    /// internally, and a token naming a key that genuinely does not exist must fail rather
    /// than spin.
    /// </remarks>
    internal sealed class RefreshingJwtFormat : ISecureDataFormat<AuthenticationTicket>
    {
        private readonly ISecureDataFormat<AuthenticationTicket> inner;
        private readonly JsonWebKeySetIssuerSecurityKeyProvider keyProvider;

        public RefreshingJwtFormat(
            ISecureDataFormat<AuthenticationTicket> inner,
            JsonWebKeySetIssuerSecurityKeyProvider keyProvider)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
        }

        public AuthenticationTicket Unprotect(string protectedText)
        {
            try
            {
                return this.inner.Unprotect(protectedText);
            }
            catch (SecurityTokenSignatureKeyNotFoundException)
            {
                this.keyProvider.RequestRefresh();
                return this.inner.Unprotect(protectedText);
            }
        }

        /// <summary>
        /// Not supported. This format validates inbound tokens; it does not mint them.
        /// </summary>
        public string Protect(AuthenticationTicket data)
        {
            throw new NotSupportedException();
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
                    AccessTokenFormat =
                        new RefreshingJwtFormat(
                            new JwtFormat(validationParameters, keyProvider),
                            keyProvider),
                });

            return app;
        }
    }
}

#endif
