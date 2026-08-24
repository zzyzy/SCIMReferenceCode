//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM
{
    using System.Runtime.Serialization;

    [DataContract]
    public class Core2ServiceConfiguration : ServiceConfigurationBase
    {
        public Core2ServiceConfiguration(
            BulkRequestsFeature bulkRequestsSupport,
            bool supportsEntityTags,
            bool supportsFiltering,
            bool supportsPasswordChange,
            bool supportsPatching,
            bool supportsSorting)
            : this(
                bulkRequestsSupport,
                supportsEntityTags,
                supportsFiltering,
                supportsPasswordChange,
                supportsPatching,
                supportsSorting,
                FilteringFeature.DefaultMaximumResults,
                declareBearerTokenAuthentication: true)
        {
        }

        /// <param name="maximumFilterResults">
        /// The <c>filter.maxResults</c> to advertise.
        /// </param>
        /// <param name="declareBearerTokenAuthentication">
        /// Whether to list the OAuth bearer token scheme in <c>authenticationSchemes</c>.
        /// RFC 7643 section 5 requires the member, and it was previously emitted as an empty
        /// array - telling a client nothing about how to authenticate. Pass false only for a
        /// service that genuinely authenticates some other way, and add that scheme instead.
        /// </param>
        public Core2ServiceConfiguration(
            BulkRequestsFeature bulkRequestsSupport,
            bool supportsEntityTags,
            bool supportsFiltering,
            bool supportsPasswordChange,
            bool supportsPatching,
            bool supportsSorting,
            int maximumFilterResults,
            bool declareBearerTokenAuthentication)
        {
            this.AddSchema(SchemaIdentifiers.Core2ServiceConfiguration);
            this.Metadata =
                new Core2Metadata()
                {
                    ResourceType = Types.ServiceProviderConfiguration
                };

            this.BulkRequests = bulkRequestsSupport;
            this.EntityTags = new Feature(supportsEntityTags);
            this.Filtering = new FilteringFeature(supportsFiltering, maximumFilterResults);
            this.PasswordChange = new Feature(supportsPasswordChange);
            this.Patching = new Feature(supportsPatching);
            this.Sorting = new Feature(supportsSorting);

            if (declareBearerTokenAuthentication)
            {
                this.AddAuthenticationScheme(
                    AuthenticationScheme.CreateOpenStandardForAuthorizationBearerTokenScheme());
            }
        }

        [DataMember(Name = AttributeNames.Metadata)]
        public Core2Metadata Metadata
        {
            get;
            set;
        }
    }
}