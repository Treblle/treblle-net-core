using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Treblle.Net.Core;

internal sealed class TrebllePayloadFactory
{
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
        Exception? exception = null,
        TreblleAttribute? treblleAttribute = null,
        string? capturedRouteTemplate = null)
    {
        string? apiKey = treblleAttribute?.ApiKey;

        var payload = new TrebllePayload
        {
            Sdk = "net-core",
            Version = TreblleConstants.PayloadVersion,
            // Payload fields mirror the configuration options: the SDK Token travels as
            // sdk_token and the API Key as api_key. The x-api-key request header carries the
            // SDK Token and is unaffected by this mapping.
            ApiKey = apiKey ?? GetEffectiveApiKey(),
            SdkToken = GetEffectiveSdkToken(),
        };

        AddLanguage(payload);

        AddServer(httpContext, payload);

        await AddRequest(httpContext, payload, capturedRouteTemplate);

        await TryAddResponse(httpContext, response, elapsedMilliseconds, payload);

        TryAddError(exception, payload);

        payload.Data.Queries = TreblleQueryCollector.GetQueries().ToList();

        AddMetadata(httpContext, payload);

        payload.EstimatedSizeBytes = EstimateSizeBytes(httpContext, payload);

        return payload;
    }

    /// <summary>
    /// A cheap approximation of the payload's serialized size, for the sender's queue budget.
    /// The two bodies dominate; everything else is bounded by the schema and the header count.
    /// </summary>
    private static long EstimateSizeBytes(HttpContext httpContext, TrebllePayload payload)
    {
        const long fixedOverheadBytes = 2048;

        var requestBytes = httpContext.Request.ContentLength ?? 0;
        var responseBytes = payload.Data.Response.Size;

        return fixedOverheadBytes
            + (requestBytes > 0 ? requestBytes : 0)
            + (responseBytes > 0 ? responseBytes : 0);
    }

    /// <summary>
    /// Merges static options metadata with anything set during the request through
    /// <see cref="ITreblleMetadata"/>, per-request values winning on a key collision.
    /// </summary>
    /// <remarks>
    /// Reading the collector off <paramref name="httpContext"/> rather than taking it as a
    /// constructor dependency is deliberate: this factory is a singleton, so it cannot hold a
    /// scoped service. It also means the exception-handler payload path in
    /// ApplicationBuilderExtensions picks metadata up with no extra plumbing, since it
    /// re-executes on the same HttpContext and the same request scope.
    /// </remarks>
    private void AddMetadata(HttpContext httpContext, TrebllePayload payload)
    {
        try
        {
            var metadata = payload.Data.Metadata;

            // Static values first so per-request values overwrite them.
            var staticMetadata = _treblleOptions.Metadata;
            if (staticMetadata is not null)
            {
                foreach (var entry in staticMetadata)
                {
                    TreblleMetadataNormalizer.Write(metadata, entry.Key, entry.Value);
                }
            }

            // GetService, not GetRequiredService: the middleware can be registered without
            // AddTreblle having run, and missing metadata must never cost the payload.
            httpContext.RequestServices?.GetService<TreblleMetadataCollector>()?.CopyTo(metadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while adding metadata to Treblle payload.");
        }
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

    private async Task AddRequest(HttpContext httpContext, TrebllePayload payload, string? capturedRouteTemplate = null)
    {
        try
        {
            // InvariantCulture, and padded components. In a custom format string ':' is a
            // placeholder for the current culture's time separator rather than a literal, and
            // an app using UseRequestLocalization sets CurrentCulture from the caller's
            // Accept-Language header — which let the caller influence the rendered timestamp.
            payload.Data.Request.Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            payload.Data.Request.Ip = ResolveClientIp(httpContext);
            payload.Data.Request.Url = httpContext.Request.GetDisplayUrl();
            payload.Data.Request.Query = httpContext.Request.QueryString.ToString();
            // Prefer the route template captured before _next. UseExceptionHandler (when registered after
            // UseTreblle) calls SetEndpoint(null) when handling errors, so GetEndpoint() is unreliable here.
            // Fall back to GetEndpoint() for apps that register UseTreblle before UseRouting — endpoint is
            // null before _next in that setup but is resolved by the time we reach this point for 2xx responses.
            var routeTemplate = capturedRouteTemplate
                ?? (httpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern?.RawText;
            // A null template means ASP.NET Core matched no registered route, so there is no route to
            // report. Leave route_path null rather than inferring one from the request path — the
            // platform relies on its presence to tell a real route from unmatched traffic.
            payload.Data.Request.RoutePath = FormatRoutePath(routeTemplate);
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

    /// <summary>
    /// Resolves the calling client's IP address.
    /// </summary>
    /// <remarks>
    /// This used to read the REMOTE_ADDR server variable, which requires IServerVariablesFeature
    /// — implemented by IIS and not by Kestrel. Every Linux and container deployment therefore
    /// reported "bogon" for every request.
    /// <para>
    /// X-Forwarded-For is consulted first so that an app behind a proxy reports the caller
    /// rather than the proxy without extra configuration. An app that has configured
    /// UseForwardedHeaders still gets the right answer: that middleware consumes the header and
    /// promotes the value onto RemoteIpAddress, so the fallback below is what applies. Note that
    /// an unproxied app reports whatever X-Forwarded-For the caller chose to send.
    /// </para>
    /// </remarks>
    private static string ResolveClientIp(HttpContext httpContext)
    {
        var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].ToString();

        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Comma-separated chain; the left-most entry is the original client.
            var separator = forwardedFor.IndexOf(',');
            var client = (separator >= 0 ? forwardedFor.Substring(0, separator) : forwardedFor).Trim();

            if (client.Length > 0)
            {
                return client;
            }
        }

        var remoteIpAddress = httpContext.Connection.RemoteIpAddress;

        if (remoteIpAddress is not null)
        {
            if (remoteIpAddress.IsIPv4MappedToIPv6)
            {
                remoteIpAddress = remoteIpAddress.MapToIPv4();
            }

            return remoteIpAddress.ToString();
        }

        // IIS still exposes it this way when the connection feature is unavailable.
        var serverIpAddress = httpContext.GetServerVariable("REMOTE_ADDR");

        return !string.IsNullOrEmpty(serverIpAddress) ? serverIpAddress : "bogon";
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
                    payload.Data.Request.Body = new Dictionary<string, object>
                    {
                        ["__message"] = "Request data was larger than 5MB",
                        ["__size"] = requestLength,
                        ["__type"] = "large_request"
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
                    payload.Data.Request.Body = new Dictionary<string, object>
                    {
                        ["__type"] = "file",
                        ["length"] = length,
                        ["contentType"] = contentType
                    };
                }
                else if (httpContext.Request.HasFormContentType)
                {
                    var form = await httpContext.Request.ReadFormAsync();
                    var files = form.Files;
                    var fileList = new List<object>();

                    foreach (var file in files)
                    {
                        fileList.Add(new Dictionary<string, object>
                        {
                            ["name"] = file.FileName,
                            ["contentType"] = file.ContentType,
                            ["length"] = file.Length,
                            ["fieldName"] = file.Name
                        });
                    }

                    if (fileList.Count > 0)
                    {
                        payload.Data.Request.Body = new Dictionary<string, object>
                        {
                            ["Files"] = fileList
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
                        try
                        {
                            var doc = XDocument.Parse(bodyData);
                            var jsonText = JsonSerializer.Serialize(ConvertXDocumentToObject(doc), TreblleJsonContext.Default.Object);
                            payload.Data.Request.Body = JsonSerializer.Deserialize<JsonElement>(jsonText, TreblleJsonContext.Default.JsonElement);
                        }
                        catch (Exception)
                        {
                            if (_treblleOptions.DebugMode)
                            {
                                _logger.LogDebug("[TREBLLE]: Skipping request body - XML contains invalid characters");
                            }
                        }
                    }
                    else
                    {
                        // Non-JSON or unknown types, store minimal info
                        payload.Data.Request.Body = new Dictionary<string, object>
                        {
                            ["__type"] = "non-json",
                            ["contentType"] = contentType
                        };
                    }
                }

                if (httpContext.Request.Body.CanSeek)
                {
                    httpContext.Request.Body.Position = 0;
                }
            }

            if (payload.Data.Request.Body == null && httpContext.Request.Query.Count > 0)
            {
                payload.Data.Request.Body = ParseQueryToDictionary(httpContext.Request.Query);
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

    // Parses IQueryCollection into a nested dictionary, expanding bracket notation.
    // "filter[time_period]=all&limit=50" → {"filter": {"time_period": "all"}, "limit": "50"}
    private static Dictionary<string, object> ParseQueryToDictionary(IQueryCollection query)
    {
        var result = new Dictionary<string, object>();

        foreach (var param in query)
        {
            var key   = param.Key;
            var value = param.Value.ToString();

            var bracketStart = key.IndexOf('[');
            if (bracketStart > 0 && key.EndsWith(']'))
            {
                var outerKey = key[..bracketStart];
                var innerKey = key[(bracketStart + 1)..^1];

                if (!result.TryGetValue(outerKey, out var existing) || existing is not Dictionary<string, object> nested)
                {
                    nested = new Dictionary<string, object>();
                    result[outerKey] = nested;
                }

                ((Dictionary<string, object>)result[outerKey])[innerKey] = value;
            }
            else
            {
                result[key] = value;
            }
        }

        return result;
    }

    private async Task TryAddResponse(HttpContext httpContext, MemoryStream? response, long elapsedMilliseconds, TrebllePayload payload)
    {
        if (response is not null && httpContext.Response?.ContentType is not null)
        {
            string contentType = httpContext.Response.ContentType;

            // Align with total payload limit of 5MB
            const long maxResponseSize = 5 * 1024 * 1024; // 5MB
            var responseLength = httpContext.Response.ContentLength ?? response.Length;
            
            if (responseLength > maxResponseSize)
            {
                payload.Data.Response.Body = new Dictionary<string, object?>
                {
                    ["__message"] = "Response data was larger than 5MB",
                    ["__size"] = responseLength,
                    ["__type"] = "large_response"
                };
                payload.Data.Response.Size = responseLength;
            }
            else if (contentType.Contains(MediaTypeNames.Application.Json, StringComparison.OrdinalIgnoreCase) == true
                || contentType.Contains("application/problem+json", StringComparison.OrdinalIgnoreCase) == true)
            {
                response.Position = 0;
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
            else if (contentType.Contains("application/xml", StringComparison.OrdinalIgnoreCase))
            {
                response.Position = 0;
                try
                {
                    using var responseReader = new StreamReader(response, leaveOpen: true);
                    var responseContent = await responseReader.ReadToEndAsync();
                    if (string.IsNullOrWhiteSpace(responseContent))
                    {
                        payload.Data.Response.Body = null;
                    }
                    else
                    {
                        var doc = XDocument.Parse(responseContent);
                        var jsonText = JsonSerializer.Serialize(ConvertXDocumentToObject(doc), TreblleJsonContext.Default.Object);
                        payload.Data.Response.Body = JsonSerializer.Deserialize<JsonElement>(jsonText, TreblleJsonContext.Default.JsonElement);
                    }
                    payload.Data.Response.Size = response.Length;
                }
                catch (Exception e)
                {
                    if (_treblleOptions.DebugMode)
                    {
                        _logger.LogDebug(e, "Treblle Debug: Error occurred while reading XML response content");
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
    }


    private static string GetProgrammingLanguageVersion()
    {
#if NET9_0_OR_GREATER
        return "13";
#elif NET8_0_OR_GREATER
        return "12";
#elif NET7_0_OR_GREATER
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

    /// <summary>
    /// Formats a route template for the payload: exactly one leading slash, literal segments lowercased.
    /// RawText is "api/[controller]" for attribute routes but "/api/items" for a minimal API declared with
    /// a leading slash, so prefixing unconditionally emitted "//api/items" and kept minimal-API endpoints
    /// from grouping with controller ones. Attribute routing also substitutes [controller] and [action]
    /// with the C# class and method names, so RawText carries identifier casing ("/api/Mps/Check") that
    /// matches neither the URL callers use nor what other Treblle SDKs report for the same endpoint. Route
    /// matching is case-insensitive, so the lowercased form is equivalent. Anything inside braces is
    /// preserved verbatim — parameter names and constraints surface as parameter identifiers in the
    /// generated documentation.
    /// </summary>
    private static string? FormatRoutePath(string? routeTemplate)
    {
        if (routeTemplate is null)
        {
            return null;
        }

        var template = routeTemplate.TrimStart('/');
        var builder = new StringBuilder(template.Length + 1);
        builder.Append('/');
        var depth = 0;

        foreach (var character in template)
        {
            if (character == '{')
            {
                depth++;
                builder.Append(character);
            }
            else if (character == '}')
            {
                if (depth > 0)
                {
                    depth--;
                }
                builder.Append(character);
            }
            else
            {
                builder.Append(depth == 0 ? char.ToLowerInvariant(character) : character);
            }
        }

        return builder.ToString();
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
            if (attr.IsNamespaceDeclaration) continue;
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
