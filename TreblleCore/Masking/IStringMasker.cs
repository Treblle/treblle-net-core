namespace Treblle.Net.Core.Masking;

public interface IStringMasker
{
    bool IsPatternMatch(string input);

    string Mask(string input);
}
