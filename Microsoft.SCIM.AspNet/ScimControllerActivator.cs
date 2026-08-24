// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Net.Http;
    using System.Web.Http.Controllers;
    using System.Web.Http.Dispatcher;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>
    /// Activates SCIM controllers through the Microsoft.Extensions.DependencyInjection
    /// container supplied to <see cref="ScimHttpConfiguration.Configure"/>.
    /// </summary>
    /// <remarks>
    /// Web API's default activator only ever calls a parameterless constructor: it asks the
    /// dependency resolver for the controller type, and because controllers are not - and on
    /// the ASP.NET Core leg need not be - registered in the container, that lookup returns
    /// null and activation falls back to <c>new()</c>. Every SCIM controller takes
    /// <see cref="IProvider"/> and <see cref="IMonitor"/>, so the fallback throws.
    /// ASP.NET Core solves this with <c>ActivatorUtilities</c>, which constructs an
    /// unregistered type from registered dependencies; doing the same here keeps the two legs
    /// registering exactly the same services.
    /// </remarks>
    public sealed class ScimControllerActivator : IHttpControllerActivator
    {
        public IHttpController Create(
            HttpRequestMessage request,
            HttpControllerDescriptor controllerDescriptor,
            Type controllerType)
        {
            if (null == request)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (null == controllerType)
            {
                throw new ArgumentNullException(nameof(controllerType));
            }

            // The per-request scope, so that any scoped dependency a consumer registers is
            // resolved from - and disposed with - the request rather than the root container.
            IServiceProvider services = new DependencyScopeServiceProvider(request.GetDependencyScope());

            return (IHttpController)ActivatorUtilities.CreateInstance(services, controllerType);
        }

        private sealed class DependencyScopeServiceProvider : IServiceProvider
        {
            private readonly System.Web.Http.Dependencies.IDependencyScope scope;

            public DependencyScopeServiceProvider(System.Web.Http.Dependencies.IDependencyScope scope)
            {
                this.scope = scope ?? throw new ArgumentNullException(nameof(scope));
            }

            public object GetService(Type serviceType)
            {
                return this.scope.GetService(serviceType);
            }
        }
    }
}
