//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Runtime.Serialization;
    using Newtonsoft.Json;

    [DataContract]
    public sealed class PatchOperation2Combined : PatchOperation2Base
    {
        private const string Template = "{0}: [{1}]";

        [DataMember(Name = AttributeNames.Value, Order = 2)]
        private object values;


        public PatchOperation2Combined()
        {
        }

        public PatchOperation2Combined(OperationName operationName, string pathExpression)
            : base(operationName, pathExpression)
        {
        }
        public static PatchOperation2Combined Create(OperationName operationName, string pathExpression, string value)
        {
            if (string.IsNullOrWhiteSpace(pathExpression))
            {
                throw new ArgumentNullException(nameof(pathExpression));
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentNullException(nameof(value));
            }

            OperationValue operationValue = new OperationValue();
            operationValue.Value = value;

            PatchOperation2Combined result = new PatchOperation2Combined(operationName, pathExpression);
            result.SetValues(new[] { operationValue });

            return result;
        }

        /// <summary>
        /// Replaces the operation's values.
        /// </summary>
        /// <remarks>
        /// Assigning the serialized form to <see cref="Value"/> instead would leave the
        /// getter serializing a string, so <c>value</c> reached the wire as a JSON string
        /// containing JSON. Readers then took the whole blob as the scalar value - which
        /// is how a bulk-created group ended up with a serialized object as a member.
        /// Holding the values as objects makes an operation built here indistinguishable
        /// from one that arrived over the wire.
        /// </remarks>
        internal void SetValues(IReadOnlyCollection<OperationValue> operationValues)
        {
            this.values = operationValues?.ToArray();
        }

        public string Value
        {
            get
            {
                if (this.values == null)
                {
                    return null;
                }

                string result = JsonConvert.SerializeObject(this.values);
                return result;
            }

            set
            {
                this.values = value;
            }
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (this.Value == null)
            {
                if 
                (
                    this?.Path?.AttributePath != null &&
                    this.Path.AttributePath.Contains(AttributeNames.Members, StringComparison.OrdinalIgnoreCase) &&
                    this.Name == SCIM.OperationName.Remove &&
                    this.Path?.SubAttributes?.Count == 1
                )
                {
                    this.Value = this.Path.SubAttributes.First().ComparisonValue;
                    IPath path = SCIM.Path.Create(AttributeNames.Members);
                    this.Path = path;
                }
            }
        }

        public override string ToString()
        {
            string allValues = string.Join(Environment.NewLine, this.Value);
            string operation = base.ToString();
            string result =
                string.Format(
                    CultureInfo.InvariantCulture,
                    PatchOperation2Combined.Template,
                    operation,
                    allValues);
            return result;
        }
    }
}