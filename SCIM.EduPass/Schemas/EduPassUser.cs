// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Scim.EduPass
{
    using System;
    using System.Linq;
    using System.Runtime.Serialization;
    using Microsoft.SCIM;

    /// <summary>
    /// The attributes of the Edupass User extension schema.
    /// </summary>
    /// <remarks>
    /// Every member is optional. An Edupass relying party declares which of them it actually
    /// stores through its <c>/Schemas</c> endpoint - see <see cref="EduPassTypeSchemes"/> - and
    /// a party that does not hold UIN/FIN omits <c>uinFin</c> entirely.
    /// </remarks>
    [DataContract]
    public class EduPassUserExtension
    {
        [DataMember(Name = EduPassAttributeNames.IdentityType, IsRequired = false, EmitDefaultValue = false)]
        public virtual string IdentityType
        {
            get;
            set;
        }

        /// <summary>
        /// The UIN/FIN. Null for non-human identities, and absent altogether for a relying
        /// party that does not store it.
        /// </summary>
        [DataMember(Name = EduPassAttributeNames.UinFin, IsRequired = false, EmitDefaultValue = false)]
        public virtual string UinFin
        {
            get;
            set;
        }

        [DataMember(Name = EduPassAttributeNames.SchoolOrHq, IsRequired = false, EmitDefaultValue = false)]
        public virtual string SchoolOrHq
        {
            get;
            set;
        }

        [DataMember(Name = EduPassAttributeNames.IdentitySource, IsRequired = false, EmitDefaultValue = false)]
        public virtual string IdentitySource
        {
            get;
            set;
        }
    }

    /// <summary>
    /// A SCIM User carrying the Edupass extension.
    /// </summary>
    /// <remarks>
    /// Derives from <see cref="Core2EnterpriseUser"/> so that the enterprise PATCH semantics in
    /// <c>Core2EnterpriseUserExtensions</c> apply - those are extension methods on that concrete
    /// type and bind statically, so a type derived from the base class would not get them.
    ///
    /// The extension is a real <c>[DataMember]</c> rather than an entry in the inherited
    /// <c>CustomExtension</c> dictionary. The dictionary would now work -
    /// <c>SchematizedJsonConverter</c> makes it round-trip - but a typed member gives
    /// <see cref="EduPassValidator"/> compile-time properties to check and lets the attribute
    /// schemes follow the type, where the dictionary holds only
    /// <c>Dictionary&lt;string, object&gt;</c> values. The converter skips a schema URI that is
    /// already bound to a typed member, so the two do not collide.
    ///
    /// The schema URI is added in the constructor, so <c>schemas</c> lists both the core User
    /// schema and the Edupass extension, as the specification's examples show.
    /// </remarks>
    [DataContract]
    public class EduPassUser : Core2EnterpriseUser
    {
        public EduPassUser()
        {
            this.AddSchema(EduPassSchemaIdentifiers.UserExtension);
            this.EduPassExtension = new EduPassUserExtension();
        }

        /// <summary>
        /// Re-declares the extension schema after deserialization.
        /// </summary>
        /// <remarks>
        /// <c>Schematized.OnDeserializing</c> resets the schema list, so whatever the constructor
        /// added is discarded and replaced by the request's own <c>schemas</c> array. A request
        /// that omits the extension URI - the specification's examples include it, but nothing
        /// enforces that - would otherwise produce a response whose <c>schemas</c> does not
        /// declare the extension it is carrying.
        /// </remarks>
        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            this.AddSchema(EduPassSchemaIdentifiers.UserExtension);

            if (null == this.EduPassExtension)
            {
                this.EduPassExtension = new EduPassUserExtension();
            }
        }

        [DataMember(Name = EduPassSchemaIdentifiers.UserExtension, IsRequired = false, EmitDefaultValue = false)]
        public virtual EduPassUserExtension EduPassExtension
        {
            get;
            set;
        }

        /// <summary>
        /// Applies a PATCH operation against the Edupass extension.
        /// </summary>
        /// <remarks>
        /// The core patcher knows nothing of this schema and rejects what it cannot place, so
        /// without this override every PATCH naming an Edupass attribute would answer 400.
        /// </remarks>
        protected override bool TryPatchExtensionAttribute(PatchOperation2 operation)
        {
            if (null == operation?.Path)
            {
                return false;
            }

            if (!EduPassSchemaIdentifiers.UserExtension.Equals(
                    operation.Path.SchemaIdentifier,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (null == this.EduPassExtension)
            {
                this.EduPassExtension = new EduPassUserExtension();
            }

            string value = OperationName.Remove == operation.Name
                ? null
                : operation.Value?.SingleOrDefault()?.Value;

            switch (operation.Path.AttributePath)
            {
                case EduPassAttributeNames.IdentityType:
                    this.EduPassExtension.IdentityType = value;
                    return true;

                case EduPassAttributeNames.UinFin:
                    this.EduPassExtension.UinFin = value;
                    return true;

                case EduPassAttributeNames.SchoolOrHq:
                    this.EduPassExtension.SchoolOrHq = value;
                    return true;

                case EduPassAttributeNames.IdentitySource:
                    this.EduPassExtension.IdentitySource = value;
                    return true;

                default:
                    return false;
            }
        }
    }
}
