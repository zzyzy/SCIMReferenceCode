// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.Owin;

    /// <summary>
    /// Reports each request, its outcome status and any escaping exception to an
    /// <see cref="IMonitor"/>.
    /// </summary>
    /// <remarks>
    /// Revived from Service/MonitoringMiddleware.cs in Microsoft.SCIM, which had been fully
    /// commented out; it belongs here because OwinMiddleware is a net48-only concept. The one
    /// change from the commented-out original is the request identifier: it called a
    /// <c>context.Request.Identify()</c> extension that does not exist anywhere in the
    /// repository, so this generates a GUID - which is exactly what
    /// <c>RequestExtensions.TryGetRequestIdentifier</c> in Microsoft.SCIM does.
    /// </remarks>
    public sealed class MonitoringMiddleware : OwinMiddleware
    {
        public MonitoringMiddleware(OwinMiddleware next, IMonitor monitor)
            : base(next)
        {
            this.Monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        }

        private IMonitor Monitor
        {
            get;
            set;
        }

        private static string ComposeRequest(IOwinContext context)
        {
            if (null == context)
            {
                throw new ArgumentNullException(nameof(context));
            }

            string method = null;
            Uri resource = null;
            string headers = null;

            if (context.Request != null)
            {
                method = context.Request.Method;
                resource = context.Request.Uri;

                if (context.Request.Headers != null)
                {
                    headers =
                        context
                        .Request
                        .Headers
                        .ToDictionary(
                            (KeyValuePair<string, string[]> item) =>
                                item.Key,
                            (KeyValuePair<string, string[]> item) =>
                                string.Join(
                                        SystemForCrossDomainIdentityManagementServiceResources.SeparatorHeaderValues,
                                        item.Value))
                        .Select(
                            (KeyValuePair<string, string> item) =>
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    SystemForCrossDomainIdentityManagementServiceResources.HeaderTemplate,
                                    item.Key,
                                    item.Value))
                        .Aggregate(
                            new StringBuilder(),
                            (StringBuilder aggregate, string item) =>
                                aggregate.AppendLine(item))
                        .ToString();
                }
            }

            string result =
                string.Format(
                    CultureInfo.InvariantCulture,
                    SystemForCrossDomainIdentityManagementServiceResources.MessageTemplate,
                    method,
                    resource,
                    headers);

            return result;
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

            string requestIdentifier = Guid.NewGuid().ToString();

            string message = MonitoringMiddleware.ComposeRequest(context);

            IInformationNotification receptionNotification =
                InformationNotificationFactory.Instance.FormatNotification(
                    SystemForCrossDomainIdentityManagementServiceResources.InformationRequestReceivedTemplate,
                    requestIdentifier,
                    ServiceNotificationIdentifiers.MonitoringMiddlewareReception,
                    message);
            this.Monitor.Inform(receptionNotification);

            try
            {
                if (this.Next != null)
                {
                    await this.Next.Invoke(context).ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                IExceptionNotification exceptionNotification =
                    ExceptionNotificationFactory.Instance.CreateNotification(
                        exception,
                        requestIdentifier,
                        ServiceNotificationIdentifiers.MonitoringMiddlewareInvocationException);
                this.Monitor.Report(exceptionNotification);

                throw;
            }

            string responseStatusCode;
            if (context.Response != null)
            {
                responseStatusCode = context.Response.StatusCode.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                responseStatusCode = null;
            }

            IInformationNotification processedNotification =
                InformationNotificationFactory.Instance.FormatNotification(
                    SystemForCrossDomainIdentityManagementServiceResources.InformationRequestProcessedTemplate,
                    requestIdentifier,
                    ServiceNotificationIdentifiers.MonitoringMiddlewareRequestProcessed,
                    message,
                    responseStatusCode);
            this.Monitor.Inform(processedNotification);
        }
    }
}
