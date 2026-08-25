// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM.WebHostSample.Provider
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Net;
    using System.Threading.Tasks;
    using System.Web.Http;
    using Microsoft.SCIM;
    using Microsoft.SCIM.WebHostSample.Domain;

    /// <summary>
    /// A group provider over a store of domain entities. See <see cref="InMemoryUserProvider"/>
    /// for the arrangement; this is the same one, over the group aggregate and its membership.
    /// </summary>
    public class InMemoryGroupProvider : ProviderBase
    {
        private readonly InMemoryStorage storage;

        public InMemoryGroupProvider()
        {
            this.storage = InMemoryStorage.Instance;
        }

        public override Task<Resource> CreateAsync(Resource resource, string correlationIdentifier)
        {
            lock (this.storage.SyncRoot)
            {
                if (resource.Identifier != null)
                {
                    throw new HttpResponseException(HttpStatusCode.BadRequest);
                }

                Core2Group group = resource as Core2Group;

                if (string.IsNullOrWhiteSpace(group?.DisplayName))
                {
                    throw new HttpResponseException(HttpStatusCode.BadRequest);
                }

                if
                (
                    this.storage.Groups.Values.Any(
                        (GroupEntity existing) =>
                            string.Equals(existing.DisplayName, group.DisplayName, StringComparison.Ordinal))
                )
                {
                    throw new HttpResponseException(HttpStatusCode.Conflict);
                }

                GroupEntity entity = ScimGroupMapper.ToEntity(group);

                DateTime created = DateTime.UtcNow;
                entity.Id = Guid.NewGuid().ToString();
                entity.CreatedUtc = created;
                entity.LastModifiedUtc = created;

                // The membership rows carry the group's key, which is only known now.
                foreach (GroupMemberEntity member in entity.Members)
                {
                    member.GroupId = entity.Id;
                }

                this.storage.Groups.Add(entity.Id, entity);

                return Task.FromResult<Resource>(ScimGroupMapper.ToScim(entity));
            }
        }

        public override Task DeleteAsync(IResourceIdentifier resourceIdentifier, string correlationIdentifier)
        {
            lock (this.storage.SyncRoot)
            {
                if (string.IsNullOrWhiteSpace(resourceIdentifier?.Identifier))
                {
                    throw new HttpResponseException(HttpStatusCode.BadRequest);
                }

                string identifier = resourceIdentifier.Identifier;

                if (this.storage.Groups.ContainsKey(identifier))
                {
                    this.storage.Groups.Remove(identifier);
                }

                return Task.CompletedTask;
            }
        }

        public override Task<Resource[]> QueryAsync(IQueryParameters parameters, string correlationIdentifier)
        {
            lock (this.storage.SyncRoot)
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

                var predicate = PredicateBuilder.False<GroupEntity>();
                Expression<Func<GroupEntity, bool>> predicateAnd;
                predicateAnd = PredicateBuilder.True<GroupEntity>();

                if (queryFilter != null)
                {
                    if (string.IsNullOrWhiteSpace(queryFilter.AttributePath))
                    {
                        throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidParameters);
                    }

                    if (string.IsNullOrWhiteSpace(queryFilter.ComparisonValue))
                    {
                        throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidParameters);
                    }

                    if (queryFilter.FilterOperator != ComparisonOperator.Equals)
                    {
                        throw new NotSupportedException(string.Format(SystemForCrossDomainIdentityManagementServiceResources.ExceptionFilterOperatorNotSupportedTemplate, queryFilter.FilterOperator));
                    }

                    if (queryFilter.AttributePath.Equals(AttributeNames.DisplayName))
                    {
                        string displayName = queryFilter.ComparisonValue;
                        predicateAnd = predicateAnd.And(p => string.Equals(p.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        throw new NotSupportedException(string.Format(SystemForCrossDomainIdentityManagementServiceResources.ExceptionFilterAttributePathNotSupportedTemplate, queryFilter.AttributePath));
                    }
                }

                predicate = predicate.Or(predicateAnd);

                Resource[] results =
                    this.storage
                        .Groups
                        .Values
                        .Where(predicate.Compile())
                        .Select((GroupEntity entity) => (Resource)ScimGroupMapper.ToScim(entity))
                        .ToArray();

                return Task.FromResult(results);
            }
        }

        public override Task<Resource> ReplaceAsync(Resource resource, string correlationIdentifier)
        {
            lock (this.storage.SyncRoot)
            {
                if (resource.Identifier == null)
                {
                    throw new HttpResponseException(HttpStatusCode.BadRequest);
                }

                Core2Group group = resource as Core2Group;

                if (string.IsNullOrWhiteSpace(group?.DisplayName))
                {
                    throw new HttpResponseException(HttpStatusCode.BadRequest);
                }

                if
                (
                    this.storage.Groups.Values.Any(
                        (GroupEntity existing) =>
                            string.Equals(existing.DisplayName, group.DisplayName, StringComparison.Ordinal) &&
                            !string.Equals(existing.Id, group.Identifier, StringComparison.OrdinalIgnoreCase))
                )
                {
                    throw new HttpResponseException(HttpStatusCode.Conflict);
                }

                if (!this.storage.Groups.TryGetValue(group.Identifier, out GroupEntity stored))
                {
                    throw new HttpResponseException(HttpStatusCode.NotFound);
                }

                GroupEntity replacement = ScimGroupMapper.ToEntity(group);
                replacement.Id = stored.Id;
                replacement.CreatedUtc = stored.CreatedUtc;
                replacement.LastModifiedUtc = DateTime.UtcNow;

                foreach (GroupMemberEntity member in replacement.Members)
                {
                    member.GroupId = replacement.Id;
                }

                this.storage.Groups[replacement.Id] = replacement;

                return Task.FromResult<Resource>(ScimGroupMapper.ToScim(replacement));
            }
        }

        public override Task<Resource> RetrieveAsync(IResourceRetrievalParameters parameters, string correlationIdentifier)
        {
            lock (this.storage.SyncRoot)
            {
                if (parameters == null)
                {
                    throw new ArgumentNullException(nameof(parameters));
                }

                if (string.IsNullOrWhiteSpace(correlationIdentifier))
                {
                    throw new ArgumentNullException(nameof(correlationIdentifier));
                }

                if (string.IsNullOrEmpty(parameters?.ResourceIdentifier?.Identifier))
                {
                    throw new ArgumentNullException(nameof(parameters));
                }

                string identifier = parameters.ResourceIdentifier.Identifier;

                if (this.storage.Groups.TryGetValue(identifier, out GroupEntity entity))
                {
                    return Task.FromResult<Resource>(ScimGroupMapper.ToScim(entity));
                }

                throw new HttpResponseException(HttpStatusCode.NotFound);
            }
        }

        public override Task UpdateAsync(IPatch patch, string correlationIdentifier)
        {
            lock (this.storage.SyncRoot)
            {
                if (null == patch)
                {
                    throw new ArgumentNullException(nameof(patch));
                }

                if (null == patch.ResourceIdentifier)
                {
                    throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidOperation);
                }

                if (string.IsNullOrWhiteSpace(patch.ResourceIdentifier.Identifier))
                {
                    throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidOperation);
                }

                if (null == patch.PatchRequest)
                {
                    throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidOperation);
                }

                PatchRequest2 patchRequest =
                    patch.PatchRequest as PatchRequest2;

                if (null == patchRequest)
                {
                    string unsupportedPatchTypeName = patch.GetType().FullName;
                    throw new NotSupportedException(unsupportedPatchTypeName);
                }

                if (!this.storage.Groups.TryGetValue(patch.ResourceIdentifier.Identifier, out GroupEntity stored))
                {
                    throw new HttpResponseException(HttpStatusCode.NotFound);
                }

                // Projected out, patched, mapped back in. See the matching comment in
                // InMemoryUserProvider.UpdateAsync for why that is what makes a PATCH atomic.
                Core2Group candidate = ScimGroupMapper.ToScim(stored);
                candidate.Apply(patchRequest);

                // The same uniqueness rule CreateAsync and ReplaceAsync enforce. A PATCH can
                // rename a group too, and without this it could rename one onto another's
                // displayName - leaving two groups a client cannot tell apart.
                if
                (
                    this.storage.Groups.Values.Any(
                        (GroupEntity existing) =>
                            string.Equals(existing.DisplayName, candidate.DisplayName, StringComparison.Ordinal)
                            && !string.Equals(existing.Id, candidate.Identifier, StringComparison.OrdinalIgnoreCase))
                )
                {
                    throw new HttpResponseException(HttpStatusCode.Conflict);
                }

                GroupEntity patched = ScimGroupMapper.ToEntity(candidate);
                patched.Id = stored.Id;
                patched.CreatedUtc = stored.CreatedUtc;
                patched.LastModifiedUtc = DateTime.UtcNow;

                foreach (GroupMemberEntity member in patched.Members)
                {
                    member.GroupId = patched.Id;
                }

                this.storage.Groups[patched.Id] = patched;

                return Task.CompletedTask;
            }
        }
    }
}
