// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// The single logging shape the SCIM layer uses for a failed operation.
    /// </summary>
    /// <remarks>
    /// Every catch block in <see cref="ScimRequestHandler{T}"/> and
    /// <see cref="ScimDiscoveryRequestHandler"/> reports the same things - the event, the
    /// exception, the request's correlation identifier and the request itself - so they share
    /// one call rather than repeating a null check and a message template 45 times. A null
    /// logger is tolerated because the handlers accept one: hosts that do not want SCIM
    /// logging pass nothing.
    ///
    /// The request is written because a status and a stack trace rarely say what a client
    /// actually sent. Header values named in <see cref="ScimLogging.RedactedHeaders"/> are
    /// replaced; the body is written verbatim up to
    /// <see cref="ScimLogging.MaximumBodyLength"/>, whatever the per-request switches say.
    /// </remarks>
    public static class ScimLoggerExtensions
    {
        private const string MessageTemplate =
            "SCIM operation failed. Correlation: {CorrelationIdentifier} " +
            "{Method} {Resource} Headers: {Headers} Body: {Body}";

        private const string MessageTemplateWithoutRequest =
            "SCIM operation failed. Correlation: {CorrelationIdentifier}";

        private const string SeparatorHeaders = "; ";
        private const string SeparatorHeaderValues = ", ";
        private const string TemplateHeader = "{0}: {1}";
        private const string TemplateTruncated = "{0}<truncated, Content-Length {1}>";

        /// <summary>How much of the body is read at a time.</summary>
        private const int ChunkLength = 8 * 1024;

        public static void LogScimFailure(
            this ILogger logger,
            EventId eventId,
            Exception exception,
            string correlationIdentifier)
        {
            if (null == logger)
            {
                return;
            }

            logger.LogError(
                eventId,
                exception,
                ScimLoggerExtensions.MessageTemplateWithoutRequest,
                correlationIdentifier);
        }

        /// <summary>
        /// Reports a failed operation together with the request that caused it.
        /// </summary>
        public static void LogScimFailure(
            this ILogger logger,
            EventId eventId,
            Exception exception,
            string correlationIdentifier,
            HttpRequestMessage request)
        {
            if (null == logger)
            {
                return;
            }

            if (null == request)
            {
                logger.LogScimFailure(eventId, exception, correlationIdentifier);
                return;
            }

            logger.LogError(
                eventId,
                exception,
                ScimLoggerExtensions.MessageTemplate,
                correlationIdentifier,
                request.Method,
                request.RequestUri,
                ScimLoggerExtensions.ComposeHeaders(request),
                ScimLoggerExtensions.ComposeBody(request));
        }

        /// <summary>
        /// The request's headers as one line, with credentials replaced.
        /// </summary>
        /// <remarks>
        /// Content headers live on <see cref="HttpRequestMessage.Content"/> rather than on the
        /// message, so both collections are read - otherwise <c>Content-Type</c>, which is
        /// often the thing that explains a deserialization failure, would be missing.
        /// </remarks>
        public static string ComposeHeaders(HttpRequestMessage request)
        {
            if (null == request)
            {
                return null;
            }

            IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers = request.Headers;

            if (null != request.Content?.Headers)
            {
                headers = headers.Concat(request.Content.Headers);
            }

            return
                string.Join(
                    ScimLoggerExtensions.SeparatorHeaders,
                    headers.Select(ScimLoggerExtensions.ComposeHeader));
        }

        private static string ComposeHeader(KeyValuePair<string, IEnumerable<string>> header)
        {
            string value =
                ScimLogging.IsRedacted(header.Key)
                    ? ScimLogging.Redacted
                    : string.Join(
                        ScimLoggerExtensions.SeparatorHeaderValues,
                        header.Value ?? Enumerable.Empty<string>());

            return
                string.Format(
                    CultureInfo.InvariantCulture,
                    ScimLoggerExtensions.TemplateHeader,
                    header.Key,
                    value);
        }

        /// <summary>
        /// The request's body, verbatim, bounded by
        /// <see cref="ScimLogging.MaximumBodyLength"/>.
        /// </summary>
        /// <remarks>
        /// Not switchable. Per-request logging is the host's, and can be turned down or off
        /// there; this is the entry whose whole purpose is to say what the client sent, on the
        /// one path a host cannot observe at all. Quietening the routine logging is not a
        /// request to be told less about what broke.
        ///
        /// Read synchronously and inside a catch: this runs on a path that is already failing,
        /// and a logging attempt must not replace the failure being reported with one of its
        /// own. A body that cannot be read is reported as absent rather than throwing.
        /// </remarks>
        public static string ComposeBody(HttpRequestMessage request)
        {
            if (null == request?.Content)
            {
                return ScimLogging.NoBody;
            }

            int maximum = ScimLogging.MaximumBodyLength;

            try
            {
                using (Stream stream = request.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                {
                    if (null == stream)
                    {
                        return ScimLogging.NoBody;
                    }

                    if (stream.CanSeek)
                    {
                        stream.Position = 0;
                    }

                    // Read in chunks, one character past the ceiling and no further: a body
                    // far larger than the ceiling would otherwise be materialised in full only
                    // to be thrown away, on a path that is already failing. The extra character
                    // is what distinguishes "exactly at the ceiling" from "longer than it".
                    using (StreamReader reader =
                        new StreamReader(stream, Encoding.UTF8, true, ScimLoggerExtensions.ChunkLength, leaveOpen: true))
                    {
                        StringBuilder body = new StringBuilder();
                        char[] chunk = new char[ScimLoggerExtensions.ChunkLength];

                        while (body.Length <= maximum)
                        {
                            int taken = reader.Read(chunk, 0, chunk.Length);
                            if (taken <= 0)
                            {
                                break;
                            }

                            body.Append(chunk, 0, taken);
                        }

                        if (0 == body.Length)
                        {
                            return ScimLogging.NoBody;
                        }

                        if (body.Length <= maximum)
                        {
                            return body.ToString();
                        }

                        return
                            string.Format(
                                CultureInfo.InvariantCulture,
                                ScimLoggerExtensions.TemplateTruncated,
                                body.ToString(0, maximum),
                                request.Content.Headers?.ContentLength);
                    }
                }
            }
            catch (Exception)
            {
                return ScimLogging.NoBody;
            }
        }
    }
}
