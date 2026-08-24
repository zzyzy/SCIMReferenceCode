// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// The single logging shape the SCIM layer uses for a failed operation.
    /// </summary>
    /// <remarks>
    /// Every catch block in <see cref="ScimRequestHandler{T}"/> and
    /// <see cref="ScimDiscoveryRequestHandler"/> reports the same three things - the event, the
    /// exception and the request's correlation identifier - so they share one call rather than
    /// repeating a null check and a message template 45 times. A null logger is tolerated
    /// because the handlers accept one: hosts that do not want SCIM logging pass nothing.
    /// </remarks>
    public static class ScimLoggerExtensions
    {
        private const string MessageTemplate = "SCIM operation failed. Correlation: {CorrelationIdentifier}";

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

            logger.LogError(eventId, exception, ScimLoggerExtensions.MessageTemplate, correlationIdentifier);
        }
    }
}
