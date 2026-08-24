// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

#if !NET48

namespace Anacle.ApiFramework.Authentication.Jwt
{
    using System;
    using Microsoft.AspNetCore.Authentication.JwtBearer;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>
    /// Wires JWKS-backed bearer token validation into ASP.NET Core.
    /// </summary>
    public static class JwtAuthenticationExtensions
    {
        /// <summary>
        /// Adds JWT bearer authentication against an authority that publishes a bare key set.
        /// </summary>
        /// <remarks>
        /// <c>Authority</c> is deliberately not set: it would make the middleware fetch an
        /// OpenID Connect discovery document, which this authority does not publish. Supplying
        /// a <c>ConfigurationManager</c> built by <see cref="JsonWebKeySetRetriever"/> instead
        /// keeps everything the middleware does for key rotation, including
        /// <c>RefreshOnIssuerKeyNotFound</c>, which forces a re-fetch the first time a token
        /// arrives naming a key identifier that is not held.
        /// </remarks>
        /// <param name="authenticationScheme">
        /// The scheme name to register under. Defaults to <c>Bearer</c>. Name it explicitly
        /// when more than one authentication mode is registered, so that endpoints can select
        /// between them.
        /// </param>
        public static IServiceCollection AddJsonWebKeySetAuthentication(
            this IServiceCollection services,
            JsonWebKeySetOptions options,
            string authenticationScheme = JwtBearerDefaults.AuthenticationScheme)
        {
            if (null == services)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (null == options)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (string.IsNullOrWhiteSpace(authenticationScheme))
            {
                throw new ArgumentException(
                    "An authentication scheme name is required.",
                    nameof(authenticationScheme));
            }

            options.Validate();

            void Configure(JwtBearerOptions bearer)
            {
                bearer.ConfigurationManager = JsonWebKeySetRetriever.CreateConfigurationManager(options);
                bearer.TokenValidationParameters = JsonWebKeySetRetriever.CreateValidationParameters(options);

                // Re-fetch the key set when a token names a key identifier that is not held.
                bearer.RefreshOnIssuerKeyNotFound = true;

                bearer.RequireHttpsMetadata = options.RequireHttpsMetadata;
            }

            services
                .AddAuthentication(authenticationScheme)
                .AddJwtBearer(authenticationScheme, Configure);

            return services;
        }
    }
}

#endif
