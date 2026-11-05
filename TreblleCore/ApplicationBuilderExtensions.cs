using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Treblle.Net.Core;

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseTreblle(this IApplicationBuilder app, bool useExceptionHandler = true)
    {
        if (useExceptionHandler)
        {
            app.UseExceptionHandler(innerApp =>
            {
                innerApp.Run(async (httpContext) =>
                {
                    var exceptionHandlerFeature = httpContext.Features.Get<IExceptionHandlerFeature>();

                    if (exceptionHandlerFeature is null)
                    {
                        return;
                    }

                    // A configuration problem disables the SDK rather than taking the host down.
                    var options = httpContext.RequestServices.GetRequiredService<IOptions<TreblleOptions>>().Value;

                    if (!options.IsEnabled)
                    {
                        return;
                    }

                    var trebllePayloadFactory = httpContext.RequestServices.GetRequiredService<TrebllePayloadFactory>();

                    var elapsedMiliseconds =
                        httpContext.Items.TryGetValue(TreblleItemKeys.ElapsedMilliseconds, out var elapsed) && elapsed is long value
                            ? value
                            : 0L;

                    var payload = await trebllePayloadFactory.CreateAsync(
                        httpContext,
                        response: null,
                        elapsedMiliseconds,
                        exceptionHandlerFeature.Error);

                    // Queued rather than awaited. Sending inline put a network call with a
                    // 10 second timeout in front of every error response — worst possible
                    // moment to add latency, since the API is already failing.
                    httpContext.RequestServices.GetRequiredService<TreblleSender>().TryEnqueue(payload);
                });
            });
        }

        app.UseMiddleware<TreblleMiddleware>();

        return app;
    }
}
