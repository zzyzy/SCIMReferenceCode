// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// The SCIM service root.
    /// </summary>
    /// <remarks>
    /// This controller previously carried no [Route] and was reachable only through
    /// MapDefaultControllerRoute(), i.e. at /Root/{action}. Both hosting legs now use
    /// attribute routing exclusively, and SchemaConstants.PathInterface is already "scim",
    /// so the service root is /scim - consistent with scim/Users, scim/Groups and
    /// scim/Schemas. See MULTI-TARGET-PLAN.md D14a and docs/scim-conformance.md section 5
    /// item 3.
    /// </remarks>
    [Route(SchemaConstants.PathInterface)]
    [Authorize]
    [ApiController]
    public sealed class RootController : ScimResourceControllerBase<Resource>
    {
        public RootController(IProvider provider, IMonitor monitor)
            : base(ScimRequestHandlerFactory.CreateRootHandler(provider, monitor))
        {
        }
    }
}
