using Microsoft.AspNetCore.Http;
using System.IO;
using System.Threading.Tasks;

namespace Treblle.Net.Core;

internal class TreblleMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TreblleService _treblleService;
    private readonly TrebllePayloadFactory _trebllePayloadFactory;

    public TreblleMiddleware(
        RequestDelegate next,
        TreblleService treblleService,
        TrebllePayloadFactory trebllePayloadFactory)
    {
        _next = next;
        _treblleService = treblleService;
        _trebllePayloadFactory = trebllePayloadFactory;
    }

    public async Task Invoke(HttpContext httpContext)
    {
        if (httpContext.GetEndpoint()?.Metadata.GetMetadata<TreblleAttribute>() is not null)
        {
            await HandleRequestWithTreblleAsync(httpContext);
        }
        else
        {
            await _next(httpContext);
        }
    }

    private async Task HandleRequestWithTreblleAsync(HttpContext httpContext)
    {
        var originalResponseBody = httpContext.Response.Body;

        try
        {
            httpContext.Request.EnableBuffering();

            var stopwatch = ValueStopwatch.StartNew();

            using var memoryStream = new MemoryStream();

            httpContext.Response.Body = memoryStream;

            await _next(httpContext);

            var elapsed = stopwatch.GetElapsedTime();

            var payload = await _trebllePayloadFactory.CreateAsync(
                httpContext,
                memoryStream,
                (long)elapsed.TotalMilliseconds);

            memoryStream.Position = 0;

            await memoryStream.CopyToAsync(originalResponseBody);

            await _treblleService.SendPayloadAsync(payload);
        }
        finally
        {
            httpContext.Response.Body = originalResponseBody;
        }
    }
}