// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web.Http.Dependencies;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Makes the request body readable again after the media-type formatter has consumed it,
    /// for hosts whose logging will use it.
    /// </summary>
    /// <remarks>
    /// The counterpart of <c>ScimRequestBufferingFilter</c> on the ASP.NET Core leg, and
    /// necessary for the same reason: by the time a controller action runs, the body has been
    /// read to bind the action's parameter, and re-reading it for the failure logging in
    /// <see cref="ScimLoggerExtensions"/> yields nothing. <c>LoadIntoBufferAsync</c> replaces
    /// the content's stream with a buffered copy, which can be read as often as anything asks.
    ///
    /// A message handler rather than a filter because it has to run before model binding, and
    /// filters do not. It is inserted only into the SCIM configuration, so a host serving other
    /// endpoints from the same process buffers nothing it did not ask to.
    ///
    /// Skipped when nothing would write the entry - see <see cref="WillLogFailures"/> - and
    /// when the body is longer than <see cref="ScimLogging.MaximumBodyLength"/> or its length
    /// is unknown: buffering more than will ever be written would be paid for on every request
    /// to no purpose. A body that is not buffered is reported as absent rather than partially
    /// read.
    /// </remarks>
    public sealed class ScimRequestBufferingHandler : DelegatingHandler
    {
        /// <summary>The level <see cref="ScimLoggerExtensions.LogScimFailure"/> writes at.</summary>
        private const LogLevel FailureLevel = LogLevel.Error;

        /// <summary>
        /// The category the enabled check is made against.
        /// </summary>
        /// <remarks>
        /// The namespace every SCIM controller is in, and therefore the prefix of every
        /// category the failure logging writes under. It is a prefix rather than the exact
        /// category because this runs before Web API has selected a controller, so there is no
        /// controller type to name yet - unlike the ASP.NET Core leg, where routing has already
        /// happened by the time the filter runs.
        ///
        /// Level rules in Microsoft.Extensions.Logging match by prefix, so a level set here or
        /// on any ancestor - including Default - is the one that answers. A host that silences
        /// this namespace and then re-enables a single controller under it would still be
        /// skipped: set the level on <c>Microsoft.SCIM</c> for this leg.
        /// </remarks>
        private const string Category = "Microsoft.SCIM";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (ScimRequestBufferingHandler.ShouldBuffer(request))
            {
                try
                {
                    await request.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Buffering is a convenience for the log. A request that cannot be
                    // buffered is still a request, and must be served.
                }
            }

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        private static bool ShouldBuffer(HttpRequestMessage request)
        {
            if (null == request?.Content)
            {
                return false;
            }

            long? length = request.Content.Headers?.ContentLength;

            if (!length.HasValue
                || length.Value <= 0
                || length.Value > ScimLogging.MaximumBodyLength)
            {
                return false;
            }

            return ScimRequestBufferingHandler.WillLogFailures(request);
        }

        /// <summary>
        /// Whether a failure on this request would actually be written.
        /// </summary>
        /// <remarks>
        /// No logger factory means no logging at all - the controllers are constructed from
        /// this same container, so a missing factory is a controller with no logger - and
        /// nothing would read the body. A factory that yields a disabled logger means the same.
        ///
        /// Resolved from the request's own dependency scope rather than the root container, so
        /// that a host whose logging configuration is scoped is answered from the same scope
        /// its controllers are built in.
        /// </remarks>
        private static bool WillLogFailures(HttpRequestMessage request)
        {
            try
            {
                IDependencyScope scope = request.GetDependencyScope();

                if (!(scope?.GetService(typeof(ILoggerFactory)) is ILoggerFactory factory))
                {
                    return false;
                }

                return factory
                    .CreateLogger(ScimRequestBufferingHandler.Category)
                    .IsEnabled(ScimRequestBufferingHandler.FailureLevel);
            }
            catch (Exception)
            {
                // A resolver that throws is the host's problem, not a reason to stop serving
                // the request. Buffer, and let the logging decide for itself.
                return true;
            }
        }
    }
}
