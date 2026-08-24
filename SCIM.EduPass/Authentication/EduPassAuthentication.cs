// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Scim.EduPass
{
    using Anacle.ApiFramework.Authentication.Jwt;
    using Microsoft.IdentityModel.Tokens;

    /// <summary>
    /// The Edupass values for JWKS-backed bearer token validation.
    /// </summary>
    /// <remarks>
    /// Everything Edupass authentication needs beyond these four values is generic and lives in
    /// <c>Anacle.ApiFramework.Authentication.Jwt</c>: fetching the key set, caching it,
    /// refreshing it on rotation, and wiring the result into either hosting framework. What is
    /// actually specific to Edupass is only the issuers, the key set path and the algorithm.
    ///
    /// Edupass authenticates only with a signed JWT. There is no API key or shared secret
    /// alternative, so the ApiKey mode in that library does not apply to an Edupass endpoint -
    /// it is there for a relying party's other callers.
    /// </remarks>
    public static class EduPassAuthentication
    {
        /// <summary>The Edupass preproduction issuer.</summary>
        public const string PreproductionIssuer = "https://api.preprod.edupass.moe.gov.sg";

        /// <summary>The Edupass production issuer.</summary>
        public const string ProductionIssuer = "https://api.edupass.moe.gov.sg";

        /// <summary>The path Edupass publishes its JSON Web Key Set at.</summary>
        public const string KeySetPath = "/.well-known/keys";

        /// <summary>
        /// Options for validating an Edupass bearer token.
        /// </summary>
        /// <param name="issuer">
        /// <see cref="PreproductionIssuer"/> or <see cref="ProductionIssuer"/>.
        /// </param>
        /// <param name="applicationCode">
        /// This relying party's Edupass application code, which is the expected audience.
        /// </param>
        public static JsonWebKeySetOptions CreateOptions(string issuer, string applicationCode)
        {
            return
                new JsonWebKeySetOptions
                {
                    Issuer = issuer,
                    Audience = applicationCode,
                    KeySetPath = EduPassAuthentication.KeySetPath,

                    // Edupass signs with ES256. Pinning it is what stops a token signed with a
                    // symmetric algorithm keyed on the published public key from validating.
                    ValidAlgorithms = new[] { SecurityAlgorithms.EcdsaSha256 },
                };
        }
    }
}
