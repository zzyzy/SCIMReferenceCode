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
        /// <remarks>
        /// The extension is declared required, which is what the specification's own
        /// Get All Resource Types example shows. It is how Edupass learns the extension is
        /// part of the User resource rather than a schema the party happens to publish.
        /// </remarks>
        public static Core2ResourceType CreateUserResourceType()
        {
            Core2ResourceType resourceType =
                new Core2ResourceType
                {
                    Identifier = Types.User,
                    Endpoint = new System.Uri(
                        ServiceConstants.SeparatorSegments + ProtocolConstants.PathUsers,
                        System.UriKind.Relative),
                    Schema = SchemaIdentifiers.Core2User,
                };

            resourceType.AddSchemaExtension(EduPassSchemaIdentifiers.UserExtension, required: true);

            return resourceType;
        }

        /// <summary>
        /// The Group resource type, for <c>IProvider.ResourceTypes</c>.
        /// </summary>
        /// <remarks>
        /// A relying party with Edupass-managed roles serves <c>/Groups</c>, so the endpoint
        /// has to appear here. Omitting it told Edupass the party had no Group resource while
        /// the route answered requests.
        /// </remarks>
        public static Core2ResourceType CreateGroupResourceType()
        {
            return
                new Core2ResourceType
                {
                    Identifier = Types.Group,
                    Endpoint = new System.Uri(
                        ServiceConstants.SeparatorSegments + ProtocolConstants.PathGroups,
                        System.UriKind.Relative),
                    Schema = SchemaIdentifiers.Core2Group,
                };
        }

        /// <summary>
        /// The core User schema as an Edupass relying party supports it, for
        /// <c>IProvider.Schema</c>.
        /// </summary>
        /// <remarks>
        /// The specification requires <c>/Schemas</c> to "minimally include the core User
        /// schema", and says a relying party indicates which fields it supports through that
        /// endpoint. So this is not RFC 7643's whole User schema: it is exactly the User
        /// Schema table the Edupass specification sets out - externalId, userName, name with
        /// only <c>formatted</c> beneath it, emails, title, active - plus <c>groups</c>.
        /// </remarks>
        public static TypeScheme CreateUserTypeScheme()
        {
            TypeScheme scheme =
                new TypeScheme
                {
                    Identifier = SchemaIdentifiers.Core2User,
                    Name = Types.User,
                    Description = "User Account",
                };

            scheme.AddAttribute(
                new AttributeScheme(AttributeNames.UserName, AttributeDataType.@string, plural: false)
                {
                    Description = "The Edupass identifier, unique within the relying party.",
                    Required = true,
                    Uniqueness = Uniqueness.server,
                    Returned = Returned.@default,
                });

            scheme.AddAttribute(
                new AttributeScheme(
                    AttributeNames.ExternalIdentifier,
                    AttributeDataType.@string,
                    plural: false)
                {
                    Description = "Edupass's identifier for the resource.",
                    Returned = Returned.@default,
                });

            AttributeScheme name =
                new AttributeScheme(AttributeNames.Name, AttributeDataType.complex, plural: false)
                {
                    Description = "The name of the user associated with the Edupass identity.",
                    Returned = Returned.@default,
                };

            // Only formatted. The specification says so explicitly: "While SCIM's name object
            // defines other name components like familyName, givenName and honorificPrefix,
            // Edupass will not be including these fields."
            name.AddSubAttribute(
                new AttributeScheme(AttributeNames.Formatted, AttributeDataType.@string, plural: false)
                {
                    Description = "The full name of the user.",
                });

            scheme.AddAttribute(name);

            AttributeScheme emails =
                new AttributeScheme(
                    AttributeNames.ElectronicMailAddresses,
                    AttributeDataType.complex,
                    plural: true)
                {
                    Description = "Email addresses for the user.",
                    Returned = Returned.@default,
                };

            AttributeScheme emailType =
                new AttributeScheme(AttributeNames.Type, AttributeDataType.@string, plural: false)
                {
                    Description = "The kind of email address.",
                };

            foreach (string value in EduPassValues.ElectronicMailAddressTypes)
            {
                emailType.AddCanonicalValues(value);
            }

            emails.AddSubAttribute(
                new AttributeScheme(AttributeNames.Value, AttributeDataType.@string, plural: false)
                {
                    Description = "The email address.",
                });
            emails.AddSubAttribute(emailType);
            emails.AddSubAttribute(
                new AttributeScheme(AttributeNames.Primary, AttributeDataType.boolean, plural: false)
                {
                    Description = "True for the notification email, false otherwise.",
                });

            scheme.AddAttribute(emails);

            scheme.AddAttribute(
                new AttributeScheme(AttributeNames.Title, AttributeDataType.@string, plural: false)
                {
                    Description = "The job title of the user.",
                    Returned = Returned.@default,
                });

            scheme.AddAttribute(
                new AttributeScheme(AttributeNames.Active, AttributeDataType.boolean, plural: false)
                {
                    Description = "Whether the identity is active at the relying party.",
                    Returned = Returned.@default,
                });

            scheme.AddAttribute(EduPassTypeSchemes.CreateGroupsAttributeScheme());

            return scheme;
        }

        /// <summary>
        /// The core Group schema as an Edupass relying party supports it, for
        /// <c>IProvider.Schema</c>.
        /// </summary>
        /// <remarks>
        /// "For RPs that support the Group resource, this must include the core Group schema."
        /// The Edupass Group Schema table is three attributes wide: externalId, displayName
        /// and the members the Members Attribute section describes.
        /// </remarks>
        public static TypeScheme CreateGroupTypeScheme()
        {
            TypeScheme scheme =
                new TypeScheme
                {
                    Identifier = SchemaIdentifiers.Core2Group,
                    Name = Types.Group,
                    Description = "Group",
                };

            scheme.AddAttribute(
                new AttributeScheme(AttributeNames.DisplayName, AttributeDataType.@string, plural: false)
                {
                    Description =
                        "The application role, as <location code>_<app code>_<role code>.",
                    Required = true,
                    // The role a Group encodes is fixed at creation: Edupass creates a Group per
                    // role and deletes it when the role is deprecated, never renaming one.
                    // BaseEduPassScimProvider enforces this, so the advertisement is honest.
                    Mutability = Mutability.immutable,
                    Uniqueness = Uniqueness.server,
                    Returned = Returned.@default,
                    CaseExact = true,
                });

            scheme.AddAttribute(
                new AttributeScheme(
                    AttributeNames.ExternalIdentifier,
                    AttributeDataType.@string,
                    plural: false)
                {
                    Description = "Edupass's identifier for the Group encoding the role.",
                    Returned = Returned.@default,
                });

            AttributeScheme members =
                new AttributeScheme(AttributeNames.Members, AttributeDataType.complex, plural: true)
                {
                    Description = "The users holding the application role.",
                    Returned = Returned.@default,
                };

            // A membership entry is added or removed whole, never edited in place, which is
            // what immutable means for a multi-valued sub-attribute (RFC 7643 section 7).
            members.AddSubAttribute(
                new AttributeScheme(AttributeNames.Value, AttributeDataType.@string, plural: false)
                {
                    Description = "The identifier of the member.",
                    Mutability = Mutability.immutable,
                    CaseExact = true,
                });
            AttributeScheme memberReference =
                new AttributeScheme(AttributeNames.Reference, AttributeDataType.reference, plural: false)
                {
                    Description = "The URI of the member.",
                    Mutability = Mutability.immutable,
                    CaseExact = true,
                };

            // RFC 7643 section 7 makes referenceTypes required on a reference attribute.
            memberReference.AddReferenceTypes(Types.User);
            members.AddSubAttribute(memberReference);
            members.AddSubAttribute(
                new AttributeScheme(AttributeNames.Display, AttributeDataType.@string, plural: false)
                {
                    Description = "The display name of the member.",
                    Mutability = Mutability.immutable,
                    CaseExact = true,
                });

            scheme.AddAttribute(members);

            return scheme;
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
                    CaseExact = true,
                });

            AttributeScheme groupReference =
                new AttributeScheme(AttributeNames.Reference, AttributeDataType.reference, plural: false)
                {
                    Description = "The URI of the group.",
                    Mutability = Mutability.readOnly,
                    CaseExact = true,
                };

            // RFC 7643 section 7 makes referenceTypes required on a reference attribute.
            groupReference.AddReferenceTypes(Types.Group);
            scheme.AddSubAttribute(groupReference);

            scheme.AddSubAttribute(
                new AttributeScheme(AttributeNames.Display, AttributeDataType.@string, plural: false)
                {
                    Description = "The display name of the group.",
                    Mutability = Mutability.readOnly,
                    CaseExact = true,
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
