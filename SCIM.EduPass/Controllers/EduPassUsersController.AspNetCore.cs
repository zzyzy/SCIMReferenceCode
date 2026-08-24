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
    /// Replaces <c>Microsoft.SCIM.UsersController</c>, which binds the sealed
    /// <c>Core2EnterpriseUser</c> and so cannot carry the Edupass extension. The host suppresses
    /// the built-in one by passing <c>typeof(Microsoft.SCIM.UsersController)</c> to
    /// <c>AddScim</c>; without that the two would contend for the same route.
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
            : base(EduPassRequestHandlerFactory.CreateUserHandler(provider, logger))
        {
        }
    }
}

#endif
