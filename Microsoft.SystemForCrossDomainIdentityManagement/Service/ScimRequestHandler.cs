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
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// The hosting-neutral SCIM resource endpoint. Holds the orchestration, the
    /// exception-to-status mapping and the failure logging that used to live in
    /// <c>ControllerTemplate&lt;T&gt;</c>.
    /// </summary>
    /// <remarks>
    /// This exists so that the ASP.NET Web API (net48) and ASP.NET Core (net10.0)
    /// controllers are thin adapters over one implementation rather than two copies
    /// of the same 760 lines - see MULTI-TARGET-PLAN.md D12.
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
                    correlationIdentifier);

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
                    correlationIdentifier);

                throw new HttpResponseException(HttpStatusCode.NotImplemented);
            }
            catch (NotSupportedException notSupportedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.DeleteNotSupportedException,
                    notSupportedException,
                    correlationIdentifier);

                throw new HttpResponseException(HttpStatusCode.NotImplemented);
            }
            catch (Exception exception)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.DeleteException,
                    exception,
                    correlationIdentifier);

                throw;
            }
        }

        // Ported from ControllerTemplate<T>.Get().
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
                    correlationIdentifier);

                return ScimResult.Error(HttpStatusCode.BadRequest, argumentException.Message);
            }
            catch (NotImplementedException notImplementedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.QueryNotImplementedException,
                    notImplementedException,
                    correlationIdentifier);

                return ScimResult.Error(HttpStatusCode.NotImplemented, notImplementedException.Message);
            }
            catch (NotSupportedException notSupportedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.QueryNotSupportedException,
                    notSupportedException,
                    correlationIdentifier);

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
                        correlationIdentifier);
                }

                return ScimResult.FromException(responseException);
            }
            catch (Exception exception)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.QueryException,
                    exception,
                    correlationIdentifier);

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
                    QueryResponseBase queryResponse =
                        await provider
                            .Query(
                                request,
                                effectiveQuery.Filters,
                                effectiveQuery.Attributes,
                                effectiveQuery.ExcludedAttributes,
                                effectiveQuery.PaginationParameters,
                                correlationIdentifier)
                            .ConfigureAwait(false);
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
                    correlationIdentifier);

                return ScimResult.Error(HttpStatusCode.BadRequest, argumentException.Message);
            }
            catch (NotImplementedException notImplementedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.GetNotImplementedException,
                    notImplementedException,
                    correlationIdentifier);

                return ScimResult.Error(HttpStatusCode.NotImplemented, notImplementedException.Message);
            }
            catch (NotSupportedException notSupportedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.GetNotSupportedException,
                    notSupportedException,
                    correlationIdentifier);

                return ScimResult.Error(HttpStatusCode.BadRequest, notSupportedException.Message);
            }
            catch (HttpResponseException responseException)
            {
                if (responseException.Response?.StatusCode != HttpStatusCode.NotFound)
                {
                    this.Logger.LogScimFailure(
                        ScimEventIds.GetException,
                        responseException.InnerException ?? responseException,
                        correlationIdentifier);
                }

                if (responseException.Response?.StatusCode == HttpStatusCode.NotFound)
                {
                    return ScimResult.Error(HttpStatusCode.NotFound, string.Format(SystemForCrossDomainIdentityManagementServiceResources.ResourceNotFoundTemplate, identifier));
                }

                return ScimResult.Error(HttpStatusCode.InternalServerError, responseException.Message);
            }
            catch (Exception exception)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.GetException,
                    exception,
                    correlationIdentifier);

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

                // If EnterpriseUser, return HTTP code 200 and user object, otherwise HTTP code 204
                if (provider.SchemaIdentifier == SchemaIdentifiers.Core2EnterpriseUser)
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
                    correlationIdentifier);

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
                    correlationIdentifier);

                throw new HttpResponseException(HttpStatusCode.NotImplemented);
            }
            catch (NotSupportedException notSupportedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.PatchNotSupportedException,
                    notSupportedException,
                    correlationIdentifier);

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
                        correlationIdentifier);
                }

                throw;
            }
            catch (Exception exception)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.PatchException,
                    exception,
                    correlationIdentifier);

                throw;
            }
        }

        // Ported from ControllerTemplate<T>.Post.
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

                if (!request.TryGetRequestIdentifier(out correlationIdentifier))
                {
                    throw new HttpResponseException(HttpStatusCode.InternalServerError);
                }

                IProviderAdapter<T> provider = this.AdaptProvider();
                Resource result = await provider.Create(request, resource, correlationIdentifier).ConfigureAwait(false);

                // RFC 7644 section 3.3: 201 with a single Location header naming the new
                // resource. The pre-port code wrote Location twice - once by hand and once
                // via CreatedAtAction, which derived a second URI from MVC routing that has
                // no ASP.NET Web API equivalent. See MULTI-TARGET-PLAN.md D15.
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
                    correlationIdentifier);

                return ScimResult.Status(HttpStatusCode.BadRequest);
            }
            catch (NotImplementedException notImplementedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.PostNotImplementedException,
                    notImplementedException,
                    correlationIdentifier);

                throw new HttpResponseException(HttpStatusCode.NotImplemented);
            }
            catch (NotSupportedException notSupportedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.PostNotSupportedException,
                    notSupportedException,
                    correlationIdentifier);

                throw new HttpResponseException(HttpStatusCode.NotImplemented);
            }
            catch (HttpResponseException httpResponseException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.PostNotSupportedException,
                    httpResponseException,
                    correlationIdentifier);

                if (httpResponseException.Response.StatusCode == HttpStatusCode.Conflict)
                    return ScimResult.Status(HttpStatusCode.Conflict);
                else
                    return ScimResult.Status(HttpStatusCode.BadRequest);
            }
            catch (Exception exception)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.PostException,
                    exception,
                    correlationIdentifier);

                throw;
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

                IProviderAdapter<T> provider = this.AdaptProvider();
                Resource result = await provider.Replace(request, resource, correlationIdentifier).ConfigureAwait(false);

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
                    correlationIdentifier);

                return ScimResult.Error(HttpStatusCode.BadRequest, argumentException.Message);
            }
            catch (NotImplementedException notImplementedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.PutNotImplementedException,
                    notImplementedException,
                    correlationIdentifier);

                return ScimResult.Error(HttpStatusCode.NotImplemented, notImplementedException.Message);
            }
            catch (NotSupportedException notSupportedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.PutNotSupportedException,
                    notSupportedException,
                    correlationIdentifier);

                return ScimResult.Error(HttpStatusCode.BadRequest, notSupportedException.Message);
            }
            catch (HttpResponseException httpResponseException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.PostNotSupportedException,
                    httpResponseException,
                    correlationIdentifier);

                if (httpResponseException.Response.StatusCode == HttpStatusCode.NotFound)
                    return ScimResult.Error(HttpStatusCode.NotFound, string.Format(SystemForCrossDomainIdentityManagementServiceResources.ResourceNotFoundTemplate, identifier));
                else if (httpResponseException.Response.StatusCode == HttpStatusCode.Conflict)
                    return ScimResult.Error(HttpStatusCode.Conflict, SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidRequest);
                else
                    return ScimResult.Error(HttpStatusCode.BadRequest, httpResponseException.Message);
            }
            catch (Exception exception)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.PutException,
                    exception,
                    correlationIdentifier);

                return ScimResult.Error(HttpStatusCode.InternalServerError, exception.Message);
            }
        }
    }
}
