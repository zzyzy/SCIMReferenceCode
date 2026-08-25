//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Newtonsoft.Json;

    public static class Core2EnterpriseUserExtensions
    {
        public static void Apply(this Core2EnterpriseUser user, PatchRequest2Base<PatchOperation2> patch)
        {
            if (null == user)
            {
                throw new ArgumentNullException(nameof(user));
            }

            if (null == patch)
            {
                return;
            }

            if (null == patch.Operations || !patch.Operations.Any())
            {
                return;
            }

            foreach (PatchOperation2 operation in patch.Operations)
            {
                user.Apply(operation);
            }
        }

        public static void Apply(this Core2EnterpriseUser user, PatchRequest2 patch)
        {
            if (null == user)
            {
                throw new ArgumentNullException(nameof(user));
            }

            if (null == patch)
            {
                return;
            }

            if (null == patch.Operations || !patch.Operations.Any())
            {
                return;
            }

            foreach (PatchOperation2Combined operation in patch.Operations)
            {
                foreach (PatchOperation2 operationInternal in ProtocolExtensions.Expand(operation))
                {
                    user.Apply(operationInternal);
                }
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity", Justification = "None")]
        private static void Apply(this Core2EnterpriseUser user, PatchOperation2 operation)
        {
            if (null == operation)
            {
                return;
            }

            if (null == operation.Path || string.IsNullOrWhiteSpace(operation.Path.AttributePath))
            {
                return;
            }

            if (
                   !string.IsNullOrWhiteSpace(operation.Path.SchemaIdentifier)
                && (operation?.Path?.SchemaIdentifier?.Equals(
                        SchemaIdentifiers.Core2EnterpriseUser,
                        StringComparison.OrdinalIgnoreCase) == true))
            {
                user.PatchEnterpriseExtension(operation);
                return;
            }

            // A path qualified by any other schema names an extension attribute, never a core one.
            // Without this it fell through to the switch below on its attribute name alone, so
            // "urn:example:2.0:User:displayName" would have overwritten the core displayName.
            if (
                   !string.IsNullOrWhiteSpace(operation.Path.SchemaIdentifier)
                && !operation.Path.SchemaIdentifier.Equals(
                        SchemaIdentifiers.Core2User,
                        StringComparison.OrdinalIgnoreCase))
            {
                if (!user.TryPatchExtensionAttribute(operation))
                {
                    throw new ArgumentException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            SystemForCrossDomainIdentityManagementProtocolResources.ExceptionInvalidPathTemplate,
                            operation.Path));
                }

                return;
            }

            OperationValue value;
            switch (operation.Path.AttributePath)
            {
                case AttributeNames.Active:
                    if (operation.Name != OperationName.Remove)
                    {
                        value = operation.Value.SingleOrDefault();
                        if (value != null && !string.IsNullOrWhiteSpace(value.Value) && bool.TryParse(value.Value, out bool active))
                        {
                            user.Active = active;
                        }
                    }
                    break;

                case AttributeNames.Addresses:
                    user.PatchAddresses(operation);
                    break;

                case AttributeNames.DisplayName:
                    value = operation.Value.SingleOrDefault();

                    if (OperationName.Remove == operation.Name)
                    {
                        if ((null == value) || string.Equals(user.DisplayName, value.Value, StringComparison.OrdinalIgnoreCase))
                        {
                            value = null;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (null == value)
                    {
                        user.DisplayName = null;
                    }
                    else
                    {
                        user.DisplayName = value.Value;
                    }
                    break;

                case AttributeNames.ElectronicMailAddresses:
                    // A path naming the collection with no value path is a whole-collection
                    // operation, which the per-address patcher declines: it needs a value
                    // path to know which address to touch. Without this a full sync of
                    // emails answered 204 and left the old addresses in place.
                    if (null == operation.Path.ValuePath)
                    {
                        Core2EnterpriseUserExtensions.PatchElectronicMailAddressCollection(user, operation);
                        break;
                    }

                    user.PatchElectronicMailAddresses(operation);
                    break;

                case AttributeNames.Ims:
                    user.PatchInstantMessagings(operation);
                    break;

                case AttributeNames.ExternalIdentifier:
                    value = operation.Value.SingleOrDefault();

                    if (OperationName.Remove == operation.Name)
                    {
                        if ((null == value) || string.Equals(user.ExternalIdentifier, value.Value, StringComparison.OrdinalIgnoreCase))
                        {
                            value = null;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (null == value)
                    {
                        user.ExternalIdentifier = null;
                    }
                    else
                    {
                        user.ExternalIdentifier = value.Value;
                    }
                    break;

                case AttributeNames.Name:
                    user.PatchName(operation);
                    break;

                case AttributeNames.PhoneNumbers:
                    user.PatchPhoneNumbers(operation);
                    break;

                case AttributeNames.PreferredLanguage:
                    value = operation.Value.SingleOrDefault();

                    if (OperationName.Remove == operation.Name)
                    {
                        if ((null == value) || string.Equals(user.PreferredLanguage, value.Value, StringComparison.OrdinalIgnoreCase))
                        {
                            value = null;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (null == value)
                    {
                        user.PreferredLanguage = null;
                    }
                    else
                    {
                        user.PreferredLanguage = value.Value;
                    }
                    break;

                case AttributeNames.Roles:
                    user.PatchRoles(operation);
                    break;

                case AttributeNames.Title:
                    value = operation.Value.SingleOrDefault();

                    if (OperationName.Remove == operation.Name)
                    {
                        if ((null == value) || string.Equals(user.Title, value.Value, StringComparison.OrdinalIgnoreCase))
                        {
                            value = null;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (null == value)
                    {
                        user.Title = null;
                    }
                    else
                    {
                        user.Title = value.Value;
                    }
                    break;

                case AttributeNames.UserName:
                    value = operation.Value.SingleOrDefault();

                    if (OperationName.Remove == operation.Name)
                    {
                        if ((null == value) || string.Equals(user.UserName, value.Value, StringComparison.OrdinalIgnoreCase))
                        {
                            value = null;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (null == value)
                    {
                        user.UserName = null;
                    }
                    else
                    {
                        user.UserName = value.Value;
                    }
                    break;

                // Modelled on the resource and advertised at /Schemas, but previously absent from
                // this switch, so a PATCH against them answered success and changed nothing.
                case AttributeNames.Nickname:
                    Core2EnterpriseUserExtensions.PatchSingularAttribute(
                        operation, () => user.Nickname, (string patched) => user.Nickname = patched);
                    break;

                case AttributeNames.Locale:
                    Core2EnterpriseUserExtensions.PatchSingularAttribute(
                        operation, () => user.Locale, (string patched) => user.Locale = patched);
                    break;

                case AttributeNames.TimeZone:
                    Core2EnterpriseUserExtensions.PatchSingularAttribute(
                        operation, () => user.TimeZone, (string patched) => user.TimeZone = patched);
                    break;

                case AttributeNames.UserType:
                    Core2EnterpriseUserExtensions.PatchSingularAttribute(
                        operation, () => user.UserType, (string patched) => user.UserType = patched);
                    break;

                default:
                    if (!user.TryPatchExtensionAttribute(operation))
                    {
                        // RFC 7644 section 3.5.2: a path that names no attribute the server can
                        // operate on is an error. Ignoring it answered 204 while doing nothing,
                        // which also made the required atomicity unenforceable - a malformed
                        // operation could never fail its request.
                        throw new ArgumentException(
                            string.Format(
                                CultureInfo.InvariantCulture,
                                SystemForCrossDomainIdentityManagementProtocolResources.ExceptionInvalidPathTemplate,
                                operation.Path));
                    }

                    break;
            }
        }

        /// <summary>
        /// Applies an operation to a singular string attribute.
        /// </summary>
        /// <remarks>
        /// Reproduces the idiom already repeated for displayName, title and the rest: a remove
        /// naming a value that does not match the stored one is ignored, and any other remove -
        /// including one with no value at all - clears the attribute.
        /// </remarks>
        private static void PatchSingularAttribute(
            PatchOperation2 operation,
            Func<string> read,
            Action<string> write)
        {
            OperationValue value = operation.Value.SingleOrDefault();

            if (OperationName.Remove == operation.Name)
            {
                if (null != value && !string.Equals(read(), value.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                value = null;
            }

            write(null == value ? null : value.Value);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity", Justification = "None")]
        private static void PatchAddresses(this Core2EnterpriseUser user, PatchOperation2 operation)
        {
            if (null == operation)
            {
                return;
            }

            if
            (
                !string.Equals(
                    Microsoft.SCIM.AttributeNames.Addresses,
                    operation.Path.AttributePath,
                    StringComparison.OrdinalIgnoreCase)
            )
            {
                return;
            }

            if (null == operation.Path.ValuePath)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(operation.Path.ValuePath.AttributePath))
            {
                return;
            }

            IFilter subAttribute = operation.Path.SubAttributes.SingleOrDefault();
            if (null == subAttribute)
            {
                return;
            }

            if
            (
                    (
                            operation.Value != null
                        && operation.Value.Count > 1
                    )
                || (
                            (null == operation.Value || operation.Value.Count < 1)
                        && operation.Name != OperationName.Remove
                    )
            )
            {
                return;
            }

            if
            (
                !string.Equals(
                    Microsoft.SCIM.AttributeNames.Type,
                    subAttribute.AttributePath,
                    StringComparison.OrdinalIgnoreCase)
            )
            {
                return;
            }

            Address addressExisting =
                user
                .Addresses?
                .SingleOrDefault(
                    (Address item) =>
                        string.Equals(subAttribute.ComparisonValue, item.ItemType, StringComparison.Ordinal));

            // A user holding a work address still has no home one, and the value path names
            // the address to create. Reading the missing entry as though it existed
            // dereferenced null.
            Address address =
                addressExisting
                ?? new Address()
                    {
                        ItemType = subAttribute.ComparisonValue
                    };

            // One pass over the sub-attributes rather than a copy of the same eight lines
            // per attribute per address type. The duplicated version reached only country,
            // locality, postalCode, region and streetAddress on a work address and
            // formatted on an "other" one, so the same PATCH answered 204 and did nothing
            // depending on which type the value path named.
            string requested = operation.Value?.FirstOrDefault()?.Value;
            string subAttributePath = operation.Path.ValuePath.AttributePath;

            if (Core2EnterpriseUserExtensions.Matches(subAttributePath, Microsoft.SCIM.AttributeNames.Country))
            {
                address.Country = Core2EnterpriseUserExtensions.ResolveValue(operation, requested, address.Country);
            }
            else if (Core2EnterpriseUserExtensions.Matches(subAttributePath, Microsoft.SCIM.AttributeNames.Locality))
            {
                address.Locality = Core2EnterpriseUserExtensions.ResolveValue(operation, requested, address.Locality);
            }
            else if (Core2EnterpriseUserExtensions.Matches(subAttributePath, Microsoft.SCIM.AttributeNames.PostalCode))
            {
                address.PostalCode = Core2EnterpriseUserExtensions.ResolveValue(operation, requested, address.PostalCode);
            }
            else if (Core2EnterpriseUserExtensions.Matches(subAttributePath, Microsoft.SCIM.AttributeNames.Region))
            {
                address.Region = Core2EnterpriseUserExtensions.ResolveValue(operation, requested, address.Region);
            }
            else if (Core2EnterpriseUserExtensions.Matches(subAttributePath, Microsoft.SCIM.AttributeNames.StreetAddress))
            {
                address.StreetAddress = Core2EnterpriseUserExtensions.ResolveValue(operation, requested, address.StreetAddress);
            }
            else if (Core2EnterpriseUserExtensions.Matches(subAttributePath, Microsoft.SCIM.AttributeNames.Formatted))
            {
                address.Formatted = Core2EnterpriseUserExtensions.ResolveValue(operation, requested, address.Formatted);
            }
            else
            {
                throw new ArgumentException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        SystemForCrossDomainIdentityManagementProtocolResources.ExceptionInvalidPathTemplate,
                        operation.Path));
            }

            if
            (
                    string.IsNullOrWhiteSpace(address.Country)
                && string.IsNullOrWhiteSpace(address.Locality)
                && string.IsNullOrWhiteSpace(address.PostalCode)
                && string.IsNullOrWhiteSpace(address.Region)
                && string.IsNullOrWhiteSpace(address.StreetAddress)
                && string.IsNullOrWhiteSpace(address.Formatted)
            )
            {
                if (addressExisting != null)
                {
                    user.Addresses =
                        user
                        .Addresses
                        .Where(
                            (Address item) =>
                                !string.Equals(subAttribute.ComparisonValue, item.ItemType, StringComparison.Ordinal))
                        .ToArray();
                }

                return;
            }

            if (addressExisting != null)
            {
                return;
            }

            IEnumerable<Address> addresses =
                new Address[]
                    {
                        address
                    };
            if (null == user.Addresses)
            {
                user.Addresses = addresses;
            }
            else
            {
                user.Addresses = user.Addresses.Union(addresses).ToArray();
            }
        }

        private static void PatchCostCenter(ExtensionAttributeEnterpriseUser2 extension, PatchOperation2 operation)
        {
            OperationValue value = operation.Value.SingleOrDefault();

            if (OperationName.Remove == operation.Name)
            {
                if ((null == value) || string.Equals(extension.CostCenter, value.Value, StringComparison.OrdinalIgnoreCase))
                {
                    value = null;
                }
                else
                {
                    return;
                }
            }

            if (null == value)
            {
                extension.CostCenter = null;
            }
            else
            {
                extension.CostCenter = value.Value;
            }
        }

        private static void PatchDepartment(ExtensionAttributeEnterpriseUser2 extension, PatchOperation2 operation)
        {
            OperationValue value = operation.Value.SingleOrDefault();

            if (OperationName.Remove == operation.Name)
            {
                if ((null == value) || string.Equals(extension.Department, value.Value, StringComparison.OrdinalIgnoreCase))
                {
                    value = null;
                }
                else
                {
                    return;
                }
            }

            if (null == value)
            {
                extension.Department = null;
            }
            else
            {
                extension.Department = value.Value;
            }
        }

        private static void PatchDivision(ExtensionAttributeEnterpriseUser2 extension, PatchOperation2 operation)
        {
            OperationValue value = operation.Value.SingleOrDefault();

            if (OperationName.Remove == operation.Name)
            {
                if ((null == value) || string.Equals(extension.Division, value.Value, StringComparison.OrdinalIgnoreCase))
                {
                    value = null;
                }
                else
                {
                    return;
                }
            }

            if (null == value)
            {
                extension.Division = null;
            }
            else
            {
                extension.Division = value.Value;
            }
        }

        private static void PatchElectronicMailAddresses(this Core2EnterpriseUser user, PatchOperation2 operation)
        {
            user.ElectronicMailAddresses = ProtocolExtensions.PatchElectronicMailAddresses(user.ElectronicMailAddresses, operation);
        }

        private static void PatchEmployeeNumber(ExtensionAttributeEnterpriseUser2 extension, PatchOperation2 operation)
        {
            OperationValue value = operation.Value.SingleOrDefault();

            if (OperationName.Remove == operation.Name)
            {
                if ((null == value) || string.Equals(extension.EmployeeNumber, value.Value, StringComparison.OrdinalIgnoreCase))
                {
                    value = null;
                }
                else
                {
                    return;
                }
            }

            if (null == value)
            {
                extension.EmployeeNumber = null;
            }
            else
            {
                extension.EmployeeNumber = value.Value;
            }
        }

        private static void PatchEnterpriseExtension(this Core2EnterpriseUser user, PatchOperation2 operation)
        {
            if (null == operation)
            {
                return;
            }

            if (null == operation.Path || string.IsNullOrWhiteSpace(operation.Path.AttributePath))
            {
                return;
            }

            ExtensionAttributeEnterpriseUser2 extension = user.EnterpriseExtension;
            switch (operation.Path.AttributePath)
            {
                case AttributeNames.CostCenter:
                    Core2EnterpriseUserExtensions.PatchCostCenter(extension, operation);
                    break;

                case AttributeNames.Department:
                    Core2EnterpriseUserExtensions.PatchDepartment(extension, operation);
                    break;

                case AttributeNames.Division:
                    Core2EnterpriseUserExtensions.PatchDivision(extension, operation);
                    break;

                case AttributeNames.EmployeeNumber:
                    Core2EnterpriseUserExtensions.PatchEmployeeNumber(extension, operation);
                    break;

                case AttributeNames.Manager:
                    Core2EnterpriseUserExtensions.PatchManager(extension, operation);
                    break;

                case AttributeNames.Organization:
                    Core2EnterpriseUserExtensions.PatchOrganization(extension, operation);
                    break;

                default:
                    // As the core switch does for a core attribute: a path naming nothing the
                    // extension defines is the client's mistake. Falling through answered 204
                    // and changed nothing, which a client reads as the write having landed.
                    throw new ArgumentException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            SystemForCrossDomainIdentityManagementProtocolResources.ExceptionInvalidPathTemplate,
                            operation.Path));
            }
        }

        private static void PatchManager(ExtensionAttributeEnterpriseUser2 extension, PatchOperation2 operation)
        {
            OperationValue value = operation.Value.SingleOrDefault();

            if (OperationName.Remove == operation.Name)
            {
                if
                (
                       null == value
                    || null == extension.Manager
                    || string.Equals(extension.Manager.Value, value.Value, StringComparison.OrdinalIgnoreCase)
                )
                {
                    value = null;
                }
                else
                {
                    return;
                }
            }

            if (null == value)
            {
                extension.Manager = null;
            }
            else
            {
                extension.Manager = new Manager();
                extension.Manager.Value = value.Value;
            }
        }

        private static void PatchName(this Core2EnterpriseUser user, PatchOperation2 operation)
        {
            if (null == operation)
            {
                return;
            }

            if (null == operation.Path)
            {
                return;
            }

            if
            (
                !string.Equals(
                    Microsoft.SCIM.AttributeNames.Name,
                    operation.Path.AttributePath,
                    StringComparison.OrdinalIgnoreCase)
            )
            {
                return;
            }

            if (null == operation.Path.ValuePath)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(operation.Path.ValuePath.AttributePath))
            {
                return;
            }

            if
            (
                    (
                            operation.Value != null
                        && operation.Value.Count > 1
                    )
                || (
                            (null == operation.Value || operation.Value.Count < 1)
                        && operation.Name != OperationName.Remove
                    )
            )
            {
                return;
            }

            Name nameExisting;
            Name name =
                nameExisting =
                user.Name;

            if (null == name)
            {
                name = new Name();
            }

            string subAttribute = operation.Path.ValuePath.AttributePath;
            string requested = operation.Value?.FirstOrDefault()?.Value;

            // RFC 7643 section 4.1.1 defines six sub-attributes of name. Only givenName and
            // familyName were handled; a PATCH naming any of the other four answered 204 and
            // changed nothing, which a client cannot tell from success. Anything outside the
            // six is refused by name rather than silently dropped.
            if (Core2EnterpriseUserExtensions.Matches(subAttribute, Microsoft.SCIM.AttributeNames.GivenName))
            {
                name.GivenName = Core2EnterpriseUserExtensions.ResolveValue(operation, requested, name.GivenName);
            }
            else if (Core2EnterpriseUserExtensions.Matches(subAttribute, Microsoft.SCIM.AttributeNames.FamilyName))
            {
                name.FamilyName = Core2EnterpriseUserExtensions.ResolveValue(operation, requested, name.FamilyName);
            }
            else if (Core2EnterpriseUserExtensions.Matches(subAttribute, Microsoft.SCIM.AttributeNames.Formatted))
            {
                name.Formatted = Core2EnterpriseUserExtensions.ResolveValue(operation, requested, name.Formatted);
            }
            else if (Core2EnterpriseUserExtensions.Matches(subAttribute, Microsoft.SCIM.AttributeNames.MiddleName))
            {
                name.MiddleName = Core2EnterpriseUserExtensions.ResolveValue(operation, requested, name.MiddleName);
            }
            else if (Core2EnterpriseUserExtensions.Matches(subAttribute, Microsoft.SCIM.AttributeNames.HonorificPrefix))
            {
                name.HonorificPrefix = Core2EnterpriseUserExtensions.ResolveValue(operation, requested, name.HonorificPrefix);
            }
            else if (Core2EnterpriseUserExtensions.Matches(subAttribute, Microsoft.SCIM.AttributeNames.HonorificSuffix))
            {
                name.HonorificSuffix = Core2EnterpriseUserExtensions.ResolveValue(operation, requested, name.HonorificSuffix);
            }
            else
            {
                throw new ArgumentException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        SystemForCrossDomainIdentityManagementProtocolResources.ExceptionInvalidPathTemplate,
                        operation.Path));
            }

            bool empty =
                string.IsNullOrWhiteSpace(name.FamilyName)
                && string.IsNullOrWhiteSpace(name.GivenName)
                && string.IsNullOrWhiteSpace(name.Formatted)
                && string.IsNullOrWhiteSpace(name.MiddleName)
                && string.IsNullOrWhiteSpace(name.HonorificPrefix)
                && string.IsNullOrWhiteSpace(name.HonorificSuffix);

            if (empty)
            {
                if (nameExisting != null)
                {
                    user.Name = null;
                }

                return;
            }

            if (nameExisting != null)
            {
                return;
            }

            user.Name = name;
        }

        /// <summary>
        /// Applies an operation naming <c>emails</c> as a whole rather than one address.
        /// </summary>
        /// <remarks>
        /// The shape a full sync takes: replace hands over the complete set, add appends to
        /// it, and remove with no value clears it. The same three cases the group members
        /// patcher already answers.
        /// </remarks>
        private static void PatchElectronicMailAddressCollection(
            Core2EnterpriseUser user,
            PatchOperation2 operation)
        {
            if (OperationName.Remove == operation.Name
                && (null == operation.Value || !operation.Value.Any()))
            {
                user.ElectronicMailAddresses = null;
                return;
            }

            if (null == operation.Value || !operation.Value.Any())
            {
                return;
            }

            ElectronicMailAddress[] supplied =
                operation
                .Value
                .Where(
                    (OperationValue item) =>
                        !string.IsNullOrWhiteSpace(item.Value))
                .Select(
                    (OperationValue item) =>
                        new ElectronicMailAddress()
                        {
                            Value = item.Value
                        })
                .ToArray();

            switch (operation.Name)
            {
                case OperationName.Replace:
                    user.ElectronicMailAddresses = supplied;
                    break;

                case OperationName.Add:
                    user.ElectronicMailAddresses =
                        null == user.ElectronicMailAddresses
                            ? supplied
                            : user.ElectronicMailAddresses.Concat(supplied).ToArray();
                    break;

                case OperationName.Remove:
                    if (null == user.ElectronicMailAddresses)
                    {
                        break;
                    }

                    user.ElectronicMailAddresses =
                        user
                        .ElectronicMailAddresses
                        .Where(
                            (ElectronicMailAddress item) =>
                                !supplied.Any(
                                    (ElectronicMailAddress removed) =>
                                        string.Equals(
                                            removed.Value,
                                            item.Value,
                                            StringComparison.OrdinalIgnoreCase)))
                        .ToArray();
                    break;
            }
        }

        private static bool Matches(string candidate, string attributeName)
        {
            return string.Equals(candidate, attributeName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The value an operation leaves a singular sub-attribute holding.
        /// </summary>
        /// <remarks>
        /// A remove clears the attribute when it names the value being held, or names no
        /// value at all. Naming a different value removes nothing - the attribute keeps
        /// what it had. This used to assign the requested value on that path, so a remove
        /// of a value the resource did not hold *wrote* that value: the operation performed
        /// an add. RFC 7644 section 3.5.2.2.
        /// </remarks>
        private static string ResolveValue(PatchOperation2 operation, string requested, string existing)
        {
            if (OperationName.Remove != operation.Name)
            {
                return requested;
            }

            if (null == requested || string.Equals(requested, existing, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return existing;
        }

        private static void PatchOrganization(ExtensionAttributeEnterpriseUser2 extension, PatchOperation2 operation)
        {
            OperationValue value = operation.Value.SingleOrDefault();

            if (OperationName.Remove == operation.Name)
            {
                if ((null == value) || string.Equals(extension.Organization, value.Value, StringComparison.OrdinalIgnoreCase))
                {
                    value = null;
                }
                else
                {
                    return;
                }
            }

            if (null == value)
            {
                extension.Organization = null;
            }
            else
            {
                extension.Organization = value.Value;
            }
        }

        /// <summary>
        /// Applies an operation to <c>ims</c>, addressed as <c>ims[type eq "..."].value</c>.
        /// </summary>
        /// <remarks>
        /// Modelled on <see cref="PatchPhoneNumbers"/>, with one difference: the type is not
        /// checked against the canonical list on <see cref="InstantMessagingBase"/>. RFC 7643
        /// section 7 makes canonical values a recommendation rather than a constraint, and
        /// rejecting an unlisted type here would mean silently discarding the operation - the
        /// failure mode this pass exists to remove.
        /// </remarks>
        private static void PatchInstantMessagings(this Core2EnterpriseUser user, PatchOperation2 operation)
        {
            if (null == operation)
            {
                return;
            }

            if (null == operation.Path.ValuePath
                || string.IsNullOrWhiteSpace(operation.Path.ValuePath.AttributePath))
            {
                return;
            }

            IFilter subAttribute = operation.Path.SubAttributes.SingleOrDefault();
            if (null == subAttribute
                || !string.Equals(
                        AttributeNames.Type,
                        subAttribute.AttributePath,
                        StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if ((null != operation.Value && operation.Value.Count > 1)
                || ((operation.Value?.Count ?? 0) < 1 && operation.Name != OperationName.Remove))
            {
                return;
            }

            InstantMessaging existing =
                user.InstantMessagings?.SingleOrDefault(
                    (InstantMessaging item) =>
                        string.Equals(subAttribute.ComparisonValue, item.ItemType, StringComparison.Ordinal));

            string value =
                Core2EnterpriseUserExtensions.ResolveValue(
                    operation,
                    operation.Value?.FirstOrDefault()?.Value,
                    existing?.Value);

            if (string.IsNullOrWhiteSpace(value))
            {
                if (null != existing)
                {
                    user.InstantMessagings =
                        user
                        .InstantMessagings
                        .Where(
                            (InstantMessaging item) =>
                                !string.Equals(subAttribute.ComparisonValue, item.ItemType, StringComparison.Ordinal))
                        .ToArray();
                }

                return;
            }

            if (null != existing)
            {
                existing.Value = value;
                return;
            }

            InstantMessaging added =
                new InstantMessaging()
                {
                    ItemType = subAttribute.ComparisonValue,
                    Value = value,
                };

            user.InstantMessagings =
                null == user.InstantMessagings
                    ? new[] { added }
                    : user.InstantMessagings.Concat(new[] { added }).ToArray();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity", Justification = "None")]
        private static void PatchPhoneNumbers(this Core2EnterpriseUser user, PatchOperation2 operation)
        {
            if (null == operation)
            {
                return;
            }

            if
            (
                !string.Equals(
                    Microsoft.SCIM.AttributeNames.PhoneNumbers,
                    operation.Path.AttributePath,
                    StringComparison.OrdinalIgnoreCase)
            )
            {
                return;
            }

            if (null == operation.Path.ValuePath)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(operation.Path.ValuePath.AttributePath))
            {
                return;
            }

            IFilter subAttribute = operation.Path.SubAttributes.SingleOrDefault();
            if (null == subAttribute)
            {
                return;
            }

            if
            (
                    (
                            operation.Value != null
                        && operation.Value.Count > 1
                    )
                || (
                            (null == operation.Value || operation.Value.Count < 1)
                        && operation.Name != OperationName.Remove
                    )
            )
            {
                return;
            }

            if
            (
                !string.Equals(
                    Microsoft.SCIM.AttributeNames.Type,
                    subAttribute.AttributePath,
                    StringComparison.OrdinalIgnoreCase)
            )
            {
                return;
            }

            // Every canonical type RFC 7643 section 4.1.2 defines, not three of them. Home,
            // other and pager were missing, so phoneNumbers[type eq "home"].value - which
            // Entra ID sends - answered 204 and changed nothing. The guard itself stays:
            // a value path naming a type the schema does not define selects no entry, and
            // inventing one from the filter would put an unnamed type on the resource.
            string phoneNumberType = subAttribute.ComparisonValue;
            if
            (
                    !string.Equals(phoneNumberType, PhoneNumber.Fax, StringComparison.Ordinal)
                && !string.Equals(phoneNumberType, PhoneNumber.Home, StringComparison.Ordinal)
                && !string.Equals(phoneNumberType, PhoneNumber.Mobile, StringComparison.Ordinal)
                && !string.Equals(phoneNumberType, PhoneNumber.Other, StringComparison.Ordinal)
                && !string.Equals(phoneNumberType, PhoneNumber.Pager, StringComparison.Ordinal)
                && !string.Equals(phoneNumberType, PhoneNumber.Work, StringComparison.Ordinal)
            )
            {
                return;
            }

            PhoneNumber phoneNumberExisting =
                user
                .PhoneNumbers?
                .SingleOrDefault(
                    (PhoneNumber item) =>
                        string.Equals(subAttribute.ComparisonValue, item.ItemType, StringComparison.Ordinal));

            // A user who already holds a number of another type still has no entry of this
            // one, and the value path names the entry to create. Reading the missing entry
            // as though it existed dereferenced null.
            PhoneNumber phoneNumber =
                phoneNumberExisting
                ?? new PhoneNumber()
                    {
                        ItemType = subAttribute.ComparisonValue
                    };

            phoneNumber.Value =
                Core2EnterpriseUserExtensions.ResolveValue(
                    operation,
                    operation.Value?.FirstOrDefault()?.Value,
                    phoneNumber.Value);

            if (string.IsNullOrWhiteSpace(phoneNumber.Value))
            {
                if (phoneNumberExisting != null)
                {
                    user.PhoneNumbers =
                        user
                        .PhoneNumbers
                        .Where(
                            (PhoneNumber item) =>
                                !string.Equals(subAttribute.ComparisonValue, item.ItemType, StringComparison.Ordinal))
                        .ToArray();
                }
                return;
            }

            if (phoneNumberExisting != null)
            {
                return;
            }

            IEnumerable<PhoneNumber> phoneNumbers =
                new PhoneNumber[]
                    {
                        phoneNumber
                    };
            if (null == user.PhoneNumbers)
            {
                user.PhoneNumbers = phoneNumbers;
            }
            else
            {
                user.PhoneNumbers = user.PhoneNumbers.Union(phoneNumbers).ToArray();
            }
        }

        private static void PatchRoles(this Core2EnterpriseUser user, PatchOperation2 operation)
        {
            user.Roles = ProtocolExtensions.PatchRoles(user.Roles, operation);
        }
    }
}
