using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Treblle.Net.Core.Masking;

public static class JsonMasker
{
    public static string? Mask(this string json, Dictionary<string, string> maskingMap, string mask, IServiceProvider serviceProvider)
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

        //var compiledPatterns = CompilePatterns(maskingPatterns);
        MaskFieldsFromJToken(jsonObject, maskingMap, mask, new List<string>(), serviceProvider);

        return jsonObject.ToString();
    }

    private static void MaskFieldsFromJToken(JToken? token, Dictionary<string, string> maskingMap, string mask, List<string> path, IServiceProvider serviceProvider)
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
                    MaskFieldsFromJToken(prop.Value, maskingMap, mask, path.Concat(new[] { prop.Name }).ToList(), serviceProvider);
                }
                else
                {
                    foreach (KeyValuePair<string, string> map in maskingMap)
                    {

                        if (shouldMap(map.Key, currentPath))
                        {
                            var masker = serviceProvider.GetKeyedService<IStringMasker>(map.Value);

                            if (masker is null)
                                throw new NullReferenceException("Masker can not be null.");

                            prop.Value = masker.Mask(prop.Value.ToString());
                            break;
                        }
                    }
                }
            }

            //MaskFieldsFromJToken(jToken, maskingMap, mask, path, serviceProvider);
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