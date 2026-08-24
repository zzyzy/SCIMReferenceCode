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
    /// from drifting apart.
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
        /// docs/scim-conformance.md section 5 item 2 for why it is no longer also derived
        /// from routing.
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
        public static ScimResult Status(HttpStatusCode statusCode, string scimType = null)
        {
            if ((int)statusCode < 400)
            {
                return new ScimResult(statusCode, null, null);
            }

            return ScimResult.Error(statusCode, ScimResult.DescribeStatus(statusCode), scimType);
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
        /// <param name="scimType">
        /// One of the <see cref="ScimTypes"/> keywords. RFC 7644 section 3.12 requires one on a
        /// 400 and permits <c>uniqueness</c> on a 409, so when the caller does not supply one
        /// and the status implies it unambiguously, it is filled in - see
        /// <see cref="DefaultScimType"/>.
        /// </param>
        public static ScimResult Error(HttpStatusCode statusCode, string message, string scimType = null)
        {
            return
                new ScimResult(
                    statusCode,
                    new Core2Error(
                        message,
                        (int)statusCode,
                        scimType ?? ScimResult.DefaultScimType(statusCode)),
                    null);
        }

        /// <summary>
        /// The <c>scimType</c> to use when a failure path did not name one.
        /// </summary>
        /// <remarks>
        /// RFC 7644 section 3.12 makes <c>scimType</c> mandatory on a 400, so every 400 gets
        /// one whether or not the failing path named it. <c>invalidValue</c> is the fallback
        /// because its definition - "a required value was missing, or the value specified was
        /// not compatible with the operation, attribute type, or resource schema" - is the
        /// broadest of the keywords, and a provider throwing a bare
        /// <c>HttpResponseException(BadRequest)</c> has told us nothing more specific. Paths
        /// that do know better pass their own: <c>invalidFilter</c>, <c>invalidSyntax</c>,
        /// <c>invalidPath</c>.
        ///
        /// 409 is <c>uniqueness</c> because a duplicate <c>userName</c> or <c>displayName</c>
        /// is the only conflict the SCIM resource endpoints raise.
        /// </remarks>
        private static string DefaultScimType(HttpStatusCode statusCode)
        {
            switch (statusCode)
            {
                case HttpStatusCode.BadRequest:
                    return ScimTypes.InvalidValue;
                case HttpStatusCode.Conflict:
                    return ScimTypes.Uniqueness;
                default:
                    return null;
            }
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

            ScimTypedException typedException = responseException as ScimTypedException;

            string detail = typedException?.Detail;
            if (string.IsNullOrWhiteSpace(detail))
            {
                detail = responseException.Response?.ReasonPhrase;
            }

            if (string.IsNullOrWhiteSpace(detail))
            {
                detail = statusCode.ToString();
            }

            return ScimResult.Error(statusCode, detail, typedException?.ScimType);
        }
    }
}
