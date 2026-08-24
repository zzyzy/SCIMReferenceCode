// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Anacle.ApiFramework.Authentication.ApiKey
{
    /// <summary>
    /// Shared defaults, so that the two hosting legs agree on the wire contract.
    /// </summary>
    public static class ApiKeyAuthenticationDefaults
    {
        /// <summary>The scheme name registered by default.</summary>
        public const string AuthenticationScheme = "ApiKey";

        /// <summary>The header the key is read from by default.</summary>
        /// <remarks>
        /// A request header, never a query string parameter. Query strings are written to
        /// access logs, browser history and proxy logs as a matter of course, and a key that
        /// travels in one should be treated as disclosed. This library offers no option to
        /// read a key from the query string.
        /// </remarks>
        public const string HeaderName = "X-Api-Key";
    }
}
