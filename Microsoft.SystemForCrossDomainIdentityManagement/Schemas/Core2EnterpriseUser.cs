//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM
{
    using System.Runtime.Serialization;

    /// <remarks>
    /// Not sealed: a downstream library that adds a schema extension derives from this type so
    /// that the enterprise PATCH semantics in <see cref="Core2EnterpriseUserExtensions"/> - which
    /// are extension methods on this concrete type, and so bind statically - apply to it too.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1501:AvoidExcessiveInheritance", Justification = "The long inheritence hieararchy reflects the the System for Cross-Domain Identity Management inheritence mechanism.")]
    [DataContract(Name = Core2EnterpriseUser.DataContractName)]
    public class Core2EnterpriseUser : Core2EnterpriseUserBase
    {
        private const string DataContractName = "Core2EnterpriseUser";

        public Core2EnterpriseUser()
            : base()
        {
        }
    }
}