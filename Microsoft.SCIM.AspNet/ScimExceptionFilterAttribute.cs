// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System.Net.Http;
    using System.Net.Http.Formatting;
    using System.Web.Http;
    using System.Web.Http.Filters;

    /// <summary>
    /// Converts an unhandled <see cref="HttpResponseException"/> into a response carrying that
    /// exception's status code and a <see cref="Core2Error"/> body, matching
    /// <c>ScimExceptionFilter</c> on the ASP.NET Core leg.
    /// </summary>
    /// <remarks>
    /// ASP.NET Web API special-cases <c>HttpResponseException</c> and returns the exception's
    /// own body-less response without consulting exception filters, so the primary mapping for
    /// throws out of an action is the catch in <see cref="ScimApiControllerBase"/>. This filter
    /// covers what that catch cannot reach - a throw from a message handler, a formatter, or an
    /// authorization filter - and guarantees that nothing produces a bare 500 by accident. See
    /// docs/scim-conformance.md section 4, requirement X1.
    /// </remarks>
    public sealed class ScimExceptionFilterAttribute : ExceptionFilterAttribute
    {
        public override void OnException(HttpActionExecutedContext actionExecutedContext)
        {
            if (null == actionExecutedContext)
            {
                return;
            }

            if (!(actionExecutedContext.Exception is HttpResponseException responseException))
            {
                return;
            }

            ScimResult scimResult = ScimResult.FromException(responseException);

            HttpRequestMessage request = actionExecutedContext.Request;
            HttpResponseMessage response = new HttpResponseMessage(scimResult.StatusCode);
            response.RequestMessage = request;

            MediaTypeFormatter formatter =
                request?.GetConfiguration()?.Formatters.JsonFormatter ?? new JsonMediaTypeFormatter();
            response.Content =
                new ObjectContent(
                    scimResult.Payload.GetType(),
                    scimResult.Payload,
                    formatter,
                    ProtocolConstants.ContentType);

            actionExecutedContext.Response = response;
        }
    }
}
