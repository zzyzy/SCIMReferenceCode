// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Scim.EduPass
{
    using Microsoft.Extensions.Logging;
    using Microsoft.SCIM;

    /// <summary>
    /// The provider adapter for <see cref="EduPassUser"/>.
    /// </summary>
    /// <remarks>
    /// Reports the enterprise User schema identifier rather than the Edupass extension URI.
    /// Two reasons: <c>SchemaIdentifier.TryFindPath</c> maps only the core identifiers to
    /// <c>/Users</c>, and <c>ScimRequestHandler.PatchAsync</c> tests for that same identifier to
    /// decide whether a PATCH answers 200 with the resource - which is the behaviour the Edupass
    /// specification requires of <c>PATCH /Users/{id}</c>.
    /// </remarks>
    public class EduPassUserProviderAdapter : ProviderAdapterTemplate<EduPassUser>
    {
        public EduPassUserProviderAdapter(IProvider provider)
            : base(provider)
        {
        }

        public override string SchemaIdentifier
        {
            get
            {
                return SchemaIdentifiers.Core2EnterpriseUser;
            }
        }
    }

    /// <summary>
    /// Creates the request handler for the Edupass User resource.
    /// </summary>
    /// <remarks>
    /// <c>ScimRequestHandlerFactory.CreateUserHandler</c> is hard-coded to the sealed
    /// <c>Core2EnterpriseUser</c> and its internal adapter, so it cannot produce a handler for
    /// this type. <c>ScimRequestHandler&lt;T&gt;</c> itself is generic over
    /// <c>where T : Resource</c> and needs no change - only the construction does.
    /// </remarks>
    public static class EduPassRequestHandlerFactory
    {
        public static ScimRequestHandler<EduPassUser> CreateUserHandler(IProvider provider, ILogger logger)
        {
            return
                new ScimRequestHandler<EduPassUser>(
                    provider,
                    logger,
                    (IProvider adapted) => new EduPassUserProviderAdapter(adapted));
        }
    }
}
