// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM.WebHostSample.Domain
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.SCIM;

    /// <summary>
    /// Translates between the SCIM group resource and <see cref="GroupEntity"/>.
    /// </summary>
    /// <remarks>
    /// Hand-written for the same reasons as <see cref="ScimUserMapper"/>. The group is the
    /// smaller of the two, and shows the same three decisions: scalars map to columns, the
    /// multi-valued attribute maps to child rows, and what the domain does not model is kept
    /// whole rather than dropped.
    /// </remarks>
    public static class ScimGroupMapper
    {
        public static GroupEntity ToEntity(Core2Group resource)
        {
            if (null == resource)
            {
                throw new ArgumentNullException(nameof(resource));
            }

            GroupEntity entity =
                new GroupEntity
                {
                    Id = resource.Identifier,
                    ExternalId = resource.ExternalIdentifier,
                    DisplayName = resource.DisplayName,
                    Version = resource.Metadata?.Version,
                };

            if (null != resource.Members)
            {
                entity.Members =
                    resource
                        .Members
                        .Where((Member item) => null != item)
                        .Select(
                            (Member item) =>
                                new GroupMemberEntity
                                {
                                    GroupId = resource.Identifier,
                                    MemberId = item.Value,

                                    // All four sub-attributes, not just value. A client that
                                    // sent display or type gets them back; rebuilding an entry
                                    // from its value alone loses them on the next read.
                                    Reference = item.Reference,
                                    Display = item.Display,
                                    MemberType = item.TypeName,
                                })
                        .ToList();
            }

            foreach (KeyValuePair<string, IDictionary<string, object>> extension in resource.CustomExtension)
            {
                entity.ExtensionData[extension.Key] = new Dictionary<string, object>(extension.Value);
            }

            return entity;
        }

        /// <summary>Maps a stored entity back to the SCIM resource a client reads.</summary>
        /// <remarks>
        /// <c>members</c> is always present, even when empty, which is the opposite of the rule
        /// the user's multi-valued attributes follow. A group with no members has a known and
        /// empty membership; omitting the attribute would say instead that this response does
        /// not report membership at all, and a client cannot tell the difference.
        /// </remarks>
        public static Core2Group ToScim(GroupEntity entity)
        {
            if (null == entity)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            Core2Group resource =
                new Core2Group
                {
                    Identifier = entity.Id,
                    ExternalIdentifier = entity.ExternalId,
                    DisplayName = entity.DisplayName,
                    Metadata =
                        new Core2Metadata
                        {
                            ResourceType = Types.Group,
                            Created = entity.CreatedUtc,
                            LastModified = entity.LastModifiedUtc,
                            Version = entity.Version,
                        },
                    Members =
                        (entity.Members ?? new List<GroupMemberEntity>())
                            .Where((GroupMemberEntity item) => null != item)
                            .Select(
                                (GroupMemberEntity item) =>
                                    new Member
                                    {
                                        Value = item.MemberId,
                                        Reference = item.Reference,
                                        Display = item.Display,
                                        TypeName = item.MemberType,
                                    })
                            .ToArray(),
                };

            foreach (KeyValuePair<string, IDictionary<string, object>> extension in entity.ExtensionData)
            {
                resource.AddCustomAttribute(extension.Key, new Dictionary<string, object>(extension.Value));
                resource.AddSchema(extension.Key);
            }

            return resource;
        }
    }
}
