using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Treblle.Net.Core;

public static class ServiceCollectionExtensions
{
    private static readonly Uri DefaultApiUri = new("https://rocknrolla.treblle.com");
    private static readonly HashSet<string> s_sensitiveWords = new()
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
    
    public static IServiceCollection AddTreblle(
        this IServiceCollection services,
        string apiKey,
        string projectId,
        string? additionalFieldsToMask = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("The api key is required", nameof(apiKey));
        }

        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new ArgumentException("The project key is required", nameof(projectId));
        }

        AddAdditionalSensitiveWords(additionalFieldsToMask);
        
        services.TryAddTransient<TreblleService>( serviceProvider =>
        {
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var treblleOptions = serviceProvider.GetRequiredService<IOptions<TreblleOptions>>();
            var logger = serviceProvider.GetRequiredService<ILogger<TreblleService>>();
            
            return new(httpClientFactory, treblleOptions, s_sensitiveWords, logger);
        });
        
        services.TryAddSingleton<TrebllePayloadFactory>();
        services.Configure<TreblleOptions>(o =>
        {
            o.ApiKey = apiKey;
            o.ProjectId = projectId;
            o.AdditionalFieldsToMask = additionalFieldsToMask;
        });
        services.AddHttpClient("Treblle", httpClient =>
        { 
            httpClient.BaseAddress = DefaultApiUri;
            httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        });

        return services;
    }
    private static void AddAdditionalSensitiveWords(string? additionalFields)
    {
        if (string.IsNullOrWhiteSpace(additionalFields)) 
            return;
    
        ReadOnlySpan<char> span = additionalFields.AsSpan();
        int start = 0;

        while (start < span.Length)
        {
            int commaIndex = span.Slice(start).IndexOf(',');
            if (commaIndex == -1)
            {
                commaIndex = span.Length - start;
            }

            var field = span.Slice(start, commaIndex);

            if (field.Length > 0)
            {
                s_sensitiveWords.Add(field.ToString());
            }

            start += commaIndex + 1; 
        }
    }
}
