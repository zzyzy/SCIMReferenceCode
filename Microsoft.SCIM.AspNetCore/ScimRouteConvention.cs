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
                // Only this assembly's controllers: a host's own controllers may legitimately
                // serve routes that begin with the same segment.
                if (controller.ControllerType.Assembly != typeof(ScimRouteConvention).Assembly)
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

                foreach (ActionModel action in controller.Actions)
                {
                    foreach (SelectorModel selector in action.Selectors)
                    {
                        if (null == selector.AttributeRouteModel?.Template)
                        {
                            continue;
                        }

                        selector.AttributeRouteModel.Template =
                            ScimPath.ApplyPrefix(selector.AttributeRouteModel.Template);
                    }
                }
            }
        }
    }
}
