// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

#if !NET48

namespace Scim.EduPass
{
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;
    using Microsoft.SCIM;

    /// <summary>
    /// The <c>/Users</c> endpoint, bound to <see cref="EduPassUser"/>.
    /// </summary>
    /// <remarks>
    /// Replaces <c>Microsoft.SCIM.UsersController</c>. A controller's generic parameter is its
    /// model-binding type - <c>[FromBody] T resource</c> - so the built-in one binds a
    /// <c>Core2EnterpriseUser</c> and drops the Edupass extension. The host suppresses it by
    /// passing <c>typeof(Microsoft.SCIM.UsersController)</c> to <c>AddScim</c>; without that the
    /// two would contend for the same route.
    ///
    /// Thin, like every other SCIM controller here: it names the resource type and delegates.
    /// The verb surface, the status codes and the error bodies all come from
    /// <c>ScimResourceControllerBase&lt;T&gt;</c> and <c>ScimRequestHandler&lt;T&gt;</c>.
    /// </remarks>
    [Route(ServiceConstants.RouteUsers)]
    [Authorize]
    [ApiController]
    public sealed class EduPassUsersController : ScimResourceControllerBase<EduPassUser>
    {
        public EduPassUsersController(IProvider provider, ILogger<EduPassUsersController> logger)
            : base(ScimRequestHandlerFactory.CreateUserHandler<EduPassUser>(provider, logger))
        {
        }
    }
}

#endif
