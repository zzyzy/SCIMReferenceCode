// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

using System.Web.Http;

namespace Microsoft.SCIM
{
    using System;

    [RoutePrefix(ServiceConstants.RouteUsers)]
    // [Authorize]
    public sealed class UsersController : ControllerTemplate<Core2EnterpriseUser>
    {
        public UsersController(IProvider provider, IMonitor monitor)
            : base(provider, monitor)
        {
        }

        protected override IProviderAdapter<Core2EnterpriseUser> AdaptProvider(IProvider provider)
        {
            if (null == provider)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            IProviderAdapter<Core2EnterpriseUser> result = new Core2EnterpriseUserProviderAdapter(provider);
            return result;
        }
    }
}
