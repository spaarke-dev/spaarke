# ADR-020: Versioning Strategy (Concise)

> **Status**: Proposed
> **Domain**: API/Package Compatibility
> **Last Updated**: 2025-12-18

---

## Decision

Use **SemVer for packages**, **tolerant readers** for payloads, and **explicit schema versioning** for evolving contracts. No silent breaking changes.

**Rationale**: Multiple deployable surfaces (API, jobs, PCF, packages) require clear compatibility rules to prevent brittle deployments.

---

## Constraints

### ✅ MUST

- **MUST** use SemVer for client packages (major bump for breaking changes)
- **MUST** implement tolerant readers (accept older payloads, default missing fields)
- **MUST** include explicit version input for evolving contracts
- **MUST** require ADR update and migration plan for breaking changes
- **MUST** provide deprecation window before removal

### ❌ MUST NOT

- **MUST NOT** make silent breaking changes
- **MUST NOT** rename or change semantics of existing fields without versioning
- **MUST NOT** break job payloads without new JobType or version

---

## Versioning by Surface

### APIs

- Prefer additive changes (new fields, new endpoints)
- Avoid breaking changes (renaming, semantic changes)
- Version via URL path if breaking change unavoidable

### Jobs (ADR-004)

- Keep `JobContract` envelope stable
- Treat `Payload` as versioned
- Include `payloadVersion` when payload evolves
- New breaking payload = new `JobType`

### Client Packages

- Clear public API surface (barrel exports)
- Major version bump for breaking changes
- Document migration paths

### Cache Keys (ADR-014)

- Include version input in all keys
- Invalidate on schema/model changes

---

## Tolerant Reader Pattern

```csharp
public record AnalysisPayload
{
    public string DocumentId { get; init; } = "";
    public string AnalysisType { get; init; } = "summary"; // default for old payloads
    public int PayloadVersion { get; init; } = 1;
}
```

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-004](ADR-004-job-contract.md) | Job payload versioning |
| [ADR-012](ADR-012-shared-components.md) | Package versioning |
| [ADR-014](ADR-014-ai-caching.md) | Cache key versioning |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-020-versioning-strategy-apis-jobs-client-packages.md](../../docs/adr/ADR-020-versioning-strategy-apis-jobs-client-packages.md)

---

**Lines**: ~95

