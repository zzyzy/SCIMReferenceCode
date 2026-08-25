//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM
{
    using System.Collections.Generic;

    /// <summary>
    /// A resource that carries schema extensions this library has no type for.
    /// </summary>
    /// <remarks>
    /// RFC 7643 section 3.3 makes a resource's <c>schemas</c> open: a service may add an
    /// extension of its own, and a client may send one the service was never compiled
    /// against. Both <see cref="Core2UserBase"/> and <see cref="Core2GroupBase"/> hold such
    /// extensions in an untyped dictionary, and did so with identical members and no shared
    /// declaration - so <see cref="SchematizedJsonConverter"/>, which is what makes the
    /// dictionary reachable from the wire, was written against the user and silently dropped
    /// every extension on a group.
    ///
    /// This is the declaration the converter needs. A resource type that wants its extension
    /// bound to real properties declares them as <c>[DataMember]</c>s instead, as
    /// <c>EduPassUser</c> does; the converter leaves those alone.
    /// </remarks>
    public interface IExtensibleResource
    {
        /// <summary>The extensions held by schema URI.</summary>
        IReadOnlyDictionary<string, IDictionary<string, object>> CustomExtension
        {
            get;
        }

        /// <summary>
        /// Records an extension. Implementations reject a key that is not a SCIM extension
        /// URI, and a value that is not a <see cref="Dictionary{TKey, TValue}"/>.
        /// </summary>
        void AddCustomAttribute(string key, object value);
    }
}
