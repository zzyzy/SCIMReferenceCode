//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM
{
    using System.Threading.Tasks;
    using System.Web.Http;
    using Microsoft.Extensions.Logging;

    [RoutePrefix(ServiceConstants.RouteBulk)]
    [Authorize]
    public sealed class BulkRequestController : ScimApiControllerBase
    {
        private readonly ScimDiscoveryRequestHandler handler;

        public BulkRequestController(IProvider provider, ILogger<BulkRequestController> logger)
        {
            this.handler = ScimRequestHandlerFactory.CreateDiscoveryHandler(provider, logger);
        }

        [HttpPost]
        [Route("")]
        public Task<IHttpActionResult> Post([FromBody] BulkRequest2 bulkRequest)
        {
            return this.ExecuteAsync(() => this.handler.ProcessBulkRequestAsync(this.Request, bulkRequest));
        }
    }
}
