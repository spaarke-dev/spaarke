# SPE File Viewer - Structured Execution Plan

This document provides a step-by-step execution plan with context-gathering, implementation, and verification steps.

---

## Phase 1: Documentation ✅ COMPLETE

**Status**: All documentation created and reviewed
- [x] IMPLEMENTATION-SDAP-ALIGNED.md
- [x] ADR-NO-HTTP-FROM-PLUGINS.md
- [x] ARCHITECTURE-CORRECTION.md

---

## Phase 2: Revert Incorrect Implementation

**Goal**: Remove Custom API and plugin from Dataverse, archive code

### 2.1 Document Current State (Context Gathering)

**Actions**:
1. Open XrmToolBox → Custom API Manager
2. Find `sprk_GetFilePreviewUrl`
3. Document current configuration:
   - Unique Name
   - Plugin Type ID (if linked)
   - Number of output parameters
4. Screenshot for reference

**Verification**:
- [ ] Custom API configuration documented
- [ ] Screenshot saved to `dev/projects/spe-file-viewer/screenshots/`

### 2.2 Delete Custom API

**Prerequisites**:
- XrmToolBox installed
- Connected to Dataverse environment

**Actions**:
1. Open XrmToolBox → Custom API Manager
2. Select `sprk_GetFilePreviewUrl`
3. Click Delete (will also delete 6 output parameters)
4. Confirm deletion

**Verification**:
- [ ] Custom API no longer appears in Custom API Manager
- [ ] Publish customizations (if not auto-published)

**Rollback Plan**: If needed, use `create-records-simple.ps1` to recreate (not recommended)

### 2.3 Delete Plugin Assembly

**Prerequisites**:
- Plugin Registration Tool installed
- Connected to Dataverse environment

**Actions**:
1. Open Plugin Registration Tool
2. Connect to organization
3. Find assembly: `Spaarke.Dataverse.CustomApiProxy`
4. Right-click → Delete
5. Confirm deletion (will also delete plugin types and steps)

**Verification**:
- [ ] Assembly no longer appears in PRT
- [ ] No orphaned plugin types remain

**Rollback Plan**: Would need to re-register DLL (not recommended)

### 2.4 Archive Plugin Code

**Prerequisites**:
- Git repository clean (no uncommitted changes)

**Actions**:
```bash
# Create archive directory
mkdir c:\code_files\spaarke\_archive

# Move plugin project
mv c:\code_files\spaarke\src\dataverse\Spaarke.CustomApiProxy\ c:\code_files\spaarke\_archive\

# Or create a git branch for historical reference
cd c:\code_files\spaarke
git checkout -b archive/custom-api-approach
git add src/dataverse/Spaarke.CustomApiProxy/
git commit -m "Archive Custom API approach (ADR: no HTTP from plugins)"
git checkout master
```

**Verification**:
- [ ] Plugin code moved to `_archive/` or committed to archive branch
- [ ] Main branch clean of plugin references

---

## Phase 3: BFF Updates

**Goal**: Implement real UAC, add download endpoint, configure CORS/JWT

### 3.1 Read IAuthorizationService Interface

**File**: Search for `IAuthorizationService` interface

**Actions**:
```bash
# Find IAuthorizationService
grep -r "interface IAuthorizationService" c:\code_files\spaarke\src\

# Read the interface definition
```

**Expected Interface**:
```csharp
public interface IAuthorizationService
{
    Task<AuthorizationResult> AuthorizeDocumentAccessAsync(
        string userId,
        string documentId,
        DocumentOperation operation,
        CancellationToken ct = default);
}
```

**Verification**:
- [ ] Interface location documented
- [ ] Method signature understood
- [ ] Return type `AuthorizationResult` understood

**If Interface Doesn't Exist**: Create it as part of Phase 3.3

### 3.2 Read Existing DocumentAuthorizationFilter

**File**: `c:\code_files\spaarke\src\api\Spe.Bff.Api\Api\Filters\DocumentAuthorizationFilter.cs`

**Actions**:
1. Read current implementation
2. Identify what it currently does (if anything)
3. Note any existing UAC logic

**Verification**:
- [ ] Current filter implementation understood
- [ ] Know whether to update or create new

### 3.3 Implement/Update DocumentAuthorizationFilter

**File**: `Spe.Bff.Api/Api/Filters/DocumentAuthorizationFilter.cs`

**Prerequisites**:
- IAuthorizationService interface exists
- Understand current filter implementation

**Implementation**:
- See IMPLEMENTATION-SDAP-ALIGNED.md section 1.2 for code

**Key Requirements**:
- [ ] Extracts userId from JWT claims (oid or NameIdentifier)
- [ ] Calls `IAuthorizationService.AuthorizeDocumentAccessAsync`
- [ ] Returns 403 Forbidden on authorization failure
- [ ] Logs authorization decisions with correlation ID
- [ ] Stores correlation ID in HttpContext.Items for downstream use

**Verification**:
- [ ] Code compiles
- [ ] Filter can be added to endpoints via `.AddEndpointFilter<DocumentAuthorizationFilter>(DocumentOperation.Read)`

### 3.4 Read Existing SpeFileStore.GetPreviewUrlAsync

**File**: `c:\code_files\spaarke\src\api\Spe.Bff.Api\Infrastructure\Graph\SpeFileStore.cs`

**Actions**:
1. Read current `GetPreviewUrlAsync` method
2. Note current parameters and return type
3. Check if correlation ID parameter exists

**Verification**:
- [ ] Current implementation documented
- [ ] Return type `PreviewResult` understood
- [ ] Know what needs to be added (correlation ID logging)

### 3.5 Update SpeFileStore with Correlation ID

**File**: `Spe.Bff.Api/Infrastructure/Graph/SpeFileStore.cs`

**Prerequisites**:
- Current implementation understood

**Changes**:
- Add `correlationId` parameter to `GetPreviewUrlAsync`
- Log correlation ID at method entry
- Log Graph-Request-Id from response headers (if available)
- See IMPLEMENTATION-SDAP-ALIGNED.md section 1.4 for code

**Verification**:
- [ ] Code compiles
- [ ] Correlation ID logged at entry
- [ ] Graph-Request-Id extraction attempted

### 3.6 Create DocumentDownloadService

**File**: `Spe.Bff.Api/Services/DocumentDownloadService.cs` (new file)

**Prerequisites**:
- Understand `DocumentPreviewService` pattern

**Implementation**:
Similar to `DocumentPreviewService` but:
- Calls Graph API for `@microsoft.graph.downloadUrl`
- Returns `DownloadResponse` with download URL, fileName, size, expiresAt

**Verification**:
- [ ] Service registered in DI container
- [ ] Returns correct DTO structure

### 3.7 Add /download Endpoint

**File**: `Spe.Bff.Api/Api/FileAccessEndpoints.cs`

**Prerequisites**:
- `DocumentDownloadService` created

**Implementation**:
- Add `docs.MapGet("/{id:guid}/download", ...)`
- Use `DocumentAuthorizationFilter`
- See IMPLEMENTATION-SDAP-ALIGNED.md section 1.1 for code

**Verification**:
- [ ] Endpoint appears in Swagger UI
- [ ] Returns 401 without token
- [ ] Returns 403 for unauthorized user

### 3.8 Read Existing Program.cs

**File**: `c:\code_files\spaarke\src\api\Spe.Bff.Api\Program.cs`

**Actions**:
1. Read current CORS configuration (if any)
2. Read current JWT configuration
3. Note where to add new config

**Verification**:
- [ ] Understand current auth setup
- [ ] Know where to add CORS policy
- [ ] Know where to update JWT validation

### 3.9 Update Program.cs with CORS and JWT

**File**: `Spe.Bff.Api/Program.cs`

**Prerequisites**:
- Current config understood

**Changes**:
- Add CORS policy for Dataverse origins (see section 1.5)
- Update JWT validation to enforce BFF audience (see section 1.6)

**Verification**:
- [ ] CORS allows `https://*.dynamics.com`
- [ ] JWT validation checks audience = `api://<BFF_APP_ID>`
- [ ] Code compiles and app starts

### 3.10 Test BFF Endpoints

**Method**: PowerShell + Postman

**Prerequisites**:
- BFF running locally or deployed
- Have valid access token

**Test Cases**:

1. **Test Preview Endpoint**:
```powershell
# Get token
$token = az account get-access-token --resource "api://YOUR_BFF_APP_ID" --query accessToken -o tsv

# Call endpoint
$correlationId = [guid]::NewGuid().ToString()
$response = Invoke-RestMethod `
  -Uri "https://localhost:5001/api/documents/VALID_DOC_ID/preview-url" `
  -Headers @{
    "Authorization" = "Bearer $token"
    "X-Correlation-Id" = $correlationId
  }

# Verify response
$response.data.previewUrl  # Should have value
$response.metadata.correlationId -eq $correlationId  # Should be true
```

2. **Test Download Endpoint**: Same pattern with `/download`

**Verification**:
- [ ] Preview endpoint returns 200 with preview URL
- [ ] Download endpoint returns 200 with download URL
- [ ] Correlation IDs match request/response
- [ ] expiresAt is present and reasonable (~10min)
- [ ] 403 for unauthorized documents

---

## Phase 4: PCF Control

**Goal**: Build PCF control with correct MSAL scopes and BFF integration

### 4.1 Create PCF Project

**Prerequisites**:
- Power Platform CLI installed
- Node.js installed

**Actions**:
```bash
cd c:\code_files\spaarke\src\pcf
pac pcf init --namespace Spaarke --name FileViewer --template dataset
cd FileViewer
npm install
```

**Verification**:
- [ ] Project created successfully
- [ ] `npm run build` works
- [ ] `npm start watch` launches test harness

### 4.2 Install Dependencies

**Actions**:
```bash
npm install @azure/msal-browser
npm install @fluentui/react
npm install react react-dom
npm install uuid
npm install --save-dev @types/react @types/react-dom @types/uuid
```

**Verification**:
- [ ] All packages installed
- [ ] No dependency conflicts

### 4.3 Implement MSAL AuthService

**File**: `FileViewer/services/AuthService.ts` (new)

**Prerequisites**:
- Know BFF App ID
- Know Tenant ID

**Implementation**:
- See IMPLEMENTATION-SDAP-ALIGNED.md section 2.1 for code
- Use named scope: `api://<BFF_APP_ID>/SDAP.Access`

**Verification**:
- [ ] TypeScript compiles
- [ ] Can acquire token in test harness

### 4.4 Implement BFF Client

**File**: `FileViewer/services/BffClient.ts` (new)

**Prerequisites**:
- AuthService implemented
- Know BFF base URL

**Implementation**:
- See IMPLEMENTATION-SDAP-ALIGNED.md section 2.2 for code
- Generate and send X-Correlation-Id

**Verification**:
- [ ] TypeScript compiles
- [ ] Can call BFF endpoint successfully

### 4.5 Create FilePreview Component

**File**: `FileViewer/components/FilePreview.tsx` (new)

**Prerequisites**:
- BffClient implemented

**Implementation**:
- React component that calls `getPreviewUrl()`
- Displays iframe with preview URL
- Handles loading/error states

**Verification**:
- [ ] Component renders in test harness
- [ ] Preview URL loads in iframe

### 4.6 Update PCF Index

**File**: `FileViewer/index.ts`

**Prerequisites**:
- FilePreview component created

**Implementation**:
- See IMPLEMENTATION-SDAP-ALIGNED.md section 2.3
- Pass loginHint from userSettings

**Verification**:
- [ ] PCF initializes correctly
- [ ] Component renders on updateView

### 4.7 Build and Test Locally

**Actions**:
```bash
npm run build
npm start watch
```

**Test Cases**:
- [ ] PCF loads in test harness
- [ ] MSAL acquires token successfully
- [ ] BFF call succeeds
- [ ] Preview displays in iframe
- [ ] Error handling works (invalid doc ID)

**Verification**:
- [ ] No console errors
- [ ] Preview URL loads

### 4.8 Package for Deployment

**Actions**:
```bash
pac pcf push --publisher-prefix sprk
```

**Verification**:
- [ ] PCF solution created
- [ ] Can import into Dataverse

### 4.9 Deploy to Dataverse

**Actions**:
1. Import PCF solution
2. Add control to form
3. Configure `documentId` parameter binding

**Verification**:
- [ ] Control appears on form
- [ ] Can configure properties

### 4.10 Test End-to-End

**Test Cases**:
1. Open form with PCF control
2. Verify MSAL token acquisition (check network tab)
3. Verify BFF call (check network tab for X-Correlation-Id)
4. Verify preview loads
5. Check console for errors

**Verification**:
- [ ] Preview loads for valid documents
- [ ] Error message for invalid documents
- [ ] 403 for unauthorized access
- [ ] Token auto-refreshes when expired

---

## Phase 5: Documentation

**Goal**: Update ADRs, create deployment/troubleshooting guides

### 5.1 Update ADR Index

**File**: Find ADR index (likely `docs/adr/README.md` or similar)

**Actions**:
1. Add link to ADR-NO-HTTP-FROM-PLUGINS.md
2. Ensure it's prominently displayed

**Verification**:
- [ ] ADR linked in index
- [ ] Rule clearly stated: "No outbound HTTP from plugins"

### 5.2 Create Deployment Guide

**File**: `dev/projects/spe-file-viewer/DEPLOYMENT-GUIDE.md`

**Content**:
- Azure AD app registration steps
- BFF scope exposure
- PCF app registration
- Environment variables
- CORS configuration

**Verification**:
- [ ] Step-by-step instructions
- [ ] Screenshots included
- [ ] Troubleshooting section

### 5.3 Create Troubleshooting Guide

**File**: `dev/projects/spe-file-viewer/TROUBLESHOOTING.md`

**Content**:
- MSAL authentication errors
- CORS issues
- JWT audience mismatch
- UAC denials
- Graph API errors

**Verification**:
- [ ] Common issues documented
- [ ] Solutions provided
- [ ] Diagnostic steps included

---

## Context Files to Read Before Starting

Before beginning each phase, read these files to gather context:

### Phase 2 (Revert)
- No code reading needed - manual Dataverse operations

### Phase 3 (BFF)
1. `Spe.Bff.Api/Api/Filters/DocumentAuthorizationFilter.cs`
2. `Spe.Bff.Api/Infrastructure/Graph/SpeFileStore.cs`
3. `Spe.Bff.Api/Api/FileAccessEndpoints.cs`
4. `Spe.Bff.Api/Program.cs`
5. Search for `IAuthorizationService` interface

### Phase 4 (PCF)
- No existing files - new project

### Phase 5 (Docs)
- Find and read existing ADR index

---

## Verification Checklist (Final)

- [ ] Custom API deleted from Dataverse
- [ ] Plugin assembly deleted from Dataverse
- [ ] Plugin code archived
- [ ] DocumentAuthorizationFilter uses IAuthorizationService
- [ ] SpeFileStore logs correlation IDs and Graph-Request-Id
- [ ] /download endpoint exists
- [ ] CORS allows Dataverse origins
- [ ] JWT validates BFF audience
- [ ] PCF uses named scope (not .default)
- [ ] PCF sends X-Correlation-Id
- [ ] End-to-end test passes
- [ ] ADR documented
- [ ] Deployment guide created
- [ ] Troubleshooting guide created

---

## Rollback Plan

If issues arise:

**Phase 2**: Custom API and plugin can be restored from archives (not recommended)
**Phase 3**: Revert code changes via git
**Phase 4**: Remove PCF control from form, uninstall solution
**Phase 5**: Revert documentation changes

**Critical**: Test thoroughly in dev environment before deploying to production.
