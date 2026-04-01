# Dataverse Patterns Index

> Pointer-based pattern files for Dataverse plugins, Web API, and entity operations.
> Each file points to canonical source code — read the code, not descriptions.

| Pattern | When to Load |
|---------|-------------|
| [plugin-structure.md](plugin-structure.md) | Creating/modifying Dataverse plugins |
| [web-api-client.md](web-api-client.md) | Accessing Dataverse from BFF API |
| [entity-operations.md](entity-operations.md) | CRUD on Dataverse entities |
| [relationship-navigation.md](relationship-navigation.md) | Lookups, @odata.bind, navigation properties |
| [polymorphic-resolver.md](polymorphic-resolver.md) | Child-to-multiple-parent associations |

## Key Constraint (ADR-002)
Plugins: <200 LoC, <50ms p95, NO HTTP calls (except Custom API Proxy → BFF).

## Related
- [Plugin Constraints](../../constraints/plugins.md) — MUST/MUST NOT rules
- [Data Constraints](../../constraints/data.md) — Caching and data access rules
