//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM
{
    using System.Globalization;
    using System.Runtime.Serialization;

    [DataContract]
    public sealed class Core2Error : ErrorBase
    {
        public Core2Error(
            string detail,
            int status,
            string scimType = null // https://datatracker.ietf.org/doc/html/rfc7644#section-3.12
            )
        {
            this.AddSchema(ProtocolSchemaIdentifiers.Version2Error);

            this.Detail = detail;
            this.Status = status.ToString(CultureInfo.InvariantCulture);
            this.ScimType = scimType;
        }
    }
}
