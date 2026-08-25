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
        /// Registers the SCIM endpoints with <c>/Users</c> bound to <typeparamref name="T"/>, a
        /// type derived from <see cref="Core2EnterpriseUser"/> that carries a schema extension.
        /// Suppresses <see cref="UsersController"/> and registers
        /// <see cref="ScimUsersController{T}"/> closed over that type, so a downstream library
        /// needs no controller of its own.
        /// </summary>
        public static IServiceCollection AddScim<T>(
            this IServiceCollection services,
            IProvider provider,
            string pathPrefix = null)
            where T : Core2EnterpriseUser
        {
            return
                ScimServiceCollectionExtensions.AddScim(
                    services,
                    provider,
                    pathPrefix,
                    new[] { typeof(UsersController) },
                    new[] { typeof(ScimUsersController<T>) });
        }

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
        /// up perfectly happily.
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
            return
                ScimServiceCollectionExtensions.AddScim(
                    services,
                    provider,
                    pathPrefix,
                    suppressedControllerTypes,
                    null);
        }

        private static IServiceCollection AddScim(
            this IServiceCollection services,
            IProvider provider,
            string pathPrefix,
            Type[] suppressedControllerTypes,
            Type[] addedControllerTypes)
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

                // Before model binding, so that the failure logging can still read the body
                // an action's [FromBody] parameter was bound from. See
                // ScimRequestBufferingFilter.
                options.Filters.Add(new ScimRequestBufferingFilter());

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

            bool suppressing = null != suppressedControllerTypes && suppressedControllerTypes.Length > 0;
            bool adding = null != addedControllerTypes && addedControllerTypes.Length > 0;

            IMvcBuilder builder =
                services
                    .AddControllers(ConfigureMvcOptions)
                    .AddNewtonsoftJson(ConfigureMvcNewtonsoftJsonOptions)
                    .AddApplicationPart(typeof(UsersController).Assembly)
                    .ConfigureApiBehaviorOptions(ConfigureApiBehaviorOptions);

            // ConfigureApplicationPartManager rather than a container registration: MVC reads
            // its feature providers from the ApplicationPartManager, which is built before the
            // container and does not consult it.
            if (suppressing || adding)
            {
                builder.ConfigureApplicationPartManager(
                    manager =>
                        manager.FeatureProviders.Add(
                            new ScimSuppressedControllerFeatureProvider(
                                suppressedControllerTypes,
                                addedControllerTypes)));
            }

            return services;
        }

        /// <summary>
        /// Maps the SCIM attribute routes. There is deliberately no conventional-route
        /// fallback on either hosting leg - see docs/scim-conformance.md section 5 item 4.
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
