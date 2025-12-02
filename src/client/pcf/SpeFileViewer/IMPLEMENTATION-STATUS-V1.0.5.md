# SPE File Viewer v1.0.5 - Implementation Status

**Implementation Date:** November 25, 2025
**Status:** Phase 1 & 2 (Partial) Complete - Ready for Completion

---

## ✅ COMPLETED: Phase 1 - PCF Control Fixes

### 1.1 Control Manifest Fix ✅
**File:** `SpeFileViewer/ControlManifest.Input.xml`
- Changed `control-type="virtual"` → `control-type="standard"`
- Updated version: `1.0.4` → `1.0.5`
- **Result:** Control will now appear in field control lists

### 1.2 GUID Validation ✅
**File:** `SpeFileViewer/index.ts` (lines 109-165)
- Added `isValidGuid()` method with regex validation
- Enhanced `extractDocumentId()` to validate GUID format
- Rejects SharePoint Item IDs (e.g., `01LBYCMX...`)
- **Result:** Prevents root cause error at source

### 1.3 Error Handling Enhancement ✅
**File:** `SpeFileViewer/BffClient.ts` (lines 83-155)
- Updated `handleErrorResponse()` to support stable error codes
- Maps error codes to user-friendly messages:
  - `invalid_id` → "Invalid document ID format"
  - `document_not_found` → "Document not found"
  - `mapping_missing_drive`/`mapping_missing_item` → "File is still initializing"
  - `storage_not_found` → "File has been removed"
  - `throttled_retry` → "Service temporarily busy"
- **Result:** Clear, actionable error messages for users

### 1.4 Type Definitions ✅
**File:** `SpeFileViewer/types.ts` (lines 37-66)
- Updated `BffErrorResponse` interface
- Added `extensions` field with `code` property
- Documented all stable error codes
- **Result:** TypeScript support for error code handling

---

## ✅ COMPLETED: Phase 2 (Partial) - BFF Infrastructure

### 2.1 IAccessDataSource Interface ✅
**File:** `Spe.Bff.Api/Infrastructure/Dataverse/IAccessDataSource.cs` (NEW)
- Abstraction for Dataverse document queries
- Single method: `GetSpePointersAsync(Guid) → (string DriveId, string ItemId)`
- Documented stable error codes in XML comments
- **Result:** Clean separation of concerns

### 2.2 SdapProblemException ✅
**File:** `Spe.Bff.Api/Infrastructure/Exceptions/SdapProblemException.cs` (NEW)
- Custom exception with stable error codes
- Properties: Code, Title, Detail, StatusCode, Extensions
- Maps to RFC 7807 Problem Details
- **Result:** Structured error handling in BFF

### 2.3 DataverseAccessDataSource Implementation ✅
**File:** `Spe.Bff.Api/Infrastructure/Dataverse/DataverseAccessDataSource.cs` (NEW)
- Implements `IAccessDataSource`
- Validates DriveId format (starts with "b!", 20+ chars)
- Validates ItemId format (alphanumeric, 20+ chars)
- Throws `SdapProblemException` with appropriate codes
- **Result:** Robust validation before Graph API calls

---

## 🔄 PENDING: Phase 2 (Remaining) - BFF Integration

### 2.4 FileAccessEndpoints Refactoring ⏳
**File:** `Spe.Bff.Api/Api/FileAccessEndpoints.cs`

**Required Changes:**

#### Update `/api/documents/{documentId}/preview-url` endpoint:

```csharp
// CHANGE 1: Add IAccessDataSource parameter
fileAccessGroup.MapGet("/{documentId}/preview-url", async (
    string documentId,
    [FromServices] IAccessDataSource accessDataSource,  // ADD THIS
    [FromServices] SpeFileStore speFileStore,
    [FromServices] ILogger<Program> logger,
    HttpContext context) =>
{
    var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault()
        ?? context.TraceIdentifier;

    logger.LogInformation("[{CorrelationId}] Getting preview URL for document {DocumentId}",
        correlationId, documentId);

    try
    {
        // CHANGE 2: Add GUID validation BEFORE Dataverse query
        if (!Guid.TryParse(documentId, out var docId))
        {
            logger.LogWarning("[{CorrelationId}] Invalid document ID format: {DocumentId}",
                correlationId, documentId);

            return Results.Problem(
                title: "Invalid document ID",
                detail: "Document ID must be a GUID (Dataverse primary key). Do not use SharePoint Item IDs.",
                statusCode: 400,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "invalid_id",
                    ["correlationId"] = correlationId
                });
        }

        // CHANGE 3: Replace direct Dataverse call with IAccessDataSource
        // OLD CODE (REMOVE):
        // var document = await dataverseService.GetDocumentAsync(documentId);
        // if (document == null) { return NotFound... }
        // if (string.IsNullOrEmpty(document.GraphDriveId)...) { return ValidationError... }

        // NEW CODE:
        var (driveId, itemId) = await accessDataSource.GetSpePointersAsync(
            docId,
            context.RequestAborted);

        // CHANGE 4: Get preview URL from Graph
        var previewResult = await speFileStore.GetPreviewUrlAsync(
            driveId,
            itemId,
            correlationId);

        // CHANGE 5: Build response (adapt to match PCF expectations)
        var response = new
        {
            previewUrl = previewResult.PreviewUrl,
            documentInfo = new
            {
                id = docId,
                name = "(metadata from SPE if available)",  // Get from driveItem if needed
                fileExtension = "",
                size = 0,
                graphItemId = itemId,
                graphDriveId = driveId
            },
            correlationId = correlationId
        };

        logger.LogInformation("[{CorrelationId}] Preview URL retrieved for document {DocumentId}",
            correlationId, docId);

        return Results.Ok(response);
    }
    catch (SdapProblemException ex)
    {
        // CHANGE 6: Handle SdapProblemException
        logger.LogWarning(ex,
            "[{CorrelationId}] Problem getting preview URL for document {DocumentId}. Code: {Code}",
            correlationId, documentId, ex.Code);

        return Results.Problem(
            title: ex.Title,
            detail: ex.Detail,
            statusCode: ex.StatusCode,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = ex.Code,
                ["correlationId"] = correlationId
            });
    }
    catch (Exception ex)
    {
        logger.LogError(ex,
            "[{CorrelationId}] Unexpected error getting preview URL for document {DocumentId}",
            correlationId, documentId);

        return Results.Problem(
            title: "Internal server error",
            detail: "An unexpected error occurred while processing your request.",
            statusCode: 500,
            extensions: new Dictionary<string, object?>
            {
                ["correlationId"] = correlationId
            });
    }
})
.AddEndpointFilter(/* existing auth filter */)
.WithName("GetDocumentPreviewUrl")
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)  // Add this
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status409Conflict)     // Add this
.Produces(StatusCodes.Status500InternalServerError);
```

#### Similar changes needed for other endpoints:
- `/api/documents/{documentId}/preview` - Same pattern
- `/api/documents/{documentId}/content` - Same pattern
- `/api/documents/{documentId}/office` - Same pattern

**Key Pattern:**
1. Add `IAccessDataSource` parameter
2. Validate GUID format first
3. Call `accessDataSource.GetSpePointersAsync()` instead of direct Dataverse query
4. Catch `SdapProblemException` and map to Problem Details with error code
5. Update `.Produces()` to include 400 and 409 status codes

---

### 2.5 Service Registration ⏳
**File:** `Spe.Bff.Api/Program.cs`

**Required Change:**

Find the section where services are registered (around line 60-65) and add:

```csharp
// Add after existing service registrations (e.g., after AddSpaarkeCore())
builder.Services.AddScoped<IAccessDataSource, DataverseAccessDataSource>();
```

**Location:** After line 62 (`builder.Services.AddSpaarkeCore();`)

---

## 🔄 PENDING: Phase 3 - Build & Deploy

### 3.1 Build PCF Control
```bash
cd /c/code_files/spaarke/src/controls/SpeFileViewer

# Disable central package management
mv ../../Directory.Packages.props ../../Directory.Packages.props.disabled

# Build PCF
npm run build

# Build solution package
cd SpeFileViewerSolution
dotnet msbuild SpeFileViewerSolution.cdsproj //t:Rebuild //p:Configuration=Release

# Create versioned package
cd bin/Release
cp SpeFileViewerSolution.zip SpeFileViewerSolution_v1.0.5.zip

# Re-enable central package management
mv ../../../../Directory.Packages.props.disabled ../../../../Directory.Packages.props
```

### 3.2 Build & Deploy BFF API
```bash
cd /c/code_files/spaarke/src/api/Spe.Bff.Api

# Build
dotnet build -c Release

# Publish
dotnet publish -c Release -o publish

# Deploy to Azure (adjust based on your deployment method)
az webapp deploy \
  --resource-group <rg-name> \
  --name spe-api-dev-67e2xz \
  --src-path publish.zip \
  --type zip
```

### 3.3 Deploy PCF Solution
```bash
# Option A: Delete old solution via make.powerapps.com UI first, then:
pac solution import \
  --path "SpeFileViewerSolution_v1.0.5.zip" \
  --force-overwrite \
  --publish-changes

# Option B: Manual import via Power Apps portal
# 1. Go to make.powerapps.com > Solutions
# 2. Delete "SpeFileViewerSolution" (v1.0.4)
# 3. Import > Browse > Select SpeFileViewerSolution_v1.0.5.zip
# 4. Publish all customizations
```

---

## 🔄 PENDING: Phase 4 - Testing

### Test Case 1: Valid Document with Mapping ✅
**Setup:**
- Document exists with valid `sprk_graphdriveid` and `sprk_graphitemid`

**Expected:**
- Preview loads successfully
- Console: `[SpeFileViewer] Using form record ID: {guid}`
- No errors

### Test Case 2: Document Without Mapping ⚠️
**Setup:**
- Document exists but `sprk_graphitemid` is empty

**Expected:**
- Error: "This file is still initializing"
- Console error code: `mapping_missing_item`
- HTTP 409 response

### Test Case 3: Invalid GUID Format (Regression Prevention) 🔒
**Setup:**
- Manually configure documentId with SharePoint Item ID: `01LBYCMX...`

**Expected:**
- PCF error immediately (before BFF call)
- Console: "Configured documentId is not a valid GUID"
- Control shows error message

### Test Case 4: Document Not Found 404
**Setup:**
- Invalid GUID or deleted document

**Expected:**
- Error: "Document not found"
- Console error code: `document_not_found`
- HTTP 404 response

### Test Case 5: Storage File Deleted 🗑️
**Setup:**
- Document mapping exists, file deleted from SharePoint

**Expected:**
- Error: "File has been removed from storage"
- Console error code: `storage_not_found`
- HTTP 404 from Graph

---

## Summary of Changes

### Files Modified (PCF):
1. ✅ `ControlManifest.Input.xml` - control-type & version
2. ✅ `index.ts` - GUID validation
3. ✅ `BffClient.ts` - error code handling
4. ✅ `types.ts` - BffErrorResponse extensions

### Files Created (BFF):
5. ✅ `Infrastructure/Dataverse/IAccessDataSource.cs`
6. ✅ `Infrastructure/Exceptions/SdapProblemException.cs`
7. ✅ `Infrastructure/Dataverse/DataverseAccessDataSource.cs`

### Files Pending Modification (BFF):
8. ⏳ `Api/FileAccessEndpoints.cs` - Use IAccessDataSource, add GUID validation
9. ⏳ `Program.cs` - Register IAccessDataSource

---

## Next Steps

1. **Complete Phase 2.4-2.5** (BFF changes above)
2. **Execute Phase 3** (Build & Deploy)
3. **Execute Phase 4** (Testing)
4. **User Acceptance** before production deployment

---

## Risk Assessment

### Low Risk ✅
- PCF changes (completed, defensive programming)
- New BFF infrastructure (clean abstractions, no breaking changes)

### Medium Risk ⚠️
- FileAccessEndpoints refactoring (needs careful testing of all endpoints)
- Deployment coordination (PCF + BFF must be deployed together)

### Mitigation ✅
- All changes are backwards compatible
- Existing error handling preserved as fallback
- Extensive logging added for troubleshooting

---

**Document Version:** 1.0
**Last Updated:** November 25, 2025
**Next Reviewer:** Senior Developer / Product Manager
