//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM
{
    using System.Threading.Tasks;
    using System.Web.Http;

    [RoutePrefix(ServiceConstants.RouteBulk)]
    [Authorize]
    public sealed class BulkRequestController : ScimApiControllerBase
    {
        private readonly ScimDiscoveryRequestHandler handler;

        public BulkRequestController(IProvider provider, IMonitor monitor)
        {
            this.handler = ScimRequestHandlerFactory.CreateDiscoveryHandler(provider, monitor);
        }

        [HttpPost]
        [Route("")]
        public Task<IHttpActionResult> Post([FromBody] BulkRequest2 bulkRequest)
        {
            return this.ExecuteAsync(() => this.handler.ProcessBulkRequestAsync(this.Request, bulkRequest));
        }
    }
}
