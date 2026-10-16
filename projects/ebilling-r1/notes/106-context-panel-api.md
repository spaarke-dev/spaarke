# Task 106 - Invoice Review context panel API (notes)

> Run 2026-10-16. Rigor: the POML says STANDARD; the tree overrides UP to FULL (tags `bff` / `api`, modifies `.cs`), so code-review and adr-check ran. Model sonnet @ high. Spec FR-33, NFR-02. Wave W4-1.

## 1. What was built

`GET /api/finance/invoices/{invoiceId}/context` (`Api/Finance/InvoiceContextEndpoints.cs`) over `InvoiceContextService` (`Services/Finance/InvoiceContextService.cs`, concrete, scoped).

- Filter chain: group `RequireAuthorization()` -> `AddFinanceAuthorizationFilter("finance.read", sprk_invoices, route invoiceId)` (Read as the caller, fail closed) -> `RequireEbillingEnabled()`. The flag never replaces authorization.
- Response: `{ invoiceId, coreRecord, budget, priorInvoices, disallowHistory, rateHistory }`. Each section is `{ status, data, code, message }`, `status` = `available` | `unavailable` | `notTracked`.
- 200 unless authorization fails. The only 4xx after authorization is the uniform 404 for an invoice deleted between the check and the read (ProblemDetails, 4xx only).
- Firm (`sprk_vendororg`) and core record (exactly one of `sprk_matter` / `sprk_project` / `sprk_regardingworkassignment`) are read from the invoice. No firm or core id is accepted from the request.

## 2. Sections, scope and codes

| Section | Source | Notes |
|---|---|---|
| coreRecord | matter / project / work assignment row | type, name, number, status code + Dataverse label, opened / closed date (work assignment has no such columns: null) |
| budget | `sprk_totalbudget`, `sprk_totalspendtodate`, `sprk_remainingbudget`, `sprk_budgetutilizationpercent`, `sprk_invoicecount` as `FinanceRollupService` stored them | never recomputed. All five empty -> `context.budget.rollup-not-populated` (escalation trigger). Work Assignment -> `notTracked` / `context.budget.not-tracked`. `asOf` = the core record's `modifiedon` (the rollups have no timestamp of their own) |
| priorInvoices | other `sprk_invoice` rows of this firm on this core record, current excluded, most recent 10 (`invoicedate` desc, `createdon` desc, id asc), `hasMore` | all statuses are shown, with a stable `status` key from `InvoiceStatusCodes` |
| disallowHistory | `sprk_billingevent` Reduced / Disallowed Fee + Expense lines joined to this firm's invoices on this core record, current excluded | counts, amounts (Reduced = billed - approved, Disallowed = billed), top 5 reasons (`sprk_adjustmentreason`, whitespace-collapsed, case-insensitive grouping, count desc then text) |
| rateHistory | this invoice's Fee lines; roster; Published-card rates (task 046's `BuildRateQuery`, all periods); billed rates on the firm's other invoices on this core record | per timekeeper: approved rates by effective period (basis timekeeper / classification, scope firm / coreRecord), billed rates on this invoice and previously. A billed name the roster did not match is listed with billed rates only |

Stable codes: `context.<section>.unavailable` (exception), `context.<section>.timeout` (deadline), `context.invoice.unavailable` / `.timeout` (header read failed: all five sections), `context.core-record.unresolved` / `.ambiguous`, `context.firm.unresolved`, `context.budget.not-tracked`, `context.budget.rollup-not-populated`, `context.disallow-history.too-large`, `context.rate-history.too-large`. The cause is logged with ids only.

## 3. Query count (NFR-02; independent of line count)

One header read, then in parallel: core record 1, budget 1 (0 for a Work Assignment), prior invoices 1, disallow history 1 (paged), rate history 4 (this invoice's fee lines, then roster + rates + previous lines in parallel; 0 of the last three when no line has a roster-matched timekeeper). Total **9** reads for a Matter / Project invoice with roster-matched timekeepers, 5 minimum. Pinned by `QueryCount_DoesNotDependOnTheInvoicesLineCount` (3 vs 400 lines, same 9). Paged reads (5000 per page) stop after 10 pages and the section says `too-large` rather than summarise a partial read. The whole response has a 5 s deadline (`Deadline`); NFR-02 (3 s p90) is measured live by task 112.

## 4. Deviations and assumptions (each one line)

1. **Disallow history counts decided invoices only** (Approved, Sent to AP, Paid). A Reduced line on an In Review invoice is provisional, and Rejected / Returned / Cancelled invoices are void (FR-36). The POML says "this firm's invoices"; this narrows it. Easy to widen in `StandingInvoiceStatuses`.
2. **History is firm AND core record** (the POML constraint), not the firm's history across all core records.
3. **"Prior" invoices are all other invoices of the firm on the core record**, most recent first, not only those dated before this one (reviews happen out of order).
4. **Two core record lookups set on one invoice -> `ambiguous`**, not a precedence rule (FR-05: exactly one).
5. **Classification rates** are included in the rate history for a timekeeper whose roster classification matches (rate rows with no timekeeper). The POML says "sprk_ratecardrate for the firm's timekeepers"; a firm that rates by classification would otherwise show nothing.
6. **Two files not in the POML list were touched**: `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` (new `GovernedFile` + `ExpectedEndpointFileCount` 126 -> 127; the guard demands it for any new endpoints file).
7. Tests live in the unit project: the contract file is compiled in through the `tests/integration/contract/**` glob.

## 5. Escalation trigger check (rollups in dev)

Read-only Dataverse MCP queries on 2026-10-16: of the matters carrying invoices, some rollups are populated, some partly (matter `Real estate transaction analysis`: budget 55,000, spend 25,000, remaining and utilization null) and some stale (matter `REAL-2026-123456.02` shows `sprk_invoicecount` 0 while 6 invoices exist). This is the already-filed defect **#1663** (baseline D6: nothing enqueues `SpendSnapshotGeneration`; the counted status set is unreachable until 015). Not re-filed. The section behaves as the trigger requires: it shows the stored values as they are, never computes spend, and is unavailable when nothing is populated. Until #1663 / 015 are fixed, budget numbers in dev can be stale.

## 6. Quality gates

- code-review (self-run over the two source files; confirmed findings fixed in `270ac538d`): (a) F1 the approved-rate read ignored an incomplete page, so a very large card set would have produced a silently partial rate history - now `too-large`; (b) F2 the unmatched timekeeper display name depended on row order - now the ordinal-smallest spelling; (c) unused `using` removed. Cohesion: one responsibility (assemble the panel), 835 lines of which ~35% is the response contract and comments - "cohesive: five read-only sections over one invoice scope".
- adr-check (manual against the loaded ADR rules): ADR-001 Minimal API, own endpoints file; ADR-008 per-resource endpoint filter on the route; ADR-010 no new interface (ceiling stays 166); ADR-013 no AI; ADR-018 flag chained after authorization; ADR-019 ProblemDetails for 4xx only; ADR-015 logs carry ids only. No violation, no ADR tension.
- Mutation proof (each turned tests red, then restored): dropping the firm condition, dropping the exclude-current condition (scoping test), removing the authorization filter (5 contract tests), removing the flag filter (disabled test), making the section guard rethrow (compile warning-as-error; the isolation theory covers it).

## 7. Known limits (K-class, not fixed)

- **K2** The reads are app-only (as every read in this namespace) and bounded by the invoice's own firm and core record; the caller's right is checked on the one invoice. A caller who can read this invoice but not a sibling invoice on the same core record still sees the sibling's number, dates, totals and status in `priorInvoices` and its disallow totals. FR-33 requires prior invoices on the core record; access to invoices follows the core record. Stated here for the 130 / uac-r2 review.
- **K2** `budget.asOf` is the core record's `modifiedon`, not the rollup time.
- **K4** `MaxTimekeepers` 1000 and the 10-page cap are fail-loud (`too-large`) limits no real invoice is expected to reach.

## 8. BFF hygiene (root section 10)

- Placement: BFF, B4 live request (a synchronous read of Dataverse for a user-facing panel; no queue, schedule or AI).
- Section 11: existing = `FinanceSummaryService` (matter-only, cached, no firm or rate scope); extension rejected (its cached DTO is used elsewhere); cost of doing nothing = FR-33 unmet. Reuses `InvoiceResolutionService.BuildRateQuery`, `InvoiceStatusCodes`, `InvoiceTotalsService` column constants, `FinanceAuthorizationFilter`, `EbillingEnabledFilter`.
- ADR-010: no 1:1 interface added; ceiling unchanged at 166 (`ADR010_DITests` passes).
- Publish size (Release, `-r linux-x64 --self-contained false`, `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs as `dotnet publish` emits them, both from short-path worktrees `C:\w106a` / `C:\w106b`): **37.325 MB** with this task vs **37.294 MB** for its parent commit (`9db5d265e`, which already contains master as merged into the branch): **delta +0.031 MB**. A separate master build was not made: the parent commit isolates this task's contribution and the branch carries master.
- `dotnet list package --vulnerable --include-transitive`: no vulnerable packages. No package added.

## 9. Tests

- `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/InvoiceContextServiceTests.cs` - 33 tests: all five sections from fixtures; hasMore; empty history; reason cap and order; scoping on every cross-invoice query (firm, core, not-this-invoice, only the invoice's own ids mentioned); decided-invoice and Fee / Expense filters; project column; Work Assignment not tracked; budget not populated / partial; each of 8 sources failing in turn; timeout; request cancellation propagates; header failure; unknown invoice; no firm; no core / two cores; query count independent of line count; too-large (disallow, rate cards); determinism; reason normalisation.
- `tests/integration/contract/Api/Finance/InvoiceContextEndpointsAuthorizationContractTests.cs` - 11 tests: 401, 403 (no rights, Write-only, other invoice / query-id decoy, rights check faulting), 503 `ebilling.disabled` (authorized) and 403 (unauthorized, flag off), 200 five sections, 200 with every source failing, Work Assignment budget, uniform 404.
- Results: see the task report (focused 44 pass; ArchTests 1052 pass).

rework: fix-round-1 task 106
