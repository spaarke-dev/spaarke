# Task 104 — FinancialCalculationToolHandler Completion Notes

**Status**: ✅ Completed (2026-06-07; Wave 4 of Phase A — H-G1 deterministic handler)
**Rigor**: FULL

## What was built

`src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/FinancialCalculationToolHandler.cs` (~540 LOC). Named financial formulas: `simple_interest`, `compound_interest`, `present_value`, `future_value`, `loan_payment`, `roi`. **NO LLM call, NO Azure OpenAI dependency.**

Distinct from task 102 (`FinancialCalculatorHandler` — general arithmetic operations). 104 is finance-domain-specific named formulas.

## Numeric discipline (FR-20 binding)

- All money math in `decimal` (binding).
- `double` escape-hatch only for fractional/large-exponent compound interest, with **quantize-back to decimal**. Documented inline.
- Exponent capped at 1000 (DoS protection).
- Banker's rounding default; configurable via `options.rounding`.

## ADR-015 compliance

Parameter validation exceptions surface parameter NAMES only (e.g., `"'principal' must be > 0"`), never values. Telemetry: handler name + tool id + analysis id + tenant id + formula name + duration + outcome. **Never input values.** Test `Telemetry_DoesNotLogInputValues` enforces.

## Tests

30 unit tests pass: positive cases per formula (simple interest, compound, PV/FV, loan payment, ROI) + validation errors (negative inputs, unknown formula) + banker's rounding behavior + decimal precision determinism (`0.1m + 0.2m == 0.3m` exactly) + ADR-015 telemetry hygiene.

## Data + script

- `infra/dataverse/sprk_analysistool-financial-calculation-row.json` — seed row metadata
- `scripts/Seed-TypedHandlers.ps1` — entry added (shared with 101, 102, 103)

## Build + size

- `dotnet build`: 0 errors, 16 pre-existing warnings
- BFF size delta: **+0.04 MB**

## Caveat (raised by task 101)

`sprk_toolcode` Dataverse column has 10-char max. Task 104's `FIN-CALC-FORMULA@v1` is 19 chars — will hit `0x80044331` on deploy. Either shorten the toolcode at deploy time OR extend the column. Defer to a coordinated data-cleanup task after Wave 4 commits.
