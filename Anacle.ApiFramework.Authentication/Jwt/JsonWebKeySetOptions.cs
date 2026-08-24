// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Anacle.ApiFramework.Authentication.Jwt
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// What is needed to validate a bearer token issued by an authority that publishes a bare
    /// JSON Web Key Set.
    /// </summary>
    /// <remarks>
    /// The JWKS case is deliberately separate from the OpenID Connect case: an authority that
    /// publishes an OpenID Connect discovery document needs none of this, because both hosting
    /// frameworks discover its keys on their own. This exists for the authority that publishes
    /// only the key set.
    /// </remarks>
    public class JsonWebKeySetOptions
    {
        /// <summary>The expected issuer claim.</summary>
        public string Issuer
        {
            get;
            set;
        }

        /// <summary>
        /// The expected audience claim. Supply this or <see cref="Audiences"/>.
        /// </summary>
        public string Audience
        {
            get;
            set;
        }

        /// <summary>
        /// The accepted audience claims, for an authority that issues more than one.
        /// </summary>
        public IEnumerable<string> Audiences
        {
            get;
            set;
        }

        /// <summary>
        /// Where to fetch the signing keys. Defaults to <see cref="Issuer"/> plus
        /// <see cref="KeySetPath"/>.
        /// </summary>
        public string KeySetAddress
        {
            get;
            set;
        }

        /// <summary>
        /// The path the key set is published at, appended to <see cref="Issuer"/> when
        /// <see cref="KeySetAddress"/> is not given. There is no cross-authority convention
        /// worth defaulting to, so this is stated per authority.
        /// </summary>
        public string KeySetPath
        {
            get;
            set;
        }

        /// <summary>
        /// The signature algorithms to accept, as <c>SecurityAlgorithms</c> values.
        /// </summary>
        /// <remarks>
        /// Required, and deliberately so. Left unset, the token handler accepts any algorithm
        /// the key material supports, which is what an algorithm-substitution attempt relies
        /// on: a token signed with HS256 using the published public key as the shared secret.
        /// Naming the algorithms the authority actually signs with closes that off.
        /// </remarks>
        public IEnumerable<string> ValidAlgorithms
        {
            get;
            set;
        }

        /// <summary>
        /// Whether the key set must be fetched over HTTPS. Turn this off only against a local
        /// test authority.
        /// </summary>
        public bool RequireHttpsMetadata
        {
            get;
            set;
        } = true;

        /// <summary>
        /// How long a fetched key set is reused before it is refreshed in the background.
        /// </summary>
        public TimeSpan AutomaticRefreshInterval
        {
            get;
            set;
        } = TimeSpan.FromHours(12);

        /// <summary>
        /// The shortest interval between two forced refreshes, so that a burst of tokens
        /// carrying an unknown key identifier cannot be used to hammer the key set endpoint.
        /// </summary>
        public TimeSpan RefreshInterval
        {
            get;
            set;
        } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Permitted clock skew when checking expiry. Kept small: a generous skew extends the
        /// window in which a leaked token still works.
        /// </summary>
        public TimeSpan ClockSkew
        {
            get;
            set;
        } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// The accepted audiences, from either <see cref="Audience"/> or <see cref="Audiences"/>.
        /// </summary>
        public IReadOnlyCollection<string> ResolveAudiences()
        {
            List<string> result = new List<string>();

            if (!string.IsNullOrWhiteSpace(this.Audience))
            {
                result.Add(this.Audience);
            }

            if (null != this.Audiences)
            {
                result.AddRange(this.Audiences.Where(value => !string.IsNullOrWhiteSpace(value)));
            }

            return result;
        }

        /// <summary>The resolved key set address.</summary>
        public string ResolveKeySetAddress()
        {
            if (!string.IsNullOrWhiteSpace(this.KeySetAddress))
            {
                return this.KeySetAddress;
            }

            if (string.IsNullOrWhiteSpace(this.Issuer) || string.IsNullOrWhiteSpace(this.KeySetPath))
            {
                throw new InvalidOperationException(
                    "Either KeySetAddress, or both Issuer and KeySetPath, must be configured.");
            }

            return this.Issuer.TrimEnd('/') + "/" + this.KeySetPath.TrimStart('/');
        }

        /// <summary>Throws if the options cannot be used.</summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(this.Issuer))
            {
                throw new InvalidOperationException("The token issuer is not configured.");
            }

            if (0 == this.ResolveAudiences().Count)
            {
                throw new InvalidOperationException("No token audience is configured.");
            }

            if (null == this.ValidAlgorithms || !this.ValidAlgorithms.Any())
            {
                throw new InvalidOperationException(
                    "ValidAlgorithms is not configured. Name the signature algorithms the issuer "
                    + "signs with; accepting any algorithm allows an algorithm-substitution attempt.");
            }

            string address = this.ResolveKeySetAddress();

            if (this.RequireHttpsMetadata
                && !address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The key set address is not HTTPS. Set RequireHttpsMetadata to false only "
                    + "against a local test authority.");
            }
        }
    }
}
