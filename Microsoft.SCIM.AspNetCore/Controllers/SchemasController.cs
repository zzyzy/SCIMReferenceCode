// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;

    [Route(ServiceConstants.RouteSchemas)]
    [Authorize]
    [ApiController]
    public sealed class SchemasController : ScimControllerBase
    {
        private readonly ScimDiscoveryRequestHandler handler;

        public SchemasController(IProvider provider, ILogger<SchemasController> logger)
        {
            this.handler = ScimRequestHandlerFactory.CreateDiscoveryHandler(provider, logger);
        }

        [HttpGet]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1716:IdentifiersShouldNotMatchKeywords", MessageId = "Get", Justification = "The names of the methods of a controller must correspond to the names of hypertext markup verbs")]
        public IActionResult Get()
        {
            return this.ToActionResult(this.handler.QuerySchemas(this.ConvertRequest()));
        }

        /// <summary>
        /// RFC 7644 section 4: one of them, retrieved the way a single User or Group is.
        /// </summary>
        /// <remarks>
        /// A catch-all route parameter, for the reason the net48 leg gives: a schema URI is
        /// all dots and colons, and a plain {identifier} segment stops at the first dot.
        /// </remarks>
        [HttpGet("{*identifier}")]
        public IActionResult Get(string identifier)
        {
            return this.ToActionResult(this.handler.RetrieveSchema(this.ConvertRequest(), identifier));
        }
    }
}
