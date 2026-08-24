// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;

    [Route(ServiceConstants.RouteUsers)]
    [Authorize]
    [ApiController]
    public sealed class UsersController : ScimResourceControllerBase<Core2EnterpriseUser>
    {
        public UsersController(IProvider provider, ILogger<UsersController> logger)
            : base(ScimRequestHandlerFactory.CreateUserHandler(provider, logger))
        {
        }
    }
}
