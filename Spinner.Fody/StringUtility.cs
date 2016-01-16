using System.Text.RegularExpressions;

namespace Spinner.Fody
{
    internal class StringUtility
    {
        private static readonly char[] s_wildcardCharacters = { '*', '?' };

        /// <summary>
        ///		Converts a simple wildcard pattern using * and ? to an equivalent regex pattern.
        /// </summary>
        /// <param name="pattern"> The wildcard pattern. </param>
        /// <returns> A string representing an equivalent regex pattern. </returns>
        internal static string WildcardToRegex(string pattern)
        {
            return "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        }

        /// <summary>
        ///		Check if a string represents a wildcard pattern.
        /// </summary>
        /// <param name="value"> The string to check. </param>
        /// <returns> True if the value is a wildcard pattern. </returns>
        internal static bool IsWildcardPattern(string value)
        {
            return value.IndexOfAny(s_wildcardCharacters) != -1;
        }
    }
}
