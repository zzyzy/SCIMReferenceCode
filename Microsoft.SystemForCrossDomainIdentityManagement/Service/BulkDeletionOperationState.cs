//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM
{
    using System;
    using System.Net;

    internal class BulkDeletionOperationState : BulkOperationStateBase<IResourceIdentifier>
    {
        public BulkDeletionOperationState(
            IRequest<BulkRequest2> request,
            BulkRequestOperation operation,
            IBulkOperationContext<IResourceIdentifier> context)
            : base(request, operation, context)
        {
        }

        public override bool TryPrepareRequest(out IRequest<IResourceIdentifier> request)
        {
            request = null;

            // path is required of every bulk operation, and omitting it used to reach
            // Uri's constructor as a null relative identifier - a NullReferenceException
            // that escaped as a 500 for what is the client's own malformed request.
            Uri absoluteResourceIdentifier =
                null == this.Operation.Path
                    ? null
                    : new Uri(this.BulkRequest.BaseResourceIdentifier, this.Operation.Path);

            if (null == absoluteResourceIdentifier
                || !UniformResourceIdentifier.TryParse(
                    absoluteResourceIdentifier,
                    this.BulkRequest.Extensions,
                    out IUniformResourceIdentifier resourceIdentifier))
            {
                this.Context.State = this;

                ErrorResponse error =
                    new ErrorResponse()
                    {
                        Status = HttpStatusCode.BadRequest,
                        ErrorType = ErrorType.invalidPath
                    };
                BulkResponseOperation response =
                    new BulkResponseOperation(this.Operation.Identifier)
                    {
                        Response = error,
                        Method = this.Operation.Method,
                        Status = HttpStatusCode.BadRequest
                    };
                response.Method = this.Operation.Method;

                this.Complete(response);
                return false;
            }

            request =
                new DeletionRequest(
                    this.BulkRequest.Request,
                    resourceIdentifier.Identifier,
                    this.BulkRequest.CorrelationIdentifier,
                    this.BulkRequest.Extensions);
            return true;
        }
    }
}
