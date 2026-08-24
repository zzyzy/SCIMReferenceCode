// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Scim.EduPass
{
    using System;

    /// <summary>
    /// What a relying party needs in order to validate an Edupass bearer token.
    /// </summary>
    /// <remarks>
    /// Every value is issued during onboarding. There is no fallback: Edupass authenticates
    /// only with a signed JWT, so a shared secret or API key is not an option.
    /// </remarks>
    public class EduPassAuthenticationOptions
    {
        /// <summary>The Edupass preproduction issuer.</summary>
        public const string PreproductionIssuer = "https://api.preprod.edupass.moe.gov.sg";

        /// <summary>The Edupass production issuer.</summary>
        public const string ProductionIssuer = "https://api.edupass.moe.gov.sg";

        /// <summary>The path Edupass publishes its JSON Web Key Set at.</summary>
        public const string KeySetPath = "/.well-known/keys";

        /// <summary>
        /// The expected <c>iss</c>. One of <see cref="PreproductionIssuer"/> or
        /// <see cref="ProductionIssuer"/>.
        /// </summary>
        public string Issuer
        {
            get;
            set;
        }

        /// <summary>
        /// The expected <c>aud</c>: this relying party's Edupass application code.
        /// </summary>
        public string Audience
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
        /// How long a fetched key set is reused before it is refreshed in the background.
        /// </summary>
        /// <remarks>
        /// This is not the rotation mechanism. An unrecognized <c>kid</c> triggers an immediate
        /// refresh regardless - which is what the specification asks for - and this interval
        /// only governs routine re-fetching.
        /// </remarks>
        public TimeSpan AutomaticRefreshInterval
        {
            get;
            set;
        } = TimeSpan.FromHours(12);

        /// <summary>
        /// The shortest interval between two forced refreshes, so that a burst of tokens
        /// carrying an unknown <c>kid</c> cannot hammer the JWKS endpoint.
        /// </summary>
        public TimeSpan RefreshInterval
        {
            get;
            set;
        } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Permitted clock skew when checking <c>exp</c>. Kept small deliberately: Edupass
        /// tokens are short-lived, and a generous skew extends the window in which a leaked
        /// token still works.
        /// </summary>
        public TimeSpan ClockSkew
        {
            get;
            set;
        } = TimeSpan.FromSeconds(30);

        /// <summary>The resolved key set address.</summary>
        public string ResolveKeySetAddress()
        {
            if (!string.IsNullOrWhiteSpace(this.KeySetAddress))
            {
                return this.KeySetAddress;
            }

            if (string.IsNullOrWhiteSpace(this.Issuer))
            {
                throw new InvalidOperationException(
                    "Either Issuer or KeySetAddress must be configured.");
            }

            return this.Issuer.TrimEnd('/') + EduPassAuthenticationOptions.KeySetPath;
        }

        /// <summary>Throws if the options cannot be used.</summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(this.Issuer))
            {
                throw new InvalidOperationException(
                    "The Edupass token issuer is not configured (EduPass:Issuer).");
            }

            if (string.IsNullOrWhiteSpace(this.Audience))
            {
                throw new InvalidOperationException(
                    "The Edupass application code is not configured (EduPass:Audience).");
            }
        }
    }
}
