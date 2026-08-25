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