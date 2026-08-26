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
    /// A user provider over SQLite, through Dapper.
    /// </summary>
    /// <remarks>
    /// Deliberately the same file as <see cref="InMemoryUserProvider"/> with the store swapped:
    /// the validation, the status codes and the domain's rules are unchanged, and every method
    /// still has the same three parts - map the request in, apply the rules, map the stored row
    /// back out. That is the claim <c>Domain</c> makes, and this provider is what tests it.
    ///
    /// What does change is where the atomicity comes from. The in-memory provider holds one
    /// process-wide lock; this one opens a transaction that takes SQLite's write lock up front,
    /// so the check and the write it guards cannot be interleaved with another request's - or
    /// with another process's, which no lock could have covered.
    /// </remarks>
    public class DatabaseUserProvider : ProviderBase
    {
        private readonly ScimDatabase database;

        public DatabaseUserProvider(ScimDatabase database)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
        }

        public override async Task<Resource> CreateAsync(Resource resource, string correlationIdentifier)
        {
            if (resource?.Identifier != null)
            {
                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }

            Core2EnterpriseUser user = resource as Core2EnterpriseUser;

            if (string.IsNullOrWhiteSpace(user?.UserName))
            {
                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }

            using (SqliteConnection connection = this.database.Open())
            using (SqliteTransaction transaction = ScimDatabase.BeginWrite(connection))
            {
                if (await UserRepository
                        .UserNameTakenAsync(connection, transaction, user.UserName, null)
                        .ConfigureAwait(false))
                {
                    throw new HttpResponseException(HttpStatusCode.Conflict);
                }

                UserEntity entity = ScimUserMapper.ToEntity(user);

                // The store's to assign, which is why the mapper leaves them alone.
                DateTime created = DateTime.UtcNow;
                entity.Id = Guid.NewGuid().ToString();
                entity.CreatedUtc = created;
                entity.LastModifiedUtc = created;

                await UserRepository.InsertAsync(connection, transaction, entity).ConfigureAwait(false);

                transaction.Commit();

                return ScimUserMapper.ToScim(entity);
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
                // success. The child rows go with it, by ON DELETE CASCADE.
                bool deleted =
                    await UserRepository
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

            DynamicParameters arguments = new DynamicParameters();
            string where = SqliteFilterTranslator.TranslateUsers(parameters.AlternateFilters, arguments);

            using (SqliteConnection connection = this.database.Open())
            {
                IReadOnlyList<UserEntity> matches =
                    await UserRepository
                        .LoadAsync(connection, null, where, arguments)
                        .ConfigureAwait(false);

                // Every match, not a page: ProviderBase.PaginateQueryAsync applies startIndex
                // and count, and needs the full match count to report totalResults. A LIMIT
                // here would report the page size as the total.
                return matches
                    .Select((UserEntity entity) => (Resource)ScimUserMapper.ToScim(entity))
                    .ToArray();
            }
        }

        public override async Task<Resource> ReplaceAsync(Resource resource, string correlationIdentifier)
        {
            if (resource?.Identifier == null)
            {
                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }

            Core2EnterpriseUser user = resource as Core2EnterpriseUser;

            if (string.IsNullOrWhiteSpace(user?.UserName))
            {
                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }

            using (SqliteConnection connection = this.database.Open())
            using (SqliteTransaction transaction = ScimDatabase.BeginWrite(connection))
            {
                if (await UserRepository
                        .UserNameTakenAsync(connection, transaction, user.UserName, user.Identifier)
                        .ConfigureAwait(false))
                {
                    throw new HttpResponseException(HttpStatusCode.Conflict);
                }

                UserEntity stored =
                    await UserRepository
                        .FindAsync(connection, transaction, user.Identifier)
                        .ConfigureAwait(false);

                if (null == stored)
                {
                    throw new HttpResponseException(HttpStatusCode.NotFound);
                }

                // RFC 7644 3.5.1: a replace, so the entity is built from the request alone and
                // an attribute the body omits is cleared. Only what the store owns survives.
                UserEntity replacement = ScimUserMapper.ToEntity(user);
                replacement.Id = stored.Id;
                replacement.CreatedUtc = stored.CreatedUtc;
                replacement.LastModifiedUtc = DateTime.UtcNow;

                await UserRepository.ReplaceAsync(connection, transaction, replacement).ConfigureAwait(false);

                transaction.Commit();

                return ScimUserMapper.ToScim(replacement);
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
                UserEntity entity =
                    await UserRepository
                        .FindAsync(connection, null, parameters.ResourceIdentifier.Identifier)
                        .ConfigureAwait(false);

                if (null == entity)
                {
                    throw new HttpResponseException(HttpStatusCode.NotFound);
                }

                return ScimUserMapper.ToScim(entity);
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
                UserEntity stored =
                    await UserRepository
                        .FindAsync(connection, transaction, patch.ResourceIdentifier.Identifier)
                        .ConfigureAwait(false);

                if (null == stored)
                {
                    throw new HttpResponseException(HttpStatusCode.NotFound);
                }

                // A PATCH is expressed against the resource, so the stored row is projected back
                // into one, patched, and mapped in again. RFC 7644 3.5.2 wants all of the
                // operations or none: a failure part-way through throws before anything is
                // written, and the transaction rolls back what was.
                Core2EnterpriseUser candidate = ScimUserMapper.ToScim(stored);
                candidate.Apply(patchRequest);

                // The uniqueness rule a PATCH can break too, by renaming a user onto another's
                // userName.
                if (!string.IsNullOrWhiteSpace(candidate.UserName)
                    && await UserRepository
                        .UserNameTakenAsync(connection, transaction, candidate.UserName, stored.Id)
                        .ConfigureAwait(false))
                {
                    throw new HttpResponseException(HttpStatusCode.Conflict);
                }

                UserEntity patched = ScimUserMapper.ToEntity(candidate);
                patched.Id = stored.Id;
                patched.CreatedUtc = stored.CreatedUtc;
                patched.LastModifiedUtc = DateTime.UtcNow;

                await UserRepository.ReplaceAsync(connection, transaction, patched).ConfigureAwait(false);

                transaction.Commit();
            }
        }
    }
}
