# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Treblle .NET Core SDK is a lightweight middleware library that provides API monitoring, observability, and auto-generated documentation for ASP.NET Core applications. It tracks API requests/responses and sends telemetry data to the Treblle platform.

## Build and Development Commands

```bash
# Build the project
dotnet build Treblle.Net.Core.sln

# Build in release mode
dotnet build Treblle.Net.Core.sln --configuration Release

# Clean build artifacts
dotnet clean Treblle.Net.Core.sln

# Pack NuGet package
dotnet pack TreblleCore/Treblle.Net.Core.csproj --configuration Release
```

## Architecture Overview

### Core Components

**TreblleMiddleware** (`TreblleCore/TreblleMiddleware.cs`): The main ASP.NET Core middleware that intercepts HTTP requests/responses. Uses a bounded channel for async payload processing to prevent memory buildup. Only processes endpoints marked with `[Treblle]` attribute.

**TreblleService** (`TreblleCore/TreblleService.cs`): Handles HTTP communication with Treblle API. Implements payload size limits (5MB) and optional data masking for security.

**TrebllePayloadFactory** (`TreblleCore/TrebllePayloadFactory.cs`): Constructs telemetry payloads from HTTP context data including request/response details, timing, and server information.

**Masking System** (`TreblleCore/Masking/`): Pluggable data masking framework for sanitizing sensitive information:
- Default maskers for common sensitive fields (passwords, credit cards, emails, SSNs)
- Extensible through `IStringMasker` interface
- Can be completely disabled for performance in high-volume scenarios

### Key Patterns

- **Background Processing**: Uses `System.Threading.Channels` for non-blocking payload transmission
- **Memory Management**: Bounded channels prevent unbounded growth; large payloads (>5MB) are truncated
- **Performance Optimization**: Optional masking disable, response size limits, efficient JSON serialization
- **Dependency Injection**: Full DI integration with `ServiceCollectionExtensions`

## Configuration

Integration requires:
1. `AddTreblle(sdkToken, apiKey)` in service configuration (new naming) OR `AddTreblle(apiKey, projectId)` (legacy, deprecated)
2. `UseTreblle()` middleware registration
3. `[Treblle]` attribute on controllers/endpoints OR `.UseTreblle()` on minimal API routes

**Parameter Naming Update**: 
- **New**: `AddTreblle(sdkToken, apiKey)` where `sdkToken` is sent as `api_key` in payload and `apiKey` is sent as `project_id`
- **Legacy**: `AddTreblle(apiKey, projectId)` (deprecated but still supported for backward compatibility)

Optional masking configuration can extend default sensitive field patterns or be disabled entirely for performance.

## Key Files

- `ServiceCollectionExtensions.cs`: DI container setup and default masking configuration
- `ApplicationBuilderExtensions.cs`: Middleware registration helpers
- `TreblleAttribute.cs`: Marker attribute for endpoint tracking
- `TreblleOptions.cs`: Configuration options including masking controls
- `RouteHandlerBuilderExtensions.cs`: Minimal API integration extensions

## Framework Dependencies

- .NET 8.0 target framework
- ASP.NET Core framework reference
- System.Text.Json for serialization
- HttpClientFactory for API communication