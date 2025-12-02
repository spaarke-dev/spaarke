# Task 4.2: Enable Authentication Middleware

**Sprint:** 4
**Priority:** 🔴 P0 BLOCKER
**Estimated Effort:** 1 day (8 hours)
**Status:** Ready for Implementation
**Dependencies:** Azure AD App Registration configured

---

## Problem Statement

### Current State (SECURITY RISK)
The application has **authorization policies configured** (30+ granular policies in Program.cs) but **no authentication middleware** enabled in the request pipeline. This means:
- User identity is never validated
- Bearer tokens are never verified
- Authorization policies run against unauthenticated users
- Security headers exist but no identity validation

**Critical Security Gap:**
```csharp
// Program.cs - Authorization policies exist...
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("canreadcontainers", policy => policy.RequireAuthenticatedUser())
    .AddPolicy("canmanagecontainers", policy => policy.RequireAuthenticatedUser());

// But NO authentication middleware configured!
// Missing: builder.Services.AddAuthentication(...)
// Missing: app.UseAuthentication()
```

**Impact:**
- All endpoints are effectively public (no identity validation)
- Authorization filters see null/anonymous user
- Production deployment would fail security audit
- Violates zero-trust security principles

### Target State (SECURE)
Enable Microsoft.Identity.Web authentication middleware with Azure AD JWT bearer token validation.

---

## Architecture Context

### Current Security Layers

**Layer 1: Authentication (MISSING - THIS TASK)**
- ❌ Validate JWT bearer tokens from Azure AD
- ❌ Extract user identity (ObjectId, UPN, roles)
- ❌ Populate HttpContext.User

**Layer 2: Authorization (EXISTS)**
- ✅ 30+ granular policies defined in Program.cs (lines 66-169)
- ✅ Endpoint-level authorization filters (ADR-008)
- ✅ Custom authorization rules (OperationAccessRule, TeamMembershipRule)
- ✅ Fail-closed design (401/403 on errors)

**Layer 3: Security Headers (EXISTS)**
- ✅ SecurityHeadersMiddleware adds HSTS, CSP, X-Frame-Options
- ✅ CORS configured (needs hardening in Task 4.5)

**Authentication Flow (After Fix):**
```
1. Client sends request with Bearer token
   ↓
2. Authentication Middleware validates JWT signature & claims
   ↓
3. HttpContext.User populated with ClaimsPrincipal
   ↓
4. Authorization Middleware checks policies/rules
   ↓
5. Endpoint handler executes (or 401/403 returned)
```

---

## Solution Design

### Step 1: Add NuGet Packages

**File:** `src/api/Spe.Bff.Api/Spe.Bff.Api.csproj` or `Directory.Packages.props`

**Required Packages:**
```xml
<PackageReference Include="Microsoft.Identity.Web" Version="3.4.0" />
<PackageReference Include="Microsoft.Identity.Web.MicrosoftGraph" Version="3.4.0" />
```

**Why Microsoft.Identity.Web?**
- Handles Azure AD B2C and Azure AD authentication
- Automatic token acquisition for downstream APIs (Graph, Dataverse)
- Integrates with `DefaultAzureCredential` pattern
- Supports OBO (On-Behalf-Of) flow for user context

---

### Step 2: Update Program.cs - Add Authentication Services

**File:** `src/api/Spe.Bff.Api/Program.cs`

**Location:** After `var builder = WebApplication.CreateBuilder(args);` (around line 15)

**Current State (lines 15-30):**
```csharp
var builder = WebApplication.CreateBuilder(args);

// Configure CORS
builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["http://localhost:3000"];
    // ...
});
```

**Add BEFORE CORS configuration:**
```csharp
var builder = WebApplication.CreateBuilder(args);

// ============================================================================
// AUTHENTICATION - Validate Azure AD JWT tokens
// ============================================================================
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(options =>
    {
        // Configure JWT bearer token validation
        builder.Configuration.Bind("AzureAd", options);

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(5), // Allow 5 min clock skew

            // Map Azure AD claims to standard claim types
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };

        // Event handlers for diagnostics
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();

                logger.LogError(
                    context.Exception,
                    "Authentication failed for request {RequestPath}. Token: {TokenPreview}",
                    context.Request.Path,
                    context.Request.Headers.Authorization.ToString().Substring(0, Math.Min(50, context.Request.Headers.Authorization.ToString().Length)));

                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();

                var userId = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? context.Principal?.FindFirst("oid")?.Value;

                logger.LogDebug(
                    "Token validated for user {UserId} on {RequestPath}",
                    userId,
                    context.Request.Path);

                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();

                logger.LogWarning(
                    "Authentication challenge issued for {RequestPath}. Error: {Error}, ErrorDescription: {ErrorDescription}",
                    context.Request.Path,
                    context.Error,
                    context.ErrorDescription);

                return Task.CompletedTask;
            }
        };
    },
    options =>
    {
        // Configure Microsoft Graph token acquisition for downstream API calls
        builder.Configuration.Bind("AzureAd", options);
    });

// Add Microsoft Graph API client (for user context operations)
builder.Services.AddMicrosoftGraph(options =>
{
    options.Scopes = ["User.Read", "Files.ReadWrite.All"];
});

// Configure CORS (existing code continues...)
builder.Services.AddCors(options =>
{
    // ... existing CORS config
});
```

**Required Using Statements (add to top of Program.cs):**
```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
```

---

### Step 3: Update Program.cs - Add Authentication Middleware

**File:** `src/api/Spe.Bff.Api/Program.cs`

**Location:** After `app.UseCors()` in the middleware pipeline (around line 240)

**Current State (lines 235-250):**
```csharp
app.UseCors();

// Security headers middleware
app.UseMiddleware<SecurityHeadersMiddleware>();

// Exception handling
app.UseExceptionHandler("/error");

// Routing
app.UseRouting();

// Health check
app.MapHealthChecks("/healthz");
```

**Update To:**
```csharp
app.UseCors();

// Security headers middleware
app.UseMiddleware<SecurityHeadersMiddleware>();

// Exception handling
app.UseExceptionHandler("/error");

// Routing
app.UseRouting();

// ============================================================================
// AUTHENTICATION & AUTHORIZATION
// ============================================================================
// CRITICAL: Authentication must come before Authorization
app.UseAuthentication();
app.UseAuthorization();

// Health check (unauthenticated)
app.MapHealthChecks("/healthz").AllowAnonymous();
```

**Important Pipeline Order:**
1. CORS → Security Headers → Exception Handler
2. Routing
3. **Authentication** (validates token, sets User)
4. **Authorization** (checks policies)
5. Endpoints

---

### Step 4: Add AzureAd Configuration

#### appsettings.json (Development)
**File:** `src/api/Spe.Bff.Api/appsettings.json`

**Add new section:**
```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "common",
    "ClientId": "YOUR-DEV-CLIENT-ID",
    "Audience": "api://YOUR-DEV-CLIENT-ID",
    "Scopes": "access_as_user",
    "CallbackPath": "/signin-oidc"
  },
  "MicrosoftGraph": {
    "BaseUrl": "https://graph.microsoft.com/v1.0",
    "Scopes": ["User.Read", "Files.ReadWrite.All"]
  }
}
```

**Configuration Explanation:**
- `Instance`: Azure AD authority URL (same for all tenants)
- `TenantId`:
  - `"common"` = multi-tenant (any Azure AD org)
  - `"organizations"` = any Azure AD org (not personal accounts)
  - `"{guid}"` = specific tenant (single-tenant app)
- `ClientId`: App registration ID from Azure AD
- `Audience`: Expected `aud` claim in JWT (usually `api://{ClientId}`)
- `Scopes`: Required scopes for accessing this API

---

#### appsettings.Production.json
**File:** `src/api/Spe.Bff.Api/appsettings.Production.json`

**Add:**
```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "YOUR-PRODUCTION-TENANT-ID",
    "ClientId": null,
    "Audience": null,
    "Scopes": "access_as_user"
  }
}
```

**Security Note:** `ClientId` and `Audience` should come from Azure Key Vault, not committed to source control.

---

### Step 5: Update Endpoint Definitions

Most endpoints already have `.RequireAuthorization()` calls, but verify and update:

**File:** `src/api/Spe.Bff.Api/Api/UserEndpoints.cs`

**Current (lines 20-30):**
```csharp
app.MapGet("/api/user/containers", async (
    [FromServices] IOboSpeService oboService,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    // Handler logic...
})
.RequireAuthorization("canreadcontainers");
```

**This is correct!** The `.RequireAuthorization()` will now work because authentication middleware is enabled.

---

**File:** `src/api/Spe.Bff.Api/Api/OBOEndpoints.cs`

Verify all endpoints have authorization requirements:
```csharp
.RequireAuthorization("canreadcontainers")  // or appropriate policy
```

---

### Step 6: Update GraphClientFactory for User Token

**File:** `src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs`

**Current Issue:** Uses client credentials (app-only) for all Graph calls.

**For OBO Operations:** Need to use user's token instead.

**Current Code (lines 75-95):**
```csharp
public GraphServiceClient CreateClient()
{
    var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ExcludeSharedTokenCacheCredential = true, // Deprecated warning
        // ...
    });

    return new GraphServiceClient(credential, _scopes);
}
```

**Add Method for User Context:**
```csharp
/// <summary>
/// Creates a Graph client using the user's access token (OBO flow).
/// </summary>
public GraphServiceClient CreateClientForUser(string userAccessToken)
{
    if (string.IsNullOrWhiteSpace(userAccessToken))
    {
        throw new ArgumentException("User access token is required", nameof(userAccessToken));
    }

    // Use the user's bearer token directly
    var credential = new SimpleTokenCredential(userAccessToken);

    return new GraphServiceClient(credential, _scopes);
}
```

**Update SimpleTokenCredential (already exists in codebase):**
```csharp
// File: src/api/Spe.Bff.Api/Infrastructure/Graph/SimpleTokenCredential.cs
// This class should already exist - verify it implements AccessToken properly
```

---

### Step 7: Update OboSpeService to Use User Token

**File:** `src/api/Spe.Bff.Api/Services/OboSpeService.cs`

**Current Constructor:**
```csharp
public OboSpeService(IGraphClientFactory graphClientFactory, ILogger<OboSpeService> logger)
{
    _graphClientFactory = graphClientFactory;
    _logger = logger;
}
```

**Update Methods to Accept HttpContext:**
```csharp
public async Task<ContainerListResponse> ListContainersAsync(HttpContext httpContext, CancellationToken ct)
{
    // Extract user token from Authorization header
    var userToken = ExtractBearerToken(httpContext);

    // Create Graph client with user's token (OBO)
    var graphClient = _graphClientFactory.CreateClientForUser(userToken);

    // Make Graph call with user's permissions
    var containers = await graphClient.Storage.FileStorage.Containers
        .GetAsync(ct);

    return MapToContainerListResponse(containers);
}

private string ExtractBearerToken(HttpContext httpContext)
{
    var authHeader = httpContext.Request.Headers.Authorization.ToString();

    if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        throw new UnauthorizedAccessException("Missing or invalid Authorization header");
    }

    return authHeader.Substring("Bearer ".Length).Trim();
}
```

**Alternative (Better):** Use `ITokenAcquisition` from Microsoft.Identity.Web
```csharp
private readonly ITokenAcquisition _tokenAcquisition;

public OboSpeService(
    IGraphClientFactory graphClientFactory,
    ITokenAcquisition tokenAcquisition,
    ILogger<OboSpeService> logger)
{
    _graphClientFactory = graphClientFactory;
    _tokenAcquisition = tokenAcquisition;
    _logger = logger;
}

public async Task<ContainerListResponse> ListContainersAsync(HttpContext httpContext, CancellationToken ct)
{
    // Acquire token using OBO flow (handles token caching, refresh)
    var userToken = await _tokenAcquisition.GetAccessTokenForUserAsync(
        scopes: ["https://graph.microsoft.com/.default"],
        user: httpContext.User);

    var graphClient = _graphClientFactory.CreateClientForUser(userToken);

    // ... rest of method
}
```

---

## Testing Strategy

### Unit Tests

**File:** `tests/unit/Spe.Bff.Api.Tests/AuthenticationTests.cs` (create new)

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace Spe.Bff.Api.Tests;

public class AuthenticationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthenticationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act - Call protected endpoint without token
        var response = await client.GetAsync("/api/user/containers");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithInvalidToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");

        // Act
        var response = await client.GetAsync("/api/user/containers");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HealthCheck_IsAnonymous_Returns200()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act - Health check should be anonymous
        var response = await client.GetAsync("/healthz");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithValidToken_CallsHandler()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace authentication with test authentication
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", options => { });
            });
        }).CreateClient();

        // Act
        var response = await client.GetAsync("/api/user/containers");

        // Assert
        // Should get past authentication (200, 500, or 400 depending on downstream services)
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

/// <summary>
/// Test authentication handler that auto-authenticates all requests.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock) : base(options, logger, encoder, clock)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
            new Claim(ClaimTypes.Name, "test@example.com"),
            new Claim("oid", "test-object-id")
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
```

---

### Integration Tests

**File:** `tests/integration/Spe.Integration.Tests/AuthenticationIntegrationTests.cs`

```csharp
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;
using System.Net.Http.Headers;
using Xunit;

namespace Spe.Integration.Tests;

[Collection("Integration")]
public class AuthenticationIntegrationTests : IClassFixture<IntegrationTestFixture>
{
    private readonly IntegrationTestFixture _fixture;

    public AuthenticationIntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RealAzureAdToken_AuthenticatesSuccessfully()
    {
        // Arrange - Acquire real token from Azure AD
        var clientId = _fixture.Configuration["AzureAd:ClientId"];
        var tenantId = _fixture.Configuration["AzureAd:TenantId"];
        var clientSecret = _fixture.Configuration["AzureAd:ClientSecret"]; // Test app registration

        var app = ConfidentialClientApplicationBuilder.Create(clientId)
            .WithClientSecret(clientSecret)
            .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
            .Build();

        var result = await app.AcquireTokenForClient(new[] { $"api://{clientId}/.default" })
            .ExecuteAsync();

        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);

        // Act
        var response = await client.GetAsync("/api/user/containers");

        // Assert - Should get past authentication (might fail authorization depending on app roles)
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredToken_Returns401()
    {
        // Arrange - Create JWT with past expiration
        var expiredToken = CreateExpiredJwtToken();

        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        // Act
        var response = await client.GetAsync("/api/user/containers");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

---

### Manual Testing with Postman

**1. Acquire Token via Azure AD:**

**Request:**
```http
POST https://login.microsoftonline.com/{tenant-id}/oauth2/v2.0/token
Content-Type: application/x-www-form-urlencoded

client_id={client-id}
&scope=api://{client-id}/.default
&client_secret={client-secret}
&grant_type=client_credentials
```

**Response:**
```json
{
  "access_token": "eyJ0eXAiOiJKV1QiLCJhbGc...",
  "expires_in": 3599,
  "token_type": "Bearer"
}
```

**2. Call API with Token:**

**Request:**
```http
GET https://localhost:5001/api/user/containers
Authorization: Bearer eyJ0eXAiOiJKV1QiLCJhbGc...
```

**Expected Response (Authenticated):**
```http
200 OK
{
  "containers": [...]
}
```

**Expected Response (No Token):**
```http
401 Unauthorized
WWW-Authenticate: Bearer error="invalid_token", error_description="The token is missing"
```

---

## Deployment Checklist

### Azure AD App Registration

**PowerShell Script to Create App Registration:**
```powershell
# Install Azure AD module
Install-Module -Name Az.Resources

# Connect to Azure
Connect-AzAccount

# Variables
$appName = "SDAP-API"
$replyUrls = @("https://sdap-api.azurewebsites.net/signin-oidc")

# Create app registration
$app = New-AzADApplication -DisplayName $appName `
    -ReplyUrls $replyUrls `
    -IdentifierUris "api://sdap-api"

# Create service principal
$sp = New-AzADServicePrincipal -ApplicationId $app.ApplicationId

# Output credentials
Write-Host "Application (Client) ID: $($app.ApplicationId)"
Write-Host "Tenant ID: $(Get-AzContext).Tenant.Id"
Write-Host "Client Secret: [Create in Azure Portal under Certificates & Secrets]"
```

**Manual Steps in Azure Portal:**
1. Navigate to Azure AD → App Registrations
2. Create new registration:
   - Name: `SDAP-API`
   - Supported account types: `Accounts in this organizational directory only`
   - Redirect URI: Leave blank (not needed for API)
3. Under "Expose an API":
   - Add scope: `access_as_user`
   - Authorized client applications: Add frontend app client ID
4. Under "Certificates & Secrets":
   - Create new client secret
   - Copy value immediately (shown only once)
5. Under "API Permissions":
   - Add Microsoft Graph: `Files.ReadWrite.All` (delegated)
   - Add Microsoft Graph: `User.Read` (delegated)

---

### Azure App Service Configuration

**Add Application Settings:**
```bash
az webapp config appsettings set `
    --name sdap-api-prod `
    --resource-group sdap-rg `
    --settings `
    AzureAd__TenantId="your-tenant-id" `
    AzureAd__ClientId="your-client-id" `
    AzureAd__Audience="api://your-client-id"

# Store client secret in Key Vault
az keyvault secret set `
    --vault-name sdap-keyvault `
    --name AzureAdClientSecret `
    --value "your-client-secret"

# Reference from App Service
az webapp config appsettings set `
    --name sdap-api-prod `
    --resource-group sdap-rg `
    --settings `
    AzureAd__ClientSecret="@Microsoft.KeyVault(SecretUri=https://sdap-keyvault.vault.azure.net/secrets/AzureAdClientSecret/)"
```

---

## Validation & Verification

### Success Criteria

✅ **Build & Compilation:**
- [ ] `dotnet build Spaarke.sln` completes with 0 errors
- [ ] No missing using statements

✅ **Unit Tests:**
- [ ] All existing authorization tests still pass
- [ ] New authentication tests pass (401 without token)

✅ **Integration Tests:**
- [ ] Real Azure AD token authenticates successfully
- [ ] Invalid token returns 401
- [ ] Health check remains anonymous (200 without token)

✅ **Functional Tests:**
- [ ] Postman: Call endpoint without token → 401
- [ ] Postman: Call endpoint with valid token → 200/403 (depending on authorization)
- [ ] Postman: Call endpoint with expired token → 401

✅ **Production Readiness:**
- [ ] Logs show "Token validated for user {UserId}"
- [ ] Application Insights tracks authentication failures
- [ ] No authentication errors in startup logs

---

## Rollback Plan

**If Authentication Breaks Endpoints:**

1. **Temporary Workaround - Disable Authentication:**
   ```csharp
   // Program.cs - Comment out authentication
   // app.UseAuthentication();
   // app.UseAuthorization();

   // Temporarily allow anonymous access to all endpoints
   app.MapGet("/api/user/containers", handler).AllowAnonymous();
   ```

2. **Restart application**

3. **Impact of Rollback:**
   - All endpoints publicly accessible (MAJOR SECURITY RISK)
   - Only use for emergency hotfix
   - Fix authentication ASAP

---

## Known Issues & Limitations

### Issue #1: Token Expiration During Long-Running Operations
**Symptom:** 401 errors mid-request for operations > 1 hour

**Mitigation:**
- Use refresh tokens for long operations
- Implement token refresh in middleware

### Issue #2: Multi-Tenant vs Single-Tenant
**Decision Required:** Should the API support external Azure AD tenants?

**Single-Tenant (Recommended for MVP):**
```json
{
  "AzureAd": {
    "TenantId": "{your-specific-tenant-guid}"
  }
}
```

**Multi-Tenant (Future):**
```json
{
  "AzureAd": {
    "TenantId": "organizations"
  }
}
```

---

## References

### Documentation
- [Microsoft.Identity.Web Docs](https://learn.microsoft.com/en-us/azure/active-directory/develop/microsoft-identity-web)
- [JWT Bearer Authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/)
- [On-Behalf-Of Flow](https://learn.microsoft.com/en-us/azure/active-directory/develop/v2-oauth2-on-behalf-of-flow)

### Related ADRs
- **ADR-008:** Authorization Endpoint Filters

### Related Files
- `src/api/Spe.Bff.Api/Program.cs` (lines 15-169, 235-250)
- `src/api/Spe.Bff.Api/Services/OboSpeService.cs`
- `src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs`

---

## AI Implementation Prompt

**Copy this prompt to your AI coding assistant:**

```
Enable Azure AD JWT authentication middleware to secure all API endpoints.

CONTEXT:
- Authorization policies exist but no authentication middleware
- All endpoints currently unauthenticated (security risk)
- Need to enable Microsoft.Identity.Web with JWT bearer validation

TASKS:
1. Add NuGet packages:
   - Microsoft.Identity.Web (3.4.0)
   - Microsoft.Identity.Web.MicrosoftGraph (3.4.0)
2. Update src/api/Spe.Bff.Api/Program.cs:
   - Add authentication services with Microsoft Identity Web (after line 15)
   - Add authentication/authorization middleware (after line 240, before endpoints)
   - Add required using statements
3. Update src/api/Spe.Bff.Api/appsettings.json:
   - Add AzureAd configuration section
   - Add MicrosoftGraph configuration section
4. Create src/api/Spe.Bff.Api/appsettings.Production.json:
   - Add production AzureAd settings (with null secrets)
5. Update health check endpoint to AllowAnonymous
6. Run dotnet build and verify no errors

VALIDATION:
- Build succeeds with 0 errors
- App starts without authentication errors
- Health check returns 200 without token
- Other endpoints return 401 without token

Reference the code examples in Steps 2-3 of this task document.
```

---

**Task Owner:** [Assign to developer]
**Reviewer:** [Assign to senior developer/architect]
**Created:** 2025-10-02
**Updated:** 2025-10-02
