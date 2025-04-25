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
using System.Net.Mime;
using System.Reflection;
using System.Runtime.InteropServices;
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
        MemoryStream? response,
        long elapsedMilliseconds,
        Exception? exception = null)
    {
        var payload = new TrebllePayload
        {
            Sdk = "net-core",
            Version = GetTrimmedSdkVersion(),
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
        payload.Data.Language.Version = GetProgrammingLanguageVersion();
    }

    private static void AddServer(HttpContext httpContext, TrebllePayload payload)
    {
        payload.Data.Server.Ip = httpContext.Connection.LocalIpAddress?.MapToIPv4()?.ToString() ?? "bogon";
        payload.Data.Server.Timezone = (!string.IsNullOrEmpty(TimeZoneInfo.Local.StandardName))
            ? TimeZoneInfo.Local.StandardName
            : "UTC";
        payload.Data.Server.Software = httpContext.GetServerVariable("SERVER_SOFTWARE");
        payload.Data.Server.Signature = null;
        payload.Data.Server.Protocol = httpContext.Request.Protocol;

        payload.Data.Server.Os.Name = Environment.OSVersion.ToString();
        payload.Data.Server.Os.Release = Environment.OSVersion.Version.ToString();
        payload.Data.Server.Os.Architecture = RuntimeInformation.ProcessArchitecture.ToString();
    }

    private async Task AddRequest(HttpContext httpContext, TrebllePayload payload)
    {
        try
        {
            payload.Data.Request.Timestamp = DateTime.UtcNow.ToString("yyyy-M-d H:m:s");
            string serverIpAddress = httpContext.GetServerVariable("REMOTE_ADDR");
            payload.Data.Request.Ip = !string.IsNullOrEmpty(serverIpAddress) ? serverIpAddress : "bogon";
            payload.Data.Request.Url = httpContext.Request.GetDisplayUrl();
            payload.Data.Request.Query = httpContext.Request.QueryString.ToString();
            payload.Data.Request.RoutePath = httpContext.Request.Path;
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
                    if (IsValidJson(bodyData))
                    {
                        payload.Data.Request.Body = JsonConvert.DeserializeObject<dynamic>(bodyData)!;
                    }
                    else
                    {
                        _logger.LogWarning("Invalid JSON detected in request.");
                    }
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

    private async Task TryAddResponse(HttpContext httpContext, MemoryStream? response, long elapsedMilliseconds, TrebllePayload payload)
    {
        if (response is not null)
        {
            if (httpContext.Response?.ContentType?.Contains(MediaTypeNames.Application.Json, StringComparison.OrdinalIgnoreCase) ?? false)
            {
                if (httpContext.Response.ContentLength.HasValue && httpContext.Response.ContentLength!.Value > 2048)
                {
                    payload.Data.Errors.Add(new Error
                    {
                        Message = "JSON response size is over 2MB",
                        Type = "E_USER_ERROR",
                        File = string.Empty,
                        Line = 0
                    });
                }
                else
                {
                    response.Position = 0;

                    {
                        try
                        {
                            using var responseReader = new StreamReader(response, leaveOpen: true);
                            var responseContent = await responseReader.ReadToEndAsync();

                            if (IsValidJson(responseContent))
                            {
                                payload.Data.Response.Body = JsonConvert.DeserializeObject<dynamic>(responseContent)!;
                            }
                            else
                            {
                                _logger.LogWarning("Invalid JSON detected in response.");
                            }
                            payload.Data.Response.Size = httpContext.Response.ContentLength ?? 0;
                        }
                        catch (Exception e)
                        {
                            _logger.LogWarning("Error ocurred while reading response content.", e);

                        }
                    }
                }
            }
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
        payload.Data.Response.LoadTime = elapsedMilliseconds;
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

    private static string GetTrimmedSdkVersion()
    {
        var versionString = Assembly.GetExecutingAssembly()
                              .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                              .InformationalVersion;

        // Strip optional suffixes
        int separatorIndex = versionString.IndexOfAny(['-', '+', ' ']);
        if (separatorIndex >= 0)
            versionString = versionString.Substring(0, separatorIndex);

        // Return zeros rather then failing if the version string fails to parse
        var success = Version.TryParse(versionString, out Version? version) ? version : new Version();

        return version.Build > 0 ? version.ToString()
                            : version.Revision > 0 ? $"{version.Major}.{version.Minor}.{version.Build}"
                            : $"{version.Major}.{version.Minor}";

    }

    private static string GetProgrammingLanguageVersion()
    {
#if NET8_0
        return "12";
#elif NET7_0
                return "11";
#else
                retrun "10";
#endif
    }

    private bool IsValidJson(string str)
    {
        try
        {
            JsonConvert.DeserializeObject(str);
            return true;
        }
        catch (JsonReaderException)
        {
            return false;
        }
    }
}
