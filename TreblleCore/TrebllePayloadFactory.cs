using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Treblle.Net.Core;

internal sealed class TrebllePayloadFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // Cache static system information to avoid repeated allocations
    private static readonly string CachedOSName = Environment.OSVersion.ToString();
    private static readonly string CachedOSVersion = Environment.OSVersion.Version.ToString();
    private static readonly string CachedArchitecture = RuntimeInformation.ProcessArchitecture.ToString();
    private static readonly string CachedTimezone = (!string.IsNullOrEmpty(TimeZoneInfo.Local.StandardName))
        ? TimeZoneInfo.Local.StandardName
        : "UTC";

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
            Version = TreblleConstants.PayloadVersion,
            // Map new properties to legacy payload field names for backward compatibility
            ProjectId = GetEffectiveApiKey(), // ApiKey from config -> project_id in payload
            ApiKey = GetEffectiveSdkToken(),  // SdkToken from config -> api_key in payload
        };

        AddLanguage(payload);

        AddServer(httpContext, payload);

        await AddRequest(httpContext, payload);

        await TryAddResponse(httpContext, response, elapsedMilliseconds, payload);

        TryAddError(exception, payload);

        return payload;
    }

    /// <summary>
    /// Gets the SDK token for authentication
    /// </summary>
    private string GetEffectiveSdkToken()
    {
        return _treblleOptions.SdkToken ?? string.Empty;
    }

    /// <summary>
    /// Gets the API key for project identification
    /// </summary>
    private string GetEffectiveApiKey()
    {
        return _treblleOptions.ApiKey ?? string.Empty;
    }

    private static void AddLanguage(TrebllePayload payload)
    {
        payload.Data.Language.Name = "c#";
        payload.Data.Language.Version = GetProgrammingLanguageVersion();
    }

    private static void AddServer(HttpContext httpContext, TrebllePayload payload)
    {
        payload.Data.Server.Ip = httpContext.Connection.LocalIpAddress?.MapToIPv4()?.ToString() ?? "bogon";
        payload.Data.Server.Timezone = CachedTimezone;
        payload.Data.Server.Software = httpContext.GetServerVariable("SERVER_SOFTWARE");
        payload.Data.Server.Signature = null;
        payload.Data.Server.Protocol = httpContext.Request.Protocol;

        payload.Data.Server.Os.Name = CachedOSName;
        payload.Data.Server.Os.Release = CachedOSVersion;
        payload.Data.Server.Os.Architecture = CachedArchitecture;
    }

    private async Task AddRequest(HttpContext httpContext, TrebllePayload payload)
    {
        try
        {
            payload.Data.Request.Timestamp = DateTime.UtcNow.ToString("yyyy-M-d H:m:s");
            string? serverIpAddress = httpContext.GetServerVariable("REMOTE_ADDR");
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
            // Pre-allocate dictionary with known capacity to reduce allocations
            var headers = new Dictionary<string, object>(httpContext.Request.Headers.Count);
            foreach (var header in httpContext.Request.Headers)
            {
                headers[header.Key] = string.Join(";", header.Value.ToArray());
            }
            payload.Data.Request.Headers = headers;
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

                // Early size check to prevent large request bodies from consuming memory
                const long maxRequestSize = 5 * 1024 * 1024; // 5MB
                var requestLength = httpContext.Request.ContentLength ?? 0;
                
                if (requestLength > maxRequestSize)
                {
                    payload.Data.Request.Body = new
                    {
                        __message = "Request data was larger than 5MB",
                        __size = requestLength,
                        __type = "large_request"
                    };
                    return;
                }

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
                        if (string.IsNullOrWhiteSpace(bodyData))
                        {
                            // Empty body is valid for requests like GET, HEAD, DELETE
                            payload.Data.Request.Body = null;
                        }
                        else if (IsValidJson(bodyData))
                        {
                            payload.Data.Request.Body = JsonSerializer.Deserialize<JsonElement>(bodyData, TreblleJsonContext.Default.JsonElement);
                        }
                        else
                        {
                            if (_treblleOptions.DebugMode)
                            {
                                _logger.LogDebug("Treblle Debug: Invalid JSON detected in request body");
                            }
                        }
                    }
                    else if (contentType.Contains("text/plain", StringComparison.OrdinalIgnoreCase))
                    {
                        payload.Data.Request.Body = bodyData;
                    }
                    else if (contentType.Contains("application/xml", StringComparison.OrdinalIgnoreCase))
                    {
                        var doc = XDocument.Parse(bodyData);
                        var jsonText = JsonSerializer.Serialize(ConvertXDocumentToObject(doc), TreblleJsonContext.Default.Object);
                        payload.Data.Request.Body = JsonSerializer.Deserialize<JsonElement>(jsonText, TreblleJsonContext.Default.JsonElement);
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
        if (response is not null && httpContext.Response?.ContentType is not null)
        {
            string? contentType = httpContext.Response?.ContentType;
            if (contentType?.Contains(MediaTypeNames.Application.Json, StringComparison.OrdinalIgnoreCase) == true
                || contentType?.Contains("application/problem+json", StringComparison.OrdinalIgnoreCase) == true)
            {
                // Align with total payload limit of 5MB
                const long maxResponseSize = 5 * 1024 * 1024; // 5MB
                var responseLength = httpContext.Response?.ContentLength ?? response.Length;
                
                if (responseLength > maxResponseSize)
                {
                    payload.Data.Response.Body = new
                    {
                        __message = "Response data was larger than 5MB",
                        __size = responseLength,
                        __type = "large_response"
                    };
                    payload.Data.Response.Size = responseLength;
                }
                else
                {
                    response.Position = 0;

                    {
                        try
                        {
                            using var responseReader = new StreamReader(response, leaveOpen: true);
                            var responseContent = await responseReader.ReadToEndAsync();

                            if (string.IsNullOrWhiteSpace(responseContent))
                            {
                                // Empty response body is valid for responses like 204 No Content
                                payload.Data.Response.Body = null;
                            }
                            else if (IsValidJson(responseContent))
                            {
                                payload.Data.Response.Body = JsonSerializer.Deserialize<JsonElement>(responseContent, TreblleJsonContext.Default.JsonElement);
                            }
                            else
                            {
                                if (_treblleOptions.DebugMode)
                                {
                                    _logger.LogDebug("Treblle Debug: Invalid JSON detected in response body");
                                }
                            }
                            payload.Data.Response.Size = response.Length;
                        }
                        catch (Exception e)
                        {
                            if (_treblleOptions.DebugMode)
                            {
                                _logger.LogDebug(e, "Treblle Debug: Error occurred while reading response content");
                            }

                        }
                    }
                }
            }
        }

        try
        {
            payload.Data.Response.Headers =
                httpContext.Response?.Headers?.ToDictionary(x => x.Key, x => (object)string.Join(";", x.Value.ToArray())) ?? new Dictionary<string, object>();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "An error occurred while attempting to read response headers. --- Exception message: {Message}",
                ex.Message);
        }

        payload.Data.Response.Code = httpContext.Response?.StatusCode ?? 500;
        payload.Data.Response.LoadTime = elapsedMilliseconds;
        
        // Set response size if not already set
        if (payload.Data.Response.Size == 0)
        {
            payload.Data.Response.Size = response?.Length ?? httpContext.Response?.ContentLength ?? 0;
        }
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


    private static string GetProgrammingLanguageVersion()
    {
#if NET8_0
        return "12";
#elif NET7_0
                return "11";
#else
                return "10";
#endif
    }

    private bool IsValidJson(string str)
    {
        try
        {
            JsonSerializer.Deserialize<JsonElement>(str, TreblleJsonContext.Default.JsonElement);
            return true;
        }
        catch (JsonException)
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
        
        // Pre-allocate with known capacity to avoid resizing
        var normalizedSegments = new List<string>(segments.Length);

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

    private static object ConvertXDocumentToObject(XDocument doc)
    {
        var result = new Dictionary<string, object>();
        if (doc.Root != null)
        {
            result[doc.Root.Name.LocalName] = ConvertXElementToObject(doc.Root);
        }
        return result;
    }

    private static object ConvertXElementToObject(XElement element)
    {
        if (!element.HasElements && !element.HasAttributes)
        {
            return element.Value;
        }

        var result = new Dictionary<string, object>();
        
        foreach (var attr in element.Attributes())
        {
            result[$"@{attr.Name.LocalName}"] = attr.Value;
        }
        
        foreach (var child in element.Elements())
        {
            var name = child.Name.LocalName;
            var value = ConvertXElementToObject(child);
            
            if (result.ContainsKey(name))
            {
                if (result[name] is List<object> list)
                {
                    list.Add(value);
                }
                else
                {
                    result[name] = new List<object> { result[name], value };
                }
            }
            else
            {
                result[name] = value;
            }
        }
        
        if (!element.HasElements && element.HasAttributes)
        {
            result["#text"] = element.Value;
        }
        
        return result;
    }

}
