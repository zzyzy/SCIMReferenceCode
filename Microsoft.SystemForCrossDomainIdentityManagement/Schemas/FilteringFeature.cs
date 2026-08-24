// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System.Runtime.Serialization;

    /// <summary>
    /// The <c>filter</c> member of the service provider configuration
    /// (RFC 7643 section 5), which carries a <c>maxResults</c> alongside <c>supported</c>.
    /// </summary>
    /// <remarks>
    /// A plain <see cref="Feature"/> cannot express <c>maxResults</c>, so the configuration
    /// endpoint used to advertise filtering support without saying how many resources a filter
    /// may return - which is the one number a client needs in order to page rather than be
    /// refused with <c>tooMany</c>.
    /// </remarks>
    [DataContract]
    public class FilteringFeature : Feature
    {
        /// <summary>
        /// The value used when a host does not configure one. RFC 7644 leaves the number to the
        /// service provider; 200 matches the ceiling most SCIM clients assume.
        /// </summary>
        public const int DefaultMaximumResults = 200;

        public FilteringFeature(bool supported)
            : this(supported, FilteringFeature.DefaultMaximumResults)
        {
        }

        public FilteringFeature(bool supported, int maximumResults)
            : base(supported)
        {
            this.MaximumResults = maximumResults;
        }

        /// <summary>
        /// The largest number of resources a filtered query will return. Omitted from the
        /// payload when filtering is not supported.
        /// </summary>
        [DataMember(Name = AttributeNames.MaximumResults, IsRequired = false, EmitDefaultValue = false)]
        public virtual int MaximumResults
        {
            get;
            set;
        }
    }
}
