//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM.WebHostSample.Provider
{
    using System;
    using System.Threading.Tasks;

    /// <summary>
    /// A provider that implements nothing.
    /// </summary>
    /// <remarks>
    /// What a service looks like before any of it is written. <see cref="ProviderBase"/>
    /// leaves query and replace as virtual methods that throw
    /// <see cref="NotImplementedException"/>, and declares create, retrieve, update and
    /// delete abstract - so signalling those the same way is a provider's job, and this is
    /// how the library expects it to be done.
    ///
    /// The shared request handlers are supposed to turn that into 501 Not Implemented rather
    /// than letting it escape as a 500. That distinction matters to anyone building on this
    /// library - a client can retry around a 501 and cannot around a 500 - and it is the one
    /// behaviour no working provider can demonstrate, because a working provider never throws
    /// it. Selected with SCIM_PROVIDER=unimplemented.
    /// </remarks>
    public class UnimplementedProvider : ProviderBase
    {
        public override Task<Resource> CreateAsync(Resource resource, string correlationIdentifier)
        {
            throw new NotImplementedException();
        }

        public override Task<Resource> RetrieveAsync(
            IResourceRetrievalParameters parameters,
            string correlationIdentifier)
        {
            throw new NotImplementedException();
        }

        public override Task UpdateAsync(IPatch patch, string correlationIdentifier)
        {
            throw new NotImplementedException();
        }

        public override Task DeleteAsync(
            IResourceIdentifier resourceIdentifier,
            string correlationIdentifier)
        {
            throw new NotImplementedException();
        }
    }
}
