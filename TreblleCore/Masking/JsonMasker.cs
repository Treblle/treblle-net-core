using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Treblle.Net.Core.Masking;

public static class JsonMasker
{
    public static string? Mask(this string json, Dictionary<string, string> maskingMap, IServiceProvider serviceProvider, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(json) || maskingMap.Count == 0)
        {
            return json;
        }

        try
        {
            var jsonObject = JsonNode.Parse(json) as JsonObject;
            if (jsonObject == null)
            {
                return json;
            }

            MaskFieldsFromJsonNode(jsonObject, maskingMap, new List<string>(), serviceProvider, logger);

            return jsonObject.ToJsonString();
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static void MaskFieldsFromJsonNode(JsonNode? token, Dictionary<string, string> maskingMap, List<string> path, IServiceProvider serviceProvider, ILogger logger)
    {
        if (token is JsonObject obj)
        {
            var propertiesToUpdate = new Dictionary<string, JsonNode?>();
            
            foreach (var property in obj)
            {
                var currentPath = string.Join(".", path.Concat(new[] { property.Key }));

                if (property.Value is JsonObject || property.Value is JsonArray)
                {
                    MaskFieldsFromJsonNode(property.Value, maskingMap, path.Concat(new[] { property.Key }).ToList(), serviceProvider, logger);
                }
                else
                {
                    var maskedValue = MaskPropertyValue(property.Value, currentPath, maskingMap, serviceProvider, logger);
                    if (maskedValue != property.Value)
                    {
                        propertiesToUpdate[property.Key] = maskedValue;
                    }
                }
            }
            
            // Update the masked properties
            foreach (var update in propertiesToUpdate)
            {
                obj[update.Key] = update.Value;
            }
        }
        else if (token is JsonArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                MaskFieldsFromJsonNode(array[i], maskingMap, path, serviceProvider, logger);
            }
        }
    }

    private static JsonNode? MaskPropertyValue(JsonNode? propertyValue, string currentPath, Dictionary<string, string> maskingMap, IServiceProvider serviceProvider, ILogger logger)
    {
        bool isValueMasked = false;
        string? maskedValue = null;

        foreach (var map in maskingMap)
        {
            if (ShouldMask(map.Key, currentPath))
            {
                var masker = serviceProvider.GetKeyedService<IStringMasker>(map.Value);
                if (masker != null)
                {
                    maskedValue = masker.Mask(propertyValue?.ToString());
                    isValueMasked = true;
                    break;
                }
                else
                {
                    logger.LogError($"Could not resolve masker for field {currentPath}");
                }
            }
        }

        if (!isValueMasked)
        {
            foreach (DefaultStringMasker masker in serviceProvider.GetServices(typeof(DefaultStringMasker)))
            {
                if (masker.IsPatternMatch(propertyValue?.ToString()))
                {
                    maskedValue = masker.Mask(propertyValue?.ToString());
                    break;
                }
            }
        }

        return maskedValue != null ? JsonValue.Create(maskedValue) : propertyValue;
    }

    private static bool ShouldMask(string sensitiveWord, string path)
    {
        sensitiveWord = sensitiveWord.ToLower();
        path = path.ToLower();
        return sensitiveWord.Contains(".")
            ? (path.Contains(sensitiveWord) || (sensitiveWord.EndsWith("*") && path.Contains(sensitiveWord.Substring(0, sensitiveWord.Length - 1))))
            : (path.Equals(sensitiveWord) || path.Contains($".{sensitiveWord}"));
    }
}