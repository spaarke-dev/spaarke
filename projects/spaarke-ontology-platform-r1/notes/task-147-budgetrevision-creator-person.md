# Task 147 — budget revision records its requester (D-135 Q27, #1777)

## What changed
- `ReviseBudgetExecutor.PlanAsync` sets `RequestedBy = RecordRequester.Of(userId)` on the owner context. `userId` is the acting user's systemuserid, already resolved by the plan (it refuses `CallerUnresolved` before the owner is asked when there is none). `RecordOwnerResolution.ApplyTo` then stamps `sprk_createdbyperson` because `sprk_budgetrevision` is now in `RecordCreatorPerson.StampedChildTables`.
- `sprk_budgetrevision` added to `scripts/Set-ChildRecordCreatorPersonSchema.ps1` `$Tables` in the same commit (`ChildRecordCreatorPersonSchemaAgreementTests` pins both).
- UAC 182's `CreatorPersonColumnPending` exemption and its negative-control case are removed. `MissingCreatorPersonColumn(created, stamped)` has no exemption; a new must-fire control (`CreatorPersonColumn_NegativeControl`) shows an unstamped created table (including the former exemption's table) is reported.
- The script's relationship check accepts `{table}_CreatedByPerson_systemuser` (here `sprk_budgetrevision_CreatedByPerson_systemuser`) as the same relationship, and checks a table listed in `$SolutionHomes` (`sprk_budgetrevision`) against SpaarkeMaster and OntologyPlatformSolution, read-only, instead of SpaarkeCore.

## Decisions / judgement calls
- **Interpretation of the name form.** D-135 writes `sprk_{table}_CreatedByPerson_systemuser`. With `{table}` the bare table name, that is `sprk_budgetrevision_CreatedByPerson_systemuser`, i.e. `"$($Table)_CreatedByPerson_systemuser"` with the logical name. A double-prefixed `sprk_sprk_...` is not a form the maker produces, so it is not accepted. Not verified live (no environment access allowed): if the owner's actual name differs, `-Verify` reports the pre-existing "created another way" FAIL naming both accepted forms, so the first live run will say so.
- **Criterion 2 (no resolvable acting user).** Today's behaviour is a refusal (`CallerUnresolved`) before the owner is asked and before any write; that is kept, and pinned by a test. The "records no person, no throw" case that can actually occur is a requester the resolver cannot name (it returns `CreatedByPerson = null`); pinned by a second test.
- **Added beyond the stated contract (one-line justification).** `PersonBearingWriters` gains `DecisionActionExecutors.cs` / `PlanAsync` so a dropped `RequestedBy` reddens the person-coverage guard, not only the unit test. `$SolutionHomes` is a per-table map, not a global rule, so every other table keeps its SpaarkeCore requirement; 142/143 add their own entries (independent lines).
- **Bug found by the simulation.** `$target = if (...) { $emptyList }` unrolls an empty list to `$null` in PowerShell; the first draft of section (d) crashed on it. Caught by the simulated `-Verify` Pester run, fixed.

## Verification
- Script not run against any environment. Verified by (a) Pester tests that read the shipped functions out of the script's AST and (b) a simulated whole `-Verify` run (az and Invoke-RestMethod shadowed): passes with the maker name and the two home solutions; fails (exit 1, MISSING named) when OntologyPlatformSolution lacks the column; a name the script does not know still FAILs for a hand check.
- Tests: see the PR body for counts.

## Left open
- uac-r2 owns the census guard: ask them to review the removal of `CreatorPersonColumnPending` (main session to relay; uac-r2 is not addressable from this lane).
- Live `-Verify` on spaarkedev1 (owner/operator): confirms the relationship name and the SpaarkeMaster/OntologyPlatformSolution membership.
- 142/143 add `sprk_decisionrecord` / `sprk_signal` to the same two lists and to `$SolutionHomes`; rebase conflicts, if any, are one-line.
