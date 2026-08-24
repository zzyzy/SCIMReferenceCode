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

        /// <summary>
        /// Replaces the default <c>scim</c> segment in a controller's <c>[RoutePrefix]</c>
        /// with the one configured through <see cref="ScimPath.SetPrefix"/>.
        /// </summary>
        /// <remarks>
        /// The net48 counterpart to <c>ScimRouteConvention</c> on the ASP.NET Core leg. This
        /// is the whole of it: the prefixes are the only templates carrying the segment, since
        /// the action-level <c>[Route]</c> templates are relative (<c>""</c> and
        /// <c>{identifier}</c>).
        /// </remarks>
        protected override string GetRoutePrefix(HttpControllerDescriptor controllerDescriptor)
        {
            string prefix = base.GetRoutePrefix(controllerDescriptor);

            if (null == controllerDescriptor
                || controllerDescriptor.ControllerType?.Assembly != typeof(ScimDirectRouteProvider).Assembly)
            {
                return prefix;
            }

            return ScimPath.ApplyPrefix(prefix);
        }
    }
}
