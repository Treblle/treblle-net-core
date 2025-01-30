
using System.Text.RegularExpressions;

namespace Treblle.Net.Core.Masking
{
    public sealed class SocialSecurityMasker : DefaultStringMasker, IStringMasker
    {
        public string Mask(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            if (Regex.IsMatch(input, Constants.SocialSecurityPattern))
            {
                // Replace the first part of the SSN with asterisks
                return Regex.Replace(input, @"^(\d{3}-\d{2}-)(\d{4})$", "***-**-$2");
            }

            return base.Mask(input);
        }
    }
}
