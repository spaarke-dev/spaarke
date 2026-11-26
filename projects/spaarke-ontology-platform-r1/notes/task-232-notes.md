# Task 232: a finance Threshold Signal recalculates the record's stored roll-up (#1767, #1663; D-230)

Branch `fix/ontology-232-threshold-rollup-recalc` from `origin/master` at `c438119bca`. Rigor FULL (BFF `.cs`). Ran on Opus 5.5 at effort high (POML: opus @ high).

## Step 1: how Threshold rules read spend on master (POML question)

Answer: **computed, never the stored columns.** #1663 (2026-10-11) predates D-122 / PR #1756; #1767 is the current state.

- `PredicateCompiler.ThresholdColumns` (`PredicateCompiler.cs`, `ThresholdColumnRanges`) is exactly the six columns `sprk_totalspendtodate`, `sprk_totalbudget`, `sprk_budgetutilizationpercent` on `sprk_matter` and `sprk_project`. `CompileThreshold` marks each `BaseDerived` through `FinanceBaseValues.IsDerived`, which is true for all six.
- `SignalEvaluator.EvaluateAsync` (task 025b) removes the stored-column condition (`RemoveThresholdCondition`), does not select the stored column, and judges spend and budget from the invoice and budget rows (`FinanceBaseValues.ReadAsync`); the fact and the sentence carry the computed value.
- A `when` clause on any of the three derived columns is refused at compile time for every rule type (`PredicateCompiler.cs`, the `IsDerived` check in the `when` compile).
- So no Threshold rule reads a stored roll-up column, and nothing had to be switched to `FinanceBaseValues`.

The stored columns are plain columns (SourceType 0) written only by `FinanceRollupService` (manual recalculate routes, the subgrid web resource, `SpendSnapshotGenerationJobHandler`, which nothing enqueues). Hence #1767: the Signal states the computed figure while the form shows a stale stored one.

## What changed

1. `SignalEvaluator.cs`: `SignalEvaluation.DerivedThresholdField` (init property; set from the compiled `ThresholdComparison` when it is base-derived). Not near `AssigneeColumnBySubject` (task 227 / PR #3287).
2. `SignalReevaluationJob.cs`: takes `IServiceScopeFactory` (the job is a singleton, `FinanceRollupService` is scoped). In `ApplyAsync`, each CREATE that returned `Created` for a policy whose evaluation has a derived Threshold field on `sprk_matter` / `sprk_project` adds the subject to a set; after the creates, `RecalculateFinanceRollupsAsync` calls `FinanceRollupService.RecalculateMatterAsync` / `RecalculateProjectAsync` once per subject. A fault (anything but the caller's cancellation) is logged as a Warning with the exception; the Signal stays raised, the run outcome is unchanged.
3. `FinanceRollupService.cs` (ebilling-r1's): `WriteRollupAsync` takes an optional `readStored` delegate; each of its two PATCHes (counted rollup, Pending Spend) is skipped when the parent already holds the values (`IsUnchanged`: amounts within 0.005, the columns' Accuracy 2; ints equal; text ordinal). `RecalculateForEntityAsync` passes a reader (`ReadStoredAsync`, application identity `IDataverseService.RetrieveAsync`, the columns of that PATCH only) that answers null on any fault, in which case the write goes ahead exactly as before. The computation is untouched. This applies to every caller (routes, subgrid, snapshot job): fewer writes, same responses.

## Which writes trigger a recalculation (D-61)

- Yes: a first raise (episode 1) and a re-raise (episode n+1: after Superseded/PolicyRetired, after a D-120 re-crossing, a re-file to another core), from the nightly pass, the event triggers (task 032) and the admin publish's `ReevaluatePolicyAsync` (all go through `ApplyAsync`).
- No: an open Signal left unchanged (the planner writes nothing for it), a close, a cleared-observed stamp, a dedupe-race reconcile (`Created` false: another pass raised it and recalculated), a writer skip, any non-finance rule, any record other than the raised subject.
- "Update" in D-230: the job never rewrites an open matter or project Signal (they have no due-date column, so `Reconciles` is always empty for them); every change to such a Signal is a new episode, which is a raise and recalculates.
- Identity: the BFF application identity, the one `FinanceRollupService` already writes with on its routes (`IFieldMappingDataverseService` → `DataverseWebApiService`). No new privilege, no schema change, no new job or timer.

## Tests (fail before → pass after)

- `tests/unit/domain/Signals/SignalReevaluationJobTests.FinanceRollup.cs` (9): utilization raise on a matter recalculates exactly that matter and not the one under the threshold; spend raise on a matter; utilization raise on a project recalculates the project; re-raise (D-120 re-crossing) recalculates; must-not-fire: unchanged open Signal, a close, a non-finance (Path B Existence) raise; a recalculation fault leaves the Signal raised, the run successful and logs a Warning with the exception; `RollupSubjectOf` table.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/FinanceRollupUnchangedWriteTests.cs` (10): stored values equal → no PATCH; one value differs → counted PATCH only; stored Pending Spend empty → writes 0; stored read fails → both PATCHes as before; parent gone still surfaces `KeyNotFoundException`; `IsUnchanged` precision/type cases.
- Fail-before proof: with the two production hooks disabled (the recalculation call and the stored reader), 9 of the 19 new tests fail (every positive case); the must-not-fire controls pass both ways, as they should.

## Known limits (K-class)

- K4: an open finance Threshold Signal keeps the figure it was raised with; when spend moves while it stays open, neither the Signal nor the stored roll-up is refreshed by the job (planner: unchanged, D-61). The form agrees with the Signal at raise time. Pre-existing Signal behaviour, not changed here.
- K2: enabling a spend policy on many records raises many Signals in one pass, so that pass recalculates each raised record once (bounded by the raises; unchanged values are not rewritten). Not a nightly mass write: no unchanged record is touched.
- K4: a Money column on an environment whose currency precision is above 2 could treat a sub-cent change as unchanged (tolerance 0.005).
- `FinanceRollupService` counts only invoices with the writer's approve record (`CountedInvoiceStatuses` / `InvoiceApproveRecord`); on dev it reads spend 0 for most matters (ebilling-r1 comment on #1663). Same computation as the evaluator, so the form and the Signal agree.

## Found, not fixed (D-106)

- See the PR / completion report.

rework: none task 232
