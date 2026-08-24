// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Collections.Generic;
    using System.Web.Http.Controllers;
    using System.Web.Http.Routing;

    /// <summary>
    /// A <see cref="IDirectRouteProvider"/> that honours <c>[Route]</c> and
    /// <c>[RoutePrefix]</c> declared on a base class.
    /// </summary>
    /// <remarks>
    /// Web API's <see cref="DefaultDirectRouteProvider"/> reads route attributes with
    /// <c>inherit: false</c>, so the verb surface declared once on
    /// <see cref="ScimApiResourceControllerBase{T}"/> produces no routes at all on
    /// <c>UsersController</c>, <c>GroupsController</c> or <c>RootController</c> - every
    /// request to scim/Users or scim/Groups 404s. ASP.NET Core's attribute routing inherits,
    /// so reading with <c>inherit: true</c> here is what keeps the two legs' route tables
    /// identical.
    /// </remarks>
    public sealed class ScimDirectRouteProvider : DefaultDirectRouteProvider
    {
        protected override IReadOnlyList<IDirectRouteFactory> GetActionRouteFactories(
            HttpActionDescriptor actionDescriptor)
        {
            if (null == actionDescriptor)
            {
                throw new ArgumentNullException(nameof(actionDescriptor));
            }

            return actionDescriptor.GetCustomAttributes<IDirectRouteFactory>(inherit: true);
        }
    }
}
