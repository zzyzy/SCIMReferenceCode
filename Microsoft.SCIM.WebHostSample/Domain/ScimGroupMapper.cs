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
        /// <c>members</c> is omitted when the group has none, which is the rule the user's
        /// multi-valued attributes already follow. This once emitted an empty array instead, on
        /// the argument that a known-empty membership and an unreported one are different
        /// things - but RFC 7643 section 2.5 settles it the other way: "unassigned attributes,
        /// the null value, or empty array ... SHALL be considered to be equivalent in 'state'".
        /// There is no difference to report, and a remove of the attribute (RFC 7644 section
        /// 3.5.2.2) can then be seen to have removed it.
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
                        ScimGroupMapper.Empty(entity.Members)
                            ? null
                            : entity.Members
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

        private static bool Empty(IList<GroupMemberEntity> members)
        {
            return null == members || !members.Any((GroupMemberEntity item) => null != item);
        }
    }
}
