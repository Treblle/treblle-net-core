using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Treblle.Net.Core;

public static class ServiceCollectionExtensions
{
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
            throw new ArgumentException("The api key is required", nameof(projectId));
        }

        services.TryAddTransient<TreblleService>();
        services.TryAddSingleton<TrebllePayloadFactory>();
        services.Configure<TreblleOptions>(o =>
        {
            o.ApiKey = apiKey;
            o.ProjectId = projectId;
            o.AdditionalFieldsToMask = additionalFieldsToMask;
        });
        services.AddHttpClient();

        return services;
    }
}
