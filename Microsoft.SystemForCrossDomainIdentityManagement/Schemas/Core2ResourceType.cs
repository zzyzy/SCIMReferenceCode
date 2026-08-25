//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.Serialization;

    [DataContract]
    public class Core2ResourceType : Resource
    {
        private Uri endpoint;

        [DataMember(Name = AttributeNames.Endpoint)]
        private string endpointValue;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields", Justification = "Serialized")]
        [DataMember(Name = AttributeNames.Name)]
        private string name;

        public Core2ResourceType()
        {
            this.AddSchema(SchemaIdentifiers.Core2ResourceType);
            this.Metadata =
                new Core2Metadata()
                {
                    ResourceType = Types.ResourceType
                };
        }

        public Uri Endpoint
        {
            get
            {
                return this.endpoint;
            }

            set
            {
                this.endpoint = value;
                this.endpointValue = new SystemForCrossDomainIdentityManagementResourceIdentifier(value).RelativePath;
            }
        }

        [DataMember(Name = AttributeNames.Metadata)]
        public Core2Metadata Metadata
        {
            get;
            set;
        }

        [DataMember(Name = AttributeNames.Schema)]
        public string Schema
        {
            get;
            set;
        }

        /// <summary>
        /// The schemas layered on <see cref="Schema"/> (RFC 7643 section 6).
        /// </summary>
        /// <remarks>
        /// Null rather than an empty list when there are none, so that a resource type
        /// with no extension does not emit an empty array - the serializer drops nulls,
        /// which is how every other optional collection in the library behaves.
        /// </remarks>
        [DataMember(Name = AttributeNames.SchemaExtensions, IsRequired = false, EmitDefaultValue = false)]
        public IReadOnlyCollection<SchemaExtension> SchemaExtensions
        {
            get;
            set;
        }

        /// <summary>Declares one extension against this resource type.</summary>
        public void AddSchemaExtension(string schema, bool required)
        {
            if (string.IsNullOrWhiteSpace(schema))
            {
                throw new ArgumentNullException(nameof(schema));
            }

            List<SchemaExtension> extensions =
                new List<SchemaExtension>(this.SchemaExtensions ?? Array.Empty<SchemaExtension>());

            if (extensions.Exists(
                    (SchemaExtension item) =>
                        string.Equals(item.Schema, schema, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            extensions.Add(new SchemaExtension() { Schema = schema, Required = required });
            this.SchemaExtensions = extensions;
        }

        private void InitializeEndpoint(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                this.endpoint = null;
                return;
            }

            this.endpoint = new Uri(value, UriKind.Relative);
        }

        private void InitializeEndpoint()
        {
            this.InitializeEndpoint(this.endpointValue);
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            this.InitializeEndpoint();
        }

        [OnSerializing]
        private void OnSerializing(StreamingContext context)
        {
            this.name = this.Identifier;
        }
    }
}