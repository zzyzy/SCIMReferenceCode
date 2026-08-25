//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM.WebHostSample
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.Owin;

    /// <summary>
    /// Copies an API key presented as <c>Authorization: Bearer &lt;key&gt;</c> into the API key
    /// header, so that a client whose only credential field is a bearer token can use the API
    /// key mode.
    /// </summary>
    /// <remarks>
    /// Sample-local on purpose. The library reads a key from a header of its own and offers no
    /// Authorization form, because a key sent where a token is expected cannot be told apart
    /// from a token. This sample accepts it anyway so that the Microsoft SCIM Validator, which
    /// only sends a bearer token, can exercise the API key mode.
    /// </remarks>
    public class ApiKeyBearerMiddleware : OwinMiddleware
    {
        private const string AuthorizationHeaderName = "Authorization";
        private const string BearerPrefix = "Bearer ";

        private readonly string headerName;

        public ApiKeyBearerMiddleware(OwinMiddleware next, string headerName)
            : base(next)
        {
            if (string.IsNullOrWhiteSpace(headerName))
            {
                throw new ArgumentException("A header name is required.", nameof(headerName));
            }

            this.headerName = headerName;
        }

        public override Task Invoke(IOwinContext context)
        {
            if (null == context)
            {
                throw new ArgumentNullException(nameof(context));
            }

            string presented = context.Request.Headers.Get(ApiKeyBearerMiddleware.AuthorizationHeaderName);

            // An already-present API key header wins, so that a caller sending both is not
            // surprised by which one was used.
            if (null == context.Request.Headers.Get(this.headerName)
                && !string.IsNullOrWhiteSpace(presented)
                && presented.StartsWith(ApiKeyBearerMiddleware.BearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string value = presented.Substring(ApiKeyBearerMiddleware.BearerPrefix.Length).Trim();

                if (!string.IsNullOrEmpty(value))
                {
                    context.Request.Headers.Set(this.headerName, value);
                }
            }

            return this.Next.Invoke(context);
        }
    }
}
