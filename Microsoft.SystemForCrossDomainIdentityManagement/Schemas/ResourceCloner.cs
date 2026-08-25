// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using Newtonsoft.Json;

    /// <summary>
    /// Produces a deep copy of a resource, so that a multi-operation PATCH can be applied
    /// atomically.
    /// </summary>
    /// <remarks>
    /// RFC 7644 section 3.5.2 requires a PATCH to succeed or fail as a whole: "if any of the
    /// operations fail, the server MUST return an error and MUST NOT apply any of the
    /// operations". <c>ProtocolExtensions.Apply</c> walks the operations in order and mutates
    /// as it goes, so a failure on the third operation leaves the first two applied. Applying
    /// to a copy and swapping it in only on success is how a provider gets the required
    /// behaviour without a transactional store.
    ///
    /// The copy is a JSON round-trip over the <c>[DataMember]</c> surface, which is the same
    /// surface the wire format uses - so anything that survives a request survives a clone,
    /// including a downstream library's extension members.
    /// </remarks>
    public static class ResourceCloner
    {
        private static readonly JsonSerializerSettings Settings =
            new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                TypeNameHandling = TypeNameHandling.None,

                // SCIM's $ref is an attribute, not Newtonsoft's reference metadata.
                MetadataPropertyHandling = MetadataPropertyHandling.Ignore,

                // Without this the clone loses any untyped schema extension, so an atomic PATCH
                // would quietly strip it from the stored resource.
                Converters = { new SchematizedJsonConverter() },
            };

        /// <summary>Returns a deep copy of <paramref name="resource"/>.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="resource"/> is null.</exception>
        public static T Clone<T>(T resource)
            where T : Resource
        {
            if (null == resource)
            {
                throw new ArgumentNullException(nameof(resource));
            }

            string serialized = JsonConvert.SerializeObject(resource, ResourceCloner.Settings);
            return JsonConvert.DeserializeObject<T>(serialized, ResourceCloner.Settings);
        }
    }
}
