# Dataverse Authentication Guide - Complete Reference

**Purpose**: Definitive guide for Dataverse authentication in the Spaarke SDAP project
**Last Updated**: 2025-10-06
**Status**: ✅ PRODUCTION READY

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [The Problem We Solved](#the-problem-we-solved)
3. [Why This Matters](#why-this-matters)
4. [The Correct Solution](#the-correct-solution)
5. [Historical Context](#historical-context)
6. [Implementation Details](#implementation-details)
7. [Configuration Requirements](#configuration-requirements)
8. [Testing and Verification](#testing-and-verification)
9. [Common Pitfalls to Avoid](#common-pitfalls-to-avoid)
10. [Troubleshooting Guide](#troubleshooting-guide)
11. [References](#references)

---

## Executive Summary

**TL;DR**: For server-to-server Dataverse authentication in .NET 8 applications, **ALWAYS use `ServiceClient` from `Microsoft.PowerPlatform.Dataverse.Client` with a connection string**. Do NOT attempt to use custom HttpClient implementations with manual token handling.

### Quick Reference

```csharp
// ✅ CORRECT: Use ServiceClient with connection string
using Microsoft.PowerPlatform.Dataverse.Client;

var connectionString = $"AuthType=ClientSecret;SkipDiscovery=true;Url={dataverseUrl};ClientId={clientId};ClientSecret={clientSecret};RequireNewInstance=true";
var serviceClient = new ServiceClient(connectionString);

// ❌ WRONG: Custom HttpClient with manual token handling
var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
var token = await credential.GetTokenAsync(new TokenRequestContext(new[] { $"{dataverseUrl}/.default" }));
httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
```

---

## The Problem We Solved

### Issue Timeline (2025-10-06)

**Duration**: ~4 hours of investigation
**Impact**: SDAP BFF API could not connect to Dataverse, blocking Sprint 7A testing

### Symptoms

1. Health check endpoint returned 503:
   ```json
   {"status":503,"detail":"Dataverse connection test failed"}
   ```

2. All authenticated endpoints timed out or returned 500 errors

3. API logs showed:
   ```
   System.UriFormatException: Invalid URI: The URI scheme is not valid.
   ```
   Later:
   ```
   Response status code does not indicate success: 500 (Internal Server Error).
   ```

### What We Tried (That Didn't Work)

1. ❌ Switching from Managed Identity to ClientSecretCredential
2. ❌ Fixing KeyVault URL typos
3. ❌ Adding Managed Identity to Key Vault permissions
4. ❌ Registering Managed Identity as Application User in Dataverse
5. ❌ Granting admin consent to API permissions
6. ❌ Adding direct App Service configuration settings

**All of these were red herrings.** The configuration was actually correct.

### Root Cause

**The custom HttpClient implementation in `DataverseWebApiService.cs` was fundamentally incompatible with Dataverse for server-to-server authentication.**

Even though:
- ✅ Token acquisition worked (verified via PowerShell)
- ✅ Application User existed and was enabled
- ✅ All permissions were correct
- ✅ Same credentials worked in PowerShell

The .NET `ClientSecretCredential` + manual HttpClient approach returned HTTP 500 from Dataverse.

---

## Why This Matters

### The Cost of Getting This Wrong

1. **4+ hours of debugging** every time this breaks
2. **False starts** chasing red herrings (Managed Identity, KeyVault, permissions)
3. **Blocked testing** - can't test PCF controls without working API
4. **Lost context** - Sprint 4 never actually tested Dataverse (only WireMock tests)

### Why Custom HttpClient Fails

Microsoft's Dataverse service expects specific token claims, request patterns, and SDK behavior that `ServiceClient` provides but custom implementations don't. The exact reason is undocumented, but the pattern is clear:

- **ServiceClient**: ✅ Works reliably
- **Custom HttpClient**: ❌ Returns 500 errors

**Do not try to debug why.** Just use `ServiceClient`.

---

## The Correct Solution

### Architecture

```
┌─────────────────────┐
│  Spe.Bff.Api        │
│  (ASP.NET Core)     │
└──────────┬──────────┘
           │
           │ Uses
           ▼
┌─────────────────────────────────┐
│ DataverseServiceClientImpl      │
│ implements IDataverseService    │
└──────────┬──────────────────────┘
           │
           │ Creates
           ▼
┌─────────────────────────────────┐
│ ServiceClient                   │
│ (Microsoft SDK)                 │
│ Package: Microsoft.PowerPlatform│
│         .Dataverse.Client       │
└──────────┬──────────────────────┘
           │
           │ Connection String:
           │ AuthType=ClientSecret
           │ Url=https://...
           │ ClientId=...
           │ ClientSecret=...
           │
           ▼
┌─────────────────────────────────┐
│ Azure AD                        │
│ - Acquires token                │
│ - Client credentials flow       │
└──────────┬──────────────────────┘
           │
           │ Bearer token
           ▼
┌─────────────────────────────────┐
│ Dataverse Web API               │
│ https://spaarkedev1             │
│   .api.crm.dynamics.com         │
└─────────────────────────────────┘
```

### Implementation

**File**: `src/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs`

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Spaarke.Dataverse;

public class DataverseServiceClientImpl : IDataverseService, IDisposable
{
    private readonly ServiceClient _serviceClient;
    private readonly ILogger<DataverseServiceClientImpl> _logger;
    private bool _disposed = false;

    public DataverseServiceClientImpl(
        IConfiguration configuration,
        ILogger<DataverseServiceClientImpl> logger)
    {
        _logger = logger;

        var dataverseUrl = configuration["Dataverse:ServiceUrl"];
        var clientId = configuration["API_APP_ID"];
        var clientSecret = configuration["Dataverse:ClientSecret"];

        if (string.IsNullOrEmpty(dataverseUrl))
            throw new InvalidOperationException("Dataverse:ServiceUrl configuration is required");

        if (string.IsNullOrEmpty(clientId))
            throw new InvalidOperationException("API_APP_ID configuration is required");

        if (string.IsNullOrEmpty(clientSecret))
            throw new InvalidOperationException("Dataverse:ClientSecret configuration is required");

        // Connection string format per Microsoft documentation
        var connectionString = $"AuthType=ClientSecret;SkipDiscovery=true;Url={dataverseUrl};ClientId={clientId};ClientSecret={clientSecret};RequireNewInstance=true";

        _logger.LogInformation("Initializing Dataverse ServiceClient for {DataverseUrl}", dataverseUrl);

        try
        {
            _serviceClient = new ServiceClient(connectionString);

            if (!_serviceClient.IsReady)
            {
                var error = _serviceClient.LastError ?? "Unknown error";
                _logger.LogError("Failed to initialize Dataverse ServiceClient: {Error}", error);
                throw new InvalidOperationException($"Failed to connect to Dataverse: {error}");
            }

            _logger.LogInformation("Dataverse ServiceClient connected successfully to {OrgName} ({OrgId})",
                _serviceClient.ConnectedOrgFriendlyName, _serviceClient.ConnectedOrgId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception initializing Dataverse ServiceClient");
            throw;
        }
    }

    // ... implement IDataverseService methods using _serviceClient ...

    public void Dispose()
    {
        if (!_disposed)
        {
            _serviceClient?.Dispose();
            _disposed = true;
        }
    }
}
```

### Dependency Injection Registration

**File**: `src/api/Spe.Bff.Api/Program.cs`

```csharp
// Dataverse service - using ServiceClient for .NET 8.0 with client secret authentication
builder.Services.AddScoped<IDataverseService, DataverseServiceClientImpl>();
```

### Package Reference

**File**: `src/shared/Spaarke.Dataverse/Spaarke.Dataverse.csproj`

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" />
  <!-- Version managed in Directory.Packages.props -->
</ItemGroup>
```

**File**: `Directory.Packages.props`

```xml
<ItemGroup>
  <PackageVersion Include="Microsoft.PowerPlatform.Dataverse.Client" Version="1.1.32" />
</ItemGroup>
```

---

## Historical Context

### Sprint 2 (August 2025)
- **Implementation**: Used `ServiceClient` (then called `DataverseService`)
- **Status**: ✅ Correct implementation
- **File**: `src/shared/Spaarke.Dataverse/DataverseService.cs`
- **Auth Pattern**: Managed Identity callback function

### Sprint 3 (October 1, 2025)
- **Change**: Replaced `ServiceClient` with custom `DataverseWebApiService` using HttpClient
- **Reason**: "For .NET 8.0 compatibility" (incorrect assumption)
- **Status**: ❌ This broke Dataverse authentication
- **File**: `src/shared/Spaarke.Dataverse/DataverseWebApiService.cs`
- **Archived**: `_archive/DataverseService.cs.archived-2025-10-01`

**Key Mistake**: The comment said "Provides full .NET 8.0 compatibility without WCF/System.ServiceModel dependencies" but this was a misunderstanding. `ServiceClient` from `Microsoft.PowerPlatform.Dataverse.Client` IS compatible with .NET 8!

### Sprint 4 (October 3, 2025)
- **Status**: Used broken `DataverseWebApiService`
- **Testing**: Only WireMock tests (mocked Dataverse API)
- **Result**: ❌ **Dataverse was never actually tested against real environment**
- **Evidence**: No deployment logs, no production testing, no integration tests with real Dataverse

**This is why we thought it was working** - the WireMock tests passed, but they don't validate authentication.

### Sprint 7A (October 6, 2025)
- **Discovery**: Dataverse authentication completely broken
- **Investigation**: 4 hours debugging
- **Solution**: Restored `ServiceClient` approach with client secret authentication
- **Status**: ✅ Working and verified

---

## Configuration Requirements

### 1. Azure App Registration

**Name**: Spaarke DSM-SPE Dev 2
**App ID**: `170c98e1-d486-4355-bcbe-170454e0207c`
**Tenant ID**: `a221a95e-6abc-4434-aecc-e48338a1b2f2`

#### Required API Permissions

| API | Permission | Type | Admin Consent |
|-----|-----------|------|---------------|
| Dynamics CRM | `user_impersonation` | Delegated | ✅ Required |
| Dynamics CRM | Application (for S2S) | Application | ✅ Required |

**Specific Permission IDs**:
- `4d114b1a-3649-4764-9dfb-be1e236ff371` - user_impersonation (Scope)
- `19766c1b-905b-43af-8756-06526ab42875` - Application permission (Role)

#### Client Secret

- **Storage**: Azure Key Vault `spaarke-spekvcert`
- **Secret Name**: `BFF-API-ClientSecret`
- **Value**: `~Ac8Q~JGnsrvNEODvFo8qmtKbgj1PmwmJ6GVUaJj`
- **Expires**: 2027-09-22
- **Created**: 2025-09-29

### 2. Dataverse Application User

**Location**: Power Platform Admin Center → SPAARKE DEV 1 → Settings → Users + permissions → Application users

**Configuration**:
- **Name**: Spaarke DSM-SPE Dev 2
- **App ID**: `170c98e1-d486-4355-bcbe-170454e0207c`
- **Security Role**: System Administrator
- **Status**: Active ✅
- **Business Unit**: Spaarke

**How to Verify**:
1. Go to https://admin.powerplatform.microsoft.com/
2. Environments → SPAARKE DEV 1
3. Settings → Users + permissions → Application users
4. Look for "Spaarke DSM-SPE Dev 2"
5. Verify Status = "Active"
6. Verify Security Role includes "System Administrator"

### 3. Azure App Service Configuration

**App Service**: `spe-api-dev-67e2xz`
**Resource Group**: `spe-infrastructure-westus2`

#### Required App Settings

| Setting | Value | Source |
|---------|-------|--------|
| `TENANT_ID` | `a221a95e-6abc-4434-aecc-e48338a1b2f2` | Direct |
| `API_APP_ID` | `170c98e1-d486-4355-bcbe-170454e0207c` | Direct |
| `Dataverse__ServiceUrl` | `https://spaarkedev1.api.crm.dynamics.com` | Direct |
| `Dataverse__ClientSecret` | `~Ac8Q~JGnsrvNEODvFo8qmtKbgj1PmwmJ6GVUaJj` | Direct |

**Note**: These are configured as **direct App Service settings**, not KeyVault references. This ensures they're immediately available without KeyVault resolution delays.

#### Alternative: KeyVault References (Optional)

```json
{
  "Dataverse": {
    "ServiceUrl": "@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/SPRK-DEV-DATAVERSE-URL)",
    "ClientSecret": "@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/BFF-API-ClientSecret)"
  }
}
```

If using KeyVault references:
- Managed Identity must be enabled on App Service
- Managed Identity must have "Key Vault Secrets User" role on Key Vault
- KeyVault URL must be exactly correct (no typos!)

### 4. appsettings.json Configuration

**File**: `src/api/Spe.Bff.Api/appsettings.json`

```json
{
  "Dataverse": {
    "ServiceUrl": "@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/SPRK-DEV-DATAVERSE-URL)",
    "ClientSecret": "@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/BFF-API-ClientSecret)"
  }
}
```

**Important**: The code reads from `IConfiguration`, which merges:
1. appsettings.json
2. Environment variables
3. App Service settings (highest priority)

App Service settings override appsettings.json.

---

## Testing and Verification

### 1. Health Check Test

```bash
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz/dataverse
```

**Expected Response**:
```json
{"status":"healthy","message":"Dataverse connection successful"}
```

**If this fails**, the Dataverse authentication is broken.

### 2. PowerShell Verification Script

Use this to verify credentials work outside of .NET:

```powershell
$tenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2"
$clientId = "170c98e1-d486-4355-bcbe-170454e0207c"
$clientSecret = "~Ac8Q~JGnsrvNEODvFo8qmtKbgj1PmwmJ6GVUaJj"
$resource = "https://spaarkedev1.api.crm.dynamics.com"

# Acquire token
$body = @{
    grant_type    = "client_credentials"
    client_id     = $clientId
    client_secret = $clientSecret
    scope         = "$resource/.default"
}

$tokenResponse = Invoke-RestMethod -Method Post `
    -Uri "https://login.microsoftonline.com/$tenantId/oauth2/v2.0/token" `
    -Body $body

Write-Host "✅ Token acquired successfully!" -ForegroundColor Green

# Test WhoAmI
$headers = @{
    Authorization = "Bearer $($tokenResponse.access_token)"
    Accept = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
}

$whoAmI = Invoke-RestMethod -Method Get `
    -Uri "$resource/api/data/v9.2/WhoAmI" `
    -Headers $headers

Write-Host "✅ WhoAmI successful!" -ForegroundColor Green
Write-Host "User ID: $($whoAmI.UserId)"
Write-Host "Organization ID: $($whoAmI.OrganizationId)"
```

**If PowerShell succeeds but .NET fails**, the issue is NOT with credentials or permissions. It's with how .NET is using them.

### 3. Manual Dataverse CRUD Test

```bash
# Get all documents (requires valid bearer token)
curl -H "Authorization: Bearer {token}" \
     -H "OData-Version: 4.0" \
     -H "Accept: application/json" \
     https://spaarkedev1.api.crm.dynamics.com/api/data/v9.2/sprk_documents
```

---

## Common Pitfalls to Avoid

### ❌ Pitfall 1: Using Custom HttpClient

**DON'T DO THIS**:
```csharp
var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
var token = await credential.GetTokenAsync(new TokenRequestContext(new[] { $"{dataverseUrl}/.default" }));
httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
var response = await httpClient.GetAsync("WhoAmI");
```

**Result**: HTTP 500 from Dataverse

**Instead**: Use `ServiceClient` with connection string

### ❌ Pitfall 2: Using Managed Identity for Dataverse

**DON'T DO THIS**:
```csharp
var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
{
    ManagedIdentityClientId = managedIdentityClientId
});
```

**Result**: Managed Identity cannot obtain Dataverse tokens

**Reason**: Dataverse requires App Registration with Client Secret for S2S, not Managed Identity

**Instead**: Use Client Secret authentication with App Registration

### ❌ Pitfall 3: Trusting WireMock Tests for Authentication

**DON'T ASSUME**:
```csharp
[Fact]
public async Task GetDocument_ReturnsDocument()
{
    _mockServer.Given(Request.Create().WithPath("/api/data/v9.2/sprk_documents(*)"))
               .RespondWith(Response.Create().WithStatusCode(200));

    var result = await _service.GetDocumentAsync("test-id");
    Assert.NotNull(result); // ✅ Test passes
}
```

**Problem**: This tests the HTTP layer, NOT authentication

**Instead**: Have integration tests that connect to real Dataverse (or use test environment)

### ❌ Pitfall 4: KeyVault URL Typos

The KeyVault URL in appsettings.json was:
- **Wrong**: `https://spaarke-spevcert.vault.azure.net` (missing 'k')
- **Correct**: `https://spaarke-spekvcert.vault.azure.net`

**Symptom**: Configuration values not resolved, appear as `null`

**Fix**: Double-check KeyVault name exactly matches

### ❌ Pitfall 5: Missing Admin Consent

**Symptom**:
```
AADSTS65001: The user or administrator has not consented to use the application
```

**Fix**:
```bash
az ad app permission admin-consent --id 170c98e1-d486-4355-bcbe-170454e0207c
```

### ❌ Pitfall 6: Application User Not Created/Enabled

**Symptom**: HTTP 401 or 403 from Dataverse

**Fix**:
1. Go to Power Platform Admin Center
2. Create Application User for the App Registration
3. Assign appropriate Security Role
4. Ensure Status = "Active"

---

## Troubleshooting Guide

### Problem: Health check returns 503

#### Step 1: Check App Service logs
```bash
az webapp log tail --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2
```

Look for:
- `Dataverse connection test failed`
- `Failed to initialize Dataverse ServiceClient`
- Exception details

#### Step 2: Verify configuration values
```bash
az webapp config appsettings list --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2
```

Confirm these exist:
- `TENANT_ID`
- `API_APP_ID`
- `Dataverse__ServiceUrl`
- `Dataverse__ClientSecret`

#### Step 3: Test credentials with PowerShell
Run the PowerShell verification script above.

- ✅ If succeeds: Issue is with .NET code (check you're using `ServiceClient`)
- ❌ If fails: Issue is with credentials/permissions

#### Step 4: Check Application User in Dataverse
1. Power Platform Admin Center
2. SPAARKE DEV 1 → Application users
3. Find "Spaarke DSM-SPE Dev 2"
4. Verify Status = "Active"
5. Verify Security Role assigned

#### Step 5: Check client secret expiration
```bash
az ad app credential list --id 170c98e1-d486-4355-bcbe-170454e0207c
```

If expired, create new secret and update KeyVault.

### Problem: ServiceClient.IsReady = false

Check `ServiceClient.LastError`:

```csharp
if (!_serviceClient.IsReady)
{
    var error = _serviceClient.LastError;
    _logger.LogError("ServiceClient not ready: {Error}", error);
}
```

Common errors:
- **"Unable to Login to Dynamics CRM"**: Check URL, credentials
- **"User does not have required privileges"**: Check Security Role
- **"Organization is disabled"**: Check Dataverse environment status

### Problem: "Unable to find a valid instance of the service"

**Symptom**: ServiceClient constructor throws exception

**Causes**:
1. Dataverse URL incorrect
2. Organization disabled
3. Network/firewall issues

**Fix**: Verify URL exactly: `https://spaarkedev1.api.crm.dynamics.com` (with `.api`)

### Problem: HTTP 401 Unauthorized

**Causes**:
1. Client secret expired
2. Application User not created
3. Admin consent not granted

**Fix**: Follow steps in Configuration Requirements section

### Problem: HTTP 403 Forbidden

**Causes**:
1. Application User doesn't have required Security Role
2. Security Role doesn't have privileges on `sprk_document` entity

**Fix**:
1. Check Security Role assigned to Application User
2. Verify Security Role has Create/Read/Write/Delete on `sprk_document`

---

## References

### Microsoft Documentation

1. **Authenticating .NET Applications**
   - **File**: `docs/KM-DATAVERSE-AUTHENTICATE-DOTNET-APPS.md`
   - **Key Section**: Lines 50-57 (.NET Core and .NET 6 applications)
   - **Quote**: "For .NET Core application development there is a `DataverseServiceClient` class... You can download the Microsoft.PowerPlatform.Dataverse.Client package"

2. **Use OAuth with Dataverse**
   - **File**: `docs/KM-DATAVERSE-TO-APP-AUTHENTICATION.md`
   - **Key Section**: Lines 280-386 (Connect as an app)
   - **Connection String Format**: Lines 349-361

3. **ServiceClient Connection Strings**
   - URL: https://docs.microsoft.com/en-us/power-apps/developer/data-platform/xrm-tooling/use-connection-strings-xrm-tooling-connect
   - Format: `AuthType=ClientSecret;Url={url};ClientId={id};ClientSecret={secret}`

### Sprint Documentation

1. **Sprint 4 Architecture**
   - **File**: `dev/projects/sdap_project/Sprint 4/ARCHITECTURE-DOC-UPDATE-COMPLETE.md`
   - **Status**: Used broken `DataverseWebApiService` but never tested real Dataverse

2. **Sprint 7A Dataverse Auth Fix**
   - **File**: `dev/projects/sdap_project/Sprint 7/TASK-7A-DATAVERSE-AUTH-FIX.md`
   - **Details**: Complete investigation timeline and solution

3. **Sprint 7A API Verification**
   - **File**: `dev/projects/sdap_project/Sprint 7/TASK-7A-API-VERIFICATION.md`
   - **Details**: Health checks, testing strategy, current status

### Code References

1. **Current Implementation (CORRECT)**
   - **File**: `src/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs`
   - **Pattern**: ServiceClient with connection string
   - **Status**: ✅ Working

2. **Previous Implementation (BROKEN)**
   - **File**: `src/shared/Spaarke.Dataverse/DataverseWebApiService.cs`
   - **Pattern**: Custom HttpClient with ClientSecretCredential
   - **Status**: ❌ Returns HTTP 500

3. **Original Implementation (CORRECT)**
   - **File**: `src/shared/Spaarke.Dataverse/_archive/DataverseService.cs.archived-2025-10-01`
   - **Pattern**: ServiceClient with Managed Identity callback
   - **Status**: ✅ Was working (Sprint 2/3)

### Git History

- **Sprint 2 (Working)**: Commit `1282013` - Uses ServiceClient
- **Sprint 3 (Broken)**: Commit `d650b9f` - Replaced with DataverseWebApiService
- **Sprint 4 (Untested)**: Commit `2403b22` - Continued using broken implementation
- **Sprint 7A (Fixed)**: Commit TBD - Restored ServiceClient approach

---

## Decision Record

**Date**: 2025-10-06
**Decision**: Always use `ServiceClient` for Dataverse authentication

### Context
Multiple approaches exist for Dataverse authentication. We tried custom HttpClient implementations for "better .NET 8 compatibility" but this created reliability issues.

### Decision
**ALWAYS use `Microsoft.PowerPlatform.Dataverse.Client.ServiceClient` for Dataverse operations.**

### Rationale

1. **Reliability**: ServiceClient is tested and supported by Microsoft
2. **Compatibility**: Works with .NET 8 despite earlier assumptions
3. **Simplicity**: Connection string approach is straightforward
4. **Support**: Microsoft documentation recommends this approach
5. **Proven**: PowerShell tests prove credentials work; ServiceClient makes same calls

### Consequences

✅ **Positive**:
- Reliable Dataverse connectivity
- No more authentication debugging
- Follows Microsoft best practices
- Simpler code (less custom logic)

⚠️ **Negative**:
- Adds dependency on Microsoft SDK (~10MB)
- Slightly less control over HTTP details
- Must follow SDK's patterns

### Alternatives Considered

1. **Custom HttpClient**: ❌ Rejected - unreliable, returns 500 errors
2. **Managed Identity**: ❌ Rejected - doesn't work for Dataverse S2S
3. **CrmServiceClient (.NET Framework)**: ❌ Rejected - not .NET 8 compatible

### Review Date
- Should be reviewed if Microsoft releases new Dataverse SDKs
- Should be reviewed if upgrading to .NET 9+

---

## Quick Start Checklist

Use this checklist when setting up Dataverse authentication in a new environment:

### Azure Setup

- [ ] Create Azure App Registration
- [ ] Add Client Secret (save it immediately!)
- [ ] Add Dynamics CRM API permissions
- [ ] Grant admin consent to API permissions
- [ ] Store secret in Azure Key Vault (optional)

### Dataverse Setup

- [ ] Open Power Platform Admin Center
- [ ] Go to environment → Application users
- [ ] Create Application User with App Registration ID
- [ ] Assign System Administrator role (or appropriate role)
- [ ] Verify Status = "Active"

### Code Setup

- [ ] Add NuGet package: `Microsoft.PowerPlatform.Dataverse.Client`
- [ ] Create ServiceClient implementation (see code above)
- [ ] Register in DI: `services.AddScoped<IDataverseService, DataverseServiceClientImpl>()`
- [ ] Configure connection string format

### Configuration

- [ ] Add `TENANT_ID` to App Service settings
- [ ] Add `API_APP_ID` to App Service settings
- [ ] Add `Dataverse__ServiceUrl` to App Service settings
- [ ] Add `Dataverse__ClientSecret` to App Service settings (or KeyVault reference)

### Testing

- [ ] Test with PowerShell script (verify credentials work)
- [ ] Deploy API to App Service
- [ ] Test health check: `/healthz/dataverse`
- [ ] Verify returns: `{"status":"healthy"}`

### If Anything Fails

- [ ] Check this document's Troubleshooting Guide section
- [ ] Verify all checklist items completed
- [ ] Check App Service logs for specific errors
- [ ] Compare configuration with working example in this doc

---

## Contact / Escalation

If this guide doesn't resolve your issue:

1. **Check Git history** for recent changes to Dataverse code
2. **Review Sprint 7A fix documentation** for similar symptoms
3. **Verify against working configuration** (this document)
4. **Do NOT attempt custom implementations** - use ServiceClient

**Remember**: If PowerShell succeeds but .NET fails, it's NOT a configuration issue. It's a code implementation issue. Use `ServiceClient`.

---

**Last Updated**: 2025-10-06
**Verified Working**: ✅ Sprint 7A deployment
**Next Review**: When upgrading .NET version or Dataverse SDK version
