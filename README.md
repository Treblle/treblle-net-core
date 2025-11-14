# Treblle - API Intelligence Platform

[![Treblle API Intelligence](https://github.com/user-attachments/assets/b268ae9e-7c8a-4ade-95da-b4ac6fce6eea)](https://treblle.com)

[Website](http://treblle.com/) • [Documentation](https://docs.treblle.com/) • [Pricing](https://treblle.com/pricing)

Treblle is an API intelligence platfom that helps developers, teams and organizations understand their APIs from a single integration point.

---

## Treblle .NET Core SDK

[![Latest Version](https://img.shields.io/nuget/v/Treblle.Net.Core)](https://www.npmjs.com/package/Treblle.Net.Core)
[![Total Downloads](https://img.shields.io/nuget/dt/Treblle.Net.Core)](https://www.npmjs.com/package/treblle)

## Requirements

| Requirement | Version |
|-------------|---------|
| **.NET** | 6.0+ |
| **ASP.NET Core** | 6.0+ |
| **C#** | 10.0+ |

**Supported Platforms:**
- Windows, macOS, Linux
- Docker containers
- Azure, AWS, Google Cloud
- Any hosting platform supporting .NET 8+

**Supported API Types:**
- REST APIs with Controllers
- Minimal APIs
- Web APIs
- Mixed controller/minimal API applications

**Dependencies:**
- `Microsoft.AspNetCore.App` (framework reference)
- `System.Text.Json` 6.0+
- No external dependencies required

## Installation

### 1. Install the Package
You can install the Treblle JavaScript .NET Core via [NuGet](https://www.nuget.org/packages/Treblle.Net.Core/). Simply run the following command:

```bash
dotnet add package Treblle.Net.Core
```

### 2. Get Your Credentials
Get your SDK Token and API Key from the [Treblle Dashboard](https://platform.treblle.com).

### 3. Configure Treblle

**Option A: Environment Variables (Recommended for Production)**
```bash
export TREBLLE_SDK_TOKEN=your_sdk_token
export TREBLLE_API_KEY=your_api_key
```

Then use zero-configuration setup:
```csharp
// Import the Treblle SDK
using Treblle.Net.Core;

// Register Treblle Services
builder.Services.AddTreblle();

// Build your application
var app = builder.Build();

// Enable the Treblle Middleware
app.UseTreblle();
```

> **💡 Where to place this code:**
> - **Program.cs** (new minimal hosting model): Add `builder.Services.AddTreblle()` before `builder.Build()` and `app.UseTreblle()` after `var app = builder.Build()`
> - **Startup.cs** (legacy): Add `services.AddTreblle()` in `ConfigureServices()` and `app.UseTreblle()` in `Configure()`
> - **Web API templates**: Place after authentication/authorization middleware but before routing

**Using .env Files with DotNetEnv**

For development environments, you can use `.env` files with the DotNetEnv package:

```bash
dotnet add package DotNetEnv
```

Create a `.env` file in your project root:
```
TREBLLE_SDK_TOKEN=your_sdk_token
TREBLLE_API_KEY=your_api_key
```

Then load the environment variables in your `Program.cs`:
```csharp
// Import the Treblle SDK
using Treblle.Net.Core;

// Import the ENV Plugin
using DotNetEnv;

// Load environment variables from .env file
Env.Load();

// Register Treblle Services
builder.Services.AddTreblle();

// Build your application
var app = builder.Build();

// Enable the Treblle Middleware
app.UseTreblle();
```

> **💡 Note:** The `DotNetEnv` package is only needed if you want to load variables from `.env` files. Standard environment variables work without any additional packages.

**Option B: appsettings.json**
```json
{
  "Treblle": {
    "SdkToken": "your_sdk_token",
    "ApiKey": "your_api_key"
  }
}
```

```csharp
// Import the Treblle SDK
using Treblle.Net.Core;

// Register Treblle Services
builder.Services.AddTreblle();

// Build your application
var app = builder.Build();

// Enable the Treblle Middleware
app.UseTreblle();
```

**Option C: Manual Configuration**
```csharp
// Import the Treblle SDK
using Treblle.Net.Core;

// Register Treblle Services
builder.Services.AddTreblle("your_sdk_token", "your_api_key");

// Build your application
var app = builder.Build();

// Enable the Treblle Middleware
app.UseTreblle();
```

That's it! Treblle will now **automatically track all your API endpoints**. 🎉

## Configuration Options

Treblle offers several configuration options:

| Option | Default | Description |
|--------|---------|-------------|
| `ExcludedPaths` | `null` | Exclude specific paths or endpoints from Treblle |
| `DebugMode` | `false` | Enable detailed logging for troubleshooting |
| `DisableMasking` | `false` | Disable data masking if not needed |

**Example with all options:**
```csharp
// Import the Treblle SDK
using Treblle.Net.Core;

builder.Services.AddTreblle(options =>
{
    options.ExcludedPaths = new[] { "/health", "/admin/*", "/swagger/*" };
    options.DebugMode = true;
    options.DisableMasking = false;
});

// Build your application
var app = builder.Build();

// Enable the Treblle Middleware
app.UseTreblle();
```

### Debug Mode

For troubleshooting and development purposes, you can enable debug mode to get detailed logging about Treblle SDK operations:

```csharp
// Import the Treblle SDK
using Treblle.Net.Core;

// Option 1: Via AddTreblle parameter
builder.Services.AddTreblle("YOUR_SDK_TOKEN", "YOUR_API_KEY", null, disableMasking: false, debugMode: true);

// Option 2: Via configuration options
builder.Services.AddTreblle("YOUR_SDK_TOKEN", "YOUR_API_KEY");
builder.Services.Configure<TreblleOptions>(options =>
{
    options.DebugMode = true;
});

// Option 3: With auto-configuration
builder.Services.AddTreblle(options =>
{
    options.DebugMode = true;
});

// Build your application
var app = builder.Build();

// Enable the Treblle Middleware
app.UseTreblle();
```

**Debug Information Provided:**
- SDK Token and API Key validation messages
- Endpoint processing status (which routes have `[Treblle]` attribute)
- JSON parsing issues in request/response bodies
- Payload size limit notifications (>5MB)
- Network transmission errors
- Middleware initialization status

All debug logs are prefixed with "[TREBLLE]:" and use `LogDebug` level, making them easy to filter and control via your logging configuration.

**Important:** To see debug messages in the console, ensure your logging configuration allows Debug level messages for Treblle components. Add this to your `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Treblle.Net.Core": "Debug"
    }
  }
}
```

**Note:** Debug mode should typically only be enabled in development or staging environments as it increases log verbosity.

### Excluding endpoints or paths

By default, Treblle now automatically tracks **all endpoints** without requiring manual `[Treblle]` attributes. You can exclude specific paths using the `ExcludedPaths` configuration:

**Configuration:**
```csharp
// Import the Treblle SDK
using Treblle.Net.Core;

// Track all endpoints except specified exclusions
builder.Services.AddTreblle("YOUR_SDK_TOKEN", "YOUR_API_KEY", options =>
{
    options.ExcludedPaths = new[] { "/health", "/metrics", "/admin/*" };
});

// Build your application
var app = builder.Build();

// Enable the Treblle Middleware
app.UseTreblle();
```

**Advanced Exclusion Patterns:**
```csharp
builder.Services.AddTreblle("YOUR_SDK_TOKEN", "YOUR_API_KEY", options =>
{
    options.ExcludedPaths = new[] 
    {
        "/health",           // Exact match
        "/metrics",          // Exact match  
        "/admin/*",          // Wildcard: excludes /admin/users, /admin/settings, etc.
        "/api/v*/internal",  // Complex wildcard: excludes /api/v1/internal, /api/v2/internal, etc.
        "/debug/*",          // Wildcard: excludes all debug endpoints
        "/_*"                // Wildcard: excludes all endpoints starting with underscore
    };
    options.DebugMode = true;
});
```

**Pattern Matching Features:**
- **Case-insensitive** matching for all patterns
- **Exact matches**: `/health` matches only `/health`
- **Wildcard support**: 
  - `*` matches any number of characters
  - `?` matches exactly one character
- **Performance optimized** with regex compilation and result caching
- **Memory efficient** with bounded cache sizes


**Common Exclusion Examples:**
```csharp
options.ExcludedPaths = new[]
{
    // Health checks and monitoring
    "/health", "/healthz", "/ready", "/live",
    "/metrics", "/prometheus",
    
    // Admin and internal APIs
    "/admin/*", "/internal/*", "/_*",
    
    // Static assets (if serving through API)
    "/assets/*", "/static/*", "/public/*",
    
    // Development endpoints
    "/swagger/*", "/debug/*", "/dev/*"
};
```

### Masking Data
Treblle automatically masks sensitive fields in request and response bodies. The following fields are masked by default:

- `password`
- `pwd`
- `secret`
- `password_confirmation`
- `passwordConfirmation`
- `cc`
- `card_number`
- `cardNumber`
- `ccv`
- `ssn`
- `credit_score`
- `creditScore`

If you want to expand the list of fields you want to hide, you can pass a list of property names you want to hide and appropriate maskers to use as a key-value pairs to the `AddTreblle` call:

```csharp
// Import the Treblle SDK
using Treblle.Net.Core;

builder.Services.AddTreblle(
    builder.Configuration["Treblle:SdkToken"],
    builder.Configuration["Treblle:ApiKey"],
    new Dictionary<string, string>()
    {
        { "customercreditCard", "CreditCardMasker" }, 
        { "firstName", "DefaultStringMasker" }
    }
);


// Build your application
var app = builder.Build();

// Enable the Treblle Middleware
app.UseTreblle();
```

Available Maskers:
```csharp
// DefaultStringMasker
masker.Mask("Hello World");   // output: ***********
masker.Mask("1234-5678");     // output:  **********
// CreditCardMasker
masker.Mask("1234-5678-1234-5678"); // output:  ****-****-****-5678
masker.Mask("1234567812345678");    // output: ****-****-****-5678
//DateMasker
masker.Mask("24-12-2024");   // output: 24-12-****
//EmailMasker
masker.Mask("user123@example.com");  // output: *******@example.com
//PostalCodeMasker
masker.Mask("SW1A 1AA");   // output: SW1A ***
//SocialSecurityMasker
masker.Mask("123-45-6789");   // output: ***-**-6789
```

By extending DefaultStringMasker class and implementing IStringMasker interface you can implement custom masking classes for your needs.

### Disabling Data Masking

For high-volume scenarios where data masking is not required, you can disable it entirely to significantly improve performance and reduce memory usage:

```csharp
// Import the Treblle SDK
using Treblle.Net.Core;

// Option 1: Via AddTreblle parameter
builder.Services.AddTreblle("YOUR_SDK_TOKEN", "YOUR_API_KEY", null, disableMasking: true);

// Option 2: Via configuration options
builder.Services.AddTreblle("YOUR_SDK_TOKEN", "YOUR_API_KEY");
builder.Services.Configure<TreblleOptions>(options =>
{
    options.DisableMasking = true;
});

// Option 3: With auto-configuration
builder.Services.AddTreblle(options =>
{
    options.DisableMasking = true;
});

// Build your application
var app = builder.Build();

// Enable the Treblle Middleware
app.UseTreblle();
```

**Performance Impact:** Disabling masking can reduce memory usage by up to 70% for large payloads, as it skips JSON parsing, object tree creation, and field processing operations. This is particularly beneficial for APIs handling large response bodies or high request volumes.

## Upgrading to v2.0

Treblle .NET Core v2.0 introduces major improvements with **breaking changes**. This guide will help you migrate from v1.x.

- ✅ **Zero-Configuration Auto-Discovery** - No more manual `[Treblle]` attributes required
- ✅ **Auto-Configuration** - Automatic credential detection from environment/config
- ✅ **Smart Path Exclusions** - Wildcard patterns like `/admin/*`, `/api/v*/internal`
- ✅ **Enhanced Debug Mode** - Comprehensive logging for troubleshooting
- ✅ **Performance Optimizations** - Cached pattern matching, memory improvements
- ✅ **Cleaner API** - Removed legacy/deprecated properties

### 📋 Migration Guide

#### **Step 1: Update Package Reference**
```xml
<!-- Update your .csproj -->
<PackageReference Include="Treblle.Net.Core" Version="2.0.0" />
```

#### **Step 2: Update SDK Initialization**

```csharp
// ❌ v1.x
builder.Services.AddTreblle(
    builder.Configuration["Treblle:ApiKey"], 
    builder.Configuration["Treblle:ProjectId"]);

// ✅ v2.0
builder.Services.AddTreblle(); 
```

#### **Step 3: Update Naming Conventions**

**appsettings.json:**
```json
// ❌ v1.x
{
  "Treblle": {
    "ApiKey": "your_api_key",
    "ProjectId": "your_project_id"
  }
}

// ✅ v2.0
{
  "Treblle": {
    "SdkToken": "your_sdk_token",
    "ApiKey": "your_api_key"
  }
}
```

#### **Step 4: Remove Manual Attributes**
```csharp
// ❌ v1.x
[Treblle]
public class ProductsController : ControllerBase
{
    [Treblle]
    public IActionResult GetProducts() => Ok();
}

// ✅ v2.0
public class ProductsController : ControllerBase
{
    public IActionResult GetProducts() => Ok();
}
```

## Disabling Default .NET HTTP logging

You may notice HTTP client logging messages like:

```
info: System.Net.Http.HttpClient.Treblle.LogicalHandler[100]
      Start processing HTTP request POST https://rocknrolla.treblle.com/
info: System.Net.Http.HttpClient.Treblle.ClientHandler[101]
      Received HTTP response headers after 328ms - 200
```

These are normal operational messages showing Treblle successfully sending data to the API. They appear because .NET automatically logs HTTP requests at `Information` level.

To reduce these messages, configure logging in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "System.Net.Http.HttpClient.Treblle": "Warning"
    }
  }
}
```

Or in `Program.cs`:

```csharp
builder.Logging.AddFilter("System.Net.Http.HttpClient.Treblle", LogLevel.Warning);
```

## Getting Help

If you continue to experience issues:

1. Enable `debug: true` and check console output
2. Verify your SDK token and API key are correct in Treblle dashboard
3. Test with a simple endpoint first
4. Check [Treblle documentation](https://docs.treblle.com) for the latest updates
5. Contact support at <https://treblle.com> or email support@treblle.com

## Support

If you have problems of any kind feel free to reach out via <https://treblle.com> or email support@treblle.com and we'll do our best to help you out.

## License

Copyright 2025, Treblle Inc. Licensed under the MIT license:
http://www.opensource.org/licenses/mit-license.php
