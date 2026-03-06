using System;

namespace Treblle.Net.Core;

public sealed class TreblleAttribute : Attribute
{
    public string? ApiKey { get; set; }

    public TreblleAttribute(string? apiKey = null, string? keyEnvVarName = null)
    {
        ApiKey = keyEnvVarName != null 
            ? Environment.GetEnvironmentVariable(keyEnvVarName)
            : apiKey;
    }
}
