using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Treblle.Net.Core
{
    public static class JsonMasker
    {
        public static string Mask(this string json, string[] blacklist, string mask)
        {
            if (string.IsNullOrWhiteSpace(json) == true)
            {
                return json;
            }

            if (blacklist == null)
            {
                return json;
            }

            if (blacklist.Any() == false)
            {
                return json;
            }

            var jsonObject = (JObject)JsonConvert.DeserializeObject(json);
            MaskFieldsFromJToken(jsonObject, blacklist, mask);

            var result = jsonObject.ToString();

            return result;
        }

        private static void MaskFieldsFromJToken(JToken token, string[] blacklist, string mask)
        {
            JContainer container = token as JContainer;
            if (container == null)
            {
                return; // abort recursive
            }

            List<JToken> removeList = new List<JToken>();
            foreach (JToken jtoken in container.Children())
            {
                if (jtoken is JProperty prop)
                {
                    var matching = blacklist.Any(item =>
                    {
                        return Regex.IsMatch(prop.Path, "(?<=\\.)(\\b" + item + "\\b)(?=\\.?)", RegexOptions.IgnoreCase);
                    });

                    if (matching)
                    {
                        removeList.Add(jtoken);
                    }
                }

                // call recursive 
                MaskFieldsFromJToken(jtoken, blacklist, mask);
            }

            // replace 
            foreach (JToken el in removeList)
            {
                var prop = (JProperty)el;
                prop.Value = mask;
            }
        }
    }
}
