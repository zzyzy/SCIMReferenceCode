// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading.Tasks;
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
    /// The request body is attached but not read. Nothing in the request pipeline reads
    /// <c>HttpRequestMessage.Content</c> - MVC model binding already produces the typed body,
    /// and the shared code only reads the method, URI and headers - but the failure logging in
    /// <see cref="ScimLoggerExtensions"/> does, and a status and a stack trace rarely say what
    /// a client actually sent. The content reads the buffered request stream on demand, so a
    /// request that succeeds costs nothing beyond the buffering itself.
    ///
    /// That buffering is <see cref="ScimRequestBufferingFilter"/>'s job: model binding consumes
    /// the stream before an action runs, so without it there is nothing left here to rewind.
    /// </remarks>
    public static class HttpContextRequestConverter
    {
        private const string HeaderKeyContentType = "Content-Type";

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

            HttpContextRequestConverter.AttachBody(request, result);

            return result;
        }

        /// <summary>
        /// Attaches the request body, to be read only if something asks for it.
        /// </summary>
        /// <remarks>
        /// Always attached: a failed operation logs the request body, and attaching content
        /// that is only read on demand costs nothing until something asks.
        ///
        /// Only when the stream can be rewound, which is what
        /// <see cref="ScimRequestBufferingFilter"/> arranges. A request whose body has already
        /// been consumed and cannot be sought is left without content rather than attached and
        /// read as empty, so that "no body" and "body unavailable" do not look the same in a
        /// log.
        /// </remarks>
        private static void AttachBody(HttpRequest request, HttpRequestMessage message)
        {
            if (null == request.Body || !request.Body.CanSeek || !request.Body.CanRead)
            {
                return;
            }

            message.Content = new RewindableStreamContent(request.Body);

            if (!string.IsNullOrWhiteSpace(request.ContentType))
            {
                message.Content.Headers.TryAddWithoutValidation(HeaderKeyContentType, request.ContentType);
            }
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

    /// <summary>
    /// Content over a seekable stream that rewinds before every read.
    /// </summary>
    /// <remarks>
    /// <see cref="StreamContent"/> reads from wherever the stream happens to be, and by the
    /// time an action runs, model binding has left the request body at its end - so a plain
    /// StreamContent over it reads as empty. This seeks to the start each time instead, and
    /// leaves the stream open: it belongs to the request, not to this content.
    /// </remarks>
    internal sealed class RewindableStreamContent : HttpContent
    {
        private readonly Stream stream;

        public RewindableStreamContent(Stream stream)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
        }

        protected override async Task SerializeToStreamAsync(Stream target, TransportContext context)
        {
            if (this.stream.CanSeek)
            {
                this.stream.Position = 0;
            }

            await this.stream.CopyToAsync(target).ConfigureAwait(false);

            if (this.stream.CanSeek)
            {
                // Rewound again rather than left at the end, so that a second reader - another
                // log line, a filter - still sees the body whole.
                this.stream.Position = 0;
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            if (this.stream.CanSeek)
            {
                length = this.stream.Length;
                return true;
            }

            length = 0;
            return false;
        }
    }
}
