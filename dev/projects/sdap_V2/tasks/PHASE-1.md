# Phase 1: Configuration & Critical Fixes

## Objective
Fix app registration configuration and ServiceClient lifetime issues.

## Duration
4-6 hours

## Prerequisites
- Branch created: `refactor/adr-compliance`
- All tests passing on main branch
- Baseline metrics captured

## Tasks

### Task 1.1: Fix App Registration Configuration
**Files to modify:**
- `src/api/Spe.Bff.Api/appsettings.json`
- `src/api/Spe.Bff.Api/appsettings.Development.json`
- `docs/DATAVERSE-AUTHENTICATION-GUIDE.md`

**Changes:**
1. Update `API_APP_ID` from `170c98e1-d486-4355-bcbe-170454e0207c` to `1e40baad-e065-4aea-a8d4-4b7ab273458c`
2. Ensure `AzureAd:ClientId` matches `API_APP_ID`
3. Ensure `Dataverse:ClientId` matches `API_APP_ID`
4. Update documentation to reflect correct app ID

### Task 1.2: Remove UAMI_CLIENT_ID Logic
**File to modify:**
- `src/api/Spe.Bff.Api/Infrastructure/GraphClientFactory.cs`

**Changes:**
1. Remove references to `UAMI_CLIENT_ID` environment variable
2. Remove user-assigned managed identity code paths
3. Simplify to client secret authentication only

### Task 1.3: Fix ServiceClient Lifetime
**File to modify:**
- `src/api/Spe.Bff.Api/Program.cs` (or wherever Dataverse services are registered)

**Current:**
```csharp
builder.Services.AddScoped<IDataverseService, DataverseServiceClientImpl>();
Change to:
csharpbuilder.Services.AddSingleton<DataverseServiceClientImpl>(sp =>
{
    var config = sp.GetRequiredService<IOptions<DataverseOptions>>().Value;
    var connectionString = $"AuthType=ClientSecret;" +
        $"Url={config.ServiceUrl};" +
        $"ClientId={config.ClientId};" +
        $"ClientSecret={config.ClientSecret};";
    
    return new DataverseServiceClientImpl(connectionString);
});

// If IDataverseService interface exists and is still needed:
builder.Services.AddSingleton<IDataverseService>(sp => 
    sp.GetRequiredService<DataverseServiceClientImpl>());
Validation Steps
Build Validation
bashdotnet build --configuration Release
# Expected: Success with 0 warnings
Test Validation
bashdotnet test
# Expected: All tests pass
Runtime Validation
bash# Start application
dotnet run --project src/api/Spe.Bff.Api

# Test health endpoint
curl -X GET https://localhost:5001/healthz/dataverse
# Expected: 200 OK

# Test ping endpoint
curl -X GET https://localhost:5001/ping
# Expected: 200 OK with timestamp
Configuration Validation
bash# Verify appsettings.json has correct values
grep -A 3 "API_APP_ID" src/api/Spe.Bff.Api/appsettings.json
# Expected: 1e40baad-e065-4aea-a8d4-4b7ab273458c

grep -A 3 "AzureAd" src/api/Spe.Bff.Api/appsettings.json
# Expected: ClientId matches API_APP_ID
Success Criteria

 All configuration files updated with correct app ID
 UAMI_CLIENT_ID references removed
 ServiceClient registered as Singleton
 Build succeeds with zero warnings
 All tests pass
 Health endpoint returns 200
 Application starts without errors