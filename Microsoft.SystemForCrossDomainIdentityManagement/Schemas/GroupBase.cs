//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM
{
    using System.Collections.Generic;
    using System.Runtime.Serialization;

    [DataContract]
    public abstract class GroupBase : Resource
    {
        [DataMember(Name = AttributeNames.DisplayName)]
        public virtual string DisplayName
        {
            get;
            set;
        }

        /// <summary>
        /// The group's members. Empty rather than null, so that a group nobody belongs to
        /// still serializes <c>members: []</c>.
        /// </summary>
        /// <remarks>
        /// <c>members</c> is advertised at <c>/Schemas</c> with <c>returned: default</c>, and
        /// omitting it says something different from returning an empty list: absent reads as
        /// "this service does not report membership", empty as "this group has none". Left
        /// uninitialized, a group kept the attribute out of its create response and out of
        /// every read until its membership was first written.
        /// </remarks>
        [DataMember(Name = AttributeNames.Members, IsRequired = false, EmitDefaultValue = false)]
        public virtual IEnumerable<Member> Members
        {
            get;
            set;
        } = new List<Member>();
    }
}