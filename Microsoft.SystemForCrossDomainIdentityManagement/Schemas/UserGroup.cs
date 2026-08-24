// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System.Runtime.Serialization;

    /// <summary>
    /// An entry in a User resource's read-only <c>groups</c> attribute
    /// (RFC 7643 section 4.1.2).
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Member"/>, which is the Group side of the same relationship.
    /// The sub-attributes are the ones the RFC defines: <c>value</c> is the group's identifier,
    /// <c>$ref</c> its URI, <c>display</c> its human-readable name and <c>type</c> whether the
    /// membership is <c>direct</c> or <c>indirect</c>.
    /// </remarks>
    [DataContract]
    public sealed class UserGroup
    {
        [DataMember(Name = AttributeNames.Value)]
        public string Value
        {
            get;
            set;
        }

        [DataMember(Name = AttributeNames.Reference, IsRequired = false, EmitDefaultValue = false)]
        public string Reference
        {
            get;
            set;
        }

        [DataMember(Name = AttributeNames.Display, IsRequired = false, EmitDefaultValue = false)]
        public string Display
        {
            get;
            set;
        }

        [DataMember(Name = AttributeNames.Type, IsRequired = false, EmitDefaultValue = false)]
        public string TypeName
        {
            get;
            set;
        }
    }
}
