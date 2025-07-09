using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
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
            payload.Data.Request.RoutePath = NormalizeRoutePath(httpContext.Request.Path);
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
                var contentType = httpContext.Request.ContentType;
                var contentDisposition = httpContext.Request.Headers["Content-Disposition"].ToString();

                // Check if it's a raw binary/file-like upload
                bool isRawFile = IsRawFile(contentDisposition, contentType);

                if (httpContext.Request.Body.CanSeek)
                {
                    httpContext.Request.Body.Position = 0;
                }

                if (isRawFile)
                {
                    // Just get content length and content type
                    var length = httpContext.Request.ContentLength ?? 0;
                    payload.Data.Request.Body = new
                    {
                        __type = "file",
                        length = length,
                        contentType
                    };
                }
                else if (httpContext.Request.HasFormContentType)
                {
                    var form = await httpContext.Request.ReadFormAsync();
                    var files = form.Files;
                    var fileList = new List<object>();

                    foreach (var file in files)
                    {
                        fileList.Add(new
                        {
                            Name = file.FileName,
                            ContentType = file.ContentType,
                            Length = file.Length,
                            FieldName = file.Name
                        });
                    }

                    if (fileList.Any())
                    {
                        payload.Data.Request.Body = new
                        {
                            Files = fileList
                        };
                    }
                    else
                    {
                        var formData = form.ToDictionary(k => k.Key, v => v.Value.ToString());
                        payload.Data.Request.Body = formData;
                    }
                }
                else
                {
                    using var requestReader = new StreamReader(httpContext.Request.Body, leaveOpen: true);
                    var bodyData = await requestReader.ReadToEndAsync();

                    if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
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
                    else if (contentType.Contains("text/plain", StringComparison.OrdinalIgnoreCase))
                    {
                        payload.Data.Request.Body = bodyData;
                    }
                    else if (contentType.Contains("application/xml", StringComparison.OrdinalIgnoreCase))
                    {
                        var doc = XDocument.Parse(bodyData);
                        var jsonText = JsonConvert.SerializeXNode(doc);
                        payload.Data.Request.Body = JsonConvert.DeserializeObject<ExpandoObject>(jsonText);
                    }
                    else
                    {
                        // Non-JSON or unknown types, store minimal info
                        payload.Data.Request.Body = new
                        {
                            __type = "non-json",
                            contentType
                        };
                    }
                }

                if (httpContext.Request.Body.CanSeek)
                {
                    httpContext.Request.Body.Position = 0;
                }
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
                if (httpContext.Response.ContentLength is > 2048)
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
            .InformationalVersion ?? "0.0.0";


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

    private bool IsRawFile(string contentDisposition, string contentType)
    {
        return contentDisposition.Contains("attachment", StringComparison.OrdinalIgnoreCase) ||
        contentDisposition.Contains("filename", StringComparison.OrdinalIgnoreCase) ||
        contentType.Contains("application/octet-stream", StringComparison.OrdinalIgnoreCase) ||
        contentType.Contains("application/pdf", StringComparison.OrdinalIgnoreCase) ||
        contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
        contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ||
        contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
        contentType.Contains("application/zip", StringComparison.OrdinalIgnoreCase) ||
        contentType.Contains("text/csv", StringComparison.OrdinalIgnoreCase) ||

        // Microsoft Office formats
        contentType.Contains("application/vnd.ms-excel", StringComparison.OrdinalIgnoreCase) ||          // .xls
        contentType.Contains("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", StringComparison.OrdinalIgnoreCase) || // .xlsx
        contentType.Contains("application/vnd.ms-powerpoint", StringComparison.OrdinalIgnoreCase) ||     // .ppt
        contentType.Contains("application/vnd.openxmlformats-officedocument.presentationml.presentation", StringComparison.OrdinalIgnoreCase) || // .pptx
        contentType.Contains("application/vnd.ms-word", StringComparison.OrdinalIgnoreCase) ||           // older .doc
        contentType.Contains("application/vnd.openxmlformats-officedocument.wordprocessingml.document", StringComparison.OrdinalIgnoreCase) || // .docx

        // Other common binary formats
        contentType.Contains("application/vnd.oasis.opendocument.", StringComparison.OrdinalIgnoreCase) || // .odt, .ods, etc.
        contentType.Contains("application/vnd.google-apps", StringComparison.OrdinalIgnoreCase) ||        // Google Drive files
        contentType.StartsWith("application/vnd.", StringComparison.OrdinalIgnoreCase);
    }

    private string NormalizeRoutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var normalizedSegments = new List<string>();

        for (int i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];

            bool isGuid = Guid.TryParse(segment, out _);
            bool isInt = int.TryParse(segment, out _);

            if (isGuid || isInt)
            {
                // Look back for the previous static segment to name the placeholder
                string paramName = "id";
                if (i > 0)
                {
                    var prevSegment = segments[i - 1];
                    paramName = ToCamelCase(Singularize(prevSegment)) + "Id";
                }

                normalizedSegments.Add($"{{{paramName}}}");
            }
            else
            {
                normalizedSegments.Add(segment);
            }
        }

        return "/" + string.Join("/", normalizedSegments);
    }

    private string ToCamelCase(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return char.ToLowerInvariant(value[0]) + value.Substring(1);
    }

    // Helper to singularize simple plural nouns like "workspaces" -> "workspace"
    private string Singularize(string plural)
    {
        if (plural.EndsWith("ies"))
            return plural.Substring(0, plural.Length - 3) + "y";
        if (plural.EndsWith("s") && !plural.EndsWith("ss"))
            return plural.Substring(0, plural.Length - 1);
        return plural;
    }

}
