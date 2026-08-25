// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM.WebHostSample.Domain
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A group as a relying party's own domain would hold it.
    /// </summary>
    /// <remarks>
    /// Membership is a child collection of the group rather than of the user, which is the shape
    /// SCIM itself takes: RFC 7643 4.1.2 makes the user's <c>groups</c> attribute read-only and
    /// derived. Held once, it cannot disagree with itself. A database would make
    /// <see cref="GroupMemberEntity"/> the join table, with a unique constraint over
    /// (GroupId, UserId).
    /// </remarks>
    public class GroupEntity
    {
        /// <summary>The primary key, and the SCIM <c>id</c>.</summary>
        public string Id { get; set; }

        /// <summary>The provisioning client's key for this group - SCIM <c>externalId</c>.</summary>
        public string ExternalId { get; set; }

        public string DisplayName { get; set; }

        public DateTime CreatedUtc { get; set; }

        public DateTime LastModifiedUtc { get; set; }

        /// <summary>The optimistic concurrency token, surfaced as SCIM <c>meta.version</c>.</summary>
        public string Version { get; set; }

        public IList<GroupMemberEntity> Members { get; set; } = new List<GroupMemberEntity>();

        /// <summary>Schema extensions this domain does not model. See <see cref="UserEntity.ExtensionData"/>.</summary>
        public IDictionary<string, IDictionary<string, object>> ExtensionData { get; set; } =
            new Dictionary<string, IDictionary<string, object>>();
    }

    /// <summary>One membership - the join row between a group and a member.</summary>
    public class GroupMemberEntity
    {
        /// <summary>The owning group - the foreign key.</summary>
        public string GroupId { get; set; }

        /// <summary>The member's identifier - SCIM <c>members[].value</c>.</summary>
        public string MemberId { get; set; }

        /// <summary>
        /// SCIM <c>members[].$ref</c> as the client sent it, when it did.
        /// </summary>
        /// <remarks>
        /// Stored rather than always recomputed because a client may reference a member the
        /// service does not host. Left unset, the hosting layer fills in a local URI on the way
        /// out, so a store that never sets this is still correct for the ordinary case.
        /// </remarks>
        public string Reference { get; set; }

        /// <summary>SCIM <c>members[].display</c>.</summary>
        public string Display { get; set; }

        /// <summary>SCIM <c>members[].type</c> - "User" or "Group".</summary>
        public string MemberType { get; set; }
    }
}
