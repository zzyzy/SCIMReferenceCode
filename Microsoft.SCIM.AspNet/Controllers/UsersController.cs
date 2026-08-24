// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System.Web.Http;
    using Microsoft.Extensions.Logging;

    [RoutePrefix(ServiceConstants.RouteUsers)]
    [Authorize]
    public sealed class UsersController : ScimApiResourceControllerBase<Core2EnterpriseUser>
    {
        public UsersController(IProvider provider, ILogger<UsersController> logger)
            : base(ScimRequestHandlerFactory.CreateUserHandler(provider, logger))
        {
        }
    }
}
