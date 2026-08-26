// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM.WebHostSample.Provider.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Dapper;
    using Microsoft.Data.Sqlite;
    using Microsoft.SCIM.WebHostSample.Domain;

    /// <summary>
    /// Reads and writes <see cref="GroupEntity"/> and its membership rows.
    /// </summary>
    /// <remarks>
    /// The same arrangement as <see cref="UserRepository"/>, over the smaller aggregate.
    /// Membership hangs off the group and not off the user, which is what
    /// <see cref="GroupEntity"/> says and what RFC 7643 4.1.2 requires: the user's
    /// <c>groups</c> attribute is read-only and derived, so it is held once and cannot
    /// disagree with itself.
    /// </remarks>
    internal static class GroupRepository
    {
        public static async Task<IReadOnlyList<GroupEntity>> LoadAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string where,
            object parameters)
        {
            string sql =
                $@"
SELECT * FROM ScimGroups WHERE {where} ORDER BY CreatedUtc, Id;
SELECT * FROM ScimGroupMembers WHERE GroupId IN (SELECT Id FROM ScimGroups WHERE {where});";

            using (SqlMapper.GridReader reader =
                await connection.QueryMultipleAsync(sql, parameters, transaction).ConfigureAwait(false))
            {
                List<GroupEntity> groups = (await reader.ReadAsync<GroupEntity>().ConfigureAwait(false)).AsList();

                IEnumerable<GroupMemberEntity> members =
                    await reader.ReadAsync<GroupMemberEntity>().ConfigureAwait(false);

                Dictionary<string, GroupEntity> index =
                    groups.ToDictionary((GroupEntity item) => item.Id, StringComparer.Ordinal);

                foreach (GroupMemberEntity member in members)
                {
                    if (index.TryGetValue(member.GroupId, out GroupEntity group))
                    {
                        group.Members.Add(member);
                    }
                }

                return groups;
            }
        }

        public static async Task<GroupEntity> FindAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string identifier)
        {
            IReadOnlyList<GroupEntity> groups =
                await GroupRepository
                    .LoadAsync(connection, transaction, "Id = @Id", new { Id = identifier })
                    .ConfigureAwait(false);

            return groups.FirstOrDefault();
        }

        /// <summary>Whether another group already holds this displayName.</summary>
        public static async Task<bool> DisplayNameTakenAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string displayName,
            string exceptIdentifier)
        {
            const string Sql =
                @"SELECT EXISTS
                  (
                      SELECT 1 FROM ScimGroups
                      WHERE DisplayName = @DisplayName
                        AND (@ExceptId IS NULL OR Id <> @ExceptId COLLATE NOCASE)
                  );";

            return
                await connection
                    .ExecuteScalarAsync<bool>(
                        Sql,
                        new { DisplayName = displayName, ExceptId = exceptIdentifier },
                        transaction)
                    .ConfigureAwait(false);
        }

        public static async Task InsertAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            GroupEntity entity)
        {
            const string Sql =
                @"INSERT INTO ScimGroups
                      (Id, ExternalId, DisplayName, CreatedUtc, LastModifiedUtc, Version, ExtensionData)
                  VALUES
                      (@Id, @ExternalId, @DisplayName, @CreatedUtc, @LastModifiedUtc, @Version, @ExtensionData);";

            await connection.ExecuteAsync(Sql, entity, transaction).ConfigureAwait(false);
            await GroupRepository.InsertMembersAsync(connection, transaction, entity).ConfigureAwait(false);
        }

        public static async Task ReplaceAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            GroupEntity entity)
        {
            const string Sql =
                @"UPDATE ScimGroups SET
                      ExternalId      = @ExternalId,
                      DisplayName     = @DisplayName,
                      CreatedUtc      = @CreatedUtc,
                      LastModifiedUtc = @LastModifiedUtc,
                      Version         = @Version,
                      ExtensionData   = @ExtensionData
                  WHERE Id = @Id;

                  DELETE FROM ScimGroupMembers WHERE GroupId = @Id;";

            await connection.ExecuteAsync(Sql, entity, transaction).ConfigureAwait(false);
            await GroupRepository.InsertMembersAsync(connection, transaction, entity).ConfigureAwait(false);
        }

        public static async Task<bool> DeleteAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string identifier)
        {
            int affected =
                await connection
                    .ExecuteAsync("DELETE FROM ScimGroups WHERE Id = @Id;", new { Id = identifier }, transaction)
                    .ConfigureAwait(false);

            return affected > 0;
        }

        /// <summary>
        /// Writes the membership rows, one per member.
        /// </summary>
        /// <remarks>
        /// Deduplicated by member, because the join table is unique over (GroupId, MemberId) and
        /// a member belongs to a group once. A request that lists the same member twice is
        /// stored once rather than rejected: SCIM has no way to distinguish the two entries, so
        /// there is nothing for the second to mean.
        ///
        /// The entity's own collection is narrowed to what was written, so that the resource the
        /// provider returns is the resource a subsequent read will give back. Answering a create
        /// with the members as they were sent would show the caller a duplicate the store had
        /// already collapsed.
        /// </remarks>
        private static async Task InsertMembersAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            GroupEntity entity)
        {
            if (null == entity.Members || entity.Members.Count == 0)
            {
                return;
            }

            List<GroupMemberEntity> rows =
                entity
                    .Members
                    .Where((GroupMemberEntity item) => null != item && !string.IsNullOrWhiteSpace(item.MemberId))
                    .GroupBy((GroupMemberEntity item) => item.MemberId, StringComparer.Ordinal)
                    .Select((IGrouping<string, GroupMemberEntity> group) => group.First())
                    .ToList();

            foreach (GroupMemberEntity member in rows)
            {
                member.GroupId = entity.Id;
            }

            entity.Members = rows;

            if (rows.Count == 0)
            {
                return;
            }

            const string Sql =
                @"INSERT INTO ScimGroupMembers (GroupId, MemberId, Reference, Display, MemberType)
                  VALUES (@GroupId, @MemberId, @Reference, @Display, @MemberType);";

            await connection.ExecuteAsync(Sql, rows, transaction).ConfigureAwait(false);
        }
    }
}
