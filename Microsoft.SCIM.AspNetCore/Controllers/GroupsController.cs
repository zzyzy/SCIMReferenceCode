// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;

    [Route(ServiceConstants.RouteGroups)]
    [Authorize]
    [ApiController]
    public sealed class GroupsController : ScimResourceControllerBase<Core2Group>
    {
        public GroupsController(IProvider provider, ILogger<GroupsController> logger)
            : base(ScimRequestHandlerFactory.CreateGroupHandler(provider, logger))
        {
        }
    }
}
