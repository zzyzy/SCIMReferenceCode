// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Microsoft.AspNetCore.Mvc.ApplicationParts;
    using Microsoft.AspNetCore.Mvc.Controllers;

    /// <summary>
    /// Removes named controllers from MVC's discovered set.
    /// </summary>
    /// <remarks>
    /// The route templates are fixed at compile time, so a downstream library that needs to
    /// serve <c>/Users</c> with its own resource type cannot simply add a second controller -
    /// MVC would report an ambiguous match. Removing the built-in one first is what makes the
    /// replacement possible.
    /// </remarks>
    public sealed class ScimSuppressedControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        private readonly HashSet<TypeInfo> suppressed;

        public ScimSuppressedControllerFeatureProvider(params Type[] suppressedControllerTypes)
        {
            if (null == suppressedControllerTypes)
            {
                throw new ArgumentNullException(nameof(suppressedControllerTypes));
            }

            this.suppressed =
                new HashSet<TypeInfo>(suppressedControllerTypes.Select(type => type.GetTypeInfo()));
        }

        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            if (null == feature)
            {
                throw new ArgumentNullException(nameof(feature));
            }

            foreach (TypeInfo controller in feature.Controllers.ToArray())
            {
                if (this.suppressed.Contains(controller))
                {
                    feature.Controllers.Remove(controller);
                }
            }
        }
    }
}
