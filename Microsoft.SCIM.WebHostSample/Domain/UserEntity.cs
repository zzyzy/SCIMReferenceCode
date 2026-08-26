// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM.WebHostSample.Domain
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A user as a relying party's own domain would hold it.
    /// </summary>
    /// <remarks>
    /// Deliberately not a SCIM type. Nothing in this namespace references <c>Microsoft.SCIM</c>
    /// except the mappers, which is the point: the wire format is one party's contract and the
    /// stored model is another's, and a relying party that lets the first dictate the second
    /// finds it cannot change either. This is the shape a table has - flat scalar columns, child
    /// rows with their own keys and a foreign key back, UTC timestamps - so that swapping the
    /// in-memory dictionary for a DbContext is a change of store rather than a change of model.
    ///
    /// The scalar/child split is the one a database forces: everything single-valued is a column
    /// here, and every multi-valued SCIM attribute is a child collection whose entries carry
    /// their own identity. SCIM has no identifier for an entry in a multi-valued attribute, so
    /// <see cref="UserValueEntity.Id"/> is the store's, minted on write and never sent.
    /// </remarks>
    public class UserEntity
    {
        /// <summary>The primary key, and the SCIM <c>id</c>.</summary>
        public string Id { get; set; }

        /// <summary>The provisioning client's key for this user - SCIM <c>externalId</c>.</summary>
        public string ExternalId { get; set; }

        public string UserName { get; set; }

        public string DisplayName { get; set; }

        public string NickName { get; set; }

        public string Title { get; set; }

        public string UserType { get; set; }

        public string PreferredLanguage { get; set; }

        public string Locale { get; set; }

        public string TimeZone { get; set; }

        /// <summary>Nullable, because SCIM's "active" is optional and a remove clears it.</summary>
        public bool? IsActive { get; set; }

        // The SCIM name object, flattened. A complex single-valued attribute is columns on the
        // owning row, not a table of its own - it has no independent lifetime.
        public string NameFormatted { get; set; }

        public string FamilyName { get; set; }

        public string GivenName { get; set; }

        public string MiddleName { get; set; }

        public string HonorificPrefix { get; set; }

        public string HonorificSuffix { get; set; }

        // The enterprise extension, flattened for the same reason. A relying party that never
        // provisions these would simply not have the columns, and the mapper would not set them.
        public string CostCenter { get; set; }

        public string Department { get; set; }

        public string Division { get; set; }

        public string EmployeeNumber { get; set; }

        public string Organization { get; set; }

        /// <summary>The manager's user identifier - a foreign key back to this same table.</summary>
        public string ManagerId { get; set; }

        public DateTime CreatedUtc { get; set; }

        public DateTime LastModifiedUtc { get; set; }

        /// <summary>The optimistic concurrency token, surfaced as SCIM <c>meta.version</c>.</summary>
        public string Version { get; set; }

        public IList<UserEmailEntity> Emails { get; set; } = new List<UserEmailEntity>();

        public IList<UserPhoneNumberEntity> PhoneNumbers { get; set; } = new List<UserPhoneNumberEntity>();

        public IList<UserInstantMessagingEntity> InstantMessagings { get; set; } =
            new List<UserInstantMessagingEntity>();

        public IList<UserRoleEntity> Roles { get; set; } = new List<UserRoleEntity>();

        public IList<UserAddressEntity> Addresses { get; set; } = new List<UserAddressEntity>();

        /// <summary>
        /// Schema extensions this domain does not model, keyed by schema URN.
        /// </summary>
        /// <remarks>
        /// A relying party is sent attributes it never asked for, and RFC 7644 expects a read to
        /// return what a write supplied. Modelling every extension anyone might send is not
        /// possible, so the remainder is kept whole and given back unchanged - one JSON column in
        /// a database, this dictionary here. Anything the domain actually acts on should be
        /// promoted to a column above instead: what lives in here cannot be queried or
        /// constrained.
        /// </remarks>
        public IDictionary<string, IDictionary<string, object>> ExtensionData { get; set; } =
            new Dictionary<string, IDictionary<string, object>>();
    }

    /// <summary>One entry of a multi-valued attribute: its own row, keyed and owned.</summary>
    public abstract class UserValueEntity
    {
        /// <summary>The row's key. The store's own - SCIM does not identify these entries.</summary>
        public string Id { get; set; }

        /// <summary>The owning user - the foreign key.</summary>
        public string UserId { get; set; }

        /// <summary>The SCIM <c>type</c> sub-attribute: work, home, and so on.</summary>
        public string ItemType { get; set; }

        /// <summary>The SCIM <c>primary</c> sub-attribute.</summary>
        public bool IsPrimary { get; set; }
    }

    public class UserEmailEntity : UserValueEntity
    {
        public string Address { get; set; }
    }

    public class UserPhoneNumberEntity : UserValueEntity
    {
        public string Number { get; set; }
    }

    public class UserInstantMessagingEntity : UserValueEntity
    {
        public string Handle { get; set; }
    }

    public class UserRoleEntity : UserValueEntity
    {
        public string Value { get; set; }

        public string Display { get; set; }
    }

    public class UserAddressEntity : UserValueEntity
    {
        public string Formatted { get; set; }

        public string StreetAddress { get; set; }

        public string Locality { get; set; }

        public string Region { get; set; }

        public string PostalCode { get; set; }

        public string Country { get; set; }
    }
}
