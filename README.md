<div align="center">
  <img src="https://github.com/user-attachments/assets/2c6cafdf-3b1d-4938-8f7a-5feac9c68404"/>
</div>
<div align="center">

# Treblle

<a href="https://docs.treblle.com/en/integrations" target="_blank">Integrations</a>
<span>&nbsp;&nbsp;•&nbsp;&nbsp;</span>
<a href="http://treblle.com/" target="_blank">Website</a>
<span>&nbsp;&nbsp;•&nbsp;&nbsp;</span>
<a href="https://docs.treblle.com" target="_blank">Docs</a>
<span>&nbsp;&nbsp;•&nbsp;&nbsp;</span>
<a href="https://blog.treblle.com" target="_blank">Blog</a>
<span>&nbsp;&nbsp;•&nbsp;&nbsp;</span>
<a href="https://twitter.com/treblleapi" target="_blank">Twitter</a>
<span>&nbsp;&nbsp;•&nbsp;&nbsp;</span>
<a href="https://treblle.com/chat" target="_blank">Discord</a>
<br />

  <hr />
</div>

API Intelligence Platform. 🚀

Treblle is a lightweight SDK that helps Engineering and Product teams build, ship & maintain REST-based APIs faster.

## Features

<div align="center">
  <br />
  <img src="https://github.com/user-attachments/assets/02cbc06a-0c8b-4077-b696-e0e73df8588c"/>
  <br />
  <br />
</div>

- [API Monitoring & Observability](https://www.treblle.com/features/api-monitoring-observability)
- [Auto-generated API Docs](https://www.treblle.com/features/auto-generated-api-docs)
- [API analytics](https://www.treblle.com/features/api-analytics)
- [Treblle API Score](https://www.treblle.com/features/api-quality-score)
- [API Lifecycle Collaboration](https://www.treblle.com/features/api-lifecycle)
- [Native Treblle Apps](https://www.treblle.com/features/native-apps)


## How Treblle Works
Once you’ve integrated a Treblle SDK in your codebase, this SDK will send requests and response data to your Treblle Dashboard.

In your Treblle Dashboard you get to see real-time requests to your API, auto-generated API docs, API analytics like how fast the response was for an endpoint, the load size of the response, etc.

Treblle also uses the requests sent to your Dashboard to calculate your API score which is a quality score that’s calculated based on the performance, quality, and security best practices for your API.

> Visit [https://docs.treblle.com](http://docs.treblle.com) for the complete documentation.

## Security

### Masking fields
Masking fields ensure certain sensitive data are removed before being sent to Treblle.

To make sure masking is done before any data leaves your server [we built it into all our SDKs](https://docs.treblle.com/en/security/masked-fields#fields-masked-by-default).

This means data masking is super fast and happens on a programming level before the API request is sent to Treblle. You can [customize](https://docs.treblle.com/en/security/masked-fields#custom-masked-fields) exactly which fields are masked when you’re integrating the SDK.

> Visit the [Masked fields](https://docs.treblle.com/en/security/masked-fields) section of the [docs](https://docs.sailscasts.com) for the complete documentation.


## Get Started

1. Sign in to [Treblle](https://platform.treblle.com).
2. [Create a Treblle project](https://docs.treblle.com/en/dashboard/projects#creating-a-project).
3. [Setup the SDK](#install-the-SDK) for your platform.


## Quick Start

### 1. Install the Package
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
builder.Services.AddTreblle(); // Auto-detects credentials
app.UseTreblle();
```

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
builder.Services.AddTreblle(); // Auto-detects from config
app.UseTreblle();
```

**Option C: Manual Configuration**
```csharp
builder.Services.AddTreblle("your_sdk_token", "your_api_key");
app.UseTreblle();
```

That's it! Treblle will now **automatically track all your API endpoints**. 🎉

## Configuration Options Overview

Treblle v2.0 offers several configuration options:

| Option | Default | Description |
|--------|---------|-------------|
| `ExcludedPaths` | `null` | Skip tracking for specific paths (supports wildcards) |
| `DebugMode` | `false` | Enable detailed logging for troubleshooting |
| `DisableMasking` | `false` | Disable data masking for performance (reduces memory by 70%) |

**Example with options:**
```csharp
builder.Services.AddTreblle(options =>
{
    options.ExcludedPaths = new[] { "/health", "/admin/*", "/swagger/*" };
    options.DebugMode = true;           // Enable for development
    options.DisableMasking = false;     // Keep masking enabled for security
});
```

## How It Works

**🚀 Zero Configuration Required**  
Treblle v2.0 automatically tracks **all your API endpoints** without any manual setup. No more adding `[Treblle]` attributes or calling `.UseTreblle()` on individual routes.

**🎯 Smart Exclusions**  
Use `ExcludedPaths` to skip tracking for health checks, admin routes, or any endpoints you don't want monitored:

```csharp
builder.Services.AddTreblle(options =>
{
    options.ExcludedPaths = new[] 
    { 
        "/health",      // Exact match
        "/admin/*",     // Wildcard: excludes /admin/users, /admin/settings, etc.
        "/swagger/*"    // Exclude Swagger UI
    };
});
```

**🔧 Legacy Support**  
If you have existing `[Treblle]` attributes, they still work! You can remove them gradually or keep them for explicit control.

> See the [docs](https://docs.treblle.com/en/integrations/net-core) for this SDK to learn more.

### Masking Additional Fields

If you want to expand the list of fields you want to hide, you can pass a list of property names you want to hide and appropriate maskers to use as a key-value pairs to the `AddTreblle` call:

```csharp
builder.Services.AddTreblle(
    builder.Configuration["Treblle:SdkToken"],
    builder.Configuration["Treblle:ApiKey"],
    new Dictionary<string, string>( { { "customercreditCard", "CreditCardMasker" }, { "firstName", "DefaultStringMasker" } });
);
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
```

**Performance Impact:** Disabling masking can reduce memory usage by up to 70% for large payloads, as it skips JSON parsing, object tree creation, and field processing operations. This is particularly beneficial for APIs handling large response bodies or high request volumes.

### Debug Mode

For troubleshooting and development purposes, you can enable debug mode to get detailed logging about Treblle SDK operations:

```csharp
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
```

**Debug Information Provided:**
- SDK Token and API Key validation messages
- Endpoint processing status (which routes have `[Treblle]` attribute)
- JSON parsing issues in request/response bodies
- Payload size limit notifications (>5MB)
- Network transmission errors
- Middleware initialization status

All debug logs are prefixed with "Treblle Debug:" and use `LogDebug` level, making them easy to filter and control via your logging configuration.

**Note:** Debug mode should typically only be enabled in development or staging environments as it increases log verbosity.

### Zero-Configuration Auto-Discovery

By default, Treblle now automatically tracks **all endpoints** without requiring manual `[Treblle]` attributes. You can exclude specific paths using the `ExcludedPaths` configuration:

**New Configuration:**
```csharp
// Track all endpoints except specified exclusions
builder.Services.AddTreblle("YOUR_SDK_TOKEN", "YOUR_API_KEY", options =>
{
    options.ExcludedPaths = new[] { "/health", "/metrics", "/admin/*" };
});
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
    
    options.DebugMode = true; // See auto-discovery decisions in logs
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

**Migration from Manual Attributes:**
- **No changes required**: Existing `[Treblle]` attributes continue to work
- **Gradual migration**: You can remove `[Treblle]` attributes as desired
- **Override capability**: `[Treblle]` attributes take precedence over exclusions (unless explicitly excluded)


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

## Upgrading to v2.0 🚀

Treblle .NET Core v2.0 introduces major improvements with **breaking changes**. This guide will help you migrate from v1.x.

### 🔥 Major New Features

- ✅ **Zero-Configuration Auto-Discovery** - No more manual `[Treblle]` attributes required
- ✅ **Auto-Configuration** - Automatic credential detection from environment/config
- ✅ **Smart Path Exclusions** - Wildcard patterns like `/admin/*`, `/api/v*/internal`
- ✅ **Enhanced Debug Mode** - Comprehensive logging for troubleshooting
- ✅ **Performance Optimizations** - Cached pattern matching, memory improvements
- ✅ **Cleaner API** - Removed legacy/deprecated properties

### 🔄 Breaking Changes

#### 1. **Removed Legacy Properties**
```csharp
// ❌ v1.x - These properties no longer exist:
options.LegacyApiKey     // REMOVED
options.ProjectId        // REMOVED

// ✅ v2.0 - Use these instead:
options.SdkToken         // Clear, consistent naming
options.ApiKey           // Clear, consistent naming
```

#### 2. **Updated Configuration Keys**
```json
// ❌ v1.x appsettings.json
{
  "Treblle": {
    "ApiKey": "your-token",      // Confusing naming
    "ProjectId": "your-project"  // Confusing naming
  }
}

// ✅ v2.0 appsettings.json
{
  "Treblle": {
    "SdkToken": "your-token",    // Clear: authentication token
    "ApiKey": "your-project"     // Clear: project identifier
  }
}
```

#### 3. **Environment Variable Changes**
```bash
# ❌ v1.x environment variables - No longer supported
TREBLLE_API_KEY_LEGACY=your-token
TREBLLE_PROJECT_ID=your-project

# ✅ v2.0 environment variables
TREBLLE_SDK_TOKEN=your-token      # Clear: authentication token
TREBLLE_API_KEY=your-project      # Clear: project identifier
```

### 📋 Step-by-Step Migration Guide

#### **Step 1: Update Package Reference**
```xml
<!-- Update your .csproj -->
<PackageReference Include="Treblle.Net.Core" Version="2.0.0-beta.1" />
```

#### **Step 2: Choose Your Migration Path**

**Option A: Zero-Configuration (Recommended)**
```csharp
// ❌ v1.x - Manual configuration
builder.Services.AddTreblle(
    builder.Configuration["Treblle:ApiKey"], 
    builder.Configuration["Treblle:ProjectId"]);

// ✅ v2.0 - Auto-configuration
builder.Services.AddTreblle(); // That's it! Auto-detects from env/config
```

**Option B: Manual Configuration**
```csharp
// ❌ v1.x
builder.Services.AddTreblle("old-api-key", "old-project-id");

// ✅ v2.0 - Same values, clearer parameter names
builder.Services.AddTreblle("your-sdk-token", "your-api-key");
```

**Option C: Advanced Configuration**
```csharp
// ✅ v2.0 - New powerful configuration options
builder.Services.AddTreblle(options =>
{
    options.ExcludedPaths = new[] { "/health", "/admin/*" };
    options.DebugMode = true;
});
```

#### **Step 3: Update Configuration Sources**

Choose one of these approaches:

**Environment Variables (Production Recommended):**
```bash
export TREBLLE_SDK_TOKEN=your_sdk_token
export TREBLLE_API_KEY=your_api_key
```

**appsettings.json:**
```json
{
  "Treblle": {
    "SdkToken": "your_sdk_token",
    "ApiKey": "your_api_key"
  }
}
```

**Manual Configuration:**
```csharp
builder.Services.AddTreblle("your_sdk_token", "your_api_key");
```

#### **Step 4: Remove Manual Attributes (Optional)**
```csharp
// ❌ v1.x - Manual attributes required
[Treblle]
public class ProductsController : ControllerBase
{
    [Treblle]
    public IActionResult GetProducts() => Ok();
}

// ✅ v2.0 - Auto-discovery (attributes optional)
public class ProductsController : ControllerBase
{
    public IActionResult GetProducts() => Ok(); // Automatically tracked!
}

// Or exclude specific paths
builder.Services.AddTreblle(options =>
{
    options.ExcludedPaths = new[] { "/health", "/internal/*" };
});
```

#### **Step 5: Test Your Migration**

Enable debug mode to verify everything works:
```csharp
builder.Services.AddTreblle(options =>
{
    options.DebugMode = true; // See what's being tracked
});
```

### 🎯 Quick Migration Checklist

- [ ] Update to v2.0.0-beta.1 package
- [ ] Choose configuration approach (auto-config recommended)
- [ ] Update environment variables or appsettings.json
- [ ] Remove legacy configuration keys
- [ ] Test with debug mode enabled
- [ ] Optionally remove `[Treblle]` attributes (auto-discovery handles this)
- [ ] Verify excluded paths work as expected

### 🔧 Troubleshooting Common Issues

**"Treblle SDK Token not found" Error:**
```csharp
// Ensure you've set one of these:
// Environment: TREBLLE_SDK_TOKEN
// Config: Treblle:SdkToken in appsettings.json
// Or use manual: AddTreblle("token", "key")
```

**Too Many Endpoints Being Tracked:**
```csharp
builder.Services.AddTreblle(options =>
{
    options.ExcludedPaths = new[] 
    {
        "/health", "/healthz",
        "/swagger/*", 
        "/admin/*",
        "/_*" // Exclude all underscore routes
    };
});
```

**Performance Concerns:**
```csharp
builder.Services.AddTreblle(options =>
{
    options.DisableMasking = true; // 70% memory reduction
    options.ExcludedPaths = new[] { "/high-volume/*" };
});
```

### 💡 Pro Tips for v2.0

1. **Start with auto-configuration** - it's the simplest approach
2. **Use environment variables in production** - more secure than config files  
3. **Enable debug mode during migration** - see exactly what's happening
4. **Exclude noisy endpoints** - `/health`, `/metrics`, etc.
5. **Remove old `[Treblle]` attributes gradually** - they still work but aren't needed

### 📞 Need Help?

- 🐛 **Issues**: [GitHub Issues](https://github.com/Treblle/treblle-net-core/issues)
- 💬 **Community**: [Discord](https://treblle.com/chat)  
- 📖 **Docs**: [docs.treblle.com](https://docs.treblle.com)

---

### Running Treblle only in production

If you want to run Treblle only in production, you can rely on the environment variables, or use a similar approach via your application config.

```csharp
if (app.Environment.IsProduction())
{
    app.UseTreblle();
}
```

## Available SDKs

Treblle provides [open-source SDKs](https://docs.treblle.com/en/integrations) that let you seamlessly integrate Treblle with your REST-based APIs.

- [`treblle-laravel`](https://github.com/Treblle/treblle-laravel): SDK for Laravel
- [`treblle-php`](https://github.com/Treblle/treblle-php): SDK for PHP
- [`treblle-symfony`](https://github.com/Treblle/treblle-symfony): SDK for Symfony
- [`treblle-lumen`](https://github.com/Treblle/treblle-lumen): SDK for Lumen
- [`treblle-sails`](https://github.com/Treblle/treblle-sails): SDK for Sails
- [`treblle-adonisjs`](https://github.com/Treblle/treblle-adonisjs): SDK for AdonisJS
- [`treblle-fastify`](https://github.com/Treblle/treblle-fastify): SDK for Fastify
- [`treblle-directus`](https://github.com/Treblle/treblle-directus): SDK for Directus
- [`treblle-strapi`](https://github.com/Treblle/treblle-strapi): SDK for Strapi
- [`treblle-express`](https://github.com/Treblle/treblle-express): SDK for Express
- [`treblle-koa`](https://github.com/Treblle/treblle-koa): SDK for Koa
- [`treblle-go`](https://github.com/Treblle/treblle-go): SDK for Go
- [`treblle-ruby`](https://github.com/Treblle/treblle-ruby): SDK for Ruby on Rails
- [`treblle-python`](https://github.com/Treblle/treblle-python): SDK for Python/Django

> See the [docs](https://docs.treblle.com/en/integrations) for more on SDKs and Integrations.

## Other Packages

Besides the SDKs, we also provide helpers and configuration used for SDK
development. If you're thinking about contributing to or creating a SDK, have a look at the resources
below:

- [`treblle-utils`](https://github.com/Treblle/treblle-utils):  A set of helpers and
  utility functions useful for the JavaScript SDKs.
- [`php-utils`](https://github.com/Treblle/php-utils):   A set of helpers and
  utility functions useful for the PHP SDKs.

## Community 💙

First and foremost: **Star and watch this repository** to stay up-to-date.

Also, follow our [Blog](https://blog.treblle.com), and on [Twitter](https://twitter.com/treblleapi).

You can chat with the team and other members on [Discord](https://treblle.com/chat) and follow our tutorials and other video material at [YouTube](https://youtube.com/@treblle).

[![Treblle Discord](https://img.shields.io/badge/Treblle%20Discord-Join%20our%20Discord-F3F5FC?labelColor=7289DA&style=for-the-badge&logo=discord&logoColor=F3F5FC&link=https://treblle.com/chat)](https://treblle.com/chat)

[![Treblle YouTube](https://img.shields.io/badge/Treblle%20YouTube-Subscribe%20on%20YouTube-F3F5FC?labelColor=c4302b&style=for-the-badge&logo=YouTube&logoColor=F3F5FC&link=https://youtube.com/@treblle)](https://youtube.com/@treblle)

[![Treblle on Twitter](https://img.shields.io/badge/Treblle%20on%20Twitter-Follow%20Us-F3F5FC?labelColor=1DA1F2&style=for-the-badge&logo=Twitter&logoColor=F3F5FC&link=https://twitter.com/treblleapi)](https://twitter.com/treblleapi)

### How to contribute

Here are some ways of contributing to making Treblle better:

- **[Try out Treblle](https://docs.treblle.com/en/introduction#getting-started)**, and let us know ways to make Treblle better for you. Let us know here on [Discord](https://treblle.com/chat).
- Join our [Discord](https://treblle.com/chat) and connect with other members to share and learn from.
- Send a pull request to any of our [open source repositories](https://github.com/Treblle) on Github. Check the contribution guide on the repo you want to contribute to for more details about how to contribute. We're looking forward to your contribution!

### Contributors
<a href="https://github.com/Treblle/treblle-net-core/graphs/contributors">
  <p align="center">
    <img  src="https://contrib.rocks/image?repo=Treblle/treblle-net-core" alt="A table of avatars from the project's contributors" />
  </p>
</a>
