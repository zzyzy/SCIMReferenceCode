// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using System.Web.Http;

    internal class RootProviderAdapter : ProviderAdapterTemplate<Resource>
    {
        public RootProviderAdapter(IProvider provider)
            : base(provider)
        {
        }

        public override string SchemaIdentifier
        {
            get
            {
                return SchemaIdentifiers.None;
            }
        }

        public override Task<Resource> Create(
            HttpRequestMessage request,
            Resource resource,
            string correlationIdentifier)
        {
            throw new HttpResponseException(HttpStatusCode.NotImplemented);
        }

        public override IResourceIdentifier CreateResourceIdentifier(string identifier)
        {
            throw new HttpResponseException(HttpStatusCode.NotImplemented);
        }

        public override Task Delete(
            HttpRequestMessage request,
            string identifier,
            string correlationIdentifier)
        {
            throw new HttpResponseException(HttpStatusCode.NotImplemented);
        }

        public override Task<Resource> Replace(
            HttpRequestMessage request,
            Resource resource, string
            correlationIdentifier)
        {
            throw new HttpResponseException(HttpStatusCode.NotImplemented);
        }

        /// <summary>
        /// The service root holds no resources of its own, so a request naming one under it
        /// names nothing.
        /// </summary>
        /// <remarks>
        /// 404, not 501. RFC 7644 section 3.12 gives 404 for "specified resource or endpoint
        /// does not exist", which is what /scim/&lt;anything&gt; is - resources live under
        /// /scim/Users and /scim/Groups. 501 said instead that the service root retrieves
        /// resources by identifier but has not implemented it yet, and a client cannot tell
        /// that from a URL it should stop asking for.
        /// </remarks>
        public override Task<Resource> Retrieve(
            HttpRequestMessage request,
            string identifier,
            IReadOnlyCollection<string> requestedAttributePaths,
            IReadOnlyCollection<string> excludedAttributePaths,
            string correlationIdentifier)
        {
            throw new HttpResponseException(HttpStatusCode.NotFound);
        }

        public override Task Update(
            HttpRequestMessage request,
            string identifier,
            PatchRequestBase patchRequest,
            string correlationIdentifier)
        {
            throw new HttpResponseException(HttpStatusCode.NotImplemented);
        }
    }
}
