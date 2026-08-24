// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Globalization;

    /// <summary>
    /// The URL path segment the SCIM endpoints are served under. Defaults to <c>scim</c>.
    /// </summary>
    /// <remarks>
    /// Process-wide rather than injected because the segment is needed in three places that
    /// have no access to a container: the route templates, which are compile-time attribute
    /// arguments; the <c>Location</c> and resource URI construction in
    /// <see cref="ProtocolExtensions"/>; and the request URI parsing in
    /// <see cref="RequestExtensions"/>. Those must agree - a host that routed on one segment
    /// while emitting <c>Location</c> headers built from another would hand out URIs that
    /// 404 - so one value serves them all.
    ///
    /// Call <see cref="SetPrefix"/> once during startup, before the first request is served.
    /// Changing it after anything has read it throws rather than silently splitting routing
    /// from URI generation.
    /// </remarks>
    public static class ScimPath
    {
        /// <summary>The segment used when a host does not configure one.</summary>
        public const string DefaultPrefix = "scim";

        private static readonly object SyncRoot = new object();

        private static string prefix = ScimPath.DefaultPrefix;
        private static bool observed;

        /// <summary>The configured segment, with no leading or trailing separator.</summary>
        public static string Prefix
        {
            get
            {
                lock (ScimPath.SyncRoot)
                {
                    ScimPath.observed = true;
                    return ScimPath.prefix;
                }
            }
        }

        /// <summary>
        /// Sets the segment. Idempotent: setting the value it already has is always allowed,
        /// so repeated host wiring with the same configuration does not throw.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// <paramref name="value"/> is empty, or contains a path separator or whitespace.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// A different segment has already been read by the routing or URI layers.
        /// </exception>
        public static void SetPrefix(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidScimPathPrefix,
                    nameof(value));
            }

            string normalized = value.Trim().Trim('/');

            if (string.IsNullOrWhiteSpace(normalized)
                || normalized.IndexOf('/') >= 0
                || normalized.IndexOf(' ') >= 0)
            {
                throw new ArgumentException(
                    SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidScimPathPrefix,
                    nameof(value));
            }

            lock (ScimPath.SyncRoot)
            {
                if (string.Equals(ScimPath.prefix, normalized, StringComparison.Ordinal))
                {
                    return;
                }

                if (ScimPath.observed)
                {
                    throw new InvalidOperationException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            SystemForCrossDomainIdentityManagementServiceResources.ExceptionScimPathPrefixAlreadyInUseTemplate,
                            ScimPath.prefix,
                            normalized));
                }

                ScimPath.prefix = normalized;
            }
        }

        /// <summary>
        /// Rewrites a route template built against <see cref="DefaultPrefix"/> so that it
        /// starts with the configured segment instead. Templates that do not start with the
        /// default segment are returned unchanged.
        /// </summary>
        public static string ApplyPrefix(string template)
        {
            if (string.IsNullOrEmpty(template))
            {
                return template;
            }

            string configured = ScimPath.Prefix;
            if (string.Equals(configured, ScimPath.DefaultPrefix, StringComparison.Ordinal))
            {
                return template;
            }

            if (string.Equals(template, ScimPath.DefaultPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return configured;
            }

            if (template.StartsWith(ScimPath.DefaultPrefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                return configured + template.Substring(ScimPath.DefaultPrefix.Length);
            }

            return template;
        }
    }
}
