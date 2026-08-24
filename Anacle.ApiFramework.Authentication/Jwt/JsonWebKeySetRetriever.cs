// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Anacle.ApiFramework.Authentication.Jwt
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.IdentityModel.Protocols;
    using Microsoft.IdentityModel.Protocols.OpenIdConnect;
    using Microsoft.IdentityModel.Tokens;

    /// <summary>
    /// Fetches a bare JSON Web Key Set and presents it as an
    /// <see cref="OpenIdConnectConfiguration"/>.
    /// </summary>
    /// <remarks>
    /// <c>OpenIdConnectConfigurationRetriever</c> - the only retriever Microsoft.IdentityModel
    /// ships - reads a discovery document, so it cannot read an authority that publishes only
    /// the key set. Wrapping the key set in an <c>OpenIdConnectConfiguration</c> is what allows
    /// <c>ConfigurationManager</c> to be used anyway, and that class is worth reaching for: it
    /// brings caching, background refresh, last-known-good configuration and the
    /// <c>RequestRefresh</c> hook that key rotation needs. None of that is worth reimplementing.
    /// </remarks>
    public class JsonWebKeySetRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
    {
        private readonly string issuer;

        public JsonWebKeySetRetriever(string issuer)
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

                    // Setting JsonWebKeySet populates the key set; SigningKeys below is what the
                    // token handler matches the token header's key identifier against.
                    JsonWebKeySet = new JsonWebKeySet(document),
                };

            foreach (SecurityKey key in configuration.JsonWebKeySet.GetSigningKeys())
            {
                configuration.SigningKeys.Add(key);
            }

            return configuration;
        }

        /// <summary>
        /// A configuration manager over the authority's key set, ready to hand to the JWT
        /// middleware on either hosting framework.
        /// </summary>
        public static ConfigurationManager<OpenIdConnectConfiguration> CreateConfigurationManager(
            JsonWebKeySetOptions options)
        {
            if (null == options)
            {
                throw new ArgumentNullException(nameof(options));
            }

            options.Validate();

            return
                new ConfigurationManager<OpenIdConnectConfiguration>(
                    options.ResolveKeySetAddress(),
                    new JsonWebKeySetRetriever(options.Issuer),
                    new HttpDocumentRetriever
                    {
                        RequireHttps = options.RequireHttpsMetadata,
                    })
                {
                    AutomaticRefreshInterval = options.AutomaticRefreshInterval,
                    RefreshInterval = options.RefreshInterval,
                };
        }

        /// <summary>
        /// Validation parameters that check the signature against the published key set, the
        /// expected issuer and audience, and the expiry.
        /// </summary>
        public static TokenValidationParameters CreateValidationParameters(
            JsonWebKeySetOptions options)
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
                    ValidAudiences = options.ResolveAudiences(),

                    ValidateLifetime = true,
                    ClockSkew = options.ClockSkew,

                    ValidateIssuerSigningKey = true,

                    // See JsonWebKeySetOptions.ValidAlgorithms: without this an attacker can
                    // present a token signed with a symmetric algorithm keyed on the published
                    // public key.
                    ValidAlgorithms = options.ValidAlgorithms,

                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                };
        }
    }
}
