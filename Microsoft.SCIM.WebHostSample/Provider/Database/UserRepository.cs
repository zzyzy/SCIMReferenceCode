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
    /// Reads and writes <see cref="UserEntity"/> and its child rows.
    /// </summary>
    /// <remarks>
    /// The SQL lives here rather than in the provider, for the same reason the mapping lives in
    /// <c>Domain</c> rather than in the provider: what the provider is about is the domain's
    /// rules - uniqueness, what a replace clears, what a delete of an absent resource means -
    /// and those read the same whether the rows are in SQLite or anywhere else.
    ///
    /// Every method takes the caller's connection and transaction rather than opening its own,
    /// so that a check and the write it guards are one atomic unit.
    /// </remarks>
    internal static class UserRepository
    {
        /// <summary>
        /// Loads the users a clause matches, with their child rows.
        /// </summary>
        /// <remarks>
        /// Six statements in one round trip, and the children are selected by repeating the
        /// clause as a subquery rather than by listing the matched keys. That keeps it to one
        /// query per table however many users match - a query per user is the shape that makes
        /// a provisioning client's list call quadratic - and avoids the parameter limit an
        /// IN list of keys would eventually hit.
        /// </remarks>
        public static async Task<IReadOnlyList<UserEntity>> LoadAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string where,
            object parameters)
        {
            string sql =
                $@"
SELECT * FROM ScimUsers WHERE {where} ORDER BY CreatedUtc, Id;
SELECT * FROM ScimUserEmails            WHERE UserId IN (SELECT Id FROM ScimUsers WHERE {where});
SELECT * FROM ScimUserPhoneNumbers      WHERE UserId IN (SELECT Id FROM ScimUsers WHERE {where});
SELECT * FROM ScimUserInstantMessagings WHERE UserId IN (SELECT Id FROM ScimUsers WHERE {where});
SELECT * FROM ScimUserRoles             WHERE UserId IN (SELECT Id FROM ScimUsers WHERE {where});
SELECT * FROM ScimUserAddresses         WHERE UserId IN (SELECT Id FROM ScimUsers WHERE {where});";

            using (SqlMapper.GridReader reader =
                await connection.QueryMultipleAsync(sql, parameters, transaction).ConfigureAwait(false))
            {
                List<UserEntity> users = (await reader.ReadAsync<UserEntity>().ConfigureAwait(false)).AsList();

                IEnumerable<UserEmailEntity> emails =
                    await reader.ReadAsync<UserEmailEntity>().ConfigureAwait(false);
                IEnumerable<UserPhoneNumberEntity> phoneNumbers =
                    await reader.ReadAsync<UserPhoneNumberEntity>().ConfigureAwait(false);
                IEnumerable<UserInstantMessagingEntity> instantMessagings =
                    await reader.ReadAsync<UserInstantMessagingEntity>().ConfigureAwait(false);
                IEnumerable<UserRoleEntity> roles =
                    await reader.ReadAsync<UserRoleEntity>().ConfigureAwait(false);
                IEnumerable<UserAddressEntity> addresses =
                    await reader.ReadAsync<UserAddressEntity>().ConfigureAwait(false);

                Dictionary<string, UserEntity> index =
                    users.ToDictionary((UserEntity item) => item.Id, StringComparer.Ordinal);

                UserRepository.Attach(index, emails, (UserEntity user) => user.Emails);
                UserRepository.Attach(index, phoneNumbers, (UserEntity user) => user.PhoneNumbers);
                UserRepository.Attach(index, instantMessagings, (UserEntity user) => user.InstantMessagings);
                UserRepository.Attach(index, roles, (UserEntity user) => user.Roles);
                UserRepository.Attach(index, addresses, (UserEntity user) => user.Addresses);

                return users;
            }
        }

        public static async Task<UserEntity> FindAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string identifier)
        {
            IReadOnlyList<UserEntity> users =
                await UserRepository
                    .LoadAsync(connection, transaction, "Id = @Id", new { Id = identifier })
                    .ConfigureAwait(false);

            return users.FirstOrDefault();
        }

        /// <summary>
        /// Whether another user already holds this userName.
        /// </summary>
        /// <remarks>
        /// Case-sensitive, matching the in-memory providers' ordinal comparison and the unique
        /// index. <paramref name="exceptIdentifier"/> is the user being replaced or patched, who
        /// is allowed to keep the name they already have.
        /// </remarks>
        public static async Task<bool> UserNameTakenAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string userName,
            string exceptIdentifier)
        {
            const string Sql =
                @"SELECT EXISTS
                  (
                      SELECT 1 FROM ScimUsers
                      WHERE UserName = @UserName
                        AND (@ExceptId IS NULL OR Id <> @ExceptId COLLATE NOCASE)
                  );";

            return
                await connection
                    .ExecuteScalarAsync<bool>(
                        Sql,
                        new { UserName = userName, ExceptId = exceptIdentifier },
                        transaction)
                    .ConfigureAwait(false);
        }

        public static async Task InsertAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            UserEntity entity)
        {
            const string Sql =
                @"INSERT INTO ScimUsers
                  (
                      Id, ExternalId, UserName, DisplayName, NickName, Title, UserType,
                      PreferredLanguage, Locale, TimeZone, IsActive,
                      NameFormatted, FamilyName, GivenName, MiddleName, HonorificPrefix, HonorificSuffix,
                      CostCenter, Department, Division, EmployeeNumber, Organization, ManagerId,
                      CreatedUtc, LastModifiedUtc, Version, ExtensionData
                  )
                  VALUES
                  (
                      @Id, @ExternalId, @UserName, @DisplayName, @NickName, @Title, @UserType,
                      @PreferredLanguage, @Locale, @TimeZone, @IsActive,
                      @NameFormatted, @FamilyName, @GivenName, @MiddleName, @HonorificPrefix, @HonorificSuffix,
                      @CostCenter, @Department, @Division, @EmployeeNumber, @Organization, @ManagerId,
                      @CreatedUtc, @LastModifiedUtc, @Version, @ExtensionData
                  );";

            await connection.ExecuteAsync(Sql, entity, transaction).ConfigureAwait(false);
            await UserRepository.InsertChildrenAsync(connection, transaction, entity).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes a user over the one already stored.
        /// </summary>
        /// <remarks>
        /// The scalar columns are updated and the child rows are replaced wholesale, which is
        /// what SCIM's write semantics are: a multi-valued attribute arrives as a whole
        /// collection whose entries carry no identity, so there is nothing to reconcile against.
        /// <see cref="Domain.ScimUserMapper"/> says the same thing from the other side, and
        /// notes what a store with anything referencing those rows would have to do instead.
        ///
        /// The row is updated rather than deleted and re-inserted so that the user's identity
        /// survives - a delete would cascade to anything a later schema hangs off it.
        /// </remarks>
        public static async Task ReplaceAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            UserEntity entity)
        {
            const string Sql =
                @"UPDATE ScimUsers SET
                      ExternalId        = @ExternalId,
                      UserName          = @UserName,
                      DisplayName       = @DisplayName,
                      NickName          = @NickName,
                      Title             = @Title,
                      UserType          = @UserType,
                      PreferredLanguage = @PreferredLanguage,
                      Locale            = @Locale,
                      TimeZone          = @TimeZone,
                      IsActive          = @IsActive,
                      NameFormatted     = @NameFormatted,
                      FamilyName        = @FamilyName,
                      GivenName         = @GivenName,
                      MiddleName        = @MiddleName,
                      HonorificPrefix   = @HonorificPrefix,
                      HonorificSuffix   = @HonorificSuffix,
                      CostCenter        = @CostCenter,
                      Department        = @Department,
                      Division          = @Division,
                      EmployeeNumber    = @EmployeeNumber,
                      Organization      = @Organization,
                      ManagerId         = @ManagerId,
                      CreatedUtc        = @CreatedUtc,
                      LastModifiedUtc   = @LastModifiedUtc,
                      Version           = @Version,
                      ExtensionData     = @ExtensionData
                  WHERE Id = @Id;

                  DELETE FROM ScimUserEmails            WHERE UserId = @Id;
                  DELETE FROM ScimUserPhoneNumbers      WHERE UserId = @Id;
                  DELETE FROM ScimUserInstantMessagings WHERE UserId = @Id;
                  DELETE FROM ScimUserRoles             WHERE UserId = @Id;
                  DELETE FROM ScimUserAddresses         WHERE UserId = @Id;";

            await connection.ExecuteAsync(Sql, entity, transaction).ConfigureAwait(false);
            await UserRepository.InsertChildrenAsync(connection, transaction, entity).ConfigureAwait(false);
        }

        /// <summary>Deletes a user, and reports whether there was one. The children cascade.</summary>
        public static async Task<bool> DeleteAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string identifier)
        {
            int affected =
                await connection
                    .ExecuteAsync("DELETE FROM ScimUsers WHERE Id = @Id;", new { Id = identifier }, transaction)
                    .ConfigureAwait(false);

            return affected > 0;
        }

        private static async Task InsertChildrenAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            UserEntity entity)
        {
            await UserRepository
                .InsertChildAsync(
                    connection,
                    transaction,
                    entity,
                    entity.Emails,
                    "INSERT INTO ScimUserEmails (Id, UserId, ItemType, IsPrimary, Address) "
                    + "VALUES (@Id, @UserId, @ItemType, @IsPrimary, @Address);")
                .ConfigureAwait(false);

            await UserRepository
                .InsertChildAsync(
                    connection,
                    transaction,
                    entity,
                    entity.PhoneNumbers,
                    "INSERT INTO ScimUserPhoneNumbers (Id, UserId, ItemType, IsPrimary, Number) "
                    + "VALUES (@Id, @UserId, @ItemType, @IsPrimary, @Number);")
                .ConfigureAwait(false);

            await UserRepository
                .InsertChildAsync(
                    connection,
                    transaction,
                    entity,
                    entity.InstantMessagings,
                    "INSERT INTO ScimUserInstantMessagings (Id, UserId, ItemType, IsPrimary, Handle) "
                    + "VALUES (@Id, @UserId, @ItemType, @IsPrimary, @Handle);")
                .ConfigureAwait(false);

            await UserRepository
                .InsertChildAsync(
                    connection,
                    transaction,
                    entity,
                    entity.Roles,
                    "INSERT INTO ScimUserRoles (Id, UserId, ItemType, IsPrimary, Value, Display) "
                    + "VALUES (@Id, @UserId, @ItemType, @IsPrimary, @Value, @Display);")
                .ConfigureAwait(false);

            await UserRepository
                .InsertChildAsync(
                    connection,
                    transaction,
                    entity,
                    entity.Addresses,
                    "INSERT INTO ScimUserAddresses "
                    + "(Id, UserId, ItemType, IsPrimary, Formatted, StreetAddress, Locality, Region, PostalCode, Country) "
                    + "VALUES (@Id, @UserId, @ItemType, @IsPrimary, @Formatted, @StreetAddress, @Locality, @Region, @PostalCode, @Country);")
                .ConfigureAwait(false);
        }

        /// <summary>Writes one multi-valued attribute's rows, stamping the foreign key.</summary>
        private static async Task InsertChildAsync<TChild>(
            SqliteConnection connection,
            SqliteTransaction transaction,
            UserEntity entity,
            IList<TChild> children,
            string sql)
            where TChild : UserValueEntity
        {
            if (null == children || children.Count == 0)
            {
                return;
            }

            List<TChild> rows = new List<TChild>();

            foreach (TChild child in children)
            {
                if (null == child)
                {
                    continue;
                }

                // The mapper mints the key; the owner is only known once the user has an id,
                // which for a create is after the mapping.
                child.UserId = entity.Id;
                rows.Add(child);
            }

            if (rows.Count == 0)
            {
                return;
            }

            await connection.ExecuteAsync(sql, rows, transaction).ConfigureAwait(false);
        }

        private static void Attach<TChild>(
            IDictionary<string, UserEntity> users,
            IEnumerable<TChild> children,
            Func<UserEntity, IList<TChild>> collection)
            where TChild : UserValueEntity
        {
            foreach (TChild child in children)
            {
                if (users.TryGetValue(child.UserId, out UserEntity user))
                {
                    collection(user).Add(child);
                }
            }
        }
    }
}
