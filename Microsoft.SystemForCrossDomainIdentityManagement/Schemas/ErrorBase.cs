//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM
{
    using System.Runtime.Serialization;

    [DataContract]
    public abstract class ErrorBase : Schematized
    {
        [DataMember(Name = "scimType", Order = 1)] //AttributeNames.ScimType
        public virtual string ScimType
        {
            get;
            set;
        }

        [DataMember(Name = "detail", Order = 2)] //AttributeNames.Detail
        public virtual string Detail
        {
            get;
            set;
        }

        /// <summary>
        /// The HTTP status code. A string, not a number: RFC 7644 section 3.12 defines
        /// <c>status</c> as "the HTTP status code (see Section 6 of [RFC7231]) expressed as a
        /// JSON string".
        /// </summary>
        [DataMember(Name = "status", Order = 3)] //AttributeNames.Status
        public virtual string Status
        {
            get;
            set;
        }
    }
}
