# Sprint 7A - Dataverse Authentication Fix

**Date**: 2025-10-06
**Status**: ✅ RESOLVED
**Duration**: ~4 hours of investigation and fixes

## Problem Summary

After deploying the Sprint 7A Universal Dataset Grid PCF control, the SDAP BFF API (`spe-api-dev-67e2xz.azurewebsites.net`) could not connect to Dataverse, returning:
- `/healthz/dataverse` → 503 "Dataverse connection test failed"
- All authenticated endpoints timing out or returning 500 errors

## Root Cause Analysis

### Initial Hypothesis (Incorrect)
- Thought Managed Identity authentication was failing
- Believed we needed `ClientSecretCredential` with App Registration

### Actual Root Cause
**The custom `HttpClient` implementation in `DataverseWebApiService.cs` was incompatible with Dataverse for server-to-server authentication.**

Key findings:
1. Sprint 4 used `DefaultAzureCredential` with Managed Identity - this was **never actually tested** against real Dataverse (only WireMock tests)
2. The original Sprint 2/3 implementation used `ServiceClient` from `Microsoft.PowerPlatform.Dataverse.Client` - this was the correct approach
3. `ServiceClient` was removed in Sprint 3 (2025-10-01) in favor of custom HttpClient for ".NET 8.0 compatibility" - but `ServiceClient` IS compatible with .NET 8!
4. Microsoft documentation explicitly recommends `ServiceClient` (aka `DataverseServiceClient`) for .NET Core/.NET 8 applications

### Evidence
- PowerShell test with same credentials: ✅ **SUCCEEDED**
  - Token acquisition worked
  - WhoAmI endpoint worked
  - Application User exists and is enabled
- .NET HttpClient with `ClientSecretCredential`: ❌ **500 Internal Server Error from Dataverse**

## Solution Implemented

### 1. Restored ServiceClient Approach
Created new implementation: [`DataverseServiceClientImpl.cs`](../../src/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs)

**Key Changes:**
```csharp
// OLD: Custom HttpClient with manual token handling
public class DataverseWebApiService : IDataverseService
{
    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;
    private AccessToken? _currentToken;

    _credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
    // Manual GetTokenAsync, manual header setting, etc.
}

// NEW: ServiceClient with connection string
public class DataverseServiceClientImpl : IDataverseService, IDisposable
{
    private readonly ServiceClient _serviceClient;

    var connectionString = $"AuthType=ClientSecret;SkipDiscovery=true;Url={dataverseUrl};ClientId={clientId};ClientSecret={clientSecret};RequireNewInstance=true";
    _serviceClient = new ServiceClient(connectionString);
}
```

### 2. Connection String Format
Per [Microsoft documentation](../../docs/KM-DATAVERSE-TO-APP-AUTHENTICATION.md) lines 349-361:

```
AuthType=ClientSecret;
SkipDiscovery=true;
Url={dataverseUrl};
ClientId={clientId};
ClientSecret={clientSecret};
RequireNewInstance=true
```

### 3. Updated Dependencies
Re-added to [`Spaarke.Dataverse.csproj`](../../src/shared/Spaarke.Dataverse/Spaarke.Dataverse.csproj):
```xml
<PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" />
```

### 4. Updated DI Registration
In [`Program.cs`](../../src/api/Spe.Bff.Api/Program.cs) line 262:
```csharp
// OLD
builder.Services.AddHttpClient<IDataverseService, DataverseWebApiService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// NEW
builder.Services.AddScoped<IDataverseService, DataverseServiceClientImpl>();
```

## Configuration Requirements

### Azure App Registration
- **App ID**: `170c98e1-d486-4355-bcbe-170454e0207c` ("Spaarke DSM-SPE Dev 2")
- **Client Secret**: Stored in KeyVault `spaarke-spekvcert` → `BFF-API-ClientSecret`
- **API Permissions**:
  - Dynamics CRM: `user_impersonation` (delegated)
  - Dynamics CRM: Application permission (for S2S) ✅ Admin consented
- **Secret Expiration**: 2027-09-22

### Dataverse Application User
- **Location**: Power Platform Admin Center → SPAARKE DEV 1 → Settings → Users + permissions → Application users
- **App ID**: `170c98e1-d486-4355-bcbe-170454e0207c`
- **Name**: "Spaarke DSM-SPE Dev 2"
- **Security Role**: System Administrator
- **Status**: Active ✅

### App Service Configuration
Key settings in `spe-api-dev-67e2xz`:
```
TENANT_ID = a221a95e-6abc-4434-aecc-e48338a1b2f2
API_APP_ID = 170c98e1-d486-4355-bcbe-170454e0207c
Dataverse__ServiceUrl = https://spaarkedev1.api.crm.dynamics.com
Dataverse__ClientSecret = ~Ac8Q~JGnsrvNEODvFo8qmtKbgj1PmwmJ6GVUaJj
```

## Testing & Verification

### Test Results
```bash
# Before fix
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz/dataverse
# {"status":503,"detail":"Dataverse connection test failed"}

# After fix
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz/dataverse
# {"status":"healthy","message":"Dataverse connection successful"}
```

### PowerShell Verification Script
Created test script proving credentials work:
```powershell
$tokenResponse = Invoke-RestMethod -Method Post `
    -Uri "https://login.microsoftonline.com/$tenantId/oauth2/v2.0/token" `
    -Body @{
        grant_type = "client_credentials"
        client_id = $clientId
        client_secret = $clientSecret
        scope = "https://spaarkedev1.api.crm.dynamics.com/.default"
    }

$whoAmI = Invoke-RestMethod -Method Get `
    -Uri "https://spaarkedev1.api.crm.dynamics.com/api/data/v9.2/WhoAmI" `
    -Headers @{ Authorization = "Bearer $($tokenResponse.access_token)" }

# Result: ✅ Success
# UserId: 773741bc-779d-f011-bbd3-7c1e5215b8b5
# OrganizationId: 0c3e6ad9-ae73-f011-8587-00224820bd31
```

## Key Learnings

1. **Always use Microsoft-recommended SDKs** for platform services
   - ServiceClient/DataverseServiceClient for Dataverse
   - Don't reinvent the wheel with custom HttpClient implementations

2. **Server-to-Server authentication requires specific patterns**
   - App Registration + Client Secret (not Managed Identity for Dataverse)
   - Connection string approach for ServiceClient
   - Application User in Dataverse with proper roles

3. **WireMock tests don't validate real authentication**
   - Sprint 4 tests passed but never validated actual Dataverse connectivity
   - Always test against real services for authentication flows

4. **Documentation matters**
   - The knowledge articles in `/docs` (KM-DATAVERSE-TO-APP-AUTHENTICATION.md) had the answer
   - Microsoft's approach (connection string) is simpler and more reliable than custom implementations

## Files Changed

### Created
- [`src/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs`](../../src/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs) - New ServiceClient implementation

### Modified
- [`src/shared/Spaarke.Dataverse/Spaarke.Dataverse.csproj`](../../src/shared/Spaarke.Dataverse/Spaarke.Dataverse.csproj) - Re-added ServiceClient package
- [`src/api/Spe.Bff.Api/Program.cs`](../../src/api/Spe.Bff.Api/Program.cs) - Updated DI registration
- [`src/api/Spe.Bff.Api/appsettings.json`](../../src/api/Spe.Bff.Api/appsettings.json) - Added ClientSecret KeyVault reference

### Deprecated (not deleted for reference)
- `src/shared/Spaarke.Dataverse/DataverseWebApiService.cs` - Custom HttpClient approach (doesn't work for S2S)

## Next Steps

1. ✅ Dataverse connection working
2. ⏭️ Test Sprint 7A file operations (Download/Delete/Replace buttons in PCF control)
3. ⏭️ Verify file metadata updates in Dataverse
4. ⏭️ Complete Sprint 7A documentation

## References

- [Microsoft: Authenticating .NET Applications](../../docs/KM-DATAVERSE-AUTHENTICATE-DOTNET-APPS.md)
- [Microsoft: Use OAuth with Dataverse](../../docs/KM-DATAVERSE-TO-APP-AUTHENTICATION.md)
- [ServiceClient Connection Strings](https://docs.microsoft.com/en-us/power-apps/developer/data-platform/xrm-tooling/use-connection-strings-xrm-tooling-connect)
- [Sprint 4 Architecture](../Sprint 4/ARCHITECTURE-DOC-UPDATE-COMPLETE.md)
