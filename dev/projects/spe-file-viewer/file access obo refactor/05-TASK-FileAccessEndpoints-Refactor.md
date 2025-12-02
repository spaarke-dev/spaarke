# Task 05: Refactor FileAccessEndpoints with OBO and Validation

**Task ID**: `05-FileAccessEndpoints-Refactor`
**Estimated Time**: 45 minutes
**Status**: Not Started
**Dependencies**: 01-SdapProblemException, 03-IGraphClientFactory, 04-GraphClientFactory-Implementation

---

## 📋 Prompt

Refactor all 4 endpoints in `FileAccessEndpoints.cs` to use OBO authentication with SPE pointer validation. This replaces app-only authentication (which requires manual container grants) with user-context authentication (automatic permission enforcement).

---

## ✅ Todos

- [ ] Open `src/api/Spe.Bff.Api/Api/FileAccessEndpoints.cs`
- [ ] Add required using statements
- [ ] Refactor `GetPreviewUrl` endpoint (validate → OBO → Graph API)
- [ ] Refactor `GetPreview` endpoint
- [ ] Refactor `GetContent` endpoint
- [ ] Refactor `GetOffice` endpoint
- [ ] Remove try-catch blocks (let global exception handler handle errors)
- [ ] Add SPE pointer validation helper method
- [ ] Build and verify no compilation errors

---

## 📚 Required Knowledge

### Current Problems
1. **App-only authentication**: Service principal requires manual grants per container
2. **No validation**: Missing checks for SPE pointer format (driveId, itemId)
3. **Generic errors**: Try-catch blocks return 500 without structured details
4. **Wrong dependency**: Uses `SpeFileStore` instead of `IGraphClientFactory`

### OBO Flow (User Context)
```
PCF → BFF API → Extract Token → OBO Exchange → Graph API (as user)
```

**Benefits**:
- User permissions automatically enforced
- No manual container grants needed
- Scalable (works with any number of containers)

### SPE Pointer Validation Rules
From technical review:

| Field | Validation Rule | Error Code |
|-------|----------------|------------|
| `driveId` | Not null/empty | `mapping_missing_drive` (409) |
| `driveId` | Starts with "b!" | `invalid_drive_id` (400) |
| `itemId` | Not null/empty | `mapping_missing_item` (409) |
| `itemId` | Length >= 20 chars | `invalid_item_id` (400) |

### Method-Group Pattern
Keep using static local functions (fixes CS1593 compiler errors):

```csharp
docs.MapGet("/{documentId}/preview-url", GetPreviewUrl)
    .WithName("GetDocumentPreviewUrl");

static async Task<IResult> GetPreviewUrl(
    string documentId,
    IDocumentStorageResolver documentStorageResolver,
    IGraphClientFactory graphFactory,  // Changed from SpeFileStore
    ILogger<Program> logger,
    HttpContext context,
    CancellationToken ct)
{
    // Implementation...
}
```

---

## 📂 Related Files

**File to Modify**:
- [src/api/Spe.Bff.Api/Api/FileAccessEndpoints.cs](../../../src/api/Spe.Bff.Api/Api/FileAccessEndpoints.cs)

**Dependencies**:
- `IDocumentStorageResolver` (unchanged)
- `IGraphClientFactory` (replaces SpeFileStore)
- `SdapProblemException` (Task 01)

---

## 🎯 Implementation

### 1. Add Using Statements (top of file)

```csharp
using Spe.Bff.Api.Infrastructure.Exceptions;
using Microsoft.Graph.Drives.Item.Items.Item.Preview;
using Microsoft.Graph.Models;
```

### 2. Validation Helper Method (add inside class)

```csharp
/// <summary>
/// Validates SPE pointer format before calling Graph API.
/// Throws SdapProblemException for invalid/missing pointers.
/// </summary>
private static void ValidateSpePointers(string? driveId, string? itemId, string documentId)
{
    // Validate driveId exists
    if (string.IsNullOrWhiteSpace(driveId))
    {
        throw new SdapProblemException(
            "mapping_missing_drive",
            "SPE Drive ID Missing",
            $"Document {documentId} does not have a Graph Drive ID (sprk_graphdriveid field is empty). " +
            $"Ensure the document has been uploaded to SharePoint Embedded.",
            409
        );
    }

    // Validate driveId format (SharePoint Embedded drives always start with "b!")
    if (!driveId.StartsWith("b!", StringComparison.Ordinal))
    {
        throw new SdapProblemException(
            "invalid_drive_id",
            "Invalid SPE Drive ID Format",
            $"Drive ID '{driveId}' does not start with 'b!' (expected SharePoint Embedded container format)",
            400
        );
    }

    // Validate itemId exists
    if (string.IsNullOrWhiteSpace(itemId))
    {
        throw new SdapProblemException(
            "mapping_missing_item",
            "SPE Item ID Missing",
            $"Document {documentId} does not have a Graph Item ID (sprk_graphitemid field is empty). " +
            $"Ensure the document has been uploaded to SharePoint Embedded.",
            409
        );
    }

    // Validate itemId length (SharePoint item IDs are typically 20+ characters)
    if (itemId.Length < 20)
    {
        throw new SdapProblemException(
            "invalid_item_id",
            "Invalid SPE Item ID Format",
            $"Item ID '{itemId}' is too short (expected at least 20 characters)",
            400
        );
    }
}
```

### 3. Refactor GetPreviewUrl Endpoint

**Replace the entire `GetPreviewUrl` method** with:

```csharp
static async Task<IResult> GetPreviewUrl(
    string documentId,
    IDocumentStorageResolver documentStorageResolver,
    IGraphClientFactory graphFactory,  // Changed from SpeFileStore
    ILogger<Program> logger,
    HttpContext context,
    CancellationToken ct)
{
    logger.LogInformation("GetPreviewUrl called | DocumentId: {DocumentId} | TraceId: {TraceId}",
        documentId, context.TraceIdentifier);

    // 1. Validate document ID format
    if (!Guid.TryParse(documentId, out var docGuid))
    {
        throw new SdapProblemException(
            "invalid_id",
            "Invalid Document ID",
            $"Document ID '{documentId}' is not a valid GUID format",
            400
        );
    }

    // 2. Get document entity from Dataverse (includes SPE pointers)
    var document = await documentStorageResolver.GetDocumentAsync(docGuid, ct);

    if (document == null)
    {
        throw new SdapProblemException(
            "document_not_found",
            "Document Not Found",
            $"Document with ID '{documentId}' does not exist",
            404
        );
    }

    // 3. Validate SPE pointers (driveId, itemId)
    ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId);

    logger.LogInformation("SPE pointers validated | DriveId: {DriveId} | ItemId: {ItemId}",
        document.GraphDriveId, document.GraphItemId);

    // 4. Create Graph client using OBO (user context)
    var graphClient = await graphFactory.ForUserAsync(context, ct);

    // 5. Call Graph API to get preview URL
    var previewResponse = await graphClient.Drives[document.GraphDriveId]
        .Items[document.GraphItemId]
        .Preview
        .PostAsync(new PreviewPostRequestBody
        {
            Viewer = "onedrive"
        }, cancellationToken: ct);

    logger.LogInformation("Preview URL retrieved successfully | TraceId: {TraceId}",
        context.TraceIdentifier);

    // 6. Return structured response
    return TypedResults.Ok(new
    {
        documentId = document.Id,
        previewUrl = previewResponse?.GetUrl,
        embedUrl = previewResponse?.GetUrl,
        correlationId = context.TraceIdentifier
    });
}
```

### 4. Refactor GetPreview Endpoint

**Replace the entire `GetPreview` method** with:

```csharp
static async Task<IResult> GetPreview(
    string documentId,
    IDocumentStorageResolver documentStorageResolver,
    IGraphClientFactory graphFactory,
    ILogger<Program> logger,
    HttpContext context,
    CancellationToken ct)
{
    logger.LogInformation("GetPreview called | DocumentId: {DocumentId}", documentId);

    // 1. Validate document ID
    if (!Guid.TryParse(documentId, out var docGuid))
    {
        throw new SdapProblemException(
            "invalid_id",
            "Invalid Document ID",
            $"Document ID '{documentId}' is not a valid GUID format",
            400
        );
    }

    // 2. Get document entity
    var document = await documentStorageResolver.GetDocumentAsync(docGuid, ct);

    if (document == null)
    {
        throw new SdapProblemException(
            "document_not_found",
            "Document Not Found",
            $"Document with ID '{documentId}' does not exist",
            404
        );
    }

    // 3. Validate SPE pointers
    ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId);

    // 4. Get preview URL (same as GetPreviewUrl endpoint)
    var graphClient = await graphFactory.ForUserAsync(context, ct);

    var previewResponse = await graphClient.Drives[document.GraphDriveId]
        .Items[document.GraphItemId]
        .Preview
        .PostAsync(new PreviewPostRequestBody
        {
            Viewer = "onedrive"
        }, cancellationToken: ct);

    if (string.IsNullOrEmpty(previewResponse?.GetUrl))
    {
        throw new SdapProblemException(
            "preview_not_available",
            "Preview Not Available",
            $"Graph API did not return a preview URL for document {documentId}",
            500
        );
    }

    // 5. Redirect to preview page
    return TypedResults.Redirect(previewResponse.GetUrl);
}
```

### 5. Refactor GetContent Endpoint

**Replace the entire `GetContent` method** with:

```csharp
static async Task<IResult> GetContent(
    string documentId,
    IDocumentStorageResolver documentStorageResolver,
    IGraphClientFactory graphFactory,
    ILogger<Program> logger,
    HttpContext context,
    CancellationToken ct)
{
    logger.LogInformation("GetContent called | DocumentId: {DocumentId}", documentId);

    // 1. Validate document ID
    if (!Guid.TryParse(documentId, out var docGuid))
    {
        throw new SdapProblemException(
            "invalid_id",
            "Invalid Document ID",
            $"Document ID '{documentId}' is not a valid GUID format",
            400
        );
    }

    // 2. Get document entity
    var document = await documentStorageResolver.GetDocumentAsync(docGuid, ct);

    if (document == null)
    {
        throw new SdapProblemException(
            "document_not_found",
            "Document Not Found",
            $"Document with ID '{documentId}' does not exist",
            404
        );
    }

    // 3. Validate SPE pointers
    ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId);

    // 4. Download file content using OBO
    var graphClient = await graphFactory.ForUserAsync(context, ct);

    var contentStream = await graphClient.Drives[document.GraphDriveId]
        .Items[document.GraphItemId]
        .Content
        .GetAsync(cancellationToken: ct);

    if (contentStream == null)
    {
        throw new SdapProblemException(
            "content_not_found",
            "File Content Not Found",
            $"Graph API returned null content stream for document {documentId}",
            500
        );
    }

    // 5. Return file stream with proper content type
    var contentType = document.MimeType ?? "application/octet-stream";
    var fileName = document.FileName ?? $"{documentId}.bin";

    logger.LogInformation("Returning file content | FileName: {FileName} | ContentType: {ContentType}",
        fileName, contentType);

    return TypedResults.Stream(contentStream, contentType, fileName);
}
```

### 6. Refactor GetOffice Endpoint

**Replace the entire `GetOffice` method** with:

```csharp
static async Task<IResult> GetOffice(
    string documentId,
    IDocumentStorageResolver documentStorageResolver,
    IGraphClientFactory graphFactory,
    ILogger<Program> logger,
    HttpContext context,
    CancellationToken ct)
{
    logger.LogInformation("GetOffice called | DocumentId: {DocumentId}", documentId);

    // 1. Validate document ID
    if (!Guid.TryParse(documentId, out var docGuid))
    {
        throw new SdapProblemException(
            "invalid_id",
            "Invalid Document ID",
            $"Document ID '{documentId}' is not a valid GUID format",
            400
        );
    }

    // 2. Get document entity
    var document = await documentStorageResolver.GetDocumentAsync(docGuid, ct);

    if (document == null)
    {
        throw new SdapProblemException(
            "document_not_found",
            "Document Not Found",
            $"Document with ID '{documentId}' does not exist",
            404
        );
    }

    // 3. Validate SPE pointers
    ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId);

    // 4. Get Office web app URL using OBO
    var graphClient = await graphFactory.ForUserAsync(context, ct);

    var driveItem = await graphClient.Drives[document.GraphDriveId]
        .Items[document.GraphItemId]
        .GetAsync(requestConfiguration =>
        {
            requestConfiguration.QueryParameters.Select = new[] { "id", "name", "webUrl" };
        }, cancellationToken: ct);

    if (string.IsNullOrEmpty(driveItem?.WebUrl))
    {
        throw new SdapProblemException(
            "office_url_not_available",
            "Office URL Not Available",
            $"Graph API did not return a webUrl for document {documentId}",
            500
        );
    }

    logger.LogInformation("Redirecting to Office web app | WebUrl: {WebUrl}", driveItem.WebUrl);

    // 5. Redirect to Office web app
    return TypedResults.Redirect(driveItem.WebUrl);
}
```

---

## ✅ Acceptance Criteria

### Build Success
- [ ] Project builds without errors
- [ ] No CS1593 errors (method group pattern maintained)
- [ ] No missing using statements

### Code Quality
- [ ] All endpoints validate document ID (GUID format)
- [ ] All endpoints validate SPE pointers (driveId, itemId)
- [ ] All endpoints use OBO via `ForUserAsync`
- [ ] No try-catch blocks (global exception handler handles errors)
- [ ] Logging includes TraceId for correlation

### Validation Logic
- [ ] Invalid GUID → 400 `"invalid_id"`
- [ ] Document not found → 404 `"document_not_found"`
- [ ] Missing driveId → 409 `"mapping_missing_drive"`
- [ ] Invalid driveId format → 400 `"invalid_drive_id"`
- [ ] Missing itemId → 409 `"mapping_missing_item"`
- [ ] Invalid itemId format → 400 `"invalid_item_id"`

### Testing Steps
1. Build solution: `dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj`
2. Run locally: `dotnet run --project src/api/Spe.Bff.Api`
3. Test with invalid document ID: `GET /api/documents/invalid-id/preview-url`
   - Expect: 400 `"invalid_id"`
4. Test with valid document ID: `GET /api/documents/{valid-guid}/preview-url`
   - Expect: 200 with preview URL (if user has access)
   - OR: 403 Forbidden (if user lacks access - expected)

---

## 📝 Notes

### Why Remove Try-Catch Blocks?
The global exception handler (Task 02) provides consistent error handling:
- Catches all exceptions automatically
- Converts to RFC 7807 Problem Details
- Includes correlation IDs
- Logs errors centrally

Endpoint-level try-catch blocks are redundant and hide errors from the global handler.

### Why Throw Instead of Return TypedResults.BadRequest?
Using exceptions provides:
- Consistent error format (global exception handler)
- Automatic correlation ID injection
- Centralized logging
- Stack traces for debugging

### Parameter Order
Keep the same parameter order for all methods:
1. Route parameters (`string documentId`)
2. Services (`IDocumentStorageResolver`, `IGraphClientFactory`, `ILogger`)
3. Framework (`HttpContext`, `CancellationToken`)

This ensures ASP.NET Core's DI resolves parameters correctly.

---

## 🔗 Related Documentation

- [Technical Review](./FILE-ACCESS-OBO-REFACTOR-REVIEW.md) - Section 5.5 (FileAccessEndpoints)
- [Current FileAccessEndpoints.cs](../../../src/api/Spe.Bff.Api/Api/FileAccessEndpoints.cs)

---

**Previous Task**: [04-TASK-GraphClientFactory-Implementation.md](./04-TASK-GraphClientFactory-Implementation.md)
**Next Task**: [06-TASK-SpeFileStore-Update.md](./06-TASK-SpeFileStore-Update.md)
