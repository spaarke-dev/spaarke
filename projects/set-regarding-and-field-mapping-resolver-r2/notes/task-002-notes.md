# Task 002 — BFF read-layer + rule DTO extension — Notes

**Completed**: 2026-07-09 · Rigor: FULL · Model: sonnet@high (Opus session)

## What was done (additive only)
Surfaced the full field-mapping rule contract so the four client engines can be built without ever reopening the BFF.

- **Models.cs** `FieldMappingRuleEntity`: +`MappingType` (int; Copy0/Default1/Concat2/Template3), +`Expression` (string?).
- **DataverseWebApiService.cs**: added `sprk_mapping_type,sprk_expression` to BOTH rule `$select` sites (GetFieldMappingRulesAsync line ~1759 + the `$expand` ruleSelect line ~2045); read both in `MapToFieldMappingRuleEntity` mirroring the existing `TryGetValue`/`ValueKind != Null` idiom.
- **FieldMappingRuleDto.cs**: +`MappingType` (string, default "Copy"), +`DefaultValue` (string?), +`Expression` (string?), +`IsRequired` (bool), +`CompatibilityMode` (string, default "Strict"). All existing fields (Id/SourceField/TargetField/types/Priority) retained.
- **FieldMappingEndpoints.cs**: `MapRuleEntityToDto` now populates the 5 new fields; added `MapMappingTypeToString` (0→Copy,1→Default,2→Concat,3→Template) + `MapCompatibilityModeToString` (0→Strict,1→Resolve), mirroring the existing `MapFieldTypeToString`/`MapSyncModeToString` style.

## Verification
- `dotnet build src/server/api/Sprk.Bff.Api/ -c Release` → **0 errors**, 18 warnings (all pre-existing, none in touched files).
- Acceptance criteria 1–5 all met.

## Quality gates (Step 9.5)
- **code-review**: CLEAN — 0 Critical / 0 Warning. No AI smells; §6.6 N/A (pure modification of existing files).
- **adr-check**: CLEAN — ADR-001 ✅ (no controllers), ADR-010 ✅ (no new interface/DI), ADR-013 N/A (no AI types).

## §10 BFF Hygiene
- Placement: additive projection to the **existing** GET /api/v1/field-mappings endpoint — no new endpoint/service/DI/interface/package.
- Publish-size: no NuGet added → additive-IL delta ≈0. Empirical compressed-size measurement is task 003's dedicated deliverable (documented deferral, next on critical path).
- No new CRUD→AI dependency.

## Note
The push path (`PushFieldMappingsAsync`) also uses `MapRuleEntityToDto`; the additive fields don't break it. Regression explicitly covered by task 003.

**Unblocks**: 003 (BFF tests + publish-size + push regression), 010 (engine shell — needs the DTO shape).
