using Microsoft.AspNetCore.Http;
using System;
using System.Linq;

namespace Treblle.Net.Core;

internal static class ApiRequestFilter
{
    /// <summary>
    /// Content types that should be tracked by Treblle (API responses)
    /// </summary>
    private static readonly string[] TrackedContentTypes = new[]
    {
        "application/json",
        "application/xml",
        "application/x-www-form-urlencoded",
        "text/plain",
        "application/vnd.api+json",
        "application/ld+json",
        "application/hal+json",
        "application/problem+json"
    };

    /// <summary>
    /// Content types that should NOT be tracked (static resources and unsupported formats)
    /// </summary>
    private static readonly string[] ExcludedContentTypes = new[]
    {
        "text/html",
        "text/css",
        "text/javascript",
        "text/xml",
        "application/javascript",
        "application/x-javascript",
        "image/",
        "video/",
        "audio/",
        "font/",
        "application/font",
        "application/vnd.ms-fontobject",
        "application/x-font-ttf",
        "application/octet-stream"
    };

    /// <summary>
    /// Path patterns that should be automatically excluded (static resources)
    /// </summary>
    private static readonly string[] DefaultExcludedPaths = new[]
    {
        "/swagger/*",
        "/swagger-ui/*", 
        "/swagger-resources/*",
        "/webjars/*",
        "/assets/*",
        "/static/*",
        "/public/*",
        "/css/*",
        "/js/*",
        "/images/*",
        "/img/*",
        "/favicon.ico",
        "/robots.txt",
        "/sitemap.xml",
        "/_next/*",
        "/_nuxt/*",
        "/node_modules/*"
    };

    /// <summary>
    /// Determines if a request should be tracked by Treblle based on content type and path
    /// </summary>
    public static bool ShouldTrackRequest(HttpContext context, string[]? userExcludedPaths)
    {
        var path = context.Request.Path.Value ?? "/";

        // First check user-configured excluded paths
        if (PathMatcher.ShouldExcludePath(path, userExcludedPaths))
        {
            return false;
        }

        // Check default excluded paths (static resources)
        if (PathMatcher.ShouldExcludePath(path, DefaultExcludedPaths))
        {
            return false;
        }

        // SOAP is not supported - skip silently
        if (IsSoapRequest(context))
        {
            return false;
        }

        return true;
    }

    private static bool IsSoapRequest(HttpContext context)
    {
        // SOAP 1.1 uses a SOAPAction header
        if (context.Request.Headers.ContainsKey("SOAPAction"))
            return true;

        // WSDL fetches are GET requests with ?wsdl — no Content-Type to check
        if (context.Request.Method == HttpMethods.Get &&
            context.Request.Query.ContainsKey("wsdl"))
            return true;

        var contentType = context.Request.ContentType;
        if (contentType == null)
            return false;

        // SOAP 1.2 uses Content-Type: application/soap+xml
        if (contentType.Contains("application/soap+xml", StringComparison.OrdinalIgnoreCase))
            return true;

        // SOAP 1.1 uses Content-Type: text/xml
        if (contentType.Contains("text/xml", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Determines if a response should be tracked by Treblle based on its content type
    /// </summary>
    public static bool ShouldTrackResponse(HttpContext context)
    {
        var contentType = context.Response.ContentType;
        
        if (string.IsNullOrEmpty(contentType))
        {
            // If no content type, assume it's an API response
            return true;
        }

        // Normalize content type (remove charset, etc.)
        var normalizedContentType = contentType.Split(';')[0].Trim().ToLowerInvariant();

        // Check if it's an explicitly excluded content type
        if (ExcludedContentTypes.Any(excluded => 
            normalizedContentType.StartsWith(excluded, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // Check if it's a tracked content type
        if (TrackedContentTypes.Contains(normalizedContentType))
        {
            return true;
        }

        // For unknown content types, default to tracking if it looks like an API endpoint
        // (has /api/ in path or returns structured data status codes)
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
        var statusCode = context.Response.StatusCode;
        
        return path.Contains("/api/") || 
               (statusCode >= 200 && statusCode < 300) || // Success responses
               (statusCode >= 400 && statusCode < 500) || // Client errors (API validation, etc.)
               statusCode == 500; // Server errors
    }

    /// <summary>
    /// Gets the default excluded paths for documentation purposes
    /// </summary>
    public static string[] GetDefaultExcludedPaths() => DefaultExcludedPaths;
}