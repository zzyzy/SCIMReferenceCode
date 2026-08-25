// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Reflection;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc.Controllers;
    using Microsoft.AspNetCore.Mvc.Filters;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Makes the request body readable again after model binding has consumed it, for hosts
    /// whose logging will use it.
    /// </summary>
    /// <remarks>
    /// Not a logging feature - the mechanism one needs. When a SCIM operation fails, the
    /// handler logs the request that caused it, body included, and by then MVC has read the
    /// stream to bind the action's parameter. Without buffering there is nothing left to
    /// re-read, and the entry that exists to say what the client sent would say nothing.
    ///
    /// Logging every request and response is the host's job, not this library's -
    /// <c>UseHttpLogging</c> does it, with its own switches and limits. This filter exists so
    /// that the one thing a host cannot log, a SCIM failure, is not logged blind.
    ///
    /// Skipped entirely when nothing would write the entry. Buffering costs a copy of every
    /// request body, and paying it for a log line that is filtered out before it is formatted
    /// is paying for nothing. The check is against the category the failure logger actually
    /// uses - the controller's own, taken from the action descriptor - so a host that has
    /// silenced one SCIM controller and not another gets the right answer for each.
    ///
    /// A resource filter rather than middleware, because it has to run after routing has
    /// picked a SCIM action - which is also what makes the controller's category known - and
    /// before model binding, which is exactly the window
    /// <see cref="IResourceFilter.OnResourceExecuting"/> occupies.
    ///
    /// <c>EnableBuffering</c> keeps the body in memory up to a threshold and spills to a
    /// temporary file beyond it, so a large body does not become a large allocation.
    /// </remarks>
    public sealed class ScimRequestBufferingFilter : IResourceFilter
    {
        /// <summary>The level <see cref="ScimLoggerExtensions.LogScimFailure"/> writes at.</summary>
        private const LogLevel FailureLevel = LogLevel.Error;

        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            if (null == context)
            {
                throw new ArgumentNullException(nameof(context));
            }

            HttpRequest request = context.HttpContext?.Request;

            if (null == request || null == request.Body || request.Body.CanSeek)
            {
                return;
            }

            if (!ScimRequestBufferingFilter.WillLogFailures(context))
            {
                return;
            }

            request.EnableBuffering();
        }

        public void OnResourceExecuted(ResourceExecutedContext context)
        {
            // Nothing to undo: the buffered stream is disposed with the request.
        }

        /// <summary>
        /// Whether a failure on this request would actually be written.
        /// </summary>
        /// <remarks>
        /// No logger factory means no logging at all, so nothing would read the body. A
        /// factory that yields a disabled logger - a <c>NullLogger</c>, or a host that has
        /// filtered this category below Error - means the same.
        ///
        /// The category is built through <c>CreateLogger(Type)</c> rather than from the type's
        /// name, because that is what <c>ILogger&lt;T&gt;</c> does: it strips generic arguments,
        /// so a closed <c>ScimUsersController&lt;EduPassUser&gt;</c> logs under
        /// <c>Microsoft.SCIM.ScimUsersController</c>. Composing the name by hand would probe a
        /// category no entry is ever written to, and answer for the wrong one.
        /// </remarks>
        private static bool WillLogFailures(ResourceExecutingContext context)
        {
            ILoggerFactory factory = context.HttpContext.RequestServices?.GetService<ILoggerFactory>();

            if (null == factory)
            {
                return false;
            }

            TypeInfo controller = (context.ActionDescriptor as ControllerActionDescriptor)?.ControllerTypeInfo;

            ILogger logger =
                null == controller
                    ? factory.CreateLogger(typeof(ScimRequestBufferingFilter))
                    : factory.CreateLogger(controller.AsType());

            return logger.IsEnabled(ScimRequestBufferingFilter.FailureLevel);
        }
    }
}
