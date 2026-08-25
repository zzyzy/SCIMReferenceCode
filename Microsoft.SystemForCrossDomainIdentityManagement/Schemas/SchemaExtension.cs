//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM
{
    using System.Runtime.Serialization;

    /// <summary>
    /// A schema layered on a resource type's base schema (RFC 7643 section 6).
    /// </summary>
    /// <remarks>
    /// Without this a resource type could name only one schema, so a service offering an
    /// extension had nowhere to say so - and the reference sample worked around it by
    /// declaring the enterprise extension as the User type's <em>base</em> schema, which
    /// tells a client the core User schema is not what /Users serves.
    /// </remarks>
    [DataContract]
    public sealed class SchemaExtension
    {
        [DataMember(Name = AttributeNames.Schema)]
        public string Schema
        {
            get;
            set;
        }

        /// <summary>
        /// Whether a resource of this type must carry the extension.
        /// </summary>
        /// <remarks>
        /// Always emitted, including when false: RFC 7643 section 6 lists it as required,
        /// and a client cannot read an absent member as a deliberate "not required".
        /// </remarks>
        [DataMember(Name = AttributeNames.Required, EmitDefaultValue = true)]
        public bool Required
        {
            get;
            set;
        }
    }
}
