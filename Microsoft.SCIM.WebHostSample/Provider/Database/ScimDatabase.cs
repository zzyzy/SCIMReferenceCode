// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM.WebHostSample.Provider.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
    using System.IO;
    using Dapper;
    using Microsoft.Data.Sqlite;
    using Newtonsoft.Json;

    /// <summary>
    /// The SQLite database the database-backed providers read and write.
    /// </summary>
    /// <remarks>
    /// The counterpart of <see cref="InMemoryStorage"/>: where that one holds two dictionaries,
    /// this one owns a connection string and a schema. The providers above it keep the same
    /// shape - map the request in, apply the domain's rules, map the stored row back out - which
    /// is what the entity/resource split in <c>Domain</c> was for.
    ///
    /// SQLite because it needs no server and runs on both sample legs; Dapper because the
    /// mapping between a row and an entity is already written by hand in <c>Domain</c>, and an
    /// ORM would add a second model to keep in step with it.
    /// </remarks>
    public sealed class ScimDatabase
    {
        /// <summary>The file a host falls back to, beside its own binaries.</summary>
        private const string DefaultFileName = "scim.db";

        private static readonly object HandlerSyncRoot = new object();
        private static bool handlersRegistered;

        private readonly string connectionString;

        public ScimDatabase(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            this.connectionString = connectionString;

            ScimDatabase.RegisterTypeHandlers();
            this.EnsureSchema();
        }

        /// <summary>
        /// The connection string a host should use, given what its configuration supplied.
        /// </summary>
        /// <remarks>
        /// SCIM_DATABASE if it is set, so a deployment can put the file where it wants it or
        /// point at <c>:memory:</c> for a run that keeps nothing. Otherwise a file beside the
        /// host's binaries, anchored to the base directory rather than left relative, because
        /// the three sample hosts are launched with three different working directories and a
        /// relative path would put the database somewhere different in each.
        ///
        /// Under IIS the account the application pool runs as needs write access to that
        /// folder, and to the -wal and -shm files WAL mode creates beside it.
        /// </remarks>
        public static string Resolve(string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            return "Data Source=" + Path.Combine(AppContext.BaseDirectory, ScimDatabase.DefaultFileName);
        }

        /// <summary>Opens a connection with the per-connection settings the providers assume.</summary>
        /// <remarks>
        /// Both pragmas are per-connection rather than per-database, so they have to be set
        /// every time: foreign keys are off by default in SQLite - which would silently leave
        /// the child rows of a deleted user behind - and the default busy timeout of zero turns
        /// any write contention into an immediate "database is locked" rather than a short wait.
        /// </remarks>
        public SqliteConnection Open()
        {
            SqliteConnection connection = new SqliteConnection(this.connectionString);
            connection.Open();

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
                command.ExecuteNonQuery();
            }

            return connection;
        }

        /// <summary>
        /// Begins a transaction that takes the write lock immediately.
        /// </summary>
        /// <remarks>
        /// Every write here is a read-modify-write - check uniqueness then insert, or project
        /// the row out, patch it and write it back - and SQLite's default deferred transaction
        /// takes no write lock until its first write. Two callers would both pass the check and
        /// the second would fail to upgrade its lock. BEGIN IMMEDIATE is what the in-memory
        /// store gets from <c>lock (SyncRoot)</c>.
        /// </remarks>
        public static SqliteTransaction BeginWrite(SqliteConnection connection)
        {
            if (null == connection)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            return connection.BeginTransaction(deferred: false);
        }

        private void EnsureSchema()
        {
            using (SqliteConnection connection = this.Open())
            using (SqliteTransaction transaction = ScimDatabase.BeginWrite(connection))
            {
                connection.Execute(ScimDatabase.Schema, transaction: transaction);
                transaction.Commit();
            }

            // Outside the transaction: a journal mode change cannot run inside one. WAL so that
            // a read does not block the writer, which is the shape a provisioning client
            // produces - a burst of writes against a service still answering reads.
            using (SqliteConnection connection = this.Open())
            {
                connection.Execute("PRAGMA journal_mode = WAL;");
            }
        }

        /// <summary>
        /// Registers the two mappings Dapper cannot infer.
        /// </summary>
        /// <remarks>
        /// Once per process, because Dapper's handler table is static. Both exist so that the
        /// entity types in <c>Domain</c> need no attributes and no database types of their own:
        /// the store adapts to the model rather than the other way round.
        /// </remarks>
        private static void RegisterTypeHandlers()
        {
            lock (ScimDatabase.HandlerSyncRoot)
            {
                if (ScimDatabase.handlersRegistered)
                {
                    return;
                }

                SqlMapper.AddTypeHandler(typeof(DateTime), new UtcDateTimeHandler());
                SqlMapper.AddTypeHandler(
                    typeof(IDictionary<string, IDictionary<string, object>>),
                    new ExtensionDataHandler());

                ScimDatabase.handlersRegistered = true;
            }
        }

        /// <summary>
        /// Marks a timestamp read back out of SQLite as UTC.
        /// </summary>
        /// <remarks>
        /// The store holds UTC in every timestamp column, but SQLite has no date type and the
        /// text it holds carries no zone, so the driver reads it back as
        /// <see cref="DateTimeKind.Unspecified"/> - which would drop the <c>Z</c> from every
        /// <c>meta.created</c> and <c>meta.lastModified</c> on the wire.
        ///
        /// <para>Reading is all this handler does, whatever <see cref="SetValue"/> says. Dapper
        /// resolves a handler for DateTime when materialising a row but not when binding a
        /// parameter - DateTime is in its built-in type map, and that is consulted first - so
        /// the value written is the driver's own text format. <see cref="Parse"/> is therefore
        /// deliberately format-agnostic: pinning it to one layout made every timestamp whose
        /// last tick digit was zero unreadable, because the driver trims trailing zeros from
        /// the fraction and the strict parse then rejected its own store's rows.</para>
        ///
        /// <para>That trimming is safe for the one comparison that matters. A
        /// <c>meta.lastModified gt</c> filter is a string comparison, and dropping trailing
        /// zeros from a decimal fraction cannot reorder two values: the shorter is a prefix of
        /// the longer, which sorts before it, exactly as the shorter timestamp is earlier.</para>
        /// </remarks>
        private sealed class UtcDateTimeHandler : SqlMapper.TypeHandler<DateTime>
        {
            public override DateTime Parse(object value)
            {
                if (value is DateTime already)
                {
                    return DateTime.SpecifyKind(already, DateTimeKind.Utc);
                }

                return
                    DateTime.SpecifyKind(
                        DateTime.Parse(
                            Convert.ToString(value, CultureInfo.InvariantCulture),
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None),
                        DateTimeKind.Utc);
            }

            public override void SetValue(IDbDataParameter parameter, DateTime value)
            {
                parameter.DbType = DbType.DateTime;
                parameter.Value = value.ToUniversalTime();
            }
        }

        /// <summary>
        /// Reads and writes <see cref="Domain.UserEntity.ExtensionData"/> as one JSON column.
        /// </summary>
        /// <remarks>
        /// The entity's own remarks say what it is for: a schema extension the domain does not
        /// model is kept whole and given back unchanged. What it cannot be is queried or
        /// constrained - anything the domain acts on belongs in a column instead.
        /// </remarks>
        private sealed class ExtensionDataHandler :
            SqlMapper.TypeHandler<IDictionary<string, IDictionary<string, object>>>
        {
            public override IDictionary<string, IDictionary<string, object>> Parse(object value)
            {
                string json = value as string;

                if (string.IsNullOrWhiteSpace(json))
                {
                    return new Dictionary<string, IDictionary<string, object>>();
                }

                return
                    JsonConvert.DeserializeObject<Dictionary<string, IDictionary<string, object>>>(json)
                    ?? new Dictionary<string, IDictionary<string, object>>();
            }

            public override void SetValue(
                IDbDataParameter parameter,
                IDictionary<string, IDictionary<string, object>> value)
            {
                parameter.DbType = DbType.String;

                // Null rather than "{}" for the ordinary case, so the column reads as absent.
                parameter.Value =
                    null == value || value.Count == 0
                        ? (object)DBNull.Value
                        : JsonConvert.SerializeObject(value);
            }
        }

        /// <summary>
        /// The schema, as the entities in <c>Domain</c> describe it.
        /// </summary>
        /// <remarks>
        /// Every single-valued attribute is a column and every multi-valued one is a child table
        /// keyed by the store, which is the split <see cref="Domain.UserEntity"/> was written to
        /// - so this DDL is a transcription of that file rather than a second design.
        ///
        /// Idempotent, and run at startup, because a sample should come up against an empty
        /// folder. A real relying party migrates instead: IF NOT EXISTS cannot alter a table
        /// that is already there, and so cannot carry a schema forward.
        /// </remarks>
        private const string Schema = @"
CREATE TABLE IF NOT EXISTS ScimUsers
(
    Id                TEXT NOT NULL PRIMARY KEY,
    ExternalId        TEXT NULL,
    UserName          TEXT NOT NULL,
    DisplayName       TEXT NULL,
    NickName          TEXT NULL,
    Title             TEXT NULL,
    UserType          TEXT NULL,
    PreferredLanguage TEXT NULL,
    Locale            TEXT NULL,
    TimeZone          TEXT NULL,
    IsActive          INTEGER NULL,
    NameFormatted     TEXT NULL,
    FamilyName        TEXT NULL,
    GivenName         TEXT NULL,
    MiddleName        TEXT NULL,
    HonorificPrefix   TEXT NULL,
    HonorificSuffix   TEXT NULL,
    CostCenter        TEXT NULL,
    Department        TEXT NULL,
    Division          TEXT NULL,
    EmployeeNumber    TEXT NULL,
    Organization      TEXT NULL,
    -- Deliberately not a foreign key back to ScimUsers: a client may provision a user before
    -- their manager, and RFC 7643 4.3 does not require the reference to resolve.
    ManagerId         TEXT NULL,
    CreatedUtc        TEXT NOT NULL,
    LastModifiedUtc   TEXT NOT NULL,
    Version           TEXT NULL,
    ExtensionData     TEXT NULL
);

-- Case-sensitive, which is the comparison the providers make when they check for a duplicate.
-- A backstop rather than the check itself: the providers answer 409, and a constraint violation
-- surfacing from here would be a 500.
CREATE UNIQUE INDEX IF NOT EXISTS UX_ScimUsers_UserName ON ScimUsers (UserName);
CREATE INDEX IF NOT EXISTS IX_ScimUsers_ExternalId ON ScimUsers (ExternalId);
CREATE INDEX IF NOT EXISTS IX_ScimUsers_LastModifiedUtc ON ScimUsers (LastModifiedUtc);

CREATE TABLE IF NOT EXISTS ScimUserEmails
(
    Id        TEXT NOT NULL PRIMARY KEY,
    UserId    TEXT NOT NULL REFERENCES ScimUsers (Id) ON DELETE CASCADE,
    ItemType  TEXT NULL,
    IsPrimary INTEGER NOT NULL,
    Address   TEXT NULL
);

CREATE TABLE IF NOT EXISTS ScimUserPhoneNumbers
(
    Id        TEXT NOT NULL PRIMARY KEY,
    UserId    TEXT NOT NULL REFERENCES ScimUsers (Id) ON DELETE CASCADE,
    ItemType  TEXT NULL,
    IsPrimary INTEGER NOT NULL,
    Number    TEXT NULL
);

CREATE TABLE IF NOT EXISTS ScimUserInstantMessagings
(
    Id        TEXT NOT NULL PRIMARY KEY,
    UserId    TEXT NOT NULL REFERENCES ScimUsers (Id) ON DELETE CASCADE,
    ItemType  TEXT NULL,
    IsPrimary INTEGER NOT NULL,
    Handle    TEXT NULL
);

CREATE TABLE IF NOT EXISTS ScimUserRoles
(
    Id        TEXT NOT NULL PRIMARY KEY,
    UserId    TEXT NOT NULL REFERENCES ScimUsers (Id) ON DELETE CASCADE,
    ItemType  TEXT NULL,
    IsPrimary INTEGER NOT NULL,
    Value     TEXT NULL,
    Display   TEXT NULL
);

CREATE TABLE IF NOT EXISTS ScimUserAddresses
(
    Id            TEXT NOT NULL PRIMARY KEY,
    UserId        TEXT NOT NULL REFERENCES ScimUsers (Id) ON DELETE CASCADE,
    ItemType      TEXT NULL,
    IsPrimary     INTEGER NOT NULL,
    Formatted     TEXT NULL,
    StreetAddress TEXT NULL,
    Locality      TEXT NULL,
    Region        TEXT NULL,
    PostalCode    TEXT NULL,
    Country       TEXT NULL
);

CREATE INDEX IF NOT EXISTS IX_ScimUserEmails_UserId ON ScimUserEmails (UserId);
CREATE INDEX IF NOT EXISTS IX_ScimUserPhoneNumbers_UserId ON ScimUserPhoneNumbers (UserId);
CREATE INDEX IF NOT EXISTS IX_ScimUserInstantMessagings_UserId ON ScimUserInstantMessagings (UserId);
CREATE INDEX IF NOT EXISTS IX_ScimUserRoles_UserId ON ScimUserRoles (UserId);
CREATE INDEX IF NOT EXISTS IX_ScimUserAddresses_UserId ON ScimUserAddresses (UserId);

CREATE TABLE IF NOT EXISTS ScimGroups
(
    Id              TEXT NOT NULL PRIMARY KEY,
    ExternalId      TEXT NULL,
    DisplayName     TEXT NOT NULL,
    CreatedUtc      TEXT NOT NULL,
    LastModifiedUtc TEXT NOT NULL,
    Version         TEXT NULL,
    ExtensionData   TEXT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_ScimGroups_DisplayName ON ScimGroups (DisplayName);
CREATE INDEX IF NOT EXISTS IX_ScimGroups_ExternalId ON ScimGroups (ExternalId);

-- The join table GroupEntity's remarks describe, unique over (GroupId, MemberId) because a
-- member belongs to a group once. The repository deduplicates before writing, so a request
-- listing the same member twice is stored once rather than rejected.
CREATE TABLE IF NOT EXISTS ScimGroupMembers
(
    GroupId    TEXT NOT NULL REFERENCES ScimGroups (Id) ON DELETE CASCADE,
    MemberId   TEXT NOT NULL,
    Reference  TEXT NULL,
    Display    TEXT NULL,
    MemberType TEXT NULL,
    PRIMARY KEY (GroupId, MemberId)
);

CREATE INDEX IF NOT EXISTS IX_ScimGroupMembers_MemberId ON ScimGroupMembers (MemberId);
";
    }
}
