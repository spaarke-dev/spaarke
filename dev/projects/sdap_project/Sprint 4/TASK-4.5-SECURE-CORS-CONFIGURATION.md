# Task 4.5: Secure CORS Configuration

**Sprint:** 4
**Priority:** 🔴 P0 BLOCKER
**Estimated Effort:** 2 hours
**Status:** Ready for Implementation
**Dependencies:** None

---

## Problem Statement

### Current State (SECURITY RISK)
The CORS configuration has a dangerous fallback that **allows all origins** (`AllowAnyOrigin()`) when no allowed origins are configured. This is a critical security vulnerability in production.

**Evidence:**
```csharp
// src/api/Spe.Bff.Api/Program.cs lines 20-40
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:3000"];

if (allowedOrigins.Length == 0 || allowedOrigins[0] == "*")
{
    policy.AllowAnyOrigin()  // ❌ DANGEROUS in production!
          .AllowAnyMethod()
          .AllowAnyHeader();
}
else
{
    policy.WithOrigins(allowedOrigins)
          .AllowCredentials()
          .AllowAnyMethod()
          .AllowAnyHeader();
}
```

**Critical Impact:**
- Production deployment may accidentally allow all origins
- Cross-origin attacks from malicious websites possible
- Credentials exposed to any origin if misconfigured
- Violates security best practices and compliance requirements

**Attack Scenario:**
1. Configuration error causes `AllowedOrigins` to be empty or `["*"]`
2. API accepts requests from `https://evil.com` with credentials
3. Attacker steals user tokens, accesses sensitive data

### Target State (SECURE)
Fail-closed CORS configuration that **rejects all requests** if allowed origins are misconfigured, rather than allowing everything.

---

## Architecture Context

### CORS Security Principles

**Principle 1: Fail-Closed**
- If configuration is missing or invalid → **reject all CORS requests**
- Never fall back to permissive settings

**Principle 2: Explicit Allowlist**
- Only specific, known origins allowed
- No wildcards in production
- Localhost only in development

**Principle 3: Credential Protection**
- `AllowCredentials()` must NEVER be combined with `AllowAnyOrigin()`
- This is a security violation and modern browsers reject it

**Principle 4: Environment-Specific Configuration**
- Development: Allow localhost variants
- Staging: Allow staging frontend URL only
- Production: Allow production frontend URL only

---

### Secure CORS Configuration Design

#### Development Environment
```json
{
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:3000",
      "http://localhost:3001",
      "http://127.0.0.1:3000"
    ]
  }
}
```

#### Staging Environment
```json
{
  "Cors": {
    "AllowedOrigins": [
      "https://sdap-staging.azurewebsites.net"
    ]
  }
}
```

#### Production Environment
```json
{
  "Cors": {
    "AllowedOrigins": [
      "https://sdap.contoso.com"
    ]
  }
}
```

---

## Solution Design

### Step 1: Update CORS Configuration Logic

**File:** `src/api/Spe.Bff.Api/Program.cs`

**Current Code (lines 20-50):**
```csharp
// Configure CORS
builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["http://localhost:3000"];

    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length == 0 || allowedOrigins[0] == "*")
        {
            policy.AllowAnyOrigin()  // ❌ DANGEROUS
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowCredentials()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
    });
});
```

**Secure Replacement:**
```csharp
// ============================================================================
// CORS - Secure, fail-closed configuration
// ============================================================================
builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();

    // Validate configuration
    if (allowedOrigins == null || allowedOrigins.Length == 0)
    {
        var environment = builder.Environment.EnvironmentName;

        // In development, allow localhost as fallback
        if (builder.Environment.IsDevelopment())
        {
            builder.Logging.LogWarning(
                "CORS: No allowed origins configured. Falling back to localhost (development only).");

            allowedOrigins = new[]
            {
                "http://localhost:3000",
                "http://localhost:3001",
                "http://127.0.0.1:3000"
            };
        }
        else
        {
            // FAIL-CLOSED: Throw exception in non-development environments
            throw new InvalidOperationException(
                $"CORS configuration is missing or empty in {environment} environment. " +
                "Configure 'Cors:AllowedOrigins' with explicit origin URLs. " +
                "CORS will NOT fall back to AllowAnyOrigin for security reasons.");
        }
    }

    // Reject wildcard configuration (security violation)
    if (allowedOrigins.Contains("*"))
    {
        throw new InvalidOperationException(
            "CORS: Wildcard origin '*' is not allowed. " +
            "Configure explicit origin URLs in 'Cors:AllowedOrigins'.");
    }

    // Validate origin URLs
    foreach (var origin in allowedOrigins)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"CORS: Invalid origin URL '{origin}'. Must be absolute URL (e.g., https://example.com).");
        }

        if (uri.Scheme != "https" && !builder.Environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"CORS: Non-HTTPS origin '{origin}' is not allowed in {builder.Environment.EnvironmentName} environment. " +
                "Use HTTPS URLs for security.");
        }
    }

    // Log allowed origins for audit trail
    builder.Logging.LogInformation(
        "CORS: Configured with {OriginCount} allowed origins: {Origins}",
        allowedOrigins.Length,
        string.Join(", ", allowedOrigins));

    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowCredentials()
              .AllowAnyMethod()
              .WithHeaders(
                  "Authorization",
                  "Content-Type",
                  "Accept",
                  "X-Requested-With")
              .WithExposedHeaders(
                  "X-Pagination-TotalCount",
                  "X-Pagination-HasMore")
              .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
    });
});
```

**Key Security Improvements:**
1. **Fail-Closed:** Throws exception if config missing (non-dev)
2. **Wildcard Rejected:** Explicit check for `"*"`
3. **URL Validation:** Ensures valid absolute URLs
4. **HTTPS Enforcement:** Non-dev environments require HTTPS
5. **Explicit Headers:** Whitelist specific headers instead of `AllowAnyHeader()`
6. **Audit Logging:** Log configured origins at startup

---

### Step 2: Update appsettings.json (Development)

**File:** `src/api/Spe.Bff.Api/appsettings.json`

**Add/Update CORS Section:**
```json
{
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:3000",
      "http://localhost:3001",
      "http://127.0.0.1:3000"
    ]
  }
}
```

---

### Step 3: Create appsettings.Staging.json

**File:** `src/api/Spe.Bff.Api/appsettings.Staging.json`

**Add:**
```json
{
  "Cors": {
    "AllowedOrigins": [
      "https://sdap-staging.azurewebsites.net"
    ]
  }
}
```

**Note:** Replace with actual staging frontend URL.

---

### Step 4: Create appsettings.Production.json (or Update Existing)

**File:** `src/api/Spe.Bff.Api/appsettings.Production.json`

**Add:**
```json
{
  "Cors": {
    "AllowedOrigins": []
  }
}
```

**Important:** Actual production origins should come from Azure App Configuration or environment variables, not committed to source control.

**Rationale:** Empty array in config file + Azure App Configuration override = fail-closed by default.

---

### Step 5: Add CORS Configuration Options Class (Optional Enhancement)

**File:** `src/api/Spe.Bff.Api/Configuration/CorsOptions.cs` (create new)

```csharp
namespace Spe.Bff.Api.Configuration;

/// <summary>
/// Configuration options for CORS policy.
/// </summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    /// <summary>
    /// List of allowed origin URLs for CORS requests.
    /// Must be absolute URLs (e.g., https://example.com).
    /// Wildcards are not allowed for security reasons.
    /// </summary>
    public string[] AllowedOrigins { get; init; } = [];

    /// <summary>
    /// Allowed HTTP headers for CORS requests.
    /// Defaults to common headers if not specified.
    /// </summary>
    public string[] AllowedHeaders { get; init; } =
    [
        "Authorization",
        "Content-Type",
        "Accept",
        "X-Requested-With"
    ];

    /// <summary>
    /// Headers exposed to browser JavaScript via CORS.
    /// </summary>
    public string[] ExposedHeaders { get; init; } =
    [
        "X-Pagination-TotalCount",
        "X-Pagination-HasMore"
    ];

    /// <summary>
    /// Whether to allow credentials (cookies, Authorization header).
    /// Must be true for authentication to work.
    /// </summary>
    public bool AllowCredentials { get; init; } = true;

    /// <summary>
    /// Maximum age for preflight cache in seconds.
    /// Defaults to 10 minutes.
    /// </summary>
    public int PreflightMaxAgeSeconds { get; init; } = 600;
}
```

**Then use strongly-typed options in Program.cs:**
```csharp
var corsOptions = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>()
    ?? new CorsOptions();

var allowedOrigins = corsOptions.AllowedOrigins;
// ... validation logic
```

---

### Step 6: Add CORS Validation on Startup

**File:** `src/api/Spe.Bff.Api/Program.cs`

**Add After CORS Configuration:**
```csharp
// Validate CORS configuration at startup (fail-fast)
app.Lifetime.ApplicationStarted.Register(() =>
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    var configuration = app.Services.GetRequiredService<IConfiguration>();

    var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();

    if (origins == null || origins.Length == 0)
    {
        if (!app.Environment.IsDevelopment())
        {
            logger.LogCritical(
                "CRITICAL: CORS allowed origins not configured in {Environment}. " +
                "API will reject all cross-origin requests.",
                app.Environment.EnvironmentName);
        }
    }
    else
    {
        logger.LogInformation(
            "CORS: Successfully configured with {Count} allowed origins",
            origins.Length);
    }
});
```

---

## Testing Strategy

### Unit Tests

**File:** `tests/unit/Spe.Bff.Api.Tests/CorsSecurityTests.cs` (create new)

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using Xunit;

namespace Spe.Bff.Api.Tests;

public class CorsSecurityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CorsSecurityTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CorsPreflightRequest_FromAllowedOrigin_Returns200()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/user/containers");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal("http://localhost:3000",
            response.Headers.GetValues("Access-Control-Allow-Origin").First());
    }

    [Fact]
    public async Task CorsRequest_FromDisallowedOrigin_RejectsCors()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/user/containers");
        request.Headers.Add("Origin", "https://evil.com");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        // Request may succeed but CORS headers should NOT be present
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task CorsRequest_WithCredentials_AllowsOnlySpecificOrigins()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/user/containers");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add("Cookie", "session=abc123");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.True(response.Headers.Contains("Access-Control-Allow-Credentials"));
        Assert.Equal("true",
            response.Headers.GetValues("Access-Control-Allow-Credentials").First());
    }
}
```

---

### Configuration Validation Tests

**File:** `tests/unit/Spe.Bff.Api.Tests/CorsConfigurationTests.cs` (create new)

```csharp
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Spe.Bff.Api.Tests;

public class CorsConfigurationTests
{
    [Fact]
    public void CorsConfiguration_WithWildcard_ThrowsException()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Cors:AllowedOrigins:0"] = "*"
            })
            .Build();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();

            if (origins != null && origins.Contains("*"))
            {
                throw new InvalidOperationException("Wildcard not allowed");
            }
        });

        Assert.Contains("Wildcard", exception.Message);
    }

    [Fact]
    public void CorsConfiguration_WithInvalidUrl_ThrowsException()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Cors:AllowedOrigins:0"] = "not-a-url"
            })
            .Build();

        // Act & Assert
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        Assert.False(Uri.TryCreate(origins[0], UriKind.Absolute, out _));
    }

    [Fact]
    public void CorsConfiguration_WithHttpInProduction_ShouldReject()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Cors:AllowedOrigins:0"] = "http://example.com",
                ["ASPNETCORE_ENVIRONMENT"] = "Production"
            })
            .Build();

        // Act
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        var uri = new Uri(origins[0]);

        // Assert - In production, non-HTTPS should be rejected
        if (configuration["ASPNETCORE_ENVIRONMENT"] == "Production")
        {
            Assert.NotEqual("http", uri.Scheme);
        }
    }
}
```

---

### Integration Tests

**File:** `tests/integration/Spe.Integration.Tests/CorsIntegrationTests.cs`

```csharp
using System.Net;
using Xunit;

namespace Spe.Integration.Tests;

[Collection("Integration")]
public class CorsIntegrationTests : IClassFixture<IntegrationTestFixture>
{
    private readonly IntegrationTestFixture _fixture;

    public CorsIntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("http://localhost:3000")]
    [InlineData("http://localhost:3001")]
    [InlineData("http://127.0.0.1:3000")]
    public async Task AllowedOrigins_ReceiveCorsHeaders(string origin)
    {
        // Arrange
        var client = _fixture.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/user/containers");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("Access-Control-Allow-Origin", response.Headers.Select(h => h.Key));
    }

    [Theory]
    [InlineData("https://evil.com")]
    [InlineData("http://not-allowed.com")]
    public async Task DisallowedOrigins_DoNotReceiveCorsHeaders(string origin)
    {
        // Arrange
        var client = _fixture.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/user/containers");
        request.Headers.Add("Origin", origin);

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.DoesNotContain("Access-Control-Allow-Origin", response.Headers.Select(h => h.Key));
    }
}
```

---

### Manual Testing with Browser

**Test 1: Preflight Request (OPTIONS)**
```bash
curl -X OPTIONS http://localhost:5001/api/user/containers \
  -H "Origin: http://localhost:3000" \
  -H "Access-Control-Request-Method: GET" \
  -v

# Expected headers in response:
# Access-Control-Allow-Origin: http://localhost:3000
# Access-Control-Allow-Credentials: true
# Access-Control-Allow-Methods: GET, POST, PUT, DELETE
```

**Test 2: Actual Request with CORS**
```bash
curl -X GET http://localhost:5001/api/user/containers \
  -H "Origin: http://localhost:3000" \
  -H "Authorization: Bearer {token}" \
  -v

# Expected headers in response:
# Access-Control-Allow-Origin: http://localhost:3000
# Access-Control-Allow-Credentials: true
```

**Test 3: Disallowed Origin**
```bash
curl -X GET http://localhost:5001/api/user/containers \
  -H "Origin: https://evil.com" \
  -H "Authorization: Bearer {token}" \
  -v

# Expected: No CORS headers in response
# Browser would block the response
```

---

## Deployment Checklist

### Azure App Service Configuration

**Add CORS Origins via Azure CLI:**
```bash
# Staging
az webapp config appsettings set \
  --name sdap-api-staging \
  --resource-group sdap-rg \
  --settings \
  Cors__AllowedOrigins__0="https://sdap-staging.azurewebsites.net"

# Production
az webapp config appsettings set \
  --name sdap-api-prod \
  --resource-group sdap-rg \
  --settings \
  Cors__AllowedOrigins__0="https://sdap.contoso.com"
```

**Alternative: Azure App Configuration**
```bash
az appconfig kv set \
  --name sdap-appconfig \
  --key "Cors:AllowedOrigins:0" \
  --value "https://sdap.contoso.com" \
  --label "Production"
```

---

### Startup Validation

**On Application Start (Check Logs):**
```
info: CORS: Configured with 1 allowed origins: https://sdap.contoso.com
```

**If Misconfigured (Non-Dev):**
```
crit: CRITICAL: CORS allowed origins not configured in Production.
      API will reject all cross-origin requests.
```

---

## Validation & Verification

### Success Criteria

✅ **Configuration Validation:**
- [ ] `AllowedOrigins` configured for all environments
- [ ] No wildcards in configuration
- [ ] HTTPS enforced in staging/production
- [ ] Fail-fast validation on startup

✅ **Security Tests:**
- [ ] Allowed origins receive CORS headers
- [ ] Disallowed origins do NOT receive CORS headers
- [ ] Wildcard configuration rejected at startup
- [ ] Invalid URLs rejected at startup

✅ **Deployment:**
- [ ] Staging environment: CORS configured correctly
- [ ] Production environment: CORS configured correctly
- [ ] Logs show configured origins at startup
- [ ] Frontend can successfully call API

✅ **Browser Testing:**
- [ ] Frontend on allowed origin can call API
- [ ] Browser DevTools shows no CORS errors
- [ ] Preflight requests succeed
- [ ] Credentials (auth tokens) work correctly

---

## Rollback Plan

**If CORS Configuration Breaks Frontend:**

1. **Temporary Workaround - Add Frontend Origin:**
   ```bash
   # Azure App Service
   az webapp config appsettings set \
     --name sdap-api-prod \
     --resource-group sdap-rg \
     --settings \
     Cors__AllowedOrigins__0="https://your-frontend-url.com"
   ```

2. **Restart application** - picks up new configuration

3. **Verify:** Check frontend can call API

**Emergency Rollback (Use Only If Absolutely Necessary):**
```csharp
// Program.cs - TEMPORARY EMERGENCY BYPASS (extremely dangerous)
policy.AllowAnyOrigin()
      .AllowAnyMethod()
      .WithHeaders("Authorization", "Content-Type");
      // NOTE: Cannot use AllowCredentials() with AllowAnyOrigin()
```

**Impact:** Removes credential support, breaks authentication.

---

## Known Issues & Limitations

### Issue #1: Multiple Frontend Domains
**Scenario:** Production has multiple frontend domains (e.g., US, EU regions)

**Solution:**
```json
{
  "Cors": {
    "AllowedOrigins": [
      "https://sdap-us.contoso.com",
      "https://sdap-eu.contoso.com",
      "https://sdap-asia.contoso.com"
    ]
  }
}
```

---

### Issue #2: Mobile App CORS
**Scenario:** Mobile apps don't send Origin header, CORS doesn't apply

**Solution:** CORS only affects browser requests. Mobile apps use direct HTTP calls (no CORS).

---

### Issue #3: Subdomain Wildcards
**Current Limitation:** Cannot use `https://*.contoso.com`

**Workaround:** List all subdomains explicitly

**Future Enhancement:** Implement custom CORS policy with regex matching

---

## References

### Documentation
- [CORS in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/cors)
- [CORS Specification](https://www.w3.org/TR/cors/)
- [OWASP CORS Security](https://cheatsheetseries.owasp.org/cheatsheets/CORS_Security_Cheat_Sheet.html)

### Security Resources
- [MDN: CORS](https://developer.mozilla.org/en-US/docs/Web/HTTP/CORS)
- [PortSwigger: CORS Attacks](https://portswigger.net/web-security/cors)

### Related Files
- `src/api/Spe.Bff.Api/Program.cs` (lines 20-50)

---

## AI Implementation Prompt

**Copy this prompt to your AI coding assistant:**

```
Secure CORS configuration to prevent allowing all origins in production.

CONTEXT:
- Current CORS config has dangerous AllowAnyOrigin() fallback
- Must implement fail-closed security (reject if misconfigured)
- Wildcard origins must be rejected

TASKS:
1. Update src/api/Spe.Bff.Api/Program.cs CORS configuration (lines 20-50):
   - Add validation to reject empty/null/wildcard origins in non-dev
   - Add URL validation (must be absolute URLs)
   - Enforce HTTPS in non-dev environments
   - Add logging for configured origins
   - Replace AllowAnyHeader with explicit header whitelist
   - Add preflight max age
2. Update src/api/Spe.Bff.Api/appsettings.json:
   - Add explicit localhost origins for development
3. Create src/api/Spe.Bff.Api/appsettings.Staging.json:
   - Add staging frontend URL
4. Update src/api/Spe.Bff.Api/appsettings.Production.json:
   - Add empty AllowedOrigins array (to be overridden by Azure config)
5. Add startup validation that logs CORS configuration
6. Run dotnet build and verify no errors

VALIDATION:
- Build succeeds with 0 errors
- App starts in development with localhost CORS
- App throws exception in production if CORS not configured

Reference Step 1 of this task document for the secure implementation.
```

---

**Task Owner:** [Assign to developer]
**Reviewer:** [Assign to senior developer/architect]
**Created:** 2025-10-02
**Updated:** 2025-10-02
