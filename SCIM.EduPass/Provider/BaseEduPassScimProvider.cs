// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Scim.EduPass
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Threading.Tasks;
    using System.Web.Http;
    using Microsoft.SCIM;

    /// <summary>
    /// The Edupass SCIM behaviour a relying party has to provide, over an
    /// <see cref="IEduPassStore"/> that a derived class supplies.
    /// </summary>
    /// <remarks>
    /// The specification places obligations on the provider that no shared library can
    /// discharge, because only the provider knows how users and groups relate. Those
    /// obligations are, in one place here:
    ///
    /// - the core <c>groups</c> attribute is projected onto every user that is read, which
    ///   Edupass requires of a relying party whose roles it manages;
    /// - deleting a group removes the application role it encodes from everyone who held it;
    /// - deleting a user removes them from every group that listed them, so no membership is
    ///   left pointing at a resource that is gone;
    /// - a membership naming an identifier that resolves to no user is refused, rather than
    ///   stored and returned to Edupass on the next read.
    ///
    /// Membership is held once, on the group. A user's <c>groups</c> is derived from it on
    /// read, so the two cannot disagree and the first three obligations above are structural
    /// rather than bookkeeping a caller has to remember.
    ///
    /// Each operation runs inside one store instance and completes it only on success, so an
    /// operation that writes more than once either lands whole or not at all.
    /// </remarks>
    public abstract class BaseEduPassScimProvider : ProviderBase
    {
        private readonly bool requireUinFin;

        /// <param name="requireUinFin">
        /// Whether this relying party stores UIN/FIN. Governs both validation and what
        /// <see cref="Schema"/> advertises, so that the two cannot drift apart.
        /// </param>
        protected BaseEduPassScimProvider(bool requireUinFin = false)
        {
            this.requireUinFin = requireUinFin;
        }

        /// <remarks>
        /// User and Group both, because a relying party with Edupass-managed roles serves
        /// both endpoints. Only the base's own User entry is dropped, so that a derived
        /// provider adding resource types of its own keeps them.
        /// </remarks>
        public override IReadOnlyCollection<Core2ResourceType> ResourceTypes
        {
            get
            {
                return
                    base
                    .ResourceTypes
                    .Where(
                        (Core2ResourceType item) =>
                            !string.Equals(item.Identifier, Types.User, StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(item.Identifier, Types.Group, StringComparison.OrdinalIgnoreCase))
                    .Concat(
                        new[]
                        {
                            EduPassTypeSchemes.CreateUserResourceType(),
                            EduPassTypeSchemes.CreateGroupResourceType(),
                        })
                    .ToArray();
            }
        }

        /// <remarks>
        /// The two core schemas as well as the extension. Edupass reads <c>/Schemas</c> to
        /// learn what the relying party supports, and a payload carrying only the extension
        /// says the party supports no core attribute at all - not even userName, which the
        /// same specification names as required.
        /// </remarks>
        public override IReadOnlyCollection<TypeScheme> Schema
        {
            get
            {
                string[] replaced =
                    new[] { SchemaIdentifiers.Core2User, SchemaIdentifiers.Core2Group };

                return
                    base
                    .Schema
                    .Where(
                        (TypeScheme item) =>
                            !replaced.Contains(item.Identifier, StringComparer.OrdinalIgnoreCase))
                    .Concat(
                        new[]
                        {
                            EduPassTypeSchemes.CreateUserTypeScheme(),
                            EduPassTypeSchemes.CreateGroupTypeScheme(),
                            EduPassTypeSchemes.CreateUserExtensionTypeScheme(this.requireUinFin),
                        })
                    .ToArray();
            }
        }

        /// <summary>
        /// Opens the store for one operation. Called once per request.
        /// </summary>
        protected abstract Task<IEduPassStore> BeginAsync();

        public override async Task<Resource> CreateAsync(Resource resource, string correlationIdentifier)
        {
            if (null == resource)
            {
                throw new ArgumentNullException(nameof(resource));
            }

            using (IEduPassStore store = await this.BeginAsync().ConfigureAwait(false))
            {
                Resource created;

                switch (resource)
                {
                    case EduPassUser user:
                        EduPassValidator.Validate(user, this.requireUinFin);

                        if (null != await store.FindUserByUserNameAsync(user.UserName).ConfigureAwait(false))
                        {
                            // Typed, and with a detail. A bare conflict reaches Edupass as the
                            // single word "Conflict", which does not say which attribute
                            // collided - and userName and externalId are both unique here.
                            throw new ScimTypedException(
                                HttpStatusCode.Conflict,
                                ScimTypes.Uniqueness,
                                "Another user already holds this 'userName'.");
                        }

                        BaseEduPassScimProvider.Stamp(user, user.Metadata);

                        // groups is read-only and derived from Group membership, so whatever
                        // the client sent is discarded rather than stored.
                        user.Groups = null;

                        await store.AddUserAsync(user).ConfigureAwait(false);

                        // Projected, because the specification requires groups on the Create
                        // User response and because echoing the client's value back would
                        // report a role the party does not hold. A new identity holds none,
                        // so this is [] - which is the answer, not the absence of one.
                        created = await BaseEduPassScimProvider.ProjectAsync(store, user).ConfigureAwait(false);
                        break;

                    case Core2Group group:
                        BaseEduPassScimProvider.RequireDisplayName(group);

                        if (null != await store.FindGroupByDisplayNameAsync(group.DisplayName).ConfigureAwait(false))
                        {
                            // The specification names displayName as the application role and
                            // requires a duplicate to be refused.
                            throw new ScimTypedException(
                                HttpStatusCode.Conflict,
                                ScimTypes.Uniqueness,
                                "Another group already holds this 'displayName'. It encodes the "
                                + "application role, which is unique.");
                        }

                        await BaseEduPassScimProvider.RequireResolvableMembersAsync(store, group).ConfigureAwait(false);

                        BaseEduPassScimProvider.Stamp(group, group.Metadata);
                        await store.AddGroupAsync(group).ConfigureAwait(false);
                        created = group;
                        break;

                    default:
                        throw new NotSupportedException(resource.GetType().FullName);
                }

                await store.CompleteAsync().ConfigureAwait(false);

                return created;
            }
        }

        public override async Task DeleteAsync(IResourceIdentifier resourceIdentifier, string correlationIdentifier)
        {
            string identifier = BaseEduPassScimProvider.RequireIdentifier(resourceIdentifier);

            using (IEduPassStore store = await this.BeginAsync().ConfigureAwait(false))
            {
                if (BaseEduPassScimProvider.IsUserRequest(resourceIdentifier.SchemaIdentifier))
                {
                    if (!await store.RemoveUserAsync(identifier).ConfigureAwait(false))
                    {
                        throw new HttpResponseException(HttpStatusCode.NotFound);
                    }

                    // Otherwise every group that listed the user keeps a membership pointing at a
                    // resource that no longer exists, and hands it back on the next read.
                    await store.RemoveMembershipsAsync(identifier).ConfigureAwait(false);
                }
                else
                {
                    // Deleting a group removes the application role it encodes from every member.
                    // Because membership is held only on the group, dropping it is that removal.
                    if (!await store.RemoveGroupAsync(identifier).ConfigureAwait(false))
                    {
                        throw new HttpResponseException(HttpStatusCode.NotFound);
                    }
                }

                await store.CompleteAsync().ConfigureAwait(false);
            }
        }

        public override async Task<Resource> RetrieveAsync(
            IResourceRetrievalParameters parameters,
            string correlationIdentifier)
        {
            if (null == parameters)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            string identifier = BaseEduPassScimProvider.RequireIdentifier(parameters.ResourceIdentifier);

            using (IEduPassStore store = await this.BeginAsync().ConfigureAwait(false))
            {
                if (BaseEduPassScimProvider.IsUserRequest(parameters.SchemaIdentifier))
                {
                    EduPassUser user = await store.FindUserAsync(identifier).ConfigureAwait(false);

                    if (null == user)
                    {
                        throw new HttpResponseException(HttpStatusCode.NotFound);
                    }

                    return await BaseEduPassScimProvider.ProjectAsync(store, user).ConfigureAwait(false);
                }

                Core2Group group = await store.FindGroupAsync(identifier).ConfigureAwait(false);

                if (null == group)
                {
                    throw new HttpResponseException(HttpStatusCode.NotFound);
                }

                return group;
            }
        }

        public override async Task<Resource[]> QueryAsync(IQueryParameters parameters, string correlationIdentifier)
        {
            if (null == parameters)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            if (null == parameters.AlternateFilters)
            {
                throw new ArgumentException(nameof(parameters));
            }

            IFilter filter = BaseEduPassScimProvider.RequireAtMostOneFilter(parameters.AlternateFilters);

            using (IEduPassStore store = await this.BeginAsync().ConfigureAwait(false))
            {
                if (BaseEduPassScimProvider.IsUserRequest(parameters.SchemaIdentifier))
                {
                    IReadOnlyCollection<EduPassUser> matches;

                    if (null != filter)
                    {
                        string comparison = BaseEduPassScimProvider.RequireEqualityFilter(filter);
                        EduPassUser match;

                        // Edupass requires eq on userName only. externalId is accepted too
                        // because the reference provider has always supported it.
                        if (AttributeNames.UserName.Equals(filter.AttributePath, StringComparison.OrdinalIgnoreCase))
                        {
                            match = await store.FindUserByUserNameAsync(comparison).ConfigureAwait(false);
                        }
                        else if (AttributeNames.ExternalIdentifier.Equals(
                                     filter.AttributePath,
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            match = await store.FindUserByExternalIdentifierAsync(comparison).ConfigureAwait(false);
                        }
                        else
                        {
                            throw new NotSupportedException(filter.AttributePath);
                        }

                        matches = null == match ? Array.Empty<EduPassUser>() : new[] { match };
                    }
                    else
                    {
                        // Every match, not a page: ProviderBase.PaginateQueryAsync applies
                        // startIndex and count, and needs the full count to report totalResults.
                        matches =
                            await store.ListUsersAsync().ConfigureAwait(false)
                            ?? (IReadOnlyCollection<EduPassUser>)Array.Empty<EduPassUser>();
                    }

                    return await BaseEduPassScimProvider.ProjectAsync(store, matches).ConfigureAwait(false);
                }

                IReadOnlyCollection<Core2Group> found;

                if (null != filter)
                {
                    string comparison = BaseEduPassScimProvider.RequireEqualityFilter(filter);

                    if (!AttributeNames.DisplayName.Equals(filter.AttributePath, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new NotSupportedException(filter.AttributePath);
                    }

                    Core2Group match = await store.FindGroupByDisplayNameAsync(comparison).ConfigureAwait(false);
                    found = null == match ? Array.Empty<Core2Group>() : new[] { match };
                }
                else
                {
                    found =
                        await store.ListGroupsAsync().ConfigureAwait(false)
                        ?? (IReadOnlyCollection<Core2Group>)Array.Empty<Core2Group>();
                }

                return found.Select((Core2Group item) => (Resource)item).ToArray();
            }
        }

        public override async Task<Resource> ReplaceAsync(Resource resource, string correlationIdentifier)
        {
            if (null == resource)
            {
                throw new ArgumentNullException(nameof(resource));
            }

            if (string.IsNullOrWhiteSpace(resource.Identifier))
            {
                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }

            using (IEduPassStore store = await this.BeginAsync().ConfigureAwait(false))
            {
                Resource replacement;

                switch (resource)
                {
                    case EduPassUser user:
                        EduPassValidator.Validate(user, this.requireUinFin);

                        EduPassUser replacedUser = await store.FindUserAsync(user.Identifier).ConfigureAwait(false);

                        if (null == replacedUser)
                        {
                            throw new HttpResponseException(HttpStatusCode.NotFound);
                        }

                        EduPassUser duplicateUser =
                            await store.FindUserByUserNameAsync(user.UserName).ConfigureAwait(false);

                        if (null != duplicateUser
                            && !string.Equals(
                                    duplicateUser.Identifier,
                                    user.Identifier,
                                    StringComparison.OrdinalIgnoreCase))
                        {
                            throw new ScimTypedException(
                                HttpStatusCode.Conflict,
                                ScimTypes.Uniqueness,
                                "Another user already holds this 'userName'.");
                        }

                        user.Metadata.Created = replacedUser.Metadata.Created;
                        user.Metadata.LastModified = DateTime.UtcNow;
                        await store.ReplaceUserAsync(user).ConfigureAwait(false);

                        replacement = await BaseEduPassScimProvider.ProjectAsync(store, user).ConfigureAwait(false);
                        break;

                    case Core2Group group:
                        BaseEduPassScimProvider.RequireDisplayName(group);

                        Core2Group replacedGroup = await store.FindGroupAsync(group.Identifier).ConfigureAwait(false);

                        if (null == replacedGroup)
                        {
                            throw new HttpResponseException(HttpStatusCode.NotFound);
                        }

                        BaseEduPassScimProvider.RequireUnchangedDisplayName(replacedGroup, group);

                        await BaseEduPassScimProvider.RequireResolvableMembersAsync(store, group).ConfigureAwait(false);

                        group.Metadata.Created = replacedGroup.Metadata.Created;
                        group.Metadata.LastModified = DateTime.UtcNow;
                        await store.ReplaceGroupAsync(group).ConfigureAwait(false);

                        replacement = group;
                        break;

                    default:
                        throw new NotSupportedException(resource.GetType().FullName);
                }

                await store.CompleteAsync().ConfigureAwait(false);

                return replacement;
            }
        }

        public override async Task UpdateAsync(IPatch patch, string correlationIdentifier)
        {
            if (null == patch)
            {
                throw new ArgumentNullException(nameof(patch));
            }

            string identifier = BaseEduPassScimProvider.RequireIdentifier(patch.ResourceIdentifier);

            if (!(patch.PatchRequest is PatchRequest2 request))
            {
                throw new ArgumentException(nameof(patch));
            }

            using (IEduPassStore store = await this.BeginAsync().ConfigureAwait(false))
            {
                // Patched on a copy and committed only once it validates. Applying to the stored
                // resource and validating afterwards left a rejected request's changes in place,
                // which is the opposite of the atomicity the specification requires.
                if (BaseEduPassScimProvider.IsUserRequest(patch.ResourceIdentifier.SchemaIdentifier))
                {
                    EduPassUser user = await store.FindUserAsync(identifier).ConfigureAwait(false);

                    if (null == user)
                    {
                        throw new HttpResponseException(HttpStatusCode.NotFound);
                    }

                    EduPassUser patchedUser = ResourceCloner.Clone(user);
                    patchedUser.Apply(request);
                    EduPassValidator.Validate(patchedUser, this.requireUinFin);

                    patchedUser.Metadata.Created = user.Metadata.Created;
                    patchedUser.Metadata.LastModified = DateTime.UtcNow;
                    await store.ReplaceUserAsync(patchedUser).ConfigureAwait(false);
                }
                else
                {
                    Core2Group group = await store.FindGroupAsync(identifier).ConfigureAwait(false);

                    if (null == group)
                    {
                        throw new HttpResponseException(HttpStatusCode.NotFound);
                    }

                    Core2Group patchedGroup = ResourceCloner.Clone(group);
                    patchedGroup.Apply(request);
                    BaseEduPassScimProvider.RequireDisplayName(patchedGroup);
                    BaseEduPassScimProvider.RequireUnchangedDisplayName(group, patchedGroup);
                    await BaseEduPassScimProvider
                        .RequireResolvableMembersAsync(store, patchedGroup)
                        .ConfigureAwait(false);

                    patchedGroup.Metadata.Created = group.Metadata.Created;
                    patchedGroup.Metadata.LastModified = DateTime.UtcNow;
                    await store.ReplaceGroupAsync(patchedGroup).ConfigureAwait(false);
                }

                await store.CompleteAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Returns the user with its <c>groups</c> attribute derived from group membership.
        /// </summary>
        private static async Task<EduPassUser> ProjectAsync(IEduPassStore store, EduPassUser user)
        {
            IReadOnlyCollection<UserGroup> memberships =
                await store.FindMembershipsAsync(user.Identifier).ConfigureAwait(false);

            // An empty array, not null. The specification's own Create User example carries
            // "groups": [], and for a relying party with Edupass-managed roles an absent
            // attribute is a different answer from "this identity holds no role yet".
            user.Groups =
                null != memberships ? memberships.ToArray() : Array.Empty<UserGroup>();

            return user;
        }

        private static async Task<Resource[]> ProjectAsync(
            IEduPassStore store,
            IReadOnlyCollection<EduPassUser> users)
        {
            if (0 == users.Count)
            {
                return Array.Empty<Resource>();
            }

            // One query for the whole result set rather than one per user.
            IReadOnlyDictionary<string, IReadOnlyCollection<UserGroup>> memberships =
                await store
                    .FindMembershipsAsync(users.Select((EduPassUser item) => item.Identifier).ToArray())
                    .ConfigureAwait(false);

            foreach (EduPassUser user in users)
            {
                IReadOnlyCollection<UserGroup> held = null;

                if (null != memberships)
                {
                    memberships.TryGetValue(user.Identifier, out held);
                }

                user.Groups = null != held ? held.ToArray() : Array.Empty<UserGroup>();
            }

            return users.Select((EduPassUser item) => (Resource)item).ToArray();
        }

        /// <summary>
        /// Assigns the identifier and creation metadata the service is responsible for.
        /// </summary>
        private static void Stamp(Resource resource, Core2Metadata metadata)
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
        }

        /// <summary>
        /// Refuses a membership naming an identifier that resolves to no user.
        /// </summary>
        private static async Task RequireResolvableMembersAsync(IEduPassStore store, Core2Group group)
        {
            if (null == group.Members)
            {
                return;
            }

            string[] named = group.Members.Select((Member member) => member.Value).ToArray();

            if (named.Any(string.IsNullOrWhiteSpace))
            {
                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }

            if (0 == named.Length)
            {
                return;
            }

            IReadOnlyCollection<string> unresolved =
                await store.FindUnresolvedUsersAsync(named).ConfigureAwait(false);

            if (null != unresolved && unresolved.Count > 0)
            {
                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }
        }

        private static bool IsUserRequest(string schemaIdentifier)
        {
            return !SchemaIdentifiers.Core2Group.Equals(schemaIdentifier, StringComparison.OrdinalIgnoreCase);
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

        /// <summary>
        /// Refuses a write that would change an existing Group's <c>displayName</c>.
        /// </summary>
        /// <remarks>
        /// <c>displayName</c> is the application role, and <see cref="EduPassTypeSchemes"/>
        /// advertises it as <c>immutable</c>. Edupass creates a Group per role and deletes it
        /// when the role is deprecated; it never renames one. Enforcing it here is what keeps
        /// the advertisement honest - a client that read <c>/Schemas</c> and believed it would
        /// otherwise find the name changing underneath it.
        ///
        /// Ordinal, because the attribute is also advertised <c>caseExact</c>: a change of case
        /// is a change.
        /// </remarks>
        private static void RequireUnchangedDisplayName(Core2Group existing, Core2Group candidate)
        {
            if (string.Equals(existing.DisplayName, candidate.DisplayName, StringComparison.Ordinal))
            {
                return;
            }

            throw new ScimTypedException(
                HttpStatusCode.BadRequest,
                ScimTypes.Mutability,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The attribute 'displayName' is immutable and cannot be changed from '{0}' to '{1}'.",
                    existing.DisplayName,
                    candidate.DisplayName));
        }

        private static string RequireIdentifier(IResourceIdentifier resourceIdentifier)
        {
            if (string.IsNullOrWhiteSpace(resourceIdentifier?.Identifier))
            {
                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }

            return resourceIdentifier.Identifier;
        }

        /// <summary>
        /// Reduces the parsed filter to the single equality comparison Edupass sends, refusing
        /// anything this provider cannot evaluate in full.
        /// </summary>
        /// <remarks>
        /// Edupass only ever filters on <c>userName eq</c> or <c>displayName eq</c>, so a
        /// single comparison is all this provider implements. What it must not do is accept a
        /// filter it only partly understands: taking the first comparison of
        /// <c>userName eq "x" and title eq "y"</c> and ignoring the rest returns a resource
        /// that does not match what was asked for, and the caller has no way to tell.
        /// A narrower answer than requested is a wrong answer, so it is refused instead.
        /// </remarks>
        private static IFilter RequireAtMostOneFilter(IReadOnlyCollection<IFilter> filters)
        {
            if (null == filters || 0 == filters.Count)
            {
                return null;
            }

            IFilter filter = filters.First();

            // More than one alternate is an "or"; a chained AdditionalFilter is an "and".
            if (filters.Count > 1 || null != filter.AdditionalFilter)
            {
                throw new ScimTypedException(
                    HttpStatusCode.BadRequest,
                    ScimTypes.InvalidFilter,
                    "This endpoint supports a single 'eq' comparison on one attribute.");
            }

            return filter;
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
