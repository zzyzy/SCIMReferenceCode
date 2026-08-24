// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Web.Http;
    using System.Web.Http.Controllers;
    using System.Web.Http.Dispatcher;
    using System.Web.Http.Routing;

    /// <summary>
    /// Resolves the controller for a request by route precedence rather than treating a
    /// multi-controller match as an error.
    /// </summary>
    /// <remarks>
    /// The SCIM service root is served by <c>RootController</c> at <c>scim/{identifier}</c>,
    /// which overlaps <c>scim/Users</c>, <c>scim/Groups</c>, <c>scim/Schemas</c> and every
    /// other endpoint. ASP.NET Core routing resolves that overlap in favour of the literal
    /// segment, so the net10.0 leg serves all of them. ASP.NET Web API instead collects every
    /// matching sub-route and <see cref="DefaultHttpControllerSelector"/> throws
    /// "Multiple controller types were found that match the URL" as soon as two of them belong
    /// to different controllers - which on this route table is every single request.
    ///
    /// Web API has already ordered the sub-routes by precedence (literal segments ahead of
    /// parameter segments) by the time they reach here, so taking the first one reproduces
    /// ASP.NET Core's choice. The sub-route list is then narrowed to that controller so that
    /// action selection does not see the discarded candidates.
    /// </remarks>
    public sealed class ScimControllerSelector : DefaultHttpControllerSelector
    {
        // System.Web.Http.Routing.RouteDataTokenKeys is internal; these are its values.
        private const string SubRoutesKey = "MS_SubRoutes";
        private const string ActionsKey = "actions";

        public ScimControllerSelector(HttpConfiguration configuration)
            : base(configuration)
        {
        }

        public override HttpControllerDescriptor SelectController(HttpRequestMessage request)
        {
            if (null == request)
            {
                throw new ArgumentNullException(nameof(request));
            }

            IHttpRouteData routeData = request.GetRouteData();

            if (null == routeData ||
                !routeData.Values.TryGetValue(ScimControllerSelector.SubRoutesKey, out object value) ||
                !(value is IHttpRouteData[] subRoutes) ||
                subRoutes.Length < 2)
            {
                return base.SelectController(request);
            }

            HttpControllerDescriptor selected =
                subRoutes
                    .Where(subRoute => null != ScimControllerSelector.GetController(subRoute))
                    .OrderBy(ScimControllerSelector.Precedence, StringComparer.Ordinal)
                    .Select(ScimControllerSelector.GetController)
                    .FirstOrDefault();

            if (null == selected)
            {
                return base.SelectController(request);
            }

            IHttpRouteData[] retained =
                subRoutes
                    .Where(subRoute => selected == ScimControllerSelector.GetController(subRoute))
                    .ToArray();

            if (retained.Length != subRoutes.Length)
            {
                routeData.Values[ScimControllerSelector.SubRoutesKey] = retained;
            }

            return selected;
        }

        /// <summary>
        /// A sortable key that ranks a route template the way ASP.NET Core ranks it: segment
        /// by segment, a literal ahead of a parameter and a parameter ahead of a catch-all.
        /// </summary>
        /// <remarks>
        /// Web API exposes no public precedence API, and the order in which it hands over
        /// matching sub-routes is not the order it matched them in, so the comparison is
        /// redone here rather than trusting the incoming sequence.
        /// </remarks>
        private static string Precedence(IHttpRouteData subRoute)
        {
            string template = subRoute.Route?.RouteTemplate ?? string.Empty;

            IEnumerable<char> ranks =
                template
                    .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(
                        segment =>
                            !segment.StartsWith("{", StringComparison.Ordinal) ? '0' :
                            segment.StartsWith("{*", StringComparison.Ordinal) ? '2' : '1');

            return new string(ranks.ToArray());
        }

        private static HttpControllerDescriptor GetController(IHttpRouteData subRoute)
        {
            if (null == subRoute?.Route?.DataTokens)
            {
                return null;
            }

            if (!subRoute.Route.DataTokens.TryGetValue(ScimControllerSelector.ActionsKey, out object actions))
            {
                return null;
            }

            IEnumerable<HttpActionDescriptor> descriptors = actions as IEnumerable<HttpActionDescriptor>;

            return descriptors?.FirstOrDefault()?.ControllerDescriptor;
        }
    }
}
