# Task 034 - suppression per (policy, core record), 30 days: progress and decisions

Lane: `task/ontology-034-suppression` (worktree `C:\wts-034`), branched from `origin/docs/ontology-platform-design` @ `c6845f474`.
Rigor FULL (POML), effort high. The POML status, TASK-INDEX and `current-task.md` are NOT edited here: the main session does
completion after review.

## What was built

| File | What |
|---|---|
| `Services/Signals/SignalSuppressionService.cs` (new) | The ONE counter and stamp. `ApplyAsync` (count the grain, stamp `sprk_suppresseduntil` at 3), `ReconcileAsync` (nightly backstop, called by the 031 job), `GetStatusForSignalAsync` (the read). `SuppressionGrain`, `SuppressionStatus`, `SuppressionEvents` (50340/50341) |
| `Api/Signals/DecisionPlanEndpoints.cs` | `GET /api/v1/signals/{signalId}/suppression` beside the decision-plan route (already a governed file, so the route-file count is unchanged); the 403 body extracted to one private helper |
| `Services/Signals/SignalReevaluationJob.cs` | Shared with 031/033: constructor takes the service; ONE call after `ReadPolicySignalsAsync` hands the planner the stamps (`ReconcileAsync`); its failures count as policy failures so the run is not "healthy" |
| `Infrastructure/DI/SignalsModule.cs` | `AddSingleton<SignalSuppressionService>()` |
| `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` | One `HandlerDecision` for the new route (uac-r2-owned file: needs their review; flagged in the PR) |
| `tests/unit/domain/Signals/SignalSuppressionTests.cs` (new) | 31 tests over an in-memory store that evaluates the real queries |
| `tests/unit/domain/Signals/SignalReevaluationJobTests.cs` | Harness constructs the job with the new dependency (one line) |

## Decisions

1. **Where the count comes from.** Signals of the grain with `sprk_resolutiontype = Dismissed` (this is what keeps an auto-closure
   out: `ConditionCleared`/`Superseded`/`PolicyRetired` are different values), joined in code to the Signal's Decision Record
   (`sprk_signal.sprk_decisionrecord`) for class, reason and `sprk_decidedon`. The POML says "over Decision Records of class
   Dismissal"; the Decision Record does not carry the no-core item (D-39), so the Signal side is the grain and the record
   supplies class/reason/time. The escalation trigger (cannot group by) does not fire: the generic core pair is on the Signal and on
   the record, and the item is on the Signal's dedupe key.
2. **One review = one dismissal.** A review that also resolves other Signals writes one record; the count is of distinct Decision
   Records. A Dismissed Signal with no record counts as its own dismissal (no reason to exclude it).
3. **Window and stamp.** Counted dismissals decided in the trailing 30 days; at 3 the stamp is the most recent counted dismissal
   plus 30 days ("30 days from the third dismissal"). It lapses on its own; the dismissals that made it are then out of the window.
   So there is no "4th dismissal after lapse suppresses at once" case.
4. **Stamped rows.** The grain's open Signals (hidden from the worklist) and its dismissed Signals in the window (what the 031
   planner reads). Only rows whose stamp changes are written (D-61); a second `ApplyAsync` writes nothing.
5. **Reason code.** The text before " - " in `sprk_reason`, looked up in 036's `DecisionActionCatalog.DismissalReasons` of both lanes
   for `CountsTowardSuppression = false`. An undeclared code COUNTS (only a declared non-counting code is excluded). A policy with
   `sprk_countstowardsuppression = No` counts nothing; its dismissals show in `DismissalsRecorded`.
6. **Quiet window (FR-17a) not implemented here.** It is 031's `SignalEpisodePlanner` re-raise rule (POML constraint).
7. **Backstop in the nightly job.** `ReconcileAsync` only queries a grain with at least 3 dismissed Signals in the window and no live
   stamp; after stamping, the grain costs nothing. A failure is logged (50341), metered (`suppression_failed`) and fails the run, but
   the planner still receives the in-memory stamp so a pairing at the threshold does not resurface that pass.
8. **Read route.** Keyed by Signal (the wizard has one): `SignalCoreRecordAccess.AuthorizeAsync` decides as the caller (matter or
   any core type readable, no-core owner-only in the Do lane); only counts and the date are returned; an unreadable count is the
   uniform 404.

## Hand-offs

- **Task 043 (commit route):** after it closes the dismissed Signals (step 6), call
  `SignalSuppressionService.ApplyAsync(policyId, policyCode, grain)` so the effect is immediate instead of next night. Build the grain
  from the Signal's `sprk_corerecordtype`/`sprk_corerecordid` (`SuppressionGrain.ForCore`) or, with no core, its subject
  (`SuppressionGrain.ForItem`). Treat a failure as non-fatal to the commit (the record is written): the nightly backstop retries.
- **Tasks 050 / 038 (worklist):** hide a Signal whose `sprk_suppresseduntil` is in the future (already a column on the row).
- **Tasks 058 / 100:** read `GET /api/v1/signals/{signalId}/suppression` -> `dismissalCount`, `dismissalsRecorded`, `threshold`,
  `windowDays`, `suppressedUntil`, `countsTowardSuppression`.

## Known limits (K class)

- A stamp written while a policy counted stays until it lapses if the policy is later switched to `countstowardsuppression = No`
  (K2: at most 30 days, then it expires).
- A Resolved (Acted) Signal carrying a stamp is not read by the status call (K2: the stamp is only ever written on open or dismissed rows).

## uac-r2 coordination (POML constraint)

`origin/master` @ `848f63913`. Open uac-r2 PR: #1639 (thread anchor) - no overlap. Reused, not rebuilt: `SignalCoreRecordAccess`
(036) over `IDataverseUserClient`. Not touched: `SecureChildLineage.cs`, `secure-record-owner-role.json`, `RecordOwnershipResolver`,
`RecordRouteAccessAuthorizationFilter`, `CallerRecordAccessProbe`, `ExternalCallerContext`, `CoreAncestorResolver`. One uac-r2-owned
file edited: the route ledger (`RouteAuthorizationGuardTests.Ledger.cs`), one `HandlerDecision` entry, no count change.
