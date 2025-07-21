using System;
using System.Collections.Generic;
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

    private readonly Dictionary<string, string> _maskingMap;
    private readonly HttpClient _httpClient;
    private readonly ILogger<TreblleService> _logger;
    private readonly IServiceProvider _serviceProvider;

    public TreblleService(
        IHttpClientFactory httpClientFactory,
        Dictionary<string, string> maskingMap,
        ILogger<TreblleService> logger,
        IServiceProvider serviceProvider)
    {
        _httpClient = httpClientFactory.CreateClient("Treblle");
        _logger = logger;
        _maskingMap = maskingMap;
        _serviceProvider = serviceProvider;
    }

    public async Task<HttpResponseMessage?> SendPayloadAsync(TrebllePayload payload)
    {
        try
        {
            var jsonPayload = JsonSerializer.Serialize(payload, JsonOptions);

            // Check if payload exceeds 5MB limit
            const int maxPayloadSizeBytes = 5 * 1024 * 1024; // 5MB
            if (Encoding.UTF8.GetByteCount(jsonPayload) > maxPayloadSizeBytes)
            {
                _logger.LogWarning("Request payload size exceeds 5MB limit, replacing with size notification");
                
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
                
                jsonPayload = JsonSerializer.Serialize(reducedPayload, JsonOptions);
            }

            var maskedJsonPayload = jsonPayload.Mask(_maskingMap, _serviceProvider, _logger);

            using HttpContent content = new StringContent(maskedJsonPayload, Encoding.UTF8, "application/json");
            using var httpResponseMessage = await _httpClient.PostAsync(string.Empty, content);
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
}