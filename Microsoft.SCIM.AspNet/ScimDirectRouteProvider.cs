// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Web.Http;
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

            // Every SCIM controller and nothing else. Tested by base type rather than by
            // assembly, so that a consumer deriving from one of these to decorate it moves with
            // the configured prefix too. See ScimRouteConvention on the other leg.
            if (null == controllerDescriptor
                || !typeof(ScimApiControllerBase).IsAssignableFrom(controllerDescriptor.ControllerType))
            {
                return prefix;
            }

            if (null == prefix)
            {
                // base reads [RoutePrefix] with inherit: false as well, so a consumer's
                // controller derived from one of ours declares no prefix of its own and would
                // otherwise route at the service root - or, where a parameterised route such as
                // the service root's overlaps, be shadowed by it and 404.
                prefix = ScimDirectRouteProvider.FindRoutePrefix(controllerDescriptor.ControllerType);
            }

            return null == prefix ? null : ScimPath.ApplyPrefix(prefix);
        }

        /// <summary>
        /// The nearest <c>[RoutePrefix]</c> on <paramref name="controllerType"/> or any of its
        /// base types.
        /// </summary>
        private static string FindRoutePrefix(Type controllerType)
        {
            for (Type type = controllerType; null != type; type = type.BaseType)
            {
                IRoutePrefix attribute =
                    type
                        .GetCustomAttributes(typeof(RoutePrefixAttribute), inherit: false)
                        .Cast<IRoutePrefix>()
                        .FirstOrDefault();

                if (null != attribute)
                {
                    return attribute.Prefix;
                }
            }

            return null;
        }
    }
}
