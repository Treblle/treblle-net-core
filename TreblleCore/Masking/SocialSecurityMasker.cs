using System.Text.RegularExpressions;

namespace Treblle.Net.Core.Masking
{
    public sealed class SocialSecurityMasker : DefaultStringMasker, IStringMasker
    {
        private const string _socialSecurityPattern = @"^\d{3}-\d{2}-\d{4}$";
        private const string _patternToReplace = @"^(\d{3}-\d{2}-)(\d{4})$";
        private const string _mask = "***-**-$2";

        public override bool IsPatternMatch(string input)
        {
            return Regex.IsMatch(input, _socialSecurityPattern);
        }

        public string Mask(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            if (Regex.IsMatch(input, _socialSecurityPattern))
            {
                // Replace the first part of the SSN with asterisks
                return Regex.Replace(input,
                                     _patternToReplace,
                                     _mask);
            }

            return base.Mask(input);
        }
    }
}
