// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

#if !NET48

namespace Scim.EduPass
{
    using System;
    using Anacle.ApiFramework.Authentication.ApiKey;
    using Anacle.ApiFramework.Authentication.Jwt;
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>
    /// Wires the configured Edupass authentication modes into ASP.NET Core.
    /// </summary>
    public static class EduPassAuthenticationExtensions
    {
        /// <summary>
        /// Registers the modes named by <paramref name="settings"/>.
        /// </summary>
        /// <remarks>
        /// With both modes enabled the default scheme becomes a policy scheme that forwards on
        /// the shape of the request: a request carrying the API key header goes to the key
        /// scheme, everything else to the bearer scheme. Both schemes stay individually
        /// addressable by name, so an endpoint can still pin one with
        /// <c>[Authorize(AuthenticationSchemes = ...)]</c>.
        /// </remarks>
        public static IServiceCollection AddEduPassAuthentication(
            this IServiceCollection services,
            EduPassAuthenticationSettings settings)
        {
            if (null == services)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (null == settings)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.Validate();

            if (settings.IsJwtEnabled)
            {
                JsonWebKeySetOptions options = settings.CreateJsonWebKeySetOptions();

                services.AddJsonWebKeySetAuthentication(
                    options,
                    EduPassAuthentication.JwtScheme);
            }

            if (settings.IsApiKeyEnabled)
            {
                services.AddApiKeyAuthentication(
                    settings.ApiKeyStore,
                    headerName: settings.ApiKeyHeaderName,
                    authenticationScheme: EduPassAuthentication.ApiKeyScheme);
            }

            if (settings.IsJwtEnabled && settings.IsApiKeyEnabled)
            {
                string headerName = settings.ApiKeyHeaderName;

                services
                    .AddAuthentication(EduPassAuthentication.CombinedScheme)
                    .AddPolicyScheme(
                        EduPassAuthentication.CombinedScheme,
                        EduPassAuthentication.CombinedScheme,
                        policy =>
                            policy.ForwardDefaultSelector =
                                context =>
                                    context.Request.Headers.ContainsKey(headerName)
                                        ? EduPassAuthentication.ApiKeyScheme
                                        : EduPassAuthentication.JwtScheme);
            }

            return services;
        }
    }
}

#endif
