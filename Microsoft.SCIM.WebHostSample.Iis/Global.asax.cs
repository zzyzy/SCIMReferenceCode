//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM.WebHostSample.Iis
{
    using System.Web;
    using System.Web.Http;

    /// <summary>
    /// The pre-existing application. It knows nothing about SCIM.
    /// </summary>
    /// <remarks>
    /// This is deliberately the whole of the application's own startup: the point of this
    /// sample is that adding SCIM does not require editing it. The SCIM endpoints are added
    /// entirely from <see cref="ScimStartup"/>, which System.Web discovers on its own through
    /// the OwinStartup attribute.
    /// </remarks>
    public class SampleApplication : HttpApplication
    {
        protected void Application_Start()
        {
            GlobalConfiguration.Configure(WebApiConfig.Register);
        }
    }
}
