// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System.Web.Http;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// The <c>/Users</c> endpoint, open over the User resource type.
    /// </summary>
    /// <remarks>
    /// A controller's generic parameter is its model-binding type - <c>[FromBody] T resource</c> -
    /// so a downstream library that adds a schema extension needs a controller bound to its own
    /// resource type. Closing this one is enough; there is no need to restate the routes, the
    /// verb surface or the handler wiring.
    ///
    /// <c>ScimHttpConfiguration.Configure&lt;T&gt;</c> registers the closed type and suppresses
    /// <see cref="UsersController"/>, which would otherwise contend for the same route. A
    /// downstream that also needs to decorate the controller - a rate-limiting filter, say -
    /// derives from this instead and registers that type.
    /// </remarks>
    [RoutePrefix(ServiceConstants.RouteUsers)]
    [Authorize]
    public class ScimUsersApiController<T> : ScimApiResourceControllerBase<T>
        where T : Core2EnterpriseUser
    {
        public ScimUsersApiController(IProvider provider, ILogger<ScimUsersApiController<T>> logger)
            : base(ScimRequestHandlerFactory.CreateUserHandler<T>(provider, logger))
        {
        }
    }
}
