using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using Treblle.Net.Core.Masking;

namespace Treblle.Net.Core;

internal sealed class TreblleService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false, // Smaller payloads
        PropertyNameCaseInsensitive = false, // Better performance
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping // Faster encoding
    };

    private static readonly string[] TreblleEndpoints = new[]
    {
        "https://rocknrolla.treblle.com",
        "https://punisher.treblle.com", 
        "https://sicario.treblle.com"
    };

    private static readonly Random Random = new();

    private readonly Dictionary<string, string> _maskingMap;
    private readonly HttpClient _httpClient;
    private readonly ILogger<TreblleService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly bool _disableMasking;
    private readonly bool _debugMode;
    private readonly string? _customIngressEndpoint;
    private readonly ThrottleState _throttleState = new();

    public TreblleService(
        IHttpClientFactory httpClientFactory,
        Dictionary<string, string> maskingMap,
        ILogger<TreblleService> logger,
        IServiceProvider serviceProvider,
        bool disableMasking = false,
        bool debugMode = false,
        string? customIngressEndpoint = null)
    {
        _httpClient = httpClientFactory.CreateClient("Treblle");
        _logger = logger;
        _maskingMap = maskingMap;
        _serviceProvider = serviceProvider;
        _disableMasking = disableMasking;
        _debugMode = debugMode;
        _customIngressEndpoint = customIngressEndpoint;
    }

    /// <summary>
    /// Checks if payloads should be throttled due to rate limiting.
    /// </summary>
    public bool ShouldThrottle() => _throttleState.ShouldThrottle();

    public async Task<HttpResponseMessage?> SendPayloadAsync(TrebllePayload payload)
    {
        try
        {
            var jsonPayload = JsonSerializer.Serialize(payload, TreblleJsonContext.Default.TrebllePayload);

            // Check if payload exceeds 5MB limit
            const int maxPayloadSizeBytes = 5 * 1024 * 1024; // 5MB
            if (Encoding.UTF8.GetByteCount(jsonPayload) > maxPayloadSizeBytes)
            {
                if (_debugMode)
                {
                    _logger.LogDebug("[TREBLLE]: Request payload size exceeds 5MB limit, replacing with size notification");
                }
                
                // Create a new payload with the large request body replaced
                var reducedPayload = new TrebllePayload
                {
                    ApiKey = payload.ApiKey,
                    ProjectId = payload.ProjectId,
                    Version = payload.Version,
                    Sdk = payload.Sdk,
                    Data = new Data
                    {
                        Server = payload.Data.Server,
                        Language = payload.Data.Language,
                        Request = new Request
                        {
                            Timestamp = payload.Data.Request.Timestamp,
                            Ip = payload.Data.Request.Ip,
                            Url = payload.Data.Request.Url,
                            RoutePath = payload.Data.Request.RoutePath,
                            Query = payload.Data.Request.Query,
                            UserAgent = payload.Data.Request.UserAgent,
                            Method = payload.Data.Request.Method,
                            Headers = payload.Data.Request.Headers,
                            Body = new { __message = "Request data payload was bigger than 5MB" }
                        },
                        Response = payload.Data.Response,
                        Errors = payload.Data.Errors
                    }
                };
                
                jsonPayload = JsonSerializer.Serialize(reducedPayload, TreblleJsonContext.Default.TrebllePayload);
            }

            var finalJsonPayload = _disableMasking 
                ? jsonPayload 
                : jsonPayload.Mask(_maskingMap, _serviceProvider, _logger);

            var endpoint = !string.IsNullOrWhiteSpace(_customIngressEndpoint)
                ? _customIngressEndpoint
                : TreblleEndpoints[Random.Next(TreblleEndpoints.Length)];
            
            var jsonBytes = Encoding.UTF8.GetBytes(finalJsonPayload ?? string.Empty);
            var compressedBytes = CompressData(jsonBytes);
            
            using HttpContent content = new ByteArrayContent(compressedBytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            
            // Only add gzip header if we actually compressed the data
            if (compressedBytes.Length < jsonBytes.Length)
            {
                content.Headers.ContentEncoding.Add("gzip");
            }
            
            using var httpResponseMessage = await _httpClient.PostAsync(endpoint, content);

            if (httpResponseMessage.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = ParseRetryAfter(httpResponseMessage);
                _throttleState.RecordThrottleResponse(retryAfter);

                if (_debugMode)
                {
                    _logger.LogDebug("[TREBLLE]: Rate limited (429) - backing off for {Seconds}s",
                        retryAfter ?? 0);
                }

                return httpResponseMessage;
            }

            if (httpResponseMessage.IsSuccessStatusCode)
            {
                _throttleState.RecordSuccess();
            }

            return httpResponseMessage;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "An error occurred while sending data to Treblle. --- Exception message: {Message}",
                ex.Message);

            return null;
        }
    }

    private static int? ParseRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            foreach (var value in values)
            {
                if (int.TryParse(value, out var seconds))
                {
                    return seconds;
                }
            }
        }
        return null;
    }

    private static byte[] CompressData(byte[] data)
    {
        // Skip compression for small payloads - not worth the overhead
        if (data.Length < 1024)
            return data;
            
        // Pre-size output stream to avoid buffer reallocations during compression
        using var output = new MemoryStream(data.Length / 3);
        using (var gzipStream = new GZipStream(output, CompressionLevel.Fastest))
        {
            gzipStream.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }
}