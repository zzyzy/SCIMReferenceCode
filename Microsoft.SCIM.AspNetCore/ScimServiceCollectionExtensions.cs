// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.ApplicationParts;
    using Microsoft.AspNetCore.Mvc.Controllers;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.Extensions.DependencyInjection;
    using Newtonsoft.Json;

    /// <summary>
    /// Host wiring for the SCIM endpoints on ASP.NET Core.
    /// </summary>
    public static class ScimServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the SCIM provider, the controllers and the
        /// <see cref="ScimExceptionFilter"/>. Controllers resolve their own
        /// <c>ILogger&lt;T&gt;</c>, so the host's existing logging configuration is used as-is.
        /// </summary>
        /// <param name="pathPrefix">
        /// The URL segment to serve the SCIM endpoints under. Defaults to <c>scim</c> when
        /// null or blank. See <see cref="ScimPath"/>.
        /// </param>
        /// <remarks>
        /// <c>AddApplicationPart</c> is load-bearing: the SCIM controllers live in this
        /// assembly, not in the entry assembly, and MVC only discovers controllers in
        /// application parts. Without it every SCIM route returns 404 while the host starts
        /// up perfectly happily. See MULTI-TARGET-PLAN.md R3.
        /// </remarks>
        /// <param name="suppressedControllerTypes">
        /// Controllers in this assembly that must not be discovered, so that a downstream
        /// library can serve the same route with its own. Pass
        /// <c>typeof(UsersController)</c> to replace the built-in Users endpoint - it binds the
        /// sealed <c>Core2EnterpriseUser</c>, so a service whose User resource carries an
        /// extension has to supply its own controller, and two controllers cannot share a
        /// route.
        /// </param>
        public static IServiceCollection AddScim(
            this IServiceCollection services,
            IProvider provider,
            string pathPrefix = null,
            params Type[] suppressedControllerTypes)
        {
            if (null == services)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (null == provider)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            if (!string.IsNullOrWhiteSpace(pathPrefix))
            {
                ScimPath.SetPrefix(pathPrefix);
            }

            services.AddSingleton(typeof(IProvider), provider);

            void ConfigureMvcOptions(MvcOptions options)
            {
                options.Filters.Add(new ScimExceptionFilter());
                options.Conventions.Add(new ScimRouteConvention());
            }

            void ConfigureMvcNewtonsoftJsonOptions(MvcNewtonsoftJsonOptions options)
            {
                options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;

                // Untyped schema extensions are invisible to the default contract; without this
                // an extension the service was not compiled against is dropped in both
                // directions. See SchematizedJsonConverter.
                options.SerializerSettings.Converters.Add(new SchematizedJsonConverter());
            }

            // [ApiController]'s automatic 400 emits a ValidationProblemDetails body, which
            // ASP.NET Web API has no equivalent for. Suppressing it lets an unparseable body
            // arrive at the action as null, where the shared handler turns it into the same
            // bare 400 both legs produce. RFC 7644 section 3.12 is satisfied either way; parity
            // is not, unless it is suppressed.
            // SuppressMapClientErrors for the same reason: without it, [ApiController] rewrites
            // any bare 4xx result into an RFC 9110 ProblemDetails body. ScimResult already
            // attaches a Core2Error to failure statuses, which is what RFC 7644 section 3.12
            // asks for and what the net48 leg emits.
            void ConfigureApiBehaviorOptions(ApiBehaviorOptions options)
            {
                options.SuppressModelStateInvalidFilter = true;
                options.SuppressMapClientErrors = true;
            }

            if (null != suppressedControllerTypes && suppressedControllerTypes.Length > 0)
            {
                services.AddSingleton<IApplicationFeatureProvider<ControllerFeature>>(
                    new ScimSuppressedControllerFeatureProvider(suppressedControllerTypes));
            }

            services
                .AddControllers(ConfigureMvcOptions)
                .AddNewtonsoftJson(ConfigureMvcNewtonsoftJsonOptions)
                .AddApplicationPart(typeof(UsersController).Assembly)
                .ConfigureApiBehaviorOptions(ConfigureApiBehaviorOptions);

            return services;
        }

        /// <summary>
        /// Maps the SCIM attribute routes. There is deliberately no conventional-route
        /// fallback on either hosting leg - see MULTI-TARGET-PLAN.md D14a.
        /// </summary>
        public static IEndpointRouteBuilder MapScim(this IEndpointRouteBuilder endpoints)
        {
            if (null == endpoints)
            {
                throw new ArgumentNullException(nameof(endpoints));
            }

            endpoints.MapControllers();
            return endpoints;
        }
    }
}
