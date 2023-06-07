// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

using System.Web.Http;

namespace Microsoft.SCIM
{
    using System;

    [RoutePrefix(ServiceConstants.RouteGroups)]
    [Authorize]
    public sealed class GroupsController : ControllerTemplate<Core2Group>
    {
        public GroupsController(IProvider provider, IMonitor monitor)
            : base(provider, monitor)
        {
        }

        protected override IProviderAdapter<Core2Group> AdaptProvider(IProvider provider)
        {
            if (null == provider)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            IProviderAdapter<Core2Group> result =
                new Core2GroupProviderAdapter(provider);
            return result;
        }
    }
}
