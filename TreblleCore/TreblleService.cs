using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Treblle.Net.Core.Masking;

namespace Treblle.Net.Core;

internal sealed class TreblleService
{
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
            var jsonPayload = JsonConvert.SerializeObject(payload);

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