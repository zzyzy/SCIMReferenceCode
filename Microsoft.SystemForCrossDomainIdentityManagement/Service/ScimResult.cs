// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Net;

    /// <summary>
    /// A hosting-neutral description of a SCIM response.
    /// </summary>
    /// <remarks>
    /// The request handling in <see cref="ScimRequestHandler{T}"/> and
    /// <see cref="ScimDiscoveryRequestHandler"/> is shared by both hosting legs
    /// (ASP.NET Web API on net48, ASP.NET Core MVC on net10.0). Neither of those
    /// frameworks' result types can be named from this assembly, so the handlers
    /// return this and each host translates it into its own action result. Keeping
    /// the translation to a few dozen lines per host is what keeps the two legs
    /// from drifting apart - see MULTI-TARGET-PLAN.md D12.
    /// </remarks>
    public sealed class ScimResult
    {
        private ScimResult(HttpStatusCode statusCode, object payload, Uri location)
        {
            this.StatusCode = statusCode;
            this.Payload = payload;
            this.Location = location;
        }

        /// <summary>The status code the response must carry.</summary>
        public HttpStatusCode StatusCode
        {
            get;
        }

        /// <summary>
        /// The object to serialize as the response body: a <see cref="Resource"/>,
        /// a <see cref="QueryResponseBase"/>, a <see cref="ServiceConfigurationBase"/>,
        /// a <see cref="BulkResponse2"/>, a <see cref="Core2Error"/>, or null for a
        /// body-less response.
        /// </summary>
        public object Payload
        {
            get;
        }

        /// <summary>
        /// The absolute URI for the <c>Location</c> header. Set on create only
        /// (RFC 7644 section 3.3); null otherwise.
        /// </summary>
        public Uri Location
        {
            get;
        }

        /// <summary>The content type to use when <see cref="Payload"/> is not null.</summary>
        public string ContentType
        {
            get
            {
                return null == this.Payload ? null : ProtocolConstants.ContentType;
            }
        }

        /// <summary>200 with a body.</summary>
        public static ScimResult Ok(object payload)
        {
            return new ScimResult(HttpStatusCode.OK, payload, null);
        }

        /// <summary>
        /// 201 with the created resource as the body and an explicit <c>Location</c>.
        /// The location is computed once, by the caller, from
        /// <see cref="RequestExtensions.GetBaseResourceIdentifier"/> and
        /// <see cref="ProtocolExtensions.GetResourceIdentifier"/> - see
        /// MULTI-TARGET-PLAN.md D15 for why it is no longer also derived from routing.
        /// </summary>
        public static ScimResult Created(Resource resource, Uri location)
        {
            return new ScimResult(HttpStatusCode.Created, resource, location);
        }

        /// <summary>204, no body.</summary>
        public static ScimResult NoContent()
        {
            return new ScimResult(HttpStatusCode.NoContent, null, null);
        }

        /// <summary>
        /// A bare status code, for the paths that previously returned <c>BadRequest()</c>,
        /// <c>NotFound()</c> or <c>Conflict()</c> from the controller base class. Failure
        /// statuses carry a <see cref="Core2Error"/> body describing the status.
        /// </summary>
        /// <remarks>
        /// The body is not cosmetic. <c>ControllerBase.BadRequest()</c> and friends return an
        /// <c>IStatusCodeActionResult</c>, which [ApiController] then rewrites into an RFC 9110
        /// <c>ProblemDetails</c> body - a shape ASP.NET Web API has no equivalent for, so the
        /// two hosting legs would answer the same request with different bodies. Attaching a
        /// <see cref="Core2Error"/> here, and suppressing the ProblemDetails rewrite on the
        /// ASP.NET Core leg, makes both legs emit the SCIM error shape that RFC 7644
        /// section 3.12 asks for anyway. See docs/scim-conformance.md requirement X10.
        /// </remarks>
        public static ScimResult Status(HttpStatusCode statusCode)
        {
            if ((int)statusCode < 400)
            {
                return new ScimResult(statusCode, null, null);
            }

            return ScimResult.Error(statusCode, ScimResult.DescribeStatus(statusCode));
        }

        private static string DescribeStatus(HttpStatusCode statusCode)
        {
            using (System.Net.Http.HttpResponseMessage message =
                new System.Net.Http.HttpResponseMessage(statusCode))
            {
                string reasonPhrase = message.ReasonPhrase;
                return string.IsNullOrWhiteSpace(reasonPhrase) ? statusCode.ToString() : reasonPhrase;
            }
        }

        /// <summary>A status code with a <see cref="Core2Error"/> body (RFC 7644 section 3.12).</summary>
        public static ScimResult Error(HttpStatusCode statusCode, string message)
        {
            return new ScimResult(statusCode, new Core2Error(message, (int)statusCode), null);
        }

        /// <summary>
        /// The response for an <see cref="System.Web.Http.HttpResponseException"/> that reached
        /// the hosting layer unhandled.
        /// </summary>
        /// <remarks>
        /// Both hosting legs go through this so that a thrown status maps to the same body on
        /// each. It matters more than it looks: on ASP.NET Web API, <c>HttpResponseException</c>
        /// is special-cased and bypasses exception filters, returning the exception's own
        /// body-less response - so the net48 leg has to catch it in the controller rather than
        /// leaving it to a filter. See docs/scim-conformance.md section 4, requirement X1.
        /// </remarks>
        public static ScimResult FromException(System.Web.Http.HttpResponseException responseException)
        {
            if (null == responseException)
            {
                throw new ArgumentNullException(nameof(responseException));
            }

            HttpStatusCode statusCode =
                responseException.Response?.StatusCode ?? HttpStatusCode.InternalServerError;

            string detail = responseException.Response?.ReasonPhrase;
            if (string.IsNullOrWhiteSpace(detail))
            {
                detail = statusCode.ToString();
            }

            return ScimResult.Error(statusCode, detail);
        }
    }
}
