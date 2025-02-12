using System.Text.RegularExpressions;
using Treblle.Net.Core.Masking;

namespace Treblle.Runtime.Masking
{
    public sealed class PostalCodeMasker : DefaultStringMasker, IStringMasker
    {
        private const string _postalCodePattern = @"^(GIR 0AA|[A-PR-UWYZ]([0-9]{1,2}|([A-HK-Y][0-9]([0-9ABEHMNPRV-Y])?)|[0-9][A-HJKPS-UW]) [0-9][ABD-HJLNP-UW-Z]{2})$";
        private const string _mask = "$1***";

        public override bool IsPatternMatch(string input)
        {
            return Regex.IsMatch(input, _postalCodePattern);
        }

        string IStringMasker.Mask(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            if (Regex.IsMatch(input, _postalCodePattern))
            {
                // Replace the last part of the postal code with asterisks
                return Regex.Replace(input,
                                     _postalCodePattern,
                                     _mask);
            }

            return base.Mask(input);
        }


    }
}