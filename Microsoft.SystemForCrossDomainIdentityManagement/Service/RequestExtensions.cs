// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Web.Http;
    using System.Collections.Generic;
    using Newtonsoft.Json;

    public static class RequestExtensions
    {
        private const string SegmentSeparator = "/";

        // Not a constant: the interface segment is configurable at startup through
        // ScimPath.SetPrefix, so this has to be read per call rather than baked in.
        private static string SegmentInterface =>
            RequestExtensions.SegmentSeparator +
            ScimPath.Prefix +
            RequestExtensions.SegmentSeparator;

        private readonly static Lazy<char[]> SegmentSeparators =
            new Lazy<char[]>(
                () =>
                    SegmentSeparator.ToArray());

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0", Justification = "False analysis of the 'this' parameter of an extension method")]
        public static Uri GetBaseResourceIdentifier(this HttpRequestMessage request)
        {
            if (null == request.RequestUri)
            {
                throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidRequest);
            }

            string lastSegment =
                request.RequestUri.AbsolutePath.Split(
                    RequestExtensions.SegmentSeparators.Value,
                    StringSplitOptions.RemoveEmptyEntries)
                .Last();
            if (string.Equals(lastSegment, ScimPath.Prefix, StringComparison.OrdinalIgnoreCase))
            {
                return request.RequestUri;
            }

            string resourceIdentifier = request.RequestUri.AbsoluteUri;

            int indexInterface =
                resourceIdentifier
                .LastIndexOf(
                    RequestExtensions.SegmentInterface,
                    StringComparison.OrdinalIgnoreCase);

            if (indexInterface < 0)
            {
                throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidRequest);
            }

            string baseResource = resourceIdentifier.Substring(0, indexInterface);
            Uri result = new Uri(baseResource, UriKind.Absolute);
            return result;
        }

        public static bool TryGetRequestIdentifier(this HttpRequestMessage request, out string requestIdentifier)
        {
            request?.Headers.TryGetValues("client-id", out IEnumerable<string> _);
            requestIdentifier = Guid.NewGuid().ToString();
            return true;
        }

        /// <summary>
        /// Records, for every update, which creations in the same request it references by
        /// <c>bulkId</c>.
        /// </summary>
        private static void Relate(
            IReadOnlyCollection<IBulkCreationOperationContext> creations,
            IReadOnlyCollection<IBulkUpdateOperationContext> updates)
        {
            foreach (IBulkUpdateOperationContext update in updates)
            {
                if (null == update.Method || null == update.Operation)
                {
                    throw new ArgumentException(
                        SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidContext);
                }

                PatchRequest2 patchRequest = RequestExtensions.ReadPatchRequest(update.Operation);

                foreach (IBulkCreationOperationContext creation in creations)
                {
                    if (null == creation.Operation
                        || string.IsNullOrWhiteSpace(creation.Operation.Identifier))
                    {
                        throw new ArgumentException(
                            SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidOperation);
                    }

                    // A creation's own subordinate cannot depend on it: it already runs
                    // after it, and recording the pair would have the creation waiting on
                    // an operation that is waiting on the creation.
                    if (creation == update.Parent)
                    {
                        continue;
                    }

                    if (patchRequest.References(creation.Operation.Identifier))
                    {
                        creation.AddDependent(update);
                        update.AddDependency(creation);
                    }
                }
            }
        }

        /// <summary>
        /// Moves each creation ahead of the first operation that depends on it, so that the
        /// identifier a reference resolves to exists by the time the reference is read.
        /// </summary>
        private static void Order(
            List<IBulkOperationContext> operations,
            IReadOnlyCollection<IBulkCreationOperationContext> creations)
        {
            foreach (IBulkCreationOperationContext creation in creations)
            {
                if (!creation.Dependents.Any())
                {
                    continue;
                }

                int firstDependent =
                    operations
                    .Select(
                        (IBulkOperationContext item, int index) => (item, index))
                    .Where(
                        (candidate) =>
                            creation
                            .Dependents
                            .Any(
                                (IBulkOperationContext dependent) =>
                                    dependent == candidate.item))
                    .Select(
                        (candidate) =>
                            candidate.index)
                    .DefaultIfEmpty(-1)
                    .Min();

                int current = operations.IndexOf(creation);

                if (firstDependent < 0 || current < 0 || current < firstDependent)
                {
                    continue;
                }

                operations.RemoveAt(current);
                operations.Insert(firstDependent, creation);
            }
        }

        /// <summary>
        /// Reads a bulk operation's <c>data</c> as a patch request.
        /// </summary>
        private static PatchRequest2 ReadPatchRequest(BulkRequestOperation operation)
        {
            if (operation.Data is PatchRequest2 patchRequest)
            {
                return patchRequest;
            }

            try
            {
                dynamic operationDataJson = JsonConvert.DeserializeObject(operation.Data.ToString());
                IReadOnlyCollection<PatchOperation2Combined> patchOperations =
                    operationDataJson.Operations.ToObject<List<PatchOperation2Combined>>();
                return new PatchRequest2(patchOperations);
            }
            catch
            {
                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }
        }


        private static void Enlist(
            this IRequest<BulkRequest2> request,
            BulkRequestOperation operation,
            List<IBulkOperationContext> operations,
            List<IBulkCreationOperationContext> creations,
            List<IBulkUpdateOperationContext> updates)
        {
            if (null == operation)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            if (null == operations)
            {
                throw new ArgumentNullException(nameof(operations));
            }

            if (null == creations)
            {
                throw new ArgumentNullException(nameof(creations));
            }

            if (null == updates)
            {
                throw new ArgumentNullException(nameof(updates));
            }

            if (null == operation.Method)
            {
                throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidOperation);
            }

            if (HttpMethod.Post == operation.Method)
            {
                IBulkCreationOperationContext context = new BulkCreationOperationContext(request, operation);

                operations.Add(context);
                creations.Add(context);

                // A creation's subordinates are the operations it had to synthesize - the
                // manager reference, the group's memberships - and they run after it.
                operations.AddRange(context.Subordinates);
                updates.AddRange(context.Subordinates);
                return;
            }

            if (HttpMethod.Delete == operation.Method)
            {
                IBulkOperationContext context = new BulkDeletionOperationContext(request, operation);
                operations.Add(context);
                return; 
            }

            if (ProtocolExtensions.PatchMethod == operation.Method)
            {
                IBulkUpdateOperationContext context = new BulkUpdateOperationContext(request, operation);
                operations.Add(context);
                updates.Add(context);
                return;
            }

            throw new HttpResponseException(HttpStatusCode.BadRequest);
        }

        public static Queue<IBulkOperationContext> EnqueueOperations(this IRequest<BulkRequest2> request)
        {
            if (null == request)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (null == request.Payload)
            {
                throw new ArgumentException(SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidRequest);
            }

            List<IBulkCreationOperationContext> creations = new List<IBulkCreationOperationContext>();
            List<IBulkUpdateOperationContext> updates = new List<IBulkUpdateOperationContext>();
            List<IBulkOperationContext> operations = new List<IBulkOperationContext>();

            foreach (BulkRequestOperation operation in request.Payload.Operations)
            {
                request.Enlist(operation, operations, creations, updates);
            }

            // Relating in one pass over the finished set, rather than as each operation is
            // enlisted, is what makes the wiring independent of the order the client sent.
            // RFC 7644 section 3.7.2 lets an operation reference a bulkId declared later,
            // and a creation's own synthesized operations do not exist until it has been
            // enlisted - so relating as we went missed a reference in either direction.
            RequestExtensions.Relate(creations, updates);
            RequestExtensions.Order(operations, creations);

            Queue<IBulkOperationContext> result = new Queue<IBulkOperationContext>(operations.Count);
            foreach (IBulkOperationContext operation in operations)
            {
                result.Enqueue(operation);
            }
            return result;
        }
    }
}
