// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System.Web.Http;
    using Microsoft.Extensions.Logging;

    [RoutePrefix(ServiceConstants.RouteGroups)]
    [Authorize]
    public sealed class GroupsController : ScimApiResourceControllerBase<Core2Group>
    {
        public GroupsController(IProvider provider, ILogger<GroupsController> logger)
            : base(ScimRequestHandlerFactory.CreateGroupHandler(provider, logger))
        {
        }
    }
}
