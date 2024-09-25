using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Treblle.Net.Core;

internal sealed class TreblleService
{
    private readonly HashSet<string> _sensitiveWords;
    private readonly HttpClient _httpClient;
    private readonly TreblleOptions _treblleOptions;
    private readonly ILogger<TreblleService> _logger;

    public TreblleService(IHttpClientFactory httpClientFactory, 
        IOptions<TreblleOptions> treblleOptions, 
        HashSet<string> sensitiveWords,
        ILogger<TreblleService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Treblle");
        _logger = logger;
        _treblleOptions = treblleOptions.Value;
        _sensitiveWords = sensitiveWords;
    }

    public async Task<HttpResponseMessage?> SendPayloadAsync(TrebllePayload payload)
    {
        try
        {
            var jsonPayload = JsonConvert.SerializeObject(payload);

            var maskedJsonPayload = jsonPayload.Mask(_sensitiveWords, "*****");

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