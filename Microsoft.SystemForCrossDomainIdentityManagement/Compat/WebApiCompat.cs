// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

// ---------------------------------------------------------------------------
// Compiled for net10.0 ONLY. On net48 this type comes from System.Web.Http.dll
// (the Microsoft.AspNet.WebApi.Core package), and Compat/ is excluded from that
// leg of the build - see Microsoft.SCIM.Core.csproj.
//
// It exists because Microsoft.AspNetCore.Mvc.WebApiCompatShim, which previously
// supplied System.Web.Http.HttpResponseException to this library, ships
// netstandard2.0 only, drags in Microsoft.AspNetCore.Mvc.Core 2.2.x, and is
// discontinued. Vendoring just the one exception type here means the shared SCIM
// layer - IProvider, IExtension, every throw/catch site, and every consumer's
// provider implementation - needs no source changes at all.
//
// Yes, this squats on the System.Web.Http namespace. That is deliberate and is
// confined to this single file. See MULTI-TARGET-PLAN.md D3 and D24, and
// docs/net48-hosting.md.
//
// Note that Compat/ contains ONLY HttpResponseException. There is no
// FromUriAttribute shim: its single use site is rewritten per hosting framework
// ([FromRoute] on net10, the native [FromUri] on net48).
// ---------------------------------------------------------------------------

namespace System.Web.Http
{
    using System;
    using System.Net;
    using System.Net.Http;

    /// <summary>
    /// An exception that carries the HTTP response a request should produce.
    /// Source-compatible with the System.Web.Http type of the same name.
    /// </summary>
    public class HttpResponseException : Exception
    {
        public HttpResponseException(HttpStatusCode statusCode)
            : this(new HttpResponseMessage(statusCode))
        {
        }

        public HttpResponseException(HttpResponseMessage response)
        {
            this.Response = response ?? throw new ArgumentNullException(nameof(response));
        }

        public HttpResponseMessage Response
        {
            get;
        }
    }
}
