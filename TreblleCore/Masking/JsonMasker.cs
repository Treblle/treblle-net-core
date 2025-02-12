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
        if (token is not JContainer container)
        {
            return;
        }

        foreach (var jToken in container.Children())
        {
            if (jToken is JProperty prop)
            {
                var currentPath = string.Join(".", path.Concat(new[] { prop.Name }));

                if (prop.Value is JContainer)
                {
                    MaskFieldsFromJToken(prop.Value, maskingMap, path.Concat(new[] { prop.Name }).ToList(), serviceProvider, logger);
                }
                else if (prop.Value is not null)
                {
                    bool isValueMasked = false;
                    foreach (KeyValuePair<string, string> map in maskingMap)
                    {

                        if (shouldMap(map.Key, currentPath))
                        {
                            var masker = serviceProvider.GetKeyedService<IStringMasker>(map.Value);

                            if (masker is not null)
                            {
                                prop.Value = masker.Mask(prop.Value.ToString());
                                isValueMasked = true;
                                break;
                            }
                            else
                            {
                                logger.LogError($"Could not resolve masker for field {currentPath}");
                            }
                        }
                    }

                    // if the value is not masked go over mapping once again to check if value matches any pattern
                    if (!isValueMasked)
                    {
                        foreach (DefaultStringMasker masker in serviceProvider.GetServices(typeof(DefaultStringMasker)))
                        {
                            if (masker.IsPatternMatch(prop.Value.ToString()))
                            {
                                prop.Value = masker.Mask(prop.Value.ToString());
                                break;
                            }
                        }
                    }
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