// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Text;
    using Microsoft.AspNetCore.Http;

    /// <summary>
    /// Projects an <see cref="HttpContext"/> onto the
    /// <see cref="System.Net.Http.HttpRequestMessage"/> that the shared SCIM layer is
    /// typed on (<c>IProvider</c>, <c>IProviderAdapter&lt;T&gt;</c>,
    /// <c>IExtension.Supports</c>, every <c>Compose*Request</c> method).
    /// </summary>
    /// <remarks>
    /// Replaces <c>HttpRequestMessageFeature</c> from the discontinued
    /// Microsoft.AspNetCore.Mvc.WebApiCompatShim package.
    ///
    /// The request body is deliberately NOT copied. Nothing in the request pipeline reads
    /// <c>HttpRequestMessage.Content</c>: MVC model binding already produces the typed body,
    /// and the shared code only reads the method, URI and headers
    /// (<c>new ResourceQuery(request.RequestUri)</c>, <c>TryGetRequestIdentifier</c>,
    /// <c>IExtension.Supports</c>, <c>GetBaseResourceIdentifier</c>). See
    /// docs/scim-conformance.md section 5 item 8 for the one accepted consequence, and for
    /// the two-line fix if it ever matters.
    /// </remarks>
    public static class HttpContextRequestConverter
    {
        public static HttpRequestMessage Convert(HttpContext context)
        {
            if (null == context)
            {
                throw new ArgumentNullException(nameof(context));
            }

            HttpRequest request = context.Request;

            HttpRequestMessage result =
                new HttpRequestMessage(
                    new HttpMethod(request.Method),
                    HttpContextRequestConverter.ComposeRequestUri(request));

            // Scheme and Host below are read after the pipeline has run, so if the host has
            // UseForwardedHeaders() configured, the values here are already the forwarded ones.
            foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> header in request.Headers)
            {
                // Content headers belong on HttpContent, which this message does not carry.
                // TryAddWithoutValidation keeps a malformed inbound header from throwing here
                // rather than at the endpoint.
                result.Headers.TryAddWithoutValidation(header.Key, (IEnumerable<string>)header.Value);
            }

            return result;
        }

        private static Uri ComposeRequestUri(HttpRequest request)
        {
            StringBuilder buffer = new StringBuilder();

            buffer.Append(request.Scheme);
            buffer.Append("://");
            buffer.Append(request.Host.ToUriComponent());
            buffer.Append(request.PathBase.ToUriComponent());
            buffer.Append(request.Path.ToUriComponent());
            buffer.Append(request.QueryString.ToUriComponent());

            return new Uri(buffer.ToString(), UriKind.Absolute);
        }
    }
}
