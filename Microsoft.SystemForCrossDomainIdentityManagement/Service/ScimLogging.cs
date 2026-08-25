//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// What the SCIM layer writes about a failed operation.
    /// </summary>
    /// <remarks>
    /// This is deliberately narrow. Logging every request and response is the host's job -
    /// ASP.NET Core has <c>UseHttpLogging</c>, and net48 has IIS logging, Application Insights
    /// or any OWIN middleware - and none of it is SCIM-specific: method, URI, headers, body,
    /// status. A library that logged it too would duplicate the framework, log a host's
    /// non-SCIM endpoints, and decide a host's retention and privacy policy for it.
    ///
    /// What the host cannot do is log a SCIM failure. The handlers convert a provider's
    /// exception into a <see cref="ScimResult"/> rather than letting it escape, so by the time
    /// any middleware sees the response there is nothing left to catch. Those entries carry the
    /// correlation identifier, the <see cref="ScimEventIds"/> event, the exception, and the
    /// request that caused it - method, URI, headers and body. See
    /// <see cref="ScimLoggerExtensions"/>.
    ///
    /// Process-wide rather than injected, for the same reason as <see cref="ScimPath"/>: the
    /// request handlers reach it with no container in scope.
    ///
    /// <code>
    /// ScimLogging.MaximumBodyLength = 64 * 1024;    // a tighter ceiling than the 10 MB default
    /// </code>
    ///
    /// The logger itself is the host's: the controllers resolve <c>ILogger&lt;T&gt;</c> from
    /// the container and hand it to the request handlers, so whatever provider a host has
    /// registered receives these entries.
    /// </remarks>
    public static class ScimLogging
    {
        /// <summary>
        /// The default ceiling on a logged body, in characters.
        /// </summary>
        /// <remarks>
        /// Large enough that no realistic SCIM resource, PatchOp or Bulk request is cut off,
        /// and bounded so that a hostile or runaway body cannot write without limit.
        /// </remarks>
        public const int DefaultMaximumBodyLength = 10 * 1024 * 1024;

        /// <summary>The value written in place of a redacted header.</summary>
        public const string Redacted = "<redacted>";

        /// <summary>Written where a message carried no body, or none could be read.</summary>
        public const string NoBody = "<none>";

        /// <summary>
        /// Header names whose values are replaced rather than written.
        /// </summary>
        /// <remarks>
        /// A body is logged verbatim - it is what the caller sent, and reproducing a failure
        /// means seeing it - but a credential in a header is not part of the request being
        /// diagnosed. An earlier version of the net48 request logging wrote the whole header
        /// dictionary, which put the caller's bearer token in the log on every request.
        /// </remarks>
        public static readonly IReadOnlyCollection<string> RedactedHeaders =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Authorization",
                "Proxy-Authorization",
                "Cookie",
                "Set-Cookie",
            };

        private static readonly object SyncRoot = new object();

        private static int maximumBodyLength = ScimLogging.DefaultMaximumBodyLength;

        /// <summary>
        /// The ceiling on a logged body, in characters. A body longer than this is written up
        /// to the ceiling and marked as truncated, so that a cut-off body is never mistaken
        /// for the whole one.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The value is not greater than zero. There is no ceiling of nothing: a failed
        /// operation logs the body, and a host that does not want that logged does not want
        /// this library.
        /// </exception>
        public static int MaximumBodyLength
        {
            get
            {
                lock (ScimLogging.SyncRoot)
                {
                    return ScimLogging.maximumBodyLength;
                }
            }

            set
            {
                if (value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                lock (ScimLogging.SyncRoot)
                {
                    ScimLogging.maximumBodyLength = value;
                }
            }
        }

        /// <summary>Whether <paramref name="headerName"/>'s value is written or replaced.</summary>
        public static bool IsRedacted(string headerName)
        {
            return null != headerName
                && ((HashSet<string>)ScimLogging.RedactedHeaders).Contains(headerName);
        }
    }
}
