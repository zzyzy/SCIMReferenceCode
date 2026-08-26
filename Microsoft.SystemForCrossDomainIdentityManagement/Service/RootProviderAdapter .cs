// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
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

        /// <summary>
        /// Queries every resource type the provider serves, as one result set.
        /// </summary>
        /// <remarks>
        /// RFC 7644 section 3.4.2 permits a query "against the service provider Base URI", and
        /// section 3.2 names the base endpoint's POST query "search from system". The root has
        /// no resources of its own, so a search of it is a search of everything under it:
        /// each resource type is queried unpaginated and the results concatenated, then paged
        /// once over the whole - paging each type separately would return the first page of
        /// each rather than the first page of the result set.
        ///
        /// A type that rejects the filter contributes nothing rather than failing the search.
        /// A filter naming userName cannot be answered by Groups, and there is no reading of
        /// "search everything" under which that is an error rather than an empty match.
        /// </remarks>
        public override async Task<QueryResponseBase> Query(
            HttpRequestMessage request,
            IReadOnlyCollection<IFilter> filters,
            IReadOnlyCollection<string> requestedAttributePaths,
            IReadOnlyCollection<string> excludedAttributePaths,
            IPaginationParameters paginationParameters,
            string correlationIdentifier)
        {
            if (null == request)
            {
                throw new ArgumentNullException(nameof(request));
            }

            List<Resource> matches = new List<Resource>();

            foreach (Core2ResourceType resourceType in this.ReadResourceTypes())
            {
                if (string.IsNullOrWhiteSpace(resourceType?.Schema))
                {
                    continue;
                }

                string path =
                    resourceType.Endpoint?.ToString() ?? new SchemaIdentifier(resourceType.Schema).FindPath();

                // The type's own schema, then the extensions it declares. A provider is free to
                // dispatch on either: this one serves /Users as Core2EnterpriseUser, whose
                // identifier is the enterprise extension's - not the core User schema the
                // resource type advertises. Trying both reads the answer out of the service's
                // own metadata rather than assuming one convention.
                foreach (string schema in RootProviderAdapter.SchemasOf(resourceType))
                {
                    IQueryParameters parameters =
                        new QueryParameters(
                            schema,
                            path,
                            filters ?? Array.Empty<IFilter>(),
                            requestedAttributePaths ?? Array.Empty<string>(),
                            excludedAttributePaths ?? Array.Empty<string>());

                    IRequest<IQueryParameters> query =
                        new QueryRequest(request, parameters, correlationIdentifier, this.Provider.Extensions);

                    try
                    {
                        IReadOnlyCollection<Resource> resources =
                            await this.Provider.QueryAsync(query).ConfigureAwait(false);

                        if (null != resources)
                        {
                            matches.AddRange(resources);
                        }

                        // Answered. Trying the rest would count the same resources twice.
                        break;
                    }
                    catch (ArgumentException)
                    {
                        // The filter names an attribute this type does not define.
                        break;
                    }
                    catch (NotSupportedException)
                    {
                        // The provider will not answer this filter for this type.
                        break;
                    }
                    catch (NotImplementedException)
                    {
                        // Not the identifier this provider dispatches the type on; try the next.
                    }
                }
            }

            int startIndex = paginationParameters?.StartIndex ?? 1;
            if (startIndex < 1)
            {
                startIndex = 1;
            }

            int? count = paginationParameters?.Count;
            if (count.HasValue && count.Value < 0)
            {
                count = 0;
            }

            IEnumerable<Resource> page = matches.Skip(startIndex - 1);
            if (count.HasValue)
            {
                page = page.Take(count.Value);
            }

            Resource[] paged = page.ToArray();

            QueryResponseBase result = new QueryResponse((IReadOnlyCollection<Resource>)paged);
            result.TotalResults = matches.Count;
            result.ItemsPerPage = paged.Length;
            result.StartIndex = paged.Length > 0 ? startIndex : (int?)null;

            return result;
        }

        private static IEnumerable<string> SchemasOf(Core2ResourceType resourceType)
        {
            yield return resourceType.Schema;

            if (null == resourceType.SchemaExtensions)
            {
                yield break;
            }

            foreach (SchemaExtension extension in resourceType.SchemaExtensions)
            {
                if (!string.IsNullOrWhiteSpace(extension?.Schema))
                {
                    yield return extension.Schema;
                }
            }
        }

        private IReadOnlyCollection<Core2ResourceType> ReadResourceTypes()
        {
            try
            {
                return
                    this.Provider?.ResourceTypes?.OfType<Core2ResourceType>().ToArray()
                    ?? Array.Empty<Core2ResourceType>();
            }
            catch (NotImplementedException)
            {
                return Array.Empty<Core2ResourceType>();
            }
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
