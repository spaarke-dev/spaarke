# Task 101 — D-H-01 `DateExtractorHandler` (FR-17, Wave 1 Pure Deterministic) — Completion Notes

**Completed**: 2026-06-07
**Rigor**: FULL
**Phase**: Parallel — 8 Typed Tool Handlers, Wave H-G1 (Wave 1 of handler workstream)
**Branch**: `work/spaarke-ai-platform-unification-r6`
**Status**: ✅ Complete; downstream tasks 105/106/107/108/109 unblocked (this contributes to Wave 1 completion)

---

## Files Touched

### New files

| File | Purpose | LOC |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/DateExtractorHandler.cs` | Pure-deterministic date extraction + normalization handler (FR-17). Implements `IToolHandler` with `Both` invocation contexts. Zero LLM/Azure OpenAI dependencies. | ~580 |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/DateExtractorHandlerTests.cs` | 35 unit tests: 4 contract + 1 Both-context + 1 no-LLM-ctor + 6 Validate + 3 ValidateChat + 10 ExecuteAsync positive + 2 config-driven + 3 error + 2 telemetry + 1 chat + 1 zero-LLM + 1 determinism + 1 results-empty-when-no-dates. | ~480 |
| `infra/dataverse/sprk_analysistool-date-extractor-row.json` | Dataverse `sprk_analysistool` seed row payload (sprk_handlerclass=DateExtractorHandler, sprk_availableincontexts=100000002/Both, sprk_toolcode=DATEXTR@v1). | ~35 |

### Modified files

| File | Change |
|---|---|
| `scripts/Seed-TypedHandlers.ps1` | Added `"DateExtractorHandler" = "$RepoRoot/infra/dataverse/sprk_analysistool-date-extractor-row.json"` entry to `$RowFiles` map (1-line additive). |
| `projects/spaarke-ai-platform-unification-r6/tasks/101-dateextractor-handler.poml` | Status `not-started` → `completed` + `<completed>` + `<completion-notes>`. |
| `projects/spaarke-ai-platform-unification-r6/tasks/TASK-INDEX.md` | Row 101 🔲 → ✅. |

### Bookkeeping

- This file (`projects/spaarke-ai-platform-unification-r6/notes/task-101-completion.md`)

**Did NOT touch** `projects/spaarke-ai-platform-unification-r6/current-task.md` (per parallel-wave pattern instructed by task brief).

---

## Decision on Shared `Seed-TypedHandlers.ps1` Script

The script **already existed** when task 101 started — it was created by a parallel sibling task (102 or 103) earlier in Wave 1. The exemplar pattern from the task brief ("first handler to land creates the script, subsequent tasks add their rows") worked exactly as designed: my contribution is a single-line entry in `$RowFiles` map + a new JSON row file in `infra/dataverse/`. No coordination overhead.

**The script is shared infrastructure — owned by Wave 1 collectively, not by any one task.**

---

## Handler Design Highlights

### Pure-deterministic regex + DateTime arithmetic (NO LLM dep)

The handler scans input text for five date pattern families using compiled regex + `DateTime` arithmetic:

| Pattern | Example | Confidence | Kind |
|---|---|---|---|
| ISO 8601 (`YYYY-MM-DD`) | `2026-03-15` | 1.0 | absolute |
| Quarter notation | `Q4 2026` → `2026-10-01` | 0.95 | range |
| Named month (explicit year) | `March 15, 2026` | 0.95 | absolute |
| Named month (year inferred) | `March 15` → `{refYear}-03-15` | 0.75 | absolute |
| Short numeric (locale-aware) | `01/02/2026` US→Jan 2, EU→Feb 1 | 0.7–0.9 | absolute |
| Relative quarter | `next quarter` / `last quarter` / `this quarter` | 0.85 | relative |
| Relative day | `today` / `tomorrow` / `yesterday` | 0.95 | relative |
| Relative duration | `in 30 days` / `in 3 weeks` / `in 6 months` / `in 2 years` | 0.9 | relative |
| Relative period | `next week` / `next month` / `next year` | 0.85 | relative |

Output is a deterministic list ordered by `StartIndex` (and `OriginalText` as tiebreaker), guaranteeing identical-input + identical-config → identical-output across runs. Verified by `ExecuteAsync_ProducesIdenticalOutputForIdenticalInput` test.

### Configuration JSON Schema (Draft 07)

Stored in `sprk_analysistool.sprk_configuration`:

```json
{
  "referenceDate": "2026-01-01T00:00:00Z",   // optional — defaults to UtcNow
  "locale": "US",                              // "US" (MM/DD/YYYY) or "EU" (DD/MM/YYYY)
  "confidenceThreshold": 0.0                   // 0.0–1.0, default 0.0
}
```

`Validate()` rejects malformed referenceDate, unsupported locale, and out-of-range confidenceThreshold up front (fail-fast).

### Chat-side argument shape

`sprk_analysistool.sprk_jsonschema` declares the LLM-facing argument shape:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "required": ["text"],
  "properties": { "text": { "type": "string", "minLength": 1 } },
  "additionalProperties": false
}
```

`ExecuteChatAsync` parses `ChatInvocationContext.ToolArgumentsJson` (already adapter-validated against the schema in task D-A-10) and extracts the `text` field, then calls the same `ExtractDates` deterministic pipeline as the playbook path.

### ADR-015 telemetry surface

Only handler name + IDs + outcome counts + duration go to logs. The handler does NOT log:
- Input text (the document body / chat argument)
- Extracted text spans (only count)
- Normalized output dates (results are returned via `ToolResult`, not via telemetry)

Two dedicated tests (`Telemetry_RespectsAdr015_ForExecuteAsync` + `Telemetry_RespectsAdr015_ForExecuteChatAsync`) pass secret-bearing input strings through and assert via `AssertTelemetryRespectsAdr015` that no fragment leaks.

---

## Build + Test Output

**BFF build**:
```
Build succeeded.
    16 Warning(s)   (all pre-existing — same warning set as pre-task baseline)
    0 Error(s)
```

**New unit tests**:
```
Passed!  - Failed:     0, Passed:    35, Skipped:     0, Total:    35, Duration: 98 ms
  - Sprk.Bff.Api.Tests.Services.Ai.Handlers.DateExtractorHandlerTests (35 tests)
```

Per-test latency averages ~3 ms — consistent with pure-deterministic regex + DateTime arithmetic; no LLM call paths exercised.

**Auto-discovery contract verified**: `HandlerType_IsRegisteredInDi` test passes — the assembly scan picks up `DateExtractorHandler` without any manual DI line outside `AnalysisServicesModule`.

---

## BFF Publish-Size Delta (NFR-02 + ADR-029)

| Metric | Pre-task baseline | Post-task | Delta |
|---|---|---|---|
| Raw publish size | 137.85 MB | 137.93 MB | **+0.08 MB** (~82 KB — handler + tests + metadata) |
| Compressed publish size | **44.20 MB** | **44.22 MB** | **+0.02 MB** (~25 KB) |
| Target per-task | ≤+0.5 MB compressed | | ✅ Well within budget |
| Cumulative R6 vs CLAUDE.md baseline (45.65 MB) | — | 44.22 MB | -1.43 MB (R6 BFF currently SMALLER than master baseline) |

**Analysis**: One new `.cs` file (~25 KB on disk) + one config-record DTO + JSON Schema constants. No new NuGet packages. No new DI registrations. The +0.02 MB compressed delta is roughly the assembly metadata overhead from the new public types (`DateExtractionResult` + `ExtractedDate` DTOs). The "BFF is smaller than master baseline" finding is consistent — R6 has not added significant binary weight at this point in the project.

---

## Dataverse Row Deployment Evidence

### Spaarke Dev (https://spaarkedev1.crm.dynamics.com)

```
$ pwsh -File scripts/Seed-TypedHandlers.ps1 -OnlyHandler DateExtractorHandler

Seeding R6 Pillar 2 typed handler sprk_analysistool rows
  Environment : https://spaarkedev1.crm.dynamics.com
  Rows        : DateExtractorHandler
  Preview     : False

--- DateExtractorHandler (DATEXTR@v1) ---
  No existing row - POSTing new sprk_analysistool
  Created with sprk_analysistoolid = b76a58b2-b862-f111-ab0c-000d3a4d8152

Done.
```

Idempotency re-run (PATCHes the existing row):

```
--- DateExtractorHandler (DATEXTR@v1) ---
  Existing row found (sprk_analysistoolid = b76a58b2-b862-f111-ab0c-000d3a4d8152) - PATCHing
  Patched.
```

### Web API verification query

```
GET sprk_analysistools?$filter=sprk_handlerclass eq 'DateExtractorHandler'
    &$select=sprk_name,sprk_handlerclass,sprk_toolcode,sprk_availableincontexts

→ [
    {
      "sprk_analysistoolid": "b76a58b2-b862-f111-ab0c-000d3a4d8152",
      "sprk_name": "SYS-Date Extractor",
      "sprk_handlerclass": "DateExtractorHandler",
      "sprk_toolcode": "DATEXTR@v1",
      "sprk_availableincontexts": 100000002    ← Both (Playbook + Chat)
    },
    {
      "sprk_analysistoolid": "0e956329-d310-f111-8342-7ced8d1dc988",
      "sprk_name": "Date Extractor",           ← pre-R6 row (kept; FR-07 backward-compat)
      "sprk_handlerclass": "DateExtractorHandler",
      "sprk_toolcode": "TL-003",
      "sprk_availableincontexts": null         ← null → Playbook fallback per FR-07
    }
  ]
```

The pre-R6 `TL-003` row is preserved — task 101 added a new SYS- row (`b76a58b2...`) alongside it. Migration / consolidation of the legacy row is out of scope for this task (would be a separate data-cleanup task if desired).

### `sprk_toolcode` field length constraint discovered

The `sprk_toolcode` Dataverse column has a **10-character maximum**. The initial value `DATE-EXTRACT@v1` (15 chars) was rejected; I shortened to `DATEXTR@v1` (10 chars). **Sibling tasks (102, 103, 104) have the same constraint** — their JSON row files currently contain over-length toolcodes (e.g., `FIN-CALC@v1` = 11 chars, `CLAUSE-CMP@v1` = 13 chars, `FIN-CALC-FORMULA@v1` = 19 chars). They will need to shorten when deploying. This is captured as a Known Finding below — no fix scope for task 101.

---

## Acceptance Criteria — Evidence

| Criterion | Evidence |
|---|---|
| `DateExtractorHandler.cs` exists in `Services/Ai/Handlers/`, implements `IToolHandler`, ZERO references to `IOpenAiClient` / Azure OpenAI types | ✅ `Constructor_DoesNotRequireOpenAiClient` test enforces mechanically; grep confirms zero `IOpenAiClient` usages |
| Handler auto-discovered (no manual DI line) | ✅ `HandlerType_IsRegisteredInDi` test passes — assembly scan finds it |
| `sprk_analysistool` row deployed to Spaarke Dev | ✅ Created sprk_analysistoolid=b76a58b2-b862-f111-ab0c-000d3a4d8152; queryable via Web API (output above) |
| Unit tests pass (contract + positive + error + config-driven + telemetry) | ✅ 35/35 pass in 98 ms |
| Deterministic property verified | ✅ `ExecuteAsync_ProducesIdenticalOutputForIdenticalInput` test verifies field-by-field equality across two independent handler instances |
| `dotnet build src/server/api/Sprk.Bff.Api/` succeeds | ✅ 0 errors; warning count unchanged from baseline |
| BFF publish-size delta ≤+0.5 MB | ✅ +0.02 MB compressed; far under budget |
| ZERO new top-level Program.cs lines | ✅ Confirmed — no Program.cs edits |
| `code-review` + `adr-check` quality gates both pass | ✅ See Quality Gates section below |

---

## Quality Gates (Step 9.5)

### code-review

**Files reviewed**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/DateExtractorHandler.cs` (✅ idiomatic C#; sealed; XML docs cite FR-17/ADRs; nullable handling correct; `ToolResult.Ok/Error` factories used; deterministic-ordering invariant documented + tested; OperationCanceledException → Cancelled, other exceptions → InternalError exactly like GenericAnalysisHandler)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/DateExtractorHandlerTests.cs` (✅ proper xUnit conventions; inherits from `TypedToolHandlerTestFixture`; FluentAssertions with `because:` rationale; 35 tests; <100 ms total)
- `infra/dataverse/sprk_analysistool-date-extractor-row.json` (✅ matches sibling-row schema; comments explain routing + invocation contract)
- `scripts/Seed-TypedHandlers.ps1` (✅ single-line additive change to `$RowFiles` map; alphabetical-ish ordering preserved)

**No critical issues. No warnings. No lint errors.**

### adr-check

| ADR | Status | Notes |
|---|---|---|
| **ADR-010 (DI minimalism)** | ✅ Pass | Auto-discovered; no Program.cs edits; no manual DI registration |
| **ADR-013 (AI architecture)** | ✅ Pass | Lives in `Services/Ai/Handlers/`; no PublicContracts facade modifications |
| **ADR-014 (AI caching)** | ✅ N/A | Pure-deterministic — no cache layer; per-tenant invariant preserved via TenantId validation |
| **ADR-015 (AI data governance)** | ✅ Pass | Telemetry tests assert no input/extracted-text leak; handler emits handler name + IDs + counts only |
| **ADR-016 (rate limit)** | ✅ N/A | No LLM call to rate-limit |
| **ADR-029 (BFF publish hygiene)** | ✅ Pass | +0.02 MB compressed delta; budget is +0.5 MB per task |

**No ADR violations.**

---

## Known Findings / Follow-ups

### 1. `sprk_toolcode` 10-char Dataverse field length constraint

The `sprk_toolcode` column on `sprk_analysistool` has `MaxLength=10`. Wave 1 sibling row JSON files currently contain over-length toolcodes:
- `sprk_analysistool-financial-calculator-row.json` → `FIN-CALC@v1` (11 chars; over by 1)
- `sprk_analysistool-clause-comparison-row.json` → `CLAUSE-CMP@v1` (13 chars; over by 3)
- `sprk_analysistool-financial-calculation-row.json` → `FIN-CALC-FORMULA@v1` (19 chars; over by 9)

When tasks 102/103/104 attempt to deploy via the shared script they will hit the same `0x80044331` validation error. **Recommendation**: either (a) each sibling task shortens its toolcode in its own row JSON, or (b) someone with authority widens the `sprk_toolcode` column via a separate schema-change task. Not scoped for task 101.

### 2. Two `DateExtractorHandler` rows exist in Spaarke Dev

After deployment, two `sprk_analysistool` rows point at `DateExtractorHandler`:
- **NEW**: `b76a58b2-b862-f111-ab0c-000d3a4d8152` — SYS-Date Extractor, DATEXTR@v1, AvailableInContexts=Both
- **PRE-R6**: `0e956329-d310-f111-8342-7ced8d1dc988` — "Date Extractor", TL-003, AvailableInContexts=null (Playbook fallback per FR-07)

The runtime resolver loads tools by toolId (not by handlerclass), so the duplication doesn't cause routing ambiguity at the handler layer. If a single canonical row is desired, a future data-cleanup task can deprecate or merge TL-003. Out of scope for task 101.

### 3. Pre-existing `FinancialCalculatorHandlerTests` failure

When running the broader handler test suite, `FinancialCalculatorHandlerTests.SourceFile_HasNoOpenAiReference` fails. This is owned by task 102 (sibling parallel-wave task) — NOT task 101. Confirmed by running `--filter "FullyQualifiedName~DateExtractorHandler"` alone: 35/35 pass cleanly.

---

## What This Unblocks

| Task | Effect |
|---|---|
| **104** Final Wave 1 handler | Sibling — independent; no direct dependency |
| **105–108** Wave 2 LLM-assisted handlers | Wave 1 must fully complete (104) before Wave 2 starts; task 101 contributes to that gate |
| **109** Handler dispatch tests (playbook + chat) | Depends on all 4 Wave 1 + all 4 Wave 2 handlers + the dispatcher; task 101 contributes the Both-context handler that exercises the `ExecuteChatAsync` path in task 109's chat-dispatch test |

---

## Summary

DateExtractorHandler is complete, deployed, and verified end-to-end. The implementation honors FR-17 (pure-deterministic — zero LLM dep), implements `IToolHandler` with `Both` invocation contexts (FR-17 acceptance), is auto-discovered (ADR-010), respects ADR-015 telemetry surface (no input-text leakage), and adds +0.02 MB compressed to the BFF publish (well under the +0.5 MB per-task budget per ADR-029). 35 unit tests pass in 98 ms — deterministic and fast.

---

*Maintained by task-execute. R6 task 101 (D-H-01) — Wave 1 contribution to the 8 typed tool handler workstream.*
