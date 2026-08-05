// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System.Web.Http;

    [RoutePrefix(ServiceConstants.RouteGroups)]
    [Authorize]
    public sealed class GroupsController : ScimApiResourceControllerBase<Core2Group>
    {
        public GroupsController(IProvider provider, IMonitor monitor)
            : base(ScimRequestHandlerFactory.CreateGroupHandler(provider, monitor))
        {
        }
    }
}
