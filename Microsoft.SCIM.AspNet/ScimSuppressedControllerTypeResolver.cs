// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Web.Http.Dispatcher;

    /// <summary>
    /// Removes named controllers from Web API's discovered set.
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

        public ScimSuppressedControllerTypeResolver(params Type[] suppressedControllerTypes)
        {
            if (null == suppressedControllerTypes)
            {
                throw new ArgumentNullException(nameof(suppressedControllerTypes));
            }

            this.suppressed = new HashSet<Type>(suppressedControllerTypes);
        }

        public override ICollection<Type> GetControllerTypes(IAssembliesResolver assembliesResolver)
        {
            return
                base
                    .GetControllerTypes(assembliesResolver)
                    .Where(type => !this.suppressed.Contains(type))
                    .ToList();
        }
    }
}
