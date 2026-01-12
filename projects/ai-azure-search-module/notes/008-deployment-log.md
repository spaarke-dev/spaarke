# Task 008: Deploy Phase 1 API - Deployment Log

**Date**: 2026-01-09
**Status**: ✅ COMPLETED
**Environment**: Dev (spe-api-dev-67e2xz.azurewebsites.net)

---

## Summary

Phase 1 API successfully deployed to Azure App Service after resolving two issues:
1. **Kiota package version mismatch** - Fixed by adding explicit package references
2. **Missing Dataverse configuration** - Fixed by adding App Service settings

Visualization endpoints are now live and responding correctly.

---

## Steps Completed

### Step 1: Build API in Release Configuration
- **Status**: SUCCESS
- **Command**: `dotnet build src/server/api/Sprk.Bff.Api -c Release`
- **Result**: Build completed with 0 warnings and errors

### Step 2: Run Tests
- **Status**: PARTIAL SUCCESS
- **Unit Tests**: 53 AI service tests passed (including 19 new VisualizationService tests)
- **Integration Tests**: Skipped (pre-existing configuration issue)
- **Command**: `dotnet test tests/unit/Sprk.Bff.Api.Tests -c Release`

### Step 3: Deploy to Azure App Service
- **Status**: SUCCESS
- **Method**: Zip deployment via Azure CLI
- **Command**: `az webapp deploy --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2 --src-path ./api-publish.zip --type zip`
- **Result**: Deployment ID 40656d94e8d44966ad115d203c12c997, completed successfully

### Step 4: Verify /healthz Endpoint (Initial Attempt)
- **Status**: FAILED (subsequently fixed)
- **HTTP Status**: 500 Internal Server Error
- **Error**: `FileNotFoundException: Could not load file or assembly 'Microsoft.Kiota.Abstractions, Version=1.17.1.0'`

### Step 5: Fix Kiota Package Mismatch
- **Status**: SUCCESS
- **Action**: Added explicit package references to `Sprk.Bff.Api.csproj`
- **Packages Added**:
  - `Microsoft.Kiota.Http.HttpClientLibrary` Version="1.21.1"
  - `Microsoft.Kiota.Serialization.Form` Version="1.21.1"
  - `Microsoft.Kiota.Serialization.Json` Version="1.21.1"
  - `Microsoft.Kiota.Serialization.Multipart` Version="1.21.1"
  - `Microsoft.Kiota.Serialization.Text` Version="1.21.1"
- **Result**: Build succeeded, redeployed to Azure

### Step 6: Fix Missing Dataverse Configuration
- **Status**: SUCCESS
- **Error Found**: `OptionsValidationException: Dataverse:ClientSecret is required`
- **Action**: Added `Dataverse__ClientSecret` to App Service settings
- **Command**: `az webapp config appsettings set --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2 --settings "Dataverse__ClientSecret=***"`

### Step 7: Final Verification
- **Status**: SUCCESS
- **Health Endpoint**: HTTP 200 OK - "Healthy"
- **Visualization Endpoint**: HTTP 401 Unauthorized (expected - requires auth)
- **Verification Date**: 2026-01-09

---

## Root Cause Analysis

### Package Version Mismatch

| Package | Direct Reference | Transitive Version | Issue |
|---------|------------------|-------------------|-------|
| Microsoft.Kiota.Abstractions | 1.21.1 | N/A | Correct |
| Microsoft.Kiota.Authentication.Azure | 1.21.1 | N/A | Correct |
| Microsoft.Kiota.Http.HttpClientLibrary | N/A | 1.17.1 | MISMATCH |
| Microsoft.Kiota.Serialization.Json | N/A | 1.17.1 | MISMATCH |
| Microsoft.Kiota.Serialization.Form | N/A | 1.17.1 | MISMATCH |
| Microsoft.Kiota.Serialization.Multipart | N/A | 1.17.1 | MISMATCH |
| Microsoft.Kiota.Serialization.Text | N/A | 1.17.1 | MISMATCH |

### Assembly Version Analysis

```
Microsoft.Kiota.Abstractions.dll        → Assembly Version: 1.21.1.0
Microsoft.Kiota.Http.HttpClientLibrary.dll → Assembly Version: 1.17.1.0
```

The `Microsoft.Kiota.Http.HttpClientLibrary` assembly (v1.17.1) was compiled against `Microsoft.Kiota.Abstractions` v1.17.1.0. At runtime, it tries to load that specific version but finds v1.21.1.0 instead, causing the `FileNotFoundException`.

### Source of Mismatch

The transitive Kiota packages are pulled in by **Microsoft.Graph.Beta** or **Microsoft.Identity.Web.MicrosoftGraph**, which have dependencies on older Kiota versions.

---

## Resolution Options

### Option 1: Add Explicit Package References (Recommended)
Add explicit references to upgrade all Kiota packages to 1.21.1:

```xml
<PackageReference Include="Microsoft.Kiota.Http.HttpClientLibrary" Version="1.21.1" />
<PackageReference Include="Microsoft.Kiota.Serialization.Json" Version="1.21.1" />
<PackageReference Include="Microsoft.Kiota.Serialization.Form" Version="1.21.1" />
<PackageReference Include="Microsoft.Kiota.Serialization.Multipart" Version="1.21.1" />
<PackageReference Include="Microsoft.Kiota.Serialization.Text" Version="1.21.1" />
```

### Option 2: Downgrade Direct References
Downgrade direct Kiota references to match transitives:

```xml
<PackageReference Include="Microsoft.Kiota.Abstractions" Version="1.17.1" />
<PackageReference Include="Microsoft.Kiota.Authentication.Azure" Version="1.17.1" />
```

### Option 3: Update Microsoft.Graph.Beta
Update to a version of Microsoft.Graph.Beta that uses Kiota 1.21.x consistently.

---

## Resolution Summary

Both issues were resolved in this session:

### Issue 1: Kiota Package Mismatch
- **Root Cause**: Jan 6 Kiota update (1.12.0 → 1.21.1) only updated direct refs, not transitive deps
- **Fix**: Added explicit package references for all 5 missing Kiota packages
- **Impact**: Prevents assembly binding conflicts at runtime

### Issue 2: Missing Dataverse Configuration
- **Root Cause**: `Dataverse__ClientSecret` not set in App Service (only `API_CLIENT_SECRET` existed)
- **Fix**: Added correct App Service setting for options validation
- **Impact**: API now initializes correctly with all required configuration

---

## Deployment Complete

✅ **Phase 1 API is now live and operational**

- Health endpoint: `https://spe-api-dev-67e2xz.azurewebsites.net/healthz` → 200 OK
- Visualization endpoint: `/api/ai/visualization/related/{documentId}` → Requires auth (401)
- All Phase 1 tasks (001-008) complete

**Next**: Phase 2 (PCF Control Development) - Tasks 010+

---

*Log updated 2026-01-09 after successful deployment*
