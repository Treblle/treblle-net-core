using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Treblle.Net.Core;

internal sealed class TrebllePayloadFactory
{
    private readonly TreblleOptions _treblleOptions;
    private readonly ILogger<TrebllePayloadFactory> _logger;

    public TrebllePayloadFactory(IOptions<TreblleOptions> treblleOptions, ILogger<TrebllePayloadFactory> logger)
    {
        _treblleOptions = treblleOptions.Value;
        _logger = logger;
    }

    internal async Task<TrebllePayload> CreateAsync(
        HttpContext httpContext,
        Stream? response,
        long elapsedMilliseconds,
        Exception? exception = null)
    {
        var payload = new TrebllePayload
        {
            Sdk = "netCore",
            Version = Environment.Version.ToString(),
            ProjectId = _treblleOptions.ProjectId,
            ApiKey = _treblleOptions.ApiKey,
        };

        AddLanguage(payload);

        AddServer(httpContext, payload);

        await AddRequest(httpContext, payload);

        await TryAddResponse(httpContext, response, elapsedMilliseconds, payload);

        TryAddError(exception, payload);

        return payload;
    }

    private static void AddLanguage(TrebllePayload payload)
    {
        payload.Data.Language.Name = "c#";
        payload.Data.Language.Version =
            $"{Environment.Version.Major}.{Environment.Version.Minor}.{Environment.Version.Revision}";
    }

    private static void AddServer(HttpContext httpContext, TrebllePayload payload)
    {
        payload.Data.Server.Ip = httpContext.GetServerVariable("LOCAL_ADDR");
        payload.Data.Server.Timezone = (!string.IsNullOrEmpty(TimeZoneInfo.Local.StandardName))
            ? TimeZoneInfo.Local.StandardName
            : "UTC";
        payload.Data.Server.Software = httpContext.GetServerVariable("SERVER_SOFTWARE");
        payload.Data.Server.Signature = null;
        payload.Data.Server.Protocol = httpContext.GetServerVariable("SERVER_PROTOCOL");

        payload.Data.Server.Os.Name = Environment.OSVersion.ToString();
        payload.Data.Server.Os.Release = Environment.OSVersion.Version.ToString();
        payload.Data.Server.Os.Architecture = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");
    }

    private async Task AddRequest(HttpContext httpContext, TrebllePayload payload)
    {
        try
        {
            payload.Data.Request.Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            payload.Data.Request.Ip = httpContext.GetServerVariable("REMOTE_ADDR");
            payload.Data.Request.Url = httpContext.Request.GetDisplayUrl();
            payload.Data.Request.UserAgent = httpContext.Request.Headers["User-Agent"].ToString();
            payload.Data.Request.Method = httpContext.Request.Method;

            TryAddRequestHeaders(httpContext, payload);

            await TryAddRequestBody(httpContext, payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while intercepting request. --- Exception message: {Message}", ex.Message);
        }
    }

    private void TryAddRequestHeaders(HttpContext httpContext, TrebllePayload payload)
    {
        try
        {
            payload.Data.Request.Headers =
                httpContext.Request.Headers.ToDictionary(x => x.Key, x => string.Join(";", x.Value));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "An error occurred while attempting to read request headers. --- Exception message: {Message}",
                ex.Message);
        }
    }

    private async Task TryAddRequestBody(HttpContext httpContext, TrebllePayload payload)
    {
        try
        {
            if (httpContext.Request.ContentType != null)
            {
                httpContext.Request.Body.Position = 0;
                using var requestReader = new StreamReader(httpContext.Request.Body);
                var bodyData = await requestReader.ReadToEndAsync();

                if (httpContext.Request.ContentType.Contains("application/json"))
                {
                    payload.Data.Request.Body = JsonConvert.DeserializeObject<dynamic>(bodyData);
                }
                else if (httpContext.Request.ContentType.Contains("text/plain"))
                {
                    payload.Data.Request.Body = bodyData;
                }
                else if (httpContext.Request.ContentType.Contains("application/xml"))
                {
                    var doc = XDocument.Parse(bodyData);
                    var jsonText = JsonConvert.SerializeXNode(doc);
                    payload.Data.Request.Body = JsonConvert.DeserializeObject<ExpandoObject>(jsonText);
                }
                else if (httpContext.Request.HasFormContentType)
                {
                    payload.Data.Request.Body = httpContext.Request.Form;
                }

                httpContext.Request.Body.Position = 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "An error occurred while attempting to read request body. --- Exception message: {Message}",
                ex.Message);
        }
    }

    private async Task TryAddResponse(HttpContext httpContext, Stream? response, long elapsedMilliseconds, TrebllePayload payload)
    {
        if (response is not null)
        {
            response.Position = 0;

            using var responseReader = new StreamReader(response, leaveOpen: true);

            var responseContent = await responseReader.ReadToEndAsync();

            payload.Data.Response.Body = JsonConvert.DeserializeObject<dynamic>(responseContent)!;

            payload.Data.Response.Size = responseContent.Length;
        }

        try
        {
            payload.Data.Response.Headers =
                httpContext.Response.Headers.ToDictionary(x => x.Key, x => string.Join(";", x.Value));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "An error occurred while attempting to read response headers. --- Exception message: {Message}",
                ex.Message);
        }

        payload.Data.Response.Code = httpContext.Response.StatusCode;
        payload.Data.Response.LoadTime = elapsedMilliseconds / (double)1000;
    }

    private void TryAddError(Exception? exception, TrebllePayload payload)
    {
        if (exception is null)
        {
            return;
        }

        var error = new Error
        {
            Source = "onException",
            Type = exception.GetType().Name,
            Message = exception.Message,
            File = null,
            Line = 0
        };

        var stackTrace = new StackTrace(exception, true);
        if (stackTrace.FrameCount > 0)
        {
            var frame = stackTrace.GetFrame(0);
            if (frame != null)
            {
                var line = frame.GetFileLineNumber();

                error.Line = line;

                var file = frame.GetFileName();

                if (file != null)
                {
                    error.File = file;
                }
            }
        }

        payload.Data.Errors.Add(error);

        payload.Data.Response.Code = StatusCodes.Status500InternalServerError;
    }
}
