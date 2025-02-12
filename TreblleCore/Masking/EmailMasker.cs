using System.Text.RegularExpressions;

namespace Treblle.Net.Core.Masking;

public sealed class EmailMasker : DefaultStringMasker ,IStringMasker
{
    private const string _emailPattern = @"^[a-zA-Z0-9._-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,4}$";
    private const string _patternToReplace = @"([^@]+)";

    public override bool IsPatternMatch(string input)
    {
        return Regex.IsMatch(input, _emailPattern);
    }
    string IStringMasker.Mask(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        if (Regex.IsMatch(input, _emailPattern))
        {
            return Regex.Replace(
                       input,
                       _patternToReplace,
                       match => new string('*', match.Length),
                       RegexOptions.None
                   );
        }

        return base.Mask(input);
    }
}