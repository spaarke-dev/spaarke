# Phase 2 - Task 1: Create SpeFileStore

**Phase**: 2 (Simplify Service Layer)
**Duration**: 2-3 hours
**Risk**: Medium
**Patterns**: [endpoint-file-upload.md](../patterns/endpoint-file-upload.md), [dto-file-upload-result.md](../patterns/dto-file-upload-result.md)
**Anti-Patterns**: [anti-pattern-interface-proliferation.md](../patterns/anti-pattern-interface-proliferation.md), [anti-pattern-leaking-sdk-types.md](../patterns/anti-pattern-leaking-sdk-types.md)

---

## Current State (Before Starting)

**Current Architecture Problem**:
- 6-layer call chain: `Endpoint → IResourceStore → SpeResourceStore → ISpeService → OboSpeService → Graph SDK`
- Unnecessary interfaces: `IResourceStore`, `ISpeService` (single implementations)
- SDK type leakage: Methods return `DriveItem` from Microsoft.Graph SDK
- Scattered logic: Upload/download logic split across multiple services

**Performance Impact**:
- Call chain depth adds 50-100ms per request (method call overhead)
- Harder to trace execution path (6 layers to debug)
- Harder to optimize (changes require touching multiple files)

**Quick Verification**:
```bash
# Check current architecture exists
ls src/api/Spe.Bff.Api/Services/SpeResourceStore.cs
ls src/api/Spe.Bff.Api/Services/OboSpeService.cs

# Count current interfaces (should be ~10)
grep -r "^public interface I" src/api/Spe.Bff.Api/ | wc -l
```

---

## Background: Why This Architecture Exists

**Historical Context**:
- Initial implementation followed "Repository Pattern" from web tutorials
- Pattern assumes multiple storage implementations (SQL, NoSQL, file storage)
- Each layer added "for testability" (mock at every boundary)
- Followed "best practice" of programming to interfaces

**How We Got 6 Layers**:
1. **Endpoint**: Receives HTTP request
2. **IResourceStore**: "Generic resource abstraction" (never had multiple implementations)
3. **SpeResourceStore**: SharePoint Embedded specific implementation
4. **ISpeService**: "SPE service contract" (only one implementation)
5. **OboSpeService**: On-Behalf-Of token handling
6. **Graph SDK**: Actual Microsoft Graph API calls

**Why Each Layer Was Added**:
- `IResourceStore`: "Future-proofing" for other storage providers (never materialized)
- `ISpeService`: "Testability" to mock SPE operations
- Separate implementations: "Separation of concerns"

**What Changed Our Understanding**:
- **ADR-007 (SPE Storage Seam)**: Only one storage provider (SharePoint Embedded) - YAGNI
- **ADR-010 (DI Minimalism)**: No interfaces unless factory/collection pattern
- Performance profiling: 50-100ms overhead from call chain depth
- Debugging difficulty: 6 files to trace for single operation

**Why Consolidation is Correct**:
- Single responsibility: All SPE operations in one place
- Easier debugging: One file to check, not six
- Better performance: Direct calls, no interface dispatch overhead
- Still testable: Mock at infrastructure boundary (IGraphClientFactory)
- DTOs prevent SDK leakage: Return our types, not Microsoft's

---

## 🤖 AI PROMPT

```
CONTEXT: You are working on Phase 2 of the SDAP BFF API refactoring, specifically consolidating SPE storage logic into a single concrete class.

TASK: Create SpeFileStore.cs (concrete class, no interface) and associated DTOs to replace SpeResourceStore and OboSpeService.

CONSTRAINTS:
- Must NOT create an interface (ISpeFileStore) - violates ADR-010
- Must return DTOs only - never DriveItem or Graph SDK types
- Must inject IGraphClientFactory (interface is OK - factory pattern)
- Must consolidate logic from both SpeResourceStore and OboSpeService

VERIFICATION BEFORE STARTING:
1. Verify Phase 1 is complete (app config fixed, UAMI removed, ServiceClient Singleton)
2. Verify files exist: SpeResourceStore.cs, OboSpeService.cs (source files to consolidate)
3. Verify current architecture has 6-layer call chain (to be reduced to 3)
4. If any verification fails, STOP and report status mismatch

FOCUS: Stay focused on creating SpeFileStore and DTOs only. Do NOT update endpoints or DI in this task (that's Task 2.2 and 2.3).
```

---

## Goal

Create a consolidated **SpeFileStore** concrete class that combines the functionality of `SpeResourceStore` and `OboSpeService` while returning DTOs instead of Graph SDK types.

**Problem**:
- Current: 6-layer call chain (Endpoint → IResourceStore → SpeResourceStore → ISpeService → OboSpeService → Graph)
- Unnecessary interfaces (IResourceStore, ISpeService)
- Returns Graph SDK types (DriveItem) instead of DTOs

**Target**:
- New: 3-layer call chain (Endpoint → SpeFileStore → Graph)
- Concrete class (no interface)
- Returns DTOs only

---

## Pre-Flight Verification

### Step 0: Verify Context and Prerequisites

```bash
# 1. Verify Phase 1 completion
- [ ] Check API_APP_ID is 1e40baad-... (not 170c98e1)
grep "API_APP_ID" src/api/Spe.Bff.Api/appsettings.json

- [ ] Verify no UAMI references
grep -r "UAMI" src/api/Spe.Bff.Api/Infrastructure/GraphClientFactory.cs

- [ ] Verify ServiceClient is Singleton
grep -A 5 "DataverseServiceClientImpl" src/api/Spe.Bff.Api/Program.cs

# 2. Verify source files exist (to consolidate)
- [ ] ls src/api/Spe.Bff.Api/Services/SpeResourceStore.cs
- [ ] ls src/api/Spe.Bff.Api/Services/OboSpeService.cs

# 3. Document current state
- [ ] Count current interfaces: grep -r "^public interface I" src/api/ | wc -l
- [ ] Expected: ~10 interfaces (will reduce to 3)
```

**If any verification fails**: STOP and complete Phase 1 first, or report status mismatch.

---

## Files to Create

```bash
- [ ] src/api/Spe.Bff.Api/Storage/SpeFileStore.cs
- [ ] src/api/Spe.Bff.Api/Models/FileUploadResult.cs
- [ ] src/api/Spe.Bff.Api/Models/FileDownloadResult.cs
- [ ] src/api/Spe.Bff.Api/Models/FileMetadata.cs
```

---

## Implementation

### Step 1: Create DTOs (Never Return SDK Types)

**Pattern**: [dto-file-upload-result.md](../patterns/dto-file-upload-result.md)

#### File: `src/api/Spe.Bff.Api/Models/FileUploadResult.cs`

```csharp
namespace Spe.Bff.Api.Models;

/// <summary>
/// Result of a file upload operation.
/// Maps from Microsoft.Graph DriveItem but never exposes it.
/// </summary>
public record FileUploadResult
{
    /// <summary>
    /// SharePoint item ID (Graph API ID)
    /// </summary>
    public required string ItemId { get; init; }

    /// <summary>
    /// File name
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
    /// Web URL to access file in SharePoint
    /// </summary>
    public string? WebUrl { get; init; }

    /// <summary>
    /// When the file was created
    /// </summary>
    public DateTimeOffset? CreatedDateTime { get; init; }

    /// <summary>
    /// ETag for concurrency control
    /// </summary>
    public string? ETag { get; init; }
}
```

#### File: `src/api/Spe.Bff.Api/Models/FileDownloadResult.cs`

```csharp
namespace Spe.Bff.Api.Models;

/// <summary>
/// Result of a file download operation.
/// </summary>
public record FileDownloadResult
{
    /// <summary>
    /// File content stream
    /// </summary>
    public required Stream Content { get; init; }

    /// <summary>
    /// File name
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// MIME type for Content-Type header
    /// </summary>
    public required string ContentType { get; init; }

    /// <summary>
    /// File size for Content-Length header
    /// </summary>
    public long? ContentLength { get; init; }
}
```

#### File: `src/api/Spe.Bff.Api/Models/FileMetadata.cs`

```csharp
namespace Spe.Bff.Api.Models;

/// <summary>
/// File metadata without content.
/// </summary>
public record FileMetadata
{
    public required string ItemId { get; init; }
    public required string Name { get; init; }
    public long Size { get; init; }
    public string? MimeType { get; init; }
    public string? WebUrl { get; init; }
    public DateTimeOffset? CreatedDateTime { get; init; }
    public DateTimeOffset? LastModifiedDateTime { get; init; }
    public string? CreatedBy { get; init; }
    public string? ModifiedBy { get; init; }
}
```

### Step 2: Create SpeFileStore (Concrete Class)

**Pattern**: [endpoint-file-upload.md](../patterns/endpoint-file-upload.md)
**Anti-Pattern**: [anti-pattern-interface-proliferation.md](../patterns/anti-pattern-interface-proliferation.md)

#### File: `src/api/Spe.Bff.Api/Storage/SpeFileStore.cs`

```csharp
using Microsoft.Graph;
using Spe.Bff.Api.Models;
using Spe.Bff.Api.Infrastructure;

namespace Spe.Bff.Api.Storage;

/// <summary>
/// SharePoint Embedded file storage operations.
/// Concrete class - no interface (per ADR-010).
/// Returns DTOs only - never Graph SDK types (per ADR-007).
/// </summary>
public class SpeFileStore
{
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly ILogger<SpeFileStore> _logger;

    public SpeFileStore(
        IGraphClientFactory graphClientFactory,
        ILogger<SpeFileStore> logger)
    {
        _graphClientFactory = graphClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Upload a file to SharePoint Embedded container.
    /// Uses OBO token exchange via GraphClientFactory.
    /// </summary>
    public async Task<FileUploadResult> UploadFileAsync(
        string containerId,
        string fileName,
        Stream content,
        string userToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(containerId);
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrEmpty(userToken);

        _logger.LogInformation(
            "Uploading file {FileName} to container {ContainerId}",
            fileName, containerId);

        // Get Graph client with OBO token
        var graphClient = await _graphClientFactory
            .CreateOnBehalfOfClientAsync(userToken);

        // Upload to SharePoint Embedded
        // containerId is the driveId in Graph API
        var driveItem = await graphClient
            .Drives[containerId]
            .Root
            .ItemWithPath(fileName)
            .Content
            .Request()
            .PutAsync<DriveItem>(content, cancellationToken);

        _logger.LogInformation(
            "File uploaded successfully: {ItemId}",
            driveItem.Id);

        // ✅ Map to DTO (never return DriveItem!)
        return MapToDriveItemToUploadResult(driveItem);
    }

    /// <summary>
    /// Download a file from SharePoint Embedded container.
    /// </summary>
    public async Task<FileDownloadResult> DownloadFileAsync(
        string containerId,
        string itemId,
        string userToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(containerId);
        ArgumentException.ThrowIfNullOrEmpty(itemId);
        ArgumentException.ThrowIfNullOrEmpty(userToken);

        _logger.LogInformation(
            "Downloading file {ItemId} from container {ContainerId}",
            itemId, containerId);

        var graphClient = await _graphClientFactory
            .CreateOnBehalfOfClientAsync(userToken);

        // Get file metadata
        var driveItem = await graphClient
            .Drives[containerId]
            .Items[itemId]
            .Request()
            .GetAsync(cancellationToken);

        // Get file content
        var contentStream = await graphClient
            .Drives[containerId]
            .Items[itemId]
            .Content
            .Request()
            .GetAsync(cancellationToken);

        _logger.LogInformation(
            "File downloaded successfully: {FileName}",
            driveItem.Name);

        // ✅ Return DTO with stream
        return new FileDownloadResult
        {
            Content = contentStream,
            FileName = driveItem.Name ?? "download",
            ContentType = driveItem.File?.MimeType ?? "application/octet-stream",
            ContentLength = driveItem.Size
        };
    }

    /// <summary>
    /// Delete a file from SharePoint Embedded container.
    /// </summary>
    public async Task DeleteFileAsync(
        string containerId,
        string itemId,
        string userToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(containerId);
        ArgumentException.ThrowIfNullOrEmpty(itemId);
        ArgumentException.ThrowIfNullOrEmpty(userToken);

        _logger.LogInformation(
            "Deleting file {ItemId} from container {ContainerId}",
            itemId, containerId);

        var graphClient = await _graphClientFactory
            .CreateOnBehalfOfClientAsync(userToken);

        await graphClient
            .Drives[containerId]
            .Items[itemId]
            .Request()
            .DeleteAsync(cancellationToken);

        _logger.LogInformation("File deleted successfully: {ItemId}", itemId);
    }

    /// <summary>
    /// Get file metadata without downloading content.
    /// </summary>
    public async Task<FileMetadata> GetFileMetadataAsync(
        string containerId,
        string itemId,
        string userToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(containerId);
        ArgumentException.ThrowIfNullOrEmpty(itemId);
        ArgumentException.ThrowIfNullOrEmpty(userToken);

        _logger.LogDebug(
            "Getting metadata for file {ItemId} from container {ContainerId}",
            itemId, containerId);

        var graphClient = await _graphClientFactory
            .CreateOnBehalfOfClientAsync(userToken);

        var driveItem = await graphClient
            .Drives[containerId]
            .Items[itemId]
            .Request()
            .GetAsync(cancellationToken);

        // ✅ Map to DTO
        return MapDriveItemToMetadata(driveItem);
    }

    /// <summary>
    /// List files in a container (folder).
    /// </summary>
    public async Task<IEnumerable<FileMetadata>> ListFilesAsync(
        string containerId,
        string? folderPath,
        string userToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(containerId);
        ArgumentException.ThrowIfNullOrEmpty(userToken);

        _logger.LogDebug(
            "Listing files in container {ContainerId}, folder {FolderPath}",
            containerId, folderPath ?? "root");

        var graphClient = await _graphClientFactory
            .CreateOnBehalfOfClientAsync(userToken);

        IDriveItemChildrenCollectionPage children;

        if (string.IsNullOrEmpty(folderPath))
        {
            // List root folder
            children = await graphClient
                .Drives[containerId]
                .Root
                .Children
                .Request()
                .GetAsync(cancellationToken);
        }
        else
        {
            // List specific folder
            children = await graphClient
                .Drives[containerId]
                .Root
                .ItemWithPath(folderPath)
                .Children
                .Request()
                .GetAsync(cancellationToken);
        }

        // ✅ Map collection to DTOs
        return children
            .Where(item => item.File != null) // Files only, not folders
            .Select(MapDriveItemToMetadata)
            .ToList();
    }

    // ============================================================================
    // Private Mapping Methods (DriveItem → DTO)
    // ============================================================================

    private static FileUploadResult MapToDriveItemToUploadResult(DriveItem driveItem)
    {
        return new FileUploadResult
        {
            ItemId = driveItem.Id ?? throw new InvalidOperationException("DriveItem.Id is null"),
            Name = driveItem.Name ?? throw new InvalidOperationException("DriveItem.Name is null"),
            Size = driveItem.Size ?? 0,
            MimeType = driveItem.File?.MimeType,
            WebUrl = driveItem.WebUrl,
            CreatedDateTime = driveItem.CreatedDateTime,
            ETag = driveItem.ETag
        };
    }

    private static FileMetadata MapDriveItemToMetadata(DriveItem driveItem)
    {
        return new FileMetadata
        {
            ItemId = driveItem.Id ?? throw new InvalidOperationException("DriveItem.Id is null"),
            Name = driveItem.Name ?? throw new InvalidOperationException("DriveItem.Name is null"),
            Size = driveItem.Size ?? 0,
            MimeType = driveItem.File?.MimeType,
            WebUrl = driveItem.WebUrl,
            CreatedDateTime = driveItem.CreatedDateTime,
            LastModifiedDateTime = driveItem.LastModifiedDateTime,
            CreatedBy = driveItem.CreatedBy?.User?.DisplayName,
            ModifiedBy = driveItem.LastModifiedBy?.User?.DisplayName
        };
    }
}
```

---

## Validation

### Build Check
```bash
dotnet build
# Expected: Success, 0 warnings
```

### Code Review Checklist
```bash
# Verify SpeFileStore is concrete (no interface)
- [ ] No ISpeFileStore.cs file exists
- [ ] SpeFileStore class does NOT implement any interface

# Verify DTOs don't expose SDK types
- [ ] FileUploadResult has no DriveItem properties
- [ ] FileDownloadResult has no DriveItem properties
- [ ] FileMetadata has no DriveItem properties

# Verify proper dependency injection
- [ ] SpeFileStore injects IGraphClientFactory (interface OK - factory pattern)
- [ ] SpeFileStore injects ILogger<SpeFileStore>
- [ ] No other dependencies

# Verify methods return DTOs
grep "DriveItem" src/api/Spe.Bff.Api/Storage/SpeFileStore.cs
# Expected: Only in private methods and local variables, NEVER in public method signatures
```

### Test File Structure
```bash
# Verify all files created
ls src/api/Spe.Bff.Api/Storage/SpeFileStore.cs
ls src/api/Spe.Bff.Api/Models/FileUploadResult.cs
ls src/api/Spe.Bff.Api/Models/FileDownloadResult.cs
ls src/api/Spe.Bff.Api/Models/FileMetadata.cs

# All should exist
```

---

## Checklist

- [ ] **Pre-flight**: Verified Phase 1 complete
- [ ] **Pre-flight**: Verified source files exist (SpeResourceStore, OboSpeService)
- [ ] **Pre-flight**: Verified current architecture has 6 layers
- [ ] Created `FileUploadResult.cs` DTO
- [ ] Created `FileDownloadResult.cs` DTO
- [ ] Created `FileMetadata.cs` DTO
- [ ] Created `SpeFileStore.cs` concrete class (NO interface)
- [ ] Implemented `UploadFileAsync` method
- [ ] Implemented `DownloadFileAsync` method
- [ ] Implemented `DeleteFileAsync` method
- [ ] Implemented `GetFileMetadataAsync` method
- [ ] Implemented `ListFilesAsync` method
- [ ] All methods return DTOs (not DriveItem)
- [ ] Private mapping methods created (DriveItem → DTO)
- [ ] Injected `IGraphClientFactory` (interface OK - factory pattern)
- [ ] Added logging to all public methods
- [ ] Added argument validation
- [ ] Build succeeds: `dotnet build`
- [ ] No ISpeFileStore interface created
- [ ] No DriveItem in public method signatures

---

## Expected Results

**Before**:
- ❌ Two separate classes: SpeResourceStore + OboSpeService
- ❌ Unnecessary interfaces: IResourceStore, ISpeService
- ❌ Returns Graph SDK types: DriveItem
- ❌ 6-layer call chain

**After**:
- ✅ Single class: SpeFileStore
- ✅ Concrete class (no interface)
- ✅ Returns DTOs only: FileUploadResult, FileDownloadResult, FileMetadata
- ✅ Ready for 3-layer call chain (will be wired up in Task 2.2)

---

## Anti-Pattern Verification

### ✅ Avoided: Interface Proliferation
```bash
# Verify no ISpeFileStore created
ls src/api/Spe.Bff.Api/Storage/ISpeFileStore.cs
# Expected: File not found ✅
```

**Why**: ADR-010 - Register concrete classes, only use interfaces for factories or collections

### ✅ Avoided: Leaking SDK Types
```bash
# Verify public methods don't return DriveItem
grep -E "public.*DriveItem" src/api/Spe.Bff.Api/Storage/SpeFileStore.cs
# Expected: No results ✅
```

**Why**: ADR-007 - Never expose Graph SDK types, use DTOs for API contracts

---

## Troubleshooting

### Issue: Build fails with "DriveItem not found"

**Cause**: Missing Microsoft.Graph package reference

**Fix**:
```bash
cd src/api/Spe.Bff.Api
dotnet add package Microsoft.Graph
```

### Issue: ArgumentException on null check

**Cause**: Using .NET 7+ ArgumentException.ThrowIfNullOrEmpty

**Fix**: If on .NET 6, use traditional null checks:
```csharp
if (string.IsNullOrEmpty(containerId))
    throw new ArgumentException("Container ID cannot be null or empty", nameof(containerId));
```

### Issue: Stream already consumed error

**Cause**: Stream read multiple times

**Fix**: Ensure stream is only read once, don't dispose it (caller's responsibility)

---

## Context Verification

Before marking complete, verify:
- [ ] ✅ SpeFileStore created as concrete class (no interface)
- [ ] ✅ All DTOs created (3 files)
- [ ] ✅ No DriveItem in public method signatures
- [ ] ✅ Task stayed focused (did NOT update endpoints or DI - that's next tasks)
- [ ] ✅ Build succeeds

**If any item unchecked**: Review and fix before proceeding to Task 2.2

---

## Commit Message

```bash
git add src/api/Spe.Bff.Api/Storage/
git add src/api/Spe.Bff.Api/Models/File*.cs
git commit -m "feat(storage): create SpeFileStore concrete class per ADR-007

- Create SpeFileStore.cs (concrete class, no interface)
- Create DTOs: FileUploadResult, FileDownloadResult, FileMetadata
- Implement 5 core methods: Upload, Download, Delete, GetMetadata, List
- Return DTOs only (never DriveItem or Graph SDK types)
- Add logging and argument validation

Consolidates: SpeResourceStore + OboSpeService logic
ADR Compliance: ADR-007 (SPE Storage Seam), ADR-010 (No unnecessary interfaces)
Anti-Patterns Avoided: Interface proliferation, SDK type leakage
Task: Phase 2, Task 1"
```

---

## Next Task

⚠️ **IMPORTANT**: SpeFileStore is created but NOT yet wired up!

➡️ [Phase 2 - Task 2: Update Endpoints](phase-2-task-2-update-endpoints.md)

**What's next**: Update all endpoints to inject `SpeFileStore` instead of `IResourceStore` or `ISpeService`

---

## Related Resources

- **Patterns**:
  - [endpoint-file-upload.md](../patterns/endpoint-file-upload.md)
  - [dto-file-upload-result.md](../patterns/dto-file-upload-result.md)
- **Anti-Patterns**:
  - [anti-pattern-interface-proliferation.md](../patterns/anti-pattern-interface-proliferation.md)
  - [anti-pattern-leaking-sdk-types.md](../patterns/anti-pattern-leaking-sdk-types.md)
- **Architecture**: [TARGET-ARCHITECTURE.md](../TARGET-ARCHITECTURE.md#solution-1-simplified-service-layer)
- **ADR**: [ARCHITECTURAL-DECISIONS.md](../ARCHITECTURAL-DECISIONS.md) - ADR-007, ADR-010
