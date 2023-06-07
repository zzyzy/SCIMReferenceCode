using System;
using System.Text;

namespace Microsoft.SCIM
{
    public static class StringExtensions
    {
        public static string Replace(this string source,
            string oldValue,
            string newValue,
            StringComparison comparison)
        {
            return source.Replace(oldValue, newValue);
        }

        public static int IndexOf(this string source, char value, StringComparison comparison)
        {
            var index = source.IndexOf($"{value}", comparison);
            return index;
        }

        public static bool Contains(this string source, string value, StringComparison comparison)
        {
            var index = source.IndexOf(value, comparison);
            return index != -1;
        }

        public static int GetHashCode(this string source, StringComparison comparison)
        {
            return source.GetHashCode();
        }
    }
}