// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Scim.EduPass
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft.SCIM;

    /// <summary>
    /// The storage a <see cref="BaseEduPassScimProvider"/> runs one SCIM operation against.
    /// </summary>
    /// <remarks>
    /// One instance is a unit of work: the provider opens it, does the reads and writes the
    /// operation needs, and calls <see cref="CompleteAsync"/> only if it got that far. Disposal
    /// without that call must leave the store as it was found, which is what makes an operation
    /// that writes more than once - deleting a user, which also strips their memberships -
    /// atomic. Backed by a database this is a transaction; backed by memory it is a lock.
    ///
    /// Every lookup here is by an attribute Edupass addresses resources through, so that an
    /// implementation can answer it from an index rather than by scanning. The uniqueness that
    /// <c>userName</c> and <c>displayName</c> carry is enforced by the provider on the read,
    /// but a database should also hold a unique constraint on both: two concurrent creates can
    /// pass the read before either writes.
    /// </remarks>
    public interface IEduPassStore : IDisposable
    {
        Task<EduPassUser> FindUserAsync(string identifier);

        Task<EduPassUser> FindUserByUserNameAsync(string userName);

        Task<EduPassUser> FindUserByExternalIdentifierAsync(string externalIdentifier);

        Task<IReadOnlyCollection<EduPassUser>> ListUsersAsync();

        Task AddUserAsync(EduPassUser user);

        Task ReplaceUserAsync(EduPassUser user);

        /// <returns><c>false</c> if no user has that identifier.</returns>
        Task<bool> RemoveUserAsync(string identifier);

        Task<Core2Group> FindGroupAsync(string identifier);

        Task<Core2Group> FindGroupByDisplayNameAsync(string displayName);

        Task<IReadOnlyCollection<Core2Group>> ListGroupsAsync();

        Task AddGroupAsync(Core2Group group);

        Task ReplaceGroupAsync(Core2Group group);

        /// <returns><c>false</c> if no group has that identifier.</returns>
        Task<bool> RemoveGroupAsync(string identifier);

        /// <summary>
        /// The groups listing the user as a member, as the <c>groups</c> attribute of a user
        /// resource. Membership is held on the group alone, so this is the only source of it.
        /// </summary>
        Task<IReadOnlyCollection<UserGroup>> FindMembershipsAsync(string userIdentifier);

        /// <summary>
        /// The memberships of several users at once, keyed by user identifier. Reading a
        /// collection of users would otherwise be one query per user.
        /// </summary>
        Task<IReadOnlyDictionary<string, IReadOnlyCollection<UserGroup>>> FindMembershipsAsync(
            IReadOnlyCollection<string> userIdentifiers);

        /// <summary>
        /// Removes the user from every group that lists them.
        /// </summary>
        Task RemoveMembershipsAsync(string userIdentifier);

        /// <summary>
        /// Those of the given identifiers that resolve to no user. A membership naming one is
        /// refused rather than stored.
        /// </summary>
        Task<IReadOnlyCollection<string>> FindUnresolvedUsersAsync(IReadOnlyCollection<string> identifiers);

        /// <summary>
        /// Commits the work. Not called when the operation failed.
        /// </summary>
        Task CompleteAsync();
    }
}
