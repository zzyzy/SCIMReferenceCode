//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM
{
    using System.Runtime.Serialization;

    [DataContract]
    public abstract class FeatureBase
    {
        [DataMember(Name = AttributeNames.Supported)]
        public virtual bool Supported
        {
            get;
            set;
        }
    }
}