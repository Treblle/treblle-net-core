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
   
    /// <summary>
    /// Adds Treblle SDK to the service collection.
    /// Parameter names changed in v1.3.4+ for clarity, but accepts same credential values.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="sdkToken">SDK Token for authentication (sent as api_key in payload). 
    /// Legacy: If migrating from old code, pass your previous "apiKey" value here.</param>
    /// <param name="apiKey">API Key for project identification (sent as project_id in payload).
    /// Legacy: If migrating from old code, pass your previous "projectId" value here.</param>
    /// <param name="FieldsToMaskPairedWithMaskers">Optional custom masking configuration</param>
    /// <param name="disableMasking">Whether to disable masking for performance</param>
    public static IServiceCollection AddTreblle(
        this IServiceCollection services,
        string sdkToken,
        string apiKey,
        Dictionary<string, string>? FieldsToMaskPairedWithMaskers = null,
        bool disableMasking = false)
    {

        if (string.IsNullOrWhiteSpace(sdkToken))
        {
            throw new ArgumentException("The SDK token is required", nameof(sdkToken));
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("The API key is required", nameof(apiKey));
        }
        
        services.TryAddTransient<TreblleService>( serviceProvider =>
        {
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var logger = serviceProvider.GetRequiredService<ILogger<TreblleService>>();
            var options = serviceProvider.GetRequiredService<IOptions<TreblleOptions>>().Value;

            if (options.DisableMasking)
            {
                logger.LogInformation("Data masking is disabled for improved performance.");
            }
            else if (FieldsToMaskPairedWithMaskers is null)
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

            return new(httpClientFactory, maskingMap, logger, serviceProvider, options.DisableMasking);
        });
        
        services.TryAddSingleton<TrebllePayloadFactory>();
        services.Configure<TreblleOptions>(o =>
        {
            // Set both new and legacy properties for maximum compatibility
            o.SdkToken = sdkToken;
            o.ApiKey = apiKey;
            
            // Also set legacy properties to ensure backward compatibility
            #pragma warning disable CS0618 // Type or member is obsolete
            o.LegacyApiKey = sdkToken;  // Same value, legacy property name
            o.ProjectId = apiKey;       // Same value, legacy property name
            #pragma warning restore CS0618 // Type or member is obsolete
            
            o.FieldsToMaskPairedWithMaskers = FieldsToMaskPairedWithMaskers;
            o.DisableMasking = disableMasking;
        });
        services.AddHttpClient("Treblle", httpClient =>
        { 
            httpClient.BaseAddress = DefaultApiUri;
            httpClient.DefaultRequestHeaders.Add("x-api-key", sdkToken);
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
