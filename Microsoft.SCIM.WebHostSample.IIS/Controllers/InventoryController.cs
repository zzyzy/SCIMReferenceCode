//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM.WebHostSample.IIS.Controllers
{
    using System.Web.Http;

    /// <summary>
    /// Stands in for whatever the application already served before SCIM was added.
    /// </summary>
    /// <remarks>
    /// It exists so that the sample can demonstrate the thing consumers actually worry about:
    /// that api/inventory keeps working, unauthenticated and with its own JSON settings, while
    /// scim/Users is served from a separate configuration in the same process.
    /// </remarks>
    public class InventoryController : ApiController
    {
        public IHttpActionResult Get()
        {
            return this.Ok(new { product = "Widget", inStock = 42, discontinued = (string)null });
        }
    }
}
