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
    /// A user provider over a store of domain entities.
    /// </summary>
    /// <remarks>
    /// Every method has the same three parts: map the request in, apply the domain's rules, map
    /// the stored row back out. The SCIM resource never reaches the store and the entity never
    /// reaches the wire, so the two can be changed independently - which is the arrangement a
    /// database-backed relying party needs, and the reason this sample maps rather than storing
    /// the resource it was sent.
    ///
    /// Queries filter over the entity, not over the resource, because that is what a database
    /// can answer: the predicates below translate a SCIM filter into a predicate on columns and
    /// would compose into a SQL WHERE clause unchanged.
    /// </remarks>
    public class InMemoryUserProvider : ProviderBase
    {
        private readonly InMemoryStorage storage;

        public InMemoryUserProvider()
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

                Core2EnterpriseUser user = resource as Core2EnterpriseUser;
                if (string.IsNullOrWhiteSpace(user?.UserName))
                {
                    throw new HttpResponseException(HttpStatusCode.BadRequest);
                }

                if
                (
                    this.storage.Users.Values.Any(
                        (UserEntity existing) =>
                            string.Equals(existing.UserName, user.UserName, StringComparison.Ordinal))
                )
                {
                    throw new HttpResponseException(HttpStatusCode.Conflict);
                }

                UserEntity entity = ScimUserMapper.ToEntity(user);

                // The store's to assign, which is why the mapper leaves them alone.
                DateTime created = DateTime.UtcNow;
                entity.Id = Guid.NewGuid().ToString();
                entity.CreatedUtc = created;
                entity.LastModifiedUtc = created;

                this.storage.Users.Add(entity.Id, entity);

                return Task.FromResult<Resource>(ScimUserMapper.ToScim(entity));
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

                if (this.storage.Users.ContainsKey(identifier))
                {
                    this.storage.Users.Remove(identifier);
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

                IEnumerable<UserEntity> matches;
                var predicate = PredicateBuilder.False<UserEntity>();
                Expression<Func<UserEntity, bool>> predicateAnd;

                if (parameters.AlternateFilters.Count <= 0)
                {
                    matches = this.storage.Users.Values;
                }
                else
                {
                    foreach (IFilter queryFilter in parameters.AlternateFilters)
                    {
                        predicateAnd = PredicateBuilder.True<UserEntity>();

                        IFilter andFilter = queryFilter;
                        IFilter currentFilter = andFilter;
                        do
                        {
                            if (string.IsNullOrWhiteSpace(andFilter.AttributePath))
                            {
                                throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidParameters);
                            }

                            else if (string.IsNullOrWhiteSpace(andFilter.ComparisonValue))
                            {
                                throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidParameters);
                            }

                            // The filter names a SCIM attribute; the predicate names a column.
                            // Translating here is what keeps the store ignorant of SCIM.
                            else if (andFilter.AttributePath.Equals(AttributeNames.UserName, StringComparison.OrdinalIgnoreCase))
                            {
                                string userName = andFilter.ComparisonValue;

                                // eq, co and sw. Entra ID looks a user up by userName before
                                // deciding whether to create them, and uses all three.
                                switch (andFilter.FilterOperator)
                                {
                                    case ComparisonOperator.Equals:
                                        predicateAnd = predicateAnd.And(p => string.Equals(p.UserName, userName, StringComparison.OrdinalIgnoreCase));
                                        break;

                                    case ComparisonOperator.Contains:
                                        predicateAnd = predicateAnd.And(p => p.UserName != null && p.UserName.IndexOf(userName, StringComparison.OrdinalIgnoreCase) >= 0);
                                        break;

                                    case ComparisonOperator.StartsWith:
                                        predicateAnd = predicateAnd.And(p => p.UserName != null && p.UserName.StartsWith(userName, StringComparison.OrdinalIgnoreCase));
                                        break;

                                    case ComparisonOperator.EndsWith:
                                        predicateAnd = predicateAnd.And(p => p.UserName != null && p.UserName.EndsWith(userName, StringComparison.OrdinalIgnoreCase));
                                        break;

                                    case ComparisonOperator.NotEquals:
                                        predicateAnd = predicateAnd.And(p => !string.Equals(p.UserName, userName, StringComparison.OrdinalIgnoreCase));
                                        break;

                                    default:
                                        throw new NotSupportedException(
                                            string.Format(SystemForCrossDomainIdentityManagementServiceResources.ExceptionFilterOperatorNotSupportedTemplate, andFilter.FilterOperator));
                                }
                            }

                            // ExternalId filter
                            else if (andFilter.AttributePath.Equals(AttributeNames.ExternalIdentifier, StringComparison.OrdinalIgnoreCase))
                            {
                                if (andFilter.FilterOperator != ComparisonOperator.Equals)
                                {
                                    throw new NotSupportedException(
                                        string.Format(SystemForCrossDomainIdentityManagementServiceResources.ExceptionFilterOperatorNotSupportedTemplate, andFilter.FilterOperator));
                                }

                                string externalIdentifier = andFilter.ComparisonValue;
                                predicateAnd = predicateAnd.And(p => string.Equals(p.ExternalId, externalIdentifier, StringComparison.OrdinalIgnoreCase));
                            }

                            //Active Filter
                            else if (andFilter.AttributePath.Equals(AttributeNames.Active, StringComparison.OrdinalIgnoreCase))
                            {
                                if (andFilter.FilterOperator != ComparisonOperator.Equals)
                                {
                                    throw new NotSupportedException(
                                        string.Format(SystemForCrossDomainIdentityManagementServiceResources.ExceptionFilterOperatorNotSupportedTemplate, andFilter.FilterOperator));
                                }

                                bool active = bool.Parse(andFilter.ComparisonValue);
                                predicateAnd = predicateAnd.And(p => p.IsActive == active);
                            }

                            //LastModified filter
                            else if (andFilter.AttributePath.Equals($"{AttributeNames.Metadata}.{AttributeNames.LastModified}", StringComparison.OrdinalIgnoreCase))
                            {
                                if (andFilter.FilterOperator == ComparisonOperator.GreaterThan)
                                {
                                    DateTime comparisonValue = DateTime.Parse(andFilter.ComparisonValue).ToUniversalTime();
                                    predicateAnd = predicateAnd.And(p => p.LastModifiedUtc > comparisonValue);
                                }
                                else if (andFilter.FilterOperator == ComparisonOperator.LessThan)
                                {
                                    DateTime comparisonValue = DateTime.Parse(andFilter.ComparisonValue).ToUniversalTime();
                                    predicateAnd = predicateAnd.And(p => p.LastModifiedUtc < comparisonValue);
                                }
                                else if (andFilter.FilterOperator == ComparisonOperator.EqualOrGreaterThan)
                                {
                                    DateTime comparisonValue = DateTime.Parse(andFilter.ComparisonValue).ToUniversalTime();
                                    predicateAnd = predicateAnd.And(p => p.LastModifiedUtc >= comparisonValue);
                                }
                                else if (andFilter.FilterOperator == ComparisonOperator.EqualOrLessThan)
                                {
                                    DateTime comparisonValue = DateTime.Parse(andFilter.ComparisonValue).ToUniversalTime();
                                    predicateAnd = predicateAnd.And(p => p.LastModifiedUtc <= comparisonValue);
                                }
                                else
                                    throw new NotSupportedException(
                                        string.Format(SystemForCrossDomainIdentityManagementServiceResources.ExceptionFilterOperatorNotSupportedTemplate, andFilter.FilterOperator));
                            }
                            else
                                throw new NotSupportedException(
                                    string.Format(SystemForCrossDomainIdentityManagementServiceResources.ExceptionFilterAttributePathNotSupportedTemplate, andFilter.AttributePath));

                            currentFilter = andFilter;
                            andFilter = andFilter.AdditionalFilter;

                        } while (currentFilter.AdditionalFilter != null);

                        predicate = predicate.Or(predicateAnd);
                    }

                    matches = this.storage.Users.Values.Where(predicate.Compile());
                }

                // Every match, not a page: ProviderBase.PaginateQueryAsync applies startIndex and
                // count, and needs the full match count to report totalResults. Paging here as well
                // reported the page size as the total, and ignored startIndex entirely.
                Resource[] results =
                    matches.Select((UserEntity entity) => (Resource)ScimUserMapper.ToScim(entity)).ToArray();

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

                Core2EnterpriseUser user = resource as Core2EnterpriseUser;

                if (string.IsNullOrWhiteSpace(user?.UserName))
                {
                    throw new HttpResponseException(HttpStatusCode.BadRequest);
                }

                if
                (
                    this.storage.Users.Values.Any(
                        (UserEntity existing) =>
                            string.Equals(existing.UserName, user.UserName, StringComparison.Ordinal) &&
                            !string.Equals(existing.Id, user.Identifier, StringComparison.OrdinalIgnoreCase))
                )
                {
                    throw new HttpResponseException(HttpStatusCode.Conflict);
                }

                if (!this.storage.Users.TryGetValue(user.Identifier, out UserEntity stored))
                {
                    throw new HttpResponseException(HttpStatusCode.NotFound);
                }

                // RFC 7644 3.5.1: a replace, so the entity is built from the request alone and
                // an attribute the body omits is cleared. Only what the store owns survives.
                UserEntity replacement = ScimUserMapper.ToEntity(user);
                replacement.Id = stored.Id;
                replacement.CreatedUtc = stored.CreatedUtc;
                replacement.LastModifiedUtc = DateTime.UtcNow;

                this.storage.Users[replacement.Id] = replacement;

                return Task.FromResult<Resource>(ScimUserMapper.ToScim(replacement));
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

                if (this.storage.Users.TryGetValue(identifier, out UserEntity entity))
                {
                    return Task.FromResult<Resource>(ScimUserMapper.ToScim(entity));
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
                    throw new ArgumentException(string.Format(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidOperation));
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

                if (!this.storage.Users.TryGetValue(patch.ResourceIdentifier.Identifier, out UserEntity stored))
                {
                    throw new HttpResponseException(HttpStatusCode.NotFound);
                }

                // A PATCH is expressed against the resource, so the stored row is projected back
                // into one, patched, and mapped in again. RFC 7644 3.5.2 wants all of the
                // operations or none: the projection is a separate object, so a failure part-way
                // through throws before the assignment below and the stored row is untouched.
                Core2EnterpriseUser candidate = ScimUserMapper.ToScim(stored);
                candidate.Apply(patchRequest);

                UserEntity patched = ScimUserMapper.ToEntity(candidate);
                patched.Id = stored.Id;
                patched.CreatedUtc = stored.CreatedUtc;
                patched.LastModifiedUtc = DateTime.UtcNow;

                this.storage.Users[patched.Id] = patched;

                return Task.CompletedTask;
            }
        }
    }
}
