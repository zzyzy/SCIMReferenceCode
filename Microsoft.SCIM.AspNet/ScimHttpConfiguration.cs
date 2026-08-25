// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Formatting;
    using System.Net.Http.Headers;
    using System.Threading;
    using System.Threading.Tasks;
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
        /// <summary>
        /// Configures the SCIM endpoints with <c>/Users</c> bound to <typeparamref name="T"/>,
        /// a type derived from <see cref="Core2EnterpriseUser"/> that carries a schema
        /// extension. Suppresses <see cref="UsersController"/> and registers
        /// <see cref="ScimUsersApiController{T}"/> closed over that type, so a downstream
        /// library needs no controller of its own.
        /// </summary>
        public static HttpConfiguration Configure<T>(
            HttpConfiguration configuration,
            IServiceProvider services,
            string pathPrefix = null)
            where T : Core2EnterpriseUser
        {
            return
                ScimHttpConfiguration.Configure(
                    configuration,
                    services,
                    pathPrefix,
                    new[] { typeof(UsersController) },
                    new[] { typeof(ScimUsersApiController<T>) });
        }

        /// <param name="pathPrefix">
        /// The URL segment to serve the SCIM endpoints under. Null uses the default <c>scim</c>;
        /// an empty or whitespace-only value serves them at the application root. See
        /// <see cref="ScimPath"/>.
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
            return
                ScimHttpConfiguration.Configure(
                    configuration,
                    services,
                    pathPrefix,
                    suppressedControllerTypes,
                    null);
        }

        private static HttpConfiguration Configure(
            HttpConfiguration configuration,
            IServiceProvider services,
            string pathPrefix,
            Type[] suppressedControllerTypes,
            Type[] addedControllerTypes)
        {
            if (null == configuration)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (null == services)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (null != pathPrefix)
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
            // between the two. See docs/scim-conformance.md section 5 item 4.
            //
            // ScimDirectRouteProvider rather than the default one so that the [Route]
            // attributes declared on ScimApiResourceControllerBase<T> are inherited by the
            // concrete controllers, as they are on ASP.NET Core.
            bool suppressing = null != suppressedControllerTypes && suppressedControllerTypes.Length > 0;
            bool adding = null != addedControllerTypes && addedControllerTypes.Length > 0;

            if (suppressing || adding)
            {
                configuration.Services.Replace(
                    typeof(IHttpControllerTypeResolver),
                    new ScimSuppressedControllerTypeResolver(
                        suppressedControllerTypes,
                        addedControllerTypes));
            }

            configuration.MapHttpAttributeRoutes(new ScimDirectRouteProvider());

            // Match the ASP.NET Core leg's Newtonsoft settings exactly.
            JsonMediaTypeFormatter jsonFormatter = configuration.Formatters.JsonFormatter;
            jsonFormatter.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
            jsonFormatter.SerializerSettings.Converters.Add(new SchematizedJsonConverter());
            jsonFormatter.SupportedMediaTypes.Add(new MediaTypeHeaderValue(ProtocolConstants.ContentType));

            // ASP.NET Core has no XML formatter, so Web API's would make
            // 'Accept: application/xml' return XML on net48 and 406 on net10 - an immediate
            // parity break. See docs/scim-conformance.md X7.
            configuration.Formatters.Remove(configuration.Formatters.XmlFormatter);

            configuration.Filters.Add(new ScimExceptionFilterAttribute());

            configuration.MessageHandlers.Insert(0, new ScimHeadRequestHandler());

            // Outermost, so that it sees the response an authorization filter short-circuited
            // with. See ScimUnauthorizedResponseHandler.
            configuration.MessageHandlers.Insert(0, new ScimUnauthorizedResponseHandler());

            // Before the formatter reads the body, so that the failure logging can read it
            // afterwards. See ScimRequestBufferingHandler and ScimLoggerExtensions.
            configuration.MessageHandlers.Add(new ScimRequestBufferingHandler());

            return configuration;
        }
    }

    /// <summary>
    /// Answers HEAD with 405, rather than letting Web API dispatch it to a GET action.
    /// </summary>
    /// <remarks>
    /// Web API matches HEAD against GET actions, so the action runs and produces a body that
    /// must then not be written. The OWIN adapter's attempt to write it anyway ends in a
    /// cancelled task and a closed socket, so the caller sees a connection reset rather than any
    /// HTTP response at all - a health-check probe reads that as the service being down. ASP.NET
    /// Core does not route HEAD to GET actions and answers 405, which is also what SCIM needs:
    /// RFC 7644 defines no HEAD semantics, and both legs must answer identically.
    /// </remarks>
    internal sealed class ScimHeadRequestHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (null != request && HttpMethod.Head == request.Method)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.MethodNotAllowed));
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
