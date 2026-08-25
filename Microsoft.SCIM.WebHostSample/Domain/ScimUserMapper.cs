// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM.WebHostSample.Domain
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.SCIM;

    /// <summary>
    /// Translates between the SCIM user resource and <see cref="UserEntity"/>.
    /// </summary>
    /// <remarks>
    /// Written out by hand, on purpose. A reflection-based mapper would bind the two models
    /// together by name, so renaming a column would change the wire format and adding an
    /// attribute would silently start persisting it - the coupling this separation exists to
    /// prevent. Every attribute a relying party stores appears here explicitly, and anything
    /// absent from this file is not stored; that is the inventory, and it is meant to be read.
    ///
    /// The pair is total in both directions for what the domain models: <c>ToScim(ToEntity(x))</c>
    /// preserves every attribute the entity has a home for. What has no home is not dropped
    /// silently - it goes to <see cref="UserEntity.ExtensionData"/> and comes back unchanged.
    ///
    /// <para><b>Child rows and identity.</b> <see cref="ToEntity"/> mints a fresh key for every
    /// entry of a multi-valued attribute, because SCIM sends those entries with no identity of
    /// their own and a write carries the whole collection. That is correct where nothing
    /// references the child rows, as here. A store where something does - an audit trail, a
    /// foreign key - has to reconcile against the existing rows by natural key instead of
    /// replacing them wholesale, and that reconciliation belongs in the store, not in this
    /// mapper.</para>
    /// </remarks>
    public static class ScimUserMapper
    {
        /// <summary>
        /// Maps an inbound SCIM resource onto a new entity.
        /// </summary>
        /// <remarks>
        /// <c>id</c> and the timestamps are the store's to decide, so they are left for the
        /// caller: a create mints them, a replace carries the created stamp forward.
        /// </remarks>
        public static UserEntity ToEntity(Core2EnterpriseUser resource)
        {
            if (null == resource)
            {
                throw new ArgumentNullException(nameof(resource));
            }

            UserEntity entity =
                new UserEntity
                {
                    Id = resource.Identifier,
                    ExternalId = resource.ExternalIdentifier,
                    UserName = resource.UserName,
                    DisplayName = resource.DisplayName,
                    NickName = resource.Nickname,
                    Title = resource.Title,
                    UserType = resource.UserType,
                    PreferredLanguage = resource.PreferredLanguage,
                    Locale = resource.Locale,
                    TimeZone = resource.TimeZone,
                    IsActive = resource.Active,
                    Version = resource.Metadata?.Version,
                };

            if (null != resource.Name)
            {
                entity.NameFormatted = resource.Name.Formatted;
                entity.FamilyName = resource.Name.FamilyName;
                entity.GivenName = resource.Name.GivenName;
                entity.MiddleName = resource.Name.MiddleName;
                entity.HonorificPrefix = resource.Name.HonorificPrefix;
                entity.HonorificSuffix = resource.Name.HonorificSuffix;
            }

            ExtensionAttributeEnterpriseUser2 enterprise = resource.EnterpriseExtension;

            if (null != enterprise)
            {
                entity.CostCenter = enterprise.CostCenter;
                entity.Department = enterprise.Department;
                entity.Division = enterprise.Division;
                entity.EmployeeNumber = enterprise.EmployeeNumber;
                entity.Organization = enterprise.Organization;
                entity.ManagerId = enterprise.Manager?.Value;
            }

            entity.Emails =
                ToChildRows(
                    resource.ElectronicMailAddresses,
                    (ElectronicMailAddress item) =>
                        new UserEmailEntity
                        {
                            Address = item.Value,
                            ItemType = item.ItemType,
                            IsPrimary = item.Primary,
                        });

            entity.PhoneNumbers =
                ToChildRows(
                    resource.PhoneNumbers,
                    (PhoneNumber item) =>
                        new UserPhoneNumberEntity
                        {
                            Number = item.Value,
                            ItemType = item.ItemType,
                            IsPrimary = item.Primary,
                        });

            entity.InstantMessagings =
                ToChildRows(
                    resource.InstantMessagings,
                    (InstantMessaging item) =>
                        new UserInstantMessagingEntity
                        {
                            Handle = item.Value,
                            ItemType = item.ItemType,
                            IsPrimary = item.Primary,
                        });

            entity.Roles =
                ToChildRows(
                    resource.Roles,
                    (Role item) =>
                        new UserRoleEntity
                        {
                            Value = item.Value,
                            Display = item.Display,
                            ItemType = item.ItemType,
                            IsPrimary = item.Primary,
                        });

            entity.Addresses =
                ToChildRows(
                    resource.Addresses,
                    (Address item) =>
                        new UserAddressEntity
                        {
                            Formatted = item.Formatted,
                            StreetAddress = item.StreetAddress,
                            Locality = item.Locality,
                            Region = item.Region,
                            PostalCode = item.PostalCode,
                            Country = item.Country,
                            ItemType = item.ItemType,
                            IsPrimary = item.Primary,
                        });

            foreach (KeyValuePair<string, IDictionary<string, object>> extension in resource.CustomExtension)
            {
                entity.ExtensionData[extension.Key] = new Dictionary<string, object>(extension.Value);
            }

            return entity;
        }

        /// <summary>Maps a stored entity back to the SCIM resource a client reads.</summary>
        /// <remarks>
        /// A fresh resource every time, which is also what makes a PATCH atomic: the operations
        /// are applied to this projection and the entity is written only once they all succeed,
        /// so a failure part-way through leaves the stored row untouched.
        ///
        /// <c>meta.resourceType</c> is derived rather than stored - it is a fact about the
        /// endpoint, not about the user. <c>meta.location</c> and the <c>$ref</c> of any
        /// reference are left unset for the hosting layer, which is the only part that knows the
        /// service's base URI.
        /// </remarks>
        public static Core2EnterpriseUser ToScim(UserEntity entity)
        {
            if (null == entity)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            Core2EnterpriseUser resource =
                new Core2EnterpriseUser
                {
                    Identifier = entity.Id,
                    ExternalIdentifier = entity.ExternalId,
                    UserName = entity.UserName,
                    DisplayName = entity.DisplayName,
                    Nickname = entity.NickName,
                    Title = entity.Title,
                    UserType = entity.UserType,
                    PreferredLanguage = entity.PreferredLanguage,
                    Locale = entity.Locale,
                    TimeZone = entity.TimeZone,
                    Active = entity.IsActive,
                    Metadata =
                        new Core2Metadata
                        {
                            ResourceType = Types.User,
                            Created = entity.CreatedUtc,
                            LastModified = entity.LastModifiedUtc,
                            Version = entity.Version,
                        },
                };

            // Omitted entirely when every part is unset, rather than returned as an empty
            // object: RFC 7643 2.5 makes an unassigned attribute absent, not present and blank.
            if (AnyAssigned(
                    entity.NameFormatted,
                    entity.FamilyName,
                    entity.GivenName,
                    entity.MiddleName,
                    entity.HonorificPrefix,
                    entity.HonorificSuffix))
            {
                resource.Name =
                    new Name
                    {
                        Formatted = entity.NameFormatted,
                        FamilyName = entity.FamilyName,
                        GivenName = entity.GivenName,
                        MiddleName = entity.MiddleName,
                        HonorificPrefix = entity.HonorificPrefix,
                        HonorificSuffix = entity.HonorificSuffix,
                    };
            }

            resource.EnterpriseExtension =
                new ExtensionAttributeEnterpriseUser2
                {
                    CostCenter = entity.CostCenter,
                    Department = entity.Department,
                    Division = entity.Division,
                    EmployeeNumber = entity.EmployeeNumber,
                    Organization = entity.Organization,
                    Manager =
                        string.IsNullOrWhiteSpace(entity.ManagerId)
                            ? null
                            : new Manager { Value = entity.ManagerId },
                };

            resource.ElectronicMailAddresses =
                ToScimValues(
                    entity.Emails,
                    (UserEmailEntity item) =>
                        new ElectronicMailAddress
                        {
                            Value = item.Address,
                            ItemType = item.ItemType,
                            Primary = item.IsPrimary,
                        });

            resource.PhoneNumbers =
                ToScimValues(
                    entity.PhoneNumbers,
                    (UserPhoneNumberEntity item) =>
                        new PhoneNumber
                        {
                            Value = item.Number,
                            ItemType = item.ItemType,
                            Primary = item.IsPrimary,
                        });

            resource.InstantMessagings =
                ToScimValues(
                    entity.InstantMessagings,
                    (UserInstantMessagingEntity item) =>
                        new InstantMessaging
                        {
                            Value = item.Handle,
                            ItemType = item.ItemType,
                            Primary = item.IsPrimary,
                        });

            resource.Roles =
                ToScimValues(
                    entity.Roles,
                    (UserRoleEntity item) =>
                        new Role
                        {
                            Value = item.Value,
                            Display = item.Display,
                            ItemType = item.ItemType,
                            Primary = item.IsPrimary,
                        });

            resource.Addresses =
                ToScimValues(
                    entity.Addresses,
                    (UserAddressEntity item) =>
                        new Address
                        {
                            Formatted = item.Formatted,
                            StreetAddress = item.StreetAddress,
                            Locality = item.Locality,
                            Region = item.Region,
                            PostalCode = item.PostalCode,
                            Country = item.Country,
                            ItemType = item.ItemType,
                            Primary = item.IsPrimary,
                        });

            // groups is not mapped in either direction. RFC 7643 4.1.2 makes it read-only and
            // derived from group membership, which this domain holds on the group alone.

            foreach (KeyValuePair<string, IDictionary<string, object>> extension in entity.ExtensionData)
            {
                resource.AddCustomAttribute(extension.Key, new Dictionary<string, object>(extension.Value));

                // schemas is derived from what the resource actually carries, rather than
                // echoed from the request: a URN a client declared and then sent nothing for
                // describes no part of the body.
                resource.AddSchema(extension.Key);
            }

            return resource;
        }

        /// <summary>Maps a multi-valued attribute to child rows, minting a key for each.</summary>
        private static IList<TEntity> ToChildRows<TResource, TEntity>(
            IEnumerable<TResource> source,
            Func<TResource, TEntity> map)
            where TEntity : UserValueEntity
        {
            List<TEntity> results = new List<TEntity>();

            if (null == source)
            {
                return results;
            }

            foreach (TResource item in source)
            {
                if (null == item)
                {
                    continue;
                }

                TEntity entity = map(item);
                entity.Id = Guid.NewGuid().ToString();
                results.Add(entity);
            }

            return results;
        }

        /// <summary>
        /// Maps child rows back, or null when there are none.
        /// </summary>
        /// <remarks>
        /// Null rather than an empty array, because the resource omits an unassigned attribute:
        /// returning <c>[]</c> would tell a client the user has an empty set of e-mail addresses
        /// where the truth is that none was ever supplied.
        /// </remarks>
        private static IEnumerable<TResource> ToScimValues<TEntity, TResource>(
            IEnumerable<TEntity> source,
            Func<TEntity, TResource> map)
            where TEntity : UserValueEntity
        {
            if (null == source)
            {
                return null;
            }

            TResource[] results = source.Where((TEntity item) => null != item).Select(map).ToArray();

            return results.Length == 0 ? null : results;
        }

        private static bool AnyAssigned(params string[] values)
        {
            return values.Any((string item) => !string.IsNullOrWhiteSpace(item));
        }
    }
}
