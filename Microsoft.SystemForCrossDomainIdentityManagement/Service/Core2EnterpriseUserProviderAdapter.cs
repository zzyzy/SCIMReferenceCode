// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    /// <summary>
    /// The provider adapter for the enterprise User resource type.
    /// </summary>
    /// <remarks>
    /// Generic so that a downstream library adding a schema extension - which needs its own
    /// derived resource type, because the controller's generic parameter is the model-binding
    /// type - does not have to restate the adapter. Such a type is still an enterprise User
    /// carrying one further extension, so it reports the same schema identifier and the same
    /// PATCH behaviour.
    /// </remarks>
    internal class Core2EnterpriseUserProviderAdapter<T> : ProviderAdapterTemplate<T>
        where T : Core2EnterpriseUser
    {
        public Core2EnterpriseUserProviderAdapter(IProvider provider)
            : base(provider)
        {
        }

        public override bool ReturnsResourceOnPatch
        {
            get { return true; }
        }

        public override string SchemaIdentifier
        {
            get
            {
                return SchemaIdentifiers.Core2EnterpriseUser;
            }
        }
    }
}
