//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM
{
    public static class QueryKeys
    {
        public const string Attributes = "attributes";
        public const string Count = "count";
        public const string Filter = "filter";
        public const string ExcludedAttributes = "excludedAttributes";
        public const string StartIndex = "startIndex";

        /// <summary>RFC 7644 section 3.4.2.3. Read from a POST query body; see SearchRequest.</summary>
        public const string SortBy = "sortBy";
        public const string SortOrder = "sortOrder";
    }
}
