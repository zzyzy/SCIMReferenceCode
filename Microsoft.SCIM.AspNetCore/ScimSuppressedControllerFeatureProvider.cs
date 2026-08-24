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
    /// Adds and removes named controllers in MVC's discovered set.
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
        private readonly TypeInfo[] added;

        public ScimSuppressedControllerFeatureProvider(params Type[] suppressedControllerTypes)
            : this(suppressedControllerTypes, null)
        {
        }

        /// <param name="addedControllerTypes">
        /// Controllers to register that assembly scanning does not find. A closed generic such
        /// as <c>ScimUsersController&lt;EduPassUser&gt;</c> is not discovered, because
        /// <c>ControllerFeatureProvider</c> only considers types whose name ends in
        /// <c>Controller</c>.
        /// </param>
        public ScimSuppressedControllerFeatureProvider(
            Type[] suppressedControllerTypes,
            Type[] addedControllerTypes)
        {
            this.suppressed =
                new HashSet<TypeInfo>(
                    (suppressedControllerTypes ?? new Type[0]).Select(type => type.GetTypeInfo()));

            this.added =
                (addedControllerTypes ?? new Type[0]).Select(type => type.GetTypeInfo()).ToArray();
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

            foreach (TypeInfo controller in this.added)
            {
                if (!feature.Controllers.Contains(controller))
                {
                    feature.Controllers.Add(controller);
                }
            }
        }
    }
}
