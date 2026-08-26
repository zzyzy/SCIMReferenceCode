// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM.WebHostSample.Provider.Database
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft.SCIM;
    using Microsoft.SCIM.WebHostSample.Resources;

    /// <summary>
    /// The database-backed counterpart of <see cref="InMemoryProvider"/>: one provider over the
    /// two resource types, dispatching to the user or the group provider.
    /// </summary>
    /// <remarks>
    /// The two share a <see cref="ScimDatabase"/> rather than holding one each, so that a
    /// membership row and the user it names are written through the same schema and, when an
    /// operation needs both, the same transaction could span them.
    /// </remarks>
    public class DatabaseProvider : ProviderBase
    {
        private readonly ProviderBase groupProvider;
        private readonly ProviderBase userProvider;

        private static readonly Lazy<IReadOnlyCollection<TypeScheme>> TypeSchema =
            new Lazy<IReadOnlyCollection<TypeScheme>>(
                () =>
                    new TypeScheme[]
                    {
                        SampleTypeScheme.UserTypeScheme,
                        SampleTypeScheme.GroupTypeScheme,
                        SampleTypeScheme.EnterpriseUserTypeScheme,
                        SampleTypeScheme.ResourceTypesTypeScheme,
                        SampleTypeScheme.SchemaTypeScheme,
                        SampleTypeScheme.ServiceProviderConfigTypeScheme
                    });

        private static readonly Lazy<IReadOnlyCollection<Core2ResourceType>> Types =
            new Lazy<IReadOnlyCollection<Core2ResourceType>>(
                () =>
                    new Core2ResourceType[]
                    {
                        SampleResourceTypes.UserResourceType,
                        SampleResourceTypes.GroupResourceType
                    });

        public DatabaseProvider(string connectionString)
            : this(new ScimDatabase(connectionString))
        {
        }

        public DatabaseProvider(ScimDatabase database)
        {
            if (null == database)
            {
                throw new ArgumentNullException(nameof(database));
            }

            this.groupProvider = new DatabaseGroupProvider(database);
            this.userProvider = new DatabaseUserProvider(database);
        }

        public override IReadOnlyCollection<Core2ResourceType> ResourceTypes => DatabaseProvider.Types.Value;

        public override IReadOnlyCollection<TypeScheme> Schema => DatabaseProvider.TypeSchema.Value;

        public override Task<Resource> CreateAsync(Resource resource, string correlationIdentifier)
        {
            return this.For(resource).CreateAsync(resource, correlationIdentifier);
        }

        public override Task DeleteAsync(IResourceIdentifier resourceIdentifier, string correlationIdentifier)
        {
            return this
                .For(resourceIdentifier?.SchemaIdentifier)
                .DeleteAsync(resourceIdentifier, correlationIdentifier);
        }

        public override Task<Resource[]> QueryAsync(IQueryParameters parameters, string correlationIdentifier)
        {
            return this.For(parameters?.SchemaIdentifier).QueryAsync(parameters, correlationIdentifier);
        }

        public override Task<Resource> ReplaceAsync(Resource resource, string correlationIdentifier)
        {
            return this.For(resource).ReplaceAsync(resource, correlationIdentifier);
        }

        public override Task<Resource> RetrieveAsync(
            IResourceRetrievalParameters parameters,
            string correlationIdentifier)
        {
            return this.For(parameters?.SchemaIdentifier).RetrieveAsync(parameters, correlationIdentifier);
        }

        public override Task UpdateAsync(IPatch patch, string correlationIdentifier)
        {
            if (null == patch)
            {
                throw new ArgumentNullException(nameof(patch));
            }

            if (string.IsNullOrWhiteSpace(patch.ResourceIdentifier?.Identifier)
                || string.IsNullOrWhiteSpace(patch.ResourceIdentifier.SchemaIdentifier))
            {
                throw new ArgumentException(nameof(patch));
            }

            return this.For(patch.ResourceIdentifier.SchemaIdentifier).UpdateAsync(patch, correlationIdentifier);
        }

        /// <summary>Chooses by the resource's type, for the operations that carry a body.</summary>
        private ProviderBase For(Resource resource)
        {
            if (resource is Core2EnterpriseUser)
            {
                return this.userProvider;
            }

            if (resource is Core2Group)
            {
                return this.groupProvider;
            }

            // Not a 400: the handlers turn this into 501, which is the honest answer for a
            // resource type this provider does not serve.
            throw new NotImplementedException();
        }

        /// <summary>Chooses by schema URN, for the operations that carry only an identifier.</summary>
        private ProviderBase For(string schemaIdentifier)
        {
            if (SchemaIdentifiers.Core2EnterpriseUser.Equals(schemaIdentifier, StringComparison.Ordinal))
            {
                return this.userProvider;
            }

            if (SchemaIdentifiers.Core2Group.Equals(schemaIdentifier, StringComparison.Ordinal))
            {
                return this.groupProvider;
            }

            throw new NotImplementedException();
        }
    }
}
