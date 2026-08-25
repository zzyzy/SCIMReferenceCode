//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM
{
    using System;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;

    public static class ProtocolConstants
    {
        public const string ContentType = "application/scim+json";
        public const string PathGroups = "Groups";
        public const string PathUsers = "Users";
        public const string PathBulk = "Bulk";
        public const string PathWebBatchInterface = SchemaConstants.PathInterface + "/batch";

        public readonly static Lazy<JsonSerializerSettings> JsonSettings =
            new Lazy<JsonSerializerSettings>(() => ProtocolConstants.InitializeSettings());

        private static JsonSerializerSettings InitializeSettings()
        {
            JsonSerializerSettings result = new JsonSerializerSettings();

            // SCIM's $ref collides with Newtonsoft's reference metadata: an object whose first
            // property is $ref is read as a pointer to an earlier $id, fails, and - because the
            // handler below swallows every error - is dropped silently. That is how a PATCH
            // adding a member exactly as the Edupass specification writes it returned 204 and
            // changed nothing.
            result.MetadataPropertyHandling = MetadataPropertyHandling.Ignore;

            result.Error = delegate (object sender, ErrorEventArgs args) { args.ErrorContext.Handled = true; };
            return result;
        }
    }
}