// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Scim.EduPass
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Threading.Tasks;
    using System.Web.Http;
    using Microsoft.SCIM;
    using Newtonsoft.Json;

    /// <summary>
    /// An in-memory <see cref="IProvider"/> over <see cref="EduPassUser"/> and
    /// <see cref="Core2Group"/>, holding the relationship between the two.
    /// </summary>
    /// <remarks>
    /// A reference for relying parties, and the harness the Edupass conformance runs execute
    /// against. It is deliberately more than a store: the specification places obligations on the
    /// provider that no shared library can discharge, because only the provider knows how users
    /// and groups relate. Those obligations are, in one place here:
    ///
    /// - the core <c>groups</c> attribute is projected onto every user that is read, which
    ///   Edupass requires of a relying party whose roles it manages;
    /// - deleting a group removes the application role it encodes from everyone who held it;
    /// - deleting a user removes them from every group that listed them, so no membership is
    ///   left pointing at a resource that is gone;
    /// - a membership naming an identifier that resolves to no user is refused, rather than
    ///   stored and returned to Edupass on the next read.
    ///
    /// Membership is held once, on the group. The user's <c>groups</c> is derived from it on
    /// read, so the two cannot disagree and the first three obligations above are structural
    /// rather than bookkeeping a caller has to remember.
    ///
    /// State is per-instance and lives only as long as the process.
    /// </remarks>
    public class InMemoryEduPassProvider : ProviderBase
    {
        private readonly IDictionary<string, EduPassUser> users =
            new Dictionary<string, EduPassUser>(StringComparer.OrdinalIgnoreCase);

        private readonly IDictionary<string, Core2Group> groups =
            new Dictionary<string, Core2Group>(StringComparer.OrdinalIgnoreCase);

        private readonly object synchronization = new object();

        private readonly bool requireUinFin;

        /// <param name="requireUinFin">
        /// Whether this relying party stores UIN/FIN. Governs both validation and what
        /// <see cref="Schema"/> advertises, so that the two cannot drift apart.
        /// </param>
        public InMemoryEduPassProvider(bool requireUinFin = false)
        {
            this.requireUinFin = requireUinFin;
        }

        public override IReadOnlyCollection<Core2ResourceType> ResourceTypes
        {
            get
            {
                return
                    base
                    .ResourceTypes
                    .Where(
                        (Core2ResourceType item) =>
                            !string.Equals(item.Identifier, Types.User, StringComparison.OrdinalIgnoreCase))
                    .Concat(new[] { EduPassTypeSchemes.CreateUserResourceType() })
                    .ToArray();
            }
        }

        public override IReadOnlyCollection<TypeScheme> Schema
        {
            get
            {
                return
                    base
                    .Schema
                    .Concat(new[] { EduPassTypeSchemes.CreateUserExtensionTypeScheme(this.requireUinFin) })
                    .ToArray();
            }
        }

        public override Task<Resource> CreateAsync(Resource resource, string correlationIdentifier)
        {
            if (null == resource)
            {
                throw new ArgumentNullException(nameof(resource));
            }

            lock (this.synchronization)
            {
                switch (resource)
                {
                    case EduPassUser user:
                        EduPassValidator.Validate(user, this.requireUinFin);

                        if (this.users.Values.Any(
                                (EduPassUser item) =>
                                    string.Equals(item.UserName, user.UserName, StringComparison.OrdinalIgnoreCase)))
                        {
                            throw new HttpResponseException(HttpStatusCode.Conflict);
                        }

                        return Task.FromResult(this.Store(user, user.Metadata, this.users));

                    case Core2Group group:
                        InMemoryEduPassProvider.RequireDisplayName(group);

                        if (this.groups.Values.Any(
                                (Core2Group item) =>
                                    string.Equals(item.DisplayName, group.DisplayName, StringComparison.OrdinalIgnoreCase)))
                        {
                            // The specification names displayName as the application role and
                            // requires a duplicate to be refused.
                            throw new HttpResponseException(HttpStatusCode.Conflict);
                        }

                        this.RequireResolvableMembers(group);

                        return Task.FromResult(this.Store(group, group.Metadata, this.groups));

                    default:
                        throw new NotSupportedException(resource.GetType().FullName);
                }
            }
        }

        public override Task DeleteAsync(IResourceIdentifier resourceIdentifier, string correlationIdentifier)
        {
            string identifier = InMemoryEduPassProvider.RequireIdentifier(resourceIdentifier);

            lock (this.synchronization)
            {
                if (this.IsUserRequest(resourceIdentifier.SchemaIdentifier))
                {
                    if (!this.users.Remove(identifier))
                    {
                        throw new HttpResponseException(HttpStatusCode.NotFound);
                    }

                    // Otherwise every group that listed the user keeps a membership pointing at a
                    // resource that no longer exists, and hands it back on the next read.
                    foreach (Core2Group group in this.groups.Values)
                    {
                        InMemoryEduPassProvider.Exclude(group, identifier);
                    }

                    return Task.FromResult(0);
                }

                // Deleting a group removes the application role it encodes from every member.
                // Because membership is held only on the group, dropping it is that removal.
                if (!this.groups.Remove(identifier))
                {
                    throw new HttpResponseException(HttpStatusCode.NotFound);
                }

                return Task.FromResult(0);
            }
        }

        public override Task<Resource> RetrieveAsync(
            IResourceRetrievalParameters parameters,
            string correlationIdentifier)
        {
            if (null == parameters)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            string identifier = InMemoryEduPassProvider.RequireIdentifier(parameters.ResourceIdentifier);

            lock (this.synchronization)
            {
                if (this.IsUserRequest(parameters.SchemaIdentifier))
                {
                    if (!this.users.TryGetValue(identifier, out EduPassUser user))
                    {
                        throw new HttpResponseException(HttpStatusCode.NotFound);
                    }

                    return Task.FromResult<Resource>(this.Project(user));
                }

                if (!this.groups.TryGetValue(identifier, out Core2Group group))
                {
                    throw new HttpResponseException(HttpStatusCode.NotFound);
                }

                return Task.FromResult<Resource>(group);
            }
        }

        public override Task<Resource[]> QueryAsync(IQueryParameters parameters, string correlationIdentifier)
        {
            if (null == parameters)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            if (null == parameters.AlternateFilters)
            {
                throw new ArgumentException(nameof(parameters));
            }

            IFilter filter = parameters.AlternateFilters.SingleOrDefault();

            lock (this.synchronization)
            {
                if (this.IsUserRequest(parameters.SchemaIdentifier))
                {
                    IEnumerable<EduPassUser> matches = this.users.Values;

                    if (null != filter)
                    {
                        string comparison = InMemoryEduPassProvider.RequireEqualityFilter(filter);

                        // Edupass requires eq on userName only. externalId is accepted too
                        // because the reference provider has always supported it.
                        if (AttributeNames.UserName.Equals(filter.AttributePath, StringComparison.OrdinalIgnoreCase))
                        {
                            matches = matches.Where(
                                (EduPassUser item) =>
                                    string.Equals(item.UserName, comparison, StringComparison.OrdinalIgnoreCase));
                        }
                        else if (AttributeNames.ExternalIdentifier.Equals(
                                     filter.AttributePath,
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            matches = matches.Where(
                                (EduPassUser item) =>
                                    string.Equals(item.ExternalIdentifier, comparison, StringComparison.OrdinalIgnoreCase));
                        }
                        else
                        {
                            throw new NotSupportedException(filter.AttributePath);
                        }
                    }

                    // Every match, not a page: ProviderBase.PaginateQueryAsync applies startIndex
                    // and count, and needs the full count to report totalResults.
                    return Task.FromResult(
                        matches.Select((EduPassUser item) => (Resource)this.Project(item)).ToArray());
                }

                IEnumerable<Core2Group> found = this.groups.Values;

                if (null != filter)
                {
                    string comparison = InMemoryEduPassProvider.RequireEqualityFilter(filter);

                    if (!AttributeNames.DisplayName.Equals(filter.AttributePath, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new NotSupportedException(filter.AttributePath);
                    }

                    found = found.Where(
                        (Core2Group item) =>
                            string.Equals(item.DisplayName, comparison, StringComparison.OrdinalIgnoreCase));
                }

                return Task.FromResult(found.Select((Core2Group item) => (Resource)item).ToArray());
            }
        }

        public override Task<Resource> ReplaceAsync(Resource resource, string correlationIdentifier)
        {
            if (null == resource)
            {
                throw new ArgumentNullException(nameof(resource));
            }

            if (string.IsNullOrWhiteSpace(resource.Identifier))
            {
                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }

            lock (this.synchronization)
            {
                switch (resource)
                {
                    case EduPassUser user:
                        EduPassValidator.Validate(user, this.requireUinFin);

                        if (!this.users.TryGetValue(user.Identifier, out EduPassUser replacedUser))
                        {
                            throw new HttpResponseException(HttpStatusCode.NotFound);
                        }

                        if (this.users.Values.Any(
                                (EduPassUser item) =>
                                    string.Equals(item.UserName, user.UserName, StringComparison.OrdinalIgnoreCase)
                                    && !string.Equals(item.Identifier, user.Identifier, StringComparison.OrdinalIgnoreCase)))
                        {
                            throw new HttpResponseException(HttpStatusCode.Conflict);
                        }

                        user.Metadata.Created = replacedUser.Metadata.Created;
                        user.Metadata.LastModified = DateTime.UtcNow;
                        this.users[user.Identifier] = user;

                        return Task.FromResult<Resource>(this.Project(user));

                    case Core2Group group:
                        InMemoryEduPassProvider.RequireDisplayName(group);

                        if (!this.groups.TryGetValue(group.Identifier, out Core2Group replacedGroup))
                        {
                            throw new HttpResponseException(HttpStatusCode.NotFound);
                        }

                        if (this.groups.Values.Any(
                                (Core2Group item) =>
                                    string.Equals(item.DisplayName, group.DisplayName, StringComparison.OrdinalIgnoreCase)
                                    && !string.Equals(item.Identifier, group.Identifier, StringComparison.OrdinalIgnoreCase)))
                        {
                            throw new HttpResponseException(HttpStatusCode.Conflict);
                        }

                        this.RequireResolvableMembers(group);

                        group.Metadata.Created = replacedGroup.Metadata.Created;
                        group.Metadata.LastModified = DateTime.UtcNow;
                        this.groups[group.Identifier] = group;

                        return Task.FromResult<Resource>(group);

                    default:
                        throw new NotSupportedException(resource.GetType().FullName);
                }
            }
        }

        public override Task UpdateAsync(IPatch patch, string correlationIdentifier)
        {
            if (null == patch)
            {
                throw new ArgumentNullException(nameof(patch));
            }

            string identifier = InMemoryEduPassProvider.RequireIdentifier(patch.ResourceIdentifier);

            if (!(patch.PatchRequest is PatchRequest2 request))
            {
                throw new ArgumentException(nameof(patch));
            }

            lock (this.synchronization)
            {
                // Patched on a copy and committed only once it validates. Applying to the stored
                // resource and validating afterwards left a rejected request's changes in place,
                // which is the opposite of the atomicity the specification requires.
                if (this.IsUserRequest(patch.ResourceIdentifier.SchemaIdentifier))
                {
                    if (!this.users.TryGetValue(identifier, out EduPassUser user))
                    {
                        throw new HttpResponseException(HttpStatusCode.NotFound);
                    }

                    EduPassUser patchedUser = InMemoryEduPassProvider.Copy(user);
                    patchedUser.Apply(request);
                    EduPassValidator.Validate(patchedUser, this.requireUinFin);

                    patchedUser.Metadata.Created = user.Metadata.Created;
                    patchedUser.Metadata.LastModified = DateTime.UtcNow;
                    this.users[identifier] = patchedUser;

                    return Task.FromResult(0);
                }

                if (!this.groups.TryGetValue(identifier, out Core2Group group))
                {
                    throw new HttpResponseException(HttpStatusCode.NotFound);
                }

                Core2Group patchedGroup = InMemoryEduPassProvider.Copy(group);
                patchedGroup.Apply(request);
                InMemoryEduPassProvider.RequireDisplayName(patchedGroup);
                this.RequireResolvableMembers(patchedGroup);

                if (this.groups.Values.Any(
                        (Core2Group item) =>
                            string.Equals(item.DisplayName, patchedGroup.DisplayName, StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(item.Identifier, identifier, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new HttpResponseException(HttpStatusCode.Conflict);
                }

                patchedGroup.Metadata.Created = group.Metadata.Created;
                patchedGroup.Metadata.LastModified = DateTime.UtcNow;
                this.groups[identifier] = patchedGroup;

                return Task.FromResult(0);
            }
        }

        /// <summary>
        /// Returns the user with its <c>groups</c> attribute derived from group membership.
        /// </summary>
        private EduPassUser Project(EduPassUser user)
        {
            UserGroup[] memberships =
                this
                .groups
                .Values
                .Where(
                    (Core2Group group) =>
                        group.Members != null
                        && group.Members.Any(
                            (Member member) =>
                                string.Equals(member.Value, user.Identifier, StringComparison.OrdinalIgnoreCase)))
                .Select(
                    (Core2Group group) =>
                        new UserGroup
                        {
                            Value = group.Identifier,
                            Display = group.DisplayName,
                        })
                .ToArray();

            user.Groups = memberships.Length > 0 ? memberships : null;

            return user;
        }

        private Resource Store<T>(T resource, Core2Metadata metadata, IDictionary<string, T> store)
            where T : Resource
        {
            if (null != resource.Identifier)
            {
                // RFC 7644 section 3.3: the server assigns the identifier.
                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }

            DateTime created = DateTime.UtcNow;
            metadata.Created = created;
            metadata.LastModified = created;
            resource.Identifier = Guid.NewGuid().ToString();

            store.Add(resource.Identifier, resource);

            return resource;
        }

        /// <summary>
        /// Refuses a membership naming an identifier that resolves to no user.
        /// </summary>
        private void RequireResolvableMembers(Core2Group group)
        {
            if (null == group.Members)
            {
                return;
            }

            foreach (Member member in group.Members)
            {
                if (string.IsNullOrWhiteSpace(member.Value) || !this.users.ContainsKey(member.Value))
                {
                    throw new HttpResponseException(HttpStatusCode.BadRequest);
                }
            }
        }

        private bool IsUserRequest(string schemaIdentifier)
        {
            return !SchemaIdentifiers.Core2Group.Equals(schemaIdentifier, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A deep copy, so that a patch can be applied and validated before it is committed.
        /// </summary>
        /// <remarks>
        /// Round-tripped through Newtonsoft rather than through <c>Schematized.Serialize</c>: the
        /// latter uses <c>DataContractJsonSerializer</c>, which throws on a default
        /// <see cref="DateTime"/> - and a resource being copied need not have its metadata dates
        /// set yet.
        /// </remarks>
        private static T Copy<T>(T resource)
            where T : Schematized
        {
            return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(resource));
        }

        private static void Exclude(Core2Group group, string identifier)
        {
            if (null == group.Members)
            {
                return;
            }

            group.Members =
                group
                .Members
                .Where(
                    (Member member) =>
                        !string.Equals(member.Value, identifier, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        private static void RequireDisplayName(Core2Group group)
        {
            if (string.IsNullOrWhiteSpace(group.DisplayName))
            {
                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }

            if (group.DisplayName.Length > EduPassValidator.MaximumAttributeLength)
            {
                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }
        }

        private static string RequireIdentifier(IResourceIdentifier resourceIdentifier)
        {
            if (string.IsNullOrWhiteSpace(resourceIdentifier?.Identifier))
            {
                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }

            return resourceIdentifier.Identifier;
        }

        private static string RequireEqualityFilter(IFilter filter)
        {
            if (string.IsNullOrWhiteSpace(filter.AttributePath)
                || string.IsNullOrWhiteSpace(filter.ComparisonValue))
            {
                throw new ArgumentException(nameof(filter));
            }

            if (ComparisonOperator.Equals != filter.FilterOperator)
            {
                // Edupass requires eq and nothing else.
                throw new NotSupportedException(filter.FilterOperator.ToString());
            }

            return filter.ComparisonValue;
        }
    }
}
