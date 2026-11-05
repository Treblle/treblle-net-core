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
            if (input is null)
                return false;

            return Regex.IsMatch(input, _creditCardPattern);
        }

        public override string Mask(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            if (Regex.IsMatch(input, _creditCardPattern))
            {
                // Remove non-digit characters from the input
                string sanitizedCard = Regex.Replace(input, @"\D", "");

                // Not a 16-digit PAN after all — a 17-to-19 digit card number, or a card
                // number surrounded by other digits. The field was still configured as a card
                // field, so fall back to full masking. Returning the input here handed the
                // value to the ingress in the clear.
                if (sanitizedCard.Length != 16)
                {
                    return base.Mask(input);
                }

                // Return the masked card
                return $"{_creditCardMask}{sanitizedCard.Substring(sanitizedCard.Length - 4)}";
            }

            return base.Mask(input);
        }
    }
}
