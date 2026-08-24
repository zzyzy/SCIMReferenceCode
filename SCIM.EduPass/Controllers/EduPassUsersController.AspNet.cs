// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

#if NET48

namespace Scim.EduPass
{
    using System.Web.Http;
    using Microsoft.Extensions.Logging;
    using Microsoft.SCIM;

    /// <summary>
    /// The <c>/Users</c> endpoint, bound to <see cref="EduPassUser"/>.
    /// </summary>
    /// <remarks>
    /// The ASP.NET Web API counterpart of the ASP.NET Core controller in the sibling file. Both
    /// exist for the same reason: the built-in <c>UsersController</c> binds the sealed
    /// <c>Core2EnterpriseUser</c>. The host suppresses it by passing
    /// <c>typeof(Microsoft.SCIM.UsersController)</c> to <c>ScimHttpConfiguration.Configure</c>.
    /// </remarks>
    [RoutePrefix(ServiceConstants.RouteUsers)]
    [Authorize]
    public sealed class EduPassUsersController : ScimApiResourceControllerBase<EduPassUser>
    {
        public EduPassUsersController(IProvider provider, ILogger<EduPassUsersController> logger)
            : base(EduPassRequestHandlerFactory.CreateUserHandler(provider, logger))
        {
        }
    }
}

#endif
