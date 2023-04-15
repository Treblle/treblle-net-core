using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Treblle.Net.Core
{
    public static class JsonMasker
    {
        public static string? Mask(this string json, string[] blacklist, string mask)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return json;
            }

            if (!blacklist.Any())
            {
                return json;
            }

            if (blacklist.Any() == false)
            {
                return json;
            }

            var jsonObject = JsonConvert.DeserializeObject(json) as JObject;

            MaskFieldsFromJToken(jsonObject, blacklist, mask);

            var result = jsonObject?.ToString();

            return result;
        }

        private static void MaskFieldsFromJToken(JToken? token, string[] blacklist, string mask)
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
                    var matching = blacklist.Any(item =>
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
