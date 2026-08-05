// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Net.Http.Formatting;
    using System.Net.Http.Headers;
    using System.Web.Http;
    using Newtonsoft.Json;

    /// <summary>
    /// Configures an ASP.NET Web API <see cref="HttpConfiguration"/> to serve the SCIM
    /// endpoints. The counterpart of <c>AddScim()</c> / <c>MapScim()</c> on the
    /// ASP.NET Core leg.
    /// </summary>
    public static class ScimHttpConfiguration
    {
        public static HttpConfiguration Configure(HttpConfiguration configuration, IServiceProvider services)
        {
            if (null == configuration)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (null == services)
            {
                throw new ArgumentNullException(nameof(services));
            }

            configuration.DependencyResolver = new ServiceProviderDependencyResolver(services);

            // Attribute routes only. There is deliberately no conventional default route on
            // either leg: Web API's default shape (api/{controller}/{id}) does not match
            // ASP.NET Core's, and a fallback route is the largest single source of drift
            // between the two. See MULTI-TARGET-PLAN.md D14a.
            configuration.MapHttpAttributeRoutes();

            // Match the ASP.NET Core leg's Newtonsoft settings exactly.
            JsonMediaTypeFormatter jsonFormatter = configuration.Formatters.JsonFormatter;
            jsonFormatter.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
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
