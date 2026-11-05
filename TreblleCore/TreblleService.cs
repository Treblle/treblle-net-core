using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Treblle.Net.Core.Masking;

namespace Treblle.Net.Core;

internal sealed class TreblleService
{
    /// <summary>
    /// The single default ingress host.
    /// </summary>
    /// <remarks>
    /// Previously one of three hostnames was picked at random per payload. Those names all
    /// resolve to the same load balancer, so rotating per payload bought no balancing: it
    /// fragmented the connection pool across three hosts and forced fresh TLS handshakes.
    /// Set <see cref="TreblleOptions.CustomIngressEndpoint"/> (or
    /// TREBLLE_CUSTOM_INGRESS_ENDPOINT) to send elsewhere, for example
    /// https://ingress-eu.treblle.com.
    /// </remarks>
    private const string DefaultIngressEndpoint = "https://ingress.treblle.com";

    private const int MaxPayloadSizeBytes = 5 * 1024 * 1024;

    /// <summary>Below this, gzip costs more than it saves.</summary>
    private const int MinimumCompressionSizeBytes = 1024;

    private readonly MaskingRules _maskingRules;
    private readonly MaskerFactory _maskerFactory;
    private readonly HttpClient _httpClient;
    private readonly ILogger<TreblleService> _logger;
    private readonly bool _disableMasking;
    private readonly bool _debugMode;
    private readonly string _endpoint;
    private readonly ThrottleState _throttleState = new();

    public TreblleService(
        IHttpClientFactory httpClientFactory,
        MaskingRules maskingRules,
        MaskerFactory maskerFactory,
        ILogger<TreblleService> logger,
        bool disableMasking = false,
        bool debugMode = false,
        string? customIngressEndpoint = null)
    {
        _httpClient = httpClientFactory.CreateClient("Treblle");
        _logger = logger;
        _maskingRules = maskingRules;
        _maskerFactory = maskerFactory;
        _disableMasking = disableMasking;
        _debugMode = debugMode;
        _endpoint = string.IsNullOrWhiteSpace(customIngressEndpoint)
            ? DefaultIngressEndpoint
            : customIngressEndpoint!;
    }

    /// <summary>
    /// Checks if payloads should be throttled due to rate limiting.
    /// </summary>
    public bool ShouldThrottle() => _throttleState.ShouldThrottle();

    public async Task SendPayloadAsync(TrebllePayload payload)
    {
        try
        {
            var jsonPayload = JsonSerializer.Serialize(payload, TreblleJsonContext.Default.TrebllePayload);

            if (Encoding.UTF8.GetByteCount(jsonPayload) > MaxPayloadSizeBytes)
            {
                if (_debugMode)
                {
                    _logger.LogDebug("[TREBLLE]: Request payload size exceeds 5MB limit, replacing with size notification");
                }

                // Replaced in place. Rebuilding the payload by hand-copying every property of
                // Request silently dropped any field added to it later.
                //
                // Dictionary, not an anonymous type: anonymous types are not declared in
                // TreblleJsonContext, and the source-generated serializer throws on any
                // runtime type it has no metadata for.
                payload.Data.Request.Body = new Dictionary<string, object>
                {
                    ["__message"] = "Request data payload was bigger than 5MB"
                };

                jsonPayload = JsonSerializer.Serialize(payload, TreblleJsonContext.Default.TrebllePayload);
            }

            var finalJsonPayload = _disableMasking
                ? jsonPayload
                : jsonPayload.Mask(_maskingRules, _maskerFactory);

            var jsonBytes = Encoding.UTF8.GetBytes(finalJsonPayload ?? string.Empty);
            var compressed = TryCompress(jsonBytes, out var bodyBytes);

            using HttpContent content = new ByteArrayContent(bodyBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            if (compressed)
            {
                content.Headers.ContentEncoding.Add("gzip");
            }

            using var httpResponseMessage = await _httpClient.PostAsync(_endpoint, content);

            if (httpResponseMessage.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = ParseRetryAfter(httpResponseMessage);
                _throttleState.RecordThrottleResponse(retryAfter);

                if (_debugMode)
                {
                    _logger.LogDebug("[TREBLLE]: Rate limited (429) - backing off for {Seconds}s",
                        retryAfter ?? 0);
                }

                return;
            }

            if (httpResponseMessage.IsSuccessStatusCode)
            {
                _throttleState.RecordSuccess();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "An error occurred while sending data to Treblle. --- Exception message: {Message}",
                ex.Message);
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

    /// <summary>
    /// Gzips <paramref name="data"/> when it is large enough to be worth it. Returns true only
    /// when <paramref name="result"/> actually holds compressed bytes, which is the only case
    /// that may set Content-Encoding.
    /// </summary>
    /// <remarks>
    /// The header used to be decided by comparing lengths, so an incompressible payload that
    /// gzip made marginally larger was sent compressed but unmarked — the ingress received
    /// bytes it could not parse.
    /// </remarks>
    private static bool TryCompress(byte[] data, out byte[] result)
    {
        if (data.Length < MinimumCompressionSizeBytes)
        {
            result = data;
            return false;
        }

        // Pre-size output stream to avoid buffer reallocations during compression
        using var output = new MemoryStream(data.Length / 3);
        using (var gzipStream = new GZipStream(output, CompressionLevel.Fastest))
        {
            gzipStream.Write(data, 0, data.Length);
        }

        result = output.ToArray();
        return true;
    }
}
