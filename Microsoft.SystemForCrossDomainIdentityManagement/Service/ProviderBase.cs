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

    public abstract class ProviderBase : IProvider
    {
        private static readonly Lazy<BulkRequestsFeature> BulkFeatureSupport =
            new Lazy<BulkRequestsFeature>(
                () =>
                    BulkRequestsFeature.CreateUnsupportedFeature());

        private static readonly Lazy<IReadOnlyCollection<TypeScheme>> TypeSchema =
            new Lazy<IReadOnlyCollection<TypeScheme>>(
                () =>
                    Array.Empty<TypeScheme>());

        private static readonly Lazy<ServiceConfigurationBase> ServiceConfiguration =
            new Lazy<ServiceConfigurationBase>(
                () =>
                    new Core2ServiceConfiguration(ProviderBase.BulkFeatureSupport.Value, false, true, false, true, false));

        private static readonly Lazy<IReadOnlyCollection<Core2ResourceType>> Types =
            new Lazy<IReadOnlyCollection<Core2ResourceType>>(
                () =>
                    Array.Empty<Core2ResourceType>());

        public virtual bool AcceptLargeObjects
        {
            get;
            set;
        }

        public virtual ServiceConfigurationBase Configuration
        {
            get
            {
                return ProviderBase.ServiceConfiguration.Value;
            }
        }

        //public virtual IEventTokenHandler EventHandler
        //{
        //    get;
        //    set;
        //}

        public virtual IReadOnlyCollection<IExtension> Extensions
        {
            get
            {
                return null;
            }
        }

        public virtual IResourceJsonDeserializingFactory<GroupBase> GroupDeserializationBehavior
        {
            get
            {
                return null;
            }
        }

        public virtual ISchematizedJsonDeserializingFactory<PatchRequest2> PatchRequestDeserializationBehavior
        {
            get
            {
                return null;
            }
        }

        public virtual IReadOnlyCollection<Core2ResourceType> ResourceTypes
        {
            get
            {
                return ProviderBase.Types.Value;
            }
        }

        public virtual IReadOnlyCollection<TypeScheme> Schema
        {
            get
            {
                return ProviderBase.TypeSchema.Value;
            }
        }

        //public virtual Action<IAppBuilder, HttpConfiguration> StartupBehavior
        //{
        //    get
        //    {
        //        return null;
        //    }
        //}

        public virtual IResourceJsonDeserializingFactory<Core2UserBase> UserDeserializationBehavior
        {
            get
            {
                return null;
            }
        }

        public abstract Task<Resource> CreateAsync(Resource resource, string correlationIdentifier);

        public virtual async Task<Resource> CreateAsync(IRequest<Resource> request)
        {
            if (null == request)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (null == request.Payload)
            {
                throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidRequest);
            }

            if (string.IsNullOrWhiteSpace(request.CorrelationIdentifier))
            {
                throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidRequest);
            }

            Resource result = await this.CreateAsync(request.Payload, request.CorrelationIdentifier).ConfigureAwait(false);
            return result;
        }

        public abstract Task DeleteAsync(IResourceIdentifier resourceIdentifier, string correlationIdentifier);

        public virtual async Task DeleteAsync(IRequest<IResourceIdentifier> request)
        {
            if (null == request)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (null == request.Payload)
            {
                throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidRequest);
            }

            if (string.IsNullOrWhiteSpace(request.CorrelationIdentifier))
            {
                throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidRequest);
            }

            await this.DeleteAsync(request.Payload, request.CorrelationIdentifier).ConfigureAwait(false);
        }

        /// <summary>
        /// Applies the <c>startIndex</c> and <c>count</c> window of RFC 7644 section 3.4.2.4 to
        /// what <see cref="QueryAsync(IRequest{IQueryParameters})"/> returned.
        /// </summary>
        /// <remarks>
        /// The contract this implies: <c>QueryAsync</c> returns every resource matching the
        /// filter, and the window is applied here. That is what the in-memory sample provider
        /// does, and it is the only arrangement in which this base class can report
        /// <c>totalResults</c> honestly - the RFC wants the full match count, not the size of
        /// the page.
        ///
        /// A provider that pages in its store - which any provider over a real database should,
        /// rather than materializing every match - overrides this method instead, and is then
        /// responsible for <c>totalResults</c>, <c>startIndex</c> and <c>itemsPerPage</c>
        /// itself.
        ///
        /// The previous implementation reported <c>startIndex: 1</c> and
        /// <c>itemsPerPage: totalResults</c> unconditionally, so a paged request was answered
        /// with metadata describing a single unpaged page.
        /// </remarks>
        public virtual async Task<QueryResponseBase> PaginateQueryAsync(IRequest<IQueryParameters> request)
        {
            if (null == request)
            {
                throw new ArgumentNullException(nameof(request));
            }

            IReadOnlyCollection<Resource> resources = await this.QueryAsync(request).ConfigureAwait(false);

            int totalResults = resources.Count;

            IPaginationParameters pagination = request.Payload?.PaginationParameters;

            // RFC 7644 section 3.4.2.4: startIndex is 1-based and anything below 1 is treated
            // as 1; a negative count is treated as 0. An absent count means the server decides,
            // and this base class returns the remainder rather than imposing a limit.
            int startIndex = pagination?.StartIndex ?? 1;
            if (startIndex < 1)
            {
                startIndex = 1;
            }

            int? count = pagination?.Count;
            if (count.HasValue && count.Value < 0)
            {
                count = 0;
            }

            IEnumerable<Resource> page = resources.Skip(startIndex - 1);
            if (count.HasValue)
            {
                page = page.Take(count.Value);
            }

            IReadOnlyCollection<Resource> paged = page.ToArray();

            QueryResponseBase result = new QueryResponse(paged);
            result.TotalResults = totalResults;
            result.ItemsPerPage = paged.Count;
            result.StartIndex = startIndex;
            return result;
        }


        public  virtual async Task<BulkResponse2> ProcessAsync(IRequest<BulkRequest2> request)
        {
            if (null == request)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (null == request.Request)
            {
                throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidRequest);
            }
            Queue<IBulkOperationContext> operations = request.EnqueueOperations();
            BulkResponse2 result = await this.ProcessAsync(operations).ConfigureAwait(false);
            return result;
        }

        public virtual async Task ProcessAsync(IBulkOperationContext operation)
        {
            if (null == operation)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            if (!operation.TryPrepare())
            {
                return;
            }

            if (null == operation.Method)
            {
                throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidOperation);
            }

            if (null == operation.Operation)
            {
                throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidOperation);
            }

            BulkResponseOperation response =
                new BulkResponseOperation(operation.Operation.Identifier)
                {
                    Method = operation.Method
                };

            if (HttpMethod.Delete == operation.Method)
            {
                IBulkOperationContext<IResourceIdentifier> context = (IBulkOperationContext<IResourceIdentifier>)operation;
                await this.DeleteAsync(context.Request).ConfigureAwait(false);
                response.Status = HttpStatusCode.NoContent;
            }
            else if (HttpMethod.Get == operation.Method)
            {
                switch (operation)
                {
                    case IBulkOperationContext<IResourceRetrievalParameters> retrievalContext:
                        response.Response = await this.RetrieveAsync(retrievalContext.Request).ConfigureAwait(false);
                        break;
                    default:
                        IBulkOperationContext<IQueryParameters> queryContext = (IBulkOperationContext<IQueryParameters>)operation;
                        response.Response = await this.QueryAsync(queryContext.Request).ConfigureAwait(false);
                        break;
                }
                response.Status = HttpStatusCode.OK;
            }
            else if (ProtocolExtensions.PatchMethod == operation.Method)
            {
                IBulkOperationContext<IPatch> context = (IBulkOperationContext<IPatch>)operation;
                await this.UpdateAsync(context.Request).ConfigureAwait(false);
                response.Status = HttpStatusCode.OK;
            }
            else if (HttpMethod.Post == operation.Method)
            {
                IBulkOperationContext<Resource> context = (IBulkOperationContext<Resource>)operation;
                Resource output = await this.CreateAsync(context.Request).ConfigureAwait(false);
                response.Status = HttpStatusCode.Created;
                response.Location = output.GetResourceIdentifier(context.BulkRequest.BaseResourceIdentifier);
            }
            else
            {
                string exceptionMessage =
                    string.Format(
                        CultureInfo.InvariantCulture,
                        SystemForCrossDomainIdentityManagementServiceResources.ExceptionMethodNotSupportedTemplate,
                        operation.Method);
                ErrorResponse error =
                    new ErrorResponse()
                    {
                        Status = HttpStatusCode.BadRequest,
                        Detail = exceptionMessage
                    };
                response.Response = error;
                response.Status = HttpStatusCode.BadRequest;
            }

            operation.Complete(response);
        }

        public virtual async Task<BulkResponse2> ProcessAsync(Queue<IBulkOperationContext> operations)
        {
            if (null == operations)
            {
                throw new ArgumentNullException(nameof(operations));
            }

            BulkResponse2 result = new BulkResponse2();
            int countFailures = 0;
            while (operations.Any())
            {
                IBulkOperationContext operation = operations.Dequeue();
                await this.ProcessAsync(operation).ConfigureAwait(false);

                bool addOperation;
                switch (operation)
                {
                    case IBulkUpdateOperationContext updateOperation:
                        addOperation = null == updateOperation.Parent;
                        break;
                    default:
                        addOperation = true;
                        break;
                }
                if (addOperation)
                {
                    result.AddOperation(operation.Response);
                }

                if (operation.Response.IsError())
                {
                    checked
                    {
                        countFailures++;
                    }
                }

                if
                (
                        operation.BulkRequest.Payload.FailOnErrors.HasValue
                    && countFailures > operation.BulkRequest.Payload.FailOnErrors.Value
                )
                {
                    break;
                }
            }
            return result;
        }

        public virtual Task<Resource[]> QueryAsync(IQueryParameters parameters, string correlationIdentifier)
        {
            throw new NotImplementedException();
        }

        public virtual async Task<Resource[]> QueryAsync(IRequest<IQueryParameters> request)
        {
            if (null == request)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (null == request.Payload)
            {
                throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidRequest);
            }

            if (string.IsNullOrWhiteSpace(request.CorrelationIdentifier))
            {
                throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidRequest);
            }

            Resource[] result = await this.QueryAsync(request.Payload, request.CorrelationIdentifier).ConfigureAwait(false);
            return result;
        }

        public virtual Task<Resource> ReplaceAsync(Resource resource, string correlationIdentifier)
        {
            throw new NotSupportedException();
        }

        public virtual async Task<Resource> ReplaceAsync(IRequest<Resource> request)
        {
            if (null == request)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (null == request.Payload)
            {
                throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidRequest);
            }

            if (string.IsNullOrWhiteSpace(request.CorrelationIdentifier))
            {
                throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidRequest);
            }

            Resource result = await this.ReplaceAsync(request.Payload, request.CorrelationIdentifier).ConfigureAwait(false);
            return result;
        }

        public abstract Task<Resource> RetrieveAsync(IResourceRetrievalParameters parameters, string correlationIdentifier);

        public virtual async Task<Resource> RetrieveAsync(IRequest<IResourceRetrievalParameters> request)
        {
            if (null == request)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (null == request.Payload)
            {
                throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidRequest);
            }

            if (string.IsNullOrWhiteSpace(request.CorrelationIdentifier))
            {
                throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidRequest);
            }

            Resource result = await this.RetrieveAsync(request.Payload, request.CorrelationIdentifier).ConfigureAwait(false);
            return result;
        }

        public abstract Task UpdateAsync(IPatch patch, string correlationIdentifier);

        public virtual async Task UpdateAsync(IRequest<IPatch> request)
        {
            if (null == request)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (null == request.Payload)
            {
                throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidRequest);
            }

            if (string.IsNullOrWhiteSpace(request.CorrelationIdentifier))
            {
                throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidRequest);
            }

            await this.UpdateAsync(request.Payload, request.CorrelationIdentifier).ConfigureAwait(false);
        }
    }
}
