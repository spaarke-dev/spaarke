# ADR-007: SPE storage seam minimalism (single focused facade)

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2025-09-27 |
| Updated | 2025-12-04 |
| Authors | Spaarke Engineering |

## Context

SharePoint Embedded (SPE) is integrated via Microsoft Graph, which already provides a high-level abstraction. A generic `IResourceStore` layered above Graph adds indirection without value. SDAP does not plan to support a second storage provider.

## Decision

| Rule | Description |
|------|-------------|
| **Single facade** | `SpeFileStore` encapsulates all Graph/SPE calls |
| **No generic interface** | Do not create `IResourceStore`; add interface later if seam needed for tests |
| **SDAP DTOs only** | Facade exposes only SDAP types (`UploadSessionDto`, `FileHandleDto`, `VersionInfoDto`) |
| **Internal config** | Graph SDK `RetryHandler` and correlation logging configured inside facade |

## Consequences

**Positive:**
- Eliminates unnecessary abstractions and duplicated retry/telemetry code
- Isolates Graph changes to a single class; callers remain stable

**Negative:**
- Facade is SPE-coupled (by design). If second provider ever required, introduce minimal `IFileStore` then

## Alternatives Considered

`IResourceStore` with multiple thin adapters. **Rejected** as premature generalization and added ceremony.

## Operationalization

### Migration

| Old | New |
|-----|-----|
| `IResourceStore` / `SpeResourceStore` | `SpeFileStore` |
| `ISpeService` / `IOboSpeService` | `SpeFileStore` |

### Implementation

| Aspect | Approach |
|--------|----------|
| `GraphServiceClient` | Injected once into `SpeFileStore` |
| Retry | SDK retry handler enabled |
| Correlation | Delegating handler for correlation |
| Callers | Never reference Graph types |

## Exceptions

If a second provider becomes a real requirement, introduce `IFileStore` with methods limited to operations SDAP actually needs.

## Success Metrics

| Metric | Target |
|--------|--------|
| Class count | Reduced |
| DI registrations | Fewer |
| Graph types above facade | Zero |
| Throttling behavior | Stable |
| Audit logging | Consistent Graph request IDs |

## Compliance

**Code review checklist:**
- [ ] No `GraphServiceClient` injected outside `SpeFileStore`
- [ ] No Graph SDK types in endpoint DTOs
- [ ] All SPE operations route through `SpeFileStore`
- [ ] Correlation ID passed to Graph requests
