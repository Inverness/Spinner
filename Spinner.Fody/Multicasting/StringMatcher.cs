using System.Text.RegularExpressions;

namespace Spinner.Fody.Multicasting
{
    /// <summary>
    /// Matches strings based on a regex or wildcard pattern.
    /// </summary>
    internal abstract class StringMatcher
    {
        internal const string RegexPrefix = "regex:";

        public static readonly StringMatcher AnyMatcher = new AnyMatcherType();

        public abstract bool IsMatch(string value);

        public static StringMatcher Create(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return AnyMatcher;

            if (pattern.StartsWith(RegexPrefix))
                return new RegexMatcher(new Regex(pattern.Substring(RegexPrefix.Length + 1)));

            int wcindex = pattern.IndexOf('*');
            int swcindex = pattern.IndexOf('?');

            if (wcindex == -1 && swcindex == -1)
                return new EqualityMatcher(pattern);

            // If a string only has a wildcard at the end, use a simpler prefix matcher that does not require a 
            // regex object.
            if (wcindex == pattern.Length - 1 && swcindex == -1)
                return new PrefixMatcher(pattern.Substring(0, pattern.Length - 1));

            return new RegexMatcher(new Regex(StringUtility.WildcardToRegex(pattern)));
        }

        private sealed class AnyMatcherType : StringMatcher
        {
            public override bool IsMatch(string value)
            {
                return true;
            }
        }

        private sealed class EqualityMatcher : StringMatcher
        {
            private readonly string _value;

            public EqualityMatcher(string value)
            {
                _value = value;
            }

            public override bool IsMatch(string value)
            {
                return value == _value;
            }
        }

        private sealed class PrefixMatcher : StringMatcher
        {
            private readonly string _value;

            public PrefixMatcher(string value)
            {
                _value = value;
            }

            public override bool IsMatch(string value)
            {
                return value.StartsWith(_value);
            }
        }

        private sealed class RegexMatcher : StringMatcher
        {
            private readonly Regex _regex;

            public RegexMatcher(Regex regex)
            {
                _regex = regex;
            }

            public override bool IsMatch(string value)
            {
                return _regex.IsMatch(value);
            }
        }
    }
}