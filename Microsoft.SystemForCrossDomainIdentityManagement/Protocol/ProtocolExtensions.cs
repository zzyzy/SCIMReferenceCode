//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Formatting;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using System.Web;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling", Justification = "None")]
    public static class ProtocolExtensions
    {
        private const string BulkIdentifierPattern =
            @"^((\s*)bulkId(\s*):(\s*)(?<" +
            ProtocolExtensions.ExpressionGroupNameBulkIdentifier +
            @">[^\s]*))";

        private const string ExpressionGroupNameBulkIdentifier = "identifier";

        // Bounds the recursion that expands a path-less operation's value. A complex
        // attribute nests one level and a schema extension's complex attribute two, so this
        // leaves headroom while keeping a hostile body from recursing without end.
        private const int MaximumExpansionDepth = 8;
        private const string SchemaIdentifierPrefix = "urn:";
        private const string SeparatorSubAttribute = ".";
        public const string MethodNameDelete = "DELETE";
        public const string MethodNamePatch = "PATCH";
        private static readonly Lazy<HttpMethod> MethodPatch =
            new Lazy<HttpMethod>(
                () =>
                    new HttpMethod(ProtocolExtensions.MethodNamePatch));
        private static readonly Lazy<Regex> BulkIdentifierExpression =
            new Lazy<Regex>(
                () =>
                    new Regex(ProtocolExtensions.BulkIdentifierPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled));
        private interface IHttpRequestMessageWriter : IDisposable
        {
            void Close();
            Task FlushAsync();
            Task WriteAsync();
        }

        public static HttpMethod PatchMethod
        {
            get
            {
                return ProtocolExtensions.MethodPatch.Value;
            }
        }

        /// <summary>
        /// Reads the raw <c>value</c> of a combined patch operation into operation values.
        /// </summary>
        /// <remarks>
        /// Three shapes reach this: an array of complex values, a single complex value, and a
        /// bare scalar. Only the first and last were handled, and a single complex value - which
        /// is exactly what a replace of a complex attribute such as <c>manager</c> carries -
        /// fell through to the scalar branch, failed to deserialize, and produced a value whose
        /// own value was null. The attribute was then emptied rather than set.
        /// </remarks>
        internal static void ReadValues(PatchOperation2 operation, string rawValue)
        {
            if (null == operation || null == rawValue)
            {
                return;
            }

            OperationValue[] values =
                JsonConvert.DeserializeObject<OperationValue[]>(
                    rawValue,
                    ProtocolConstants.JsonSettings.Value);

            if (null != values)
            {
                foreach (OperationValue value in values)
                {
                    operation.AddValue(value);
                }

                return;
            }

            OperationValue single =
                JsonConvert.DeserializeObject<OperationValue>(
                    rawValue,
                    ProtocolConstants.JsonSettings.Value);

            if (null != single && (null != single.Value || null != single.Reference))
            {
                operation.AddValue(single);
                return;
            }

            string scalar =
                JsonConvert.DeserializeObject<string>(
                    rawValue,
                    ProtocolConstants.JsonSettings.Value);

            operation.AddValue(
                new OperationValue()
                {
                    Value = scalar
                });
        }

        /// <summary>
        /// Turns one operation as it arrived into the operations that are to be applied.
        /// </summary>
        /// <remarks>
        /// RFC 7644 sections 3.5.2.1 and 3.5.2.3: an add or a replace that carries no path
        /// targets the resource itself, and its value is then a set of attributes to apply.
        /// Each member of that set is the operation the client would have sent had it named
        /// the attribute in a path, so it is expanded into exactly that and applied by the
        /// same code. Without this the appliers, which all begin by requiring a path, returned
        /// on the first line - so a path-less operation answered success and changed nothing.
        ///
        /// A member whose own value is an object is expanded again, because a complex
        /// attribute is patched a sub-attribute at a time. The separator differs by what the
        /// parent names: a schema extension qualifies its attributes with a colon, a complex
        /// attribute with a period.
        /// </remarks>
        internal static IEnumerable<PatchOperation2> Expand(PatchOperation2Combined operation)
        {
            if (null == operation)
            {
                return Enumerable.Empty<PatchOperation2>();
            }

            if (null != operation.Path && !string.IsNullOrWhiteSpace(operation.Path.AttributePath))
            {
                PatchOperation2 single =
                    new PatchOperation2()
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
                ProtocolExtensions.ReadValues(single, operation.Value);

                return new[] { single };
            }

            // Only add and replace are defined without a path. A remove without one names
            // nothing to remove, and is left alone here rather than guessed at.
            if (OperationName.Add != operation.Name && OperationName.Replace != operation.Name)
            {
                return Enumerable.Empty<PatchOperation2>();
            }

            JObject attributes = ProtocolExtensions.ReadAttributes(operation.Value);

            if (null == attributes)
            {
                return Enumerable.Empty<PatchOperation2>();
            }

            List<PatchOperation2> result = new List<PatchOperation2>();
            ProtocolExtensions.Expand(attributes, null, operation.OperationName, ProtocolExtensions.MaximumExpansionDepth, result);
            return result;
        }

        private static JObject ReadAttributes(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject(rawValue) as JObject;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static void Expand(
            JObject attributes,
            string prefix,
            string operationName,
            int depth,
            IList<PatchOperation2> result)
        {
            foreach (JProperty attribute in attributes.Properties())
            {
                if (string.IsNullOrWhiteSpace(attribute.Name))
                {
                    continue;
                }

                string path = ProtocolExtensions.Qualify(prefix, attribute.Name);

                if (depth > 1 && attribute.Value is JObject complex && complex.HasValues)
                {
                    ProtocolExtensions.Expand(complex, path, operationName, depth - 1, result);
                    continue;
                }

                PatchOperation2 expanded =
                    new PatchOperation2()
                    {
                        OperationName = operationName,
                        Path = Path.Create(path)
                    };

                ProtocolExtensions.ReadValues(expanded, attribute.Value.ToString(Formatting.None));

                result.Add(expanded);
            }
        }

        /// <summary>
        /// Names a member of <paramref name="prefix"/> the way a path names it.
        /// </summary>
        private static string Qualify(string prefix, string name)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return name;
            }

            // "urn:...:User" + "department" is one attribute path, not a sub-attribute of a
            // complex one, so it takes the schema separator rather than the period.
            return
                prefix.StartsWith(ProtocolExtensions.SchemaIdentifierPrefix, StringComparison.OrdinalIgnoreCase)
                    ? string.Concat(prefix, SchemaConstants.SeparatorSchemaIdentifierAttribute, name)
                    : string.Concat(prefix, ProtocolExtensions.SeparatorSubAttribute, name);
        }

        public static void Apply(this Core2Group group, PatchRequest2 patch)
        {
            if (null == group)
            {
                throw new ArgumentNullException(nameof(group));
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
                    group.Apply(operationInternal);
                }
            }
        }

        /// <summary>
        /// Applies <c>members[&lt;selector&gt; eq &lt;comparison&gt;].&lt;subAttribute&gt;</c> to one
        /// membership entry, adding the entry when no member satisfies the filter.
        /// </summary>
        /// <remarks>
        /// RFC 7644 section 3.5.2.3: a replace whose target does not exist is treated as an
        /// add, so a group that holds no member of the named type gains one rather than being
        /// left alone.
        /// </remarks>
        private static IEnumerable<Member> PatchMember(IEnumerable<Member> members, PatchOperation2 operation)
        {
            IFilter subAttribute = operation.Path.SubAttributes?.SingleOrDefault();

            if (null == subAttribute)
            {
                return members;
            }

            if (operation.Value != null && operation.Value.Count > 1)
            {
                return members;
            }

            string selector = subAttribute.AttributePath;
            string patched = operation.Path.ValuePath.AttributePath;

            bool Matches(Member item) =>
                ProtocolExtensions.MemberMatches(item, selector, subAttribute.ComparisonValue);

            Member existing = members?.SingleOrDefault((Member item) => Matches(item));

            Member member = existing ?? ProtocolExtensions.CreateMember(selector, subAttribute.ComparisonValue);

            if (null == member)
            {
                return members;
            }

            if (!ProtocolExtensions.TryReadMember(member, patched, out string current))
            {
                return members;
            }

            string resolved =
                ProtocolExtensions.ResolveValue(
                    operation,
                    operation.Value?.FirstOrDefault()?.Value,
                    current);

            if (!ProtocolExtensions.TryWriteMember(member, patched, resolved))
            {
                return members;
            }

            // A membership with no value identifies nobody, so emptying it removes the entry.
            if (string.IsNullOrWhiteSpace(member.Value))
            {
                return
                    null == existing
                        ? members
                        : members.Where((Member item) => !Matches(item)).ToArray();
            }

            if (existing != null)
            {
                return members;
            }

            Member[] added = new Member[] { member };

            return null == members ? added : members.Concat(added).ToArray();
        }

        private static bool MemberMatches(Member member, string selector, string comparison)
        {
            return
                ProtocolExtensions.TryReadMember(member, selector, out string held)
                && string.Equals(comparison, held, StringComparison.Ordinal);
        }

        private static Member CreateMember(string selector, string comparison)
        {
            Member member = new Member();

            return ProtocolExtensions.TryWriteMember(member, selector, comparison) ? member : null;
        }

        private static bool TryReadMember(Member member, string subAttribute, out string value)
        {
            value = null;

            if (null == member)
            {
                return false;
            }

            if (string.Equals(subAttribute, Microsoft.SCIM.AttributeNames.Value, StringComparison.OrdinalIgnoreCase))
            {
                value = member.Value;
                return true;
            }

            if (string.Equals(subAttribute, Microsoft.SCIM.AttributeNames.Type, StringComparison.OrdinalIgnoreCase))
            {
                value = member.TypeName;
                return true;
            }

            if (string.Equals(subAttribute, Microsoft.SCIM.AttributeNames.Display, StringComparison.OrdinalIgnoreCase))
            {
                value = member.Display;
                return true;
            }

            if (string.Equals(subAttribute, Microsoft.SCIM.AttributeNames.Reference, StringComparison.OrdinalIgnoreCase))
            {
                value = member.Reference;
                return true;
            }

            return false;
        }

        private static bool TryWriteMember(Member member, string subAttribute, string value)
        {
            if (string.Equals(subAttribute, Microsoft.SCIM.AttributeNames.Value, StringComparison.OrdinalIgnoreCase))
            {
                member.Value = value;
                return true;
            }

            if (string.Equals(subAttribute, Microsoft.SCIM.AttributeNames.Type, StringComparison.OrdinalIgnoreCase))
            {
                member.TypeName = value;
                return true;
            }

            if (string.Equals(subAttribute, Microsoft.SCIM.AttributeNames.Display, StringComparison.OrdinalIgnoreCase))
            {
                member.Display = value;
                return true;
            }

            if (string.Equals(subAttribute, Microsoft.SCIM.AttributeNames.Reference, StringComparison.OrdinalIgnoreCase))
            {
                member.Reference = value;
                return true;
            }

            return false;
        }

        private static void Apply(this Core2Group group, PatchOperation2 operation)
        {
            if (null == operation || null == operation.Path || string.IsNullOrWhiteSpace(operation.Path.AttributePath))
            {
                return;
            }

            OperationValue value;
            switch (operation.Path.AttributePath)
            {
                case AttributeNames.DisplayName:
                    value = operation.Value.SingleOrDefault();

                    if (OperationName.Remove == operation.Name)
                    {
                        if (
                            null == value
                            || string.Equals(group.DisplayName, value.Value, StringComparison.OrdinalIgnoreCase))
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
                        group.DisplayName = null;
                    }
                    else
                    {
                        group.DisplayName = value.Value;
                    }
                    break;

                case AttributeNames.ExternalIdentifier:
                    value = operation.Value.SingleOrDefault();

                    if (OperationName.Remove == operation.Name)
                    {
                        if (
                            null == value
                            || string.Equals(group.ExternalIdentifier, value.Value, StringComparison.OrdinalIgnoreCase))
                        {
                            value = null;
                        }
                        else
                        {
                            break;
                        }
                    }

                    group.ExternalIdentifier = null == value ? null : value.Value;
                    break;

                case AttributeNames.Members:
                    // A value path names a sub-attribute of one entry -
                    // members[type eq "Group"].value - rather than the membership as a whole.
                    // Falling through to the cases below took the operation for a full sync
                    // and replaced every member with the single value it carried.
                    if (null != operation.Path.ValuePath
                        && !string.IsNullOrWhiteSpace(operation.Path.ValuePath.AttributePath))
                    {
                        group.Members = ProtocolExtensions.PatchMember(group.Members, operation);
                        break;
                    }

                    if (operation.Value != null)
                    {
                        switch (operation.Name)
                        {
                            // A full membership sync. Without this the operation fell through the
                            // switch, leaving the membership untouched while the service still
                            // answered 204.
                            case OperationName.Replace:
                                group.Members =
                                    operation
                                    .Value
                                    .Where((OperationValue item) => !string.IsNullOrWhiteSpace(item.Value))
                                    .Select(ProtocolExtensions.ToMember)
                                    .GroupBy((Member item) => item.Value, StringComparer.OrdinalIgnoreCase)
                                    .Select((IGrouping<string, Member> item) => item.First())
                                    .ToArray();
                                break;

                            case OperationName.Add:
                                IEnumerable<Member> membersToAdd =
                                     operation
                                     .Value
                                     .Select(ProtocolExtensions.ToMember)
                                     .ToArray();

                                IList<Member> buffer = new List<Member>();
                                if(null == group.Members)
                                {
                                    group.Members = new List<Member>();
                                }
                                foreach (Member member in membersToAdd)
                                {
                                    //O(n) with the number of group members, so for large groups this is not optimal
                                    if (!group.Members.Any((Member item) =>
                                            string.Equals(item.Value, member.Value, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        buffer.Add(member);
                                    }
                                }

                                group.Members = group.Members.Concat(buffer.ToArray());
                                break;

                            case OperationName.Remove:
                                if (null == group.Members)
                                {
                                    break;
                                }

                                if (operation?.Value?.FirstOrDefault()?.Value == null)
                                {
                                    group.Members = Enumerable.Empty<Member>();
                                    break;
                                }

                                // Ordinal-ignore-case, because adding is case-insensitive and
                                // so is every membership lookup. A case-sensitive removal
                                // means a member can be added and then never removed by a
                                // client whose identifier came back in a different case.
                                IDictionary<string, Member> members =
                                    new Dictionary<string, Member>(
                                        group.Members.Count(),
                                        StringComparer.OrdinalIgnoreCase);
                                foreach (Member item in group.Members)
                                {
                                    members[item.Value] = item;
                                }

                                foreach (OperationValue operationValue in operation.Value)
                                {
                                    if (members.TryGetValue(operationValue.Value, out Member removedMember))
                                    {
                                        members.Remove(operationValue.Value);
                                    }
                                }

                                group.Members = members.Values;
                                break;
                        }
                    }
                    break;

                default:
                    // RFC 7644 section 3.5.2: a path naming no operable attribute is an error.
                    // Ignoring it answered 204 while changing nothing, and made the required
                    // atomicity unenforceable - a malformed operation could never fail its request.
                    throw new ArgumentException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            SystemForCrossDomainIdentityManagementProtocolResources.ExceptionInvalidPathTemplate,
                            operation.Path));
            }
        }

        /// <summary>
        /// Carries a patched membership entry across whole, not just its <c>value</c>.
        /// </summary>
        /// <remarks>
        /// RFC 7643 section 4.2 gives <c>members</c> the <c>$ref</c>, <c>display</c> and
        /// <c>type</c> sub-attributes. Rebuilding the entry from <c>value</c> alone
        /// discarded whatever else the client sent while still answering success, so a
        /// group read back after a write was missing references the client had supplied.
        /// </remarks>
        private static Member ToMember(OperationValue value)
        {
            return
                new Member()
                {
                    Value = value.Value,
                    Reference = value.Reference,
                    Display = value.Display,
                    TypeName = value.TypeName,
                };
        }

        public static HttpRequestMessage ComposeDeleteRequest(this Resource resource, Uri baseResourceIdentifier)
        {
            if (null == baseResourceIdentifier)
            {
                throw new ArgumentNullException(nameof(baseResourceIdentifier));
            }

            Uri resourceIdentifier = resource.GetResourceIdentifier(baseResourceIdentifier);

            HttpRequestMessage result = null;
            try
            {
                result = new HttpRequestMessage(HttpMethod.Delete, resourceIdentifier);
                return result;
            }
            catch
            {
                if (result != null)
                {
                    result.Dispose();
#pragma warning disable IDE0059 // Unnecessary assignment of a value
                    result = null;
#pragma warning restore IDE0059 // Unnecessary assignment of a value
                }

                throw;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0", Justification = "False analysis of 'this' parameter of an extension method")]
        public static HttpRequestMessage ComposeGetRequest(
            this Schematized schematized,
            Uri baseResourceIdentifier,
            IReadOnlyCollection<IFilter> filters,
            IReadOnlyCollection<string> requestedAttributePaths,
            IReadOnlyCollection<string> excludedAttributePaths,
            IPaginationParameters paginationParameters)
        {
            if (null == baseResourceIdentifier)
            {
                throw new ArgumentNullException(nameof(baseResourceIdentifier));
            }

            if (null == filters)
            {
                throw new ArgumentNullException(nameof(filters));
            }

            if (null == requestedAttributePaths)
            {
                throw new ArgumentNullException(nameof(requestedAttributePaths));
            }

            if (null == excludedAttributePaths)
            {
                throw new ArgumentNullException(nameof(excludedAttributePaths));
            }

            Uri resourceIdentifier =
                schematized.ComposeResourceIdentifier(
                    baseResourceIdentifier,
                    filters,
                    requestedAttributePaths,
                    excludedAttributePaths,
                    paginationParameters);
            HttpRequestMessage result = null;
            try
            {
                result = new HttpRequestMessage(HttpMethod.Get, resourceIdentifier);
                return result;
            }
            catch
            {
                if (result != null)
                {
                    result.Dispose();
#pragma warning disable IDE0059 // Unnecessary assignment of a value
                    result = null;
#pragma warning restore IDE0059 // Unnecessary assignment of a value
                }

                throw;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0", Justification = "False analysis of 'this' parameter of an extension method")]
        public static HttpRequestMessage ComposeGetRequest(
            this Schematized schematized,
            Uri baseResourceIdentifier,
            IReadOnlyCollection<IFilter> filters,
            IReadOnlyCollection<string> requestedAttributePaths,
            IReadOnlyCollection<string> excludedAttributePaths)
        {
            HttpRequestMessage result = null;
            try
            {
                result =
                    schematized
                    .ComposeGetRequest(
                        baseResourceIdentifier,
                        filters,
                        requestedAttributePaths,
                        excludedAttributePaths,
                        null);
                return result;
            }
            catch
            {
                if (result != null)
                {
                    result.Dispose();
#pragma warning disable IDE0059 // Unnecessary assignment of a value
                    result = null;
#pragma warning restore IDE0059 // Unnecessary assignment of a value
                }

                throw;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0", Justification = "False analysis of 'this' parameter of an extension method")]
        public static HttpRequestMessage ComposeGetRequest(
            this Resource resource,
            Uri baseResourceIdentifier,
            IReadOnlyCollection<string> requestedAttributePaths,
            IReadOnlyCollection<string> excludedAttributePaths)
        {
            if (null == baseResourceIdentifier)
            {
                throw new ArgumentNullException(nameof(baseResourceIdentifier));
            }

            if (null == requestedAttributePaths)
            {
                throw new ArgumentNullException(nameof(requestedAttributePaths));
            }

            if (null == excludedAttributePaths)
            {
                throw new ArgumentNullException(nameof(excludedAttributePaths));
            }

            Uri resourceIdentifier =
                resource.ComposeResourceIdentifier(
                    baseResourceIdentifier,
                    requestedAttributePaths,
                    excludedAttributePaths);
            HttpRequestMessage result = null;
            try
            {
                result = new HttpRequestMessage(HttpMethod.Get, resourceIdentifier);
                return result;
            }
            catch
            {
                if (result != null)
                {
                    result.Dispose();
#pragma warning disable IDE0059 // Unnecessary assignment of a value
                    result = null;
#pragma warning restore IDE0059 // Unnecessary assignment of a value
                }

                throw;
            }
        }

        public static HttpRequestMessage ComposeGetRequest(this Resource resource, Uri baseResourceIdentifier)
        {
            if (null == baseResourceIdentifier)
            {
                throw new ArgumentNullException(nameof(baseResourceIdentifier));
            }

            HttpRequestMessage result = null;
            try
            {
                IReadOnlyCollection<string> requestedAttributePaths = Array.Empty<string>();
                IReadOnlyCollection<string> excludedAttributePaths = Array.Empty<string>();
                result = resource.ComposeGetRequest(baseResourceIdentifier, requestedAttributePaths, excludedAttributePaths);
                return result;
            }
            catch
            {
                if (result != null)
                {
                    result.Dispose();
#pragma warning disable IDE0059 // Unnecessary assignment of a value
                    result = null;
#pragma warning restore IDE0059 // Unnecessary assignment of a value
                }

                throw;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters", Justification = "The parameter must be a patch for the operation to produce a semantically valid result")]
        public static HttpRequestMessage ComposePatchRequest(
            this Resource resource,
            Uri baseResourceIdentifier,
            PatchRequestBase patch)
        {
            if (null == baseResourceIdentifier)
            {
                throw new ArgumentNullException(nameof(baseResourceIdentifier));
            }

            if (null == patch)
            {
                throw new ArgumentNullException(nameof(patch));
            }

            Dictionary<string, object> json = patch.ToJson();

            Uri resourceIdentifier = resource.GetResourceIdentifier(baseResourceIdentifier);

            HttpRequestMessage result = null;
            try
            {
                HttpContent requestContent = null;
                try
                {
                    string contentType = MediaTypes.Protocol;

                    MediaTypeFormatter contentFormatter = new JsonMediaTypeFormatter();
                    requestContent =
                        new ObjectContent<Dictionary<string, object>>(
                            json,
                            contentFormatter,
                            contentType);
                    result = new HttpRequestMessage(ProtocolExtensions.PatchMethod, resourceIdentifier);
                    result.Content = requestContent;
                    requestContent = null;
                    return result;
                }
                finally
                {
                    if (requestContent != null)
                    {
                        requestContent.Dispose();
                        requestContent = null;
                    }
                }
            }
            catch
            {
                if (result != null)
                {
                    result.Dispose();
#pragma warning disable IDE0059 // Unnecessary assignment of a value
                    result = null;
#pragma warning restore IDE0059 // Unnecessary assignment of a value
                }

                throw;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0", Justification = "False analysis of 'this' parameter of an extension method")]
        public static HttpRequestMessage ComposePatchRequest(
            this Resource patch,
            Uri baseResourceIdentifier)
        {
            if (null == baseResourceIdentifier)
            {
                throw new ArgumentNullException(nameof(baseResourceIdentifier));
            }

            Dictionary<string, object> json = patch.ToJson();
            json.Trim();

            Uri resourceIdentifier = patch.GetResourceIdentifier(baseResourceIdentifier);

            HttpRequestMessage result = null;
            try
            {
                HttpContent requestContent = null;
                try
                {
                    MediaTypeFormatter contentFormatter = new JsonMediaTypeFormatter();
                    requestContent =
                        new ObjectContent<Dictionary<string, object>>(
                            json,
                            contentFormatter,
                            MediaTypes.Json);
                    result = new HttpRequestMessage(ProtocolExtensions.PatchMethod, resourceIdentifier);
                    result.Content = requestContent;
                    requestContent = null;
                    return result;
                }
                finally
                {
                    if (requestContent != null)
                    {
                        requestContent.Dispose();
                        requestContent = null;
                    }
                }
            }
            catch
            {
                if (result != null)
                {
                    result.Dispose();
#pragma warning disable IDE0059 // Unnecessary assignment of a value
                    result = null;
#pragma warning restore IDE0059 // Unnecessary assignment of a value
                }

                throw;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0", Justification = "False analysis of extension method")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters", Justification = "Performing the operation on the base type would be invalid")]
        public static HttpRequestMessage ComposePutRequest(this Resource resource, Uri baseResourceIdentifier)
        {
            if (null == baseResourceIdentifier)
            {
                throw new ArgumentNullException(nameof(baseResourceIdentifier));
            }

            string contentType = MediaTypes.Protocol;

            Dictionary<string, object> json = resource.ToJson();
            json.Trim();

            Uri resourceIdentifier = resource.GetResourceIdentifier(baseResourceIdentifier);

            HttpRequestMessage result = null;
            try
            {
                HttpContent requestContent = null;
                try
                {
                    MediaTypeFormatter contentFormatter = new JsonMediaTypeFormatter();
                    requestContent =
                        new ObjectContent<Dictionary<string, object>>(
                            json,
                            contentFormatter,
                            contentType);
                    result = new HttpRequestMessage(HttpMethod.Put, resourceIdentifier);
                    result.Content = requestContent;
                    requestContent = null;
                    return result;
                }
                finally
                {
                    if (requestContent != null)
                    {
                        requestContent.Dispose();
                        requestContent = null;
                    }
                }
            }
            catch
            {
                if (result != null)
                {
                    result.Dispose();
#pragma warning disable IDE0059 // Unnecessary assignment of a value
                    result = null;
#pragma warning restore IDE0059 // Unnecessary assignment of a value
                }

                throw;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0", Justification = "False analysis of extension method")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters", Justification = "Performing the operation on the base type would be invalid")]
        public static HttpRequestMessage ComposePostRequest(this Resource resource, Uri baseResourceIdentifier)
        {
            if (null == baseResourceIdentifier)
            {
                throw new ArgumentNullException(nameof(baseResourceIdentifier));
            }

            string contentType = MediaTypes.Protocol;

            Dictionary<string, object> json = resource.ToJson();
            json.Trim();

            Uri typeResourceIdentifier = resource.GetTypeIdentifier(baseResourceIdentifier);

            HttpRequestMessage result = null;
            try
            {
                HttpContent requestContent = null;
                try
                {
                    MediaTypeFormatter contentFormatter = new JsonMediaTypeFormatter();
                    requestContent =
                        new ObjectContent<Dictionary<string, object>>(
                            json,
                            contentFormatter,
                            contentType);
                    result = new HttpRequestMessage(HttpMethod.Post, typeResourceIdentifier);
                    result.Content = requestContent;
                    requestContent = null;
                    return result;
                }
                finally
                {
                    if (requestContent != null)
                    {
                        requestContent.Dispose();
                        requestContent = null;
                    }
                }
            }
            catch
            {
                if (result != null)
                {
                    result.Dispose();
#pragma warning disable IDE0059 // Unnecessary assignment of a value
                    result = null;
#pragma warning restore IDE0059 // Unnecessary assignment of a value
                }

                throw;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0", Justification = "False analysis of 'this' parameter of an extension method")]
        public static UriBuilder ComposeResourceIdentifier(this Resource resource, Uri baseResourceIdentifier)
        {
            if (null == baseResourceIdentifier)
            {
                throw new ArgumentNullException(nameof(baseResourceIdentifier));
            }

            if (string.IsNullOrWhiteSpace(resource.Identifier))
            {
                throw new InvalidOperationException(SystemForCrossDomainIdentityManagementProtocolResources.ExceptionInvalidResource);
            }

            Uri foundation = resource.GetResourceIdentifier(baseResourceIdentifier);
            UriBuilder result = new UriBuilder(foundation);
            return result;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0", Justification = "False analysis of 'this' parameter of an extension method")]
        public static Uri ComposeResourceIdentifier(
            this Resource resource,
            Uri baseResourceIdentifier,
            IReadOnlyCollection<string> requestedAttributePaths,
            IReadOnlyCollection<string> excludedAttributePaths)
        {
            if (null == baseResourceIdentifier)
            {
                throw new ArgumentNullException(nameof(baseResourceIdentifier));
            }

            if (null == requestedAttributePaths)
            {
                throw new ArgumentNullException(nameof(requestedAttributePaths));
            }

            if (null == excludedAttributePaths)
            {
                throw new ArgumentNullException(nameof(excludedAttributePaths));
            }

            if (!resource.TryGetSchemaIdentifier(out string schemaIdentifier))
            {
                schemaIdentifier = resource.GetSchemaIdentifier();
            }

            if (!resource.TryGetPath(out string path))
            {
                path = resource.GetPath();
            }

            IResourceRetrievalParameters retrievalParameters =
                new ResourceRetrievalParameters(
                    schemaIdentifier,
                    path,
                    resource.Identifier,
                    requestedAttributePaths,
                    excludedAttributePaths);
            string query = retrievalParameters.ToString();
            UriBuilder resourceIdentifier = resource.ComposeResourceIdentifier(baseResourceIdentifier);
            resourceIdentifier.Query = query;
            Uri result = resourceIdentifier.Uri;
            return result;
        }

        public static Uri ComposeResourceIdentifier(
            this Schematized schematized,
            Uri baseResourceIdentifier,
            IQueryParameters parameters)
        {
            if (null == baseResourceIdentifier)
            {
                throw new ArgumentNullException(nameof(baseResourceIdentifier));
            }

            if (null == parameters)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            Uri typeIdentifier = schematized.GetTypeIdentifier(baseResourceIdentifier);
            UriBuilder resourceIdentifier = new UriBuilder(typeIdentifier);
            resourceIdentifier.Query = parameters.ToString();
            Uri result = resourceIdentifier.Uri;
            return result;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0", Justification = "False analysis of an extension method")]
        public static Uri ComposeResourceIdentifier(
            this Schematized schematized,
            Uri baseResourceIdentifier,
            IReadOnlyCollection<IFilter> filters,
            IReadOnlyCollection<string> requestedAttributePaths,
            IReadOnlyCollection<string> excludedAttributePaths,
            IPaginationParameters paginationParameters)
        {
            if (null == baseResourceIdentifier)
            {
                throw new ArgumentNullException(nameof(baseResourceIdentifier));
            }

            if (null == filters)
            {
                throw new ArgumentNullException(nameof(filters));
            }

            if (null == requestedAttributePaths)
            {
                throw new ArgumentNullException(nameof(requestedAttributePaths));
            }

            if (null == excludedAttributePaths)
            {
                throw new ArgumentNullException(nameof(excludedAttributePaths));
            }

            if (!schematized.TryGetSchemaIdentifier(out string schemaIdentifier))
            {
                schemaIdentifier = schematized.GetSchemaIdentifier();
            }

            if (!schematized.TryGetPath(out string path))
            {
                path = schematized.GetPath();
            }

            IQueryParameters queryParameters =
                new QueryParameters(
                    schemaIdentifier,
                    path,
                    filters,
                    requestedAttributePaths,
                    excludedAttributePaths);
            queryParameters.PaginationParameters = paginationParameters;
            Uri result = schematized.ComposeResourceIdentifier(baseResourceIdentifier, queryParameters);
            return result;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0", Justification = "False analysis of an extension method")]
        public static Uri ComposeResourceIdentifier(
            this Schematized schematized,
            Uri baseResourceIdentifier,
            IReadOnlyCollection<IFilter> filters,
            IReadOnlyCollection<string> requestedAttributePaths,
            IReadOnlyCollection<string> excludedAttributePaths)
        {
            Uri result =
                schematized.ComposeResourceIdentifier(
                    baseResourceIdentifier,
                    filters,
                    requestedAttributePaths,
                    excludedAttributePaths,
                    null);
            return result;
        }

        private static Uri ComposeTypeIdentifier(Uri baseResourceIdentifier, string path)
        {
            if (null == baseResourceIdentifier)
            {
                throw new ArgumentNullException(nameof(baseResourceIdentifier));
            }

            if (null == path)
            {
                throw new ArgumentNullException(nameof(path));
            }

            string baseResourceIdentifierValue = baseResourceIdentifier.ToString().TrimEnd('/');
            string prefix = ScimPath.Prefix;
            string resultValue =
                baseResourceIdentifierValue +
                ServiceConstants.SeparatorSegments +
                (string.IsNullOrEmpty(prefix)
                    ? string.Empty
                    : prefix + ServiceConstants.SeparatorSegments) +
                path.TrimStart('/');

            Uri result = new Uri(resultValue);
            return result;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0", Justification = "False analysis of 'this' parameter of an extension method")]
        public static IResourceIdentifier GetIdentifier(this Resource resource)
        {
            if (!resource.TryGetSchemaIdentifier(out string schemaIdentifier))
            {
                schemaIdentifier = resource.GetSchemaIdentifier();
            }

            IResourceIdentifier result = new ResourceIdentifier(schemaIdentifier, resource.Identifier);
            return result;
        }

        private static string GetPath(this Schematized schematized)
        {
            if (schematized.TryGetPath(out string path))
            {
                return path;
            }

            if (schematized.Is(SchemaIdentifiers.Core2EnterpriseUser))
            {
                return ProtocolConstants.PathUsers;
            }

            if (schematized.Is(SchemaIdentifiers.Core2User))
            {
                return ProtocolConstants.PathUsers;
            }

            if (schematized.Is(SchemaIdentifiers.Core2Group))
            {
                return ProtocolConstants.PathGroups;
            }

            switch (schematized)
            {
                case UserBase _:
                    return ProtocolConstants.PathUsers;
                case GroupBase _:
                    return ProtocolConstants.PathGroups;
                default:
                    string unsupportedTypeName = schematized.GetType().FullName;
                    throw new NotSupportedException(unsupportedTypeName);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0", Justification = "False analysis of 'this' parameter of an extension method")]
        public static Uri GetResourceIdentifier(this Resource resource, Uri baseResourceIdentifier)
        {
            if (null == baseResourceIdentifier)
            {
                throw new ArgumentNullException(nameof(baseResourceIdentifier));
            }

            if (string.IsNullOrWhiteSpace(resource.Identifier))
            {
                throw new InvalidOperationException(SystemForCrossDomainIdentityManagementProtocolResources.ExceptionInvalidResource);
            }

            if (resource.TryGetIdentifier(baseResourceIdentifier, out Uri result))
            {
                return result;
            }

            Uri typeResource = resource.GetTypeIdentifier(baseResourceIdentifier);
            string escapedIdentifier = Uri.EscapeDataString(resource.Identifier);
            string resultValue =
                typeResource.ToString() +
                ServiceConstants.SeparatorSegments + 
                escapedIdentifier;
            result = new Uri(resultValue);
            return result;
        }

        private static string GetSchemaIdentifier(IReadOnlyCollection<string> schemaIdentifiers)
        {
            if (null == schemaIdentifiers)
            {
                throw new ArgumentNullException(nameof(schemaIdentifiers));
            }

            if (!schemaIdentifiers.Any())
            {
                throw new ArgumentException(SystemForCrossDomainIdentityManagementProtocolResources.ExceptionUnidentifiableSchema);
            }

            foreach (string schema in schemaIdentifiers)
            {
                switch (schema)
                {
                    case SchemaIdentifiers.Core2User:
                    case SchemaIdentifiers.Core2EnterpriseUser:
                        return SchemaIdentifiers.Core2EnterpriseUser;
                    case SchemaIdentifiers.Core2Group:
                        return SchemaIdentifiers.Core2Group;
                }
            }

            string schemas = string.Join(Environment.NewLine, schemaIdentifiers);
            throw new NotSupportedException(schemas);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0", Justification = "False analysis of 'this' parameter of an extension method")]
        public static string GetSchemaIdentifier(this Schematized schematized)
        {
            if (!schematized.TryGetSchemaIdentifier(out string result))
            {
                result = ProtocolExtensions.GetSchemaIdentifier(schematized.Schemas);
            }
            return result;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0", Justification = "False analysis of 'this' parameter of an extension method")]
        public static Uri GetTypeIdentifier(this Schematized schematized, Uri baseResourceIdentifier)
        {
            if (null == baseResourceIdentifier)
            {
                throw new ArgumentNullException(nameof(baseResourceIdentifier));
            }

            if (null == schematized.Schemas)
            {
                throw new InvalidOperationException(SystemForCrossDomainIdentityManagementProtocolResources.ExceptionInvalidResource);
            }

            Uri result;
            string path = schematized.GetPath();
            result = ProtocolExtensions.ComposeTypeIdentifier(baseResourceIdentifier, path);

            return result;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0", Justification = "False analysis of 'this' parameter of an extension method")]
        public static bool Matches(this IExtension extension, string schemaIdentifier)
        {
            bool result = string.Equals(schemaIdentifier, extension.SchemaIdentifier, StringComparison.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>
        /// The value an operation leaves a single-valued sub-attribute holding.
        /// </summary>
        /// <remarks>
        /// A remove clears the attribute when it names the value being held, or names no
        /// value at all. Naming a different value removes nothing - the attribute keeps
        /// what it had. Assigning the requested value on that path meant a remove of a
        /// value the resource did not hold *wrote* that value: the operation performed an
        /// add. RFC 7644 section 3.5.2.2.
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

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity", Justification = "None")]
        internal static IEnumerable<ElectronicMailAddress> PatchElectronicMailAddresses(
            IEnumerable<ElectronicMailAddress> electronicMailAddresses,
            PatchOperation2 operation)
        {
            if (null == operation)
            {
                return electronicMailAddresses;
            }

            if
            (
                !string.Equals(
                    AttributeNames.ElectronicMailAddresses,
                    operation.Path.AttributePath,
                    StringComparison.OrdinalIgnoreCase)
            )
            {
                return electronicMailAddresses;
            }

            if (null == operation.Path.ValuePath)
            {
                return electronicMailAddresses;
            }

            if (string.IsNullOrWhiteSpace(operation.Path.ValuePath.AttributePath))
            {
                return electronicMailAddresses;
            }

            IFilter subAttribute = operation.Path.SubAttributes.SingleOrDefault();
            if (null == subAttribute)
            {
                return electronicMailAddresses;
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
                return electronicMailAddresses;
            }

            if
            (
                !string.Equals(
                    Microsoft.SCIM.AttributeNames.Type,
                    subAttribute.AttributePath,
                    StringComparison.OrdinalIgnoreCase)
            )
            {
                return electronicMailAddresses;
            }

            // Every canonical type RFC 7643 section 4.1.2 defines, as the phone number patcher
            // does. Other was missing, so emails[type eq "other"].value - which Entra ID
            // sends - answered 204 and changed nothing.
            string electronicMailAddressType = subAttribute.ComparisonValue;
            if
            (
                    !string.Equals(electronicMailAddressType, ElectronicMailAddress.Home, StringComparison.Ordinal)
                && !string.Equals(electronicMailAddressType, ElectronicMailAddress.Other, StringComparison.Ordinal)
                && !string.Equals(electronicMailAddressType, ElectronicMailAddress.Work, StringComparison.Ordinal)
            )
            {
                return electronicMailAddresses;
            }

            ElectronicMailAddress electronicMailAddressExisting =
                electronicMailAddresses?
                .SingleOrDefault(
                    (ElectronicMailAddress item) =>
                        string.Equals(subAttribute.ComparisonValue, item.ItemType, StringComparison.Ordinal));

            // A user holding a work address still has no home one, and the value path names
            // the address to create. Reading the missing entry as though it existed
            // dereferenced null on the very next line.
            ElectronicMailAddress electronicMailAddress =
                electronicMailAddressExisting
                ?? new ElectronicMailAddress()
                    {
                        ItemType = electronicMailAddressType
                    };

            electronicMailAddress.Value =
                ProtocolExtensions.ResolveValue(
                    operation,
                    operation.Value?.FirstOrDefault()?.Value,
                    electronicMailAddress.Value);

            IEnumerable<ElectronicMailAddress> result;
            if (string.IsNullOrWhiteSpace(electronicMailAddress.Value))
            {
                if (electronicMailAddressExisting != null)
                {
                    result =
                        electronicMailAddresses
                        .Where(
                            (ElectronicMailAddress item) =>
                                !string.Equals(subAttribute.ComparisonValue, item.ItemType, StringComparison.Ordinal))
                        .ToArray();
                }
                else
                {
                    result = electronicMailAddresses;
                }
                return result;
            }

            if (electronicMailAddressExisting != null)
            {
                return electronicMailAddresses;
            }

            result =
                new ElectronicMailAddress[]
                    {
                        electronicMailAddress
                    };
            if (null == electronicMailAddresses)
            {
                return result;
            }

            // Union with `result`, not with itself: unioning the existing collection with
            // the existing collection returned it unchanged, so adding an address of a type
            // the user did not yet hold silently did nothing.
            result = electronicMailAddresses.Union(result).ToArray();
            return result;
        }

        /// <summary>
        /// Applies an operation naming <c>roles</c> as a whole, or one entry of it by filter.
        /// </summary>
        private static IEnumerable<Role> PatchRoleEntries(IEnumerable<Role> roles, PatchOperation2 operation)
        {
            List<Role> current =
                null == roles ? new List<Role>() : new List<Role>(roles);

            IFilter selector = operation.Path.SubAttributes?.SingleOrDefault();

            if (OperationName.Remove == operation.Name)
            {
                if (null == selector && (null == operation.Value || operation.Value.Count < 1))
                {
                    return Enumerable.Empty<Role>();
                }

                IEnumerable<string> removing =
                    null != selector
                        ? new[] { selector.ComparisonValue }
                        : operation.Value.Select((OperationValue item) => item.Value);

                foreach (string value in removing)
                {
                    current.RemoveAll(
                        (Role item) =>
                            string.Equals(item.Value, value, StringComparison.OrdinalIgnoreCase));
                }

                return current.ToArray();
            }

            if (null == operation.Value)
            {
                return roles;
            }

            if (OperationName.Replace == operation.Name && null == selector)
            {
                current.Clear();
            }

            foreach (OperationValue value in operation.Value)
            {
                if (string.IsNullOrWhiteSpace(value.Value))
                {
                    continue;
                }

                if (current.Any(
                        (Role item) =>
                            string.Equals(item.Value, value.Value, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                current.Add(
                    new Role()
                    {
                        Value = value.Value
                    });
            }

            return current.ToArray();
        }

        /// <summary>
        /// Whether <paramref name="role"/> satisfies <c>roles[&lt;selector&gt; eq &lt;comparison&gt;]</c>.
        /// </summary>
        private static bool RoleMatches(Role role, string selector, string comparison)
        {
            if (null == role)
            {
                return false;
            }

            if (string.Equals(selector, Microsoft.SCIM.AttributeNames.Type, StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(comparison, role.ItemType, StringComparison.Ordinal);
            }

            if (string.Equals(selector, Microsoft.SCIM.AttributeNames.Primary, StringComparison.OrdinalIgnoreCase))
            {
                return bool.TryParse(comparison, out bool primary) && role.Primary == primary;
            }

            if (string.Equals(selector, Microsoft.SCIM.AttributeNames.Value, StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(comparison, role.Value, StringComparison.Ordinal);
            }

            if (string.Equals(selector, Microsoft.SCIM.AttributeNames.Display, StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(comparison, role.Display, StringComparison.Ordinal);
            }

            return false;
        }

        /// <summary>
        /// A role that <c>roles[&lt;selector&gt; eq &lt;comparison&gt;]</c> would select, or null
        /// when the filter names a sub-attribute a role does not define.
        /// </summary>
        private static Role CreateRole(string selector, string comparison)
        {
            if (string.Equals(selector, Microsoft.SCIM.AttributeNames.Type, StringComparison.OrdinalIgnoreCase))
            {
                return new Role() { ItemType = comparison, Primary = true };
            }

            if (string.Equals(selector, Microsoft.SCIM.AttributeNames.Primary, StringComparison.OrdinalIgnoreCase))
            {
                return
                    bool.TryParse(comparison, out bool primary)
                        ? new Role() { Primary = primary }
                        : null;
            }

            if (string.Equals(selector, Microsoft.SCIM.AttributeNames.Value, StringComparison.OrdinalIgnoreCase))
            {
                return new Role() { Value = comparison };
            }

            if (string.Equals(selector, Microsoft.SCIM.AttributeNames.Display, StringComparison.OrdinalIgnoreCase))
            {
                return new Role() { Display = comparison };
            }

            return null;
        }

        private static bool TryReadRole(Role role, string subAttribute, out string value)
        {
            value = null;

            if (string.Equals(subAttribute, Microsoft.SCIM.AttributeNames.Value, StringComparison.OrdinalIgnoreCase))
            {
                value = role.Value;
                return true;
            }

            if (string.Equals(subAttribute, Microsoft.SCIM.AttributeNames.Display, StringComparison.OrdinalIgnoreCase))
            {
                value = role.Display;
                return true;
            }

            if (string.Equals(subAttribute, Microsoft.SCIM.AttributeNames.Type, StringComparison.OrdinalIgnoreCase))
            {
                value = role.ItemType;
                return true;
            }

            if (string.Equals(subAttribute, Microsoft.SCIM.AttributeNames.Primary, StringComparison.OrdinalIgnoreCase))
            {
                value = role.Primary ? bool.TrueString : bool.FalseString;
                return true;
            }

            return false;
        }

        private static bool TryWriteRole(Role role, string subAttribute, string value)
        {
            if (string.Equals(subAttribute, Microsoft.SCIM.AttributeNames.Value, StringComparison.OrdinalIgnoreCase))
            {
                role.Value = value;
                return true;
            }

            if (string.Equals(subAttribute, Microsoft.SCIM.AttributeNames.Display, StringComparison.OrdinalIgnoreCase))
            {
                role.Display = value;
                return true;
            }

            if (string.Equals(subAttribute, Microsoft.SCIM.AttributeNames.Type, StringComparison.OrdinalIgnoreCase))
            {
                role.ItemType = value;
                return true;
            }

            if (string.Equals(subAttribute, Microsoft.SCIM.AttributeNames.Primary, StringComparison.OrdinalIgnoreCase))
            {
                if (!bool.TryParse(value, out bool primary))
                {
                    return false;
                }

                role.Primary = primary;
                return true;
            }

            return false;
        }

        internal static IEnumerable<Role> PatchRoles(IEnumerable<Role> roles, PatchOperation2 operation)
        {
            if (null == operation)
            {
                return roles;
            }

            if
            (
                !string.Equals(
                    Microsoft.SCIM.AttributeNames.Roles,
                    operation.Path.AttributePath,
                    StringComparison.OrdinalIgnoreCase)
            )
            {
                return roles;
            }

            // A whole-entry operation: "add roles" with a list of values, or "remove
            // roles[value eq "x"]". Only the sub-attribute shape - roles[type eq "x"].value -
            // was handled, so the two operations a client is most likely to send answered
            // success and changed nothing.
            if (null == operation.Path.ValuePath
                || string.IsNullOrWhiteSpace(operation.Path.ValuePath.AttributePath))
            {
                return ProtocolExtensions.PatchRoleEntries(roles, operation);
            }

            if (string.IsNullOrWhiteSpace(operation.Path.ValuePath.AttributePath))
            {
                return roles;
            }

            IFilter subAttribute = operation.Path.SubAttributes.SingleOrDefault();
            if (null == subAttribute)
            {
                return roles;
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
                return roles;
            }

            // The filter names which sub-attribute selects the entry and the value path names
            // which one is written. Reading the first as type and the second as value was only
            // right for roles[type eq "x"].value: roles[primary eq true].display then matched
            // nothing, appended an entry typed "true", and wrote the display name over its
            // value.
            string selector = subAttribute.AttributePath;
            string patched = operation.Path.ValuePath.AttributePath;

            bool Matches(Role item) =>
                ProtocolExtensions.RoleMatches(item, selector, subAttribute.ComparisonValue);

            Role roleExisting = roles?.SingleOrDefault((Role item) => Matches(item));

            // A new entry is seeded so that the filter that failed to find it now would find
            // it. Leaving the selecting sub-attribute unset meant a role added this way could
            // never be found by the same path again, and reading the missing entry as though
            // it existed dereferenced null.
            Role role = roleExisting ?? ProtocolExtensions.CreateRole(selector, subAttribute.ComparisonValue);

            if (null == role)
            {
                return roles;
            }

            if (!ProtocolExtensions.TryReadRole(role, patched, out string current))
            {
                return roles;
            }

            string resolved =
                ProtocolExtensions.ResolveValue(
                    operation,
                    operation.Value?.FirstOrDefault()?.Value,
                    current);

            if (!ProtocolExtensions.TryWriteRole(role, patched, resolved))
            {
                return roles;
            }

            IEnumerable<Role> result;

            // Only an emptied value drops the entry: a role with no value carries nothing.
            // An emptied display or type leaves an entry that still names a role.
            if (string.Equals(patched, Microsoft.SCIM.AttributeNames.Value, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(role.Value))
            {
                if (roleExisting != null)
                {
                    result = roles.Where((Role item) => !Matches(item)).ToArray();
                }
                else
                {
                    result = roles;
                }
                return result;
            }

            if (roleExisting != null)
            {
                return roles;
            }

            result =
                new Role[]
                    {
                        role
                    };

            if (null == roles)
            {
                return result;
            }

            // Union with `result`, not with itself: unioning the existing roles with the
            // existing roles returned them unchanged, so adding a role of a type the user
            // did not yet hold silently did nothing.
            result = roles.Union(result).ToArray();
            return result;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "resourceIdentifier", Justification = "False analysis of extension method")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0", Justification = "False analysis of 'this' parameter of an extension method")]
#pragma warning disable IDE0060 // Remove unused parameter
        public static Uri Serialize(this IResourceIdentifier resourceIdentifier, Resource resource, Uri baseResourceIdentifier)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            if (null == resource)
            {
                throw new ArgumentNullException(nameof(resource));
            }

            if (null == baseResourceIdentifier)
            {
                throw new ArgumentNullException(nameof(baseResourceIdentifier));
            }

            Uri typeResource = resource.GetTypeIdentifier(baseResourceIdentifier);
            string escapedIdentifier = Uri.EscapeDataString(resource.Identifier);
            string resultValue =
                typeResource.ToString() +
                ServiceConstants.SeparatorSegments +
                escapedIdentifier;

            Uri result = new Uri(resultValue);
            return result;
        }

        public static async Task<string> SerializeAsync(this HttpRequestMessage request, bool acceptLargeObjects)
        {
            if (null == request)
            {
                throw new ArgumentNullException(nameof(request));
            }

            StringBuilder buffer = new StringBuilder();
            TextWriter textWriter = null;
            try
            {

#pragma warning disable CA2000 // Dispose objects before losing scope
                textWriter = new StringWriter(buffer);
#pragma warning restore CA2000 // Dispose objects before losing scope

                IHttpRequestMessageWriter requestWriter = null;
                try
                {
                    requestWriter = new HttpRequestMessageWriter(request, textWriter, acceptLargeObjects);
                    textWriter = null;
                    await requestWriter.WriteAsync().ConfigureAwait(false);
                    await requestWriter.FlushAsync().ConfigureAwait(false);
                    string result = buffer.ToString();
                    return result;
                }
                finally
                {
                    if (requestWriter != null)
                    {
                        requestWriter.Close();
                        requestWriter = null;
                    }
                }
            }
            finally
            {
                if (textWriter != null)
                {
                    textWriter.Flush();
                    textWriter.Close();
#pragma warning disable IDE0059 // Unnecessary assignment of a value
                    textWriter = null;
#pragma warning restore IDE0059 // Unnecessary assignment of a value
                }
            }
        }

        public static async Task<string> SerializeAsync(this HttpRequestMessage request)
        {
            if (null == request)
            {
                throw new ArgumentNullException(nameof(request));
            }

            string result = await request.SerializeAsync(false).ConfigureAwait(false);
            return result;
        }

        public static IReadOnlyCollection<T> ToCollection<T>(this IEnumerable enumerable)
        {
            if (null == enumerable)
            {
                throw new ArgumentNullException(nameof(enumerable));
            }

            IList<T> list = new List<T>();
            foreach (object item in enumerable)
            {
                T typed = (T)item;
                list.Add(typed);
            }
            IReadOnlyCollection<T> result = list.ToArray();
            return result;
        }

        public static IReadOnlyCollection<T> ToCollection<T>(this ArrayList array)
        {
            if (null == array)
            {
                throw new ArgumentNullException(nameof(array));
            }

            IList<T> list = new List<T>(array.Count);
            foreach (object item in array)
            {
                T typed = (T)item;
                list.Add(typed);
            }
            IReadOnlyCollection<T> result = list.ToArray();
            return result;
        }

        public static IReadOnlyCollection<T> ToCollection<T>(this T item)
        {
            IReadOnlyCollection<T> result =
                new T[]
                    {
                        item
                    };
            return result;
        }

        private static bool TryMatch(
            IReadOnlyCollection<string> schemaIdentifiers,
            IReadOnlyCollection<IExtension> extensions,
            out IExtension matchingExtension)
        {
            matchingExtension = null;

            if (null == extensions)
            {
                return false;
            }

            if (null == schemaIdentifiers)
            {
                return false;
            }

            foreach (IExtension extension in extensions)
            {
                foreach (string schemaIdentifier in schemaIdentifiers)
                {
                    if (extension.Matches(schemaIdentifier))
                    {
                        matchingExtension = extension;
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool TryMatch(
            this IReadOnlyCollection<IExtension> extensions,
            IReadOnlyCollection<string> schemaIdentifiers,
            out IExtension matchingExtension)
        {
            bool result = ProtocolExtensions.TryMatch(schemaIdentifiers, extensions, out matchingExtension);
            return result;
        }

        public static bool TryMatch(
            this IReadOnlyCollection<IExtension> extensions,
            string schemaIdentifier,
            out IExtension matchingExtension)
        {
            if (string.IsNullOrWhiteSpace(schemaIdentifier))
            {
                matchingExtension = null;
                return false;
            }
            IReadOnlyCollection<string> schemaIdentifiers = schemaIdentifier.ToCollection();
            bool result = extensions.TryMatch(schemaIdentifiers, out matchingExtension);
            return result;
        }

        public static bool References(this PatchRequest2Base<PatchOperation2Combined> patch, string referee)
        {
            if (null == patch)
            {
                throw new ArgumentNullException(nameof(patch));
            }

            if (string.IsNullOrWhiteSpace(referee))
            {
                throw new ArgumentNullException(nameof(referee));
            }

            bool result = patch.TryFindReference(referee, out IReadOnlyCollection<OperationValue> _);
            return result;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0", Justification = "False analysis of the 'this' parameter of an extension method")]
        public static bool TryFindReference(
            this PatchRequest2Base<PatchOperation2Combined> patch,
            string referee,
            out IReadOnlyCollection<OperationValue> references)
        {
            if (null == patch)
            {
                throw new ArgumentNullException(nameof(patch));
            }

            references = null;

            if (string.IsNullOrWhiteSpace(referee))
            {
                throw new ArgumentNullException(nameof(referee));
            }

            List<OperationValue> patchOperation2Values = new List<OperationValue>();

            foreach (PatchOperation2Combined operation in patch.Operations)
            {
                patchOperation2Values.AddRange(ProtocolExtensions.ReadOperationValues(operation));
            }

            IReadOnlyCollection<OperationValue> patchOperationValues = patchOperation2Values.AsReadOnly();

            IList<OperationValue> referencesBuffer = new List<OperationValue>(patchOperationValues.Count);
            foreach (OperationValue patchOperationValue in patchOperationValues)
            {
                if (!patchOperationValue.TryParseBulkIdentifierReferenceValue(out string value))
                {
                    value = patchOperationValue.Value;
                }

                if (string.Equals(referee, value,StringComparison.InvariantCulture))
                {
                    referencesBuffer.Add(patchOperationValue);
                }
            }

            references = referencesBuffer.ToArray();
            bool result = references.Any();
            return result;
        }

        /// <summary>
        /// Reads one combined operation's raw <c>value</c> into operation values.
        /// </summary>
        private static IReadOnlyCollection<OperationValue> ReadOperationValues(
            PatchOperation2Combined operation)
        {
            if (null == operation?.Value)
            {
                return Array.Empty<OperationValue>();
            }

            OperationValue[] values =
                JsonConvert.DeserializeObject<OperationValue[]>(
                    operation.Value,
                    ProtocolConstants.JsonSettings.Value);

            if (null != values)
            {
                return values;
            }

            OperationValue single =
                JsonConvert.DeserializeObject<OperationValue>(
                    operation.Value,
                    ProtocolConstants.JsonSettings.Value);

            if (null != single && (null != single.Value || null != single.Reference))
            {
                return new[] { single };
            }

            string scalar =
                JsonConvert.DeserializeObject<string>(
                    operation.Value,
                    ProtocolConstants.JsonSettings.Value);

            return new[] { new OperationValue() { Value = scalar } };
        }

        /// <summary>
        /// Rewrites every value that references <paramref name="referee"/> as a
        /// <c>bulkId</c> to <paramref name="replacement"/>, and returns how many it changed.
        /// </summary>
        /// <remarks>
        /// Bulk resolution used to read the values, mutate the objects it had read and stop
        /// there - but reading deserializes, so the mutation landed on a copy and the
        /// operation still carried <c>bulkId:...</c>. The reference reached the provider
        /// unresolved and was stored as though it were an identifier. Writing the values
        /// back is what actually resolves them.
        /// </remarks>
        internal static int ReplaceReferences(
            this PatchRequest2Base<PatchOperation2Combined> patch,
            string referee,
            string replacement)
        {
            if (null == patch)
            {
                throw new ArgumentNullException(nameof(patch));
            }

            if (string.IsNullOrWhiteSpace(referee))
            {
                throw new ArgumentNullException(nameof(referee));
            }

            if (string.IsNullOrWhiteSpace(replacement))
            {
                throw new ArgumentNullException(nameof(replacement));
            }

            int count = 0;

            foreach (PatchOperation2Combined operation in patch.Operations)
            {
                IReadOnlyCollection<OperationValue> values =
                    ProtocolExtensions.ReadOperationValues(operation);

                bool changed = false;
                foreach (OperationValue value in values)
                {
                    if
                    (
                            value.TryParseBulkIdentifierReferenceValue(out string bulkIdentifier)
                        && string.Equals(referee, bulkIdentifier, StringComparison.Ordinal)
                    )
                    {
                        value.Value = replacement;
                        changed = true;
                        count++;
                    }
                }

                if (changed)
                {
                    operation.SetValues(values);
                }
            }

            return count;
        }

        /// <summary>
        /// The <c>bulkId</c> references still present in a patch request.
        /// </summary>
        /// <remarks>
        /// Read after resolution: anything left names a <c>bulkId</c> the request never
        /// declared, and storing it would hand a client back a reference to a resource
        /// that does not exist.
        /// </remarks>
        internal static IReadOnlyCollection<string> FindBulkIdentifierReferences(
            this PatchRequest2Base<PatchOperation2Combined> patch)
        {
            if (null == patch)
            {
                throw new ArgumentNullException(nameof(patch));
            }

            List<string> result = new List<string>();

            foreach (PatchOperation2Combined operation in patch.Operations)
            {
                foreach (OperationValue value in ProtocolExtensions.ReadOperationValues(operation))
                {
                    if (value.TryParseBulkIdentifierReferenceValue(out string bulkIdentifier))
                    {
                        result.Add(bulkIdentifier);
                    }
                }
            }

            return result;
        }

        private static bool TryParseBulkIdentifierReferenceValue(string value, out string bulkIdentifier)
        {
            bulkIdentifier = null;

            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            Match match = ProtocolExtensions.BulkIdentifierExpression.Value.Match(value);
            bool result = match.Success;
            if (result)
            {
                bulkIdentifier = match.Groups[ProtocolExtensions.ExpressionGroupNameBulkIdentifier].Value;
            }

            return result;
        }

        public static bool TryParseBulkIdentifierReferenceValue(this OperationValue value, out string bulkIdentifier)
        {
            bulkIdentifier = null;

            if (null == value)
            {
                return false;
            }

            bool result = ProtocolExtensions.TryParseBulkIdentifierReferenceValue(value.Value, out bulkIdentifier);
            return result;
        }


        private sealed class HttpRequestMessageWriter : IHttpRequestMessageWriter
        {
            private const string TemplateHeader = "{0}: {1}";

            private readonly object thisLock = new object();

            private TextWriter innerWriter;

            public HttpRequestMessageWriter(HttpRequestMessage message, TextWriter writer, bool acceptLargeObjects)
            {
                this.Message = message ?? throw new ArgumentNullException(nameof(message));
                this.innerWriter = writer ?? throw new ArgumentNullException(nameof(writer));
                this.AcceptLargeObjects = acceptLargeObjects;
            }

            private bool AcceptLargeObjects
            {
                get;
            }

            private HttpRequestMessage Message
            {
                get;
                set;
            }

            public void Close()
            {
                this.innerWriter.Flush();
                this.innerWriter.Close();
            }

            public void Dispose()
            {
                if (this.innerWriter != null)
                {
                    lock (this.thisLock)
                    {
                        if (this.innerWriter != null)
                        {
                            this.Close();
                            this.innerWriter = null;
                        }
                    }
                }
            }

            public async Task FlushAsync()
            {
                await this.innerWriter.FlushAsync().ConfigureAwait(false);
            }

            public async Task WriteAsync()
            {
                if (this.Message.RequestUri != null)
                {
                    string line = HttpUtility.UrlDecode(this.Message.RequestUri.AbsoluteUri);
                    await this.innerWriter.WriteLineAsync(line).ConfigureAwait(false);
                }

                if (this.Message.Headers != null)
                {
                    foreach (KeyValuePair<string, IEnumerable<string>> header in this.Message.Headers)
                    {
                        if (!header.Value.Any())
                        {
                            continue;
                        }

                        string value;
                        if (1 == header.Value.LongCount())
                        {
                            value = header.Value.Single();
                        }
                        else
                        {
                            string[] values = header.Value.ToArray();
                            value = JsonFactory.Instance.Create(values, this.AcceptLargeObjects);
                        }

                        string line =
                            string.Format(
                                CultureInfo.InvariantCulture,
                                HttpRequestMessageWriter.TemplateHeader,
                                header.Key,
                                value);
                        await this.innerWriter.WriteLineAsync(line).ConfigureAwait(false);
                    }
                }

                if (this.Message.Content != null)
                {
                    string line = await this.Message.Content.ReadAsStringAsync().ConfigureAwait(false);
                    await this.innerWriter.WriteLineAsync(line).ConfigureAwait(false);
                }
            }

        }
    }
}
