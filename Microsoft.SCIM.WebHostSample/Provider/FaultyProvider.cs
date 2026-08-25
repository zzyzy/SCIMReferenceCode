//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM.WebHostSample.Provider
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using System.Web.Http;

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
    /// A provider is also entitled to fault with a status it chose: 403 for a caller its
    /// store will not serve, 501 for an operation it does not offer, 429 for one it is
    /// shedding. A request naming <c>status-{code}</c> in its identifier, userName or
    /// displayName faults with that code, so each verb can be asked whether it carries the
    /// status through or rewrites it. A request naming no status still faults the ordinary
    /// way, which is what keeps the catch-all covered.
    ///
    /// Selected with SCIM_PROVIDER=faulty.
    /// </remarks>
    public class FaultyProvider : ProviderBase
    {
        private const string Message = "the provider faulted";

        private static readonly Regex ChosenStatus =
            new Regex(@"status-(?<code>\d{3})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
            FaultyProvider.FaultAsRequested(FaultyProvider.Markers(resource));
            throw new InvalidOperationException(FaultyProvider.Message);
        }

        public override Task<Resource> RetrieveAsync(
            IResourceRetrievalParameters parameters,
            string correlationIdentifier)
        {
            FaultyProvider.FaultAsRequested(parameters?.ResourceIdentifier?.Identifier);
            throw new InvalidOperationException(FaultyProvider.Message);
        }

        public override Task<Resource[]> QueryAsync(
            IQueryParameters parameters,
            string correlationIdentifier)
        {
            FaultyProvider.FaultAsRequested(FaultyProvider.Markers(parameters));
            throw new InvalidOperationException(FaultyProvider.Message);
        }

        public override Task<Resource> ReplaceAsync(Resource resource, string correlationIdentifier)
        {
            FaultyProvider.FaultAsRequested(FaultyProvider.Markers(resource));
            throw new InvalidOperationException(FaultyProvider.Message);
        }

        public override Task UpdateAsync(IPatch patch, string correlationIdentifier)
        {
            FaultyProvider.FaultAsRequested(patch?.ResourceIdentifier?.Identifier);
            throw new InvalidOperationException(FaultyProvider.Message);
        }

        public override Task DeleteAsync(
            IResourceIdentifier resourceIdentifier,
            string correlationIdentifier)
        {
            FaultyProvider.FaultAsRequested(resourceIdentifier?.Identifier);
            throw new InvalidOperationException(FaultyProvider.Message);
        }

        /// <summary>Faults with the status the request named, if it named one.</summary>
        private static void FaultAsRequested(params string[] candidates)
        {
            foreach (string candidate in candidates ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                Match match = FaultyProvider.ChosenStatus.Match(candidate);
                if (!match.Success)
                {
                    continue;
                }

                int code = int.Parse(match.Groups["code"].Value, CultureInfo.InvariantCulture);
                throw new HttpResponseException((HttpStatusCode)code);
            }
        }

        /// <summary>The places in a resource a caller can put the marker.</summary>
        private static string[] Markers(Resource resource)
        {
            switch (resource)
            {
                case Core2UserBase user:
                    return new[] { user.Identifier, user.UserName, user.ExternalIdentifier };
                case Core2Group group:
                    return new[] { group.Identifier, group.DisplayName, group.ExternalIdentifier };
                default:
                    return new[] { resource?.Identifier };
            }
        }

        /// <summary>The places in a query a caller can put the marker.</summary>
        private static string[] Markers(IQueryParameters parameters)
        {
            return
                (parameters?.AlternateFilters ?? Array.Empty<IFilter>())
                .Select((IFilter filter) => filter?.ComparisonValue)
                .ToArray();
        }
    }
}
