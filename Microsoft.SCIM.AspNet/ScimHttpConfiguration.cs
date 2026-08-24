// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Net.Http.Formatting;
    using System.Net.Http.Headers;
    using System.Web.Http;
    using System.Web.Http.Dispatcher;
    using Newtonsoft.Json;

    /// <summary>
    /// Configures an ASP.NET Web API <see cref="HttpConfiguration"/> to serve the SCIM
    /// endpoints. The counterpart of <c>AddScim()</c> / <c>MapScim()</c> on the
    /// ASP.NET Core leg.
    /// </summary>
    public static class ScimHttpConfiguration
    {
        /// <param name="pathPrefix">
        /// The URL segment to serve the SCIM endpoints under. Defaults to <c>scim</c> when
        /// null or blank. See <see cref="ScimPath"/>.
        /// </param>
        /// <param name="suppressedControllerTypes">
        /// Controllers in this assembly that must not be discovered, so that a downstream
        /// library can serve the same route with its own. See the ASP.NET Core counterpart in
        /// <c>ScimServiceCollectionExtensions.AddScim</c>.
        /// </param>
        public static HttpConfiguration Configure(
            HttpConfiguration configuration,
            IServiceProvider services,
            string pathPrefix = null,
            params Type[] suppressedControllerTypes)
        {
            if (null == configuration)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (null == services)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (!string.IsNullOrWhiteSpace(pathPrefix))
            {
                ScimPath.SetPrefix(pathPrefix);
            }

            configuration.DependencyResolver = new ServiceProviderDependencyResolver(services);

            // Controllers are not registered in the container on either leg; both construct
            // them from registered dependencies instead. See ScimControllerActivator.
            configuration.Services.Replace(typeof(IHttpControllerActivator), new ScimControllerActivator());

            // The service root at scim/{identifier} overlaps every other SCIM route; Web API
            // treats that as an error where ASP.NET Core resolves it. See ScimControllerSelector.
            configuration.Services.Replace(
                typeof(IHttpControllerSelector),
                new ScimControllerSelector(configuration));

            // Attribute routes only. There is deliberately no conventional default route on
            // either leg: Web API's default shape (api/{controller}/{id}) does not match
            // ASP.NET Core's, and a fallback route is the largest single source of drift
            // between the two. See MULTI-TARGET-PLAN.md D14a.
            //
            // ScimDirectRouteProvider rather than the default one so that the [Route]
            // attributes declared on ScimApiResourceControllerBase<T> are inherited by the
            // concrete controllers, as they are on ASP.NET Core.
            if (null != suppressedControllerTypes && suppressedControllerTypes.Length > 0)
            {
                configuration.Services.Replace(
                    typeof(IHttpControllerTypeResolver),
                    new ScimSuppressedControllerTypeResolver(suppressedControllerTypes));
            }

            configuration.MapHttpAttributeRoutes(new ScimDirectRouteProvider());

            // Match the ASP.NET Core leg's Newtonsoft settings exactly.
            JsonMediaTypeFormatter jsonFormatter = configuration.Formatters.JsonFormatter;
            jsonFormatter.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
            jsonFormatter.SerializerSettings.Converters.Add(new SchematizedJsonConverter());
            jsonFormatter.SupportedMediaTypes.Add(new MediaTypeHeaderValue(ProtocolConstants.ContentType));

            // ASP.NET Core has no XML formatter, so Web API's would make
            // 'Accept: application/xml' return XML on net48 and 406 on net10 - an immediate
            // parity break. See MULTI-TARGET-PLAN.md R7 and docs/scim-conformance.md X7.
            configuration.Formatters.Remove(configuration.Formatters.XmlFormatter);

            configuration.Filters.Add(new ScimExceptionFilterAttribute());

            return configuration;
        }
    }
}
