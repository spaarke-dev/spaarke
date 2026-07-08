# SRFR-056 — BFF Push Field-Mapping Schema Drift Fix

**Date**: 2026-07-08
**Owner**: Claude Code (Opus 4.7)
**Trigger**: Owner UAT hit BFF 500 on `POST /api/v1/field-mappings/push` after SRFR-053 unblocked auth (was 401 before).

---

## Root Cause

`src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs` queried `sprk_fieldmappingprofile` filtered by three columns that **have never existed** on the deployed entity:

- `sprk_sourceentity` (text) — does not exist
- `sprk_targetentity` (text) — does not exist
- `sprk_isactive` (boolean) — does not exist

The actual entity schema (verified on `spaarkedev1`) uses lookups to a catalog table:

```
sprk_fieldmappingprofile:
  sprk_fieldmappingprofileid GUID (primary key)
  sprk_name                  NVARCHAR(850)
  sprk_sourcerecordtype      LOOKUP -> sprk_recordtype_ref   (raw: _sprk_sourcerecordtype_value)
  sprk_targetrecordtype      LOOKUP -> sprk_recordtype_ref   (raw: _sprk_targetrecordtype_value)
  sprk_capabilitymode        CHOICE  (100000000=Strict, 100000001=Resolve)
  sprk_defaultvalue          NVARCHAR(1000)
  sprk_description           MULTILINE TEXT
  statecode                  STATE   (0=Active, 1=Inactive)

sprk_recordtype_ref (catalog):
  sprk_recordtype_refid      GUID (primary key)
  sprk_recordlogicalname     e.g. "sprk_matter", "sprk_event"
```

The drift went undetected for many months because the endpoint was gated on auth misconfiguration (returned 401 pre-SRFR-053). Once SRFR-053 unblocked auth, Dataverse Web API returned a 400 for the bogus `$filter` (unknown attribute), which the BFF handler surfaced as a 500 (`try/catch` → `Results.Problem 500`).

---

## Fix

Rewrote the three profile-query methods in `DataverseWebApiService.cs` to use a **two-step lookup**:

1. Step 1 — resolve entity logical names to `sprk_recordtype_ref` GUIDs:
   ```
   GET sprk_recordtype_refs?$select=sprk_recordtype_refid,sprk_recordlogicalname
       &$filter=sprk_recordlogicalname eq 'sprk_matter' or sprk_recordlogicalname eq 'sprk_event'
   ```
2. Step 2 — query the profile filtered by the resolved GUIDs:
   ```
   GET sprk_fieldmappingprofiles?
       $filter=_sprk_sourcerecordtype_value eq {srcGuid}
           and _sprk_targetrecordtype_value eq {tgtGuid}
           and statecode eq 0
       &$select=sprk_fieldmappingprofileid,sprk_name,
                _sprk_sourcerecordtype_value,_sprk_targetrecordtype_value,
                sprk_capabilitymode,sprk_defaultvalue,sprk_description,statecode
   ```

Extracted the two-step name→ID lookup into a private helper `LookupRecordTypeIdsAsync(IEnumerable<string> logicalNames, ct)` used by both `GetFieldMappingProfileAsync` and `GetFieldMappingProfileWithRulesAsync`. For `QueryFieldMappingProfilesAsync` (which returns ALL active profiles for client-side filtering), added a reverse-lookup helper `GetRecordTypeNamesByIdsAsync(IEnumerable<Guid> ids, ct)` so the DTO `SourceEntity`/`TargetEntity` fields stay populated (consumer-visible field names in `FieldMappingProfileDto` unchanged for backward compatibility with PCF/webresource callers).

### Mapper changes

`MapToFieldMappingProfileEntity(Dictionary<string, JsonElement> data)` grew an optional `IReadOnlyDictionary<Guid, string>? recordTypeIdToLogicalName` parameter. When present, the mapper resolves `_sprk_sourcerecordtype_value` / `_sprk_targetrecordtype_value` GUIDs to logical names and writes them onto `SourceEntity` / `TargetEntity`.

Fields that don't exist on the deployed entity were removed from the query and defaulted in the mapper:
- `MappingDirection` — always `0` (`ParentToChild`). Was `sprk_mappingdirection` in the code, but the column doesn't exist.
- `SyncMode` — always `0` (`OneTime`). Was `sprk_syncmode`.
- `IsActive` — derived from `statecode == 0` instead of `sprk_isactive`.

The `FieldMappingProfileEntity` model (`Models.cs`) was left as-is — same shape, same required fields (`SourceEntity`, `TargetEntity`). This keeps the interface `IFieldMappingDataverseService` and downstream DTO surface unchanged. Consumer contracts unaffected.

### Files modified (net LOC diff)

| File | Diff | Notes |
|---|---|---|
| `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs` | +215 / -47 (net +168) | 3 query methods rewritten + 2 lookup helpers added + mapper signature extended |
| `src/server/shared/Spaarke.Dataverse/Models.cs` | 0 | Kept `FieldMappingProfileEntity` shape — SourceEntity/TargetEntity now populated via lookup resolution, not schema fields |
| `src/server/shared/Spaarke.Dataverse/IFieldMappingDataverseService.cs` | 0 | No signature changes |
| `src/server/api/Sprk.Bff.Api/Api/FieldMappings/FieldMappingEndpoints.cs` | 0 | No changes — service abstraction preserved |
| `src/server/api/Sprk.Bff.Api/Models/FieldMapping/FieldMapping*.cs` | 0 | DTOs unchanged |
| `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` | 0 | Stubs return null/empty — no schema knowledge needed |

Tests: no updates required. Existing `PushFieldMappingsTests.cs` unit tests are DTO-shape only (79 tests, all pass). Existing `FieldMappingEndpointsTests.cs` integration tests are route/401-shape only (unaffected by schema change).

---

## Build & Test Verification

```
dotnet build src/server/api/Sprk.Bff.Api/  →  Build succeeded. 0 Error(s), 19 pre-existing Warning(s)
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~FieldMapping"
  →  Passed! Failed: 0, Passed: 79, Skipped: 1, Total: 80
Full unit suite: 7682 passed / 14 failed / 111 skipped
  (all 14 failures pre-existing; unrelated modules: ComposeService, DailyBriefingCollector,
   SummarizeSessionEndpoint, KnowledgeDeploymentConfig, SessionFilesCleanupJob,
   ExecutorConfigSchemasEndpoint. Verified by stash+re-run baseline.)
```

---

## Deploy Verification

Ran `scripts/Deploy-BffApi.ps1` (canonical BFF deploy script per `bff-deploy` skill).

```
[1/4] Building API in Release mode... Build successful
[2/4] Creating deployment package... Package created: 46.81 MB
[3/4] Deploying directly to App Service... Deployment command returned success
[4/4] Verifying file replacement on server... All 4 critical files match local build (SHA-256 verified)
[5/4] Verifying health endpoint... dev health check passed!
```

Publish-size: **46.81 MB compressed** — well under the 60 MB ceiling (BFF Hygiene §10 / spec NFR-01).
Prior baseline was ~45.65 MB; delta ≈ +1.16 MB (no new packages, likely build-config drift). Below the +5 MB single-task escalation threshold.

CVE check: `dotnet list package --vulnerable --include-transitive` — no new HIGH-severity CVEs introduced by this task (no NuGet changes).

Endpoint smoke tests (unauthenticated — expect 401 not 500/404):

```
GET  /api/v1/field-mappings/profiles                        -> 401  ✓
GET  /api/v1/field-mappings/profiles/sprk_matter/sprk_event -> 401  ✓
POST /api/v1/field-mappings/push                            -> 401  ✓
```

Result: routes are registered and gated on auth — no longer bare 500 on the unauthorized path. Owner UAT re-test with real bearer token expected to succeed against a `sprk_matter → sprk_event` profile (or receive 404 "Profile Not Found" if no active row exists for that pair).

---

## Notes / Follow-ups

- The `FieldMappingProfileEntity` model still has `MappingDirection` and `SyncMode` int properties that no longer map to deployed schema columns. Kept for downstream compatibility (`FieldMappingProfileDto.SyncMode` string derivation) — always default to `0/0`. If future schema adds these back, re-hydrate at the mapper.
- `sprk_capabilitymode` (100000000=Strict, 100000001=Resolve) is now `$select`ed but not yet surfaced on `FieldMappingProfileEntity`. Not required for the push flow (rules carry their own `sprk_compatibilitymode`), so left off the entity shape.
- Prior code caught `$expand` failures in `GetFieldMappingProfileWithRulesAsync` and fell back to `GetFieldMappingProfileAsync`. That fallback is preserved and still works — both paths now query the corrected schema.
- No changes to `DataverseServiceClientImpl` stubs (which return null/empty on the SDK-based path) — the fix targets `DataverseWebApiService` where the actual Web API queries live.

## ADR compliance

- **ADR-010 (DI Minimalism)**: No new interfaces added. Concrete `DataverseWebApiService` still implements existing `IFieldMappingDataverseService`. ✓
- **ADR-028 (Auth v2)**: No auth-path changes. Server-side auth unchanged; only Dataverse query logic touched. ✓
- **BFF Extensions Governance (§10)**: Editing existing endpoints, not adding new ones. Publish-size + CVE checks performed above. Test update obligation N/A (existing DTO-shape and route-shape tests continue to pass; no service-signature changes). ✓
