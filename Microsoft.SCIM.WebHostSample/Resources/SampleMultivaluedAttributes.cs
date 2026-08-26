// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM.WebHostSample.Resources
{
    public static class SampleMultivaluedAttributes
    {
        public static AttributeScheme ValueSubAttributeScheme
        {
            get
            {
                AttributeScheme valueScheme = new AttributeScheme("value", AttributeDataType.@string, false)
                {
                    Description = SampleConstants.DescriptionValue,
                };
                return valueScheme;
            }
        }

        public static AttributeScheme TypeSubAttributeScheme
        {
            get
            {
                AttributeScheme typeScheme = new AttributeScheme("type", AttributeDataType.@string, false)
                {
                    Description = SampleConstants.DescriptionType,
                    Mutability = Mutability.immutable
                };
                typeScheme.AddCanonicalValues(Types.Group);
                typeScheme.AddCanonicalValues(Types.User);

                return typeScheme;
            }
        }

        /// <summary>
        /// The <c>$ref</c> sub-attribute of a multi-valued attribute's entry.
        /// </summary>
        /// <remarks>
        /// RFC 7643 section 4.2 gives members value, $ref, type and display. The service
        /// returns $ref on every membership - the hosting layer fills in a local URI - and a
        /// schema that does not declare it says the response carries an attribute the resource
        /// does not have. A client validating a response against the advertised schema, which
        /// is what /Schemas is for, rejects it.
        /// </remarks>
        public static AttributeScheme ReferenceSubAttributeScheme
        {
            get
            {
                AttributeScheme referenceScheme =
                    new AttributeScheme(AttributeNames.Reference, AttributeDataType.reference, false)
                    {
                        Description = SampleConstants.DescriptionReference,
                        Mutability = Mutability.immutable
                    };
                referenceScheme.AddReferenceTypes(Types.User);
                referenceScheme.AddReferenceTypes(Types.Group);

                return referenceScheme;
            }
        }

        public static AttributeScheme DisplaySubAttributeScheme
        {
            get
            {
                AttributeScheme typeScheme = new AttributeScheme("display", AttributeDataType.@string, false)
                {
                    Description = SampleConstants.DescriptionDisplay
                };
                return typeScheme;
            }
        }

        public static AttributeScheme Type2SubAttributeScheme
        {
            get
            {
                AttributeScheme typeScheme = new AttributeScheme("type", AttributeDataType.@string, false)
                {
                    Description = SampleConstants.DescriptionType
                };
                typeScheme.AddCanonicalValues("work");
                typeScheme.AddCanonicalValues("home");
                typeScheme.AddCanonicalValues("other");

                return typeScheme;
            }
        }

        public static AttributeScheme TypeAuthenticationSchemesAttributeScheme
        {
            get
            {
                AttributeScheme typeScheme = new AttributeScheme("type", AttributeDataType.@string, false)
                {
                    Description = SampleConstants.DescriptionType,
                    Required = true
                };
                typeScheme.AddCanonicalValues("oauth");
                typeScheme.AddCanonicalValues("oauth2");
                typeScheme.AddCanonicalValues("oauthbearertoken");
                typeScheme.AddCanonicalValues("httpbasic");
                typeScheme.AddCanonicalValues("httpdigest");

                return typeScheme;
            }
        }

        public static AttributeScheme TypeImsSubAttributeScheme
        {
            get
            {
                AttributeScheme typeScheme = new AttributeScheme("type", AttributeDataType.@string, false)
                {
                    Description = SampleConstants.DescriptionType
                };
                typeScheme.AddCanonicalValues("aim");
                typeScheme.AddCanonicalValues("gtalk");
                typeScheme.AddCanonicalValues("icq");
                typeScheme.AddCanonicalValues("xmpp");
                typeScheme.AddCanonicalValues("msn");
                typeScheme.AddCanonicalValues("skype");
                typeScheme.AddCanonicalValues("qq");
                typeScheme.AddCanonicalValues("yahoo");
          
                return typeScheme;
            }
        }

        public static AttributeScheme TypeDefaultSubAttributeScheme
        {
            get
            {
                AttributeScheme typeScheme = new AttributeScheme("type", AttributeDataType.@string, false)
                {
                    Description = SampleConstants.DescriptionType,
                };
                return typeScheme;
            }
        }

        public static AttributeScheme PrimarySubAttributeScheme
        {
            get
            {
                AttributeScheme typeScheme = new AttributeScheme("primary", AttributeDataType.boolean, false)
                {
                    Description = SampleConstants.DescriptionPrimary
                };
                return typeScheme;
            }
        }
    }
}
