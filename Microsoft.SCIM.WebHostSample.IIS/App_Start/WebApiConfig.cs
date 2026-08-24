//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM.WebHostSample.IIS
{
    using System.Web.Http;

    /// <summary>
    /// The pre-existing application's own Web API routes, on the global configuration.
    /// </summary>
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration configuration)
        {
            // Conventional routing, on purpose.
            //
            // ScimHttpConfiguration.Configure calls MapHttpAttributeRoutes(), and Web API
            // discovers controllers by scanning every loaded assembly - not just the one the
            // configuration came from. An attribute-routed controller of your own is therefore
            // also mapped into the SCIM configuration, where it would be constructed by the
            // SCIM container and formatted by the SCIM formatter settings. Conventional routes
            // are not scanned that way, so they stay on this configuration only.
            //
            // If your application already uses attribute routing, see integration-guide.md.
            configuration.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional });
        }
    }
}
