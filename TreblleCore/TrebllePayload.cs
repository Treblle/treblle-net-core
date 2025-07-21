using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Treblle.Net.Core
{
    public class Os
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        [JsonPropertyName("release")]
        public string? Release { get; set; }
        [JsonPropertyName("architecture")]
        public string? Architecture { get; set; }
    }

    public class Server
    {
        [JsonPropertyName("ip")]
        public string? Ip { get; set; }
        [JsonPropertyName("timezone")]
        public string? Timezone { get; set; }
        [JsonPropertyName("software")]
        public string? Software { get; set; }
        [JsonPropertyName("signature")]
        public string? Signature { get; set; }
        [JsonPropertyName("protocol")]
        public string? Protocol { get; set; }
        [JsonPropertyName("os")]
        public Os Os { get; set; } = new();
    }

    public class Language
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        [JsonPropertyName("version")]
        public string? Version { get; set; }
    }

    public class Request
    {
        [JsonPropertyName("timestamp")]
        public string? Timestamp { get; set; }
        [JsonPropertyName("ip")]
        public string? Ip { get; set; }
        [JsonPropertyName("url")]
        public string? Url { get; set; }
        [JsonPropertyName("route_path")]
        public string? RoutePath { get; set; }
        [JsonPropertyName("query")]
        public string? Query { get; set; }
        [JsonPropertyName("user_agent")]
        public string? UserAgent { get; set; }
        [JsonPropertyName("method")]
        public string? Method { get; set; }
        [JsonPropertyName("headers")]
        public Dictionary<string, object>? Headers { get; set; }
        [JsonPropertyName("body")]
        public object? Body { get; set; }
    }

    public class Error
    {
        [JsonPropertyName("source")]
        public string? Source { get; set; }
        [JsonPropertyName("type")]
        public string? Type { get; set; }
        [JsonPropertyName("message")]
        public string? Message { get; set; }
        [JsonPropertyName("file")]
        public string? File { get; set; }
        [JsonPropertyName("line")]
        public int Line { get; set; }
    }

    public class Response
    {
        [JsonPropertyName("headers")]
        public Dictionary<string, object>? Headers { get; set; }
        [JsonPropertyName("code")]
        public int Code { get; set; }
        [JsonPropertyName("size")]
        public long Size { get; set; }
        [JsonPropertyName("load_time")]
        public double LoadTime { get; set; }
        [JsonPropertyName("body")]
        public object? Body { get; set; }
    }

    public class Data
    {
        [JsonPropertyName("server")]
        public Server Server { get; set; } = new();
        [JsonPropertyName("language")]
        public Language Language { get; set; } = new();
        [JsonPropertyName("request")]
        public Request Request { get; set; } = new();
        [JsonPropertyName("response")]
        public Response Response { get; set; } = new();
        [JsonPropertyName("errors")]
        public List<Error> Errors { get; set; } = new();
    }

    public class TrebllePayload
    {
        [JsonPropertyName("api_key")]
        public string? ApiKey { get; set; }
        [JsonPropertyName("project_id")]
        public string? ProjectId { get; set; }
        [JsonPropertyName("version")]
        public string? Version { get; set; }
        [JsonPropertyName("sdk")]
        public string? Sdk { get; set; }
        [JsonPropertyName("data")]
        public Data Data { get; set; } = new();
    }
}
