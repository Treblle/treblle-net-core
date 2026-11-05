using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
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
    /// <summary>
    /// The built-in field/masker pairs.
    /// </summary>
    /// <remarks>
    /// Read-only. User overrides are merged into a fresh dictionary per registration rather
    /// than written back here — the previous version mutated this static every time
    /// TreblleService was resolved, while the background sender was enumerating it.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> DefaultMaskingMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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
        { "user.payments.cc", "CreditCardMasker" },
        { "authorization", "AuthorizationMasker" },
        { "x-api-key", "AuthorizationMasker" }
    };

    /// <summary>
    /// Adds Treblle SDK to the service collection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="sdkToken">SDK Token for authentication (sent as sdk_token in payload)</param>
    /// <param name="apiKey">API Key for project identification (sent as api_key in payload)</param>
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
    /// <param name="sdkToken">SDK Token for authentication (sent as sdk_token in payload)</param>
    /// <param name="apiKey">API Key for project identification (sent as api_key in payload)</param>
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
    /// Auto-configuration. Credentials come from the environment or configuration, and a
    /// missing one disables the SDK rather than throwing — see <see cref="ValidateOptions"/>.
    /// </summary>
    private static IServiceCollection AddTreblleWithAutoConfiguration(
        IServiceCollection services,
        Action<TreblleOptions>? configureOptions)
    {
        // Configure auto-detection first, then user overrides, then validation last.
        services.AddOptions<TreblleOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                options.SdkToken = Resolve(configuration, "TREBLLE_SDK_TOKEN", "Treblle:SdkToken") ?? string.Empty;
                options.ApiKey = Resolve(configuration, "TREBLLE_API_KEY", "Treblle:ApiKey") ?? string.Empty;
                options.CustomIngressEndpoint = Resolve(configuration, "TREBLLE_CUSTOM_INGRESS_ENDPOINT", "Treblle:CustomIngressEndpoint");
            });

        if (configureOptions != null)
        {
            services.PostConfigure<TreblleOptions>(configureOptions);
        }

        // Registered after the user's delegate so it sees the final values.
        services.PostConfigure<TreblleOptions>(ValidateOptions);

        AddTreblleCore(services);

        return services;
    }

    /// <summary>
    /// Explicit configuration. Null or empty credentials are a programming error that is
    /// detectable here, at registration time, so this overload still throws.
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
        if (string.IsNullOrWhiteSpace(sdkToken))
        {
            throw new ArgumentException("The SDK token is required", nameof(sdkToken));
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("The API key is required", nameof(apiKey));
        }

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

        // Validation runs as a PostConfigure so it sees the result of every Configure
        // delegate, including a CustomIngressEndpoint set by configureOptions above.
        services.PostConfigure<TreblleOptions>(ValidateOptions);

        AddTreblleCore(services);

        return services;
    }

    /// <summary>
    /// Reads a setting from the environment, falling back to configuration.
    /// </summary>
    /// <remarks>
    /// A set-but-empty environment variable must not shadow configuration. On Unix
    /// <c>TREBLLE_SDK_TOKEN=</c> is a variable that exists with an empty value, and
    /// <see cref="Environment.GetEnvironmentVariable(string)"/> returns an empty string rather
    /// than null for it — so a plain <c>??</c> chain accepted it as the credential and never
    /// consulted appsettings.json. Passing an empty -e flag to a container, or an empty key in
    /// a ConfigMap, silently disabled the SDK on an otherwise correctly configured app.
    /// </remarks>
    private static string? Resolve(IConfiguration configuration, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);

        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        value = configuration[configurationKey];

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Registrations shared by every entry point. Previously duplicated across the two
    /// configuration paths, which let them drift apart.
    /// </summary>
    private static void AddTreblleCore(IServiceCollection services)
    {
        services.TryAddSingleton<TreblleService>(serviceProvider =>
        {
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var logger = serviceProvider.GetRequiredService<ILogger<TreblleService>>();
            var options = serviceProvider.GetRequiredService<IOptions<TreblleOptions>>().Value;

            return new TreblleService(
                httpClientFactory,
                serviceProvider.GetRequiredService<MaskingRules>(),
                serviceProvider.GetRequiredService<MaskerFactory>(),
                logger,
                options.DisableMasking,
                options.DebugMode,
                options.CustomIngressEndpoint);
        });

        // Compiled once for the lifetime of the application, and never mutated afterwards.
        services.TryAddSingleton(serviceProvider =>
            BuildMaskingRules(serviceProvider.GetRequiredService<IOptions<TreblleOptions>>().Value.FieldsToMaskPairedWithMaskers));

        services.TryAddSingleton<TrebllePayloadFactory>();

        // One sender for the application, started and stopped with the host so queued payloads
        // get a real chance to flush on shutdown.
        services.TryAddSingleton<TreblleSender>();
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<TreblleSender>());

        services.AddHttpClient("Treblle", (serviceProvider, httpClient) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<TreblleOptions>>().Value;
            httpClient.DefaultRequestHeaders.Remove("x-api-key");
            httpClient.DefaultRequestHeaders.Add("x-api-key", options.SdkToken);
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            // The ingress negotiates HTTP/2. Without setting this the request version stayed at
            // 1.1 and EnableMultipleHttp2Connections below was a no-op. RequestVersionOrLower
            // means ALPN falls back to 1.1 cleanly against anything that does not speak h2,
            // including a custom ingress endpoint.
            httpClient.DefaultRequestVersion = HttpVersion.Version20;
            httpClient.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        })
        .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<TreblleOptions>>().Value;

            return new SocketsHttpHandler
            {
                UseCookies = false, // Treblle API doesn't use cookies - disabling saves memory and processing

                // Honouring the system proxy by default: hard-coding this off lost every
                // payload, silently, in any environment with mandatory egress proxying.
                UseProxy = options.UseSystemProxy,

                // Recycling connections on a timer is what picks up DNS changes as the
                // ingress load balancer's addresses rotate. It replaces handler rotation
                // (disabled below), which threw away the warm pool every two minutes.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = Math.Clamp(options.SenderConcurrency, 1, 64),
                EnableMultipleHttp2Connections = true
            };
        })
        // The handler is owned by a singleton client talking to one host, so there is nothing
        // to gain from rotating it; PooledConnectionLifetime above handles DNS.
        .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

        // The maskers are stateless, and MaskerFactory resolves each of them once, so they are
        // registered as singletons rather than constructed per masked property.
        services.TryAddSingleton<DefaultStringMasker>();
        services.TryAddSingleton<EmailMasker>();
        services.TryAddSingleton<CreditCardMasker>();
        services.TryAddSingleton<SocialSecurityMasker>();
        services.TryAddSingleton<DateMasker>();
        services.TryAddSingleton<PostalCodeMasker>();
        services.TryAddSingleton<AuthorizationMasker>();

        // Register masker factory for .NET 6+ compatibility
        services.TryAddSingleton<MaskerFactory>();

        // One metadata collector per request. The concrete type is registered as well as the
        // interface, and the interface forwards to it, so TrebllePayloadFactory reads back
        // the very instance callers wrote to.
        services.TryAddScoped<TreblleMetadataCollector>();
        services.TryAddScoped<ITreblleMetadata>(serviceProvider =>
            serviceProvider.GetRequiredService<TreblleMetadataCollector>());

        services.AddHostedService<TreblleDiagnosticHostedService>();
        services.AddHostedService<TreblleStartupLogger>();
    }

    /// <summary>
    /// Merges user overrides over the defaults into an immutable rule set.
    /// </summary>
    private static MaskingRules BuildMaskingRules(IDictionary<string, string>? overrides)
    {
        if (overrides is null || overrides.Count == 0)
        {
            return new MaskingRules(DefaultMaskingMap);
        }

        var merged = new Dictionary<string, string>(DefaultMaskingMap, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in overrides)
        {
            merged[entry.Key] = entry.Value;
        }

        return new MaskingRules(merged);
    }

    /// <summary>
    /// The final configuration step. Nothing here throws.
    /// </summary>
    /// <remarks>
    /// These checks used to throw from inside a Configure delegate, which does not run until
    /// <c>IOptions&lt;TreblleOptions&gt;</c> is first resolved — inside the middleware, on the
    /// first request. An application with a missing credential booted green and then returned
    /// 500 for every request. A configuration problem now disables the SDK, and
    /// <see cref="TreblleStartupLogger"/> reports it during startup.
    /// </remarks>
    private static void ValidateOptions(TreblleOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SdkToken))
        {
            Disable(options,
                "SDK Token not found. Set the TREBLLE_SDK_TOKEN environment variable, or Treblle:SdkToken in appsettings.json, or use the AddTreblle(sdkToken, apiKey) overload.");
            return;
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            Disable(options,
                "API Key not found. Set the TREBLLE_API_KEY environment variable, or Treblle:ApiKey in appsettings.json, or use the AddTreblle(sdkToken, apiKey) overload.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(options.CustomIngressEndpoint))
        {
            if (!TryNormalizeIngressEndpoint(options.CustomIngressEndpoint!, out var normalized, out var error))
            {
                // Deliberately not falling back to the default endpoint: that would ship data
                // to a region the operator explicitly did not choose.
                Disable(options, error);
                return;
            }

            options.CustomIngressEndpoint = normalized;
        }
    }

    private static void Disable(TreblleOptions options, string reason)
    {
        options.IsEnabled = false;
        options.DisabledReason = reason;
    }

    /// <summary>
    /// Validates and normalizes a custom ingress endpoint URL, removing any trailing slash.
    /// </summary>
    private static bool TryNormalizeIngressEndpoint(string endpoint, out string? normalized, out string error)
    {
        normalized = null;

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            error = $"Invalid CustomIngressEndpoint URL: '{endpoint}'. Must be a valid absolute URL.";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            error = $"Invalid CustomIngressEndpoint URL: '{endpoint}'. Must use HTTPS scheme.";
            return false;
        }

        normalized = uri.GetLeftPart(UriPartial.Authority) + uri.AbsolutePath.TrimEnd('/');
        error = string.Empty;
        return true;
    }
}
