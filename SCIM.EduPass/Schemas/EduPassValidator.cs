// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Scim.EduPass
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Text.RegularExpressions;
    using Microsoft.SCIM;

    /// <summary>
    /// Validates a User or Group against the Edupass interface specification.
    /// </summary>
    /// <remarks>
    /// These are constraints Edupass states and SCIM does not, so they cannot live in
    /// Microsoft.SCIM: closed value sets for the extension attributes, a UIN/FIN format, a
    /// 256-character ceiling on every variable-length attribute, and exactly one primary email
    /// address.
    ///
    /// A failure throws <see cref="ScimTypedException"/> with <c>invalidValue</c>, which the
    /// shared handler turns into a 400 carrying that <c>scimType</c> - the shape the
    /// specification asks for.
    /// </remarks>
    public static class EduPassValidator
    {
        /// <summary>
        /// The maximum length of any variable-length attribute, including an application role
        /// name.
        /// </summary>
        public const int MaximumAttributeLength = 256;

        private static readonly Regex UinFinExpression =
            new Regex(@"^[STFGM]\d{7}[A-Z]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Throws if <paramref name="user"/> violates the specification.
        /// </summary>
        /// <param name="requireUinFin">
        /// Whether this relying party stores UIN/FIN. When false, a supplied <c>uinFin</c> is
        /// still format-checked but never required.
        /// </param>
        public static void Validate(EduPassUser user, bool requireUinFin = false)
        {
            if (null == user)
            {
                throw new ArgumentNullException(nameof(user));
            }

            EduPassValidator.RequirePresent(user.UserName, AttributeNames.UserName);
            EduPassValidator.RequireLength(user.UserName, AttributeNames.UserName);
            EduPassValidator.RequireLength(user.ExternalIdentifier, AttributeNames.ExternalIdentifier);
            EduPassValidator.RequireLength(user.Name?.Formatted, "name.formatted");
            EduPassValidator.RequireLength(user.Title, AttributeNames.Title);

            EduPassValidator.ValidateElectronicMailAddresses(user.ElectronicMailAddresses);

            EduPassUserExtension extension = user.EduPassExtension;
            if (null == extension)
            {
                if (requireUinFin)
                {
                    throw EduPassValidator.Invalid(
                        SR.MissingExtension(EduPassSchemaIdentifiers.UserExtension));
                }

                return;
            }

            EduPassValidator.RequireOneOf(
                extension.IdentityType,
                EduPassValues.IdentityTypes,
                EduPassAttributeNames.IdentityType);

            EduPassValidator.RequireOneOf(
                extension.SchoolOrHq,
                EduPassValues.SchoolOrHq,
                EduPassAttributeNames.SchoolOrHq);

            EduPassValidator.RequireOneOf(
                extension.IdentitySource,
                EduPassValues.IdentitySources,
                EduPassAttributeNames.IdentitySource);

            if (requireUinFin && string.IsNullOrWhiteSpace(extension.UinFin))
            {
                // Non-human identities never have one, so absence is only an error for a party
                // that stores UIN/FIN and is told the identity is human.
                bool nonHuman =
                    string.Equals(extension.IdentityType, "Non-human", StringComparison.OrdinalIgnoreCase);

                if (!nonHuman)
                {
                    throw EduPassValidator.Invalid(SR.Missing(EduPassAttributeNames.UinFin));
                }
            }

            if (!string.IsNullOrWhiteSpace(extension.UinFin)
                && !EduPassValidator.UinFinExpression.IsMatch(extension.UinFin))
            {
                throw EduPassValidator.Invalid(SR.Malformed(EduPassAttributeNames.UinFin));
            }
        }

        /// <summary>
        /// Throws if <paramref name="group"/> violates the specification. The
        /// <c>displayName</c> encodes an application role, so it is bound by the same
        /// 256-character limit.
        /// </summary>
        public static void Validate(Core2Group group)
        {
            if (null == group)
            {
                throw new ArgumentNullException(nameof(group));
            }

            EduPassValidator.RequirePresent(group.DisplayName, AttributeNames.DisplayName);
            EduPassValidator.RequireLength(group.DisplayName, AttributeNames.DisplayName);
            EduPassValidator.RequireLength(group.ExternalIdentifier, AttributeNames.ExternalIdentifier);
        }

        private static void ValidateElectronicMailAddresses(
            IEnumerable<ElectronicMailAddress> electronicMailAddresses)
        {
            if (null == electronicMailAddresses)
            {
                return;
            }

            ElectronicMailAddress[] addresses = electronicMailAddresses.ToArray();

            foreach (ElectronicMailAddress address in addresses)
            {
                EduPassValidator.RequireLength(address.Value, AttributeNames.ElectronicMailAddresses);

                EduPassValidator.RequireOneOf(
                    address.ItemType,
                    EduPassValues.ElectronicMailAddressTypes,
                    "emails.type");
            }

            int primaryCount = addresses.Count(address => address.Primary);

            // The primary address is the notification email; more than one is ambiguous, and
            // RFC 7643 section 2.4 forbids it outright.
            if (primaryCount > 1)
            {
                throw EduPassValidator.Invalid(SR.MultiplePrimary());
            }
        }

        private static void RequirePresent(string value, string attributeName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw EduPassValidator.Invalid(SR.Missing(attributeName));
            }
        }

        private static void RequireLength(string value, string attributeName)
        {
            if (null != value && value.Length > EduPassValidator.MaximumAttributeLength)
            {
                throw EduPassValidator.Invalid(
                    SR.TooLong(attributeName, EduPassValidator.MaximumAttributeLength));
            }
        }

        private static void RequireOneOf(string value, IEnumerable<string> permitted, string attributeName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (!permitted.Contains(value, StringComparer.Ordinal))
            {
                throw EduPassValidator.Invalid(SR.NotPermitted(attributeName, permitted));
            }
        }

        private static ScimTypedException Invalid(string detail)
        {
            return new ScimTypedException(HttpStatusCode.BadRequest, ScimTypes.InvalidValue, detail);
        }

        /// <summary>Detail messages. Kept together so the wording stays consistent.</summary>
        private static class SR
        {
            public static string Missing(string attributeName)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "The required attribute '{0}' is missing or empty.",
                    attributeName);
            }

            public static string MissingExtension(string schemaIdentifier)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "The required schema extension '{0}' is missing.",
                    schemaIdentifier);
            }

            public static string Malformed(string attributeName)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "The attribute '{0}' does not match its required format.",
                    attributeName);
            }

            public static string TooLong(string attributeName, int maximum)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "The attribute '{0}' exceeds the maximum length of {1} characters.",
                    attributeName,
                    maximum);
            }

            public static string NotPermitted(string attributeName, IEnumerable<string> permitted)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "The attribute '{0}' must be one of: {1}.",
                    attributeName,
                    string.Join(", ", permitted));
            }

            public static string MultiplePrimary()
            {
                return "At most one email address may be marked primary.";
            }
        }
    }
}
