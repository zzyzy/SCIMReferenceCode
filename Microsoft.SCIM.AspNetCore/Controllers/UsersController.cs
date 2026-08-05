// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;

    [Route(ServiceConstants.RouteUsers)]
    [Authorize]
    [ApiController]
    public sealed class UsersController : ScimResourceControllerBase<Core2EnterpriseUser>
    {
        public UsersController(IProvider provider, IMonitor monitor)
            : base(ScimRequestHandlerFactory.CreateUserHandler(provider, monitor))
        {
        }
    }
}
