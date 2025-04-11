using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Treblle.Net.Core.Masking;

public static class JsonMasker
{
    public static string? Mask(this string json, Dictionary<string, string> maskingMap, IServiceProvider serviceProvider, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(json) || maskingMap.Count == 0)
        {
            return json;
        }

        var jsonObject = JsonConvert.DeserializeObject(json) as JObject;
        if (jsonObject == null)
        {
            return json;
        }

        MaskFieldsFromJToken(jsonObject, maskingMap, new List<string>(), serviceProvider, logger);

        return jsonObject.ToString();
    }


    private static void MaskFieldsFromJToken(JToken? token, Dictionary<string, string> maskingMap, List<string> path, IServiceProvider serviceProvider, ILogger logger)
    {
        if (token is JObject obj)
        {
            foreach (var property in obj.Properties())
            {
                var currentPath = string.Join(".", path.Concat(new[] { property.Name }));

                if (property.Value is JObject || property.Value is JArray)
                {
                    MaskFieldsFromJToken(property.Value, maskingMap, path.Concat(new[] { property.Name }).ToList(), serviceProvider, logger);
                }
                else
                {
                    maskProperty(property, currentPath, maskingMap, serviceProvider, logger);
                }
            }
        }
        else if (token is JArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                MaskFieldsFromJToken(array[i], maskingMap, path, serviceProvider, logger);
            }
        }
    }

    private static void maskProperty(JProperty property, string currentPath, Dictionary<string, string> maskingMap, IServiceProvider serviceProvider, ILogger logger)
    {
        bool isValueMasked = false;

        foreach (var map in maskingMap)
        {
            if (shouldMap(map.Key, currentPath))
            {
                var masker = serviceProvider.GetKeyedService<IStringMasker>(map.Value);
                if (masker != null)
                {
                    property.Value = masker.Mask(property.Value?.ToString());
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
                if (masker.IsPatternMatch(property.Value?.ToString()))
                {
                    property.Value = masker.Mask(property.Value?.ToString());
                    break;
                }
            }
        }
    }

    private static bool shouldMap(string sensitiveWord, string path)
    {
        sensitiveWord = sensitiveWord.ToLower();
        path = path.ToLower();
        return sensitiveWord.Contains(".")
            ? (path.Contains(sensitiveWord) || (sensitiveWord.EndsWith("*") && path.Contains(sensitiveWord.Substring(0, sensitiveWord.Length - 1))))
            : (path.Equals(sensitiveWord) || path.Contains($".{sensitiveWord}"));
    }
}