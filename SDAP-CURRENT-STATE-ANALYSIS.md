# SDAP Current State Analysis - October 16, 2025

**Purpose**: Reconcile existing V2 architecture with current blocker and simplified design spec
**Status**: HTTP 500 error on file upload - root cause analysis

---

## Executive Summary

### What We Have (V2 Architecture - Oct 13)

✅ **Complete Architecture Documented**:
- Full authentication flow (MSAL.js → BFF API → OBO → Graph API)
- Two app registrations (BFF API: `1e40baad...`, PCF Client: `170c98e1...`)
- Container Type created (`8a6ce34c...`)
- BFF API deployed to Azure (`spe-api-dev-67e2xz.azurewebsites.net`)
- PCF control deployed to Dataverse

✅ **Working Components**:
- `/ping` endpoint returns JSON ✅
- JWT authentication middleware configured ✅
- Graph client factory with OBO flow ✅
- Upload session manager implemented ✅

❌ **Current Blocker**:
- File upload from PCF control → HTTP 500 (IIS crash)
- Error occurs BEFORE .NET code executes
- No .NET exception logs captured (app crashes during middleware)

### What We're Missing (Design Spec Findings - Oct 16)

Based on comparing V2 architecture with SharePoint Embedded reference guide:

🔍 **Critical Permission Missing**:
- **Problem**: `FileStorageContainer.Selected` scope NOT in V2 architecture docs
- **Required**: Both Microsoft Graph AND SharePoint Online need this permission
- **Impact**: SharePoint Embedded requires this for container access
- **Evidence**: Reference guide lines 32-68 show this is mandatory

🔍 **Container Type Registration Status Unknown**:
- **Problem**: V2 architecture says "registered" but doesn't show how
- **Required**: Must register Container Type in consumer tenant via REST API
- **Impact**: Unregistered = "access denied" errors
- **Evidence**: Reference guide lines 139-206 show this is mandatory step

🔍 **Graph Endpoint Version Unclear**:
- **V2 Says**: Uses `/drives/{containerId}` endpoint (correct)
- **Missing**: Must use `/beta/` not `/v1.0/` for SharePoint Embedded
- **Impact**: v1.0 endpoints may not recognize Container drive IDs

---

## Step-by-Step Analysis

### Step 1: Verify App Registration Permissions

#### Current State (from V2 Architecture)

**BFF API App (`1e40baad...`) Permissions** (from line 582-585):
```json
{
  "delegated": [
    "Files.ReadWrite.All",
    "Sites.FullControl.All"
  ]
}
```

#### What's Missing

**Reference Guide Requirements** (lines 32-68):
```json
{
  "Microsoft Graph (00000003-0000-0000-c000-000000000000)": {
    "FileStorageContainer.Selected (Delegated)": "085ca537-6565-41c2-aca7-db852babc212",
    "FileStorageContainer.Selected (Application)": "40dc41bc-0f7e-42ff-89bd-d9516947e474"
  },
  "SharePoint Online (00000003-0000-0ff1-ce00-000000000000)": {
    "FileStorageContainer.Selected (Delegated)": "4d114b1a-3649-4764-9dfb-be1e236ff371",
    "FileStorageContainer.Selected (Application)": "19766c1b-905b-43af-8756-06526ab42875"
  }
}
```

#### Action Required

```bash
# Check current permissions
az ad app permission list --id 1e40baad-e065-4aea-a8d4-4b7ab273458c --output table

# Expected to NOT see:
# - FileStorageContainer.Selected (Graph)
# - FileStorageContainer.Selected (SharePoint)
```

**Hypothesis**: This is the root cause of HTTP 500.

When BFF API attempts OBO token exchange, it requests:
- ✅ `Sites.FullControl.All` (granted)
- ✅ `Files.ReadWrite.All` (granted)
- ❌ `FileStorageContainer.Selected` (NOT granted)

Azure AD returns error → ASP.NET Core crashes during token exchange → HTTP 500 before logging.

---

### Step 2: Verify Container Type Registration

#### Current State (from V2 Architecture)

**Line 425 Claims**:
> ✅ **Container Type Registration**: BFF API (`1e40baad...`) is registered

**Line 601-604 Claims**:
> **SharePoint Embedded Registration**:
> - ✅ **Registered in Container Type** `8a6ce34c-6055-4681-8f87-2f4f9f921c06`
> - **Purpose**: Allows BFF API to perform file operations on SPE containers
> - **Validates**: Token B `appid` claim during Graph API calls

#### What's Missing

**No Evidence Of**:
- HOW Container Type was registered
- WHEN Container Type was registered
- Certificate-based registration (required per reference guide)
- Postman collection execution (reference guide line 186-206)

#### Action Required

**Verify Registration Status**:
```powershell
# PowerShell script to check registration
Import-Module "Microsoft.Online.SharePoint.PowerShell"
Connect-SPOService -Url "https://spaarkedev1-admin.sharepoint.com"

# Get Container Type details
$ct = Get-SPOContainerType -ContainerTypeId 8a6ce34c-6055-4681-8f87-2f4f9f921c06

# Check if BFF API is registered
Write-Host "Container Type: $($ct.ContainerTypeName)"
Write-Host "Owning App: $($ct.OwningApplicationId)"
# Expected: 170c98e1... (PCF Client, NOT BFF API)

# Check registered applications
# Need to query REST API: /_api/v2.1/storageContainerTypes/{id}/applicationPermissions
```

**Expected Finding**: Container Type owned by PCF Client (`170c98e1...`), but BFF API (`1e40baad...`) is NOT registered as authorized app.

**Impact**: When BFF API (with Token B appid=`1e40baad...`) tries to access container, SharePoint Embedded returns 403 Forbidden.

---

### Step 3: Verify Graph Endpoint Version

#### Current State (from V2 Architecture)

**Line 394 Code Snippet**:
```csharp
var uploadedItem = await graphClient.Drives[containerId].Root
    .ItemWithPath(path)
    .Content
    .PutAsync(content, cancellationToken: ct);
```

**ADR-011 Claims** (lines 127-137):
> **Principle**: Use Graph API `/drives/` endpoint instead of SPE-specific `/storage/fileStorage/containers/`
> **Implementation**: `graphClient.Drives[containerId]`
> **Graph API HTTP**: `PUT /v1.0/drives/{containerId}/root:/{path}:/content`

#### What's Missing

**No Mention Of**:
- SharePoint Embedded requires **BETA endpoint** (`/beta/drives/...`)
- Microsoft Graph SDK configuration for beta version
- Fallback to v1.0 vs explicit beta selection

#### Action Required

**Check GraphClientFactory Configuration**:
```csharp
// Expected (CORRECT for SPE):
var graphClient = new GraphServiceClient(authProvider) {
    BaseUrl = "https://graph.microsoft.com/beta"  // BETA required
};

// Likely Current (WRONG for SPE):
var graphClient = new GraphServiceClient(authProvider);
// Defaults to: https://graph.microsoft.com/v1.0
```

**Verification**:
```bash
# Search codebase for GraphServiceClient instantiation
grep -rn "GraphServiceClient\|BaseUrl\|beta" src/api/Spe.Bff.Api/
```

**Expected Finding**: GraphClientFactory uses default (v1.0) instead of beta.

**Impact**: Graph API may not recognize Container ID as valid Drive ID in v1.0 endpoint.

---

### Step 4: Verify OBO Token Exchange Scopes

#### Current State (from V2 Architecture)

**Line 318-322 Code Snippet**:
```csharp
var result = await _confidentialClientApp.AcquireTokenOnBehalfOf(
    scopes: new[] {
        "https://graph.microsoft.com/Sites.FullControl.All",
        "https://graph.microsoft.com/Files.ReadWrite.All"
    },
    userAssertion: new UserAssertion(userAccessToken)
).ExecuteAsync();
```

#### What's Missing

**Missing Scope**:
```csharp
"https://graph.microsoft.com/FileStorageContainer.Selected"  // NOT in V2 code
```

**Reference Guide Pattern** (lines 517-526):
```typescript
const graphTokenRequest = {
  oboAssertion: token,
  scopes: [
    'Sites.Read.All',
    'FileStorageContainer.Selected'  // REQUIRED for SPE
  ]
};
```

#### Action Required

**Find OBO Code**:
```bash
# Locate OBO token exchange in codebase
grep -rn "AcquireTokenOnBehalfOf\|CreateOnBehalfOfClientAsync" src/api/Spe.Bff.Api/
```

**Expected Finding**: GraphClientFactory.cs OBO exchange does NOT request `FileStorageContainer.Selected`.

**Impact**: Token B doesn't have permission to access SharePoint Embedded containers → 403 Forbidden.

---

## Root Cause Hypothesis

### Most Likely Cause: Missing FileStorageContainer.Selected Permission

**Failure Point**: OBO Token Exchange (Step 7-8 in auth flow)

**Sequence of Events**:
1. ✅ PCF control acquires Token A for BFF API
2. ✅ PCF sends Token A to BFF API endpoint
3. ✅ BFF API validates Token A signature
4. ✅ BFF API extracts user assertion from Token A
5. ❌ **BFF API requests OBO token with scopes that don't include FileStorageContainer.Selected**
6. ❌ **Azure AD issues Token B without FileStorageContainer.Selected scope**
7. ✅ BFF API creates Graph client with Token B
8. ❌ **Graph API call fails with 401/403 because Token B lacks required scope**
9. ❌ **ASP.NET Core middleware crashes before logging the error**
10. ❌ **IIS returns generic HTTP 500.0 error page**

**Why No Logs**:
- Token exchange succeeds (no error thrown)
- Token B is valid (proper signature, not expired)
- Error only occurs when Graph API validates Token B scopes
- Graph SDK may throw exception that crashes middleware
- Exception occurs before application logging captures it

### Secondary Cause: Unregistered Container Type

**Failure Point**: SharePoint Embedded Validation (Step 10-11 in auth flow)

**Even if Token B has correct scopes**, SPE validates:
1. ✅ Token signature valid
2. ✅ Token audience is `https://graph.microsoft.com`
3. ✅ Token has `FileStorageContainer.Selected` scope
4. ❌ **Token appid (`1e40baad...`) is NOT registered in Container Type**
5. ❌ **SPE returns 403 Forbidden**

**Result**: Same HTTP 500 error, but different root cause.

---

## Diagnostic Plan

### Immediate Next Step: Check Logs

✅ **Already Done** (Oct 16 04:51):
- Enabled detailed logging: `ASPNETCORE_DETAILEDERRORS=true`
- Enabled debug logging: `Logging__LogLevel__Default=Debug`
- Restarted app
- Log stream active

🎯 **Waiting For**: User to trigger file upload from PCF control to capture actual exception.

### If No Helpful Logs: Manual Testing

**Test 1: Verify App Permissions**
```bash
# Check if FileStorageContainer.Selected is granted
az ad app permission list --id 1e40baad-e065-4aea-a8d4-4b7ab273458c --query "[?resourceAppId=='00000003-0000-0000-c000-000000000000'].resourceAccess[?id=='085ca537-6565-41c2-aca7-db852babc212']"

# Expected: Empty (not granted)
```

**Test 2: Check Container Type Registration**
```bash
# Use Postman or curl to query SharePoint REST API
# Endpoint: /_api/v2.1/storageContainerTypes/8a6ce34c-6055-4681-8f87-2f4f9f921c06/applicationPermissions
# Auth: Certificate-based (spe-app-cert from KeyVault)
```

**Test 3: Verify Graph Endpoint Version**
```bash
# Check GraphClientFactory code
cat src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs | grep -A5 "GraphServiceClient"
```

**Test 4: Test OBO Flow Independently**
```bash
# Create test script to acquire OBO token and inspect scopes
# See if FileStorageContainer.Selected is in Token B
```

---

## Recommended Fix Priority

### Priority 1: Add FileStorageContainer.Selected Permission (CRITICAL)

**Why First**: Easiest to verify and fix, highest likelihood of being root cause.

**Steps**:
1. Add permissions to BFF API app manifest (lines 32-68 from reference guide)
2. Grant admin consent
3. Update GraphClientFactory OBO scopes to request `FileStorageContainer.Selected`
4. Restart BFF API
5. Test upload

**Estimated Fix Time**: 15 minutes
**Estimated Test Time**: 5 minutes

### Priority 2: Verify Container Type Registration (HIGH)

**Why Second**: Cannot be fixed via code change alone, requires PowerShell/Postman.

**Steps**:
1. Verify current registration status
2. If not registered, use certificate from KeyVault to register via REST API
3. Follow reference guide lines 139-206 exactly
4. Test upload

**Estimated Fix Time**: 30 minutes (includes finding/creating certificate)
**Estimated Test Time**: 5 minutes

### Priority 3: Update Graph Endpoint to Beta (MEDIUM)

**Why Third**: Less likely to cause HTTP 500, more likely to cause 404.

**Steps**:
1. Update GraphClientFactory to use beta endpoint
2. Test all Graph API calls (not just upload)
3. Verify no breaking changes in beta API

**Estimated Fix Time**: 10 minutes
**Estimated Test Time**: 15 minutes (regression testing)

---

## Comparison: V2 Architecture vs Simplified Design Spec

| Aspect | V2 Architecture (Oct 13) | Simplified Spec (Oct 16) | Gap Analysis |
|--------|--------------------------|--------------------------|--------------|
| **FileStorageContainer.Selected** | ❌ Not mentioned | ✅ Explicitly required | **CRITICAL GAP** |
| **Container Type Registration** | ✅ Claims "registered" | ✅ Shows HOW to register | **VERIFICATION NEEDED** |
| **Graph Endpoint Version** | ⚠️ Implies v1.0 | ✅ Requires beta | **LIKELY GAP** |
| **OBO Scopes** | ✅ Shows Files + Sites | ✅ Shows Files + Sites + Container | **CRITICAL GAP** |
| **Custom API Scope** | ❌ Not mentioned | ✅ Requires Container.Manage | **NICE-TO-HAVE** |
| **Certificate Auth** | ⚠️ Mentioned, not used | ✅ Required for registration | **REGISTRATION GAP** |
| **PCF Client Scopes** | ⚠️ Unclear | ✅ Must request Container scope | **VERIFICATION NEEDED** |

### Key Insight

**V2 Architecture is 90% correct**, but missing the **critical 10%** that makes SharePoint Embedded work:
1. ❌ `FileStorageContainer.Selected` permission
2. ❓ Container Type registration verification
3. ⚠️ Graph beta endpoint configuration

**Simplified Design Spec fills the gaps** by following the reference guide exactly.

---

## Next Actions

### For Immediate Diagnosis

1. ✅ **Log stream running** - waiting for user to trigger upload
2. 🔍 **Check app permissions** - verify FileStorageContainer.Selected is missing
3. 🔍 **Review GraphClientFactory** - confirm OBO scopes and endpoint version
4. 🔍 **Test Container Type registration** - verify BFF API is authorized

### For Quick Fix (if hypothesis correct)

1. **Add 4 permissions to BFF API app registration** (5 minutes)
2. **Grant admin consent** (1 minute)
3. **Update GraphClientFactory OBO scopes** (2 minutes)
4. **Update GraphClientFactory to use beta endpoint** (2 minutes)
5. **Deploy updated code** (5 minutes)
6. **Test upload** (2 minutes)

**Total Estimated Time to Fix**: 15-20 minutes (if hypothesis is correct)

---

## Success Criteria

### Minimal Success

- ✅ File upload from PCF control returns HTTP 200/201
- ✅ File appears in SharePoint Embedded container
- ✅ No HTTP 500 errors

### Full Success

- ✅ Minimal success criteria met
- ✅ All app permissions correctly configured
- ✅ Container Type registration verified
- ✅ Graph beta endpoint in use
- ✅ OBO token includes FileStorageContainer.Selected
- ✅ Documentation updated with correct permissions

---

**Document Created**: 2025-10-16 05:00 AM
**Based On**:
- SDAP-ARCHITECTURE-OVERVIEW-V2-2025-10-13-2213.md
- SDAP-SIMPLIFIED-DESIGN-SPEC.md
- Current blocker investigation (Oct 16)
