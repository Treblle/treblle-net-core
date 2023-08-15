using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Collections.Generic;

namespace Treblle.Net.Core;

public static class ServiceCollectionExtensions
{
    private static readonly Uri RollaApiUri = new("https://rocknrolla.treblle.com");
    private static readonly Uri PunisherApiUri = new("https://punisher.treblle.com");
    private static readonly Uri SicarioApiUri = new("https://sicario.treblle.com");
    private static readonly List<Uri> uris = new() { RollaApiUri, PunisherApiUri, SicarioApiUri };
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
        services.AddHttpClient("Treblle", httpClient =>
        {
            Uri clientUri = uris[new Random().Next(0, uris.Count)];
            httpClient.BaseAddress = clientUri;
            httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        });

        return services;
    }
}
