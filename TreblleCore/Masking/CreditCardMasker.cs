using System;
using System.Text.RegularExpressions;
using Treblle.Net.Core.Masking;

namespace Treblle.Runtime.Masking
{
    public sealed class CreditCardMasker : DefaultStringMasker, IStringMasker
    {

        public string Mask(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            if (Regex.IsMatch(input, Constants.CreditCardPattern))
            {
                // Remove non-digit characters from the input
                string sanitizedCard = Regex.Replace(input, @"\D", "");

                // If the result isn't 16 digits long, return original
                if (sanitizedCard.Length != 16)
                {
                    return input;
                }

                // Return the masked card
                return $"****-****-****-{sanitizedCard.Substring(sanitizedCard.Length - 4)}";
            }

            return base.Mask(input);
        }
    }
}
