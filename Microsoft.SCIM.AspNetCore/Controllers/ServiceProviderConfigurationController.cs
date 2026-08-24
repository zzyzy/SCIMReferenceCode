// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;

    [Route(ServiceConstants.RouteServiceConfiguration)]
    [Authorize]
    [ApiController]
    public sealed class ServiceProviderConfigurationController : ScimControllerBase
    {
        private readonly ScimDiscoveryRequestHandler handler;

        public ServiceProviderConfigurationController(IProvider provider, ILogger<ServiceProviderConfigurationController> logger)
        {
            this.handler = ScimRequestHandlerFactory.CreateDiscoveryHandler(provider, logger);
        }

        [HttpGet]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1716:IdentifiersShouldNotMatchKeywords", MessageId = "Get", Justification = "The names of the methods of a controller must correspond to the names of hypertext markup verbs")]
        public IActionResult Get()
        {
            return this.ToActionResult(this.handler.RetrieveServiceProviderConfiguration(this.ConvertRequest()));
        }
    }
}
