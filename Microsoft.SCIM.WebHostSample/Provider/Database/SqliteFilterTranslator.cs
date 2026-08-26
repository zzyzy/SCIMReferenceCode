// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM.WebHostSample.Provider.Database
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Dapper;
    using Microsoft.SCIM;

    /// <summary>
    /// Translates a SCIM filter into a SQL WHERE clause over the columns of the store.
    /// </summary>
    /// <remarks>
    /// The in-memory providers build a predicate over <c>UserEntity</c> and note that it "would
    /// compose into a SQL WHERE clause unchanged". This is that clause. The attributes it
    /// accepts and the operators it accepts for each are deliberately the same set, so the two
    /// sample stores answer a given filter the same way.
    ///
    /// Every comparison value becomes a parameter. A filter arrives from the wire, so composing
    /// one into the SQL text would be an injection into the store's own query - and unlike an
    /// escaping rule, a parameter cannot be got wrong for some particular value.
    /// </remarks>
    internal static class SqliteFilterTranslator
    {
        /// <summary>The clause that matches every row, for a query with no filter.</summary>
        private const string MatchAll = "1 = 1";

        /// <summary>
        /// The user filter: an OR across the alternates, an AND along each one's chain.
        /// </summary>
        public static string TranslateUsers(
            IReadOnlyCollection<IFilter> filters,
            DynamicParameters parameters)
        {
            if (null == filters || filters.Count == 0)
            {
                return SqliteFilterTranslator.MatchAll;
            }

            List<string> alternates = new List<string>();

            foreach (IFilter alternate in filters)
            {
                List<string> conjuncts = new List<string>();

                for (IFilter filter = alternate; null != filter; filter = filter.AdditionalFilter)
                {
                    conjuncts.Add(SqliteFilterTranslator.TranslateUser(filter, parameters));
                }

                alternates.Add(string.Join(" AND ", conjuncts));
            }

            return string.Join(" OR ", alternates.Select((string item) => "(" + item + ")"));
        }

        /// <summary>
        /// The group filter: <c>displayName eq</c>, or nothing.
        /// </summary>
        /// <remarks>
        /// The narrower set the in-memory group provider accepts, kept narrow on purpose. A
        /// relying party adding an attribute here adds a column and an index with it; what
        /// cannot be indexed should not be advertised as filterable.
        /// </remarks>
        public static string TranslateGroups(IFilter filter, DynamicParameters parameters)
        {
            if (null == filter)
            {
                return SqliteFilterTranslator.MatchAll;
            }

            SqliteFilterTranslator.Validate(filter);

            if (filter.FilterOperator != ComparisonOperator.Equals)
            {
                throw new NotSupportedException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        SystemForCrossDomainIdentityManagementServiceResources.ExceptionFilterOperatorNotSupportedTemplate,
                        filter.FilterOperator));
            }

            if (!filter.AttributePath.Equals(AttributeNames.DisplayName, StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        SystemForCrossDomainIdentityManagementServiceResources.ExceptionFilterAttributePathNotSupportedTemplate,
                        filter.AttributePath));
            }

            return SqliteFilterTranslator.Text("DisplayName", filter, parameters);
        }

        private static string TranslateUser(IFilter filter, DynamicParameters parameters)
        {
            SqliteFilterTranslator.Validate(filter);

            if (filter.AttributePath.Equals(AttributeNames.UserName, StringComparison.OrdinalIgnoreCase))
            {
                // eq, co, sw, ew and ne. Entra ID looks a user up by userName before deciding
                // whether to create them, and uses more than one of these.
                return SqliteFilterTranslator.Text("UserName", filter, parameters);
            }

            if (filter.AttributePath.Equals(AttributeNames.ExternalIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                SqliteFilterTranslator.RequireEquals(filter);
                return SqliteFilterTranslator.Text("ExternalId", filter, parameters);
            }

            if (filter.AttributePath.Equals(AttributeNames.Active, StringComparison.OrdinalIgnoreCase))
            {
                SqliteFilterTranslator.RequireEquals(filter);

                string name = SqliteFilterTranslator.Add(parameters, bool.Parse(filter.ComparisonValue));

                // A user whose active is unset matches neither true nor false, because in SQL a
                // comparison with NULL is not true. That is the same answer the in-memory
                // provider gives, where a null bool? equals neither.
                return "IsActive = " + name;
            }

            string lastModified =
                string.Concat(AttributeNames.Metadata, ".", AttributeNames.LastModified);

            if (filter.AttributePath.Equals(lastModified, StringComparison.OrdinalIgnoreCase))
            {
                return SqliteFilterTranslator.Timestamp("LastModifiedUtc", filter, parameters);
            }

            throw new NotSupportedException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    SystemForCrossDomainIdentityManagementServiceResources.ExceptionFilterAttributePathNotSupportedTemplate,
                    filter.AttributePath));
        }

        private static void Validate(IFilter filter)
        {
            if (string.IsNullOrWhiteSpace(filter.AttributePath)
                || string.IsNullOrWhiteSpace(filter.ComparisonValue))
            {
                throw new ArgumentException(
                    SystemForCrossDomainIdentityManagementServiceResources.ExceptionInvalidParameters);
            }
        }

        private static void RequireEquals(IFilter filter)
        {
            if (filter.FilterOperator != ComparisonOperator.Equals)
            {
                throw new NotSupportedException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        SystemForCrossDomainIdentityManagementServiceResources.ExceptionFilterOperatorNotSupportedTemplate,
                        filter.FilterOperator));
            }
        }

        /// <summary>
        /// A comparison against a text column, case-insensitively.
        /// </summary>
        /// <remarks>
        /// COLLATE NOCASE for equality and LIKE for the substring operators, which SQLite
        /// already matches case-insensitively. Both are ASCII-only, which is what
        /// <see cref="StringComparison.OrdinalIgnoreCase"/> is on the in-memory side, so the two
        /// stores agree.
        /// </remarks>
        private static string Text(string column, IFilter filter, DynamicParameters parameters)
        {
            switch (filter.FilterOperator)
            {
                case ComparisonOperator.Equals:
                    return column + " = " + SqliteFilterTranslator.Add(parameters, filter.ComparisonValue) + " COLLATE NOCASE";

                case ComparisonOperator.NotEquals:
                    // The null case spelled out, because NULL <> 'x' is not true in SQL and the
                    // in-memory predicate does match a row whose value is unset.
                    return "("
                        + column + " IS NULL OR "
                        + column + " <> " + SqliteFilterTranslator.Add(parameters, filter.ComparisonValue) + " COLLATE NOCASE)";

                case ComparisonOperator.Contains:
                    return SqliteFilterTranslator.Like(column, "%" + SqliteFilterTranslator.EscapeLike(filter.ComparisonValue) + "%", parameters);

                case ComparisonOperator.StartsWith:
                    return SqliteFilterTranslator.Like(column, SqliteFilterTranslator.EscapeLike(filter.ComparisonValue) + "%", parameters);

                case ComparisonOperator.EndsWith:
                    return SqliteFilterTranslator.Like(column, "%" + SqliteFilterTranslator.EscapeLike(filter.ComparisonValue), parameters);

                default:
                    throw new NotSupportedException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            SystemForCrossDomainIdentityManagementServiceResources.ExceptionFilterOperatorNotSupportedTemplate,
                            filter.FilterOperator));
            }
        }

        private static string Timestamp(string column, IFilter filter, DynamicParameters parameters)
        {
            string comparison;

            switch (filter.FilterOperator)
            {
                case ComparisonOperator.GreaterThan:
                    comparison = ">";
                    break;

                case ComparisonOperator.LessThan:
                    comparison = "<";
                    break;

                case ComparisonOperator.EqualOrGreaterThan:
                    comparison = ">=";
                    break;

                case ComparisonOperator.EqualOrLessThan:
                    comparison = "<=";
                    break;

                default:
                    throw new NotSupportedException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            SystemForCrossDomainIdentityManagementServiceResources.ExceptionFilterOperatorNotSupportedTemplate,
                            filter.FilterOperator));
            }

            // Bound as a DateTime so that the same handler that wrote the column formats the
            // comparison value. The column is text, and only identical formatting makes the
            // string ordering the timestamp ordering.
            DateTime value = DateTime.Parse(
                filter.ComparisonValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

            return column + " " + comparison + " " + SqliteFilterTranslator.Add(parameters, value);
        }

        private static string Like(string column, string pattern, DynamicParameters parameters)
        {
            return column + " LIKE " + SqliteFilterTranslator.Add(parameters, pattern) + " ESCAPE '\\'";
        }

        /// <summary>
        /// Escapes the LIKE wildcards, so that a filter value is matched literally.
        /// </summary>
        /// <remarks>
        /// <c>userName co "a_b"</c> asks for an underscore, not for any character. Without this
        /// the wildcards in a comparison value would widen the match - which for a client that
        /// looks a user up before creating them is the difference between finding them and
        /// finding somebody else.
        /// </remarks>
        private static string EscapeLike(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_");
        }

        private static string Add(DynamicParameters parameters, object value)
        {
            string name = string.Concat("@f", parameters.ParameterNames.Count().ToString(CultureInfo.InvariantCulture));
            parameters.Add(name, value);

            return name;
        }
    }
}
