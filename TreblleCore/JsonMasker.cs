using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Treblle.Net.Core;

public static class JsonMasker
{
    public static string? Mask(this string json, HashSet<string> blacklist, string mask)
    {
        if (string.IsNullOrWhiteSpace(json) || blacklist.Count == 0)
        {
            return json;
        }

        var jsonObject = JsonConvert.DeserializeObject(json) as JObject;
        var blacklistSet = new HashSet<string>(blacklist);

        MaskFieldsFromJToken(jsonObject, blacklistSet, mask);

        return jsonObject?.ToString();
    }

    private static void MaskFieldsFromJToken(JToken? token, HashSet<string> blacklist, string mask)
    {
        if (token is not JContainer container)
        {
            return;
        }

        foreach (var jToken in container.Children())
        {
            if (jToken is JProperty prop)
            {
                if (blacklist.Contains(prop.Name))
                {
                    prop.Value = mask;
                }
            }

            MaskFieldsFromJToken(jToken, blacklist, mask);
        }
    }
}