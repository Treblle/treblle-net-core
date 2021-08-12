using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Treblle.Net.Core
{
    public class Os
    {
        [JsonProperty("name")]
        public string Name { get; set; }
        [JsonProperty("release")]
        public string Release { get; set; }
        [JsonProperty("architecture")]
        public string Architecture { get; set; }
    }

    public class Server
    {
        [JsonProperty("ip")]
        public string Ip { get; set; }
        [JsonProperty("timezone")]
        public string Timezone { get; set; }
        [JsonProperty("software")]
        public string Software { get; set; }
        [JsonProperty("signature")]
        public string Signature { get; set; }
        [JsonProperty("protocol")]
        public string Protocol { get; set; }
        [JsonProperty("os")]
        public Os Os { get; set; }
    }

    public class Language
    {
        [JsonProperty("name")]
        public string Name { get; set; }
        [JsonProperty("version")]
        public string Version { get; set; }
    }

    public class Request
    {
        [JsonProperty("timestamp")]
        public string Timestamp { get; set; }
        [JsonProperty("ip")]
        public string Ip { get; set; }
        [JsonProperty("url")]
        public string Url { get; set; }
        [JsonProperty("user_agent")]
        public string UserAgent { get; set; }
        [JsonProperty("method")]
        public string Method { get; set; }
        [JsonProperty("headers")]
        public dynamic Headers { get; set; }
        [JsonProperty("body")]
        public dynamic Body { get; set; }
    }

    public class Error
    {
        [JsonProperty("source")]
        public string Source { get; set; }
        [JsonProperty("type")]
        public string Type { get; set; }
        [JsonProperty("message")]
        public string Message { get; set; }
        [JsonProperty("file")]
        public string File { get; set; }
        [JsonProperty("line")]
        public int Line { get; set; }
    }

    public class Response
    {
        [JsonProperty("headers")]
        public dynamic Headers { get; set; }
        [JsonProperty("code")]
        public int Code { get; set; }
        [JsonProperty("size")]
        public long Size { get; set; }
        [JsonProperty("load_time")]
        public double LoadTime { get; set; }
        [JsonProperty("body")]
        public dynamic Body { get; set; }
    }

    public class Data
    {
        [JsonProperty("server")]
        public Server Server { get; set; }
        [JsonProperty("language")]
        public Language Language { get; set; }
        [JsonProperty("request")]
        public Request Request { get; set; }
        [JsonProperty("response")]
        public Response Response { get; set; }
        [JsonProperty("errors")]
        public List<Error> Errors { get; set; }
    }

    public class TrebllePayload
    {
        [JsonProperty("api_key")]
        public string ApiKey { get; set; }
        [JsonProperty("project_id")]
        public string ProjectId { get; set; }
        [JsonProperty("version")]
        public string Version { get; set; }
        [JsonProperty("sdk")]
        public string Sdk { get; set; }
        [JsonProperty("data")]
        public Data Data { get; set; }
    }
}
