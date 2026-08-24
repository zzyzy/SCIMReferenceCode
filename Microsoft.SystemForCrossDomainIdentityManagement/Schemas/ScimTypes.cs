// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    /// <summary>
    /// The <c>scimType</c> detail error keywords of RFC 7644 section 3.12, table 9.
    /// </summary>
    /// <remarks>
    /// The RFC requires one of these on a 400, and permits <c>uniqueness</c> on a 409. They
    /// tell a client which of several possible mistakes it made, which a bare status code and
    /// a prose <c>detail</c> cannot.
    /// </remarks>
    public static class ScimTypes
    {
        /// <summary>The filter yields more results than the server will process.</summary>
        public const string TooMany = "tooMany";

        /// <summary>The resource being created already exists.</summary>
        public const string Uniqueness = "uniqueness";

        /// <summary>The request body was not valid against the request schema.</summary>
        public const string InvalidSyntax = "invalidSyntax";

        /// <summary>The filter attribute and comparison combination is not supported.</summary>
        public const string InvalidFilter = "invalidFilter";

        /// <summary>A PATCH operation's <c>path</c> was invalid or malformed.</summary>
        public const string InvalidPath = "invalidPath";

        /// <summary>
        /// A required value was missing, or the value was incompatible with the operation,
        /// the attribute type or the resource schema.
        /// </summary>
        public const string InvalidValue = "invalidValue";

        /// <summary>A PATCH <c>path</c> matched no attribute or value to operate on.</summary>
        public const string NoTarget = "noTarget";

        /// <summary>The specified SCIM protocol version is not supported.</summary>
        public const string InvalidVersion = "invalidVers";

        /// <summary>Mutability was violated - e.g. a write to a read-only attribute.</summary>
        public const string Mutability = "mutability";

        /// <summary>The sort parameters were not valid.</summary>
        public const string InvalidSortSpecification = "sensitive";
    }
}
