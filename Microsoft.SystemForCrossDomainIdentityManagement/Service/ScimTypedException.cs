// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Net;
    using System.Runtime.Serialization;
    using System.Web.Http;

    /// <summary>
    /// An <see cref="HttpResponseException"/> that also carries the RFC 7644 section 3.12
    /// <c>scimType</c> keyword for the failure.
    /// </summary>
    /// <remarks>
    /// <see cref="HttpResponseException"/> can express the status but not which of the several
    /// possible mistakes the client made. A provider that knows - a bad PATCH path, a filter on
    /// an attribute it does not support - throws this instead so that the keyword reaches the
    /// response body rather than being defaulted.
    /// </remarks>
    [Serializable]
    public class ScimTypedException : HttpResponseException
    {
        public ScimTypedException(HttpStatusCode statusCode, string scimType)
            : base(statusCode)
        {
            this.ScimType = scimType;
        }

        public ScimTypedException(HttpStatusCode statusCode, string scimType, string detail)
            : base(statusCode)
        {
            this.ScimType = scimType;
            this.Detail = detail;
        }

        /// <summary>One of the <see cref="ScimTypes"/> keywords.</summary>
        public string ScimType
        {
            get;
        }

        /// <summary>
        /// The human-readable <c>detail</c> for the response body. Null means the status's
        /// reason phrase is used instead.
        /// </summary>
        public string Detail
        {
            get;
        }
    }
}
