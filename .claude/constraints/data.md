# Data & Storage Constraints

> **Domain**: Data Access, Storage, Caching
> **Source ADRs**: ADR-005, ADR-007, ADR-009
> **Last Updated**: 2025-12-18

---

## When to Load This File

Load when:
- Working with SharePoint Embedded (SPE) storage
- Implementing file operations via SpeFileStore
- Adding caching logic
- Working with document hierarchy or metadata

---

## MUST Rules

### Flat Storage (ADR-005)

- ✅ **MUST** store documents flat in SPE containers (no nested folders)
- ✅ **MUST** maintain hierarchy/relationships in Dataverse metadata
- ✅ **MUST** use Dataverse lookups for folder-like navigation
- ✅ **MUST** version documents via SPE versioning (not folder-based)

### SpeFileStore Facade (ADR-007)

- ✅ **MUST** access SPE files only through `SpeFileStore`
- ✅ **MUST** return domain types from facade methods
- ✅ **MUST** handle Graph SDK throttling/retries inside facade
- ✅ **MUST** use `ContainerId` + `DriveItemId` for file identification

### Redis Caching (ADR-009)

- ✅ **MUST** use `IDistributedCache` (Redis) for cross-request caching
- ✅ **MUST** use `RequestCache` for per-request collapse
- ✅ **MUST** include version input in cache keys (rowVersion, ETag)
- ✅ **MUST** scope cache keys by tenant

---

## MUST NOT Rules

### Flat Storage (ADR-005)

- ❌ **MUST NOT** create nested folder structures in SPE
- ❌ **MUST NOT** rely on SPE folder paths for navigation
- ❌ **MUST NOT** store folder hierarchy in SPE metadata

### SpeFileStore Facade (ADR-007)

- ❌ **MUST NOT** expose Graph SDK types above `SpeFileStore`
- ❌ **MUST NOT** make direct Graph/SPE calls outside facade
- ❌ **MUST NOT** create generic `IResourceStore<T>` abstractions
- ❌ **MUST NOT** inject `GraphServiceClient` into controllers/endpoints

### Redis Caching (ADR-009)

- ❌ **MUST NOT** use in-process L1 cache without profiling proof
- ❌ **MUST NOT** cache authorization decisions (cache data only)
- ❌ **MUST NOT** hard-code TTLs in endpoints (centralize in options)
- ❌ **MUST NOT** cache raw document bytes without ADR-015 compliance

---

## Quick Reference Patterns

### SpeFileStore Usage

```csharp
// Correct: Use SpeFileStore facade
var file = await _speFileStore.GetFileAsync(containerId, driveItemId, ct);
var stream = await _speFileStore.GetContentAsync(containerId, driveItemId, ct);

// Wrong: Direct Graph SDK usage
var driveItem = await _graphClient.Drives[driveId].Items[itemId].GetAsync(); // ❌
```

**See**: [SpeFileStore Pattern](../patterns/storage/spe-filestore.md)

### Cache Key Pattern

```csharp
// Use centralized key builder
var key = DistributedCacheExtensions.CreateKey(
    "ai-embedding",           // category
    tenantId,                 // tenant isolation
    documentId,               // identifier
    $"v:{rowVersion}"         // version
);
```

### Storage Architecture

| Layer | Responsibility |
|-------|---------------|
| SPE Containers | Flat blob storage, versioning |
| Dataverse | Metadata, relationships, hierarchy |
| SpeFileStore | Unified access facade |
| Redis | Cross-request caching |

---

## Source ADRs (Full Context)

| ADR | Focus | When to Load |
|-----|-------|--------------|
| [ADR-005](../adr/ADR-005-flat-storage.md) | Flat storage philosophy | Migration planning |
| [ADR-007](../adr/ADR-007-spefilestore.md) | SpeFileStore design | New storage operations |
| [ADR-009](../adr/ADR-009-redis-caching.md) | Caching strategy | Cache implementation |

---

**Lines**: ~105
**Purpose**: Single-file reference for all data/storage constraints

