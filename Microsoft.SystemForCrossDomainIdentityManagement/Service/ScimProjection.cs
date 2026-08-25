// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    /// <summary>
    /// Applies the <c>attributes</c> and <c>excludedAttributes</c> query parameters of
    /// RFC 7644 section 3.9 to a response payload.
    /// </summary>
    /// <remarks>
    /// The projection is done on the serialized JSON rather than on the resource object.
    /// Nulling properties would be the obvious alternative, but it cannot express a
    /// non-nullable member - <c>active</c> is a <c>bool</c>, so "omit it" and "it is false"
    /// are the same edit - and it cannot reach a sub-attribute such as <c>name.formatted</c>.
    /// Projecting the JSON handles both, and it works for any resource subclass a downstream
    /// library defines without this code knowing about it.
    ///
    /// Serializer settings match those of the hosts (<c>NullValueHandling.Ignore</c>) so that
    /// the projected body is exactly the unprojected one minus the removed members.
    /// </remarks>
    public static class ScimProjection
    {
        /// <summary>
        /// Attributes with <c>returned: always</c> in RFC 7643, which a client cannot exclude
        /// and which are present whether or not they were requested.
        /// </summary>
        private static readonly string[] AlwaysReturned =
            new[]
            {
                AttributeNames.Identifier,
                AttributeNames.Schemas,
                AttributeNames.Metadata,
            };

        // Fully qualified: Microsoft.SCIM has its own JsonSerializer.
        private static readonly Newtonsoft.Json.JsonSerializer Serializer =
            Newtonsoft.Json.JsonSerializer.Create(
                new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,

                    // SCIM's $ref is an attribute, not Newtonsoft's reference metadata.
                    MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
                    Converters = { new SchematizedJsonConverter() },
                });

        /// <summary>
        /// Returns <paramref name="payload"/> projected, or the payload unchanged when neither
        /// parameter was supplied.
        /// </summary>
        /// <param name="payload">
        /// A <see cref="Resource"/> or a <see cref="QueryResponseBase"/>. Anything else is
        /// returned unchanged - the discovery endpoints are not projected.
        /// </param>
        public static object Apply(
            object payload,
            IReadOnlyCollection<string> requestedAttributePaths,
            IReadOnlyCollection<string> excludedAttributePaths)
        {
            bool hasRequested = ScimProjection.Any(requestedAttributePaths);
            bool hasExcluded = ScimProjection.Any(excludedAttributePaths);

            if (null == payload || (!hasRequested && !hasExcluded))
            {
                return payload;
            }

            if (!(payload is Resource) && !(payload is QueryResponseBase))
            {
                return payload;
            }

            JToken token = JToken.FromObject(payload, ScimProjection.Serializer);

            if (token is JObject root
                && root.Property(ProtocolAttributeNames.Resources, StringComparison.OrdinalIgnoreCase)?.Value is JArray resources)
            {
                foreach (JToken resource in resources)
                {
                    ScimProjection.ProjectResource(
                        resource as JObject,
                        requestedAttributePaths,
                        excludedAttributePaths,
                        hasRequested);
                }

                return root;
            }

            ScimProjection.ProjectResource(
                token as JObject,
                requestedAttributePaths,
                excludedAttributePaths,
                hasRequested);

            return token;
        }

        private static bool Any(IReadOnlyCollection<string> paths)
        {
            return null != paths && paths.Any(path => !string.IsNullOrWhiteSpace(path));
        }

        private static void ProjectResource(
            JObject resource,
            IReadOnlyCollection<string> requestedAttributePaths,
            IReadOnlyCollection<string> excludedAttributePaths,
            bool hasRequested)
        {
            if (null == resource)
            {
                return;
            }

            if (hasRequested)
            {
                ScimProjection.RetainRequested(resource, requestedAttributePaths);
            }

            if (null != excludedAttributePaths)
            {
                foreach (string path in excludedAttributePaths)
                {
                    ScimProjection.Remove(resource, path);
                }
            }
        }

        /// <summary>
        /// Removes every member that was not requested, keeping the always-returned ones.
        /// </summary>
        private static void RetainRequested(JObject resource, IReadOnlyCollection<string> requestedAttributePaths)
        {
            HashSet<string> retain =
                new HashSet<string>(ScimProjection.AlwaysReturned, StringComparer.OrdinalIgnoreCase);

            // A request for name.formatted is a request for name; the sub-attribute pruning
            // below narrows it afterwards.
            foreach (string path in requestedAttributePaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                retain.Add(ScimProjection.RootSegment(path));
            }

            foreach (JProperty property in resource.Properties().ToArray())
            {
                if (!retain.Contains(property.Name))
                {
                    property.Remove();
                }
            }

            foreach (string path in requestedAttributePaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                string root = ScimProjection.RootSegment(path);
                if (string.Equals(root, path.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string remainder = path.Trim().Substring(root.Length + 1);

                if (resource.Property(root, StringComparison.OrdinalIgnoreCase)?.Value is JObject complex)
                {
                    string retainSub = ScimProjection.RootSegment(remainder);

                    foreach (JProperty property in complex.Properties().ToArray())
                    {
                        if (!string.Equals(property.Name, retainSub, StringComparison.OrdinalIgnoreCase))
                        {
                            property.Remove();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Removes one attribute path. A path naming an always-returned attribute is ignored,
        /// per RFC 7643 section 7 - <c>returned: always</c> attributes cannot be excluded.
        /// </summary>
        private static void Remove(JObject resource, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            path = path.Trim();
            string root = ScimProjection.RootSegment(path);

            if (ScimProjection.AlwaysReturned.Contains(root, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(root, path, StringComparison.OrdinalIgnoreCase))
            {
                resource.Property(root, StringComparison.OrdinalIgnoreCase)?.Remove();
                return;
            }

            string remainder = path.Substring(root.Length + 1);
            JToken value = resource.Property(root, StringComparison.OrdinalIgnoreCase)?.Value;

            if (value is JObject complex)
            {
                ScimProjection.Remove(complex, remainder);
            }
            else if (value is JArray multiValued)
            {
                foreach (JToken item in multiValued)
                {
                    if (item is JObject element)
                    {
                        ScimProjection.Remove(element, remainder);
                    }
                }
            }
        }

        private static string RootSegment(string path)
        {
            string trimmed = path.Trim();
            int separator = trimmed.IndexOf('.');
            return separator < 0 ? trimmed : trimmed.Substring(0, separator).Trim();
        }
    }
}
