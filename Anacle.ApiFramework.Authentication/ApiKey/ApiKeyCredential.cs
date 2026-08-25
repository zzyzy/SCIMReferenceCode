// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Anacle.ApiFramework.Authentication.ApiKey
{
    using System;

    /// <summary>
    /// Reads the key out of the header value that carried it.
    /// </summary>
    /// <remarks>
    /// Shared by both hosting legs, and deliberately not conditioned on the target framework:
    /// the two legs have to agree on what counts as a presented key, and the only way to be
    /// sure of that is for them to run the same code.
    /// </remarks>
    public static class ApiKeyCredential
    {
        private const char SeparatorScheme = ' ';

        /// <summary>
        /// Extracts the key from a header value, honouring an authentication scheme if one is
        /// expected.
        /// </summary>
        /// <param name="headerValue">The header's value, as presented.</param>
        /// <param name="scheme">
        /// The RFC 7235 auth-scheme the value is expected to carry - <c>Bearer</c>, for
        /// instance. Null or empty means the whole value is the key, which is the shape a
        /// header of the API key's own takes.
        /// </param>
        /// <param name="key">The key, or null when the value carries none.</param>
        /// <returns>Whether a key was found.</returns>
        /// <remarks>
        /// A value that does not carry the expected scheme yields no key rather than a wrong
        /// one, so that a bearer token arriving where an API key scheme is expected leaves the
        /// request anonymous and any other authentication still gets its turn. Matching is
        /// case-insensitive because RFC 7235 section 2.1 makes the scheme token so.
        /// </remarks>
        public static bool TryRead(string headerValue, string scheme, out string key)
        {
            key = null;

            if (string.IsNullOrWhiteSpace(headerValue))
            {
                return false;
            }

            string presented = headerValue.Trim();

            if (string.IsNullOrWhiteSpace(scheme))
            {
                key = presented;
                return true;
            }

            string expected = scheme.Trim();

            // The separating space is part of the prefix: without it "Bearerfoo" would be read
            // as the key "foo" presented under the Bearer scheme.
            if (presented.Length <= expected.Length
                || presented[expected.Length] != ApiKeyCredential.SeparatorScheme
                || !presented.StartsWith(expected, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string remainder = presented.Substring(expected.Length + 1).Trim();

            if (string.IsNullOrEmpty(remainder))
            {
                return false;
            }

            key = remainder;
            return true;
        }

        /// <summary>
        /// The auth-scheme to name in a <c>WWW-Authenticate</c> challenge.
        /// </summary>
        /// <remarks>
        /// RFC 7235 section 4.1 requires a challenge to name an auth-scheme token. When a key
        /// travels in a header of its own there is no scheme on the wire to echo, so the
        /// registered scheme name stands in - a header name is not an auth-scheme and must
        /// never be sent as one.
        /// </remarks>
        public static string ChallengeScheme(string scheme)
        {
            return
                string.IsNullOrWhiteSpace(scheme)
                    ? ApiKeyAuthenticationDefaults.AuthenticationScheme
                    : scheme.Trim();
        }
    }
}
