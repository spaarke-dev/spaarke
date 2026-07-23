# Relationship Navigation Pattern

> **Last Reviewed**: 2026-07-10 (added client `@odata.bind` GUID-normalization rule — ADR-044 / FAILURE-MODES AP-6)
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When
Setting lookup fields, querying by relationship, discovering navigation properties, or building any client-side `@odata.bind` (create/update payloads in `Create*Wizard` services, PCF write handlers, etc.).

## Read These Files
1. `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` — EntityReference usage + metadata queries
2. `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs` — @odata.bind usage (REST)
3. `src/server/shared/Spaarke.Dataverse/Models.cs` — LookupNavigationMetadata model
4. `src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts` — client `cleanGuid` (canonical `@odata.bind` GUID normalizer) + `findNavProp`/`applyResolverFields`

## Constraints
- **ADR-007**: Facade must handle lookup complexity — callers pass GUIDs, not EntityReferences
- **ADR-044**: Dataverse GUIDs MUST be canonicalized (bare, lowercase) at every boundary — client `@odata.bind` uses the shared `cleanGuid`

## Key Rules
- SDK (ServiceClient): use logical name (lowercase) — `entity["sprk_matter"] = new EntityReference("sprk_matter", guid)`
- Web API / Xrm.WebApi: use SchemaName (CASE-SENSITIVE) — `"sprk_Matter@odata.bind": "/sprk_matters(guid)"`
- OData filter by lookup: `_sprk_matter_value eq {guid}` (underscore prefix, logical name)
- SchemaName convention: publisher prefix + PascalCase (e.g., `sprk_CompletedBy`)
- Metadata discovery: `RetrieveEntityRequest` with `EntityFilters.Relationships` to find nav property names
- CRITICAL: Wrong casing in `@odata.bind` causes silent save failures
- **GUID normalization (client `@odata.bind`, MANDATORY — ADR-044)**: Xrm sources (`Xrm.Utility.lookupObjects`, `userSettings.userId`, `Xrm.WebApi.createRecord`) return braced + UPPERCASE GUIDs. Dataverse rejects `({GUID})` in the key predicate with HTTP 400 **"Error in query syntax"** (names no property — it's a URL-parse failure). ALWAYS wrap the GUID before building the bind: `import { cleanGuid } from '@spaarke/ui-components'` → `/${entitySet}(${cleanGuid(id)})`. No-op on bare GUIDs. **Do NOT hand-roll `.replace(/[{}]/g,'')`** — the scattered local copies are what caused this bug. See FAILURE-MODES AP-6.
