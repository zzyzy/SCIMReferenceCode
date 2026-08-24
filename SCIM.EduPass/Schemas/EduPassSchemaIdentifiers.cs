// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Scim.EduPass
{
    /// <summary>
    /// The Edupass schema URIs and attribute names.
    /// </summary>
    public static class EduPassSchemaIdentifiers
    {
        /// <summary>
        /// The Edupass User extension, as named in the Edupass SCIM interface specification.
        /// </summary>
        public const string UserExtension = "urn:ietf:params:scim:schemas:extension:Edupass:2.0:User";
    }

    /// <summary>The attribute names in the Edupass User extension.</summary>
    public static class EduPassAttributeNames
    {
        public const string IdentityType = "identityType";
        public const string UinFin = "uinFin";
        public const string SchoolOrHq = "schoolOrHq";
        public const string IdentitySource = "identitySource";
    }

    /// <summary>
    /// The permitted values of the Edupass extension attributes and of the core
    /// <c>emails[].type</c>.
    /// </summary>
    /// <remarks>
    /// Edupass specifies these as closed sets. The core library treats <c>emails[].type</c> as a
    /// free string, correctly - RFC 7643 canonical values are advisory - so the constraint is
    /// enforced here instead.
    /// </remarks>
    public static class EduPassValues
    {
        /// <summary>Values of the <c>identityType</c> attribute.</summary>
        public static readonly string[] IdentityTypes =
            new[] { "Non-human", "Student", "Staff", "Temp", "Intern", "Vendor", "Others" };

        /// <summary>Values of the <c>schoolOrHq</c> attribute.</summary>
        public static readonly string[] SchoolOrHq = new[] { "School", "HQ" };

        /// <summary>Values of the <c>identitySource</c> attribute.</summary>
        public static readonly string[] IdentitySources = new[] { "HRPS", "SC", "MIMS" };

        /// <summary>Values of the core <c>emails[].type</c> sub-attribute.</summary>
        public static readonly string[] ElectronicMailAddressTypes =
            new[] { "WOG", "CES", "ICON", "OTHER" };
    }
}
