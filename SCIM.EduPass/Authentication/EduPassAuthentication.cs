// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Scim.EduPass
{
    using System;
    using Anacle.ApiFramework.Authentication.ApiKey;
    using Anacle.ApiFramework.Authentication.Jwt;
    using Microsoft.IdentityModel.Tokens;

    /// <summary>
    /// Which authentication modes an Edupass endpoint accepts.
    /// </summary>
    /// <remarks>
    /// Flags rather than a choice of one, because a relying party often has to serve Edupass and
    /// its own callers on the same endpoint. With both set the two run concurrently and a
    /// request satisfies either.
    /// </remarks>
    [Flags]
    public enum EduPassAuthenticationModes
    {
        /// <summary>No mode. Not a usable configuration.</summary>
        None = 0,

        /// <summary>Edupass's own mode: an ES256 bearer token validated against its key set.</summary>
        Jwt = 1,

        /// <summary>A shared key in a request header, for a relying party's other callers.</summary>
        ApiKey = 2,
    }

    /// <summary>
    /// The Edupass values for JWKS-backed bearer token validation.
    /// </summary>
    /// <remarks>
    /// Everything Edupass authentication needs beyond these four values is generic and lives in
    /// <c>Anacle.ApiFramework.Authentication.Jwt</c>: fetching the key set, caching it,
    /// refreshing it on rotation, and wiring the result into either hosting framework. What is
    /// actually specific to Edupass is only the issuers, the key set path and the algorithm.
    ///
    /// Edupass itself authenticates only with a signed JWT - it offers no API key or shared
    /// secret alternative. The ApiKey mode is therefore never used by Edupass; it is there for a
    /// relying party's other callers, such as an internal admin tool or a monitoring probe, that
    /// may need the same endpoint.
    /// </remarks>
    public static class EduPassAuthentication
    {
        /// <summary>The Edupass preproduction issuer.</summary>
        public const string PreproductionIssuer = "https://api.preprod.edupass.moe.gov.sg";

        /// <summary>The Edupass production issuer.</summary>
        public const string ProductionIssuer = "https://api.edupass.moe.gov.sg";

        /// <summary>The path Edupass publishes its JSON Web Key Set at.</summary>
        public const string KeySetPath = "/.well-known/keys";

        /// <summary>The scheme the Edupass bearer token is registered under.</summary>
        public const string JwtScheme = "Bearer";

        /// <summary>The scheme the API key mode is registered under.</summary>
        public const string ApiKeyScheme = ApiKeyAuthenticationDefaults.AuthenticationScheme;

        /// <summary>The scheme that selects between the two when both are enabled.</summary>
        public const string CombinedScheme = "EduPass";

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

    /// <summary>
    /// How an Edupass endpoint authenticates its callers.
    /// </summary>
    /// <remarks>
    /// One object rather than a parameter per mode, so that a host can bind it from
    /// configuration and the enabled modes are a single switch.
    /// </remarks>
    public class EduPassAuthenticationSettings
    {
        /// <summary>The modes to enable. Both may be set.</summary>
        public EduPassAuthenticationModes Modes
        {
            get;
            set;
        } = EduPassAuthenticationModes.Jwt;

        /// <summary>
        /// <see cref="EduPassAuthentication.PreproductionIssuer"/> or
        /// <see cref="EduPassAuthentication.ProductionIssuer"/>. Required by the Jwt mode.
        /// </summary>
        public string Issuer
        {
            get;
            set;
        } = EduPassAuthentication.PreproductionIssuer;

        /// <summary>
        /// This relying party's Edupass application code, which is the expected audience.
        /// Required by the Jwt mode.
        /// </summary>
        public string ApplicationCode
        {
            get;
            set;
        }

        /// <summary>
        /// Resolves a presented API key to a caller. Required by the ApiKey mode.
        /// </summary>
        /// <remarks>
        /// An implementation rather than a list of keys: where keys live and how they are
        /// revoked is the relying party's decision. <c>HashedApiKeyStore</c> covers a small
        /// static set.
        /// </remarks>
        public IApiKeyStore ApiKeyStore
        {
            get;
            set;
        }

        /// <summary>The header the API key is read from.</summary>
        public string ApiKeyHeaderName
        {
            get;
            set;
        } = ApiKeyAuthenticationDefaults.HeaderName;

        /// <summary>Whether the Jwt mode is enabled.</summary>
        public bool IsJwtEnabled
        {
            get
            {
                return 0 != (this.Modes & EduPassAuthenticationModes.Jwt);
            }
        }

        /// <summary>Whether the ApiKey mode is enabled.</summary>
        public bool IsApiKeyEnabled
        {
            get
            {
                return 0 != (this.Modes & EduPassAuthenticationModes.ApiKey);
            }
        }

        /// <summary>The bearer token options implied by these settings.</summary>
        public JsonWebKeySetOptions CreateJsonWebKeySetOptions()
        {
            return EduPassAuthentication.CreateOptions(this.Issuer, this.ApplicationCode);
        }

        /// <summary>Throws if the settings cannot be used.</summary>
        public void Validate()
        {
            if (EduPassAuthenticationModes.None == this.Modes)
            {
                throw new InvalidOperationException(
                    "No authentication mode is enabled. An endpoint with no mode enabled rejects "
                    + "every caller; enable Jwt, ApiKey, or both.");
            }

            if (this.IsJwtEnabled)
            {
                if (string.IsNullOrWhiteSpace(this.ApplicationCode))
                {
                    throw new InvalidOperationException(
                        "The Edupass application code is not configured. It is the audience the "
                        + "bearer token is checked against.");
                }

                this.CreateJsonWebKeySetOptions().Validate();
            }

            if (this.IsApiKeyEnabled)
            {
                if (null == this.ApiKeyStore)
                {
                    throw new InvalidOperationException(
                        "The ApiKey mode is enabled but no IApiKeyStore is configured.");
                }

                if (string.IsNullOrWhiteSpace(this.ApiKeyHeaderName))
                {
                    throw new InvalidOperationException(
                        "The ApiKey mode is enabled but no header name is configured.");
                }
            }
        }
    }
}
