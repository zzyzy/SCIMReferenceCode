//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM
{
    using System.Globalization;
    using System.Runtime.Serialization;

    [DataContract]
    public sealed class OperationValue
    {
        private const string Template = "{0} {1}";

        [DataMember(Name = ProtocolAttributeNames.Reference, Order = 0, IsRequired = false, EmitDefaultValue = false)]
        public string Reference
        {
            get;
            set;
        }

        [DataMember(Name = AttributeNames.Value, Order = 1, IsRequired = false, EmitDefaultValue = false)]
        public string Value
        {
            get;
            set;
        }

        /// <summary>
        /// The <c>display</c> sub-attribute of a multi-valued attribute's entry.
        /// </summary>
        /// <remarks>
        /// Present for the same reason as <see cref="Reference"/>: a client patching a
        /// members or roles entry sends the whole entry, and anything this type cannot
        /// hold is dropped on the floor while the write still reports success.
        /// </remarks>
        [DataMember(Name = AttributeNames.Display, Order = 2, IsRequired = false, EmitDefaultValue = false)]
        public string Display
        {
            get;
            set;
        }

        /// <summary>The <c>type</c> sub-attribute of a multi-valued attribute's entry.</summary>
        [DataMember(Name = AttributeNames.Type, Order = 3, IsRequired = false, EmitDefaultValue = false)]
        public string TypeName
        {
            get;
            set;
        }

        /// <summary>The <c>primary</c> sub-attribute of a multi-valued attribute's entry.</summary>
        /// <remarks>
        /// Nullable, and omitted when it is: RFC 7643 section 2.4 makes an absent
        /// <c>primary</c> mean false, so a type that could not tell absent from false would
        /// report every entry a client sent as non-primary - and did.
        /// </remarks>
        [DataMember(Name = AttributeNames.Primary, Order = 4, IsRequired = false, EmitDefaultValue = false)]
        public bool? Primary
        {
            get;
            set;
        }

        /// <summary>
        /// The sub-attributes an address entry carries beyond type and primary.
        /// </summary>
        /// <remarks>
        /// RFC 7643 section 4.1.2. An address has no "value" of its own, so unlike an email or
        /// a phone number it cannot be carried by <see cref="Value"/> alone - and an operation
        /// naming the addresses collection as a whole had nowhere to put what it was sent.
        /// </remarks>
        [DataMember(Name = AttributeNames.Formatted, Order = 5, IsRequired = false, EmitDefaultValue = false)]
        public string Formatted
        {
            get;
            set;
        }

        [DataMember(Name = AttributeNames.StreetAddress, Order = 6, IsRequired = false, EmitDefaultValue = false)]
        public string StreetAddress
        {
            get;
            set;
        }

        [DataMember(Name = AttributeNames.Locality, Order = 7, IsRequired = false, EmitDefaultValue = false)]
        public string Locality
        {
            get;
            set;
        }

        [DataMember(Name = AttributeNames.Region, Order = 8, IsRequired = false, EmitDefaultValue = false)]
        public string Region
        {
            get;
            set;
        }

        [DataMember(Name = AttributeNames.PostalCode, Order = 9, IsRequired = false, EmitDefaultValue = false)]
        public string PostalCode
        {
            get;
            set;
        }

        [DataMember(Name = AttributeNames.Country, Order = 10, IsRequired = false, EmitDefaultValue = false)]
        public string Country
        {
            get;
            set;
        }

        public override string ToString()
        {
            string result =
                string.Format(
                    CultureInfo.InvariantCulture,
                    OperationValue.Template,
                    this.Value,
                    this.Reference)
                .Trim();
            return result;
        }
    }
}