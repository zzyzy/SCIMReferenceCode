//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM.WebHostSample.Provider
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    /// <summary>
    /// A provider whose every member throws.
    /// </summary>
    /// <remarks>
    /// The handlers wrap every provider call, including the discovery properties, and turn
    /// what escapes into a SCIM error response. Nothing else can show that: a provider that
    /// works never faults, and a provider that is merely unimplemented throws a specific
    /// exception the handlers map to 501.
    ///
    /// What this proves is the shape of the answer when a provider fails for a reason the
    /// library knows nothing about - a 500 carrying the RFC 7644 section 3.12 error body
    /// rather than an ASP.NET ProblemDetails payload, an empty response, or a stack trace.
    /// The two hosting legs have to agree on that as much as on anything else.
    ///
    /// Selected with SCIM_PROVIDER=faulty.
    /// </remarks>
    public class FaultyProvider : ProviderBase
    {
        private const string Message = "the provider faulted";

        // A different exception per discovery property, because each endpoint maps them
        // separately and a provider is free to throw any of them: NotImplementedException
        // and NotSupportedException both mean "this service does not offer that" and have
        // to answer 501, and an ArgumentException is the client's mistake at 400. One type
        // everywhere would only ever show the catch-all.
        public override IReadOnlyCollection<Core2ResourceType> ResourceTypes =>
            throw new NotImplementedException(FaultyProvider.Message);

        public override IReadOnlyCollection<TypeScheme> Schema =>
            throw new NotSupportedException(FaultyProvider.Message);

        public override ServiceConfigurationBase Configuration =>
            throw new InvalidOperationException(FaultyProvider.Message);

        public override Task<Resource> CreateAsync(Resource resource, string correlationIdentifier)
        {
            throw new InvalidOperationException(FaultyProvider.Message);
        }

        public override Task<Resource> RetrieveAsync(
            IResourceRetrievalParameters parameters,
            string correlationIdentifier)
        {
            throw new InvalidOperationException(FaultyProvider.Message);
        }

        public override Task<Resource[]> QueryAsync(
            IQueryParameters parameters,
            string correlationIdentifier)
        {
            throw new InvalidOperationException(FaultyProvider.Message);
        }

        public override Task<Resource> ReplaceAsync(Resource resource, string correlationIdentifier)
        {
            throw new InvalidOperationException(FaultyProvider.Message);
        }

        public override Task UpdateAsync(IPatch patch, string correlationIdentifier)
        {
            throw new InvalidOperationException(FaultyProvider.Message);
        }

        public override Task DeleteAsync(
            IResourceIdentifier resourceIdentifier,
            string correlationIdentifier)
        {
            throw new InvalidOperationException(FaultyProvider.Message);
        }
    }
}
