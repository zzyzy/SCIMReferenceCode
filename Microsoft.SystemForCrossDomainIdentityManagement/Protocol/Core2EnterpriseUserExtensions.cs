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
                PatchOperation2 operationInternal = new PatchOperation2()
                {
                    OperationName = operation.OperationName,
                    Path = operation.Path
                };

                // RFC 7644 section 3.5.2.2: a remove operation carries no value, and the
                // operation is then left carrying none either - the patchers below read
                // SingleOrDefault() as null and clear the attribute. Deserializing the absent
                // value threw, which surfaced as 400 invalidPath; substituting a value object
                // whose own value was null instead made the remove look like the removal of
                // some other value, which they ignore. Either way nothing could be removed by
                // path alone.
                if (null != operation.Value)
                {
                    OperationValue[] values =
                        JsonConvert.DeserializeObject<OperationValue[]>(
                            operation.Value,
                            ProtocolConstants.JsonSettings.Value);

                    if (null == values)
                    {
                        string value =
                            JsonConvert.DeserializeObject<string>(
                                operation.Value,
                                ProtocolConstants.JsonSettings.Value);

                        operationInternal.AddValue(
                            new OperationValue()
                            {
                                Value = value
                            });
                    }
                    else
                    {
                        foreach (OperationValue value in values)
                        {
                            operationInternal.AddValue(value);
                        }
                    }
                }

                user.Apply(operationInternal);
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

            Address address;
            Address addressExisting;
            if (user.Addresses != null)
            {
                addressExisting =
                    address =
                        user
                        .Addresses
                        .SingleOrDefault(
                            (Address item) =>
                                string.Equals(subAttribute.ComparisonValue, item.ItemType, StringComparison.Ordinal));
            }
            else
            {
                addressExisting = null;
                address =
                    new Address()
                    {
                        ItemType = subAttribute.ComparisonValue
                    };
            }

            string value;
            if (string.Equals(Address.Work, subAttribute.ComparisonValue, StringComparison.Ordinal))
            {
                if
                (
                    string.Equals(
                        Microsoft.SCIM.AttributeNames.Country,
                        operation.Path.ValuePath.AttributePath,
                        StringComparison.Ordinal)
                )
                {
                    value = operation.Value?.FirstOrDefault()?.Value;
                    if
                    (
                            value != null
                        && OperationName.Remove == operation.Name
                        && string.Equals(value, address.Country, StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        value = null;
                    }
                    address.Country = value;
                }

                if
                (
                    string.Equals(
                        Microsoft.SCIM.AttributeNames.Locality,
                        operation.Path.ValuePath.AttributePath,
                        StringComparison.Ordinal)
                )
                {
                    value = operation.Value?.FirstOrDefault()?.Value;
                    if
                    (
                            value != null
                        && OperationName.Remove == operation.Name
                        && string.Equals(value, address.Locality, StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        value = null;
                    }
                    address.Locality = value;
                }

                if
                (
                    string.Equals(
                        Microsoft.SCIM.AttributeNames.PostalCode,
                        operation.Path.ValuePath.AttributePath,
                        StringComparison.Ordinal)
                )
                {
                    value = operation.Value?.FirstOrDefault()?.Value;
                    if
                    (
                            value != null
                        && OperationName.Remove == operation.Name
                        && string.Equals(value, address.PostalCode, StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        value = null;
                    }
                    address.PostalCode = value;
                }

                if
                (
                    string.Equals(
                        Microsoft.SCIM.AttributeNames.Region,
                        operation.Path.ValuePath.AttributePath,
                        StringComparison.Ordinal)
                )
                {
                    value = operation.Value?.FirstOrDefault()?.Value;
                    if
                    (
                            value != null
                        && OperationName.Remove == operation.Name
                        && string.Equals(value, address.Region, StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        value = null;
                    }
                    address.Region = value;
                }

                if
                (
                    string.Equals(
                        Microsoft.SCIM.AttributeNames.StreetAddress,
                        operation.Path.ValuePath.AttributePath,
                        StringComparison.Ordinal)
                )
                {
                    value = operation.Value?.FirstOrDefault()?.Value;
                    if
                    (
                            value != null
                        && OperationName.Remove == operation.Name
                        && string.Equals(value, address.StreetAddress, StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        value = null;
                    }
                    address.StreetAddress = value;
                }
            }

            if (string.Equals(Address.Other, subAttribute.ComparisonValue, StringComparison.Ordinal))
            {
                if
                (
                    string.Equals(
                        Microsoft.SCIM.AttributeNames.Formatted,
                        operation.Path.ValuePath.AttributePath,
                        StringComparison.Ordinal)
                )
                {
                    value = operation.Value?.FirstOrDefault()?.Value;
                    if
                    (
                            value != null
                        && OperationName.Remove == operation.Name
                        && string.Equals(value, address.Formatted, StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        value = null;
                    }
                    address.Formatted = value;
                }
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

            string value;
            if
            (
                string.Equals(
                    Microsoft.SCIM.AttributeNames.GivenName,
                    operation.Path.ValuePath.AttributePath,
                    StringComparison.OrdinalIgnoreCase)
            )
            {
                value = operation.Value?.FirstOrDefault()?.Value;
                if
                (
                        value != null
                    && OperationName.Remove == operation.Name
                    && string.Equals(value, name.GivenName, StringComparison.OrdinalIgnoreCase)
                )
                {
                    value = null;
                }
                name.GivenName = value;
            }

            if
            (
                string.Equals(
                    Microsoft.SCIM.AttributeNames.FamilyName,
                    operation.Path.ValuePath.AttributePath,
                    StringComparison.OrdinalIgnoreCase)
            )
            {
                value = operation.Value?.FirstOrDefault()?.Value;
                if
                (
                        value != null
                    && OperationName.Remove == operation.Name
                    && string.Equals(value, name.FamilyName, StringComparison.OrdinalIgnoreCase)
                )
                {
                    value = null;
                }
                name.FamilyName = value;
            }

            if (string.IsNullOrWhiteSpace(name.FamilyName) && string.IsNullOrWhiteSpace(name.GivenName))
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

            string value = operation.Value?.FirstOrDefault()?.Value;
            if (null != value
                && OperationName.Remove == operation.Name
                && string.Equals(value, existing?.Value, StringComparison.OrdinalIgnoreCase))
            {
                value = null;
            }

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

            string phoneNumberType = subAttribute.ComparisonValue;
            if
            (
                    !string.Equals(phoneNumberType, PhoneNumber.Fax, StringComparison.Ordinal)
                && !string.Equals(phoneNumberType, PhoneNumber.Mobile, StringComparison.Ordinal)
                && !string.Equals(phoneNumberType, PhoneNumber.Work, StringComparison.Ordinal)
            )
            {
                return;
            }

            PhoneNumber phoneNumber;
            PhoneNumber phoneNumberExisting;
            if (user.PhoneNumbers != null)
            {
                phoneNumberExisting =
                    phoneNumber =
                        user
                        .PhoneNumbers
                        .SingleOrDefault(
                            (PhoneNumber item) =>
                                string.Equals(subAttribute.ComparisonValue, item.ItemType, StringComparison.Ordinal));
            }
            else
            {
                phoneNumberExisting = null;
                phoneNumber =
                    new PhoneNumber()
                    {
                        ItemType = subAttribute.ComparisonValue
                    };
            }

            string value = operation.Value?.FirstOrDefault()?.Value;
            if
            (
                    value != null
                && OperationName.Remove == operation.Name
                && string.Equals(value, phoneNumber.Value, StringComparison.OrdinalIgnoreCase)
            )
            {
                value = null;
            }
            phoneNumber.Value = value;

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
