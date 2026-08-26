//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Runtime.Serialization;
    using System.Text;

    /// <summary>
    /// The body of a query made with POST, per RFC 7644 section 3.4.3.
    /// </summary>
    /// <remarks>
    /// The same parameters section 3.4.2 defines for the query string, carried in a body
    /// instead: a filter long enough to overflow a URL, or one a client would rather not have
    /// written to every access log along the way, has nowhere else to go.
    /// </remarks>
    [DataContract]
    public sealed class SearchRequest
    {
        private const char SeparatorAttributes = ',';

        [DataMember(Name = AttributeNames.Schemas, Order = 0)]
        public IReadOnlyCollection<string> Schemas
        {
            get;
            set;
        }

        [DataMember(Name = QueryKeys.Attributes, Order = 1, IsRequired = false, EmitDefaultValue = false)]
        public IReadOnlyCollection<string> Attributes
        {
            get;
            set;
        }

        [DataMember(Name = QueryKeys.ExcludedAttributes, Order = 2, IsRequired = false, EmitDefaultValue = false)]
        public IReadOnlyCollection<string> ExcludedAttributes
        {
            get;
            set;
        }

        [DataMember(Name = QueryKeys.Filter, Order = 3, IsRequired = false, EmitDefaultValue = false)]
        public string Filter
        {
            get;
            set;
        }

        [DataMember(Name = QueryKeys.SortBy, Order = 4, IsRequired = false, EmitDefaultValue = false)]
        public string SortBy
        {
            get;
            set;
        }

        [DataMember(Name = QueryKeys.SortOrder, Order = 5, IsRequired = false, EmitDefaultValue = false)]
        public string SortOrder
        {
            get;
            set;
        }

        [DataMember(Name = QueryKeys.StartIndex, Order = 6, IsRequired = false, EmitDefaultValue = false)]
        public int? StartIndex
        {
            get;
            set;
        }

        [DataMember(Name = QueryKeys.Count, Order = 7, IsRequired = false, EmitDefaultValue = false)]
        public int? Count
        {
            get;
            set;
        }

        /// <summary>
        /// Renders the request as the query string that would have carried the same query on a
        /// GET, without a leading "?".
        /// </summary>
        /// <remarks>
        /// Rather than a second way of reading the same parameters. RFC 7644 section 3.4.3
        /// says a POST query is answered "as specified in Section 3.4.2" - it is the same
        /// query, arriving differently - so it is turned back into one and handed to the code
        /// that already serves GET. A separate parser would be a second place for filter
        /// handling, attribute notation and pagination to disagree with themselves.
        /// </remarks>
        public string ToQueryString()
        {
            StringBuilder result = new StringBuilder();

            SearchRequest.Append(result, QueryKeys.Attributes, SearchRequest.Join(this.Attributes));
            SearchRequest.Append(result, QueryKeys.ExcludedAttributes, SearchRequest.Join(this.ExcludedAttributes));
            SearchRequest.Append(result, QueryKeys.Filter, this.Filter);
            SearchRequest.Append(result, QueryKeys.SortBy, this.SortBy);
            SearchRequest.Append(result, QueryKeys.SortOrder, this.SortOrder);

            if (this.StartIndex.HasValue)
            {
                SearchRequest.Append(
                    result,
                    QueryKeys.StartIndex,
                    this.StartIndex.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (this.Count.HasValue)
            {
                SearchRequest.Append(
                    result,
                    QueryKeys.Count,
                    this.Count.Value.ToString(CultureInfo.InvariantCulture));
            }

            return result.ToString();
        }

        private static string Join(IReadOnlyCollection<string> values)
        {
            if (null == values || values.Count < 1)
            {
                return null;
            }

            return
                string.Join(
                    SearchRequest.SeparatorAttributes.ToString(CultureInfo.InvariantCulture),
                    values.Where((string item) => !string.IsNullOrWhiteSpace(item)));
        }

        private static void Append(StringBuilder buffer, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            // No leading "?": UriBuilder.Query adds one of its own, and a string that already
            // carries it becomes "??attributes=…", whose first key is "?attributes" - which
            // matches nothing, so the parameter is silently ignored.
            if (buffer.Length > 0)
            {
                buffer.Append('&');
            }

            buffer.Append(Uri.EscapeDataString(key));
            buffer.Append('=');
            buffer.Append(Uri.EscapeDataString(value));
        }
    }
}
