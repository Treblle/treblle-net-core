using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Treblle.Net.Core;

public static class ServiceCollectionExtensions
{
    private static readonly Uri DefaultApiUri = new("https://rocknrolla.treblle.com");

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
            throw new ArgumentException("The project id is required", nameof(projectId));
        }

        services.TryAddTransient<TreblleService>();
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
}
