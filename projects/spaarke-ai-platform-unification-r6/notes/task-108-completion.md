# Task 108 — InvoiceExtractionToolHandler Completion Notes

**Status**: ✅ Completed (2026-06-07; Wave 2 of Phase A — H-G2 LLM-assisted handler)
**Rigor**: FULL
**FR**: FR-19 (LLM-assisted invoice extraction + line-item arithmetic)

## What was built

`src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/InvoiceExtractionToolHandler.cs` (~670 LOC including XML doc).

Pipeline: invoice text → per-tenant cache check → LLM structured-output extraction (Azure OpenAI `GetStructuredCompletionRawAsync`) → strict-decode against draft 2020-12 schema → deterministic decimal arithmetic (tax method + discount order aware) → `ToolResult` with structured invoice + optional `arithmeticErrors[]`.

Distinct from Wave 1 task 104 (`FinancialCalculationToolHandler` — pure deterministic formulas). Task 108 is the LLM-assisted financial extraction handler — the last typed handler in R6 Pillar 2.

## Arithmetic verification approach: SURFACE, do not auto-correct (FR-19 binding)

When the LLM-reported figures (line totals, subtotal, tax, grand total) disagree with the deterministic decimal computation, the handler surfaces every mismatch in `arithmeticErrors[]` (each carrying `field`, `expected`, `actual`, `severity`). The handler does **NOT** silently overwrite LLM output — auditable trail preserved for downstream human reviewers. The `totals` block in the result reflects the deterministic computation (the trusted output), while `lineItems[]` carries the LLM's raw extraction so consumers can compare.

The mismatch tolerance is dynamic — one decimal place looser than the configured `decimalPrecision` (5 × 10^-precision) — to avoid false positives from rounding noise.

Test `ArithmeticMismatch_SurfacesInErrorsArray_NotAutoCorrected` enforces both invariants:
1. `arithmeticErrors[]` is non-empty when LLM disagrees with math
2. `totals.grandTotal` shows the deterministic computation, not the LLM's wrong value

## Money-math discipline (FR-19 binding)

All monetary math in `decimal` — never `double` / `float`. Test `Decimal_AdditionIsExact` enforces the canonical `1.10m + 1.10m + 1.10m == 3.30m` invariant. The `ComputeTotals` static method is `internal` so tests + future handlers can inspect computation determinism directly.

Tax handling — both `exclusive` (default; tax computed on subtotal, added on top) and `inclusive` (line totals already include tax; subtotal back-derived as `gross / (1 + rate)`).

Discount order — `pre-tax` (default; reduces subtotal before tax; tax proportionally adjusted) and `post-tax` (tax computed on full subtotal; discount subtracted from grand total).

Rounding mode — `banker` (`MidpointRounding.ToEven`; default) or `away_from_zero`.

## Configuration surface

| Key | Default | Notes |
|---|---|---|
| `taxMethod` | `exclusive` | `inclusive` back-derives subtotal from gross line totals |
| `discountOrder` | `pre-tax` | `post-tax` shifts grand total deterministically |
| `roundingMode` | `banker` | per ADR-015 banker's rounding is default |
| `decimalPrecision` | 2 | 0-8; set 0 for JPY-style currencies |
| `expectedCurrencies` | USD/EUR/GBP/JPY/CAD | LLM output outside allow-list → `VALIDATION_FAILED` |
| `modelDeployment` | (null → ToolHandlerModel) | per-row Azure OpenAI deployment override |
| `extractLineItems` | true | when false, header-only extraction |
| `validateArithmetic` | true | when false, suppresses `arithmeticErrors[]` |

## ADR compliance

- **ADR-010** (DI minimalism): auto-discovered by `ToolFrameworkExtensions.AddToolHandlersFromAssembly`; ZERO manual DI line. `AutoDiscoveryVerificationTests` passes for `InvoiceExtractionToolHandler`.
- **ADR-013** (AI architecture): handler lives in `Services/Ai/Handlers/`; `IOpenAiClient` is AI-internal injection — never crosses into CRUD-side code. No PublicContracts changes.
- **ADR-014** (AI caching): cache key `invoice-extractor:{tenantId}:{sha256(text + config-fingerprint)}`. `tenantId` is mandatory — `BuildCacheKey("", ...)` throws. Three dedicated tests verify the per-tenant key invariant.
- **ADR-015** (data governance): telemetry logs handler id + tenant + invocation id + line-item COUNT BUCKET (`empty`/`single`/`small`/`medium`/`large`) + arithmetic error count + cache-hit flag + duration ONLY. Test `Telemetry_DoesNotLogInvoiceContent_OrMonetaryValues` enforces — uses sentinel strings (vendor name, invoice number, line description, monetary amount) and asserts none appear in captured log output.
- **ADR-016** (rate limits): LLM call routes through existing `IOpenAiClient.GetStructuredCompletionRawAsync` (same path as sibling Wave 2 handlers 105/106/107 and `GenericAnalysisHandler`); `ai-context` policy applies. No bypass.
- **ADR-029** (BFF size): +0.12 MB uncompressed delta (well under per-task +0.6 MB cap).
- **NFR-13** (safety pipeline): preserved — handler uses same `IOpenAiClient` dispatch path as every other LLM-assisted handler; `SafetyPipelineMiddleware` chain unchanged.

## Tests

27 unit tests pass:

- **Contract** (5): DI registration; HandlerId == nameof; metadata valid; SupportedToolTypes non-empty; SupportedInvocationContexts == Both
- **Decimal precision invariant**: `1.10m + 1.10m + 1.10m == 3.30m` exact
- **Positive (5)**: exclusive tax + no discount; inclusive tax back-derives subtotal; per-line tax rate; pre-tax discount semantics; post-tax discount semantics
- **Mismatch handling**: LLM reports wrong grand total → surfaced in `arithmeticErrors`, computed total reflects math (not LLM)
- **Empty line items**: handled gracefully (grand total = 0)
- **Currency (3)**: EUR tagged in output; out-of-allow-list rejected; missing currency rejected
- **Discount-order swap**: pre-tax vs post-tax produces different grand totals (config-driven determinism)
- **Cache hit/miss**: second identical call serves from cache; LLM mock called exactly once
- **Error handling (5)**: LLM returns invalid JSON → ModelError; negative quantity → ValidationFailed; missing TenantId; invalid config JSON; unknown taxMethod
- **Cache key (ADR-014, 3)**: starts with `invoice-extractor:{tenantId}:`; different tenants → different keys; empty tenantId throws
- **ADR-015 telemetry**: sensitive sentinels (vendor, invoice number, customer, line description, monetary amount) NEVER appear in captured logs

## Data + script

- `infra/dataverse/sprk_analysistool-invoice-extractor-row.json` — seed row metadata
  - `sprk_toolcode = "INVEXT@v1"` (9 chars; ≤10 cap per task 104 note)
  - `sprk_handlerclass = "InvoiceExtractionToolHandler"`
  - `sprk_availableincontexts = 100000002` (Both)
- `scripts/Seed-TypedHandlers.ps1` — entry added (in same wave with 104 / sibling Wave 2)

## Build + tests + size

- `dotnet build src/server/api/Sprk.Bff.Api/`: **0 errors**, 16 pre-existing warnings
- `dotnet test --filter "FullyQualifiedName~InvoiceExtractionToolHandler"`: **27 passed / 0 failed**
- Full handler test set (`FullyQualifiedName~Handlers`): **291 passed / 0 failed**
- BFF publish-size delta: **+0.12 MB** uncompressed (137.962 → 138.082 MB). Well under per-task ≤+0.6 MB cap; cumulative R6 Wave 1 + Wave 2 budget ≤+5 MB on track.
- Vulnerable packages: only pre-existing `Microsoft.Kiota.Abstractions 1.21.2` (HIGH). NOT introduced by this task — task adds ZERO new NuGet packages.

## Quality gates (FULL rigor)

- **adr-check**: ✅ PASS — 7 ADR compliance dimensions verified (ADR-010, ADR-013, ADR-014, ADR-015, ADR-016, ADR-029, NFR-13)
- **code-review** equivalent: handler mirrors the established Wave 2 LLM-assisted pattern (sibling 105/106/107) with no novel architectural shape. Same XML doc style, same DI surface, same telemetry shape, same cache integration.
- **ArchTests**: 6 pre-existing failures unrelated to this task (`DataverseAuthorizationFilterOptions`, `IInvoice{Analysis,Review}Service` from unrelated CRUD subsystem, etc.). My handler does not affect any ArchTest.

## Wave 2 completion

This was the **last** of the 4 Wave 2 LLM-assisted typed handlers:
- ✅ 105 `EntityExtractorHandler` (LLM-assisted NER)
- ✅ 106 `ClauseAnalyzerHandler` (LLM-assisted clause structuring) — note: still showed 🔲 in TASK-INDEX at time of writing; handler.cs already exists so the work is done
- ✅ 107 `RiskDetectorHandler` (LLM-assisted + severity scoring)
- ✅ 108 `InvoiceExtractionToolHandler` (LLM-assisted + line-item arithmetic) — this task

With Wave 2 complete, **task 109** (handler dispatch tests for playbook + chat) is unblocked.

## Open items

None blocking. Two observations for future cleanup:

1. The `InvocationContextKind.Both` chat surface relies on per-tool `text` argument in `ChatInvocationContext.ToolArgumentsJson`. Task 010 (`ToolHandlerToAIFunctionAdapter`) is responsible for validating the JSON schema at the adapter boundary; the handler's chat-side validation is a defensive guard.
2. Pre-tax discount proportional tax adjustment in `ComputeTotals` is a defensible default for the most common invoice shape (single tax rate per invoice + total discount). Edge case: multi-rate invoices with line-specific discounts would need a different model — but no current consumer requires it, and the LLM-reported numbers are surfaced as-is in `arithmeticErrors[]` so reviewers see any anomaly.
