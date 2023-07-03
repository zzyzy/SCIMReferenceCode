//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

using System;

namespace Microsoft.SCIM
{
    using System.Runtime.Serialization;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1501:AvoidExcessiveInheritance", Justification = "The long inheritence hieararchy reflects the the System for Cross-Domain Identity Management inheritence mechanism.")]
    [DataContract(Name = Core2EnterpriseUser.DataContractName)]
    public sealed class Core2EnterpriseUser : Core2EnterpriseUserBase
    {
        private const string DataContractName = "Core2EnterpriseUser";

        public Core2EnterpriseUser()
            : base()
        {
        }
        
        /// <summary>
        /// Need to configure in Azure AD to map "employeeLeaveDateTime" to "leaveDateTime" in
        /// Enterprise applications > Your App > Provisioning > Mappings >
        /// Provision Azure Active Directory Users > Attribute Mappings.
        ///
        /// 1. Check "Show advanced options"
        /// 2. Click "Edit attribute list for customappsso"
        /// 3. Add "leaveDateTime" with data type set to "DateTime" (leave other fields as default)
        /// 4. Click "Save"
        /// 5. Go back to "Attribute Mappings"
        /// 6. Click "Add New Mapping"
        /// 7. Set Mapping type = Direct
        /// 8. Set Source attribute = employeeLeaveDateTime
        /// 9. Set Target attribute = leaveDateTime (the one we just created)
        /// 10. Leave others as default (Match objects using this attribute = No, Apply this mapping = Always)
        /// 
        /// </summary>
        [DataMember(Name = AttributeNames.LeaveDateTime, IsRequired = false, EmitDefaultValue = false)]
        public DateTime LeaveDateTime
        {
            get;
            set;
        }
    }
}