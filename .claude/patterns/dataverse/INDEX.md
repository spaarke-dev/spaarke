# Dataverse Patterns Index

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

> Pointer-based pattern files for Dataverse plugins, Web API, and entity operations.
> Each file points to canonical source code — read the code, not descriptions.

| Pattern | When to Load | Last Reviewed | Status |
|---------|-------------|---------------|--------|
| [plugin-structure.md](plugin-structure.md) | Creating/modifying Dataverse plugins | 2026-04-05 | Verified |
| [web-api-client.md](web-api-client.md) | Accessing Dataverse from BFF API | 2026-04-05 | Verified |
| [entity-operations.md](entity-operations.md) | CRUD on Dataverse entities | 2026-04-05 | Verified |
| [relationship-navigation.md](relationship-navigation.md) | Lookups, @odata.bind, navigation properties | 2026-04-05 | Verified |
| [polymorphic-resolver.md](polymorphic-resolver.md) | Child-to-multiple-parent associations | 2026-04-05 | Verified |

## Key Constraint (ADR-002)
Plugins: <200 LoC, <50ms p95, NO HTTP calls (except Custom API Proxy → BFF).

## Related
- [Plugin Constraints](../../constraints/plugins.md) — MUST/MUST NOT rules
- [Data Constraints](../../constraints/data.md) — Caching and data access rules
