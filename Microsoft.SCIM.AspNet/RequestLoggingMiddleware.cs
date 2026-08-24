// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Microsoft.Owin;

    /// <summary>
    /// Logs each request, its outcome status and any escaping exception.
    /// </summary>
    /// <remarks>
    /// Revived from Service/MonitoringMiddleware.cs in Microsoft.SCIM, which had been fully
    /// commented out; it belongs here because OwinMiddleware is a net48-only concept. The
    /// ASP.NET Core leg needs no equivalent - <c>UseHttpLogging</c> covers it.
    ///
    /// Header values for <c>Authorization</c>, <c>Proxy-Authorization</c> and <c>Cookie</c> are
    /// replaced rather than logged. The original wrote the whole header dictionary out, which
    /// put the caller's bearer token in the log on every request.
    /// </remarks>
    public sealed class RequestLoggingMiddleware : OwinMiddleware
    {
        private const string Redacted = "<redacted>";

        private static readonly HashSet<string> RedactedHeaders =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Authorization",
                "Proxy-Authorization",
                "Cookie",
                "Set-Cookie",
            };

        public RequestLoggingMiddleware(OwinMiddleware next, ILogger<RequestLoggingMiddleware> logger)
            : base(next)
        {
            this.Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private ILogger Logger
        {
            get;
        }

        private static string ComposeHeaders(IOwinRequest request)
        {
            if (null == request?.Headers)
            {
                return null;
            }

            return
                string.Join(
                    "; ",
                    request
                        .Headers
                        .Select(
                            (KeyValuePair<string, string[]> item) =>
                                string.Concat(
                                    item.Key,
                                    ": ",
                                    RequestLoggingMiddleware.RedactedHeaders.Contains(item.Key)
                                        ? RequestLoggingMiddleware.Redacted
                                        : string.Join(", ", item.Value ?? Array.Empty<string>()))));
        }

        public override async Task Invoke(IOwinContext context)
        {
            if (null == context)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (null == context.Request)
            {
                throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidContext);
            }

            string correlationIdentifier = Guid.NewGuid().ToString();
            string method = context.Request.Method;
            Uri resource = context.Request.Uri;

            this.Logger.LogInformation(
                ScimEventIds.RequestReceived,
                "SCIM request received. {Method} {Resource} Correlation: {CorrelationIdentifier} Headers: {Headers}",
                method,
                resource,
                correlationIdentifier,
                RequestLoggingMiddleware.ComposeHeaders(context.Request));

            try
            {
                if (this.Next != null)
                {
                    await this.Next.Invoke(context).ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                this.Logger.LogError(
                    ScimEventIds.RequestPipelineException,
                    exception,
                    "SCIM request failed in the pipeline. {Method} {Resource} Correlation: {CorrelationIdentifier}",
                    method,
                    resource,
                    correlationIdentifier);

                throw;
            }

            this.Logger.LogInformation(
                ScimEventIds.RequestProcessed,
                "SCIM request processed. {Method} {Resource} Status: {StatusCode} Correlation: {CorrelationIdentifier}",
                method,
                resource,
                context.Response?.StatusCode,
                correlationIdentifier);
        }
    }
}
