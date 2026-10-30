# Task 049 (spend part) - D-102 held spend and D-104 Pending Spend

> Owner decisions: D-102 (held invoices do not count), D-103 (counted spend = Approved, Sent to AP, Paid; amends D-102), D-104 (Pending Spend).
> The pipeline half of 049 (job, processor, notification sink, FinanceModule) is a separate branch and is not touched here.

## What changed

| File | Change |
|---|---|
| `Services/Finance/FinanceBaseValues.cs` | Adds `PendingInvoiceStatuses` (In Review), `HeldSubmissionConditions` (the one held predicate), `PendingSpendColumn`, `BuildGroupedPendingSumFetchXml`, `ReadPendingSpendAsync`. `BuildGroupedSumFetchXml` now delegates to one private builder; its output is byte-identical (existing tests pass unchanged). |
| `Services/Finance/FinanceRollupService.cs` | `BuildPendingInvoiceQuery`, and `WriteRollupAsync`: the counted rollup (`BuildRollupFields`, unchanged apart from named arguments) is written first, then Pending Spend is read and written in its own PATCH (`BuildPendingSpendFields`). A pending failure is logged and leaves the stored value unchanged (review round F1; the first version put `sprk_pendingspend` in the counted PATCH). Counted spend (`BuildInvoiceQuery`) is unchanged. |
| `scripts/ebilling/Deploy-EbillingPendingSpendSchema.ps1` | Idempotent additive script: `sprk_matter.sprk_pendingspend` and `sprk_project.sprk_pendingspend` (Money, precision 2, -922337203685477 .. 922337203685477, the range of `sprk_totalspendtodate`; corrected in the review round). Parse-checked only. NOT run against any environment. |
| `docs/data-model/sprk_invoice-matter-project.md` | A row per parent for the new column. |
| tests | `SpendHeldAndPendingTests.cs` (new, 18 tests), `FinanceRollupPayloadTests.cs` (new argument, new assertion), `FinanceBaseValuesSingleDefinitionGuardTests.cs` (guard: no consumer spells the held predicate; 2 new controls). |

## D-102 (as amended by D-103): counted spend has no held predicate

A held invoice is In Review, and `CountedInvoiceStatuses` is Approved, Sent to AP, Paid, so a held invoice is excluded automatically on both paths. No predicate was added to counted spend (`D102_CountedSpendQueries_HaveNoHeldPredicate...` pins that: no link-entity in either counted query).

Tests that name D-102 (all in `SpendHeldAndPendingTests`):
- must-fire, rollup path: a held In Review invoice is excluded from `FinanceRollupService.BuildInvoiceQuery`'s result.
- must-fire, live path: the same row is excluded from `FinanceBaseValues.ReadAsync` (the Threshold input).
- must-not-fire, both paths: a blocked invoice is released (submission to Accepted, invoice to Approved) and is then counted.

Mutation check: adding `InReview` back to `CountedInvoiceStatuses` fails 7 of the 18 tests, including both must-fire tests and the must-not-fire test (the held row is admitted and the released-invoice assertion on the pre-release step fails first).

## D-104: Pending Spend

Pending Spend = sum of `sprk_invoice.sprk_totalamount` over the parent's invoices with `statuscode` in `PendingInvoiceStatuses` (In Review) that are not held.

### The held representation (settled with the pipeline agent and the coordinator; no schema change)

Held = an In Review invoice that has a linked `sprk_invoicesubmission` (`sprk_invoicesubmission.sprk_invoice` = the invoice) with `sprk_processingstatus` IN (Processing 100000001, Rejected 100000003) AND `sprk_outcomereasoncode` = `ebilling.submission.blocked`. Override (105) releases it by setting the submission to Accepted; Return moves the invoice out of In Review. (Processing was added by review fix R1, see "R1" at the end: D-130(a) leaves a blocked submission Processing until the after-steps' final write. Originally this was Rejected alone.)

Why: 020 gave the submission no Blocked status (Received, Processing, Accepted, Rejected, Failed), and no 044/045 column carries it. "Rejected + blocked reason code" is the state the Override / Return flow already needs (105's own constraint: "an internal-channel submission whose processing status is Rejected because of a blocking finding can be overridden"). Reading it needs a submission link in the query, which the task allows; no invoice flag was added.

The reason code is a literal (`FinanceBaseValues.BlockedReasonCode`) with `// TODO(049)` to swap for `SubmissionReasonCodes.Blocked` when the pipeline branch's `Submission/SubmissionReasonCodes.cs` is merged. The status uses `InvoiceSubmissionStatus.Rejected` directly.

### Double-count hazard and the anti-join

An invoice can have several linked submissions (resubmissions). A plain join in a sum would repeat the invoice amount once per linked row. Both paths use a LEFT OUTER link with BOTH held conditions inside the link, plus a where-clause null check on the linked key (`link-type="outer"` + `<condition entityname="held" attribute="sprk_invoicesubmissionid" operator="null"/>`; `JoinOperator.LeftOuter` + `ConditionOperator.Null` with `EntityName = "held"` in the QueryExpression). A linked row exists only when it is a blocking one, and that invoice is then excluded, so the invoice is counted exactly once or not at all. I chose outer + null over `link-type="not any"` because it is the long-standing documented anti-join and needs no newer FetchXML feature; `not any` would also work.

Proof: the test evaluator joins like SQL (one row per matching linked row), and `D104_AnInvoiceWithSeveralSubmissions_IsNeverCountedTwice_NorHalfCounted` shows: two non-blocking submissions -> counted once (7, not 14); one blocking + one not -> excluded (not 11, not partially counted); two blocking -> excluded. A control (`ANaivePlainJoin_WouldCountAnInvoiceOncePerLinkedSubmission...`) removes the conditions and the null check and gets 14, so the evaluator can see the hazard. Mutation check: dropping the null check fails 5 tests.

Limit of the evidence: there is no FetchXML or QueryExpression evaluator in the repo. The in-memory evaluator in `SpendHeldAndPendingTests` interprets exactly these shapes (documented in its remarks); it proves the query logic and the reader, not that Dataverse accepts the FetchXML. **Open: one read-only live check on dev** (run `BuildGroupedPendingSumFetchXml` through the operator client; the live seam test `FinanceBaseValuesSeamTests` is the place) should be run before 079 relies on it. I did not run one (no live calls were in scope).

### Fail-closed

`ReadPendingSpendAsync` reuses `ReadGroupedSums`: an unknown parent or sum shape throws `InvalidOperationException`, a Dataverse failure propagates, an unknown parent entity throws `ArgumentOutOfRangeException`. Tests: `D104_AnUnreadablePendingShape_Throws_NotZero`, `D104_ADataverseFailure_Propagates`, `D104_AnUnknownParentEntity_IsRefused`. A submission table that does not exist would make the query fail, not read as zero.

### Tests (D-104)

- must-fire: held In Review not in pending (both paths); Approved (and every non-In-Review status) not in pending (both paths).
- must-not-fire: unheld In Review counted as pending and not as actual spend (both paths); a submission with no blocked code or another code (Rejected or Processing), or Accepted / Failed / Received with the blocked code, does not hold its invoice. (This line used to say "Processing does not hold"; that predates D-130(a). R1 flipped it: a Processing submission WITH the blocked code now holds, a must-fire case, `D130a_AProcessingSubmissionWithTheBlockedCode_HoldsItsInvoice_OnBothPaths`.)
- shape: the pending FetchXML and QueryExpression are pinned against the shared constants; the guard in `FinanceBaseValuesSingleDefinitionGuardTests` flags any consumer that spells the submission columns or the reason code.

## §11 answers

**New `FinanceBaseValues` measure (`PendingInvoiceStatuses`, `HeldSubmissionConditions`, `ReadPendingSpendAsync`, `BuildGroupedPendingSumFetchXml`)**
- Existing: the counted-spend reader (`ReadAsync` / `BuildGroupedSumFetchXml`) and its status set; nothing else computes pending spend.
- Extension: the FetchXML builder is extended (one private builder now serves both), and `ReadGroupedSums` is reused unchanged; the held predicate cannot live in `ReadAsync` because counted spend must not carry it (D-103) and its return tuple is the evaluator's contract.
- Cost of doing nothing: the owner's D-104 (Pending Spend on both paths, Threshold-usable) would be unbuilt, and the rollup column and the live number would each grow their own copy of the held predicate and drift.

**New rollup columns (`sprk_matter.sprk_pendingspend`, `sprk_project.sprk_pendingspend`)**
- Existing: `sprk_totalspendtodate` holds actual spend only and `sprk_monthlyspendcurrent` is a different measure; no column holds In Review amounts.
- Extension: actual spend cannot be widened to include In Review (D-103 excludes it), and a computed-on-read value would not be visible on the matter form or usable as a stored column; so one additive Money column per parent, same shape as `sprk_totalspendtodate`, written by the existing rollup.
- Cost of doing nothing: the matter and project forms cannot show Pending Spend and there is nothing stored for the Threshold follow-up; the owner approved the additive column (D-104).

## Gates

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors, 0 warnings.
- Unit filter (Finance, Billing, Signal, Threshold, Ontology, Rollup, Budget): 5522 passed, 0 failed, 6 skipped (pre-existing skips).
- `tests/Spaarke.ArchTests`: 1209 passed, 0 failed.
- Publish size (`.claude/rules/bff-hygiene.md` item 4): baseline = `c342f4b9a` (the branch base, so only this change is measured), after = the commit before this note. Both built Release from fresh detached worktrees at `C:\wt049m` / `C:\wt049b`, zipped with `Compress-Archive` (Optimal), 192 files each. Before 39,746,211 bytes (37.905 MB), after 39,748,261 bytes (37.907 MB), delta +2,050 bytes. No package added. Worktrees removed with `Remove-WorktreeSafe.ps1`.

## Needs a decision or a sequencing step

1. **Deploy order.** Run the schema script (053 syncs SpaarkeMaster, 079 runs it, with the owner's OK) BEFORE this code is deployed to an environment. If the BFF arrives first, the counted rollup (total spend, budget, velocity, timeline) is unaffected: Pending Spend is a separate PATCH, so each recalculation logs one Error ("Pending Spend (D-104) not updated") and the stored pending value stays unchanged until the script runs (review round F1).
2. **`RecalculateFinanceResponse` is unchanged** (no `PendingSpend` field); the stored column is the only output. Add the field only if a client needs it.
3. Work assignment was not given a Pending Spend column (the task allowed matter and project only; work assignments have no counted-spend column either).

## For ontology-r1

Their follow-up (not done here; their files were not edited):

| Item | Value |
|---|---|
| Column logical name | `sprk_pendingspend` |
| Entities | `sprk_matter`, `sprk_project` (the same name on both) |
| Type | Currency (Money), precision 2, min -922337203685477, max 922337203685477 (the range of `sprk_totalspendtodate`: it stores a sum, so not the per-invoice 0 .. 1e9; corrected in the review round); stored, plain (not calculated or rollup) |
| Writer | `FinanceRollupService` only (stored copy; can be stale like `sprk_totalspendtodate`) |
| Live measure | `FinanceBaseValues.ReadPendingSpendAsync(entities, parentEntity, parentIds, ct)` -> `parentId -> decimal` (parents with none are 0). Constant: `FinanceBaseValues.PendingSpendColumn` |
| Held predicate | `FinanceBaseValues.HeldSubmissionConditions` (status Processing or Rejected + reason `ebilling.submission.blocked` on a linked `sprk_invoicesubmission`) |

To make a Threshold able to use it they need to: add `sprk_pendingspend` to `PolicyVersionValidator.AuthorableColumns`; add its range to the `PredicateCompiler` ranges (±922337203685477, Currency, like `sprk_totalspendtodate`); add a `RuleBodyDescriber` label ("Pending spend"); and, if the Threshold is to read it from base rows instead of the stored copy (the D-122 rule), add it to `FinanceBaseValues.DerivedColumns` / `Needs` / `ValueOf` and make `SignalEvaluator` call `ReadPendingSpendAsync` (the current `ReadAsync` tuple was left untouched on purpose). Until then the column is stored but unusable in a rule, which is safe. `FinanceBaseValues.IsDerived` deliberately returns false for it today.

Range to send them: the commit(s) on branch `worktree-agent-a4620f6f07c910055` from `c342f4b9a` (see the report).

## ontology-r1 review round (2026-10-19)

ontology-r1 reviewed `be84b2a8f`. The counted spend and held/pending logic held. The old and new counted FetchXML are byte-identical across 12 combinations. Its mutations were all caught: dropping the null check, moving the reason condition to the outer filter, and adding In Review to the counted set.

| Finding | Fix |
|---|---|
| F1: `sprk_pendingspend` was in the single rollup PATCH, and the pending query (joined to `sprk_invoicesubmission`) ran in the same `Task.WhenAll`. On an environment the BFF reaches before the schema script, every recalculate failed (500), none of the ten stored fields updated, and `SpendSnapshotGenerationJobHandler` retried and then poisoned the job. | `FinanceRollupService.WriteRollupAsync` writes the counted rollup first. A failure there still propagates (404 / 500 as before). Then it reads Pending Spend and writes it in its own PATCH (`BuildPendingSpendFields`). A failure to read or write pending is logged as an Error and leaves the stored value unchanged, never 0. Tests in `FinanceRollupPendingSpendWriteTests`: a pending query failure or a pending write failure still writes the counted fields; a counted failure propagates and pending is not attempted; cancellation is not swallowed. Mutations (ontology-r1's count): catching cancellation only fails 3 of the 5; removing the catch fails 2; swallowing cancellation too fails 1; writing 0 on failure fails 2. A legitimate 0 is pinned by `BothWrites_Succeed_WithZeroPending_WritesZero` (re-check F3): skipping the write at 0 would leave a matter showing a stale amount forever. The catch filter is `!ct.IsCancellationRequested` (re-check K): an HttpClient timeout on the pending PATCH, a `TaskCanceledException` while the caller's token is not cancelled, is contained like any other pending failure. |
| F4: the column used the shared `New-MoneyDef` range (0 .. 1e9, the per-invoice `sprk_totalamount` range), but it stores a sum | The script overrides the range to ±922337203685477, as on `sprk_totalspendtodate`. The data-model rows, script header and "For ontology-r1" range are corrected. The script had not run anywhere. |
| K6: `BuildRollupFields` took `totalSpend, pendingSpend, ...` positionally | Pending is no longer in that payload, and the call uses named arguments. `"sprk_invoiceid"` is now `FinanceBaseValues.InvoiceKeyColumn`, used by both builders. |

**Known limits:**
- **K1, resolved for the query shapes.** The read-only seam case `FinanceBaseValuesSeamTests.PendingSpendQueries_AreAcceptedByDataverse_ReadOnly` ran against spaarkedev1 on 2026-10-19 and passed. It sends both pending shapes, for matter and project, with ids that match no row, and creates nothing. The aggregate FetchXML (with `entityname` on a root condition) and the rollup's QueryExpression are both accepted, and the reader reads the result. The anti-join's semantics on real rows are pinned by the unit tests only. A live semantic case needs created submissions, which is a live write needing the owner's OK.
- **K2, fail open for a user identity.** The outer join applies row security to the user-owned `sprk_invoicesubmission`, so a caller who cannot read the blocking submission would see a held invoice counted as pending. `ReadPendingSpendAsync`'s doc comment now says to call it only with an application identity. There is no user-identity caller; ontology's follow-up uses the evaluator's app identity.
- **K3, the reason-code literal.** `FinanceBaseValues.BlockedReasonCode` stays the literal `"ebilling.submission.blocked"` (`TODO(049)`) until 049 part B creates `SubmissionReasonCodes.Blocked`. Part B swaps it so there is one definition.
- **K4, pending briefly wrong or stale.** A rollup that runs between 049 step 6 (the invoice committed In Review) and the blocked reason code being written counts a soon-to-be-held invoice as pending (since R1 that window is gone for a blocked outcome: the persist transaction writes the code with the invoice, and a Processing submission carrying it is already held). Override and Return do not trigger a recalculation, so pending stays stale until the next rollup. To revisit with 105, which could trigger a recalculation on release.
- **K5, by design.** In Review is statuscode 1, the default, so every invoice created without an explicit status counts as pending. This matches D-103 / D-104.
