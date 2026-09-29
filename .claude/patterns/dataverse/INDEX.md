# Dataverse Patterns Index

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

> Pointer-based pattern files for Dataverse Web API, entity operations, and the server-side write path (no plugins — ADR-002).
> Each file points to canonical source code — read the code, not descriptions.

| Pattern | When to Load | Last Reviewed | Status |
|---------|-------------|---------------|--------|
| [plugin-structure.md](plugin-structure.md) | Retired → redirect: about to write a plugin, or a record needs a rule enforced on save | 2026-09-25 | Verified |
| [web-api-client.md](web-api-client.md) | Accessing Dataverse from BFF API | 2026-04-05 | Verified |
| [entity-operations.md](entity-operations.md) | CRUD on Dataverse entities | 2026-04-05 | Verified |
| [relationship-navigation.md](relationship-navigation.md) | Lookups, @odata.bind, navigation properties | 2026-04-05 | Verified |
| [polymorphic-resolver.md](polymorphic-resolver.md) | Child-to-multiple-parent associations | 2026-04-05 | Verified |

## Key Constraint (ADR-002, reviewed 2026-09-25)
No Dataverse plugins. Record invariants have one owner in the BFF write path; clients preview only; security fails closed.

## Related
- [Plugin + Write-Path Constraints](../../constraints/plugins.md) — MUST/MUST NOT rules
- [Dataverse Write-Path Architecture](../../../docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md) — layers + invariant registry
- [Data Constraints](../../constraints/data.md) — Caching and data access rules
