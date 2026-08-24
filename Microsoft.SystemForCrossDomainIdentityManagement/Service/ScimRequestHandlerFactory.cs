// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Creates the hosting-neutral request handlers.
    /// </summary>
    /// <remarks>
    /// The concrete provider adapters (<c>Core2EnterpriseUserProviderAdapter</c>,
    /// <c>Core2GroupProviderAdapter</c>, <c>RootProviderAdapter</c>) are internal to
    /// this assembly, so the hosting projects cannot construct them directly. Routing
    /// that construction through here keeps them internal and, more usefully, means the
    /// resource-to-adapter mapping is written once rather than once per hosting leg.
    /// </remarks>
    public static class ScimRequestHandlerFactory
    {
        public static ScimRequestHandler<Core2EnterpriseUser> CreateUserHandler(IProvider provider, ILogger logger)
        {
            return new ScimRequestHandler<Core2EnterpriseUser>(
                provider,
                logger,
                (IProvider adapted) =>
                {
                    if (null == adapted)
                    {
                        throw new ArgumentNullException(nameof(adapted));
                    }

                    return new Core2EnterpriseUserProviderAdapter(adapted);
                });
        }

        public static ScimRequestHandler<Core2Group> CreateGroupHandler(IProvider provider, ILogger logger)
        {
            return new ScimRequestHandler<Core2Group>(
                provider,
                logger,
                (IProvider adapted) =>
                {
                    if (null == adapted)
                    {
                        throw new ArgumentNullException(nameof(adapted));
                    }

                    return new Core2GroupProviderAdapter(adapted);
                });
        }

        public static ScimRequestHandler<Resource> CreateRootHandler(IProvider provider, ILogger logger)
        {
            return new ScimRequestHandler<Resource>(
                provider,
                logger,
                (IProvider adapted) =>
                {
                    if (null == adapted)
                    {
                        throw new ArgumentNullException(nameof(adapted));
                    }

                    return new RootProviderAdapter(adapted);
                });
        }

        public static ScimDiscoveryRequestHandler CreateDiscoveryHandler(IProvider provider, ILogger logger)
        {
            return new ScimDiscoveryRequestHandler(provider, logger);
        }
    }
}
