// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// The <see cref="EventId"/> values the SCIM layer logs with.
    /// </summary>
    /// <remarks>
    /// These replace the <c>ServiceNotificationIdentifiers</c> constants that the deleted
    /// <c>IMonitor</c> surface used. The numeric values are carried over unchanged so that
    /// anything already filtering or alerting on them keeps working; 36 and 37 stay retired.
    /// </remarks>
    public static class ScimEventIds
    {
        public static readonly EventId BulkRequestPostArgumentException = new EventId(1, nameof(BulkRequestPostArgumentException));
        public static readonly EventId BulkRequestPostException = new EventId(2, nameof(BulkRequestPostException));
        public static readonly EventId BulkRequestPostNotImplementedException = new EventId(3, nameof(BulkRequestPostNotImplementedException));
        public static readonly EventId BulkRequestPostNotSupportedException = new EventId(4, nameof(BulkRequestPostNotSupportedException));

        public static readonly EventId DeleteArgumentException = new EventId(5, nameof(DeleteArgumentException));
        public static readonly EventId DeleteException = new EventId(6, nameof(DeleteException));
        public static readonly EventId DeleteNotImplementedException = new EventId(7, nameof(DeleteNotImplementedException));
        public static readonly EventId DeleteNotSupportedException = new EventId(8, nameof(DeleteNotSupportedException));

        public static readonly EventId GetArgumentException = new EventId(9, nameof(GetArgumentException));
        public static readonly EventId GetException = new EventId(10, nameof(GetException));
        public static readonly EventId GetNotImplementedException = new EventId(11, nameof(GetNotImplementedException));
        public static readonly EventId GetNotSupportedException = new EventId(12, nameof(GetNotSupportedException));

        public static readonly EventId PatchArgumentException = new EventId(13, nameof(PatchArgumentException));
        public static readonly EventId PatchException = new EventId(14, nameof(PatchException));
        public static readonly EventId PatchNotImplementedException = new EventId(15, nameof(PatchNotImplementedException));
        public static readonly EventId PatchNotSupportedException = new EventId(16, nameof(PatchNotSupportedException));

        public static readonly EventId PostArgumentException = new EventId(17, nameof(PostArgumentException));
        public static readonly EventId PostException = new EventId(18, nameof(PostException));
        public static readonly EventId PostNotImplementedException = new EventId(19, nameof(PostNotImplementedException));
        public static readonly EventId PostNotSupportedException = new EventId(20, nameof(PostNotSupportedException));

        public static readonly EventId PutArgumentException = new EventId(21, nameof(PutArgumentException));
        public static readonly EventId PutException = new EventId(22, nameof(PutException));
        public static readonly EventId PutNotImplementedException = new EventId(23, nameof(PutNotImplementedException));
        public static readonly EventId PutNotSupportedException = new EventId(24, nameof(PutNotSupportedException));

        public static readonly EventId QueryArgumentException = new EventId(25, nameof(QueryArgumentException));
        public static readonly EventId QueryNotImplementedException = new EventId(26, nameof(QueryNotImplementedException));
        public static readonly EventId QueryNotSupportedException = new EventId(27, nameof(QueryNotSupportedException));
        public static readonly EventId QueryException = new EventId(28, nameof(QueryException));

        public static readonly EventId RequestPipelineException = new EventId(29, nameof(RequestPipelineException));
        public static readonly EventId RequestReceived = new EventId(30, nameof(RequestReceived));
        public static readonly EventId RequestProcessed = new EventId(31, nameof(RequestProcessed));

        public static readonly EventId ResourceTypesGetArgumentException = new EventId(32, nameof(ResourceTypesGetArgumentException));
        public static readonly EventId ResourceTypesGetException = new EventId(33, nameof(ResourceTypesGetException));
        public static readonly EventId ResourceTypesGetNotImplementedException = new EventId(34, nameof(ResourceTypesGetNotImplementedException));
        public static readonly EventId ResourceTypesGetNotSupportedException = new EventId(35, nameof(ResourceTypesGetNotSupportedException));

        public static readonly EventId SchemasGetArgumentException = new EventId(38, nameof(SchemasGetArgumentException));
        public static readonly EventId SchemasGetException = new EventId(39, nameof(SchemasGetException));
        public static readonly EventId SchemasGetNotImplementedException = new EventId(40, nameof(SchemasGetNotImplementedException));
        public static readonly EventId SchemasGetNotSupportedException = new EventId(41, nameof(SchemasGetNotSupportedException));

        public static readonly EventId ServiceProviderConfigurationGetArgumentException = new EventId(42, nameof(ServiceProviderConfigurationGetArgumentException));
        public static readonly EventId ServiceProviderConfigurationGetException = new EventId(43, nameof(ServiceProviderConfigurationGetException));
        public static readonly EventId ServiceProviderConfigurationGetNotImplementedException = new EventId(44, nameof(ServiceProviderConfigurationGetNotImplementedException));
        public static readonly EventId ServiceProviderConfigurationGetNotSupportedException = new EventId(45, nameof(ServiceProviderConfigurationGetNotSupportedException));
    }
}
