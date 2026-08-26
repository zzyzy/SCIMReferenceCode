// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using System.Web.Http;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// The hosting-neutral SCIM resource endpoint. Holds the orchestration, the
    /// exception-to-status mapping and the failure logging that used to live in
    /// <c>ControllerTemplate&lt;T&gt;</c>.
    /// </summary>
    /// <remarks>
    /// This exists so that the ASP.NET Web API (net48) and ASP.NET Core (net10.0)
    /// controllers are thin adapters over one implementation rather than two copies
    /// of the same 760 lines.
    ///
    /// Several paths deliberately <c>throw</c> rather than return a
    /// <see cref="ScimResult"/>. Each host must install an exception filter that maps
    /// an unhandled <see cref="HttpResponseException"/> to its status code plus a
    /// <see cref="Core2Error"/> body; without one, today's 404/501 responses become
    /// 500s. See docs/scim-conformance.md section 4, requirement X1.
    /// </remarks>
    public class ScimRequestHandler<T> where T : Resource
    {
        private readonly Func<IProvider, IProviderAdapter<T>> adaptProvider;

        public ScimRequestHandler(
            IProvider provider,
            ILogger logger,
            Func<IProvider, IProviderAdapter<T>> adaptProvider)
        {
            this.Provider = provider;
            this.Logger = logger;
            this.adaptProvider = adaptProvider ?? throw new ArgumentNullException(nameof(adaptProvider));
        }

        protected ILogger Logger
        {
            get;
        }

        protected IProvider Provider
        {
            get;
        }

        protected IProviderAdapter<T> AdaptProvider()
        {
            return this.adaptProvider(this.Provider);
        }

        /// <summary>
        /// Fills in <c>meta.location</c> on a resource that does not already carry one.
        /// </summary>
        /// <remarks>
        /// RFC 7643 section 3.1 makes <c>location</c> part of the common <c>meta</c> attribute,
        /// but a provider cannot compute it: the URI depends on where the service is hosted,
        /// which only the request knows. So it is filled in here, from the same two calls that
        /// produce the <c>Location</c> header on create - which also guarantees the header and
        /// the body agree. A provider that sets its own value keeps it.
        /// </remarks>
        protected static void EnsureMetadataLocation(HttpRequestMessage request, Resource resource)
        {
            if (null == request || null == resource)
            {
                return;
            }

            // Before the metadata block, not after it: a provider that already set
            // meta.location returns early below, and the cross-references would then never
            // be filled in on exactly the resources whose provider was most thorough.
            ScimRequestHandler<T>.EnsureReferences(request, resource);

            Core2Metadata metadata = (resource as Core2UserBase)?.Metadata ?? (resource as Core2GroupBase)?.Metadata;

            if (null == metadata || !string.IsNullOrWhiteSpace(metadata.Location))
            {
                return;
            }

            try
            {
                metadata.Location =
                    resource.GetResourceIdentifier(request.GetBaseResourceIdentifier()).AbsoluteUri;
            }
            catch (ArgumentException)
            {
                // The request URI does not contain the SCIM interface segment, so no absolute
                // resource URI can be derived. Leaving location unset is better than guessing.
            }
        }

        /// <summary>
        /// Fills in the <c>$ref</c> of a resource's cross-references.
        /// </summary>
        /// <remarks>
        /// RFC 7643 gives both <c>groups</c> (section 4.1.2) and <c>members</c> (section 4.2)
        /// a <c>$ref</c> sub-attribute holding the URI of the resource on the other side. Only
        /// the request knows the service's base URI, which is why this sits here beside
        /// <c>meta.location</c> rather than in a provider: a provider that tried to build the
        /// URI would have to be told where it is being served from, and every provider would
        /// have to be told separately.
        ///
        /// A reference the provider set is left alone. This only supplies the ones it could
        /// not.
        /// </remarks>
        protected static void EnsureReferences(HttpRequestMessage request, Resource resource)
        {
            if (null == request || null == resource)
            {
                return;
            }

            Uri baseResource;
            try
            {
                baseResource = request.GetBaseResourceIdentifier();
            }
            catch (ArgumentException)
            {
                return;
            }

            if (null == baseResource)
            {
                return;
            }

            if (resource is Core2UserBase user && null != user.Groups)
            {
                foreach (UserGroup group in user.Groups)
                {
                    if (string.IsNullOrWhiteSpace(group?.Reference) && !string.IsNullOrWhiteSpace(group?.Value))
                    {
                        group.Reference =
                            ScimRequestHandler<T>.ComposeReference(baseResource, ProtocolConstants.PathGroups, group.Value);
                    }
                }
            }

            if (resource is Core2GroupBase groupResource && null != groupResource.Members)
            {
                foreach (Member member in groupResource.Members)
                {
                    if (string.IsNullOrWhiteSpace(member?.Reference) && !string.IsNullOrWhiteSpace(member?.Value))
                    {
                        member.Reference =
                            ScimRequestHandler<T>.ComposeReference(baseResource, ProtocolConstants.PathUsers, member.Value);
                    }
                }
            }
        }

        private static string ComposeReference(Uri baseResource, string path, string identifier)
        {
            // GetBaseResourceIdentifier strips the SCIM interface segment, so it has to be put
            // back. meta.location goes through ProtocolExtensions.ComposeTypeIdentifier, which
            // does the same - omitting it here produced references that 404 while the
            // location beside them resolved.
            string origin = baseResource.AbsoluteUri.TrimEnd('/');
            string prefix = ScimPath.Prefix;
            return
                origin
                + ServiceConstants.SeparatorSegments
                + (string.IsNullOrEmpty(prefix)
                    ? string.Empty
                    : prefix + ServiceConstants.SeparatorSegments)
                + path
                + ServiceConstants.SeparatorSegments
                + Uri.EscapeDataString(identifier);
        }

        /// <summary>Applies <see cref="EnsureMetadataLocation"/> across a query response.</summary>
        protected static void EnsureMetadataLocation(HttpRequestMessage request, QueryResponseBase response)
        {
            if (null == response?.Resources)
            {
                return;
            }

            foreach (Resource resource in response.Resources)
            {
                ScimRequestHandler<T>.EnsureMetadataLocation(request, resource);
            }
        }

        // Ported from ControllerTemplate<T>.Delete.
        public virtual async Task<ScimResult> DeleteAsync(HttpRequestMessage request, string identifier)
        {
            string correlationIdentifier = null;
            try
            {
                if (string.IsNullOrWhiteSpace(identifier))
                {
                    return ScimResult.Status(HttpStatusCode.BadRequest);
                }

                identifier = Uri.UnescapeDataString(identifier);
                if (!request.TryGetRequestIdentifier(out correlationIdentifier))
                {
                    throw new HttpResponseException(HttpStatusCode.InternalServerError);
                }

                IProviderAdapter<T> provider = this.AdaptProvider();
                await provider.Delete(request, identifier, correlationIdentifier).ConfigureAwait(false);
                return ScimResult.NoContent();
            }
            catch (ArgumentException argumentException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.DeleteArgumentException,
                    argumentException,
                    correlationIdentifier,
                    request);

                return ScimResult.Status(HttpStatusCode.BadRequest);
            }
            catch (HttpResponseException responseException)
            {
                if (responseException.Response?.StatusCode == HttpStatusCode.NotFound)
                {
                    return ScimResult.Status(HttpStatusCode.NotFound);
                }

                throw;
            }
            catch (NotImplementedException notImplementedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.DeleteNotImplementedException,
                    notImplementedException,
                    correlationIdentifier,
                    request);

                throw new HttpResponseException(HttpStatusCode.NotImplemented);
            }
            catch (NotSupportedException notSupportedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.DeleteNotSupportedException,
                    notSupportedException,
                    correlationIdentifier,
                    request);

                throw new HttpResponseException(HttpStatusCode.NotImplemented);
            }
            catch (Exception exception)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.DeleteException,
                    exception,
                    correlationIdentifier,
                    request);

                // A SCIM error body, as the query and replace handlers already answer for
                // the same exception. Rethrowing let a provider's exception out of the SCIM
                // layer, where the host decided what to do with it - a plain-text stack trace
                // under the developer exception page, an empty 500 without it, and something
                // different again on the other hosting leg.
                return ScimResult.Error(HttpStatusCode.InternalServerError, exception.Message);
            }
        }

        // Ported from ControllerTemplate<T>.Get().
        /// <summary>
        /// Answers a query made with POST to a "/.search" endpoint, per RFC 7644 section 3.4.3.
        /// </summary>
        /// <remarks>
        /// The body carries the parameters section 3.4.2 defines for the query string, and the
        /// response is "returned as specified in Section 3.4.2" - the same query, arriving
        /// differently. So the body is rendered back into a query string and answered by
        /// <see cref="QueryAsync"/>: one filter parser, one pagination path, one projection,
        /// and no second implementation to drift from the first.
        /// </remarks>
        public virtual Task<ScimResult> SearchAsync(HttpRequestMessage request, SearchRequest search)
        {
            if (null == request)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (null == search)
            {
                return
                    Task.FromResult(
                        ScimResult.Error(
                            HttpStatusCode.BadRequest,
                            SystemForCrossDomainIdentityManagementProtocolResources.ExceptionInvalidRequest,
                            ScimTypes.InvalidSyntax));
            }

            // RFC 7644 section 3.4.3: "Query requests MUST be identified using the following
            // URI". A body that names something else is not a query request, and answering it
            // as one would let a client's mistake read as a successful search of everything.
            if (null == search.Schemas
                || !search.Schemas.Any(
                        (string item) =>
                            string.Equals(
                                item,
                                ProtocolSchemaIdentifiers.Version2SearchRequest,
                                StringComparison.OrdinalIgnoreCase)))
            {
                return
                    Task.FromResult(
                        ScimResult.Error(
                            HttpStatusCode.BadRequest,
                            SystemForCrossDomainIdentityManagementProtocolResources.ExceptionInvalidRequest,
                            ScimTypes.InvalidSyntax));
            }

            UriBuilder address = new UriBuilder(request.RequestUri);

            // The endpoint the query is against, without the ".search" that marked the POST as
            // one. ResourceQuery reads only the query string, but the address is also what the
            // response's own paging links and metadata locations are built from.
            if (address.Path.EndsWith(ServiceConstants.PathSegmentSearch, StringComparison.OrdinalIgnoreCase))
            {
                address.Path =
                    address.Path.Substring(0, address.Path.Length - ServiceConstants.PathSegmentSearch.Length)
                        .TrimEnd('/');
            }

            address.Query = search.ToQueryString();

            HttpRequestMessage query = new HttpRequestMessage(HttpMethod.Get, address.Uri);

            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
            {
                query.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return this.QueryAsync(query);
        }

        public virtual async Task<ScimResult> QueryAsync(HttpRequestMessage request)
        {
            string correlationIdentifier = null;
            try
            {
                if (!request.TryGetRequestIdentifier(out correlationIdentifier))
                {
                    throw new HttpResponseException(HttpStatusCode.InternalServerError);
                }

                IResourceQuery resourceQuery = new ResourceQuery(request.RequestUri);
                IProviderAdapter<T> provider = this.AdaptProvider();
                QueryResponseBase result =
                    await provider
                            .Query(
                                request,
                                resourceQuery.Filters,
                                resourceQuery.Attributes,
                                resourceQuery.ExcludedAttributes,
                                resourceQuery.PaginationParameters,
                                correlationIdentifier)
                            .ConfigureAwait(false);

                ScimRequestHandler<T>.EnsureMetadataLocation(request, result);

                // A provider may or may not have honoured the projection parameters, so it is
                // applied here as well: RFC 7644 section 3.9 is the hosting layer's promise,
                // not the store's. Projecting an already-projected body is a no-op.
                return
                    ScimResult.Ok(
                        ScimProjection.Apply(
                            result,
                            resourceQuery.Attributes,
                            resourceQuery.ExcludedAttributes));
            }
            catch (ArgumentException argumentException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.QueryArgumentException,
                    argumentException,
                    correlationIdentifier,
                    request);

                return ScimResult.Error(HttpStatusCode.BadRequest, argumentException.Message);
            }
            catch (NotImplementedException notImplementedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.QueryNotImplementedException,
                    notImplementedException,
                    correlationIdentifier,
                    request);

                return ScimResult.Error(HttpStatusCode.NotImplemented, notImplementedException.Message);
            }
            catch (NotSupportedException notSupportedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.QueryNotSupportedException,
                    notSupportedException,
                    correlationIdentifier,
                    request);

                // A provider rejects a filter it cannot honour by throwing NotSupportedException.
                return ScimResult.Error(
                    HttpStatusCode.BadRequest,
                    notSupportedException.Message,
                    ScimTypes.InvalidFilter);
            }
            catch (HttpResponseException responseException)
            {
                // The thrown status is the answer. This used to return 500 for anything that
                // was not a 404, which swallowed the 400 a malformed filter raises and any
                // 400/403 a provider signals from its query path.
                HttpStatusCode statusCode =
                    responseException.Response?.StatusCode ?? HttpStatusCode.InternalServerError;

                if (statusCode != HttpStatusCode.NotFound)
                {
                    this.Logger.LogScimFailure(
                        ScimEventIds.GetException,
                        responseException.InnerException ?? responseException,
                        correlationIdentifier,
                        request);
                }

                return ScimResult.FromException(responseException);
            }
            catch (Exception exception)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.QueryException,
                    exception,
                    correlationIdentifier,
                    request);

                return ScimResult.Error(HttpStatusCode.InternalServerError, exception.Message);
            }
        }

        // Ported from ControllerTemplate<T>.Get(string identifier).
        public virtual async Task<ScimResult> RetrieveAsync(HttpRequestMessage request, string identifier)
        {
            string correlationIdentifier = null;
            try
            {
                if (string.IsNullOrWhiteSpace(identifier))
                {
                    return ScimResult.Error(HttpStatusCode.BadRequest, SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidIdentifier);
                }

                if (!request.TryGetRequestIdentifier(out correlationIdentifier))
                {
                    throw new HttpResponseException(HttpStatusCode.InternalServerError);
                }

                IResourceQuery resourceQuery = new ResourceQuery(request.RequestUri);
                if (resourceQuery.Filters.Any())
                {
                    if (resourceQuery.Filters.Count != 1)
                    {
                        return ScimResult.Error(HttpStatusCode.BadRequest, SystemForCrossDomainIdentityManagementServiceResources.ExceptionFilterCount);
                    }

                    IFilter filter = new Filter(AttributeNames.Identifier, ComparisonOperator.Equals, identifier);
                    filter.AdditionalFilter = resourceQuery.Filters.Single();
                    IReadOnlyCollection<IFilter> filters =
                        new IFilter[]
                            {
                                filter
                            };
                    IResourceQuery effectiveQuery =
                        new ResourceQuery(
                            filters,
                            resourceQuery.Attributes,
                            resourceQuery.ExcludedAttributes);
                    IProviderAdapter<T> provider = this.AdaptProvider();
                    QueryResponseBase queryResponse;

                    try
                    {
                        queryResponse =
                            await provider
                                .Query(
                                    request,
                                    effectiveQuery.Filters,
                                    effectiveQuery.Attributes,
                                    effectiveQuery.ExcludedAttributes,
                                    effectiveQuery.PaginationParameters,
                                    correlationIdentifier)
                                .ConfigureAwait(false);
                    }
                    catch (NotSupportedException unsupportedFilter)
                    {
                        // invalidFilter, as the collection query already answers for the
                        // same refusal. The request only reaches the provider because it
                        // carried a filter, so a provider declining it is declining the
                        // filter - and reporting that as invalidValue made one mistake
                        // look like two different ones depending on the URL it arrived on.
                        this.Logger.LogScimFailure(
                            ScimEventIds.GetNotSupportedException,
                            unsupportedFilter,
                            correlationIdentifier,
                            request);

                        throw new ScimTypedException(
                            HttpStatusCode.BadRequest,
                            ScimTypes.InvalidFilter,
                            unsupportedFilter.Message);
                    }
                    if (!queryResponse.Resources.Any())
                    {
                        return ScimResult.Error(HttpStatusCode.NotFound, string.Format(SystemForCrossDomainIdentityManagementServiceResources.ResourceNotFoundTemplate, identifier));
                    }

                    Resource result = queryResponse.Resources.Single();
                    ScimRequestHandler<T>.EnsureMetadataLocation(request, result);
                    return
                        ScimResult.Ok(
                            ScimProjection.Apply(
                                result,
                                resourceQuery.Attributes,
                                resourceQuery.ExcludedAttributes));
                }
                else
                {
                    IProviderAdapter<T> provider = this.AdaptProvider();
                    Resource result =
                        await provider
                            .Retrieve(
                                request,
                                identifier,
                                resourceQuery.Attributes,
                                resourceQuery.ExcludedAttributes,
                                correlationIdentifier)
                            .ConfigureAwait(false);
                    if (null == result)
                    {
                        return ScimResult.Error(HttpStatusCode.NotFound, string.Format(SystemForCrossDomainIdentityManagementServiceResources.ResourceNotFoundTemplate, identifier));
                    }

                    ScimRequestHandler<T>.EnsureMetadataLocation(request, result);

                    return
                        ScimResult.Ok(
                            ScimProjection.Apply(
                                result,
                                resourceQuery.Attributes,
                                resourceQuery.ExcludedAttributes));
                }
            }
            catch (ArgumentException argumentException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.GetArgumentException,
                    argumentException,
                    correlationIdentifier,
                    request);

                return ScimResult.Error(HttpStatusCode.BadRequest, argumentException.Message);
            }
            catch (NotImplementedException notImplementedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.GetNotImplementedException,
                    notImplementedException,
                    correlationIdentifier,
                    request);

                return ScimResult.Error(HttpStatusCode.NotImplemented, notImplementedException.Message);
            }
            catch (NotSupportedException notSupportedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.GetNotSupportedException,
                    notSupportedException,
                    correlationIdentifier,
                    request);

                return ScimResult.Error(HttpStatusCode.BadRequest, notSupportedException.Message);
            }
            catch (HttpResponseException responseException)
            {
                if (responseException.Response?.StatusCode != HttpStatusCode.NotFound)
                {
                    this.Logger.LogScimFailure(
                        ScimEventIds.GetException,
                        responseException.InnerException ?? responseException,
                        correlationIdentifier,
                        request);
                }

                if (responseException.Response?.StatusCode == HttpStatusCode.NotFound)
                {
                    return ScimResult.Error(HttpStatusCode.NotFound, string.Format(SystemForCrossDomainIdentityManagementServiceResources.ResourceNotFoundTemplate, identifier));
                }

                // The status the provider chose. Reporting every one of them as 500 said
                // the service had failed, when a 403 or a 501 is a deliberate answer the
                // client can act on.
                return ScimResult.FromException(responseException);
            }
            catch (Exception exception)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.GetException,
                    exception,
                    correlationIdentifier,
                    request);

                return ScimResult.Error(HttpStatusCode.InternalServerError, exception.Message);
            }
        }

        // Ported from ControllerTemplate<T>.Patch.
        public virtual async Task<ScimResult> PatchAsync(
            HttpRequestMessage request,
            string identifier,
            PatchRequest2 patchRequest)
        {
            string correlationIdentifier = null;

            try
            {
                if (string.IsNullOrWhiteSpace(identifier))
                {
                    return ScimResult.Status(HttpStatusCode.BadRequest);
                }

                identifier = Uri.UnescapeDataString(identifier);

                if (null == patchRequest)
                {
                    return ScimResult.Status(HttpStatusCode.BadRequest, ScimTypes.InvalidSyntax);
                }

                if (!request.TryGetRequestIdentifier(out correlationIdentifier))
                {
                    throw new HttpResponseException(HttpStatusCode.InternalServerError);
                }

                IProviderAdapter<T> provider = this.AdaptProvider();
                await provider.Update(request, identifier, patchRequest, correlationIdentifier).ConfigureAwait(false);

                if (provider.ReturnsResourceOnPatch)
                {
                    return await this.RetrieveAsync(request, identifier).ConfigureAwait(false);
                }
                else
                    return ScimResult.NoContent();
            }
            catch (ArgumentException argumentException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.PatchArgumentException,
                    argumentException,
                    correlationIdentifier,
                    request);

                // On a PATCH the argument that is nearly always wrong is the operation path.
                return ScimResult.Error(
                    HttpStatusCode.BadRequest,
                    argumentException.Message,
                    ScimTypes.InvalidPath);
            }
            catch (NotImplementedException notImplementedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.PatchNotImplementedException,
                    notImplementedException,
                    correlationIdentifier,
                    request);

                throw new HttpResponseException(HttpStatusCode.NotImplemented);
            }
            catch (NotSupportedException notSupportedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.PatchNotSupportedException,
                    notSupportedException,
                    correlationIdentifier,
                    request);

                throw new HttpResponseException(HttpStatusCode.NotImplemented);
            }
            catch (HttpResponseException responseException)
            {
                if (responseException.Response?.StatusCode == HttpStatusCode.NotFound)
                {
                    return ScimResult.Status(HttpStatusCode.NotFound);
                }
                else
                {
                    this.Logger.LogScimFailure(
                        ScimEventIds.GetException,
                        responseException.InnerException ?? responseException,
                        correlationIdentifier,
                        request);
                }

                throw;
            }
            catch (Exception exception)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.PatchException,
                    exception,
                    correlationIdentifier,
                    request);

                // A SCIM error body, as the query and replace handlers already answer for
                // the same exception. Rethrowing let a provider's exception out of the SCIM
                // layer, where the host decided what to do with it - a plain-text stack trace
                // under the developer exception page, an empty 500 without it, and something
                // different again on the other hosting leg.
                return ScimResult.Error(HttpStatusCode.InternalServerError, exception.Message);
            }
        }

        // Ported from ControllerTemplate<T>.Post.

        /// <summary>
        /// Whether every multi-valued attribute of <paramref name="resource"/> marks at most one
        /// value primary.
        /// </summary>
        /// <remarks>
        /// RFC 7643 section 2.4: the primary sub-attribute must appear no more than once per
        /// multi-valued attribute. Two values both claiming to be primary leaves every consumer
        /// to pick one arbitrarily, and two consumers need not pick the same one.
        /// </remarks>
        private static bool HasSinglePrimary(Resource resource, out string attributeName)
        {
            attributeName = null;

            if (!(resource is Core2UserBase user))
            {
                return true;
            }

            KeyValuePair<string, IEnumerable<TypedItem>>[] multiValued =
                new[]
                {
                    new KeyValuePair<string, IEnumerable<TypedItem>>(
                        AttributeNames.ElectronicMailAddresses, user.ElectronicMailAddresses),
                    new KeyValuePair<string, IEnumerable<TypedItem>>(
                        AttributeNames.PhoneNumbers, user.PhoneNumbers),
                    new KeyValuePair<string, IEnumerable<TypedItem>>(
                        AttributeNames.Ims, user.InstantMessagings),
                    new KeyValuePair<string, IEnumerable<TypedItem>>(
                        AttributeNames.Roles, user.Roles),
                };

            foreach (KeyValuePair<string, IEnumerable<TypedItem>> attribute in multiValued)
            {
                if (null != attribute.Value
                    && attribute.Value.Count((TypedItem item) => item.Primary) > 1)
                {
                    attributeName = attribute.Key;
                    return false;
                }
            }

            return true;
        }

        public virtual async Task<ScimResult> CreateAsync(HttpRequestMessage request, T resource)
        {
            string correlationIdentifier = null;

            try
            {
                if (null == resource)
                {
                    // The body did not bind: unparseable, or not the request schema.
                    return ScimResult.Status(HttpStatusCode.BadRequest, ScimTypes.InvalidSyntax);
                }

                if (!ScimRequestHandler<T>.HasSinglePrimary(resource, out string offendingAttribute))
                {
                    return ScimResult.Error(
                        HttpStatusCode.BadRequest,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidOperationTemplate,
                            offendingAttribute),
                        ScimTypes.InvalidValue);
                }

                if (!request.TryGetRequestIdentifier(out correlationIdentifier))
                {
                    throw new HttpResponseException(HttpStatusCode.InternalServerError);
                }

                IProviderAdapter<T> provider = this.AdaptProvider();
                Resource result = await provider.Create(request, resource, correlationIdentifier).ConfigureAwait(false);

                // RFC 7644 section 3.3: 201 with a single Location header naming the new
                // resource. The pre-port code wrote Location twice - once by hand and once
                // via CreatedAtAction, which derived a second URI from MVC routing that has
                // no ASP.NET Web API equivalent. See docs/scim-conformance.md section 5
                // item 2.
                Uri baseResourceIdentifier = request.GetBaseResourceIdentifier();
                Uri location = result.GetResourceIdentifier(baseResourceIdentifier);
                ScimRequestHandler<T>.EnsureMetadataLocation(request, result);
                return ScimResult.Created(result, location);
            }
            catch (ArgumentException argumentException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.PostArgumentException,
                    argumentException,
                    correlationIdentifier,
                    request);

                return ScimResult.Status(HttpStatusCode.BadRequest);
            }
            catch (NotImplementedException notImplementedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.PostNotImplementedException,
                    notImplementedException,
                    correlationIdentifier,
                    request);

                throw new HttpResponseException(HttpStatusCode.NotImplemented);
            }
            catch (NotSupportedException notSupportedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.PostNotSupportedException,
                    notSupportedException,
                    correlationIdentifier,
                    request);

                throw new HttpResponseException(HttpStatusCode.NotImplemented);
            }
            catch (HttpResponseException httpResponseException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.PostNotSupportedException,
                    httpResponseException,
                    correlationIdentifier,
                    request);

                // No special case for 409. A bare status was returned here, which discarded
                // the scimType and the detail a provider had chosen - so every conflict on
                // create read as "uniqueness / Conflict", including the ones that are not
                // uniqueness violations at all and whose detail said what to do about it.
                // FromException degrades to exactly that answer when the provider supplied
                // nothing, so nothing is lost by letting conflicts take the same path as
                // every other status.
                //
                // The status the provider chose, not a blanket 400. A provider is
                // entitled to answer 403 for a caller its store will not serve, 501 for
                // an operation it does not offer, or 429 for one it is shedding, and
                // rewriting all three as "your request was malformed" told the client
                // something untrue and not retryable.
                return ScimResult.FromException(httpResponseException);
            }
            catch (Exception exception)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.PostException,
                    exception,
                    correlationIdentifier,
                    request);

                // A SCIM error body, as the query and replace handlers already answer for
                // the same exception. Rethrowing let a provider's exception out of the SCIM
                // layer, where the host decided what to do with it - a plain-text stack trace
                // under the developer exception page, an empty 500 without it, and something
                // different again on the other hosting leg.
                return ScimResult.Error(HttpStatusCode.InternalServerError, exception.Message);
            }
        }

        // Ported from ControllerTemplate<T>.Put.
        public virtual async Task<ScimResult> ReplaceAsync(HttpRequestMessage request, T resource, string identifier)
        {
            string correlationIdentifier = null;

            try
            {
                if (null == resource)
                {
                    return ScimResult.Error(
                        HttpStatusCode.BadRequest,
                        SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidResource,
                        ScimTypes.InvalidSyntax);
                }

                if (string.IsNullOrEmpty(identifier))
                {
                    return ScimResult.Error(HttpStatusCode.BadRequest, SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidIdentifier);
                }

                if (!request.TryGetRequestIdentifier(out correlationIdentifier))
                {
                    throw new HttpResponseException(HttpStatusCode.InternalServerError);
                }

                // RFC 7644 section 3.5.1: the request URI identifies the resource. id is
                // read-only, so a client may legitimately omit it from the body - and did,
                // which left the provider with an unidentified resource and turned a replace
                // of something that does not exist into 400 rather than 404. A body that
                // names a different resource is a different matter, and is refused.
                if (
                       !string.IsNullOrWhiteSpace(resource.Identifier)
                    && !string.Equals(resource.Identifier, identifier, StringComparison.OrdinalIgnoreCase))
                {
                    return ScimResult.Error(
                        HttpStatusCode.BadRequest,
                        SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidIdentifier,
                        ScimTypes.Mutability);
                }

                if (!ScimRequestHandler<T>.HasSinglePrimary(resource, out string offendingAttribute))
                {
                    return ScimResult.Error(
                        HttpStatusCode.BadRequest,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidOperationTemplate,
                            offendingAttribute),
                        ScimTypes.InvalidValue);
                }

                resource.Identifier = identifier;

                IProviderAdapter<T> provider = this.AdaptProvider();
                Resource result = await provider.Replace(request, resource, correlationIdentifier).ConfigureAwait(false);

                // As the create, read and query paths already do. Without it a replace was
                // the one response whose meta.location and cross-references depended on the
                // provider having built them itself.
                ScimRequestHandler<T>.EnsureMetadataLocation(request, result);

                // RFC 7644 section 3.5.1: a successful replace is 200, not 201. The pre-port
                // code set 201 unconditionally in ConfigureResponse and then returned Ok(),
                // leaving the wire status dependent on result-execution ordering. D15.
                return ScimResult.Ok(result);
            }
            catch (ArgumentException argumentException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.PutArgumentException,
                    argumentException,
                    correlationIdentifier,
                    request);

                return ScimResult.Error(HttpStatusCode.BadRequest, argumentException.Message);
            }
            catch (NotImplementedException notImplementedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.PutNotImplementedException,
                    notImplementedException,
                    correlationIdentifier,
                    request);

                return ScimResult.Error(HttpStatusCode.NotImplemented, notImplementedException.Message);
            }
            catch (NotSupportedException notSupportedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.PutNotSupportedException,
                    notSupportedException,
                    correlationIdentifier,
                    request);

                // 501, as POST, GET, PATCH and DELETE all answer for the same exception.
                // ProviderBase's own replace throws it, so a provider that has not written
                // one was telling the client its request was malformed - which it was not,
                // and which a client cannot usefully retry.
                return ScimResult.Error(HttpStatusCode.NotImplemented, notSupportedException.Message);
            }
            catch (HttpResponseException httpResponseException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.PostNotSupportedException,
                    httpResponseException,
                    correlationIdentifier,
                    request);

                if (httpResponseException.Response.StatusCode == HttpStatusCode.NotFound)
                {
                    return ScimResult.Error(HttpStatusCode.NotFound, string.Format(SystemForCrossDomainIdentityManagementServiceResources.ResourceNotFoundTemplate, identifier));
                }

                // The status the provider chose, with the scimType and detail it chose.
                // Not the exception's Message, though: an HttpResponseException thrown for
                // its status alone carries "Exception of type '...' was thrown.", which is
                // no use as an error detail - FromException prefers the typed detail, then
                // the reason phrase, and never the message. The 409 special case that used
                // to sit here answered a fixed "invalid request" for every conflict,
                // throwing away both.
                return ScimResult.FromException(httpResponseException);
            }
            catch (Exception exception)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.PutException,
                    exception,
                    correlationIdentifier,
                    request);

                return ScimResult.Error(HttpStatusCode.InternalServerError, exception.Message);
            }
        }
    }
}
