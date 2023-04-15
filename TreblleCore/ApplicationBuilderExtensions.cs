using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Treblle.Net.Core;

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseTreblle(this IApplicationBuilder app, bool useExceptionHandler = false)
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

                    var trebllePayloadFactory = httpContext.RequestServices.GetRequiredService<TrebllePayloadFactory>();

                    var payload = await trebllePayloadFactory.CreateAsync(
                        httpContext,
                        response: null,
                        default,
                        exceptionHandlerFeature.Error);

                    var treblleService = httpContext.RequestServices.GetRequiredService<TreblleService>();

                    await treblleService.SendPayloadAsync(payload);
                });
            });
        }

        app.UseMiddleware<TreblleMiddleware>();

        return app;
    }
}
