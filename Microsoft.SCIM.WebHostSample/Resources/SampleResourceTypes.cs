// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM.WebHostSample.Resources
{
    using System;

    public class SampleResourceTypes
    {
        public static Core2ResourceType UserResourceType
        {
            get
            {
                // The base schema is the core User one. The enterprise schema extends it,
                // and now that a resource type can say so it is declared as an extension
                // rather than substituted for the base - which had been telling clients
                // that /Users does not serve the core User schema at all.
                Core2ResourceType userResource = new Core2ResourceType
                {
                    Identifier = Types.User,
                    Endpoint = new Uri($"{SampleConstants.SampleScimEndpoint}/Users"),
                    Schema = $"{SampleConstants.Core2SchemaPrefix}{Types.User}"
                };

                userResource.AddSchemaExtension(SampleConstants.UserEnterpriseSchema, required: false);

                return userResource;
            }
        }

        public static Core2ResourceType GroupResourceType
        {
            get
            {
                Core2ResourceType groupResource = new Core2ResourceType
                {
                    Identifier = Types.Group,
                    Endpoint = new Uri($"{SampleConstants.SampleScimEndpoint}/Groups"),
                    Schema = $"{SampleConstants.Core2SchemaPrefix}{Types.Group}"
                };

                return groupResource;
            }
        }
    }
}
