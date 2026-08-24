// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

#if !NET48

namespace Scim.EduPass
{
    using System;
    using Microsoft.AspNetCore.Authentication.JwtBearer;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>
    /// Wires Edupass bearer token validation into ASP.NET Core.
    /// </summary>
    public static class EduPassAuthenticationExtensions
    {
        /// <summary>
        /// Adds JWT bearer authentication configured for Edupass.
        /// </summary>
        /// <remarks>
        /// <c>Authority</c> is deliberately not set: it would make the middleware fetch an
        /// OpenID Connect discovery document, and Edupass publishes only a JWKS. Supplying a
        /// <c>ConfigurationManager</c> built by <see cref="EduPassKeySetRetriever"/> instead
        /// keeps everything the middleware does for key rotation - including
        /// <c>RefreshOnIssuerKeyNotFound</c>, which forces a JWKS re-fetch the first time a
        /// token arrives with an unrecognized <c>kid</c>. That is exactly the behaviour the
        /// Edupass specification asks relying parties to implement.
        /// </remarks>
        public static IServiceCollection AddEduPassAuthentication(
            this IServiceCollection services,
            EduPassAuthenticationOptions options)
        {
            if (null == services)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (null == options)
            {
                throw new ArgumentNullException(nameof(options));
            }

            options.Validate();

            void Configure(JwtBearerOptions bearer)
            {
                bearer.ConfigurationManager = EduPassKeySetRetriever.CreateConfigurationManager(options);
                bearer.TokenValidationParameters = EduPassKeySetRetriever.CreateValidationParameters(options);

                // Re-fetch the key set when a token names a kid we do not hold.
                bearer.RefreshOnIssuerKeyNotFound = true;

                // No metadata address, so do not let the middleware try to discover one.
                bearer.RequireHttpsMetadata = true;
            }

            services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(Configure);

            return services;
        }
    }
}

#endif
