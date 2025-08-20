using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
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
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="sdkToken">SDK Token for authentication (sent as api_key in payload)</param>
    /// <param name="apiKey">API Key for project identification (sent as project_id in payload)</param>
    /// <param name="FieldsToMaskPairedWithMaskers">Optional custom masking configuration</param>
    /// <param name="disableMasking">Whether to disable masking for performance</param>
    /// <param name="debugMode">Whether to enable debug logging for troubleshooting</param>
    public static IServiceCollection AddTreblle(
        this IServiceCollection services,
        string sdkToken,
        string apiKey,
        Dictionary<string, string>? FieldsToMaskPairedWithMaskers = null,
        bool disableMasking = false,
        bool debugMode = false)
    {
        return AddTreblle(services, sdkToken, apiKey, FieldsToMaskPairedWithMaskers, disableMasking, debugMode, null);
    }

    /// <summary>
    /// Adds Treblle SDK to the service collection with configuration options.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="sdkToken">SDK Token for authentication (sent as api_key in payload)</param>
    /// <param name="apiKey">API Key for project identification (sent as project_id in payload)</param>
    /// <param name="configureOptions">Action to configure TreblleOptions including excludedPaths, debugMode, etc.</param>
    public static IServiceCollection AddTreblle(
        this IServiceCollection services,
        string sdkToken,
        string apiKey,
        Action<TreblleOptions> configureOptions)
    {
        return AddTreblle(services, sdkToken, apiKey, null, false, false, configureOptions);
    }

    /// <summary>
    /// Adds Treblle SDK to the service collection with automatic credential detection.
    /// Automatically detects SDK Token and API Key from multiple configuration sources.
    /// </summary>
    /// <param name="services">The service collection</param>
    public static IServiceCollection AddTreblle(this IServiceCollection services)
    {
        return AddTreblleWithAutoConfiguration(services, null);
    }

    /// <summary>
    /// Adds Treblle SDK to the service collection with automatic credential detection and configuration options.
    /// Automatically detects SDK Token and API Key from multiple configuration sources.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Action to configure TreblleOptions including excludedPaths, debugMode, etc.</param>
    public static IServiceCollection AddTreblle(
        this IServiceCollection services,
        Action<TreblleOptions> configureOptions)
    {
        return AddTreblleWithAutoConfiguration(services, configureOptions);
    }

    /// <summary>
    /// Internal method for auto-configuration that bypasses credential validation
    /// </summary>
    private static IServiceCollection AddTreblleWithAutoConfiguration(
        IServiceCollection services,
        Action<TreblleOptions>? configureOptions)
    {
        // Configure auto-detection first, then user overrides
        services.AddOptions<TreblleOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                var (sdkToken, apiKey) = GetTreblleCredentials(configuration);
                options.SdkToken = sdkToken;
                options.ApiKey = apiKey;
            });

        if (configureOptions != null)
        {
            services.PostConfigure<TreblleOptions>(configureOptions);
        }

        // Register services without credential validation since they're handled by options
        services.TryAddTransient<TreblleService>(serviceProvider =>
        {
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var logger = serviceProvider.GetRequiredService<ILogger<TreblleService>>();
            var options = serviceProvider.GetRequiredService<IOptions<TreblleOptions>>().Value;

            if (options.DebugMode)
            {
                logger.LogDebug("[TREBLLE]: TreblleService initialized with debug mode enabled");
            }

            if (options.DisableMasking)
            {
                if (options.DebugMode)
                {
                    logger.LogDebug("[TREBLLE]: Data masking is disabled for improved performance");
                }
            }
            else
            {
                if (options.DebugMode)
                {
                    logger.LogDebug("[TREBLLE]: Using default sensitive words for masking");
                }
            }

            return new(httpClientFactory, maskingMap, logger, serviceProvider, options.DisableMasking, options.DebugMode);
        });

        services.TryAddSingleton<TrebllePayloadFactory>();

        services.AddHttpClient("Treblle", (serviceProvider, httpClient) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<TreblleOptions>>().Value;
            httpClient.BaseAddress = DefaultApiUri;
            httpClient.DefaultRequestHeaders.Add("x-api-key", options.SdkToken);
        });

        // Register masker types individually (replacement for keyed services)
        services.TryAddTransient<DefaultStringMasker>();
        services.TryAddTransient<EmailMasker>();
        services.TryAddTransient<CreditCardMasker>();
        services.TryAddTransient<SocialSecurityMasker>();
        services.TryAddTransient<DateMasker>();
        services.TryAddTransient<PostalCodeMasker>();

        // Register masker factory for .NET 6+ compatibility
        services.TryAddSingleton<MaskerFactory>();

        return services;
    }

    /// <summary>
    /// Internal method with full parameter support including configuration action
    /// </summary>
    private static IServiceCollection AddTreblle(
        this IServiceCollection services,
        string sdkToken,
        string apiKey,
        Dictionary<string, string>? FieldsToMaskPairedWithMaskers,
        bool disableMasking,
        bool debugMode,
        Action<TreblleOptions>? configureOptions = null)
    {
        // Create a temporary logger for setup validation if debug mode is enabled
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var setupLogger = loggerFactory.CreateLogger("Treblle.Setup");

        if (string.IsNullOrWhiteSpace(sdkToken))
        {
            if (debugMode)
            {
                setupLogger.LogDebug("[TREBLLE]: SDK Token is null or empty - this will cause authentication failures");
            }
            throw new ArgumentException("The SDK token is required", nameof(sdkToken));
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (debugMode)
            {
                setupLogger.LogDebug("[TREBLLE]: API Key is null or empty - this will cause project identification failures");
            }
            throw new ArgumentException("The API key is required", nameof(apiKey));
        }

        if (debugMode)
        {
            setupLogger.LogDebug("[TREBLLE]: SDK Token and API Key provided successfully");
            setupLogger.LogDebug("[TREBLLE]: Debug mode is enabled - additional logging will be available");
        }
        
        services.TryAddTransient<TreblleService>( serviceProvider =>
        {
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var logger = serviceProvider.GetRequiredService<ILogger<TreblleService>>();
            var options = serviceProvider.GetRequiredService<IOptions<TreblleOptions>>().Value;

            if (options.DebugMode)
            {
                logger.LogDebug("[TREBLLE]: TreblleService initialized with debug mode enabled");
            }

            if (options.DisableMasking)
            {
                if (options.DebugMode)
                {
                    logger.LogDebug("[TREBLLE]: Data masking is disabled for improved performance");
                }
            }
            else if (FieldsToMaskPairedWithMaskers is null)
            {
                if (options.DebugMode)
                {
                    logger.LogDebug("[TREBLLE]: Using default sensitive words for masking");
                }
            }
            else 
            {
                foreach (var kv in FieldsToMaskPairedWithMaskers)
                {
                    maskingMap[kv.Key] = kv.Value;
                }
                
                if (options.DebugMode)
                {
                    logger.LogDebug("[TREBLLE]: Using custom masking configuration with {Count} custom rules", FieldsToMaskPairedWithMaskers.Count);
                }
            }

            return new(httpClientFactory, maskingMap, logger, serviceProvider, options.DisableMasking, options.DebugMode);
        });
        
        services.TryAddSingleton<TrebllePayloadFactory>();
        services.Configure<TreblleOptions>(o =>
        {
            o.SdkToken = sdkToken;
            o.ApiKey = apiKey;
            o.FieldsToMaskPairedWithMaskers = FieldsToMaskPairedWithMaskers;
            o.DisableMasking = disableMasking;
            o.DebugMode = debugMode;
            
            // Apply additional configuration if provided
            configureOptions?.Invoke(o);
        });
        services.AddHttpClient("Treblle", httpClient =>
        { 
            httpClient.BaseAddress = DefaultApiUri;
            httpClient.DefaultRequestHeaders.Add("x-api-key", sdkToken);
        });

        // Register masker types individually (replacement for keyed services)
        services.TryAddTransient<DefaultStringMasker>();
        services.TryAddTransient<EmailMasker>();
        services.TryAddTransient<CreditCardMasker>();
        services.TryAddTransient<SocialSecurityMasker>();
        services.TryAddTransient<DateMasker>();
        services.TryAddTransient<PostalCodeMasker>();

        // Register masker factory for .NET 6+ compatibility
        services.TryAddSingleton<MaskerFactory>();

        return services;
    }

    /// <summary>
    /// Detects Treblle credentials from multiple configuration sources in order of precedence.
    /// </summary>
    private static (string sdkToken, string apiKey) GetTreblleCredentials(IConfiguration configuration)
    {
        // Check for SDK Token in order of precedence
        var sdkToken = 
            // 1. Environment variable (highest precedence)
            Environment.GetEnvironmentVariable("TREBLLE_SDK_TOKEN") ??
            // 2. appsettings.json
            configuration["Treblle:SdkToken"];

        // Check for API Key in order of precedence  
        var apiKey = 
            // 1. Environment variable (highest precedence)
            Environment.GetEnvironmentVariable("TREBLLE_API_KEY") ??
            // 2. appsettings.json
            configuration["Treblle:ApiKey"];


        // Validate that we found both credentials
        if (string.IsNullOrWhiteSpace(sdkToken))
        {
            throw new InvalidOperationException(
                "Treblle SDK Token not found. Please set one of the following:\n" +
                "- Environment variable: TREBLLE_SDK_TOKEN\n" +
                "- Configuration: Treblle:SdkToken in appsettings.json\n" +
                "- Or use AddTreblle(sdkToken, apiKey) overload for manual configuration");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Treblle API Key not found. Please set one of the following:\n" +
                "- Environment variable: TREBLLE_API_KEY\n" +
                "- Configuration: Treblle:ApiKey in appsettings.json\n" +
                "- Or use AddTreblle(sdkToken, apiKey) overload for manual configuration");
        }

        return (sdkToken, apiKey);
    }

}
