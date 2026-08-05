//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM
{
    using System;
    using System.Text;
    using System.Text.RegularExpressions;

    internal static class StringExtension
    {
        private const string PatternEscapedDoubleQuote = @"\\*" + StringExtension.QuoteDouble;
        private const string PatternEscapedSingleQuote = @"\\*" + StringExtension.QuoteSingle;
        private const string QuoteDouble = "\"";
        private const string QuoteSingle = "'";

        private static readonly Lazy<Regex> ExpressionEscapedDoubleQuote =
            new Lazy<Regex>(
                () =>
                    new Regex(StringExtension.PatternEscapedDoubleQuote, RegexOptions.Compiled | RegexOptions.CultureInvariant));
        private static readonly Lazy<Regex> ExpressionEscapedSingleQuote =
            new Lazy<Regex>(
                () =>
                    new Regex(StringExtension.PatternEscapedSingleQuote, RegexOptions.Compiled | RegexOptions.CultureInvariant));

        // The three helpers below exist because string.Replace(string, string, StringComparison),
        // string.IndexOf(char, StringComparison) and string.GetHashCode(StringComparison) were
        // added in .NET Core 2.0/2.1 and are absent from .NET Framework 4.8. Rather than compile
        // different code per target framework - which is exactly how the two hosting legs would
        // drift apart - both legs go through these.
        //
        // Every call site passed StringComparison.InvariantCulture over inputs that are plain
        // ASCII: GUID placeholders, RFC 2396 reserved punctuation, and the single characters
        // ')', ' ' and '.'. Ordinal and invariant-culture comparison agree on all of those, so
        // the ordinal implementations here preserve the previous behaviour and, more importantly,
        // are identical on net48 and net10.0.

        public static string ReplaceInvariant(this string input, string oldValue, string newValue)
        {
            if (null == input)
            {
                throw new ArgumentNullException(nameof(input));
            }

            return input.Replace(oldValue, newValue);
        }

        public static int IndexOfInvariant(this string input, char value)
        {
            if (null == input)
            {
                throw new ArgumentNullException(nameof(input));
            }

            return input.IndexOf(value);
        }

        public static int GetInvariantHashCode(this string input)
        {
            if (null == input)
            {
                throw new ArgumentNullException(nameof(input));
            }

            return StringComparer.InvariantCulture.GetHashCode(input);
        }

        public static string Unquote(this string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return input;
            }

            int indexQuoteDouble = input.Trim().IndexOf(StringExtension.QuoteDouble, 0, StringComparison.OrdinalIgnoreCase);
            int indexQuoteSingle = input.Trim().IndexOf(StringExtension.QuoteSingle, 0, StringComparison.OrdinalIgnoreCase);
            Regex expression;
            if (0 == indexQuoteDouble)
            {
                expression = StringExtension.ExpressionEscapedDoubleQuote.Value;
            }
            else if (0 == indexQuoteSingle)
            {
                expression = StringExtension.ExpressionEscapedSingleQuote.Value;
            }
            else
            {
                return input;
            }

            MatchCollection matches = expression.Matches(input);
            if (matches.Count <= 0)
            {
                return input;
            }

            StringBuilder buffer = new StringBuilder(input);
            for (int matchIndex = matches.Count - 1; matchIndex >= 0; matchIndex--)
            {
                Match match = matches[matchIndex];
                int index = match.Index;
                buffer.Remove(index, 1);
            }
            string result = buffer.ToString();
            return result;
        }
    }
}