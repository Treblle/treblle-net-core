using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Treblle.Net.Core
{
    public static class JsonMasker
    {
        public static string? Mask(this string json, List<string> blacklist, string mask)
        {
            if (string.IsNullOrWhiteSpace(json) || blacklist.Count == 0)
            {
                return json;
            }

            var jsonObject = JsonConvert.DeserializeObject(json) as JObject;

            MaskFieldsFromJToken(jsonObject, blacklist, mask);

            var result = jsonObject?.ToString();

            return result;
        }

        private static void MaskFieldsFromJToken(JToken? token, List<string> blacklist, string mask)
        {
            if (token is not JContainer container)
            {
                return;
            }

            var removeList = new List<JToken>();

            foreach (var jToken in container.Children())
            {
                if (jToken is JProperty prop)
                {
                    var matching = blacklist.Exists(item =>
                        Regex.IsMatch(prop.Path, "(?<=\\.)(\\b" + item + "\\b)(?=\\.?)", RegexOptions.IgnoreCase));

                    if (matching)
                    {
                        removeList.Add(jToken);
                    }
                }

                MaskFieldsFromJToken(jToken, blacklist, mask);
            }

            foreach (var el in removeList)
            {
                var prop = (JProperty)el;
                prop.Value = mask;
            }
        }
    }
}
