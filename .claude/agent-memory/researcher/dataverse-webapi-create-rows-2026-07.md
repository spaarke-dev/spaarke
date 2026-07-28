# Dataverse Web API v9.2 — creating custom-entity rows (2026-07)

**Date**: 2026-07-28
**Question**: Concrete, current Web API mechanics to seed catalog rows for custom sprk_ tables (spaarkedev1).

## Key facts (Learn, ms.date 2026-03)
- **Create**: `POST {org}/api/data/v9.2/{entitysetname}` with headers `OData-MaxVersion: 4.0`, `OData-Version: 4.0`, `Accept: application/json`, `Content-Type: application/json; charset=utf-8`, `Authorization: Bearer`. Default returns **204 No Content** with `OData-EntityId` response header = URI containing the minted GUID.
- **Get the row back**: add `Prefer: return=representation` → **201 Created**, body includes primary key (e.g. `accountid`) and `@odata.etag`. With return=representation the `OData-EntityId` header is NOT returned; read the PK from the body instead. Use `$select` to trim.
- **Lookup on create**: `"<NavProp>@odata.bind": "/<entityset>(<guid>)"`. NavProp is the single-valued **navigation property**, NOT the column logical name.
- **@odata.bind gotcha (Spaarke, metadata-verified)**: sprk_ lookup nav props are **PascalCase schema names**, and entity-set names are irregular. Real verified examples in `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs`:
  - `sprk_Playbook@odata.bind` → `/sprk_analysisplaybooks(id)` (col is NOT `sprk_playbookid`)
  - `sprk_AnalysisId@odata.bind` → `/sprk_analysises(id)`  ← `sprk_analysis` pluralizes to **sprk_analysises**
  - `sprk_OutputTypeId@odata.bind` → `/sprk_aioutputtypes(id)`
  - `sprk_EventType_Ref@odata.bind` → `/sprk_eventtype_refs(id)` (`sprk_eventtypes` does NOT exist)
  - `sprk_Matter/Project/Invoice/Event/ParentDocument@odata.bind` → `/sprk_matters|projects|invoices|events|documents(id)`
- **Entity set name**: use `EntityMetadata.EntitySetName`, NOT logical+"s". Verify via `GET .../EntityDefinitions(LogicalName='sprk_x')?$select=EntitySetName,LogicalCollectionName` or Ctrl+F the `$metadata` doc. Spaarke resolves it at runtime via `GetEntitySetNameAsync` (RetrieveEntityRequest, EntityFilters.Entity) in DataverseServiceClientImpl.cs:852.
- **Find nav prop name**: `$metadata` `<NavigationProperty>` on the EntityType (case-sensitive), or relationship metadata `ReferencingEntityNavigationPropertyName`. Fastest empirical check: GET one existing row and read the `<navprop>@odata.bind`-style keys / `_<col>_value` annotations.
- **Alternate-key upsert**: `PATCH {entityset}(key1='a',key2='b')` — create-if-absent else update; same 204 + OData-EntityId (keys, not GUID) for both. `Prefer: return=representation` distinguishes 201 (created) vs 200 (updated). Do NOT put alt-key values in the body. `If-Match: *` = prevent create (update-only); `If-None-Match: *` = prevent update (create-only).
- **Large string cols**: Multiline/Memo default MaxLength 2000, configurable up to **1,048,576** chars — verify the column's MaxLength before seeding JSON blobs (systemPrompt/inputschema). JSON string escaping only (`\"`, `\\`, `\n`); no Dataverse-specific escaping. If seeding via curl, pass the body from a file (`--data @body.json`) to avoid shell-quoting mangling.

## Sources
- learn.microsoft.com/.../webapi/create-entity-web-api (2026-03-09)
- learn.microsoft.com/.../webapi/update-delete-entities-using-web-api (2026-01-09) — upsert section
- learn.microsoft.com/.../webapi/web-api-service-documents (2026-03-27) — EntitySetName / $metadata
- Spaarke code: `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs` (verified sprk_ nav props), `DataverseServiceClientImpl.cs:852` (GetEntitySetNameAsync)

## Open questions
- The two target catalog tables named by caller (`sprk_analysisaction`, `sprk_playbookconsumer`): their exact EntitySetName + lookup nav props NOT yet metadata-confirmed. `sprk_analysisaction` likely `sprk_analysisactions` but VERIFY via EntityDefinitions given the `sprk_analysis`→`sprk_analysises` precedent.
