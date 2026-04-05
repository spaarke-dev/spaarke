# Relationship Navigation Pattern

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When
Setting lookup fields, querying by relationship, or discovering navigation properties.

## Read These Files
1. `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` — EntityReference usage + metadata queries
2. `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs` — @odata.bind usage (REST)
3. `src/server/shared/Spaarke.Dataverse/Models.cs` — LookupNavigationMetadata model

## Constraints
- **ADR-007**: Facade must handle lookup complexity — callers pass GUIDs, not EntityReferences

## Key Rules
- SDK (ServiceClient): use logical name (lowercase) — `entity["sprk_matter"] = new EntityReference("sprk_matter", guid)`
- Web API / Xrm.WebApi: use SchemaName (CASE-SENSITIVE) — `"sprk_Matter@odata.bind": "/sprk_matters(guid)"`
- OData filter by lookup: `_sprk_matter_value eq {guid}` (underscore prefix, logical name)
- SchemaName convention: publisher prefix + PascalCase (e.g., `sprk_CompletedBy`)
- Metadata discovery: `RetrieveEntityRequest` with `EntityFilters.Relationships` to find nav property names
- CRITICAL: Wrong casing in `@odata.bind` causes silent save failures
