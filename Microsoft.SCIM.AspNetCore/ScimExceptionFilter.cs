// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System.Web.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Filters;

    /// <summary>
    /// Converts an unhandled <see cref="HttpResponseException"/> into a response carrying
    /// that exception's status code and a <see cref="Core2Error"/> body.
    /// </summary>
    /// <remarks>
    /// This filter is NOT optional. Microsoft.AspNetCore.Mvc.WebApiCompatShim used to install
    /// an equivalent one; without it, every status the SCIM layer signals by throwing -
    /// notably 404 from the providers and 501 from <c>RootProviderAdapter</c> and the
    /// not-implemented paths - would surface as a 500. The net48 leg installs the matching
    /// <c>ScimExceptionFilterAttribute</c>. Both must map identically; see
    /// docs/scim-conformance.md section 4, requirement X1.
    /// </remarks>
    public sealed class ScimExceptionFilter : IExceptionFilter
    {
        public void OnException(ExceptionContext context)
        {
            if (null == context)
            {
                return;
            }

            if (!(context.Exception is HttpResponseException responseException))
            {
                return;
            }

            ScimResult scimResult = ScimResult.FromException(responseException);

            ObjectResult result =
                new ObjectResult(scimResult.Payload)
                {
                    StatusCode = (int)scimResult.StatusCode
                };
            result.ContentTypes.Add(ProtocolConstants.ContentType);

            context.Result = result;
            context.ExceptionHandled = true;
        }
    }
}
