// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using System.Web.Http;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// The hosting-neutral discovery and bulk endpoints, ported from
    /// <c>SchemasController</c>, <c>ResourceTypesController</c>,
    /// <c>ServiceProviderConfigurationController</c> and <c>BulkRequestController</c>.
    /// </summary>
    /// <remarks>
    /// Every failure path here <c>throws</c> <see cref="HttpResponseException"/> rather
    /// than returning a <see cref="ScimResult"/> - exactly as the original controllers did,
    /// which returned bare domain objects on success. Each host's exception filter is what
    /// turns those throws into 400/500/501 responses instead of 500s across the board.
    /// See docs/scim-conformance.md section 4, requirement X1.
    /// </remarks>
    public class ScimDiscoveryRequestHandler
    {
        public ScimDiscoveryRequestHandler(IProvider provider, ILogger logger)
        {
            this.Provider = provider;
            this.Logger = logger;
        }

        protected ILogger Logger
        {
            get;
        }

        protected IProvider Provider
        {
            get;
        }

        // Ported from SchemasController.Get.
        public virtual ScimResult QuerySchemas(HttpRequestMessage request)
        {
            string correlationIdentifier = null;

            try
            {
                if (!request.TryGetRequestIdentifier(out correlationIdentifier))
                {
                    throw new HttpResponseException(HttpStatusCode.InternalServerError);
                }

                IProvider provider = this.Provider;
                if (null == provider)
                {
                    throw new HttpResponseException(HttpStatusCode.InternalServerError);
                }

                IReadOnlyCollection<Resource> resources = provider.Schema;
                QueryResponseBase result = new QueryResponse(resources);

                result.TotalResults =
                    result.ItemsPerPage =
                        resources.Count;
                result.StartIndex = 1;
                return ScimResult.Ok(result);
            }
            catch (ArgumentException argumentException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.SchemasGetArgumentException,
                    argumentException,
                    correlationIdentifier);

                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }
            catch (NotImplementedException notImplementedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.SchemasGetNotImplementedException,
                    notImplementedException,
                    correlationIdentifier);

                throw new HttpResponseException(HttpStatusCode.NotImplemented);
            }
            catch (NotSupportedException notSupportedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.SchemasGetNotSupportedException,
                    notSupportedException,
                    correlationIdentifier);

                throw new HttpResponseException(HttpStatusCode.NotImplemented);
            }
            catch (HttpResponseException)
            {
                // The thrown status is the answer; this method throws one itself. Without
                // this the generic catch below would rewrite it.
                throw;
            }
            catch (Exception exception)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.SchemasGetException,
                    exception,
                    correlationIdentifier);

                // A SCIM error body, as the query and replace handlers already answer for
                // the same exception. Rethrowing let a provider's exception out of the SCIM
                // layer, where the host decided what to do with it - a plain-text stack trace
                // under the developer exception page, an empty 500 without it, and something
                // different again on the other hosting leg.
                return ScimResult.Error(HttpStatusCode.InternalServerError, exception.Message);
            }
        }

        // Ported from ResourceTypesController.Get.
        public virtual ScimResult QueryResourceTypes(HttpRequestMessage request)
        {
            string correlationIdentifier = null;

            try
            {
                if (!request.TryGetRequestIdentifier(out correlationIdentifier))
                {
                    throw new HttpResponseException(HttpStatusCode.InternalServerError);
                }

                IProvider provider = this.Provider;
                if (null == provider)
                {
                    throw new HttpResponseException(HttpStatusCode.InternalServerError);
                }

                IReadOnlyCollection<Resource> resources = provider.ResourceTypes;
                QueryResponseBase result = new QueryResponse(resources);

                result.TotalResults =
                    result.ItemsPerPage =
                        resources.Count;
                result.StartIndex = 1;
                return ScimResult.Ok(result);
            }
            catch (ArgumentException argumentException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.ResourceTypesGetArgumentException,
                    argumentException,
                    correlationIdentifier);

                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }
            catch (NotImplementedException notImplementedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.ResourceTypesGetNotImplementedException,
                    notImplementedException,
                    correlationIdentifier);

                throw new HttpResponseException(HttpStatusCode.NotImplemented);
            }
            catch (NotSupportedException notSupportedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.ResourceTypesGetNotSupportedException,
                    notSupportedException,
                    correlationIdentifier);

                throw new HttpResponseException(HttpStatusCode.NotImplemented);
            }
            catch (HttpResponseException)
            {
                // The thrown status is the answer; this method throws one itself. Without
                // this the generic catch below would rewrite it.
                throw;
            }
            catch (Exception exception)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.ResourceTypesGetException,
                    exception,
                    correlationIdentifier);

                // A SCIM error body, as the query and replace handlers already answer for
                // the same exception. Rethrowing let a provider's exception out of the SCIM
                // layer, where the host decided what to do with it - a plain-text stack trace
                // under the developer exception page, an empty 500 without it, and something
                // different again on the other hosting leg.
                return ScimResult.Error(HttpStatusCode.InternalServerError, exception.Message);
            }
        }

        // Ported from ServiceProviderConfigurationController.Get.
        public virtual ScimResult RetrieveServiceProviderConfiguration(HttpRequestMessage request)
        {
            string correlationIdentifier = null;

            try
            {
                if (!request.TryGetRequestIdentifier(out correlationIdentifier))
                {
                    throw new HttpResponseException(HttpStatusCode.InternalServerError);
                }

                IProvider provider = this.Provider;
                if (null == provider)
                {
                    throw new HttpResponseException(HttpStatusCode.InternalServerError);
                }

                ServiceConfigurationBase result = provider.Configuration;
                return ScimResult.Ok(result);
            }
            catch (ArgumentException argumentException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.ServiceProviderConfigurationGetArgumentException,
                    argumentException,
                    correlationIdentifier);

                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }
            catch (NotImplementedException notImplementedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.ServiceProviderConfigurationGetNotImplementedException,
                    notImplementedException,
                    correlationIdentifier);

                throw new HttpResponseException(HttpStatusCode.NotImplemented);
            }
            catch (NotSupportedException notSupportedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.ServiceProviderConfigurationGetNotSupportedException,
                    notSupportedException,
                    correlationIdentifier);

                throw new HttpResponseException(HttpStatusCode.NotImplemented);
            }
            catch (HttpResponseException)
            {
                // The thrown status is the answer; this method throws one itself. Without
                // this the generic catch below would rewrite it.
                throw;
            }
            catch (Exception exception)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.ServiceProviderConfigurationGetException,
                    exception,
                    correlationIdentifier);

                // A SCIM error body, as the query and replace handlers already answer for
                // the same exception. Rethrowing let a provider's exception out of the SCIM
                // layer, where the host decided what to do with it - a plain-text stack trace
                // under the developer exception page, an empty 500 without it, and something
                // different again on the other hosting leg.
                return ScimResult.Error(HttpStatusCode.InternalServerError, exception.Message);
            }
        }

        // Ported from BulkRequestController.Post.
        public virtual async Task<ScimResult> ProcessBulkRequestAsync(HttpRequestMessage request, BulkRequest2 bulkRequest)
        {
            string correlationIdentifier = null;

            try
            {
                if (null == bulkRequest)
                {
                    throw new HttpResponseException(HttpStatusCode.BadRequest);
                }

                if (!request.TryGetRequestIdentifier(out correlationIdentifier))
                {
                    throw new HttpResponseException(HttpStatusCode.InternalServerError);
                }

                IProvider provider = this.Provider;
                if (null == provider)
                {
                    throw new HttpResponseException(HttpStatusCode.InternalServerError);
                }

                IReadOnlyCollection<IExtension> extensions = provider.ReadExtensions();
                IRequest<BulkRequest2> request2 = new BulkRequest(request, bulkRequest, correlationIdentifier, extensions);
                BulkResponse2 result = await provider.ProcessAsync(request2).ConfigureAwait(false);
                return ScimResult.Ok(result);
            }
            catch (ArgumentException argumentException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.BulkRequestPostArgumentException,
                    argumentException,
                    correlationIdentifier);

                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }
            catch (NotImplementedException notImplementedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.BulkRequestPostNotImplementedException,
                    notImplementedException,
                    correlationIdentifier);

                throw new HttpResponseException(HttpStatusCode.NotImplemented);
            }
            catch (NotSupportedException notSupportedException)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.BulkRequestPostNotSupportedException,
                    notSupportedException,
                    correlationIdentifier);

                throw new HttpResponseException(HttpStatusCode.NotImplemented);
            }
            catch (HttpResponseException)
            {
                // The thrown status is the answer, and this method throws one itself for a
                // body it could not read. Without this the generic catch below turned that
                // deliberate 400 into a 500.
                throw;
            }
            catch (Exception exception)
            {
                this.Logger.LogScimFailure(
                    ScimEventIds.BulkRequestPostException,
                    exception,
                    correlationIdentifier);

                // A SCIM error body, as the query and replace handlers already answer for
                // the same exception. Rethrowing let a provider's exception out of the SCIM
                // layer, where the host decided what to do with it - a plain-text stack trace
                // under the developer exception page, an empty 500 without it, and something
                // different again on the other hosting leg.
                return ScimResult.Error(HttpStatusCode.InternalServerError, exception.Message);
            }
        }
    }
}
