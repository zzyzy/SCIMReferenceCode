// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Scim.EduPass
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.IdentityModel.Protocols;
    using Microsoft.IdentityModel.Protocols.OpenIdConnect;
    using Microsoft.IdentityModel.Tokens;

    /// <summary>
    /// Fetches Edupass's JSON Web Key Set and presents it as an
    /// <see cref="OpenIdConnectConfiguration"/>.
    /// </summary>
    /// <remarks>
    /// Edupass publishes a bare JWKS at <c>/.well-known/keys</c>, not an OpenID Connect
    /// discovery document, so <c>OpenIdConnectConfigurationRetriever</c> - the only retriever
    /// Microsoft.IdentityModel ships - cannot read it. Wrapping the key set in an
    /// <c>OpenIdConnectConfiguration</c> is what lets
    /// <c>ConfigurationManager&lt;OpenIdConnectConfiguration&gt;</c> be used anyway, and that
    /// class is worth reaching for: it brings caching, background refresh, last-known-good
    /// configuration, and the <c>RequestRefresh</c> hook that key rotation needs.
    ///
    /// If Edupass ever also publishes <c>/.well-known/openid-configuration</c>, this type
    /// becomes unnecessary - set <c>Authority</c> and delete it.
    /// </remarks>
    public class EduPassKeySetRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
    {
        private readonly string issuer;

        public EduPassKeySetRetriever(string issuer)
        {
            if (string.IsNullOrWhiteSpace(issuer))
            {
                throw new ArgumentException("An issuer is required.", nameof(issuer));
            }

            this.issuer = issuer;
        }

        public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
            string address,
            IDocumentRetriever retriever,
            CancellationToken cancel)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new ArgumentException("An address is required.", nameof(address));
            }

            if (null == retriever)
            {
                throw new ArgumentNullException(nameof(retriever));
            }

            string document = await retriever.GetDocumentAsync(address, cancel).ConfigureAwait(false);

            OpenIdConnectConfiguration configuration =
                new OpenIdConnectConfiguration
                {
                    Issuer = this.issuer,
                    JwksUri = address,

                    // Setting JsonWebKeySet populates SigningKeys, which is what the token
                    // handler matches the JWT header's kid against.
                    JsonWebKeySet = new JsonWebKeySet(document),
                };

            foreach (SecurityKey key in configuration.JsonWebKeySet.GetSigningKeys())
            {
                configuration.SigningKeys.Add(key);
            }

            return configuration;
        }

        /// <summary>
        /// A configuration manager over Edupass's key set, ready to hand to the JWT middleware.
        /// </summary>
        public static ConfigurationManager<OpenIdConnectConfiguration> CreateConfigurationManager(
            EduPassAuthenticationOptions options)
        {
            if (null == options)
            {
                throw new ArgumentNullException(nameof(options));
            }

            options.Validate();

            return
                new ConfigurationManager<OpenIdConnectConfiguration>(
                    options.ResolveKeySetAddress(),
                    new EduPassKeySetRetriever(options.Issuer),
                    new HttpDocumentRetriever())
                {
                    AutomaticRefreshInterval = options.AutomaticRefreshInterval,
                    RefreshInterval = options.RefreshInterval,
                };
        }

        /// <summary>
        /// The validation rules the Edupass specification states: a valid ES256 signature from
        /// the published key set, the expected <c>iss</c> and <c>aud</c>, and an unexpired
        /// <c>exp</c>.
        /// </summary>
        public static TokenValidationParameters CreateValidationParameters(
            EduPassAuthenticationOptions options)
        {
            if (null == options)
            {
                throw new ArgumentNullException(nameof(options));
            }

            options.Validate();

            return
                new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,

                    ValidateAudience = true,
                    ValidAudience = options.Audience,

                    ValidateLifetime = true,
                    ClockSkew = options.ClockSkew,

                    ValidateIssuerSigningKey = true,

                    // Pinned to ES256: Edupass signs with it, and accepting anything else
                    // would leave the endpoint open to an algorithm-substitution attempt.
                    ValidAlgorithms = new[] { SecurityAlgorithms.EcdsaSha256 },

                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                };
        }
    }
}
