// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    /// <summary>
    /// Host-wide switches a consumer sets once at startup, in Startup.cs or Program.cs.
    /// </summary>
    public static class ScimServiceOptions
    {
        /// <summary>
        /// Whether a successful PATCH of a Group answers 200 with the updated resource
        /// rather than 204 No Content.
        /// </summary>
        /// <remarks>
        /// RFC 7644 section 3.5.2 permits either form, so this is a deployment's contract
        /// with its clients rather than a conformance question. It defaults to false, which
        /// is what the EduPass/FIMS interface spec requires ("Status 204: PATCH applied").
        /// A PATCH carrying "attributes" or "excludedAttributes" answers 200 either way,
        /// which that section requires outright.
        /// </remarks>
        public static bool GroupPatchReturnsResource { get; set; }
    }
}
