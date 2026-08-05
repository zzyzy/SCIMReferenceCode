// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System.Web.Http;

    [RoutePrefix(ServiceConstants.RouteUsers)]
    [Authorize]
    public sealed class UsersController : ScimApiResourceControllerBase<Core2EnterpriseUser>
    {
        public UsersController(IProvider provider, IMonitor monitor)
            : base(ScimRequestHandlerFactory.CreateUserHandler(provider, monitor))
        {
        }
    }
}
