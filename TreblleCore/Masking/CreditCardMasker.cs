using System.Text.RegularExpressions;
using Treblle.Net.Core.Masking;

namespace Treblle.Runtime.Masking
{
    public sealed class CreditCardMasker : DefaultStringMasker, IStringMasker
    {
        private const string _creditCardPattern = @"\d{4}-?\d{4}-?\d{4}-?\d{4}";
        private const string _creditCardMask = "****-****-****-";

        public override bool IsPatternMatch(string input)
        {
            return Regex.IsMatch(input, _creditCardPattern);
        }

        public string Mask(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            if (Regex.IsMatch(input, _creditCardPattern))
            {
                // Remove non-digit characters from the input
                string sanitizedCard = Regex.Replace(input, @"\D", "");

                // If the result isn't 16 digits long, return original
                if (sanitizedCard.Length != 16)
                {
                    return input;
                }

                // Return the masked card
                return $"{_creditCardMask}{sanitizedCard.Substring(sanitizedCard.Length - 4)}";
            }

            return base.Mask(input);
        }
    }
}
