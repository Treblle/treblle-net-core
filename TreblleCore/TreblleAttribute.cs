using System;

namespace Treblle.Net.Core;

public sealed class TreblleAttribute : Attribute
{
    public string? ApiKey { get; set; }

    public TreblleAttribute(string? apiKey = null)
    {
        ApiKey = apiKey;
    }
}
