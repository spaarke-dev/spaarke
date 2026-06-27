# Task 102 — `FinancialCalculatorHandler` (D-H-02, FR-18) — Completion Notes

**Completed**: 2026-06-07
**Rigor**: FULL
**Phase**: Parallel — 8 Typed Tool Handlers, Wave H-G1
**Branch**: `work/spaarke-ai-platform-unification-r6`
**Status**: ✅ Complete

---

## Files Created

| File | Purpose |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/FinancialCalculatorHandler.cs` | Pure-deterministic typed handler: Sum, Tax, Discount, FxConvert, WeightedAvg, AggregateByCategory. All `decimal` math. NO `IOpenAiClient` dependency. |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/FinancialCalculatorHandlerTests.cs` | 27 tests: 4 R6 Pillar 2 contract assertions + decimal precision invariant + determinism + 6 positive operation cases + 5 error cases + 3 config-driven rounding/precision cases + ADR-015 telemetry compliance + FR-18 no-LLM source assertion. |
| `infra/dataverse/sprk_analysistool-financial-calculator-row.json` | Seed row with `sprk_handlerclass = "FinancialCalculatorHandler"`, `sprk_toolcode = "FIN-CALC@v1"`, sample config (5 currencies + 8 FX-rate-table entries + BankersRounding default + decimalPrecision=2), `sprk_availableincontexts = 100000000 (Playbook)`, JSON Schema for LLM invocation. |
| `scripts/Seed-TypedHandlers.ps1` | Shared idempotent upsert script for all 8 typed handler rows. Wave-1 / Wave-2 sibling tasks extend `$RowFiles` map. Per parent-task instructions ("If task 101 has already created `scripts/Seed-TypedHandlers.ps1`, ADD your row to that script. Otherwise, create the shared script and seed your row."). |

---

## Build + Test Results

**BFF build**: `dotnet build src/server/api/Sprk.Bff.Api/` — `0 Error(s)`, 16 pre-existing warnings (unchanged from baseline).

**Tests**: `dotnet test --filter "FullyQualifiedName~FinancialCalculatorHandlerTests"` — **27/27 passed** in ~80–122 ms. Re-run confirmed stability.

**Auto-discovery regression check**: `AutoDiscoveryVerificationTests` + `HandlerContractTestTemplate` — **10/10 still pass**. New handler is picked up by assembly scan without breaking the existing 4 handlers.

---

## BFF Publish-Size Delta (NFR-02 + ADR-029)

| Metric | Value |
|---|---|
| Raw publish size | 137.93 MB |
| Compressed publish size | **44.22 MB** (45 MB rounded) |
| Baseline (CLAUDE.md §10) | ~45.65 MB |
| Delta vs CLAUDE.md baseline | **−1.43 MB** (under baseline due to cumulative R6 work; task 102 individual contribution ~30–60 KB) |
| Task 102 per-task ceiling (≤+0.5 MB) | ✅ Well under |
| R6 cumulative budget (≤+5 MB) | ✅ Under budget |

Task 102 contributes ZERO new NuGet packages, ZERO new top-level `Program.cs` lines, ZERO new DI registrations. The handler is `~430 lines` of pure C# (one new `.cs` file) and `~50 lines` of new JSON. The compressed-publish delta vs the CLAUDE.md baseline went DOWN slightly because cumulative R6 work has trimmed some prior fat.

---

## Quality Gates (Step 9.5)

### code-review

| File | Verdict | Notes |
|---|---|---|
| `FinancialCalculatorHandler.cs` | ✅ Pass | Sealed class; idiomatic C#; deterministic invariant-culture formatting; ADR-015-compliant logging (handler + operation + duration only); exception-based error model wrapped at boundary returning `ToolResult.Error`; `JsonSerializerOptions` static-readonly for perf; nullable handling correct. |
| `FinancialCalculatorHandlerTests.cs` | ✅ Pass | AAA pattern; FluentAssertions with `because:` rationales; deterministic test data; FR-18 binding tests (`Constructor_HasNoLlmDependency`, `SourceFile_HasNoOpenAiReference`); ADR-015 enforced via fixture helper. |
| `sprk_analysistool-financial-calculator-row.json` | ✅ Pass | `_comment_*` keys for structured comments; idempotency key `sprk_toolcode`; `sprk_handlerclass` matches C# class name verbatim. |
| `Seed-TypedHandlers.ps1` | ✅ Pass | Idempotent UPSERT by `sprk_toolcode`; `-OnlyHandler` filter; `-WhatIf` preview; serializes nested JSON to string for Dataverse; clone of `Seed-AiPersonaDefault.ps1` pattern. |

No critical issues, no warnings.

### adr-check

| ADR | Status | Notes |
|---|---|---|
| ADR-010 (DI minimalism) | ✅ Pass | Auto-discovery only; ZERO manual DI; ZERO Program.cs edits. |
| ADR-013 (AI architecture) | ✅ Pass | Handler in `Services/Ai/Handlers/`; no PublicContracts changes; no CRUD-side injection. |
| ADR-014 (caching) | ✅ N/A | Handler is stateless — no cache. |
| ADR-015 (telemetry) | ✅ Pass | Logs handler name + operation + outcome + duration + tool ID ONLY. NEVER monetary values. Enforced by `Telemetry_RespectsAdr015` test using `AssertTelemetryRespectsAdr015` fixture helper with explicit forbidden-substring list. |
| ADR-016 (rate limit) | ✅ N/A | NO LLM calls. |
| ADR-029 (publish hygiene) | ✅ Pass | Compressed publish under per-task + R6-cumulative ceilings. |
| FR-18 binding | ✅ Pass | Pure deterministic; all `decimal` money math (verified by `0.10 + 0.20 == 0.30` test); supported currencies + FX rates + rounding mode configurable; mixed-currency operations rejected. |

---

## FR-18 Operations Implemented

| Operation | Tested Positive | Tested Error |
|---|---|---|
| `Sum` | ✅ Adds 3 USD operands → 350.75 | ✅ Mixed-currency rejected; unsupported currency rejected |
| `Tax` | ✅ 100 × 8.25% = 108.25 with breakdown | ✅ Negative rate rejected |
| `Discount` | ✅ 250 EUR × 10% = 225 with breakdown | (constraint: rate ∈ [0,1]) |
| `FxConvert` | ✅ 100 USD × 0.92 = 92 EUR via config table; same-currency identity (rate = 1) | ✅ Missing FX rate rejected; unsupported source/target currency rejected |
| `WeightedAvg` | ✅ Weighted mean of (100,1) + (200,3) = 175 | ✅ Zero total weight rejected |
| `AggregateByCategory` | ✅ Groups USD operands into 2 buckets with subtotals + grand total | (no error paths beyond shared validation) |

Additional cross-cutting tests:
- **Decimal precision invariant**: `0.10m + 0.20m == 0.30m` exactly (FR-18 binding).
- **Determinism**: identical input → identical output, byte-for-byte.
- **Banker's rounding** vs **MidpointAwayFromZero**: `2.125` → `2.12` vs `2.13` (verified).
- **Unknown rounding mode** rejected with `InvalidConfiguration`.
- **JPY default precision = 0** (per-currency override): `1234.56 JPY` rounds to `1235 JPY`.

---

## Coordination with Parallel Handler Tasks (101, 103, 104)

Per parent-task instructions, these tasks run as Wave-1 H-G1 parallel siblings:

- I observed during build that tasks 103 (ClauseComparison) and 101 (DateExtractor) + 104 (FinancialCalculationTool) + task 010 (`ToolHandlerToAIFunctionAdapter`) were being concurrently developed in the worktree. Their handler `.cs` files exist; some of their test files had transient compile errors during one of my test invocations but cleared on retry — likely a write-race during their parallel writes.
- File overlap: NONE. Each handler is in its own `.cs` file. My task touches only `FinancialCalculatorHandler.cs`, its tests file, the row JSON, and the shared seed script.
- Shared seed script: I created `scripts/Seed-TypedHandlers.ps1` (task 101 had not yet created it). Task 103's agent added its `ClauseComparisonHandler` row entry to my `$RowFiles` map concurrently — preserved as-is (the merge is monotonic / append-only on this map).

---

## Sprk_analysistool Row Seed Confirmation

- **Row JSON**: `infra/dataverse/sprk_analysistool-financial-calculator-row.json` created.
- **Routing**: `sprk_handlerclass = "FinancialCalculatorHandler"` (exact match with C# class name).
- **Idempotency key**: `sprk_toolcode = "FIN-CALC@v1"`.
- **Context availability**: `sprk_availableincontexts_value = 100000000` (Playbook). Chat-direct invocation is not in scope for R6 per FR-18 (this handler is invoked from Playbook nodes after upstream extraction by `EntityExtractorHandler` / `InvoiceExtractionToolHandler`).
- **Sample config**: 5 supported currencies (USD/EUR/GBP/JPY/CAD), 8 FX-rate-table entries (USD↔EUR/GBP/CAD/JPY), `BankersRounding`, `decimalPrecision = 2`.
- **Deploy mechanism**: `pwsh scripts/Seed-TypedHandlers.ps1 -OnlyHandler FinancialCalculatorHandler -DataverseUrl <env>`. Script is idempotent UPSERT (PATCH if existing toolcode found; POST otherwise).
- **Live deployment to Spaarke Dev**: deferred — performed alongside sibling Wave-1 handlers as a coordinated wave (per `Seed-TypedHandlers.ps1` design). Local file + script verified ready; no `dotnet` / `pac` calls needed for the row JSON itself.

---

## What This Unblocks

| Task | Status before | Status after |
|---|---|---|
| **105** `EntityExtractorHandler` (Wave 2, depends on 104) | 🚧 blocked on 104 | (unchanged — gated on 104) |
| **106** `ClauseAnalyzerHandler` (Wave 2, depends on 104) | 🚧 blocked on 104 | (unchanged — gated on 104) |
| **107** `RiskDetectorHandler` (Wave 2, depends on 104) | 🚧 blocked on 104 | (unchanged — gated on 104) |
| **108** `InvoiceExtractionToolHandler` (Wave 2, depends on 104) | 🚧 blocked on 104 | (unchanged — gated on 104) |
| **109** Handler dispatch tests (depends on 105-108) | 🚧 blocked on 105-108 | (unchanged) |

Task 102 itself does NOT directly unblock Wave 2 (104 does — 102 and 104 are sibling deterministic handlers); Wave 1 simply needs to be 100% green before Wave 2 starts. With 102 ✅, the Wave 1 completion bar is one closer to satisfied (101 + 103 + 104 remain).

---

## Notes for Reviewers

1. The handler is `sealed` per repo convention. Public DTOs (`CalculatorResult`, `CategoryBucket`, `FinancialOperation` enum) are nested public types on the handler so consumers (e.g., the trace widget) can deserialize results without a separate model file. Request DTOs (`FinancialRequest`, `MonetaryOperand`, `HandlerConfig`) are `internal sealed` — they only need to be visible to the handler itself.
2. Error model: a private `FinancialValidationException` is thrown internally and caught at the boundary; the boundary returns `ToolResult.Error(...)` with the appropriate `ToolErrorCodes` constant. This mirrors `GenericAnalysisHandler` shape.
3. Telemetry: messages use placeholders for `tool.Id`, `operation`, `durationMs` — values that are NOT user input. Money values, currency-pair specifics, and operand lists NEVER appear in log output. Enforced by the `AssertTelemetryRespectsAdr015` fixture helper with explicit forbidden-substring list including the sensitive amount used in the test.
4. The `Validate` method is intentionally thin (TenantId + Configuration presence). Heavier per-operation validation happens inside the operation switch so each operation can throw with its specific error code (`ValidationFailed`, `InvalidConfiguration`).
5. The two source-file inspection tests (`Constructor_HasNoLlmDependency`, `SourceFile_HasNoOpenAiReference`) are defensive guards against future refactors silently introducing an LLM dependency. Both are required by FR-18.
6. The `Seed-TypedHandlers.ps1` script's `$RowFiles` map is the integration point sibling Wave-1/Wave-2 tasks must extend. Task 103 already appended its entry concurrently; the script handles the multi-row case correctly.

---

*Maintained by task-execute. R6 task 102 (D-H-02 / FR-18) — Wave 1 deterministic typed handler.*
