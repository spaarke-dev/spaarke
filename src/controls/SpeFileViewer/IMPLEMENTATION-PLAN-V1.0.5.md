# SPE File Viewer v1.0.5 - Implementation Plan

## Assessment of Senior Developer's Analysis

### Validation of Root Cause ✅

**Senior Dev is CORRECT:**
- **Root Issue:** PCF was sending Graph `driveItem.id` (e.g., `01LBYCMX...`) instead of Dataverse Document GUID
- **Why it happened:** v1.0.2 used `usage="bound"`, user bound to `sprk_graphitemid` field
- **My v1.0.3 fix attempt:** Changed to `usage="input"` to use form record ID - **THIS WAS THE RIGHT DIRECTION**
- **My v1.0.4 mistake:** Changed `control-type="virtual"` - **THIS BROKE DEPLOYMENT**

### Gaps in Current Implementation

**PCF Control Issues:**
1. ✅ Document ID extraction logic exists (v1.0.3+)
2. ✅ MSAL using named scope (AuthService.ts line 43)
3. ❌ Missing validation for driveId/itemId format before calling BFF
4. ❌ Error messages don't distinguish error types clearly
5. ❌ Control manifest has wrong type (v1.0.4)

**BFF API Issues:**
1. ❌ Missing `IAccessDataSource` abstraction layer
2. ❌ No validation that ID is GUID format before Dataverse query
3. ❌ No distinction between "document not found" vs "mapping missing"
4. ❌ Error responses don't include stable error codes
5. ✅ CORS already configured (verified in previous sessions)
6. ✅ UAC already implemented (DocumentAuthorizationFilter exists)

---

## Implementation Plan

### Phase 1: PCF Control Fixes (v1.0.5)

#### Task 1.1: Fix Control Manifest
**File:** `ControlManifest.Input.xml`

**Change:**
```xml
<!-- FROM (v1.0.4 - BROKEN): -->
<control control-type="virtual">

<!-- TO (v1.0.5 - CORRECT): -->
<control control-type="standard">
```

**Reasoning:** Senior dev confirmed field controls are correct approach. Virtual requires dataset element.

#### Task 1.2: Enhance Document ID Validation
**File:** `index.ts`

**Current Code (lines 114-134):**
```typescript
private extractDocumentId(context: ComponentFramework.Context<IInputs>): string {
    const rawValue = context.parameters.documentId.raw;

    if (rawValue && typeof rawValue === 'string' && rawValue.trim() !== '') {
        console.log('[SpeFileViewer] Using configured document ID:', rawValue);
        return rawValue.trim();
    }

    const recordId = (context.mode as any).contextInfo?.entityId;
    if (recordId) {
        console.log('[SpeFileViewer] Using form record ID:', recordId);
        return recordId;
    }

    console.warn('[SpeFileViewer] No document ID available');
    return '';
}
```

**Enhanced Code:**
```typescript
private extractDocumentId(context: ComponentFramework.Context<IInputs>): string {
    const rawValue = context.parameters.documentId.raw;

    // Option 1: User manually configured document ID
    if (rawValue && typeof rawValue === 'string' && rawValue.trim() !== '') {
        const trimmed = rawValue.trim();

        // Validate GUID format (prevent sending driveItemId by accident)
        if (!this.isValidGuid(trimmed)) {
            console.error('[SpeFileViewer] Configured documentId is not a valid GUID:', trimmed);
            throw new Error('Document ID must be a GUID format (Dataverse primary key). Do not use SharePoint Item IDs.');
        }

        console.log('[SpeFileViewer] Using configured document ID:', trimmed);
        return trimmed;
    }

    // Option 2: Use form record ID (default behavior)
    const recordId = (context.mode as any).contextInfo?.entityId;
    if (recordId && typeof recordId === 'string') {
        if (!this.isValidGuid(recordId)) {
            console.error('[SpeFileViewer] Form record ID is not a valid GUID:', recordId);
            throw new Error('Form context did not provide a valid GUID.');
        }

        console.log('[SpeFileViewer] Using form record ID:', recordId);
        return recordId;
    }

    console.warn('[SpeFileViewer] No document ID available from input or form context');
    return '';
}

/**
 * Validate GUID format (prevents accidentally sending driveItemId)
 * Valid: ad1b0c34-52a5-f011-bbd3-7c1e5215b8b5
 * Invalid: 01LBYCMX76QPLGITR47BB355T4G2CVDL2B (driveItemId)
 */
private isValidGuid(value: string): boolean {
    const guidRegex = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
    return guidRegex.test(value);
}
```

**Benefit:** Prevents the root cause error (sending driveItemId instead of Document GUID) at the source.

#### Task 1.3: Optional UX Guard (Pre-flight Check)
**File:** `FilePreview.tsx`

**Add before BFF call:**
```typescript
private async loadPreview(): Promise<void> {
    const { documentId, accessToken, correlationId } = this.props;

    // Validate documentId format (GUID)
    if (!this.isValidGuid(documentId)) {
        this.setState({
            isLoading: false,
            error: 'Invalid document ID format. Expected a GUID (Dataverse primary key).',
            previewUrl: null,
            documentInfo: null
        });
        return;
    }

    // Optional: Check if mapping exists (requires adding these props)
    // This is a UX enhancement - shows better message while file initializes
    const { driveId, itemId } = this.props;
    if (!driveId || !itemId) {
        this.setState({
            isLoading: false,
            error: 'This file is initializing. Please try again in a moment.',
            previewUrl: null,
            documentInfo: null
        });
        return;
    }

    // Proceed with BFF call...
    this.setState({ isLoading: true, error: null });

    try {
        const response = await this.bffClient.getPreviewUrl(documentId, accessToken, correlationId);
        // ... rest of existing code
    } catch (error) {
        this.handlePreviewError(error);
    }
}

private isValidGuid(value: string): boolean {
    const guidRegex = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
    return guidRegex.test(value);
}
```

#### Task 1.4: Enhanced Error Handling
**File:** `BffClient.ts`

**Update error handling to show stable error codes:**
```typescript
private async handleErrorResponse(response: Response, correlationId: string): Promise<never> {
    const responseText = await response.text();
    let errorBody: BffErrorResponse | null = null;

    try {
        errorBody = JSON.parse(responseText) as BffErrorResponse;
    } catch {
        // Non-JSON response
    }

    // Extract error code from extensions (as per senior dev spec)
    const errorCode = errorBody?.extensions?.code as string | undefined;
    const title = errorBody?.title || `HTTP ${response.status}`;
    const detail = errorBody?.detail || '';

    console.error('[BffClient] BFF API Error:', {
        status: response.status,
        code: errorCode,
        title: title,
        detail: detail,
        correlationId: errorBody?.correlationId || correlationId
    });

    // Map error codes to user-friendly messages
    switch (errorCode) {
        case 'invalid_id':
            throw new Error('Invalid document ID format. Please contact support.');

        case 'document_not_found':
            throw new Error('Document not found. It may have been deleted.');

        case 'mapping_missing_drive':
        case 'mapping_missing_item':
            throw new Error('This file is still initializing. Please try again in a moment.');

        case 'storage_not_found':
            throw new Error('File has been removed from storage. Contact your administrator.');

        case 'throttled_retry':
            throw new Error('Service is temporarily busy. Please try again in a few seconds.');

        default:
            // Fall back to HTTP status codes
            switch (response.status) {
                case 401:
                    throw new Error('Authentication failed. Please refresh the page.');
                case 403:
                    throw new Error('You do not have permission to access this file.');
                case 404:
                    throw new Error('Document not found. It may have been deleted.');
                case 409:
                    throw new Error('File is not ready for preview. Please try again shortly.');
                default:
                    throw new Error(detail || title || 'An unexpected error occurred.');
            }
    }
}
```

#### Task 1.5: Update Types
**File:** `types.ts`

**Add error code support:**
```typescript
export interface BffErrorResponse {
    type?: string;
    title: string;
    status: number;
    detail?: string;
    correlationId?: string;
    errors?: Record<string, string[]>;
    extensions?: {
        code?: string;  // Stable error code (invalid_id, document_not_found, etc.)
        [key: string]: any;
    };
}
```

---

### Phase 2: BFF API Enhancements

#### Task 2.1: Add IAccessDataSource Interface
**New File:** `Spe.Bff.Api/Infrastructure/Dataverse/IAccessDataSource.cs`

```csharp
namespace Spe.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// Abstracts Dataverse access for document storage pointers.
/// Isolates mapping logic from business services.
/// </summary>
public interface IAccessDataSource
{
    /// <summary>
    /// Resolve Document GUID to SharePoint Embedded storage pointers.
    /// </summary>
    /// <param name="documentId">Dataverse Document primary key (GUID)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tuple of (DriveId, ItemId) for Graph API calls</returns>
    /// <exception cref="SdapProblemException">
    /// - document_not_found: Document row doesn't exist
    /// - mapping_missing_drive: DriveId not populated or invalid
    /// - mapping_missing_item: ItemId not populated or invalid
    /// </exception>
    Task<(string DriveId, string ItemId)> GetSpePointersAsync(Guid documentId, CancellationToken cancellationToken);
}
```

#### Task 2.2: Implement DataverseAccessDataSource
**New File:** `Spe.Bff.Api/Infrastructure/Dataverse/DataverseAccessDataSource.cs`

```csharp
namespace Spe.Bff.Api.Infrastructure.Dataverse;

public sealed class DataverseAccessDataSource : IAccessDataSource
{
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<DataverseAccessDataSource> _logger;

    public DataverseAccessDataSource(
        IDataverseService dataverseService,
        ILogger<DataverseAccessDataSource> logger)
    {
        _dataverseService = dataverseService;
        _logger = logger;
    }

    public async Task<(string DriveId, string ItemId)> GetSpePointersAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Resolving SPE pointers for document {DocumentId}", documentId);

        // Query Dataverse for document record
        var columns = new[] { "sprk_graphdriveid", "sprk_graphitemid" };
        var entity = await _dataverseService.RetrieveAsync(
            "sprk_document",
            documentId,
            columns,
            cancellationToken);

        if (entity == null)
        {
            _logger.LogWarning("Document {DocumentId} not found in Dataverse", documentId);
            throw new SdapProblemException(
                code: "document_not_found",
                title: "Document not found",
                detail: $"Document with ID '{documentId}' does not exist.",
                statusCode: 404);
        }

        // Extract storage pointers
        var driveId = entity.GetAttributeValue<string>("sprk_graphdriveid");
        var itemId = entity.GetAttributeValue<string>("sprk_graphitemid");

        // Validate driveId format (SharePoint DriveId starts with "b!" and is 20+ chars)
        if (!IsLikelyDriveId(driveId))
        {
            _logger.LogWarning(
                "Document {DocumentId} has invalid or missing DriveId: {DriveId}",
                documentId, driveId);

            throw new SdapProblemException(
                code: "mapping_missing_drive",
                title: "Storage mapping incomplete",
                detail: "DriveId is not recorded or invalid for this document. The file may still be uploading.",
                statusCode: 409);
        }

        // Validate itemId format (Graph ItemId is alphanumeric, 20+ chars)
        if (!IsLikelyItemId(itemId))
        {
            _logger.LogWarning(
                "Document {DocumentId} has invalid or missing ItemId: {ItemId}",
                documentId, itemId);

            throw new SdapProblemException(
                code: "mapping_missing_item",
                title: "Storage mapping incomplete",
                detail: "ItemId is not recorded or invalid for this document. The file may still be uploading.",
                statusCode: 409);
        }

        _logger.LogInformation(
            "Resolved document {DocumentId} to storage pointers (DriveId: {DriveId}, ItemId: {ItemId})",
            documentId, driveId.Substring(0, 8) + "...", itemId.Substring(0, 8) + "...");

        return (driveId!, itemId!);
    }

    /// <summary>
    /// Heuristic check for SharePoint DriveId format.
    /// Valid: starts with "b!" and is 20+ characters
    /// Example: b!OWdlYTJh...
    /// </summary>
    private static bool IsLikelyDriveId(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.StartsWith("b!", StringComparison.Ordinal)
           && value.Length > 20;

    /// <summary>
    /// Heuristic check for Graph ItemId format.
    /// Valid: alphanumeric start, 20+ characters
    /// Example: 01LBYCMX76QPLGITR47BB355T4G2CVDL2B
    /// </summary>
    private static bool IsLikelyItemId(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && char.IsLetterOrDigit(value[0])
           && value.Length > 20;
}
```

#### Task 2.3: Add SdapProblemException
**New File:** `Spe.Bff.Api/Infrastructure/Exceptions/SdapProblemException.cs`

```csharp
namespace Spe.Bff.Api.Infrastructure.Exceptions;

/// <summary>
/// Exception that maps to RFC 7807 Problem Details with stable error codes.
/// </summary>
public sealed class SdapProblemException : Exception
{
    public string Code { get; }
    public string Title { get; }
    public string? Detail { get; }
    public int StatusCode { get; }
    public Dictionary<string, object>? Extensions { get; }

    public SdapProblemException(
        string code,
        string title,
        string? detail = null,
        int statusCode = 400,
        Dictionary<string, object>? extensions = null)
        : base($"{code}: {title}")
    {
        Code = code;
        Title = title;
        Detail = detail;
        StatusCode = statusCode;
        Extensions = extensions;
    }
}
```

#### Task 2.4: Update FileAccessEndpoints.cs
**File:** `Spe.Bff.Api/Api/FileAccessEndpoints.cs`

**Add GUID validation:**
```csharp
public static class FileAccessEndpoints
{
    public static IEndpointRouteBuilder MapFileAccessEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/documents")
            .RequireAuthorization()
            .WithTags("File Access");

        // Get preview URL endpoint
        group.MapGet("/{id}/preview-url", GetPreviewUrlAsync)
            .WithName("GetPreviewUrl")
            .WithOpenApi();

        // Get download URL endpoint
        group.MapGet("/{id}/download-url", GetDownloadUrlAsync)
            .WithName("GetDownloadUrl")
            .WithOpenApi();

        return app;
    }

    private static async Task<IResult> GetPreviewUrlAsync(
        string id,
        [FromServices] IAccessDataSource accessDataSource,
        [FromServices] SpeFileStore fileStore,
        [FromServices] ILogger<Program> logger,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var correlationId = httpContext.GetCorrelationId();

        // Validate GUID format (prevent driveItemId from being passed)
        if (!Guid.TryParse(id, out var documentId))
        {
            logger.LogWarning(
                "Invalid document ID format: {Id}. Expected GUID. CorrelationId: {CorrelationId}",
                id, correlationId);

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

        try
        {
            logger.LogInformation(
                "Getting preview URL for document {DocumentId}. CorrelationId: {CorrelationId}",
                documentId, correlationId);

            // Resolve storage pointers from Dataverse
            var (driveId, itemId) = await accessDataSource.GetSpePointersAsync(
                documentId,
                cancellationToken);

            // Get preview URL from Graph API
            var previewUrl = await fileStore.GetPreviewUrlAsync(
                driveId,
                itemId,
                cancellationToken);

            // Get file metadata
            var metadata = await fileStore.GetFileMetadataAsync(
                driveId,
                itemId,
                cancellationToken);

            var response = new
            {
                previewUrl = previewUrl,
                documentInfo = new
                {
                    id = documentId,
                    name = metadata.Name,
                    fileExtension = Path.GetExtension(metadata.Name),
                    size = metadata.Size,
                    graphItemId = itemId,
                    graphDriveId = driveId
                },
                correlationId = correlationId
            };

            logger.LogInformation(
                "Preview URL generated for document {DocumentId}. CorrelationId: {CorrelationId}",
                documentId, correlationId);

            return Results.Ok(response);
        }
        catch (SdapProblemException ex)
        {
            logger.LogWarning(
                ex,
                "Problem getting preview URL for document {DocumentId}. Code: {Code}. CorrelationId: {CorrelationId}",
                documentId, ex.Code, correlationId);

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
            logger.LogError(
                ex,
                "Unexpected error getting preview URL for document {DocumentId}. CorrelationId: {CorrelationId}",
                documentId, correlationId);

            return Results.Problem(
                title: "Internal server error",
                detail: "An unexpected error occurred while processing your request.",
                statusCode: 500,
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = correlationId
                });
        }
    }

    // Similar changes for GetDownloadUrlAsync...
}
```

#### Task 2.5: Register IAccessDataSource in DI
**File:** `Spe.Bff.Api/Program.cs`

```csharp
// Add to service registration section
builder.Services.AddScoped<IAccessDataSource, DataverseAccessDataSource>();
```

---

### Phase 3: Build & Deploy v1.0.5

#### Task 3.1: Update Version Numbers
**Files to update:**
- `ControlManifest.Input.xml`: version="1.0.5"
- `Solution.xml`: <Version>1.0.5</Version>

#### Task 3.2: Build PCF Control
```bash
cd /c/code_files/spaarke/src/controls/SpeFileViewer

# Disable central package management
mv ../../Directory.Packages.props ../../Directory.Packages.props.disabled

# Clean and rebuild
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

#### Task 3.3: Deploy to Dataverse
```bash
# Delete broken v1.0.4 solution first (via make.powerapps.com UI)

# Import v1.0.5
pac solution import \
  --path "SpeFileViewerSolution_v1.0.5.zip" \
  --force-overwrite \
  --publish-changes
```

#### Task 3.4: Deploy BFF Changes
```bash
cd /c/code_files/spaarke/src/api/Spe.Bff.Api

# Build and publish
dotnet publish -c Release -o publish

# Deploy to Azure (example using zip deploy)
az webapp deploy \
  --resource-group <rg-name> \
  --name spe-api-dev-67e2xz \
  --src-path publish.zip \
  --type zip
```

---

### Phase 4: Testing & Verification

#### Test Case 1: Valid Document with Mapping
**Setup:**
- Document exists in Dataverse
- `sprk_graphdriveid` and `sprk_graphitemid` populated
- File exists in SharePoint Embedded

**Expected Result:**
- ✅ Preview loads successfully
- ✅ Console shows: `[SpeFileViewer] Using form record ID: {guid}`
- ✅ No errors

#### Test Case 2: Document Without Mapping
**Setup:**
- Document exists in Dataverse
- `sprk_graphitemid` is empty or invalid

**Expected Result:**
- ❌ Error shown: "This file is initializing. Please try again in a moment."
- ✅ Console shows error code: `mapping_missing_item`
- ✅ HTTP 409 response

#### Test Case 3: Document Not Found
**Setup:**
- Invalid GUID or document deleted

**Expected Result:**
- ❌ Error shown: "Document not found. It may have been deleted."
- ✅ Console shows error code: `document_not_found`
- ✅ HTTP 404 response

#### Test Case 4: Invalid ID Format (Regression Test)
**Setup:**
- Manually configure documentId with SharePoint Item ID: `01LBYCMX...`

**Expected Result:**
- ❌ Error shown immediately (PCF validation)
- ✅ Console shows: "Configured documentId is not a valid GUID"
- ✅ No BFF call made (caught at PCF layer)

#### Test Case 5: Storage File Deleted
**Setup:**
- Document mapping exists
- File deleted from SharePoint Embedded

**Expected Result:**
- ❌ Error shown: "File has been removed from storage."
- ✅ Console shows error code: `storage_not_found`
- ✅ HTTP 404 from Graph API

---

## Comparison: My Approach vs Senior Dev Spec

### Agreements ✅
1. **Root cause diagnosis**: PCF sending driveItemId instead of Document GUID
2. **Architecture validity**: PCF → BFF → Dataverse → Graph → SPE is correct
3. **MSAL scope**: Using named scope `api://{BFF_ID}/SDAP.Access` (already correct in v1.0.3)
4. **Control type**: Must be `control-type="standard"` not "virtual"
5. **Error codes**: Stable error codes (invalid_id, document_not_found, mapping_missing_*)
6. **IAccessDataSource**: Abstraction layer for Dataverse queries

### Differences / Enhancements 🔄
1. **PCF-side validation**: I added GUID format validation in PCF (senior dev didn't specify, but it's a good defense-in-depth measure)
2. **Error handling details**: I provided more specific error message mappings
3. **Optional UX guard**: I added pre-flight check for driveId/itemId (senior dev mentioned as optional)
4. **Exception type**: I created `SdapProblemException` for cleaner error handling (senior dev suggested but didn't provide full implementation)

### Implementation Approach Differences
- **Senior Dev:** Provides code samples as reference
- **My Plan:** Provides complete file-by-file implementation with line numbers and full context

---

## Risk Assessment

### High Confidence ✅
1. Control manifest fix (`standard` not `virtual`) - Simple, low-risk
2. GUID validation in PCF - Defensive programming, prevents bad inputs
3. IAccessDataSource interface - Clean architecture, testable
4. Error code standardization - Improves observability

### Medium Confidence ⚠️
1. Validation heuristics (`IsLikelyDriveId`, `IsLikelyItemId`) - May need adjustment based on actual data
2. Error message wording - May need UX review
3. Exception handling flow - Need to ensure all paths covered

### Low Risk Areas ✅
1. MSAL configuration - Already correct in v1.0.3
2. CORS - Already configured
3. UAC - Already implemented (DocumentAuthorizationFilter)

---

## Timeline Estimate

### Phase 1: PCF Control Fixes
- **Time:** 3-4 hours
- **Complexity:** Low-Medium
- **Risk:** Low (mostly validation and error handling improvements)

### Phase 2: BFF API Enhancements
- **Time:** 4-5 hours
- **Complexity:** Medium
- **Risk:** Low-Medium (new abstraction layer, need thorough testing)

### Phase 3: Build & Deploy
- **Time:** 1-2 hours
- **Complexity:** Low
- **Risk:** Low (established process)

### Phase 4: Testing & Verification
- **Time:** 2-3 hours
- **Complexity:** Medium
- **Risk:** Medium (need to test all error scenarios)

**Total Estimate:** 10-14 hours of development + testing

---

## Recommendation

**Proceed with v1.0.5 implementation following senior dev's specification with the enhancements I've outlined above.**

### Key Success Criteria
1. ✅ Control appears in Dataverse control lists (fix from v1.0.4 breakage)
2. ✅ Only Document GUIDs accepted (prevent root cause error)
3. ✅ Clear error codes for all failure scenarios
4. ✅ IAccessDataSource abstraction enables better testing
5. ✅ All existing functionality preserved

### Next Steps
1. **Get approval** from senior dev on this implementation plan
2. **Execute Phase 1** (PCF fixes) first - can be tested independently
3. **Execute Phase 2** (BFF enhancements) - requires coordination with BFF deployment
4. **Deploy and test** in development environment
5. **User acceptance testing** before production deployment

---

**Plan Version:** 1.0
**Created:** November 24, 2025
**Status:** Ready for Review & Implementation
