using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Treblle.Net.Core.Masking;
using Treblle.Runtime.Masking;

namespace Treblle.Net.Core;

public static class ServiceCollectionExtensions
{
    private static readonly Uri DefaultApiUri = new("https://rocknrolla.treblle.com");
    private static readonly Dictionary<string, string> maskingMap = new()
    {
        { "password", "DefaultStringMasker" },
        { "pwd", "DefaultStringMasker" },
        { "secret", "DefaultStringMasker" },
        { "password_confirmation", "DefaultStringMasker" },
        { "passwordConfirmation", "DefaultStringMasker" },
        { "cc", "CreditCardMasker" },
        { "card_number", "CreditCardMasker" },
        { "cardNumber", "CreditCardMasker" },
        { "ccv", "CreditCardMasker" },
        { "ssn", "SocialSecurityMasker" },
        { "credit_score", "DefaultStringMasker" },
        { "creditScore", "DefaultStringMasker" },
        { "email", "EmailMasker" },
        { "account.*", "DefaultStringMasker" },
        { "user.email", "EmailMasker" },
        { "user.dob", "DateMasker" },
        { "user.password","DefaultStringMasker" },
        { "user.ss", "SocialSecurityMasker" },
        { "user.payments.cc", "CreditCardMasker" }
    };
   
    public static IServiceCollection AddTreblle(
        this IServiceCollection services,
        string apiKey,
        string projectId,
        Dictionary<string, string>? FieldsToMaskPairedWithMaskers = null)
    {

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("The api key is required", nameof(apiKey));
        }

        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new ArgumentException("The project key is required", nameof(projectId));
        }
        
        services.TryAddTransient<TreblleService>( serviceProvider =>
        {
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var logger = serviceProvider.GetRequiredService<ILogger<TreblleService>>();

            if (FieldsToMaskPairedWithMaskers is null)
            {
                logger.LogInformation("Using default sensitive words.");
            }
            else 
            {
                foreach (var kv in FieldsToMaskPairedWithMaskers)
                {
                    maskingMap[kv.Key] = kv.Value;
                }
            }

            return new(httpClientFactory, maskingMap, logger, serviceProvider);
        });
        
        services.TryAddSingleton<TrebllePayloadFactory>();
        services.Configure<TreblleOptions>(o =>
        {
            o.ApiKey = apiKey;
            o.ProjectId = projectId;
            o.FieldsToMaskPairedWithMaskers = FieldsToMaskPairedWithMaskers;
        });
        services.AddHttpClient("Treblle", httpClient =>
        { 
            httpClient.BaseAddress = DefaultApiUri;
            httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        });

        services.TryAddKeyedTransient<IStringMasker, DefaultStringMasker>(nameof(DefaultStringMasker));
        services.TryAddKeyedTransient<IStringMasker, EmailMasker>(nameof(EmailMasker));
        services.TryAddKeyedTransient<IStringMasker, CreditCardMasker>(nameof(CreditCardMasker));
        services.TryAddKeyedTransient<IStringMasker, SocialSecurityMasker>(nameof(SocialSecurityMasker));
        services.TryAddKeyedTransient<IStringMasker, DateMasker>(nameof(DateMasker));
        services.TryAddKeyedTransient<IStringMasker, PostalCodeMasker>(nameof(PostalCodeMasker));

        services.TryAddTransient<DefaultStringMasker, EmailMasker>();
        services.TryAddTransient<DefaultStringMasker, CreditCardMasker>();
        services.TryAddTransient<DefaultStringMasker, SocialSecurityMasker>();
        services.TryAddTransient<DefaultStringMasker, DateMasker>();
        services.TryAddTransient<DefaultStringMasker, PostalCodeMasker>();

        return services;
    }
}
