# Task 224 notes: deleted-rule evidence redaction covers every empty-lookup Signal over successive runs (#2085, DI-116)

## Cause
The nightly job's deleted-rule redaction (task 102b fix round 4) finds the codes of deleted policies with
`SignalClosureService.DeletedPolicyCodesWithSignalsAsync`: every `sprk_signal` with an empty `sprk_policy`, any status, ordered by
`sprk_signalid`, at most `SignalEvaluator.MaxPages` (200) pages of `PageSize` (500). `SignalReevaluationJob.RedactSignalsOfDeletedPoliciesAsync`
sweeps those codes (`SweepStoredEvidenceAsync`) and, when the read hit the bound, failed the run. Nothing deletes Signals, so past 100,000 such
Signals every run started at the same first row: codes whose Signals sort later were never redacted, and every run failed (the
`/healthz/catalog` staleness check turns Unhealthy after 26 h).

Line numbers below are at origin/master 1b89025736.

## Options checked
- **(a) Rotating cursor** (`sprk_signalid` greater than the last id read), kept between runs and wrapped to the start when a pass ends. **Chosen.**
- **(b) Read only the empty-lookup Signals that still carry stored detail** (task 217's `SignalSuppressionService.AddStoredDetailFilter`,
  `"text"`/`"source"` LIKE). Rejected as the fix, for two reasons:
  1. It does not bound the space. A Signal whose witness stays in its own family keeps its detail for good (task 217 K4: the rule must re-check
     it every night, because a witness can be re-filed into a secure family any day). The detail-carrying empty-lookup Signals can still pass
     100,000, and the same first-rows stall comes back.
  2. It changes which codes the deleted-policy sweep names. The per-code sweep reads by dedupe-key PREFIX, so a code is swept for every Signal
     carrying it, lookup or not. Filtering the discovery read would stop naming a code whose orphan Signals hold no detail while other Signals
     under the same reused code do. That is the escalation trigger ("changes which evidence is redacted"), and task 217 had to reason about the
     same interaction.
  (a) keeps the discovery predicate and the per-code sweep exactly as they are; it only changes WHERE in the id order a run starts.

## Design
- `SignalClosureService.DeletedPolicyCodesWithSignalsAsync(existing, ct, Guid? after = null)` (~634): when `after` is set, adds
  `sprk_signalid` GreaterThan `after` to the same query (same columns, same `sprk_policy` Null condition, same order, same paging cookie, same
  bound). The returned `DeletedPolicyCodes` record keeps its positional shape and gains two init properties: `SignalsRead` (rows returned) and
  `ResumeAfter` (the last row's id, set only when the bound was reached). Each row's id is taken before the defence-in-depth `sprk_policy`
  skip, so a skipped row still moves the cursor.
- `SignalReevaluationJob.RedactSignalsOfDeletedPoliciesAsync` (~531, called ~147):
  1. reads the cursor (`DeletedPolicyRedactionCursorKey`); missing, unparseable, the empty id, or a cache fault (logged at Warning) all
     mean null, so the read starts at the start;
  2. reads the codes after the cursor; a read fault counts a policy failure, is logged at Error as before, and leaves the cursor where it was,
     so the next run reads the same window again;
  3. sweeps the codes (`SweepStoredEvidenceAsync`, unchanged: same rule, same skip of codes already attempted this run, same
     one-failure-per-policy accounting);
  4. bound reached: saves `ResumeAfter` (7-day lifetime) and logs a Warning with the counts: pages × page size, Signals read, where it
     started, deleted codes found, and the id it continues after. **The run is not failed.** If the cursor cannot be saved, the next run would
     read the same Signals again, so that run is failed (policy failure) and logged at Warning, as before #2085;
  5. end reached after a cursor (a pass finished): clears the cursor so the next run starts at the start (Information). If the clear fails, the
     next runs would read only the Signals after it until it expires, so the run is failed and logged at Warning.
- A run with no cursor that reaches the end does exactly what it did before: one read from the start, no cache write and no remove. The only
  added call is the cursor read.

### Where the cursor lives, and why
In the job's existing `IDistributedCache` (Redis; ADR-009), under `sprk:ontology:evaluator:deleted-policy-redaction-cursor`, beside the
job's `LastSuccessCacheKey`. The job already takes the cache, so no DI change. It is the idiom `TodoAssigneeOwnerReconciliationJob` already
uses for its nightly scan cursor (`CursorKey`/`ReadCursorAsync`/`WriteCursorAsync`/`ClearCursorAsync`, also with an absolute lifetime). The
cache is acceptable here **only** because losing the cursor is safe: a lost, evicted, expired or unreadable cursor starts the read at the
start. That repeats work and never skips a row. The job run store was not used: `JobRunContext` does not expose the previous run's result to
the job, and adding that would be new surface.

### Why every row is covered
- Within one pass the windows tile the id order: run k reads ids greater than run k−1's last id read, up to the bound, and run k+1 continues
  after run k's last id. Dataverse evaluates the `gt` condition and the `ORDER BY` in the same (SQL `uniqueidentifier`) order, so a window
  neither skips nor repeats ids. This is the same pairing the existing id-cursor reads rely on (#2982 `ReadPolicyPagesAsync`,
  `TodoAssigneeOwnerReconciliationJob`).
- A Signal that exists when a pass starts is read in that pass. A Signal added during a pass (a new id, or an existing Signal whose lookup is
  emptied by a policy delete) is read in that pass if its id is after the cursor, otherwise in the next pass.
- A pass takes ceil(N / 100,000) runs. With no wrap inside a run, a run that reaches the end after a cursor stops there; the next run starts
  the new pass.

### A code whose sweep fails inside a window
The cursor still advances. The failure fails the run and is logged with the code, as before. The code is swept again when the pass comes
back to a window holding one of its Signals (at most ceil(N / 100,000) runs). Holding the cursor on any sweep failure would bring back
#2085's stall under one persistent per-code fault: every later code would wait behind it.

## Tests
New file `tests/unit/domain/Signals/SignalReevaluationJobTests.DeletedPolicyRedactionCursor.cs`. It runs the REAL job, closure service,
suppression service and writer over the job harness, with one row per page, so the bound is 200 rows:
- `Run_MoreSignalsWithNoPolicyThanOneRunReads_TwoRunsTogetherRedactEveryDeletedCode_AndTheThirdStartsOver` (must-fire). 201 Resolved
  empty-lookup Signals. The first by id belongs to deleted code A and still quotes a witness that has since moved behind a secure wall. The last
  by id, past run 1's bound, belongs to deleted code Z with the same detail. The other 199 are A's, already redacted.
  - Run 1 reads from the start, redacts A's Signal, leaves Z's, saves the 200th id, warns with the counts and succeeds.
  - Run 2 reads only after that id, sweeps only Z, redacts it to the exact expected JSON, clears the cursor and succeeds.
  - Run 3 starts at the start and writes nothing. The 199 already-redacted Signals are never written (row versions unchanged).
- `Run_UnderTheBound_ReadsFromTheStart_LeavesNoCursor_AndRedactsAsBefore` (must-not-fire): one read with no id condition, no cursor written,
  the same redaction, no bound warning.
- `Run_ACursorThatCannotBeRead_StartsFromTheStart_AndSkipsNothing` (garbage, empty id, cache read fault; must-not-fire).
- `Run_ACursorPastEverySignal_ReadsNothingThisRun_ClearsIt_AndTheNextRunStartsOver` (must-not-fire: a stale cursor skips nothing).
- `Run_TheBoundReached_AndTheCursorCannotBeSaved_IsAFailedRun_ThatStillRedactsWhatItRead` (no progress means a failed run).
- `Run_APassThatEnds_ButTheCursorCannotBeCleared_IsAFailedRun`.
- `Run_TheCodeReadFaultsAfterACursor_KeepsTheCursor_AndTheRunFails` (fail closed, unchanged; the cursor is kept).
- `DeletedPolicyCodes_AfterACursor_ReadsOnlyLaterSignals_AndNamesWhereToContinue` (service level: `SignalsRead`, `ResumeAfter`).

Harness, additive only (`SignalReevaluationJobTests.cs`): `Harness(cache:)` takes an optional `IDistributedCache` (default unchanged).
The fake's `Holds` models `GreaterThan` on the row's primary id, in the same order its `Paged` sorts rows; any other operator still throws.

### Existing test changed (justified)
`SignalReevaluationJobDeletedPolicyTests.Run_R4_MoreOrphansThanOneRunReads_ClosesTheBound_WarnsMoreRemain_AndLeavesTheRestForTheNextRun`
asserted that the redaction's bound fails the run, with the old warning text. That is the exact behaviour acceptance criterion 3 removes: the
bound is now reported and the run succeeds. Two assertions changed: the Warning now matches "the next run continues after Signal", and
`Success` is now true. Every close assertion in it is unchanged. The old "was not checked" text is still asserted, in the new can't-save test.
Every other existing test passes unmodified.

### Fail-before (origin/master's two source files swapped in)
The new file does not compile against master (it uses the cursor key and the new parameter and properties), so the proof ran on a
master-compatible copy of the must-fire test.
- With every assertion kept, it fails on run 1: `Success` is false and `policyFailures=1`.
- With the success, cursor and log assertions removed, it fails on run 2: code Z's evidence still holds the witness's `text` and `source`
  after two runs. That is #2085.
- The changed R4 test fails on master because the new Warning is missing.
After restoring the branch's files, every new and changed test passes.

### Mutation checks (scratch edits to `SignalReevaluationJob.cs`, each built, run and restored from a byte copy)
| Mutation | Tests that fail |
|---|---|
| The cursor is read but not passed to the read (`after` replaced with null) | 4 (must-fire, stale cursor, clear-fails, read-fault) |
| The cursor is never cleared at the end of a pass | 3 (must-fire, stale cursor, clear-fails) |
| A cursor-save failure does not count a policy failure | 1 (`…CannotBeSaved…`) |

## Placement and §11
- Placement: existing `SignalClosureService` (one optional parameter, two init properties on an existing internal record) and
  `SignalReevaluationJob` (the call site, one const, one lifetime, three private cache helpers). Nightly `IScheduledJob` path (ADR-036),
  unchanged. No endpoint, service, interface, DI registration, package, column or schema change.
- §11: **Existing**: the job's `IDistributedCache` (already injected; `LastSuccessCacheKey`), the id-cursor idiom of
  `TodoAssigneeOwnerReconciliationJob` and #2982's `ReadPolicyPagesAsync`. **Extension**: the existing read gains an optional cursor, and the
  job reuses its own cache. The three small cache helpers copy `TodoAssigneeOwnerReconciliationJob`'s private ones; sharing them would mean
  editing that job, which is outside this task (see Known limits). **Cost of doing nothing**: past 100,000 empty-lookup Signals, deleted codes
  sorting later are never redacted (D-123 evidence stays visible) and every nightly run fails.

## Known limits
- K4: the three cursor helpers duplicate `TodoAssigneeOwnerReconciliationJob`'s private `ReadCursorAsync`/`WriteCursorAsync`/`ClearCursorAsync`
  (about 40 lines). Extracting a shared helper would touch that job, owned by another lane's work. A candidate for a later cleanup; no
  behaviour depends on it.
- K2: a run that reaches the end after a cursor does not wrap within the run to use the rest of its bound. A pass therefore takes
  ceil(N / 100,000) runs plus at most one, never fewer rows than needed.
- K2: an unparseable value left in the cursor key is read as "no cursor" every run until it expires. Only something other than this job could
  write it, and it never skips a row.
- K4: real Dataverse cost of the `gt` condition on the primary key is not measured; it is the clustered-index seek the other id cursors
  already use.

## Results
- BFF build (`-warnaserror`): 0 warnings, 0 errors.
- `--filter "FullyQualifiedName~SignalReevaluationJobTests"`: 332 pass / 0 fail.
- `--filter "FullyQualifiedName~Signals"` (includes the contract baselines): 5,530 pass / 0 fail / 6 skipped (pre-existing).
- `tests/Spaarke.ArchTests` (built explicitly, full): 2,831 pass / 0 fail.
- Publish size (Release, short paths: `C:\wt224m` = fresh origin/master 1b89025736, `C:\wt224` = this branch; `Compress-Archive`; 194 files
  each): master 43,619,662 bytes, branch 43,623,242 bytes, **+3,580 bytes**.
- No package or csproj change, so no new CVE surface.
- Conflict check: no open PR and no `work/ebilling-r1` change touches `SignalReevaluationJob.cs` or `SignalClosureService.cs`. Task 223 edits
  another site of `SignalReevaluationJob.cs` (~962); merge master first if it lands before this.
- Self-review (Step 9.5; no sub-agents in this lane): no F-class finding left. ADR-009 (a loss-safe cursor in the existing cache, as
  `TodoAssigneeOwnerReconciliationJob`); ADR-036 (job and schedule unchanged); ADR-038 (tests under the `tests/unit/domain` KEEP path, one
  existing assertion pair changed and justified above, nothing deleted); §11 (private/internal members in existing types). Independent
  adversarial review: for the main session.

rework: none task 224
