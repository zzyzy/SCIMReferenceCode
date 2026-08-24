// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Scim.EduPass
{
    using Microsoft.Extensions.Logging;
    using Microsoft.SCIM;

    /// <summary>
    /// The provider adapter for <see cref="EduPassUser"/>.
    /// </summary>
    /// <remarks>
    /// Reports the enterprise User schema identifier, which is the correct answer:
    /// <see cref="IProviderAdapter{T}.SchemaIdentifier"/> names the resource type's base schema,
    /// and an <see cref="EduPassUser"/> is an enterprise User that carries one further extension.
    /// The Edupass URI identifies that extension, not the resource type, so it does not belong
    /// here - and reporting it would leave <c>SchemaIdentifier.FindPath</c> with no mapping to
    /// <c>/Users</c>.
    ///
    /// <see cref="ReturnsResourceOnPatch"/> is what makes <c>PATCH /Users/{id}</c> answer 200
    /// with the resource, as the Edupass specification requires.
    /// </remarks>
    public class EduPassUserProviderAdapter : ProviderAdapterTemplate<EduPassUser>
    {
        public EduPassUserProviderAdapter(IProvider provider)
            : base(provider)
        {
        }

        public override bool ReturnsResourceOnPatch
        {
            get
            {
                return true;
            }
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
