// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Web.Http.Dispatcher;

    /// <summary>
    /// Adds and removes named controllers in Web API's discovered set.
    /// </summary>
    /// <remarks>
    /// The net48 counterpart to <c>ScimSuppressedControllerFeatureProvider</c>. Web API resolves
    /// controller types by scanning assemblies, so replacing the resolver is the supported way
    /// to hide one - and hiding the built-in <c>UsersController</c> is what lets a downstream
    /// library serve <c>/Users</c> with its own resource type instead.
    /// </remarks>
    public sealed class ScimSuppressedControllerTypeResolver : DefaultHttpControllerTypeResolver
    {
        private readonly HashSet<Type> suppressed;
        private readonly Type[] added;

        public ScimSuppressedControllerTypeResolver(params Type[] suppressedControllerTypes)
            : this(suppressedControllerTypes, null)
        {
        }

        /// <param name="addedControllerTypes">
        /// Controllers to discover that assembly scanning does not find. A closed generic such
        /// as <c>ScimUsersApiController&lt;EduPassUser&gt;</c> is named
        /// <c>ScimUsersApiController`1</c>, which fails Web API's "ends with Controller" rule,
        /// so it has to be added here rather than found.
        /// </param>
        public ScimSuppressedControllerTypeResolver(
            Type[] suppressedControllerTypes,
            Type[] addedControllerTypes)
        {
            this.suppressed = new HashSet<Type>(suppressedControllerTypes ?? new Type[0]);
            this.added = addedControllerTypes ?? new Type[0];
        }

        public override ICollection<Type> GetControllerTypes(IAssembliesResolver assembliesResolver)
        {
            List<Type> result =
                base
                    .GetControllerTypes(assembliesResolver)
                    .Where(type => !this.suppressed.Contains(type))
                    .ToList();

            foreach (Type type in this.added)
            {
                if (!result.Contains(type))
                {
                    result.Add(type);
                }
            }

            return result;
        }
    }
}
