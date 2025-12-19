# ADR-007: SpeFileStore Facade (Concise)

> **Status**: Accepted
> **Domain**: Data/Storage
> **Last Updated**: 2025-12-18

---

## Decision

Use **single focused facade** (`SpeFileStore`) for all SPE/Graph operations. No generic `IResourceStore`. Expose only SDAP DTOs.

**Rationale**: Graph SDK already provides abstraction. Additional interfaces add indirection without value.

---

## Constraints

### ✅ MUST

- **MUST** route all SPE operations through `SpeFileStore`
- **MUST** expose only SDAP types (`UploadSessionDto`, `FileHandleDto`, `VersionInfoDto`)
- **MUST** propagate correlation ID to Graph requests
- **MUST** configure retry/timeout inside facade

### ❌ MUST NOT

- **MUST NOT** inject `GraphServiceClient` outside `SpeFileStore`
- **MUST NOT** expose Graph SDK types in endpoint DTOs
- **MUST NOT** create `IResourceStore` abstraction

---

## Implementation Pattern

```csharp
// ✅ DO: Use SpeFileStore facade
public class DocumentController(SpeFileStore store)
{
    public async Task<FileHandleDto> GetFile(string id) =>
        await store.GetFileAsync(id);  // Returns SDAP DTO
}

// ❌ DON'T: Inject Graph directly
public class BadController(GraphServiceClient graph)
{
    // WRONG - Graph types leak to callers
}
```

### Facade Structure

```csharp
public class SpeFileStore
{
    private readonly IGraphClientFactory _factory;

    // SDAP DTOs only
    public Task<FileHandleDto> GetFileAsync(string id) { ... }
    public Task<UploadSessionDto> CreateUploadSessionAsync(...) { ... }
    public Task<VersionInfoDto> GetVersionsAsync(string id) { ... }
}
```

**See**: [SpeFileStore Pattern](../patterns/data/spefilestore-usage.md)

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-003](ADR-003-authorization-seams.md) | Storage seam for authorization |
| [ADR-005](ADR-005-flat-storage.md) | Flat storage model |
| [ADR-010](ADR-010-di-minimalism.md) | Concrete registration |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-007-spe-storage-seam-minimalism.md](../../docs/adr/ADR-007-spe-storage-seam-minimalism.md)

---

**Lines**: ~80
