// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System.Web.Http;
    using Microsoft.Extensions.Logging;

    [RoutePrefix(ServiceConstants.RouteSchemas)]
    [Authorize]
    public sealed class SchemasController : ScimApiControllerBase
    {
        private readonly ScimDiscoveryRequestHandler handler;

        public SchemasController(IProvider provider, ILogger<SchemasController> logger)
        {
            this.handler = ScimRequestHandlerFactory.CreateDiscoveryHandler(provider, logger);
        }

        [HttpGet]
        [Route("")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1716:IdentifiersShouldNotMatchKeywords", MessageId = "Get", Justification = "The names of the methods of a controller must correspond to the names of hypertext markup verbs")]
        public IHttpActionResult Get()
        {
            return this.Execute(() => this.handler.QuerySchemas(this.Request));
        }
    }
}
