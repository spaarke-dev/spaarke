# BFF API Fix: Resolve Document GUID to SharePoint Embedded Pointers

## Problem Statement

The BFF API receives Document GUIDs from the PCF but cannot preview files because it doesn't query Dataverse to get the SharePoint Embedded pointers (`sprk_GraphDriveId` and `sprk_GraphItemId`).

**Current Error:** All preview requests return 404 "Document not found"

**Root Cause:** The BFF is trying to use the Document GUID directly with Graph API instead of:
1. Querying Dataverse for the Document record
2. Extracting `sprk_graphdriveid` (Drive ID) and `sprk_graphitemid` (Item ID)
3. Using those to call Graph API

## Architecture

### Data Model

| Dataverse Field | Purpose | Format | Example |
|----------------|---------|--------|---------|
| `sprk_documentid` | Primary key (GUID) | GUID | `ad1b0c34-52a5-f011-bbd3-7c1e5215b8b5` |
| `sprk_graphdriveid` | SharePoint Embedded Drive ID | `b!...` | `b!yLRdWEOAdkaWXskuRfByIRiz1S9kb...` |
| `sprk_graphitemid` | SharePoint Embedded Item ID | `01...` | `01LBYCMX76QPLGITR47BB355T4G2CVDL2B` |

### API Flow

```
┌─────────┐   Document GUID    ┌─────────────┐
│   PCF   │ ─────────────────> │  BFF API    │
└─────────┘                     │             │
                                │  1. Query   │──┐
                                │  Dataverse  │  │
                                └─────────────┘  │
                                        │         │
                                        V         V
┌──────────────┐              ┌──────────────────────┐
│   Dataverse  │ <────────── │ Get sprk_graphdriveid │
│              │              │ Get sprk_graphitemid  │
└──────────────┘              └──────────────────────┘
                                        │
                                        V
                                ┌─────────────┐
                                │  2. Call    │
                                │  Graph API  │
                                └─────────────┘
                                        │
                                        V
                                ┌─────────────┐
                                │  3. Return  │
                                │  Preview URL│
                                └─────────────┘
```

## Implementation Steps

### Step 1: Add Access Data Source Interface

**File:** `Spe.Bff.Api/Infrastructure/Data/IAccessDataSource.cs`

```csharp
namespace Spe.Bff.Api.Infrastructure.Data;

/// <summary>
/// Data source for querying Dataverse to resolve Document GUIDs to SharePoint Embedded pointers
/// </summary>
public interface IAccessDataSource
{
    /// <summary>
    /// Get SharePoint Embedded pointers (Drive ID and Item ID) for a document
    /// </summary>
    /// <param name="documentId">Dataverse Document GUID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tuple of (DriveId, ItemId)</returns>
    /// <exception cref="DocumentNotFoundException">Document not found in Dataverse</exception>
    /// <exception cref="MappingMissingException">Document exists but SPE pointers are missing/invalid</exception>
    Task<(string DriveId, string ItemId)> GetSpePointersAsync(Guid documentId, CancellationToken cancellationToken);
}

/// <summary>
/// Thrown when a document record is not found in Dataverse
/// </summary>
public class DocumentNotFoundException : Exception
{
    public Guid DocumentId { get; }

    public DocumentNotFoundException(Guid documentId, string? message = null)
        : base(message ?? $"Document {documentId} not found in Dataverse")
    {
        DocumentId = documentId;
    }
}

/// <summary>
/// Thrown when a document exists but SharePoint Embedded pointers are missing or invalid
/// </summary>
public class MappingMissingException : Exception
{
    public Guid DocumentId { get; }
    public string MissingField { get; }

    public MappingMissingException(Guid documentId, string missingField, string message)
        : base(message)
    {
        DocumentId = documentId;
        MissingField = missingField;
    }
}
```

### Step 2: Implement Dataverse Access Data Source

**File:** `Spe.Bff.Api/Infrastructure/Data/DataverseAccessDataSource.cs`

```csharp
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Spe.Bff.Api.Infrastructure.Data;

public class DataverseAccessDataSource : IAccessDataSource
{
    private readonly IOrganizationServiceAsync _dataverseService;
    private readonly ILogger<DataverseAccessDataSource> _logger;

    public DataverseAccessDataSource(
        IOrganizationServiceAsync dataverseService,
        ILogger<DataverseAccessDataSource> logger)
    {
        _dataverseService = dataverseService;
        _logger = logger;
    }

    public async Task<(string DriveId, string ItemId)> GetSpePointersAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Querying Dataverse for Document {DocumentId}", documentId);

        try
        {
            // Query Dataverse for the document record
            var entity = await _dataverseService.RetrieveAsync(
                "sprk_document",
                documentId,
                new ColumnSet("sprk_graphdriveid", "sprk_graphitemid"),
                cancellationToken
            );

            // Extract Drive ID
            var driveId = entity.GetAttributeValue<string>("sprk_graphdriveid");
            if (!IsLikelyDriveId(driveId))
            {
                _logger.LogWarning(
                    "Document {DocumentId} has missing or invalid Drive ID: {DriveId}",
                    documentId, driveId);
                throw new MappingMissingException(
                    documentId,
                    "sprk_graphdriveid",
                    "Drive ID is not recorded or invalid for this document. The file may still be uploading.");
            }

            // Extract Item ID
            var itemId = entity.GetAttributeValue<string>("sprk_graphitemid");
            if (!IsLikelyItemId(itemId))
            {
                _logger.LogWarning(
                    "Document {DocumentId} has missing or invalid Item ID: {ItemId}",
                    documentId, itemId);
                throw new MappingMissingException(
                    documentId,
                    "sprk_graphitemid",
                    "Item ID is not recorded or invalid for this document. The file may still be uploading.");
            }

            _logger.LogInformation(
                "Resolved Document {DocumentId} to Drive {DriveId}, Item {ItemId}",
                documentId, driveId, itemId);

            return (driveId, itemId);
        }
        catch (Exception ex) when (ex is not DocumentNotFoundException && ex is not MappingMissingException)
        {
            _logger.LogError(ex, "Failed to query Dataverse for Document {DocumentId}", documentId);
            throw new DocumentNotFoundException(documentId, $"Failed to retrieve document: {ex.Message}");
        }
    }

    /// <summary>
    /// Validate Drive ID format (should start with "b!" and be reasonably long)
    /// </summary>
    private static bool IsLikelyDriveId(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.StartsWith("b!", StringComparison.Ordinal)
            && value.Length > 20;
    }

    /// <summary>
    /// Validate Item ID format (should start with alphanumeric and be reasonably long)
    /// </summary>
    private static bool IsLikelyItemId(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length > 20
            && char.IsLetterOrDigit(value[0]);
    }
}
```

### Step 3: Update FileAccessEndpoints to Use Access Data Source

**File:** `Spe.Bff.Api/Api/FileAccessEndpoints.cs`

Update the preview endpoint to query Dataverse first:

```csharp
// Add IAccessDataSource to constructor
private readonly IAccessDataSource _accessDataSource;

public FileAccessEndpoints(
    IAccessDataSource accessDataSource,  // NEW
    ISpeFileStore speFileStore,
    IDocumentAuthorizationService authService,
    ILogger<FileAccessEndpoints> logger)
{
    _accessDataSource = accessDataSource;
    _speFileStore = speFileStore;
    _authService = authService;
    _logger = logger;
}

// Update GetPreviewUrl endpoint
app.MapGet("/api/documents/{documentId:guid}/preview-url", async (
    [FromRoute] Guid documentId,
    [FromHeader(Name = "X-Correlation-Id")] string? correlationId,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    correlationId ??= Guid.NewGuid().ToString();

    using var scope = _logger.BeginScope(new Dictionary<string, object>
    {
        ["CorrelationId"] = correlationId,
        ["DocumentId"] = documentId
    });

    try
    {
        _logger.LogInformation("Preview URL requested for Document {DocumentId}", documentId);

        // Step 1: Resolve Document GUID to SharePoint Embedded pointers
        var (driveId, itemId) = await _accessDataSource.GetSpePointersAsync(documentId, cancellationToken);

        _logger.LogInformation(
            "Resolved Document {DocumentId} to Drive {DriveId}, Item {ItemId}",
            documentId, driveId, itemId);

        // Step 2: Check user authorization
        var userId = httpContext.User.GetUserId();
        var hasAccess = await _authService.CanAccessDocumentAsync(userId, documentId, cancellationToken);

        if (!hasAccess)
        {
            _logger.LogWarning(
                "User {UserId} denied access to Document {DocumentId}",
                userId, documentId);

            return Results.Problem(
                title: "Access Denied",
                detail: "You do not have permission to access this file.",
                statusCode: 403,
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = correlationId,
                    ["documentId"] = documentId
                });
        }

        // Step 3: Get preview URL from SharePoint Embedded
        var previewUrl = await _speFileStore.GetPreviewUrlAsync(driveId, itemId, cancellationToken);

        _logger.LogInformation(
            "Preview URL generated for Document {DocumentId}",
            documentId);

        return Results.Ok(new FilePreviewResponse
        {
            PreviewUrl = previewUrl,
            DocumentId = documentId,
            CorrelationId = correlationId,
            DocumentInfo = new DocumentInfo
            {
                Name = "Unknown", // TODO: Get from Dataverse or Graph response
                Size = 0
            }
        });
    }
    catch (DocumentNotFoundException ex)
    {
        _logger.LogWarning(ex, "Document {DocumentId} not found", documentId);
        return Results.Problem(
            title: "Document Not Found",
            detail: ex.Message,
            statusCode: 404,
            extensions: new Dictionary<string, object?>
            {
                ["correlationId"] = correlationId,
                ["documentId"] = documentId,
                ["errorCode"] = "document_not_found"
            });
    }
    catch (MappingMissingException ex)
    {
        _logger.LogWarning(ex,
            "Document {DocumentId} missing {Field}",
            documentId, ex.MissingField);

        return Results.Problem(
            title: "File Not Ready",
            detail: ex.Message,
            statusCode: 404,
            extensions: new Dictionary<string, object?>
            {
                ["correlationId"] = correlationId,
                ["documentId"] = documentId,
                ["errorCode"] = $"mapping_missing_{ex.MissingField.ToLowerInvariant()}"
            });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to get preview URL for Document {DocumentId}", documentId);
        return Results.Problem(
            title: "Internal Server Error",
            detail: "An unexpected error occurred while generating the preview URL.",
            statusCode: 500,
            extensions: new Dictionary<string, object?>
            {
                ["correlationId"] = correlationId
            });
    }
})
.RequireAuthorization()
.WithName("GetDocumentPreviewUrl")
.WithOpenApi();
```

### Step 4: Register Services in DI Container

**File:** `Spe.Bff.Api/Program.cs`

```csharp
// Add Access Data Source
builder.Services.AddScoped<IAccessDataSource, DataverseAccessDataSource>();
```

### Step 5: Update SpeFileStore to Accept Drive ID and Item ID

**File:** `Spe.Bff.Api/Infrastructure/Graph/ISpeFileStore.cs`

```csharp
/// <summary>
/// Get preview URL for a file in SharePoint Embedded
/// </summary>
/// <param name="driveId">SharePoint Embedded Drive ID (from sprk_graphdriveid)</param>
/// <param name="itemId">SharePoint Embedded Item ID (from sprk_graphitemid)</param>
/// <param name="cancellationToken">Cancellation token</param>
/// <returns>Preview URL</returns>
Task<string> GetPreviewUrlAsync(string driveId, string itemId, CancellationToken cancellationToken);
```

**File:** `Spe.Bff.Api/Infrastructure/Graph/SpeFileStore.cs`

```csharp
public async Task<string> GetPreviewUrlAsync(
    string driveId,
    string itemId,
    CancellationToken cancellationToken)
{
    _logger.LogInformation("Getting preview URL for Drive {DriveId}, Item {ItemId}", driveId, itemId);

    try
    {
        // Call Microsoft Graph to get preview URL
        var previewAction = await _graphClient.Drives[driveId]
            .Items[itemId]
            .Preview
            .PostAsync(new Microsoft.Graph.Drives.Item.Items.Item.Preview.PreviewPostRequestBody
            {
                // Optional: Add viewer preferences
            }, cancellationToken: cancellationToken);

        if (previewAction?.GetUrl == null)
        {
            throw new InvalidOperationException("Graph API returned null preview URL");
        }

        _logger.LogInformation(
            "Preview URL generated for Drive {DriveId}, Item {ItemId}",
            driveId, itemId);

        return previewAction.GetUrl;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex,
            "Failed to get preview URL for Drive {DriveId}, Item {ItemId}",
            driveId, itemId);
        throw;
    }
}
```

## Testing

### Unit Tests

**File:** `Spe.Bff.Api.Tests/DataverseAccessDataSourceTests.cs`

```csharp
[Fact]
public async Task GetSpePointersAsync_ValidDocument_ReturnsPointers()
{
    // Arrange
    var documentId = Guid.NewGuid();
    var expectedDriveId = "b!yLRdWEOAdkaWXskuRfByIRiz1S9kb";
    var expectedItemId = "01LBYCMX76QPLGITR47BB355T4G2CVDL2B";

    // Mock Dataverse service
    var mockDvService = new Mock<IOrganizationServiceAsync>();
    mockDvService.Setup(x => x.RetrieveAsync(
            "sprk_document",
            documentId,
            It.IsAny<ColumnSet>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Entity("sprk_document", documentId)
        {
            ["sprk_graphdriveid"] = expectedDriveId,
            ["sprk_graphitemid"] = expectedItemId
        });

    var sut = new DataverseAccessDataSource(mockDvService.Object, Mock.Of<ILogger<DataverseAccessDataSource>>());

    // Act
    var (driveId, itemId) = await sut.GetSpePointersAsync(documentId, CancellationToken.None);

    // Assert
    Assert.Equal(expectedDriveId, driveId);
    Assert.Equal(expectedItemId, itemId);
}

[Fact]
public async Task GetSpePointersAsync_MissingDriveId_ThrowsMappingMissingException()
{
    // Arrange
    var documentId = Guid.NewGuid();
    var mockDvService = new Mock<IOrganizationServiceAsync>();
    mockDvService.Setup(x => x.RetrieveAsync(
            "sprk_document",
            documentId,
            It.IsAny<ColumnSet>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Entity("sprk_document", documentId)
        {
            ["sprk_graphdriveid"] = null,  // Missing!
            ["sprk_graphitemid"] = "01LBYCMX76QPLGITR47BB355T4G2CVDL2B"
        });

    var sut = new DataverseAccessDataSource(mockDvService.Object, Mock.Of<ILogger<DataverseAccessDataSource>>());

    // Act & Assert
    var ex = await Assert.ThrowsAsync<MappingMissingException>(
        () => sut.GetSpePointersAsync(documentId, CancellationToken.None));

    Assert.Equal("sprk_graphdriveid", ex.MissingField);
}
```

### Integration Test

1. **Create a test document** in Dataverse with valid `sprk_graphdriveid` and `sprk_graphitemid`
2. **Call the BFF API** with the document GUID
3. **Verify** it returns a preview URL

## Error Codes

| Code | HTTP Status | Meaning | User Message |
|------|-------------|---------|--------------|
| `document_not_found` | 404 | Document record doesn't exist in Dataverse | "Document not found. It may have been deleted." |
| `mapping_missing_sprk_graphdriveid` | 404 | Document exists but Drive ID is missing/invalid | "File is still uploading. Please try again in a moment." |
| `mapping_missing_sprk_graphitemid` | 404 | Document exists but Item ID is missing/invalid | "File is still uploading. Please try again in a moment." |
| `storage_not_found` | 404 | Graph API returned 404 (file deleted from SharePoint) | "File not found in storage. It may have been deleted." |
| `unauthorized` | 401 | Bearer token invalid or expired | "Authentication failed. Please refresh the page." |
| `access_denied` | 403 | User doesn't have access per UAC rules | "You do not have permission to access this file." |

## Deployment Checklist

- [ ] Implement `IAccessDataSource` interface
- [ ] Implement `DataverseAccessDataSource` with validation
- [ ] Update `FileAccessEndpoints` to query Dataverse
- [ ] Update `SpeFileStore.GetPreviewUrlAsync` signature
- [ ] Register `IAccessDataSource` in DI container
- [ ] Add unit tests
- [ ] Add integration tests
- [ ] Update API documentation
- [ ] Deploy to dev environment
- [ ] Test with PCF control
- [ ] Verify correlation IDs in logs
- [ ] Deploy to production

## Success Criteria

✅ PCF control sends Document GUID to BFF
✅ BFF queries Dataverse to get Drive ID and Item ID
✅ BFF calls Graph API with Drive ID and Item ID
✅ Preview URL is returned to PCF
✅ File preview displays in PCF control
✅ Proper error handling for missing/invalid pointers
✅ Correlation IDs tracked through entire flow
