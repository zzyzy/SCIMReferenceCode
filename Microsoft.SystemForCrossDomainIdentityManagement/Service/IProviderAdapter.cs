// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading.Tasks;

    public interface IProviderAdapter<T> where T : Resource
    {
        string SchemaIdentifier { get; }

        /// <summary>
        /// Whether a successful PATCH answers 200 with the updated resource rather than 204.
        /// </summary>
        /// <remarks>
        /// RFC 7644 section 3.5.2 leaves the choice to the service. It is a property of the
        /// resource type's contract, not of its schema, so it is declared here rather than
        /// inferred from <see cref="SchemaIdentifier"/>.
        /// </remarks>
        bool ReturnsResourceOnPatch { get; }

        Task<Resource> Create(HttpRequestMessage request, Resource resource, string correlationIdentifier);
        Task Delete(HttpRequestMessage request, string identifier, string correlationIdentifier);
        Task<QueryResponseBase> Query(
            HttpRequestMessage request,
            IReadOnlyCollection<IFilter> filters,
            IReadOnlyCollection<string> requestedAttributePaths,
            IReadOnlyCollection<string> excludedAttributePaths,
            IPaginationParameters paginationParameters,
            string correlationIdentifier);
        Task<Resource> Replace(HttpRequestMessage request, Resource resource, string correlationIdentifier);
        Task<Resource> Retrieve(
            HttpRequestMessage request,
            string identifier,
            IReadOnlyCollection<string> requestedAttributePaths,
            IReadOnlyCollection<string> excludedAttributePaths,
            string correlationIdentifier);
        Task Update(
            HttpRequestMessage request,
            string identifier,
            PatchRequestBase patchRequest,
            string correlationIdentifier);
    }
}
