// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Scim.EduPass
{
    using Microsoft.SCIM;

    /// <summary>
    /// The <c>/Schemas</c> and <c>/ResourceTypes</c> payloads that advertise the Edupass User
    /// extension.
    /// </summary>
    /// <remarks>
    /// Edupass reads these to learn which attributes a relying party actually stores, so a
    /// party that does not hold UIN/FIN builds its extension scheme with
    /// <c>includeUinFin: false</c> and Edupass stops sending it. The core library derives
    /// nothing here - <c>IProvider.Schema</c> and <c>IProvider.ResourceTypes</c> are collections
    /// the provider supplies - so a provider must add these to them explicitly.
    /// </remarks>
    public static class EduPassTypeSchemes
    {
        /// <summary>
        /// The Edupass User extension schema, for <c>IProvider.Schema</c>.
        /// </summary>
        /// <param name="includeUinFin">
        /// Whether to advertise <c>uinFin</c>. Pass false for a relying party that does not
        /// store it - that is how the specification says to opt out.
        /// </param>
        public static TypeScheme CreateUserExtensionTypeScheme(bool includeUinFin = true)
        {
            TypeScheme scheme =
                new TypeScheme
                {
                    Identifier = EduPassSchemaIdentifiers.UserExtension,
                    Name = "EdupassUser",
                    Description = "Edupass User Extension",
                };

            scheme.AddAttribute(
                EduPassTypeSchemes.CreateStringAttribute(
                    EduPassAttributeNames.IdentityType,
                    EduPassValues.IdentityTypes));

            if (includeUinFin)
            {
                scheme.AddAttribute(
                    EduPassTypeSchemes.CreateStringAttribute(EduPassAttributeNames.UinFin, null));
            }

            scheme.AddAttribute(
                EduPassTypeSchemes.CreateStringAttribute(
                    EduPassAttributeNames.SchoolOrHq,
                    EduPassValues.SchoolOrHq));

            scheme.AddAttribute(
                EduPassTypeSchemes.CreateStringAttribute(
                    EduPassAttributeNames.IdentitySource,
                    EduPassValues.IdentitySources));

            return scheme;
        }

        /// <summary>
        /// The User resource type, declaring the Edupass extension in
        /// <c>schemaExtensions</c>, for <c>IProvider.ResourceTypes</c>.
        /// </summary>
        public static Core2ResourceType CreateUserResourceType()
        {
            return
                new Core2ResourceType
                {
                    Identifier = Types.User,
                    Endpoint = new System.Uri(
                        ServiceConstants.SeparatorSegments + ProtocolConstants.PathUsers,
                        System.UriKind.Relative),
                    Schema = SchemaIdentifiers.Core2User,
                };
        }

        /// <summary>
        /// The <c>groups</c> attribute of the core User schema, which Edupass requires a relying
        /// party with Edupass-managed roles to return.
        /// </summary>
        /// <remarks>
        /// Part of RFC 7643's core User schema rather than the Edupass extension, but the sample
        /// schema in the reference code does not declare it, and Edupass checks
        /// <c>/Schemas</c> - so it is offered here for a provider to add to its User scheme.
        /// </remarks>
        public static AttributeScheme CreateGroupsAttributeScheme()
        {
            AttributeScheme scheme =
                new AttributeScheme(AttributeNames.Groups, AttributeDataType.complex, plural: true)
                {
                    Description = "The groups the user belongs to.",
                    Mutability = Mutability.readOnly,
                    Returned = Returned.@default,
                };

            scheme.AddSubAttribute(
                new AttributeScheme(AttributeNames.Value, AttributeDataType.@string, plural: false)
                {
                    Description = "The identifier of the group.",
                    Mutability = Mutability.readOnly,
                });

            scheme.AddSubAttribute(
                new AttributeScheme(AttributeNames.Reference, AttributeDataType.reference, plural: false)
                {
                    Description = "The URI of the group.",
                    Mutability = Mutability.readOnly,
                });

            scheme.AddSubAttribute(
                new AttributeScheme(AttributeNames.Display, AttributeDataType.@string, plural: false)
                {
                    Description = "The display name of the group.",
                    Mutability = Mutability.readOnly,
                });

            return scheme;
        }

        private static AttributeScheme CreateStringAttribute(string name, string[] canonicalValues)
        {
            AttributeScheme scheme =
                new AttributeScheme(name, AttributeDataType.@string, plural: false)
                {
                    Mutability = Mutability.readWrite,
                    Returned = Returned.@default,
                    Required = false,
                };

            if (null != canonicalValues)
            {
                foreach (string value in canonicalValues)
                {
                    scheme.AddCanonicalValues(value);
                }
            }

            return scheme;
        }
    }
}
