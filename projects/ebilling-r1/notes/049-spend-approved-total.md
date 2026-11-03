# Task 049 (spend part) - D-130 (2): counted spend sums the approved total

> Owner decision D-130 (2), 2026-10-19: **counted spend sums the approved (payable) total**, `sprk_approvedtotal`, falling back to the billed `sprk_totalamount` where the approved total is empty, on both spend paths. Reviewer disallowances and reductions therefore lower spend. Pending Spend (D-104) uses the same amount rule. Builds on D-103 (counted statuses), D-102 / D-104 (held, pending), D-127 / D-129 (credit memos are negative and signed).
> ontology-r1 owns `FinanceBaseValues` and the Threshold measures, and reviews this.

## The rule (defined once, in `FinanceBaseValues`)

An invoice's spend amount is `sprk_approvedtotal` when it **has a value (not null)**, otherwise `sprk_totalamount`, otherwise 0.

- `FinanceBaseValues.SpendAmount(Entity)`, `SumSpend(IEnumerable<Entity>)` and `SpendAmountColumns` are the rollup-side definition (next to `CountedInvoiceStatuses`).
- An approved total of **0** is a value: every charge disallowed counts 0 and does not fall back to billed. Pinned by a test on both paths.
- Signed: a credit memo's negative approved total lowers spend; a credit memo with no approved total falls back to its (negative) billed total.

## The FetchXML shape (live path)

An aggregate FetchXML cannot coalesce per row, so the amount rule is two grouped aggregates, each per parent, added by the reader. Both carry the same filters as before (parent `in`, `statuscode in` the counted or pending set, and for pending the held anti-join):

| Part (`FinanceBaseValues.SpendAmountPart`) | Sums | Extra root condition |
|---|---|---|
| `BilledWhereNoApprovedTotal` | `sprk_totalamount` | `sprk_approvedtotal` operator `null` |
| `ApprovedTotal` | `sprk_approvedtotal` | `sprk_approvedtotal` operator `not-null` |

- Each invoice is in exactly one part (null / not-null are complementary), so nothing is counted twice and nothing is missed.
- Builders: `BuildGroupedSpendFetchXml(parent, part, ids)` (counted), `BuildGroupedPendingSpendFetchXml(parent, part, ids)` (pending), `BuildGroupedBudgetFetchXml(parent, ids)` (budget, unchanged shape). They replace `BuildGroupedSumFetchXml` and `BuildGroupedPendingSumFetchXml` (internal; every caller and test was updated).
- `ReadAsync` sends three invoice queries per chunk of 200 parents when spend is needed (was one: billed part, approved part, billed re-read; see "Read order"), plus the budget query when needed. `ReadPendingSpendAsync` likewise sends three. Still grouped, never one per parent.
- **Fail-closed reader contract kept.** Both results go through the unchanged `ReadGroupedSums`: an unknown parent or sum shape throws `InvalidOperationException`, a Dataverse failure propagates. A throw from either part aborts the whole read, so a half-read spend never comes back as a smaller number. Tests: an unreadable row in part 0 or part 1 throws; a Dataverse failure in part 0 or part 1 of the pending read propagates.
- **Read order and the re-read (corrected in the review round below).** The reader runs the billed-fallback part, the approved part, then the billed part AGAIN. The parts are separate queries, so a racing write can move spend EITHER way, under all four operators: an invoice whose approved total goes from empty to set between the two is counted twice (and for a credit memo that LOWERS spend, which a `le` / `lt` rule reads as headroom); a status change or a new counted invoice can be seen by one part and missed by the other. Any of these changes the billed part, so the re-read detects it: if any parent's billed sum differs from the first read, the whole three-read attempt is retried once, and if the second attempt also differs the read throws `InvalidOperationException` (fail closed). The earlier claim that this order "can over-count, never drop" was wrong and is withdrawn. A test pins the order.
- **Null sum.** A part whose group has only null amounts returns a null sum, which `ReadGroupedSums` already treats as 0.

## Paths changed

| Reader | Change | Why |
|---|---|---|
| `FinanceRollupService.RecalculateForEntityAsync` (stored `sprk_totalspendtodate`) | Total spend uses `SumSpend`; the invoice queries now select both amount columns (`InvoiceColumns()`) | counted spend, stored path |
| Same method: monthly spend (`sprk_monthlyspendcurrent`), prior month, the 12-month timeline (`sprk_monthlyspendtimeline`), velocity (`sprk_monthovermonthvelocity`), average invoice (`sprk_averageinvoiceamount`), utilization, remaining budget | All derive from the same per-invoice `SpendAmount` (extracted to `FinanceRollupService.ComputeSpendFigures`, internal, so a test can run it without a live `ServiceClient`) | They are views of counted spend; if only the total moved, the matter form would disagree with itself |
| `FinanceRollupService` Pending Spend (`sprk_pendingspend`) | `SumSpend` over the pending query, which now selects both amount columns | D-104 uses the same rule (D-130) |
| `FinanceBaseValues.ReadAsync` (the live Threshold input, `sprk_totalspendtodate` and utilization) | Two grouped queries, summed | counted spend, live path |
| `FinanceBaseValues.ReadPendingSpendAsync` | Two grouped queries, summed, held anti-join kept in both | D-104, live path |
| `FinancialCalculationToolHandler` (the AI financial-calculation tool: spend, remaining budget, utilization, average) | Uses `FinanceBaseValues.SpendAmount` over the rollup's own `BuildInvoiceQuery` | It is documented to send the rollup's query "so the tool's totals agree with the matter form"; left alone it would report billed spend while the form shows approved spend |
| `PolicyVersionValidator` (doc comment only) | The residual-risk note names the two amount columns and the new builder | The evaluator now also reads `sprk_invoice.sprk_approvedtotal` org-wide, which is not on the allow-list either (same accepted residual, D-161) |

## Paths left alone, with reasons

| Reader | Reason |
|---|---|
| `FinanceSummaryService` recent-invoices list (`BuildRecentInvoicesQuery`, `ToRecentInvoice`) | Not a spend sum: the last 5 invoices of any status, each showing its own billed `TotalAmount` and status. It is an invoice listing; changing the field's meaning would change a DTO contract |
| `SpendSnapshotService` / `SpendSnapshotGenerationJobHandler` / `SignalEvaluationService` (legacy `sprk_invoicedamount`) | Sums billing events with `VisibilityState = Invoiced`, not `sprk_invoice` totals; a different measure, not the D-103 counted spend, and not fed by `sprk_totalamount` |
| `InvoiceReviewService`, `InvoiceTotalsService`, `InvoiceTotalsCalculator` | They compute and write the totals themselves (`sprk_totalamount`, `sprk_approvedtotal`); they are the source of the amount, not a reader of spend |
| `InvoiceLiveFactResolver` (`invoiceTotal`) | A per-invoice fact documented as the billed total; not an aggregate |
| `InvoiceIndexingJobHandler` | Indexes the invoice's fields for search |
| `ExternalAccessModule` (`sprk_totalamount` in the invoice ReadableColumns) | A column allow-list for the external SPA's record detail; no sum |
| `PolicyVersionValidator` / `PredicateCompiler` (logic) | They only name the derived columns; the sum itself is `FinanceBaseValues`, unchanged API for them (`Needs`, `ValueOf`, `ReadAsync`'s tuple) |

## Tests (each failed before, on its path)

All in `tests/unit/Sprk.Bff.Api.Tests/Services/Finance` unless noted. Both-path tests assert the rollup path (the rollup's `QueryExpression` evaluated in memory, then `SumSpend`) and the live path (`FinanceBaseValues.ReadAsync` / `ReadPendingSpendAsync` through the grouped FetchXML evaluated in memory) on the same rows.

- an Approved invoice with a disallowed line counts its approved total (billed 1000, approved 800 -> 800);
- an invoice with no approved total counts its billed total;
- an approved total of 0 counts 0 (no fallback);
- mixed invoices sum correctly (approved where set, billed where not, In Review / Rejected / another matter / a project left out);
- a credit memo lowers spend: with an approved total, with only a billed total, and with a reduced credit (D-129);
- Pending Spend uses the same rule, still excludes held invoices, counts an invoice with several linked submissions once;
- a seeded random world of 200 invoices agrees on both paths for counted and pending;
- the D-102 must-fire / must-not-fire and D-104 held tests are unchanged in intent and green; a new test shows a held invoice WITH an approved total is still left out of counted spend;
- the rollup's stored figures (`ComputeSpendFigures`: total, current month, prior month, timeline, count, velocity, average) use the approved amount;
- the shapes: each FetchXML part sums the right column with the complementary null / not-null filter; the rollup queries select both amount columns; the reader sends the parts in the documented order;
- fail-closed, per part (above);
- `FinancialCalculationToolHandlerTests`: the tool's query reads both columns;
- `FinanceBaseValuesSingleDefinitionGuardTests` (arch): no consumer (rollup, evaluator, tool handler) names an invoice amount column itself; must-fire and must-not-fire controls added.

**The in-memory store now projects columns.** `SpendHeldAndPendingTests.Store.Run` returns only the columns in the query's `ColumnSet`, as Dataverse does. Without that, a rollup that forgot `sprk_approvedtotal` would read the column from the full in-memory row and a test would pass for the wrong reason.

**Mutation checks** (production code mutated, `SpendHeldAndPending` + `FinanceBaseValuesTests`, 71 tests):

| Mutation | Failed |
|---|---|
| Rollup amount = billed total only (the pre-D-130 rollup) | 8 |
| Live path = one billed-only query (the pre-D-130 live read) | 13 |
| Rollup `ColumnSet` leaves out `sprk_approvedtotal` | 7 |
| Billed part loses its `null` filter (double count) | 14 |

**Limits of the evidence.** The in-memory evaluator proves the query logic and the reader, not that Dataverse accepts the new FetchXML. The new elements are the standard `null` / `not-null` operators on a Money column inside an aggregate; the existing read-only seam case `FinanceBaseValuesSeamTests.PendingSpendQueries_AreAcceptedByDataverse_ReadOnly` now sends the new pending shape (it creates nothing), and `GroupedSum_OnRealRows_...` sends the new counted shape. **Neither was run live here** (no live calls were in scope). Both should be run before 079 relies on them. The real-row seam case creates invoices with no approved total, so each falls into the billed part and its expected sums are unchanged; a case with real approved totals is a live write and was not added.

## §11 answers

**No new surface.** No endpoint, DI registration, package, Dataverse column, schema script, or file surface is added. `sprk_approvedtotal` already exists (task 012 / 016). The additions are internal members of existing classes (`FinanceBaseValues.SpendAmount`, `SumSpend`, `SpendAmountColumns`, `SpendAmountPart`, the builders, `FinanceRollupService.ComputeSpendFigures`); `SpendAmountPart` is the one new public type, nested in `FinanceBaseValues`, and exists so the tests and the builders name the two halves of the rule. For completeness:

- Existing: the counted and pending sums in `FinanceBaseValues` and `FinanceRollupService`, which this changes.
- Extension: yes; this extends them. `ComputeSpendFigures` is the existing loop moved to a static method so it can be tested; nothing was added to it.
- Cost of doing nothing: the owner's D-130 (2) is unbuilt: a reviewer's disallowance would not lower the matter's spend, and a Threshold would judge a budget on billed amounts the firm will never be paid.

BFF hygiene (§10): no endpoint, service, registration, package or background work is added, so the publish-size measurement is not required for this change (no package, no new type of consequence).

## Gates

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors, 0 warnings.
- Unit filter (Finance, Spend, Rollup, Budget, Signal, Ontology, Billing): 5686 passed, 0 failed, 6 skipped (pre-existing skips).
- `tests/Spaarke.ArchTests`: 1212 passed, 0 failed.
- Not run: the live seam cases (no live calls in scope). The seam files compile with the unit project.

**Harness change in ontology-r1's `SignalReevaluationJobTests`.** Its in-memory aggregate fake refuses any condition it does not model (so a changed fetch fails loudly). It now models `null` / `not-null` on `sprk_approvedtotal`, `WithInvoice` takes an optional approved total, and the three assertions that counted fetches moved from 2 to 3 (two invoice parts and one budget query; a spend-only rule from 1 to 2). One evaluator-level test, `Run_SpendThreshold_SumsTheApprovedTotal_FallingBackToTheBilledTotal_AndACreditMemoLowersIt`, was added.

## Noticed, not changed

- `FinancialCalculationToolHandler` computes utilization with its own `* 100` formula instead of `FinanceBaseValues.UtilizationPercent` (pre-existing; the arch guard therefore checks only the amount rule for it).
- The pre-existing `ExternalAccessModule` invoice column allow-list and `FinanceSummaryService` recent-invoices list still show the billed total only; if the SPA or the matter page should show the approved total beside it, that is a separate display decision.

## For ontology-r1

Spend, in the live Threshold input and in the stored `sprk_totalspendtodate`, is now the sum of each counted invoice's **approved total, else its billed total**. Pending Spend is the same amount over In Review invoices that are not held.

- `FinanceBaseValues.ReadAsync(...)` keeps its signature and its `(Spend, Budget)` tuple. The `SignalEvaluator` needs no change. It now issues three invoice queries per 200 parents (was one; the third is the billed re-read) when a rule reads spend or utilization, plus the budget query when needed. `ReadPendingSpendAsync` also issues three.
- Fail-closed: unchanged. A shape the reader does not know, or any Dataverse failure in either query, throws; nothing reads as zero. Call `ReadPendingSpendAsync` with an application identity only (K2 from the earlier round still holds: the held exclusion joins the user-owned submission table).
- Internal API renames (nothing outside `Sprk.Bff.Api` and its tests used them): `BuildGroupedSumFetchXml` is now `BuildGroupedSpendFetchXml(parent, part, ids)` and `BuildGroupedBudgetFetchXml(parent, ids)`; `BuildGroupedPendingSumFetchXml` is now `BuildGroupedPendingSpendFetchXml(parent, part, ids)`.
- New public members on `FinanceBaseValues`: `InvoiceApprovedTotal`, `SpendAmountColumns`, `SpendAmount`, `SumSpend`, `SpendAmountPart`.
- `PolicyVersionValidator`: only a doc comment changed. The accepted residual (D-161: the evaluator reads base invoice columns org-wide, which the allow-list does not cover) now also covers `sprk_invoice.sprk_approvedtotal`. Whether `sprk_approvedtotal` should be added to the allow-list or the secured-column test is your call; it was not changed here.
- Behaviour you will see: an Approved invoice billed 1000 with 200 disallowed counts 800; a credit memo (negative approved total) lowers spend; an approved total of 0 counts 0. A Threshold such as "spend reaches 90 percent of budget" now fires later than it would on billed amounts when reviewers disallow charges. In Review invoices are unaffected until approved (not counted either way).
- Please check: the `null` / `not-null` condition on `sprk_approvedtotal` inside an aggregate FetchXML (the two new shapes) via the read-only seam case, and that dev data has no invoice whose approved total is the placeholder 0 while its billed total is real (a 0 is treated as a real value and counts 0, by design).
- Guard: `FinanceBaseValuesSingleDefinitionGuardTests` now also flags any rollup / evaluator source that names an invoice amount column, so a second copy of the rule fails CI.

## Live check (main session, 2026-10-19)

The read-only seam case `FinanceBaseValuesSeamTests.PendingSpendQueries_AreAcceptedByDataverse_ReadOnly` now also sends `FinanceBaseValues.ReadAsync` (both counted parts plus the budget query). It passed against spaarkedev1, run as the operator with ids that match no row; it creates nothing. Dataverse accepts `null` / `not-null` on `sprk_approvedtotal` inside the grouped aggregates, for both counted spend and Pending Spend, on matter and project. The real-row semantic case still creates rows, so running it needs the owner's OK.

## Who keeps sprk_approvedtotal current (ontology-r1 review, 2026-10-19)

D-130's promise that reviewer disallowances, reductions and credit memos lower spend holds only once the reviewer write paths recalculate totals. Today `IInvoiceTotalsService.RecalculateAsync` has one caller, the 049 persister at submission. The pending tasks own the other callers, and their POMLs now carry this as a tested constraint:
- 100: line decisions (Reduce, Disallow, bulk, accept-clean);
- 101: corrections;
- 102: adjustments.
None is built yet, so nothing is wrong in shipped code. Until they land, the approved total equals the submission-time total. The stored matter and project spend follows at the next rollup.

## Review round (ontology-r1 review notes on D-130, 2026-10-19)

**K1, the read race (fixed in the read, not just the comment).** The old comment ("can over-count, never drop, so a Threshold never reads a shortfall as headroom") was false: a credit memo counted twice LOWERS spend, and for `le` / `lt` rules an over-count is the harmful direction. A racing write moves spend either way, under all four operators.
- `FinanceBaseValues` now reads, per chunk: billed part, approved part, billed part again. If any parent's billed sum differs between the two billed reads (a parent present in one and absent in the other counts as 0), the whole three-read attempt is retried once inline and a Warning is logged (`{ParentCount}` parents only, no amounts: ADR-015). If the second attempt also differs it throws `InvalidOperationException` ("Spend could not be read consistently ..."), the existing fail-closed reader convention; the caller retries on its next run. One concurrent write therefore costs one extra read, not a failed run.
- Why the billed re-read detects every case: an invoice enters or leaves the billed part when its approved total is set, its status changes into or out of the counted set, its amount changes, or it is created or deleted. Each of those changes the billed sum of its parent between the first and third read, unless another write exactly cancels it (accepted: a coincident equal and opposite pair of writes inside one query window).
- Same for Pending Spend (`ReadPendingSpendAsync`): it shares `ReadSpendPartsAsync`. Both take an optional `ILogger? logger` (the evaluator passes its own); callers that pass none read without the warning.
- Fetch counts: invoice reads per spend read 2 to 3 (counted: 3 invoice + 1 budget when budget is needed). Updated, each equal or stricter: `SignalReevaluationJobTests` (2 to 3 and 3 to 4), `SignalEvaluationTriggerHandlerTests` (3 to 4), the order test and the fail-closed theories in `SpendHeldAndPendingTests` (a third index added).
- Tests (`SpendHeldAndPendingTests`): stable world reads three times and sums correctly (counted and pending); billed changing on both attempts throws (counted and pending, six reads); a difference on the first attempt only succeeds with the second attempt's sums and logs exactly one Warning with no values; a store-backed credit memo whose approved total is set right after the billed read returns 200 (a two-read order returned 100). Mutation check: with the comparison forced to "equal", 4 of these fail.

**K2, the aggregate fake.** `SignalReevaluationJobTests` now classifies by value (`GetAttributeValue<Money>("sprk_approvedtotal") is not null`), as Dataverse does. The D-130 evaluator test gained an invoice with an explicit null approved total (billed 25, counts 25); with the old presence-based fake that test fails (checked).

**Field-security guard (ontology's answer: `sprk_approvedtotal` is NOT added to `AuthorableColumns`).**
- `FinanceBaseValues.DerivedBaseColumns` states the derived Threshold's base columns once: `sprk_invoice` (`sprk_totalamount`, `sprk_approvedtotal`, `statuscode`), `sprk_budget.sprk_totalbudget`, and the held anti-join's `sprk_invoicesubmission` (`sprk_processingstatus`, `sprk_outcomereasoncode`, `sprk_invoice`), all from the existing constants.
- Live (gated, not run here): `SignalPredicateTests.AuthorableColumns_ExistAndAreNotFieldSecured_OnTheLiveSchema` also asserts each exists and `IsSecured == false`.
- Offline: `DerivedBaseColumnsFieldSecurityTests` (data-mutation, beside `FirmReasonFieldSecurityAgreementTests`) reads the SpaarkeMaster `Entity.xml` of each table: all seven columns are present and unsecured. A column missing from the source must be listed with a reason in `NotYetInSolution` (empty today); a listed column that IS present fails, so a skip cannot hide a column. Controls: a secured, an unsecured and a missing-element attribute; a test pins the column set.

**Guard widening.** `FinanceBaseValuesSingleDefinitionGuardTests` now also scans all of `src/server/**` (comments ignored) for a file that names an invoice amount column. Existing hits are a ratchet baseline, each with its reason: `ExternalAccessModule` (column allow-list), `FinanceSummaryService` (recent-invoices listing), `InvoiceReviewService`, `InvoiceTotalsService` (they write the totals), `InvoiceLiveFactResolver` (per-invoice billed fact). A new file that names a column fails; a baseline entry that stops naming one fails (three entries were dropped on the first run because they only name a column in comments). Must-fire and must-not-fire controls added. Not violations: none of the baselined files computes a spend sum.

**Gates (this round).** Build 0 errors, 0 warnings. Unit filter (Finance, Spend, Rollup, Budget, Signal, Ontology, Billing, DerivedBaseColumns): 5697 passed, 0 failed, 6 skipped (the pre-existing live skips, including the extended seam case). `tests/Spaarke.ArchTests`: all passed after the baseline fix. Live seam cases not run.

**Re-check (ontology-r1, 2026-10-19).** Three findings from the re-check of ae5197e25, all fixed.
- **F3 (fix-now), the other half of the key union.** No test had a parent present ONLY in the re-check billed read, so `SameSums` iterating only `first.Keys` left every test green. Added `SpendHeldAndPendingTests.F3_*` (a Theory over counted and pending each): (a) first billed read empty, re-check `Rows(Matter, 40)`, second attempt stable gives the second attempt's 45, six reads and exactly one Warning; (b) the same mismatch on both attempts throws. Fail-before: with `SameSums` mutated to `first.Keys.All(...)` all four cases fail (4 failed of 49 in the class), reverted afterwards.
- **K1, `DerivedBaseColumns` omitted the grouped and filtered columns.** Added `sprk_matter` and `sprk_project` on `sprk_invoice` and on `sprk_budget` (the group keys and the `in` filter); every other attribute the queries name (amounts, `statuscode`, the submission columns) was already there, and the joins use primary keys (`sprk_invoiceid`, `sprk_invoicesubmissionid`) plus `sprk_invoicesubmission.sprk_invoice`. New test `K1_EveryColumnTheQueriesTouch_IsInDerivedBaseColumns` builds the counted, pending and budget FetchXml through the real builders for matter and project, collects every `attribute` / condition / `from` / `to` / group-by name per entity (resolving link aliases) and asserts each is listed or is that table's primary key; a control test proves the collector finds each reference kind and flags an unlisted one. Fail-before: with the four columns removed the new test and the pinned-set test in `DerivedBaseColumnsFieldSecurityTests` both fail. The offline `DerivedBaseColumnsFieldSecurityTests` reads SpaarkeMaster Entity.xml: all four new columns are present and unsecured, `NotYetInSolution` stays empty.
- **K2, pin.** `AuthorableColumnsTests.TheSeed_...` now asserts `AuthorableColumns` does not contain `(sprk_invoice, sprk_approvedtotal)`.
