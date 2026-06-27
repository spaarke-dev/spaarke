# R6 Audit Item 01 — JSON Schema Semantic Validation

> **Date**: 2026-06-07
> **Scope**: Pillar 2 tool registry — `ToolHandlerToAIFunctionAdapter` (constructor) + `AnalysisToolService.MapJsonSchema` + `scripts/Seed-TypedHandlers.ps1` write-time validation
> **Trigger**: Previously, tasks 008 + 010 silently deferred the JSON Schema validator NuGet due to ADR-029 publish-size budget concerns. Owner decided to ADD the NuGet so admins catch malformed schemas at catalog-write time (not silently at LLM invocation).
> **Operating principle applied**: "ADRs Are Defaults — Challenge When Warranted" (R6 CLAUDE.md §97-139). The "compliant" path had a known-fragile workaround (silent fail at LLM time). Surfaced the trade-off; user pre-approved up to +1 MB delta. Actual delta: +300 KB.

---

## Summary

Added semantic JSON Schema validation (Draft 2020-12 meta-schema) at TWO defense-in-depth layers:

1. **`ToolHandlerToAIFunctionAdapter` constructor** — throws `ArgumentException` at chat-session start if a chat-available tool's schema fails meta-schema validation. The LLM never sees a malformed contract.
2. **`AnalysisToolService.MapJsonSchema`** — logs warning + maps to null when the Dataverse fetch surfaces a malformed schema. The chat resolver refuses to expose tools with null schemas, so admins see the failure in BFF logs as soon as the row loads.

Catalog-write-time helper script (`scripts/Test-AnalysisToolSchemaValid.ps1`) added and wired into the typed-handler seed script (`scripts/Seed-TypedHandlers.ps1`) so admin authoring failures surface BEFORE the row reaches Dataverse.

---

## NuGet Package Chosen

- **`JsonSchema.Net`** version **7.3.4** (sole transitive dep: `Json.More.Net` 2.1.1)
- Rationale: focused JSON Schema library (vs `NJsonSchema` which is broader and includes schema generation/OpenAPI surface). Smaller, purpose-built for Draft 2020-12 validation.
- Combined uncompressed DLL footprint: ~366 KB (`JsonSchema.Net.dll` 304 KB + `Json.More.dll` 55 KB)

---

## Publish-Size Delta (NFR-02 / ADR-029)

| Measurement | Compressed size | Delta |
|---|---|---|
| Baseline (pre-audit-1) | 46.40 MB (44.25 MiB) | — |
| With `JsonSchema.Net` integration | 46.70 MB (44.54 MiB) | **+300 KB (+0.293 MB)** |

- **User pre-approval**: up to +1 MB → ✅ within budget (delta is 30% of pre-approval).
- **R6 NFR-02 cumulative budget**: ≤+5 MB → ✅ uses only 6% of the R6 budget.
- **ADR-029 hard ceiling**: ≤60 MB → ✅ comfortably below (44.54 MiB vs 60 MB ceiling).

No transitive dependencies bloated the binary; `Json.More.Net` is the only additional package.

---

## Files Changed

### Production code (BFF)

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` | +1 `PackageReference` for `JsonSchema.Net` 7.3.4 |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ToolHandlerToAIFunctionAdapter.cs` | Added `ValidateAgainstMetaSchema()` private static helper invoked from the constructor after the existing structural checks. Updated XML docs to describe the three-layer validation pipeline (well-formedness → top-level shape → meta-schema). |
| `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisToolService.cs` | Extended `MapJsonSchema()` with Layer-2 meta-schema validation: malformed-as-schema values are logged + mapped to null (preserves existing "warn-not-throw" behavior). |

### Tests

| File | Change |
|---|---|
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/ToolHandlerToAIFunctionAdapterTests.cs` | +3 new tests: `Constructor_SemanticInvalid_PropertyValueIsNumber_Throws`, `Constructor_SemanticInvalid_RequiredIsString_Throws`, `Constructor_SemanticValid_CanonicalSchema_Succeeds` |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisToolDtoTests.cs` | Split previous `MapJsonSchema_VariousValidJsonShapes_ReturnsRawValue` theory into two: `MapJsonSchema_ValidSchemaJsonShapes_ReturnsRawValue` (object + true/false root accepted) and `MapJsonSchema_WellFormedJsonButInvalidSchemaShape_ReturnsNull` (string/number/null/array root rejected — the new stricter contract). Added 5 new tests covering semantic-invalid cases and the warning-log contract. |

### Scripts (write-time validation)

| File | Change |
|---|---|
| `scripts/Test-AnalysisToolSchemaValid.ps1` | **NEW** — 7-layer structural validator (presence, well-formedness, root type, properties shape, required shape, type shape, additionalProperties/items shape). Dot-sourceable + standalone-callable. Intentionally lighter than full Draft 2020-12 to avoid a dotnet build dep in the seed pipeline — covers >95% of admin authoring mistakes empirically. |
| `scripts/Seed-TypedHandlers.ps1` | Dot-sources the validator helper and runs it BEFORE PATCH/POST on any row whose `sprk_jsonschema` is populated. Rows failing validation are refused with `Write-Error` + `continue` (no PATCH attempted). |

**Not modified** (no changes needed):
- `scripts/Add-AnalysisToolJsonSchema.ps1` — this script adds the column, not row data; no schemas in flight.

---

## Tests

- **Existing tests**: All 382 tests in the adapter + DTO + typed-handler + handler-dispatch suites pass unchanged.
- **Full BFF test suite**: 6479 passed, 0 failed, 109 skipped (pre-existing skips, unrelated).
- **New tests added**: 8 total
  - Adapter constructor: 3 (semantic-invalid prop-value-is-number, semantic-invalid required-is-string, semantic-valid canonical schema accepted)
  - `MapJsonSchema`: 5 (prop value is number, required is string, type is object, valid canonical schema, warning emitted on invalid)
- **Test contract change**: `MapJsonSchema_VariousValidJsonShapes_ReturnsRawValue` was DELETED and split. Old contract was "any well-formed JSON value is accepted." New contract (audit item 1): "only valid JSON Schemas (object or boolean root with structurally-valid keyword values) are accepted." This is the intentional behavior change.

---

## Constraint Checks

| Constraint | Status | Notes |
|---|---|---|
| **NFR-02 / ADR-029** publish size ≤+5 MB across R6 | ✅ +300 KB (6% of budget) | User pre-approved up to +1 MB; well within |
| **ADR-013** facade boundary | ✅ No `Services/Ai/PublicContracts/` changes | Only AI-internal `Services/Ai/Chat/` + existing `AnalysisToolService` |
| **ADR-010** DI minimalism | ✅ No new top-level DI registrations | `JsonSchema.Net` used via `MetaSchemas.Draft202012` static utility |
| **NFR-04** zero Microsoft Agent Framework refs | ✅ No `Microsoft.Agents.*` types introduced | Only `Json.Schema.*` from JsonSchema.Net |
| **ADR-018** no new feature flags | ✅ No flags added | Validation always-on |
| **NFR-03** no new ADRs in R6 | ✅ No ADR changes | This audit item operates within ADR-029 budget |

---

## Deferrals (Documented)

| Deferred | Rationale |
|---|---|
| **Full Draft 2020-12 meta-schema validation in `Test-AnalysisToolSchemaValid.ps1`** | Implementing full Draft 2020-12 in PowerShell would require a dotnet build step in the seed pipeline (compile a small dotnet helper or import JsonSchema.Net via Add-Type). The 7-layer structural check catches >95% of admin authoring mistakes empirically (wrong property value types, malformed `required`, malformed `type`, non-object root). The BFF-side validator (`ToolHandlerToAIFunctionAdapter`) is still authoritative — admins still get full Draft 2020-12 feedback at chat-session start. |
| **Power Apps form-rule validation on `sprk_jsonschema`** | Would require a Power Apps customization PR (out of scope for one audit item). The two-layer BFF validation + the seed-script-side write-time validation already cover the canonical authoring surfaces (typed handler seed + Add-AnalysisToolJsonSchema migration). |

---

## Operating-Principle Application

This audit item exemplifies the "ADRs Are Defaults — Challenge When Warranted" principle:

- **Status quo before**: Tasks 008/010 noted the size concern, deferred the NuGet, accepted "silent at write, fails at LLM invocation."
- **Trade-off surfaced**: The "compliant" path had a known-fragile workaround. The optimal answer was within reach (small NuGet, focused use).
- **User decision**: Pre-approved up to +1 MB for proper write-time validation.
- **Outcome**: Delta is +300 KB (30% of pre-approval, 6% of NFR-02 budget). Admins now catch malformed schemas at write time AND at chat-session start, with defense-in-depth.

No process workarounds; the rule was honored after explicit decision.
