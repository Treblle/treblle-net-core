using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Treblle.Net.Core.Masking;

public static class JsonMasker
{
    /// <summary>
    /// Masks a serialized payload against an ad-hoc field/masker map. Retained for callers
    /// outside the SDK; internally the rules are compiled once at registration.
    /// </summary>
    public static string? Mask(this string json, Dictionary<string, string> maskingMap, IServiceProvider serviceProvider, ILogger logger)
    {
        if (maskingMap is null || maskingMap.Count == 0)
        {
            return json;
        }

        return Mask(json, new MaskingRules(maskingMap), serviceProvider.GetRequiredService<MaskerFactory>());
    }

    internal static string? Mask(this string json, MaskingRules rules, MaskerFactory maskerFactory)
    {
        if (string.IsNullOrWhiteSpace(json) || rules.IsEmpty)
        {
            return json;
        }

        try
        {
            if (JsonNode.Parse(json) is not JsonObject jsonObject)
            {
                return json;
            }

            MaskFieldsFromJsonNode(jsonObject, rules, string.Empty, maskerFactory);

            return jsonObject.ToJsonString();
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static void MaskFieldsFromJsonNode(JsonNode? token, MaskingRules rules, string path, MaskerFactory maskerFactory)
    {
        if (token is JsonObject obj)
        {
            // Allocated only once something actually needs masking. Properties cannot be
            // reassigned while the object is being enumerated.
            List<KeyValuePair<string, JsonNode?>>? updates = null;

            foreach (var property in obj)
            {
                var currentPath = path.Length == 0 ? property.Key : path + "." + property.Key;

                // Metadata is supplied by the developer rather than by an API client, so it is
                // passed through verbatim - matching the Laravel SDK. Without this the path
                // walk below would redact data.metadata.email, .password, .secret and friends
                // off the default masking map. Ordinal full-path equality, so a user key named
                // "metadata" elsewhere (data.request.body.metadata) is unaffected.
                if (string.Equals(currentPath, "data.metadata", StringComparison.Ordinal))
                {
                    continue;
                }

                if (property.Value is JsonObject)
                {
                    MaskFieldsFromJsonNode(property.Value, rules, currentPath, maskerFactory);
                }
                else if (property.Value is JsonArray childArray)
                {
                    var arrayMaskerName = rules.FindMaskerName(currentPath);

                    if (arrayMaskerName is not null)
                    {
                        var masker = maskerFactory.GetMasker(arrayMaskerName);

                        for (int i = 0; i < childArray.Count; i++)
                        {
                            childArray[i] = JsonValue.Create(masker.Mask(childArray[i]?.ToString() ?? string.Empty));
                        }
                    }
                    else
                    {
                        MaskFieldsFromJsonNode(property.Value, rules, currentPath, maskerFactory);
                    }
                }
                else
                {
                    var maskerName = rules.FindMaskerName(currentPath);

                    if (maskerName is not null)
                    {
                        var masked = maskerFactory.GetMasker(maskerName).Mask(property.Value?.ToString() ?? string.Empty);

                        updates ??= new List<KeyValuePair<string, JsonNode?>>();
                        updates.Add(new KeyValuePair<string, JsonNode?>(property.Key, JsonValue.Create(masked)));
                    }
                }
            }

            if (updates is not null)
            {
                foreach (var update in updates)
                {
                    obj[update.Key] = update.Value;
                }
            }
        }
        else if (token is JsonArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                MaskFieldsFromJsonNode(array[i], rules, path, maskerFactory);
            }
        }
    }
}
