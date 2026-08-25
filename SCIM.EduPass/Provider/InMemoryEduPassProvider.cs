// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Scim.EduPass
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.SCIM;

    /// <summary>
    /// A <see cref="BaseEduPassScimProvider"/> over dictionaries.
    /// </summary>
    /// <remarks>
    /// A reference for relying parties, and the harness the Edupass conformance runs execute
    /// against. State is per-instance and lives only as long as the process.
    /// </remarks>
    public class InMemoryEduPassProvider : BaseEduPassScimProvider
    {
        private readonly IDictionary<string, EduPassUser> users =
            new Dictionary<string, EduPassUser>(StringComparer.OrdinalIgnoreCase);

        private readonly IDictionary<string, Core2Group> groups =
            new Dictionary<string, Core2Group>(StringComparer.OrdinalIgnoreCase);

        // Not a monitor: the provider holds the store across awaits, and a monitor can only be
        // released by the thread that took it.
        private readonly SemaphoreSlim synchronization = new SemaphoreSlim(1, 1);

        public InMemoryEduPassProvider(bool requireUinFin = false)
            : base(requireUinFin)
        {
        }

        protected override async Task<IEduPassStore> BeginAsync()
        {
            await this.synchronization.WaitAsync().ConfigureAwait(false);

            return new Store(this.users, this.groups, this.synchronization);
        }

        /// <remarks>
        /// Writes are buffered and applied by <see cref="CompleteAsync"/>, so an operation that
        /// fails part way leaves nothing behind - the same guarantee a transaction gives the
        /// database implementations of this interface.
        /// </remarks>
        private sealed class Store : IEduPassStore
        {
            private readonly IDictionary<string, EduPassUser> users;
            private readonly IDictionary<string, Core2Group> groups;
            private readonly SemaphoreSlim synchronization;
            private readonly List<Action> pending = new List<Action>();

            private bool released;

            public Store(
                IDictionary<string, EduPassUser> users,
                IDictionary<string, Core2Group> groups,
                SemaphoreSlim synchronization)
            {
                this.users = users;
                this.groups = groups;
                this.synchronization = synchronization;
            }

            public Task<EduPassUser> FindUserAsync(string identifier)
            {
                this.users.TryGetValue(identifier, out EduPassUser user);

                return Task.FromResult(user);
            }

            public Task<EduPassUser> FindUserByUserNameAsync(string userName)
            {
                return Task.FromResult(
                    this.users.Values.FirstOrDefault(
                        (EduPassUser item) =>
                            string.Equals(item.UserName, userName, StringComparison.OrdinalIgnoreCase)));
            }

            public Task<EduPassUser> FindUserByExternalIdentifierAsync(string externalIdentifier)
            {
                return Task.FromResult(
                    this.users.Values.FirstOrDefault(
                        (EduPassUser item) =>
                            string.Equals(
                                item.ExternalIdentifier,
                                externalIdentifier,
                                StringComparison.OrdinalIgnoreCase)));
            }

            public Task<IReadOnlyCollection<EduPassUser>> ListUsersAsync()
            {
                return Task.FromResult<IReadOnlyCollection<EduPassUser>>(this.users.Values.ToArray());
            }

            public Task AddUserAsync(EduPassUser user)
            {
                this.pending.Add(() => this.users.Add(user.Identifier, user));

                return Task.FromResult(0);
            }

            public Task ReplaceUserAsync(EduPassUser user)
            {
                this.pending.Add(() => this.users[user.Identifier] = user);

                return Task.FromResult(0);
            }

            public Task<bool> RemoveUserAsync(string identifier)
            {
                if (!this.users.ContainsKey(identifier))
                {
                    return Task.FromResult(false);
                }

                this.pending.Add(() => this.users.Remove(identifier));

                return Task.FromResult(true);
            }

            public Task<Core2Group> FindGroupAsync(string identifier)
            {
                this.groups.TryGetValue(identifier, out Core2Group group);

                return Task.FromResult(group);
            }

            public Task<Core2Group> FindGroupByDisplayNameAsync(string displayName)
            {
                return Task.FromResult(
                    this.groups.Values.FirstOrDefault(
                        (Core2Group item) =>
                            string.Equals(item.DisplayName, displayName, StringComparison.OrdinalIgnoreCase)));
            }

            public Task<IReadOnlyCollection<Core2Group>> ListGroupsAsync()
            {
                return Task.FromResult<IReadOnlyCollection<Core2Group>>(this.groups.Values.ToArray());
            }

            public Task AddGroupAsync(Core2Group group)
            {
                this.pending.Add(() => this.groups.Add(group.Identifier, group));

                return Task.FromResult(0);
            }

            public Task ReplaceGroupAsync(Core2Group group)
            {
                this.pending.Add(() => this.groups[group.Identifier] = group);

                return Task.FromResult(0);
            }

            public Task<bool> RemoveGroupAsync(string identifier)
            {
                if (!this.groups.ContainsKey(identifier))
                {
                    return Task.FromResult(false);
                }

                this.pending.Add(() => this.groups.Remove(identifier));

                return Task.FromResult(true);
            }

            public Task<IReadOnlyCollection<UserGroup>> FindMembershipsAsync(string userIdentifier)
            {
                return Task.FromResult<IReadOnlyCollection<UserGroup>>(
                    this
                    .groups
                    .Values
                    .Where((Core2Group group) => Store.Lists(group, userIdentifier))
                    .Select(Store.Held)
                    .ToArray());
            }

            public Task<IReadOnlyDictionary<string, IReadOnlyCollection<UserGroup>>> FindMembershipsAsync(
                IReadOnlyCollection<string> userIdentifiers)
            {
                Dictionary<string, IReadOnlyCollection<UserGroup>> result =
                    new Dictionary<string, IReadOnlyCollection<UserGroup>>(StringComparer.OrdinalIgnoreCase);

                foreach (string identifier in userIdentifiers)
                {
                    result[identifier] =
                        this
                        .groups
                        .Values
                        .Where((Core2Group group) => Store.Lists(group, identifier))
                        .Select(Store.Held)
                        .ToArray();
                }

                return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyCollection<UserGroup>>>(result);
            }

            public Task RemoveMembershipsAsync(string userIdentifier)
            {
                foreach (Core2Group group in this.groups.Values.Where(
                             (Core2Group item) => Store.Lists(item, userIdentifier)))
                {
                    Core2Group excluded = group;

                    this.pending.Add(
                        () =>
                            excluded.Members =
                                excluded
                                .Members
                                .Where(
                                    (Member member) =>
                                        !string.Equals(
                                            member.Value,
                                            userIdentifier,
                                            StringComparison.OrdinalIgnoreCase))
                                .ToArray());
                }

                return Task.FromResult(0);
            }

            public Task<IReadOnlyCollection<string>> FindUnresolvedUsersAsync(IReadOnlyCollection<string> identifiers)
            {
                return Task.FromResult<IReadOnlyCollection<string>>(
                    identifiers.Where((string item) => !this.users.ContainsKey(item)).ToArray());
            }

            public Task CompleteAsync()
            {
                foreach (Action write in this.pending)
                {
                    write();
                }

                this.pending.Clear();

                return Task.FromResult(0);
            }

            public void Dispose()
            {
                if (this.released)
                {
                    return;
                }

                this.released = true;
                this.pending.Clear();
                this.synchronization.Release();
            }

            private static bool Lists(Core2Group group, string userIdentifier)
            {
                return
                    null != group.Members
                    && group.Members.Any(
                        (Member member) =>
                            string.Equals(member.Value, userIdentifier, StringComparison.OrdinalIgnoreCase));
            }

            private static UserGroup Held(Core2Group group)
            {
                return
                    new UserGroup
                    {
                        Value = group.Identifier,
                        Display = group.DisplayName,
                    };
            }
        }
    }
}
