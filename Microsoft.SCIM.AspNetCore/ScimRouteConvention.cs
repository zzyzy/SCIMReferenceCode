// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using Microsoft.AspNetCore.Mvc.ApplicationModels;

    /// <summary>
    /// Rewrites the SCIM controllers' attribute-route templates so that they start with the
    /// segment configured through <see cref="ScimPath.SetPrefix"/>.
    /// </summary>
    /// <remarks>
    /// The templates are compile-time attribute arguments (<c>ServiceConstants.RouteUsers</c>
    /// and friends), so the segment cannot be configured at the attribute. MVC's built-in
    /// token replacement only understands <c>[controller]</c>, <c>[action]</c> and
    /// <c>[area]</c>, which does not help with a literal. An application-model convention runs
    /// once, before endpoints are built, and edits exactly the templates this assembly owns -
    /// leaving verbs, filters, authorization and inherited actions untouched.
    /// </remarks>
    public sealed class ScimRouteConvention : IApplicationModelConvention
    {
        public void Apply(ApplicationModel application)
        {
            if (null == application)
            {
                throw new ArgumentNullException(nameof(application));
            }

            foreach (ControllerModel controller in application.Controllers)
            {
                // Every SCIM controller and nothing else. Tested by base type rather than by
                // assembly: a consumer that derives from one of these to decorate it - a rate
                // limiting filter, say - lives in its own assembly but inherits the same
                // compile-time template, and must move with the configured prefix. An assembly
                // check leaves such a controller stranded on the default segment while the rest
                // of the service moves, with no error to say so. A host's unrelated controllers
                // do not derive from this base and are still left alone.
                if (!typeof(ScimControllerBase).IsAssignableFrom(controller.ControllerType))
                {
                    continue;
                }

                foreach (SelectorModel selector in controller.Selectors)
                {
                    if (null == selector.AttributeRouteModel?.Template)
                    {
                        continue;
                    }

                    selector.AttributeRouteModel.Template =
                        ScimPath.ApplyPrefix(selector.AttributeRouteModel.Template);
                }

                bool isServiceRoot = typeof(RootController) == controller.ControllerType;

                foreach (ActionModel action in controller.Actions)
                {
                    foreach (SelectorModel selector in action.Selectors)
                    {
                        if (null == selector.AttributeRouteModel?.Template)
                        {
                            continue;
                        }

                        string template = selector.AttributeRouteModel.Template;

                        if (isServiceRoot)
                        {
                            template = ScimRouteConvention.ExcludeCollectionSegments(template);
                        }

                        selector.AttributeRouteModel.Template = ScimPath.ApplyPrefix(template);
                    }
                }
            }
        }

        /// <summary>
        /// Constrains the service root's identifier parameter so that it cannot match a
        /// collection segment.
        /// </summary>
        /// <remarks>
        /// The service root routes at the prefix itself, so its <c>{identifier}</c> template is
        /// <c>scim/{identifier}</c> - the same shape as <c>scim/Users</c>. For a verb the Users
        /// controller does define, the literal wins and nothing is amiss. For one it does not,
        /// the parameterised route was the only candidate left, so <c>PUT scim/Users</c> reached
        /// the service root as a resource named "Users" and answered 400 or 415 where net48,
        /// whose routing stops at the matched controller, answered 405. Excluding the segments
        /// makes both legs answer 405 and stops a collection URI being read as an identifier.
        /// </remarks>
        private static string ExcludeCollectionSegments(string template)
        {
            const string Parameter = "{identifier}";

            if (!template.Contains(Parameter))
            {
                return template;
            }

            string reserved =
                string.Join(
                    "|",
                    ProtocolConstants.PathUsers,
                    ProtocolConstants.PathGroups,
                    ProtocolConstants.PathBulk,
                    ServiceConstants.PathSegmentSchemas,
                    ServiceConstants.PathSegmentResourceTypes,
                    ServiceConstants.PathSegmentServiceProviderConfiguration);

            return template.Replace(
                Parameter,
                "{identifier:regex(^(?!(?i:" + reserved + ")$).*$)}");
        }
    }
}
