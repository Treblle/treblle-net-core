using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Treblle.Net.Core;

internal sealed class TreblleService
{
    private static readonly List<string> SensitiveWords = new()
    {
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

    private readonly HttpClient _httpClient;
    private readonly TreblleOptions _treblleOptions;
    private readonly ILogger<TreblleService> _logger;

    public TreblleService(IHttpClientFactory httpClientFactory, IOptions<TreblleOptions> treblleOptions, ILogger<TreblleService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Treblle");
        _logger = logger;
        _treblleOptions = treblleOptions.Value;
    }

    public async Task<HttpResponseMessage?> SendPayloadAsync(TrebllePayload payload)
    {
        try
        {
            var jsonPayload = JsonConvert.SerializeObject(payload);

            if (!string.IsNullOrWhiteSpace(_treblleOptions.AdditionalFieldsToMask))
            {
                var additionalFields = _treblleOptions.AdditionalFieldsToMask.Split(',');

                if (additionalFields.Any())
                {
                    var list = additionalFields.ToList();

                    SensitiveWords.AddRange(list);
                }
            }

            var maskedJsonPayload = jsonPayload.Mask(SensitiveWords.ToArray(), "*****");

            var httpResponseMessage = await _httpClient.PostAsJsonAsync(string.Empty, maskedJsonPayload);

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
