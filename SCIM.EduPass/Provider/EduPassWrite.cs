// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Scim.EduPass
{
    using System;
    using System.Collections.Generic;
    using Microsoft.SCIM;

    /// <summary>The request a store write is serving.</summary>
    public enum EduPassWriteKind
    {
        /// <summary>A PUT. The resource is the request whole, and an omitted attribute is cleared.</summary>
        Replace,

        /// <summary>A PATCH. The resource is the stored one with the operations applied.</summary>
        Patch,
    }

    /// <summary>
    /// Which request a write to <see cref="IEduPassStore"/> is serving, and what a PATCH asked for.
    /// </summary>
    /// <remarks>
    /// Both verbs reach the store as a whole resource, because that is what applying a PATCH
    /// produces: RFC 7644 section 3.5.2 defines the operations against the resource, and what
    /// the store is to hold afterwards is the result. A store that writes every column needs
    /// nothing more. One that records an audit row, raises a change event, or issues an UPDATE
    /// naming only the columns that moved needs to know which verb it was serving, and cannot
    /// recover that from the resource alone - which is why it is passed rather than inferred.
    ///
    /// <see cref="Operations"/> is the request body as it arrived, so:
    ///
    /// - <see cref="PatchOperation2Base.Name"/> is the <c>op</c> - add, remove or replace;
    /// - <see cref="PatchOperation2Base.Path"/> is <c>path</c>, already parsed into its schema
    ///   identifier, attribute path and the value filter that selects one entry of a
    ///   multi-valued attribute (<c>emails[type eq "work"].value</c> is all three);
    /// - <see cref="PatchOperation2Combined.Value"/> is <c>value</c> as JSON, because an
    ///   operation's value is a scalar, an object or an array depending on what it names.
    ///
    /// Two things it is not. An operation whose <c>path</c> is absent names no attribute at
    /// all - its value is an object carrying several, and reading them means reading that
    /// JSON. And the operations are what was asked for, not what changed: they carry no
    /// before-value and an operation can be a no-op. Where either matters, compare the
    /// resource being written against the one the store was read for - the provider patches a
    /// copy, so the stored resource is still the before-image when the write arrives.
    /// </remarks>
    public sealed class EduPassWrite
    {
        private static readonly PatchOperation2Combined[] None = new PatchOperation2Combined[0];

        private EduPassWrite(EduPassWriteKind kind, IReadOnlyCollection<PatchOperation2Combined> operations)
        {
            this.Kind = kind;
            this.Operations = operations;
        }

        /// <summary>A write serving a PUT.</summary>
        public static EduPassWrite Replace { get; } =
            new EduPassWrite(EduPassWriteKind.Replace, EduPassWrite.None);

        public EduPassWriteKind Kind { get; }

        /// <summary>
        /// The operations a PATCH asked for, in the order the request listed them. Empty for
        /// a PUT, so that a caller may enumerate it without testing <see cref="Kind"/> first.
        /// </summary>
        public IReadOnlyCollection<PatchOperation2Combined> Operations { get; }

        /// <summary>A write serving the given PATCH.</summary>
        public static EduPassWrite Patch(PatchRequest2 request)
        {
            if (null == request)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return new EduPassWrite(EduPassWriteKind.Patch, request.Operations ?? EduPassWrite.None);
        }
    }
}
