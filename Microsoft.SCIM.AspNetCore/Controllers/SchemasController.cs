// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;

    [Route(ServiceConstants.RouteSchemas)]
    [Authorize]
    [ApiController]
    public sealed class SchemasController : ScimControllerBase
    {
        private readonly ScimDiscoveryRequestHandler handler;

        public SchemasController(IProvider provider, IMonitor monitor)
        {
            this.handler = ScimRequestHandlerFactory.CreateDiscoveryHandler(provider, monitor);
        }

        [HttpGet]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1716:IdentifiersShouldNotMatchKeywords", MessageId = "Get", Justification = "The names of the methods of a controller must correspond to the names of hypertext markup verbs")]
        public IActionResult Get()
        {
            return this.ToActionResult(this.handler.QuerySchemas(this.ConvertRequest()));
        }
    }
}
