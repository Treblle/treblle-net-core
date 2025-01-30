using System.Text.RegularExpressions;

namespace Treblle.Net.Core.Masking;

public sealed class EmailMasker : DefaultStringMasker ,IStringMasker
{
    string IStringMasker.Mask(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        if (Regex.IsMatch(input, Constants.EmailPattern))
        {
            return Regex.Replace(
                       input,
                       @"([^@]+)",
                       match => new string('*', match.Length),
                       RegexOptions.None
                   );
        }

        return base.Mask(input);
    }
}