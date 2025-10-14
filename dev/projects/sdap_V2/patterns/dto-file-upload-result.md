# Pattern: File Upload Result DTO

**Use For**: Returning file metadata from upload operations
**Task**: Creating DTOs that never expose Graph SDK types
**Time**: 5 minutes

---

## Quick Copy-Paste

```csharp
namespace Spe.Bff.Api.Models;

/// <summary>
/// Result of file upload operation.
/// NEVER exposes Graph SDK types (DriveItem, Drive, etc.)
/// </summary>
public record FileUploadResult
{
    /// <summary>
    /// SPE drive item ID (unique identifier for file in container)
    /// </summary>
    public required string ItemId { get; init; }

    /// <summary>
    /// File name as stored in SPE
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// MIME type (e.g., "application/pdf")
    /// </summary>
    public string? MimeType { get; init; }

    /// <summary>
    /// Web URL for viewing file in Office Online
    /// </summary>
    public string? WebUrl { get; init; }

    /// <summary>
    /// When file was created in SPE
    /// </summary>
    public DateTimeOffset? CreatedDateTime { get; init; }

    /// <summary>
    /// ETag for optimistic concurrency
    /// </summary>
    public string? ETag { get; init; }
}
```

---

## Mapping from Graph SDK

```csharp
// In SpeFileStore.cs
public async Task<FileUploadResult> UploadFileAsync(...)
{
    var driveItem = await graphClient
        .Storage.FileStorage.Containers[containerId]
        .Drive.Root.ItemWithPath(fileName).Content
        .Request()
        .PutAsync<DriveItem>(content, cancellationToken);

    // ALWAYS map to DTO before returning
    return new FileUploadResult
    {
        ItemId = driveItem.Id!,
        Name = driveItem.Name!,
        Size = driveItem.Size ?? 0,
        MimeType = driveItem.File?.MimeType,
        WebUrl = driveItem.WebUrl,
        CreatedDateTime = driveItem.CreatedDateTime,
        ETag = driveItem.ETag
    };
}
```

---

## Rules (ADR-007)

**✅ DO**:
- Use `record` types for immutability
- Use `required` for mandatory properties
- Use nullable types (`?`) for optional properties
- Add XML documentation comments
- Map Graph SDK types to DTOs in service layer

**❌ DON'T**:
- Return `DriveItem` directly from endpoints
- Return `Drive` or any Graph SDK type
- Expose Graph SDK types in API signatures
- Use nullable for required properties

---

## Checklist

- [ ] Uses `record` keyword
- [ ] Required properties marked with `required`
- [ ] Optional properties use nullable types (`?`)
- [ ] XML documentation for all properties
- [ ] Never exposes Graph SDK types

---

## Related Files

- Create in: `src/api/Spe.Bff.Api/Models/FileUploadResult.cs`
- Used by: All file upload endpoints
- Mapped from: `DriveItem` in `SpeFileStore.cs`
