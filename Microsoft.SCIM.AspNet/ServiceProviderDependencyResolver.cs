// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Web.Http.Dependencies;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>
    /// Bridges Microsoft.Extensions.DependencyInjection onto ASP.NET Web API's
    /// <see cref="IDependencyResolver"/>, so that both hosting legs register their SCIM
    /// dependencies with the same container and the same lines of code.
    /// See MULTI-TARGET-PLAN.md D9.
    /// </summary>
    public sealed class ServiceProviderDependencyResolver : IDependencyResolver
    {
        private readonly IServiceScope scope;
        private readonly IServiceProvider services;

        public ServiceProviderDependencyResolver(IServiceProvider services)
            : this(services, null)
        {
        }

        private ServiceProviderDependencyResolver(IServiceProvider services, IServiceScope scope)
        {
            this.services = services ?? throw new ArgumentNullException(nameof(services));
            this.scope = scope;
        }

        public IDependencyScope BeginScope()
        {
            IServiceScope childScope = this.services.CreateScope();
            return new ServiceProviderDependencyResolver(childScope.ServiceProvider, childScope);
        }

        /// <remarks>
        /// Web API requires <c>null</c> for an unregistered type rather than a throw, which is
        /// the opposite of <c>GetRequiredService</c>. <c>GetService</c> already returns null, so
        /// this is a straight delegation - but it is the single most common way to get this
        /// bridge wrong, hence the note.
        /// </remarks>
        public object GetService(Type serviceType)
        {
            return this.services.GetService(serviceType);
        }

        public IEnumerable<object> GetServices(Type serviceType)
        {
            if (null == serviceType)
            {
                return Enumerable.Empty<object>();
            }

            return this.services.GetServices(serviceType) ?? Enumerable.Empty<object>();
        }

        public void Dispose()
        {
            this.scope?.Dispose();
        }
    }
}
