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
        /// The header names redacted unless a host adds to them.
        /// </summary>
        private static readonly string[] DefaultRedactedHeaders =
            new[]
            {
                "Authorization",
                "Proxy-Authorization",
                "Cookie",
                "Set-Cookie",
            };

        /// <remarks>
        /// Replaced wholesale rather than mutated, so that <see cref="IsRedacted"/> can read it
        /// without a lock while <see cref="AddRedactedHeader"/> is adding to it. Reading a
        /// <see cref="HashSet{T}"/> during a concurrent write is undefined, and this is read on
        /// every logged header.
        /// </remarks>
        private static volatile HashSet<string> redactedHeaders =
            new HashSet<string>(ScimLogging.DefaultRedactedHeaders, StringComparer.OrdinalIgnoreCase);

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

        /// <summary>
        /// Header names whose values are replaced rather than written.
        /// </summary>
        /// <remarks>
        /// A body is logged verbatim - it is what the caller sent, and reproducing a failure
        /// means seeing it - but a credential in a header is not part of the request being
        /// diagnosed. An earlier version of the net48 request logging wrote the whole header
        /// dictionary, which put the caller's bearer token in the log on every request.
        ///
        /// Extend it with <see cref="AddRedactedHeader"/> rather than assuming the defaults
        /// cover a deployment: they name the standard credential headers, and a relying party
        /// that authenticates through a header of its own is not covered by any of them.
        /// </remarks>
        public static IReadOnlyCollection<string> RedactedHeaders
        {
            get
            {
                return ScimLogging.redactedHeaders;
            }
        }

        /// <summary>
        /// Also redacts <paramref name="headerName"/>, for a host whose credential does not
        /// travel in one of the standard headers.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The defaults cover <c>Authorization</c> and friends, which is every credential the
        /// samples use and none of the credentials a real relying party may have. An endpoint
        /// authenticating with, say, <c>X-Api-Key</c> had no way to keep that key out of a
        /// failure log: the header was written verbatim, and a failure log is exactly the file
        /// most likely to be attached to a support ticket. Adding a header here is the fix, and
        /// it belongs at startup beside the authentication configuration.
        /// </para>
        /// <para>
        /// Idempotent, and matched case-insensitively as header names are.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="headerName"/> is null or blank.</exception>
        public static void AddRedactedHeader(string headerName)
        {
            if (string.IsNullOrWhiteSpace(headerName))
            {
                throw new ArgumentException("A header name is required.", nameof(headerName));
            }

            lock (ScimLogging.SyncRoot)
            {
                if (ScimLogging.redactedHeaders.Contains(headerName))
                {
                    return;
                }

                HashSet<string> extended =
                    new HashSet<string>(ScimLogging.redactedHeaders, StringComparer.OrdinalIgnoreCase)
                    {
                        headerName,
                    };

                ScimLogging.redactedHeaders = extended;
            }
        }

        /// <summary>Whether <paramref name="headerName"/>'s value is written or replaced.</summary>
        public static bool IsRedacted(string headerName)
        {
            return null != headerName && ScimLogging.redactedHeaders.Contains(headerName);
        }
    }
}
