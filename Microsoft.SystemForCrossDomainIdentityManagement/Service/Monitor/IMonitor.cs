// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

using System.Net.Http;

namespace Microsoft.SCIM
{
    using System;

    public interface IMonitor
    {
        HttpRequestMessage Request { get; set; }
        void Inform(IInformationNotification notification);
        void Report(IExceptionNotification notification);
        void Warn(Notification<Exception> notification);
        void Warn(Notification<string> notification);
    }
}
