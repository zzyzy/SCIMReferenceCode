// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM.WebHostSample.Provider.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Threading.Tasks;
    using System.Web.Http;
    using Dapper;
    using Microsoft.Data.Sqlite;
    using Microsoft.SCIM;
    using Microsoft.SCIM.WebHostSample.Domain;

    /// <summary>
    /// A group provider over SQLite, through Dapper. See <see cref="DatabaseUserProvider"/> for
    /// the arrangement; this is the same one, over the group aggregate and its membership.
    /// </summary>
    public class DatabaseGroupProvider : ProviderBase
    {
        private readonly ScimDatabase database;

        public DatabaseGroupProvider(ScimDatabase database)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
        }

        public override async Task<Resource> CreateAsync(Resource resource, string correlationIdentifier)
        {
            if (resource?.Identifier != null)
            {
                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }

            Core2Group group = resource as Core2Group;

            if (string.IsNullOrWhiteSpace(group?.DisplayName))
            {
                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }

            using (SqliteConnection connection = this.database.Open())
            using (SqliteTransaction transaction = ScimDatabase.BeginWrite(connection))
            {
                if (await GroupRepository
                        .DisplayNameTakenAsync(connection, transaction, group.DisplayName, null)
                        .ConfigureAwait(false))
                {
                    throw new HttpResponseException(HttpStatusCode.Conflict);
                }

                GroupEntity entity = ScimGroupMapper.ToEntity(group);

                DateTime created = DateTime.UtcNow;
                entity.Id = Guid.NewGuid().ToString();
                entity.CreatedUtc = created;
                entity.LastModifiedUtc = created;

                await GroupRepository.InsertAsync(connection, transaction, entity).ConfigureAwait(false);

                transaction.Commit();

                return ScimGroupMapper.ToScim(entity);
            }
        }

        public override async Task DeleteAsync(IResourceIdentifier resourceIdentifier, string correlationIdentifier)
        {
            if (string.IsNullOrWhiteSpace(resourceIdentifier?.Identifier))
            {
                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }

            using (SqliteConnection connection = this.database.Open())
            using (SqliteTransaction transaction = ScimDatabase.BeginWrite(connection))
            {
                // RFC 7644 3.6: a delete of a resource that is not there is a 404, not a
                // success. The membership rows go with it, by ON DELETE CASCADE.
                bool deleted =
                    await GroupRepository
                        .DeleteAsync(connection, transaction, resourceIdentifier.Identifier)
                        .ConfigureAwait(false);

                if (!deleted)
                {
                    throw new HttpResponseException(HttpStatusCode.NotFound);
                }

                transaction.Commit();
            }
        }

        public override async Task<Resource[]> QueryAsync(IQueryParameters parameters, string correlationIdentifier)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            if (string.IsNullOrWhiteSpace(correlationIdentifier))
            {
                throw new ArgumentNullException(nameof(correlationIdentifier));
            }

            if (null == parameters.AlternateFilters)
            {
                throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidParameters);
            }

            if (string.IsNullOrWhiteSpace(parameters.SchemaIdentifier))
            {
                throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidParameters);
            }

            IFilter queryFilter = parameters.AlternateFilters.SingleOrDefault();

            DynamicParameters arguments = new DynamicParameters();
            string where = SqliteFilterTranslator.TranslateGroups(queryFilter, arguments);

            using (SqliteConnection connection = this.database.Open())
            {
                IReadOnlyList<GroupEntity> matches =
                    await GroupRepository
                        .LoadAsync(connection, null, where, arguments)
                        .ConfigureAwait(false);

                return matches
                    .Select((GroupEntity entity) => (Resource)ScimGroupMapper.ToScim(entity))
                    .ToArray();
            }
        }

        public override async Task<Resource> ReplaceAsync(Resource resource, string correlationIdentifier)
        {
            if (resource?.Identifier == null)
            {
                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }

            Core2Group group = resource as Core2Group;

            if (string.IsNullOrWhiteSpace(group?.DisplayName))
            {
                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }

            using (SqliteConnection connection = this.database.Open())
            using (SqliteTransaction transaction = ScimDatabase.BeginWrite(connection))
            {
                if (await GroupRepository
                        .DisplayNameTakenAsync(connection, transaction, group.DisplayName, group.Identifier)
                        .ConfigureAwait(false))
                {
                    throw new HttpResponseException(HttpStatusCode.Conflict);
                }

                GroupEntity stored =
                    await GroupRepository
                        .FindAsync(connection, transaction, group.Identifier)
                        .ConfigureAwait(false);

                if (null == stored)
                {
                    throw new HttpResponseException(HttpStatusCode.NotFound);
                }

                GroupEntity replacement = ScimGroupMapper.ToEntity(group);
                replacement.Id = stored.Id;
                replacement.CreatedUtc = stored.CreatedUtc;
                replacement.LastModifiedUtc = DateTime.UtcNow;

                await GroupRepository.ReplaceAsync(connection, transaction, replacement).ConfigureAwait(false);

                transaction.Commit();

                return ScimGroupMapper.ToScim(replacement);
            }
        }

        public override async Task<Resource> RetrieveAsync(
            IResourceRetrievalParameters parameters,
            string correlationIdentifier)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            if (string.IsNullOrWhiteSpace(correlationIdentifier))
            {
                throw new ArgumentNullException(nameof(correlationIdentifier));
            }

            if (string.IsNullOrEmpty(parameters.ResourceIdentifier?.Identifier))
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            using (SqliteConnection connection = this.database.Open())
            {
                GroupEntity entity =
                    await GroupRepository
                        .FindAsync(connection, null, parameters.ResourceIdentifier.Identifier)
                        .ConfigureAwait(false);

                if (null == entity)
                {
                    throw new HttpResponseException(HttpStatusCode.NotFound);
                }

                return ScimGroupMapper.ToScim(entity);
            }
        }

        public override async Task UpdateAsync(IPatch patch, string correlationIdentifier)
        {
            if (null == patch)
            {
                throw new ArgumentNullException(nameof(patch));
            }

            if (null == patch.ResourceIdentifier
                || string.IsNullOrWhiteSpace(patch.ResourceIdentifier.Identifier)
                || null == patch.PatchRequest)
            {
                throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidOperation);
            }

            PatchRequest2 patchRequest = patch.PatchRequest as PatchRequest2;

            if (null == patchRequest)
            {
                throw new NotSupportedException(patch.GetType().FullName);
            }

            using (SqliteConnection connection = this.database.Open())
            using (SqliteTransaction transaction = ScimDatabase.BeginWrite(connection))
            {
                GroupEntity stored =
                    await GroupRepository
                        .FindAsync(connection, transaction, patch.ResourceIdentifier.Identifier)
                        .ConfigureAwait(false);

                if (null == stored)
                {
                    throw new HttpResponseException(HttpStatusCode.NotFound);
                }

                // Projected out, patched, mapped back in. See the matching comment in
                // DatabaseUserProvider.UpdateAsync for why that is what makes a PATCH atomic.
                Core2Group candidate = ScimGroupMapper.ToScim(stored);
                candidate.Apply(patchRequest);

                // The same uniqueness rule CreateAsync and ReplaceAsync enforce. A PATCH can
                // rename a group too, and without this it could rename one onto another's
                // displayName - leaving two groups a client cannot tell apart.
                if (!string.IsNullOrWhiteSpace(candidate.DisplayName)
                    && await GroupRepository
                        .DisplayNameTakenAsync(connection, transaction, candidate.DisplayName, stored.Id)
                        .ConfigureAwait(false))
                {
                    throw new HttpResponseException(HttpStatusCode.Conflict);
                }

                GroupEntity patched = ScimGroupMapper.ToEntity(candidate);
                patched.Id = stored.Id;
                patched.CreatedUtc = stored.CreatedUtc;
                patched.LastModifiedUtc = DateTime.UtcNow;

                await GroupRepository.ReplaceAsync(connection, transaction, patched).ConfigureAwait(false);

                transaction.Commit();
            }
        }
    }
}
