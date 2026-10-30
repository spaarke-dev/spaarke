# Task 016 — Invoice totals calculator (I-1) — notes

> 2026-10-15, parallel mode (TASK-INDEX.md and current-task.md left to the main session). Rigor STANDARD (code-review + adr-check still run: the task touches `tests/**`).

## 1. What was built

| File | Role |
|---|---|
| `Services/Finance/InvoiceTotalsCalculator.cs` | Pure, static. `Calculate(billedTotal, lines)` returns an `InvoiceTotalsCalculation` (five totals + computed amount per Adjustment line + optional `Refusal` with a stable code). Also holds the option-value enums (`InvoiceLineCostType`, `InvoiceLineStatus`, `InvoiceAdjustmentMethod`) and `InvoiceTotalsRefusalCodes`. |
| `Services/Finance/InvoiceTotalsService.cs` | `IInvoiceTotalsService.RecalculateAsync(invoiceId)` (no total parameter anywhere). Reads the invoice (`sprk_totalamount` + `versionnumber`) THEN the lines (paged), calculates, writes changed Adjustment-line `sprk_amount` (update-only), then the five totals with `If-Match` on the version read; 412 -> re-read and recompute, 3 attempts, then `DBConcurrencyException`. A refused calculation writes nothing. |
| `Infrastructure/DI/FinanceModule.cs` | One unconditional `AddScoped<IInvoiceTotalsService, InvoiceTotalsService>()`. |
| `tests/unit/.../InvoiceTotalsCalculatorTests.cs`, `InvoiceTotalsServiceTests.cs` | 38 tests. |
| `tests/Spaarke.ArchTests/ADR010_DITests.cs` | 1:1 ceiling 160 -> 161 (seam justification in the comment). |
| `docs/data-model/sprk_invoice.md` | "Totals calculation" section: definitions, rounding choice, refusal codes. |

## 2. Decisions

- **Rounding:** `decimal`, `MidpointRounding.AwayFromZero`, 2 decimals, on each computed adjustment and each total (recorded in `sprk_invoice.md`).
- **Several percent adjustments share one base** (they do not compound); the POML formula defines each Percent line on `fees total - Σ Fee-line adjustments`, which is what is implemented.
- **Fail closed instead of skipping** (beyond the POML's one refusal): a missing billed total, a negative Fee/Expense amount, a line with no cost type, a Reduced line with no approved amount or one outside 0..billed, an Adjustment line with no method/value, a negative value or a percent above 100. Each is a refusal with its own code, nothing written. A skipped line would silently change what the firm is paid.
- **Write order:** Adjustment-line amounts first (idempotent), invoice totals last as the commit point; the invoice version is read before the lines, so a recalculation that committed in between makes this one's conditional write fail and retry.
- **Deactivated lines are counted** like active ones (no task defines line deactivation).
- **Escalation trigger (billed total vs fees + expenses):** I-1 as written in the POML constraints is unambiguous (approved = `sprk_totalamount` - adjustments), so no STOP. When lines exist and the two differ, the service logs a warning. Non-blocking owner question in the report: is a billed total that differs from fees + expenses (firm-side discount not shown as a line) expected?

- **Review (adversarial-reviewer, one pass) and resolutions.** F2 `ReadMoney` converted a `double` billed total through a culture-sensitive string (de-DE: 1500.50 -> 15005) -> fixed (typed switch), test under de-DE fails with the old code. F3 service tests only fed `long` -> fixed with the double case. K2 null billed total was read as 0 -> now refused (`billed-total-missing`). K2 negative Fee/Expense amounts could not be stored (platform minimum 0) -> refused (`line-amount-negative`). K3 a deleted Adjustment line surfaced as `KeyNotFoundException` ("invoice not found") -> now a re-read, then `DBConcurrencyException` after 3 attempts. K4 doc remark on refused figures corrected; Adjustment-line status ignored is now documented and pinned by a test. Known limits, no change: every successful call PATCHes the invoice (bumps `versionnumber`) even when the totals are unchanged, so a concurrent If-Match writer such as the lifecycle service (014) gets a 412 and retries; the ADR-010 ceiling is justified by callers that do not exist yet (falsifiable note in the test file, re-check at 102).

## 3. Findings the callers need

- **FR-32 refusal point.** The calculator/service refuse a result; the adjustment line that caused it already exists when `RecalculateAsync` runs (callers 100-102 create it, then recalculate). The caller must undo or reject that adjustment line when it gets `IsRefused`; nothing here deletes it. The Adjustment line's own `sprk_amount` stays at its old value in that case.
- **Direct Dataverse edits of the totals** are not blocked by this task (needs read-only on forms in 029 and, if wanted, a field-security profile). Brief gap, raised in the report.

## 4. Verification

- Unit: `InvoiceTotals*` 38 passed (32 before the review fixes). Mutation checks (each makes tests fail): expenses in the percent base (4 fail), base before line adjustments (3), banker's rounding (1), write despite refusal (2), wrong version in the conditional write (2).
- ArchTests: 1045 passed after the ceiling change.
- Publish size (fresh short-path worktrees `C:\wt016b` parent `290ffd704` vs `C:\wt016m` this commit): framework-dependent linux-x64, Release, PDBs included, Compress-Archive Optimal, 192 files each: 37.128 MB -> 37.135 MB, delta +0.008 MB (7,990 bytes). No package added; `dotnet list package --vulnerable --include-transitive`: none.

## 5. Section 11 answers (new surface)

| Surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `InvoiceTotalsCalculator` | `FinanceBaseValues` (matter/project spend rollup) and `SpendSnapshotService` (period sums) compute different things; nothing computes per-invoice I-1 totals. | No: FinanceBaseValues is ontology-owned and a different grain. | FR-07 fails and 049/100/101/102 each compute totals (breaks ADR-002 one owner). |
| `IInvoiceTotalsService` / `InvoiceTotalsService` | `InvoiceReviewService` only copies a caller's total on create; no recalculation. | No: a separate persistence owner keeps the calculator pure. | Approved total not recalculated on change; client-supplied totals possible. |
| Interface (ADR-010 seam) | `IEbillingSettingsReader` precedent. | Concrete registration rejected: four callers fake it in tests. | Caller tests would need Dataverse mocks for the read-compute-write. |
| Refusal-code constants, enums | `SpendSnapshotService` reads `sprk_costtype` as an int with no meaning. | No existing enum for the 010 option values. | Magic numbers in every caller. |
