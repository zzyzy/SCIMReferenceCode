//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM
{
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;

    [Route(ServiceConstants.RouteBulk)]
    [Authorize]
    [ApiController]
    public sealed class BulkRequestController : ScimControllerBase
    {
        private readonly ScimDiscoveryRequestHandler handler;

        public BulkRequestController(IProvider provider, IMonitor monitor)
        {
            this.handler = ScimRequestHandlerFactory.CreateDiscoveryHandler(provider, monitor);
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] BulkRequest2 bulkRequest)
        {
            ScimResult result =
                await this.handler
                    .ProcessBulkRequestAsync(this.ConvertRequest(), bulkRequest)
                    .ConfigureAwait(false);
            return this.ToActionResult(result);
        }
    }
}
