// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web.Http;

    /// <summary>
    /// Drops Web API's own error document from a 401, leaving the bodiless challenge the
    /// ASP.NET Core leg sends.
    /// </summary>
    /// <remarks>
    /// <c>AuthorizeAttribute.HandleUnauthorizedRequest</c> answers with
    /// <c>{"Message":"Authorization has been denied for this request."}</c>. ASP.NET Core's
    /// authentication middleware writes no body at all, so the two legs disagreed on the one
    /// response every unauthenticated client sees - and net48's body is an ASP.NET problem
    /// document, which is not a SCIM error and tells a SCIM client nothing it can parse. See
    /// docs/scim-conformance.md section 4, requirement X2, and the XML formatter above it for
    /// the same kind of parity break.
    ///
    /// Bodiless rather than a <see cref="Core2Error"/>: RFC 7644 section 3.12 prefixes its
    /// body table with "if present", so a 401 carrying no body is conformant, and matching the
    /// other leg is worth more here than adding a body only one leg would send.
    ///
    /// Only Web API's own <see cref="HttpError"/> is removed. A host that answers 401 with a
    /// body of its own - a SCIM error, or anything else it has chosen - keeps it.
    /// </remarks>
    public sealed class ScimUnauthorizedResponseHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response =
                await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (HttpStatusCode.Unauthorized == response?.StatusCode
                && response.Content is ObjectContent content
                && typeof(HttpError) == content.ObjectType)
            {
                response.Content.Dispose();
                response.Content = null;
            }

            return response;
        }
    }
}
