﻿using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Treblle.Net.Core;

internal class TreblleMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TreblleService _treblleService;
    private readonly TrebllePayloadFactory _trebllePayloadFactory;
    private readonly ILogger<TreblleMiddleware> _logger;

    public TreblleMiddleware(
        RequestDelegate next,
        TreblleService treblleService,
        TrebllePayloadFactory trebllePayloadFactory,
        ILogger<TreblleMiddleware> logger)
    {
        _next = next;
        _treblleService = treblleService;
        _trebllePayloadFactory = trebllePayloadFactory;
        _logger = logger;
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