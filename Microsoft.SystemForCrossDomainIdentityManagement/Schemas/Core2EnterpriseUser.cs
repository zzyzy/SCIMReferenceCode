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

        /// <summary>
        /// Applies a PATCH operation naming an attribute this type does not model.
        /// </summary>
        /// <returns>
        /// True if the operation was applied, false to leave it unhandled.
        /// </returns>
        /// <remarks>
        /// An unhandled operation is rejected with 400 invalidPath, so a derived type carrying a
        /// schema extension must override this to claim its own attributes - otherwise a PATCH
        /// against them fails. <see cref="Core2EnterpriseUserExtensions"/> binds statically, so
        /// this virtual call is the only point at which a subclass gets to participate.
        /// </remarks>
        protected internal virtual bool TryPatchExtensionAttribute(PatchOperation2 operation)
        {
            return false;
        }
    }
}