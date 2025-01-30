using System.Text.RegularExpressions;

namespace Treblle.Net.Core.Masking;


public class DefaultStringMasker : IStringMasker
{
    public virtual string Mask(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        return Regex.Replace(input, ".", "*", RegexOptions.Singleline);
    }
}
