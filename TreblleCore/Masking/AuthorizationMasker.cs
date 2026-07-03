using System;

namespace Treblle.Net.Core.Masking;

internal sealed class AuthorizationMasker : IStringMasker
{
    private static readonly string[] SchemesWithToken = new[] { "bearer", "basic", "digest" };

    public bool IsPatternMatch(string input) => false;

    public string Mask(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var spaceIndex = input.IndexOf(' ');
        if (spaceIndex > 0)
        {
            var scheme = input[..spaceIndex];
            var token  = input[(spaceIndex + 1)..];

            if (Array.IndexOf(SchemesWithToken, scheme.ToLowerInvariant()) >= 0)
                return scheme + " " + new string('*', token.Length);
        }

        return new string('*', input.Length);
    }
}
