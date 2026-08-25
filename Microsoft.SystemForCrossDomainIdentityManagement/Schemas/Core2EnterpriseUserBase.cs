//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM
{
    using System.Runtime.Serialization;

    [DataContract]
    public abstract class Core2EnterpriseUserBase : Core2UserBase
    {
        protected Core2EnterpriseUserBase()
            : base()
        {
            this.AddSchema(SchemaIdentifiers.Core2EnterpriseUser);
            this.EnterpriseExtension = new ExtensionAttributeEnterpriseUser2();
        }

        [DataMember(Name = AttributeNames.ExtensionEnterpriseUser2)]
        public ExtensionAttributeEnterpriseUser2 EnterpriseExtension
        {
            get;
            set;
        }

        /// <summary>
        /// Keeps an extension holding no values out of the response body.
        /// </summary>
        /// <remarks>
        /// The property is instantiated by the constructor so that a PATCH against an enterprise
        /// attribute has somewhere to land, which meant every response carried
        /// <c>"urn:ietf:params:scim:schemas:extension:enterprise:2.0:User": {}</c> whether or not
        /// the resource used the schema. On a subclass that does not declare the enterprise URN in
        /// its own <c>schemas</c> - <c>EduPassUser</c> - that is an attribute belonging to a schema
        /// the response does not declare and the service does not advertise at <c>/Schemas</c>.
        /// Newtonsoft honours this convention, so the member is written only once it holds
        /// something.
        /// </remarks>
        public bool ShouldSerializeEnterpriseExtension()
        {
            ExtensionAttributeEnterpriseUser2 extension = this.EnterpriseExtension;

            if (null == extension)
            {
                return false;
            }

            return null != extension.Manager
                || !string.IsNullOrWhiteSpace(extension.CostCenter)
                || !string.IsNullOrWhiteSpace(extension.Department)
                || !string.IsNullOrWhiteSpace(extension.Division)
                || !string.IsNullOrWhiteSpace(extension.EmployeeNumber)
                || !string.IsNullOrWhiteSpace(extension.Organization);
        }
    }
}