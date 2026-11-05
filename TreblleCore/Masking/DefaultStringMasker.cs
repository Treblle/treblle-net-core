namespace Treblle.Net.Core.Masking;

public class DefaultStringMasker : IStringMasker
{
    public virtual bool IsPatternMatch(string input)
    {
        if (input is null)
            return false;

        return false;
    }

    public virtual string Mask(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        // Was Regex.Replace(input, ".", "*"), which ran one regex match per character —
        // masking a 100 KB field cost 100k matches to produce a run of asterisks.
        return new string('*', input.Length);
    }
}
