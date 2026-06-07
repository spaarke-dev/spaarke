# Task 008 Completion — Add `JsonSchema` Field to `AnalysisTool` DTO + Dataverse Column (D-A-08)

> **Project**: spaarke-ai-platform-unification-r6
> **Phase**: A — Data-driven Foundation
> **Pillar**: 2 — Tool Registry
> **Wave**: Phase A Wave 3 (sequential after 006 ✅, 007 ✅, 009 ✅)
> **Rigor Level**: FULL
> **Completed**: 2026-06-07
> **FR**: FR-08

---

## Summary

Added `JsonSchema` (nullable string) to the `AnalysisTool` DTO + corresponding `sprk_jsonschema` Memo (multi-line text) column to the `sprk_analysistool` Dataverse entity. The field stores the JSON Schema (Draft 2020-12 family) describing each tool's parameter shape for LLM function-calling.

**Critical-path role**: This field feeds the `ToolHandlerToAIFunctionAdapter` (task 010), which wraps an `IToolHandler` as a `Microsoft.Extensions.AI.AIFunction` — the LLM sees the JSON Schema as the function's parameter declaration. Without this field, chat-side tool invocation cannot be generated from data; the chat agent would still need hardcoded tool classes.

---

## Files Modified

| File | Type | Description |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs` | Modified | Added `JsonSchema` nullable string property to `AnalysisTool` record (+33 lines: 1 property + 27-line XML doc capturing FR-08 contract, validation scope, and required-for-chat enforcement responsibility). |
| `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisToolService.cs` | Modified | Added `MapJsonSchema(string?, Guid, ILogger)` internal static method (validates well-formedness via `JsonDocument.Parse`); added `ToolEntity.JsonSchema` field with `[JsonPropertyName("sprk_jsonschema")]`; extended `$select` clause in `ListToolsAsync`; wired both `GetToolAsync` + `ListToolsAsync` to map the new field. Mirrors task 007's `MapAvailableInContexts` pattern verbatim. |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisToolDtoTests.cs` | Modified | Added 11 new unit tests for FR-08 contract enforcement (40 total in file; 40/40 pass). |
| `scripts/Add-AnalysisToolJsonSchema.ps1` | NEW | Idempotent PowerShell deployment script (DryRun + Test-AttributeExists guard) mirroring task 007's `Add-AnalysisToolAvailableInContexts.ps1`. |

---

## Dataverse Deployment Evidence

- **Environment**: Spaarke Dev (`https://spaarkedev1.crm.dynamics.com`) — Wave 3 user approval covers this schema change.
- **Column metadata** (verified via Web API):
  - `LogicalName: sprk_jsonschema`
  - `AttributeType: Memo` (multi-line text)
  - `MaxLength: 100000` (~100 KB) — matches canonical `sprk_systemprompt` convention from `Create-AiPersonaEntity.ps1`
  - `RequiredLevel: None` (nullable)
- **Backward-compat invariant**: 5 sample rows verified `sprk_jsonschema = NULL` post-deploy (Clause Comparison, Document Classifier, Summary Generator, General Analysis, Clause Analyzer). All existing playbook-only tools remain unchanged.
- **Idempotency**: Re-ran the deployment script — produced "sprk_jsonschema already exists, skipping" as expected.

---

## Schema Choice Decision

**Decision**: `MemoAttributeMetadata` with `MaxLength=100000` (~100 KB).

**Rationale**:
1. **Volume**: Production tool JSON Schemas can run hundreds to thousands of characters (e.g., `DocumentSearch` with `query`, `filters`, `pageSize`, `entityScope` parameters). Single-line text (NVARCHAR(4000)) is insufficient.
2. **Convention**: `Create-AiPersonaEntity.ps1` uses `MaxLength=100000` for `sprk_systemprompt` (the canonical Spaarke convention for "system-prompt-shape" multi-line text columns).
3. **Headroom**: The Q9 batch migration (task 012) populates JSON Schemas for 10 chat tools. Largest production schema today is ~3 KB. 100 KB ceiling gives ~33× headroom.

**Not chosen**:
- Single-line text (4 KB) — too small for production tool schemas.
- Memo with 1 MB — overkill; 100 KB matches existing Spaarke conventions and Dataverse field-level UX (the Memo editor handles 100 KB cleanly).

---

## Validation Contract (FR-08 separation-of-concerns)

The mapper validates **well-formedness only** (JSON parses cleanly via `JsonDocument.Parse`). It does NOT validate JSON Schema semantics (required keywords, type correctness).

- **Well-formedness check (this task)**: malformed JSON → log + return null. Never silently passed to LLM.
- **Semantic validation (task 010 — adapter)**: validates that the schema is a usable JSON Schema (e.g., `type: object`, declared `properties`, etc.) before constructing the `AIFunction`. Out of scope for this DTO.
- **Required-for-chat rule (task 011 — chat resolver)**: enforces that tools with `AvailableInContexts ∋ Chat` or `Both` have a non-null `JsonSchema`. NOT enforced at DTO level because playbook-only rows must remain assignable from Dataverse.

This separation keeps the DTO + mapper resilient to playbook-only rows while ensuring nothing malformed reaches the LLM.

---

## Test Coverage

11 new unit tests + 12 pre-existing AvailableInContexts tests = 40/40 pass in `AnalysisToolDtoTests.cs`.

| Test | Coverage |
|---|---|
| `AnalysisTool_DefaultConstruction_JsonSchemaIsNull` | DTO default value (backward-compat) |
| `AnalysisTool_SerializeDeserialize_RoundTripsJsonSchemaPopulated` | Wire round-trip with realistic Draft 2020-12 schema |
| `AnalysisTool_SerializeDeserialize_NullJsonSchemaPreserved` | Null wire value preserved |
| `AnalysisTool_WithExpression_PreservesJsonSchema` | Record `with` semantics |
| `MapJsonSchema_NullRaw_ReturnsNull` | Null Dataverse value handling |
| `MapJsonSchema_EmptyOrWhitespace_ReturnsNull` (×4) | Whitespace-only treated as null |
| `MapJsonSchema_ValidJson_ReturnsRawValue` | Valid object-shape schema passes through |
| `MapJsonSchema_VariousValidJsonShapes_ReturnsRawValue` (×6) | Mapper does not enforce "must be object" (adapter's job) |
| `MapJsonSchema_MalformedJson_ReturnsNullAndDoesNotThrow` (×5) | FR-08 binding: malformed JSON never reaches LLM |
| `MapJsonSchema_LargeValidSchema_ReturnsRawValue` | Schemas approaching 100 KB Dataverse ceiling |

**Full Sprk.Bff.Api.Tests suite**: 6176 pass, 109 skipped, 1 flaky (`R5SummarizeTelemetryTests.BothInvocationPaths_RecordViaSameCounter`). Flaky test verified independent of task 008 via `git stash` + isolated run (passes alone; fails in full-suite parallel execution due to shared telemetry counter state — pre-existing, unrelated).

---

## Build + Publish-Size Evidence

- **Build (`dotnet build -c Release`)**: 0 errors, 16 warnings (same as baseline; all pre-existing in unrelated files).

- **Publish size (uncompressed)**:

  | Snapshot | Bytes | MB |
  |---|---|---|
  | Baseline (pre-task) | 144,515,290 | 137.821 MB |
  | Post-task | 144,519,038 | 137.824 MB |
  | **Delta** | **+3,748** | **+0.004 MB** |

  Far below +1 MB report threshold; far below R6 NFR-02 +5 MB cumulative budget.

---

## Quality Gates

| Gate | Status | Notes |
|---|---|---|
| code-review | ✅ PASS | 0 critical, 0 warnings, 2 forward-looking suggestions (one for task 010 — semantic validation; one for task 012 — extending CRUD Create/Update request DTOs). Quantitative metrics + quality direction + AI smell detection all clean (0 findings across 4 files). |
| adr-check | ✅ PASS | All applicable ADRs compliant. BFF §10 §A 5/5 + F satisfied. |

---

## ADR Compliance

| ADR | Compliance | Evidence |
|---|---|---|
| ADR-010 (DI minimalism) | ✅ | No new DI registrations. Mapper is `internal static` on existing class. No new interfaces. |
| ADR-013 (refined — facade boundary) | ✅ | DTO + mapper entirely in Zone A (`Services/Ai/`). No `PublicContracts/` changes. No CRUD-side AI-internal injections. |
| ADR-015 (data governance) | ✅ | Log statement records `toolId` + schema length only; never schema content. |
| ADR-018 (no new feature flags) | ✅ | None added. |
| ADR-027 (Dataverse conventions) | ✅ | `sprk_` prefix; idempotent unmanaged-solution deployment; matches sibling-script pattern. |
| ADR-029 (BFF publish hygiene) | ✅ | +0.004 MB delta ≪ +5 MB R6 budget. |
| NFR-03 (no new ADRs) | ✅ | None proposed. |
| NFR-04 (zero Agent Framework) | ✅ | No `Microsoft.Agents.*` references. |
| FR-08 (JsonSchema field) | ✅ | Field added; nullable; well-formedness enforced. |

---

## Downstream Unblocking

- **Task 010 (`ToolHandlerToAIFunctionAdapter`)**: now has `JsonSchema` as the LLM-facing parameter declaration. Adapter consumes `AnalysisTool.JsonSchema` directly.
- **Task 011 (chat resolver `ResolveTools()`)**: now has the data needed to enforce the FR-08 "required-for-chat" rule (`AvailableInContexts ∋ Chat`/`Both` AND `JsonSchema != null` → tool exposed to chat).
- **Task 012 (Q9 batch migration of 10 chat tools)**: column exists; migration script can `PATCH` `sprk_jsonschema` for each tool row.

---

## Decisions Log

| Timestamp | Decision | Rationale |
|---|---|---|
| 2026-06-07 | Memo (MaxLength=100000), not single-line text | Production tool schemas can be several KB; matches Spaarke `sprk_systemprompt` canonical convention. |
| 2026-06-07 | DTO nullable + mapper-side well-formedness validation (NOT DTO-side required) | FR-08 binding: required-for-chat is enforced at chat-side resolver (task 011), not DTO; otherwise playbook-only rows can't be assigned from Dataverse. |
| 2026-06-07 | Did NOT extend `CreateToolRequest`/`UpdateToolRequest` | Task 007 also did not extend these for `AvailableInContexts`. Task 012 batch migration will use direct Dataverse writes. Forward-looking suggestion flagged in code-review. |
| 2026-06-07 | Malformed JSON → log + null (not throw) | FR-08: never silently pass garbage to LLM. Logging trail enables task 011 to refuse exposure with diagnostic context; throwing would cascade to playbook orchestration unnecessarily. |

---

## Risks / Forward-Looking

- **Task 010 semantic validation**: the adapter is responsible for JSON Schema semantic validation. If task 010 skips this, malformed-but-well-formed JSON (e.g., `{"foo": "bar"}` — not a valid schema) could reach the LLM. Recommend task 010 implements `IsValidJsonSchema(string)` check + refuses to construct `AIFunction` if invalid.
- **Task 012 CRUD request DTOs**: when chat tools are batch-migrated, the migration script needs to write `sprk_jsonschema` directly to Dataverse (the CRUD request DTOs don't carry the field). Forward-looking only — not blocking.

---

## References

- Project: `projects/spaarke-ai-platform-unification-r6/`
- Sibling task: `tasks/007-add-availableincontexts-enum-to-analysistool.poml` (task 007 — same DTO + Dataverse entity)
- Sibling task: `tasks/009-split-execution-context.poml` (task 009 — same Pillar 2 wave)
- Blocks: `tasks/010-build-toolhandler-aifunction-adapter.poml` (consumes JsonSchema)
- Blocks: `tasks/011-wire-resolve-tools-to-dataverse.poml` (enforces required-for-chat at resolver)
- Blocks: `tasks/012-q9-bigbang-migrate-chat-tools.poml` (populates JsonSchema for 10 migrated tools)
- ADR-013 (refined 2026-05-20): `.claude/adr/ADR-013-ai-architecture.md`
- ADR-027: `.claude/adr/ADR-027-dataverse-solution-management.md`
- ADR-029: `.claude/adr/ADR-029-bff-publish-hygiene.md`
- Canonical pattern: `scripts/Create-AiPersonaEntity.ps1` (Memo with MaxLength=100000 for `sprk_systemprompt`)
- Canonical pattern: `scripts/Add-AnalysisToolAvailableInContexts.ps1` (sibling script structure)
