using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;


namespace Treblle.Net.Core
{
    internal class TreblleApiService
    {
        private string _apiKey = "";
        private List<string> _sensitiveWords = new List<string>() {
        "password",
        "pwd",
        "secret",
        "password_confirmation",
        "passwordConfirmation",
        "cc",
        "card_number",
        "cardNumber",
        "ccv",
        "ssn",
        "credit_score",
        "creditScore"
    };

        public TreblleApiService()
        {
            try
            {
                _apiKey = ConfigurationManager.AppSettings["TreblleApiKey"];
            }
            catch (Exception ex)
            {
                if (ex is ConfigurationException)
                {
                    Logger.LogMessage("An error occured while trying to read the configuration file. Check if app.config is formatted properly. --- Exception message: " + ex.Message, LogMessageType.Error);
                }
                else
                {
                    Logger.LogMessage("An error occured while intercepting request. --- Exception message: " + ex.Message, LogMessageType.Error);
                }
            }
        }

        public WebResponse SendPayload(TrebllePayload payload)
        {
            try
            {
                var json = JsonConvert.SerializeObject(payload);

                var additionalFieldsToMask = ConfigurationManager.AppSettings["AdditionalFieldsToMask"];
                if (!String.IsNullOrWhiteSpace(additionalFieldsToMask))
                {
                    var additionalFields = additionalFieldsToMask.Split(',');
                    if (additionalFields.Any())
                    {
                        var list = additionalFields.ToList();
                        _sensitiveWords.AddRange(list);
                    }
                }

                var maskedJson = json.Mask(_sensitiveWords.ToArray(), "*****");

                var httpWebRequest = (HttpWebRequest)WebRequest.Create("https://rocknrolla.treblle.com");
                httpWebRequest.ContentType = "application/json";
                httpWebRequest.Method = "POST";
                httpWebRequest.Headers.Add("x-api-key", _apiKey);

                using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
                {
                    streamWriter.Write(maskedJson);
                }

                var httpResponse = httpWebRequest.GetResponse();
                return httpResponse;
            }
            catch (Exception ex)
            {
                Logger.LogMessage("An error occured while sending data to Treblle. --- Exception message: " + ex.Message, LogMessageType.Error);
                return null;
            }
        }
    }
}
