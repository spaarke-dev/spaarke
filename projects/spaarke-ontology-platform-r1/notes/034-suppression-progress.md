# Task 034 - suppression per (policy, core record), 30 days: progress and decisions

Lane: `task/ontology-034-suppression` (worktree `C:\wts-034`), branched from `origin/docs/ontology-platform-design` @ `c6845f474`.
Rigor FULL (POML), effort high. The POML status, TASK-INDEX and `current-task.md` are NOT edited here: the main session does
completion after review.

## What was built

| File | What |
|---|---|
| `Services/Signals/SignalSuppressionService.cs` (new) | The ONE counter and stamp. `ApplyAsync` (count the grain, stamp `sprk_suppresseduntil` at 3), `ReconcileAsync` (nightly backstop, called by the 031 job), `GetStatusForSignalAsync` (the read). `SuppressionGrain`, `SuppressionStatus`, `SuppressionEvents` (50350/50351) |
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
7. **Backstop in the nightly job.** `ReconcileAsync` only queries a grain with at least 3 DISTINCT dismissals (Decision Records) in the window and no live
   stamp or a row missing it (partial-stamp repair); a policy that does not count is skipped; a fully stamped grain costs nothing. A failure is logged (50351), metered (`suppression_failed`) and fails the run, but
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

- K1: a failed grain read or stamp write leaves that group unsuppressed (the stamp is applied in memory for the planner on a failed write) for that pass; the run fails visibly (policy failure + `suppression_failed` metric, EventId 50351) and the next night retries, including a partly written stamp.
- K2 (uac-r2 review): Decision-Record facts behind the caller's own readable rows are aggregated, so the counted / recorded split tells a caller a counting reason from a non-counting one on records Console users cannot read. Aggregate numbers on the caller's readable grain only; accepted as designed.
- K3: in the Do lane a lapsed suppression re-raises only when the due date has changed (031's rule: an unchanged date is `dateUnchanged`).
- A policy switched to `countstowardsuppression = No` has its live stamps withdrawn (cleared) by the next nightly pass or the next `ApplyAsync` on the record (#1661 F1, superseding the earlier "stays until it lapses" limit).
- A Resolved (Acted) Signal carrying a stamp is not read by the status call (the stamp is only written on open or dismissed rows).
- Cross-wall stamp (uac-r2 K1): fixed by #1661 (owner decision D-123, option A; uac-r2 audit F6). Every grain is split by `SuppressionPartition`: ordinary, one partition per SET of secure roots, or the fail-closed undetermined bucket. A Signal's partition comes from one helper (`SignalSuppressionService.PartitionsAsync`): `ownerid` is the Secure Record owner team, OR uac-r2's `SecureRootsAboveAsync` finds secure roots above the Signal row or its subject row (union). Used by `ApplyAsync`, `ReconcileAsync`, the count read and the 031 planner. The planner places each hit's NEW episode from the same truth (`PartitionsOfHitsAsync`), never from the previous episode; the nightly reconcile runs before the planner. A hit on a stamped record whose partition is unknown is skipped and counted (`partitionUnresolved`). A live stamp a partition's own dismissals do not justify (a move, a pre-#1661 cross-wall stamp, a policy switched to No, the undetermined bucket) is lowered or cleared, nightly and in `ApplyAsync`.
- #1661 known limits: on a nightly fail-closed path (team ambiguous, walk fault) live stamps are withheld from the planner in memory only, nothing is written; the walk stops at an intermediate row owned by an ordinary team (the resolver's own rule; the subject row itself is a walk start, so a subject waiting to be re-owned is placed); a Secure-team-owned Signal with no root found (for example a project or work-assignment core with no typed lookup on the Signal) is undetermined, so it is never stamped; extra Secure-team lookups per walk (uac-r2's helper resolves the team itself).

## Fix round 1 (independent review + uac-r2 review of eccee0700)

| Finding | Change |
|---|---|
| F1 partial stamp never repaired | `ReconcileAsync` selects a group with at least 3 DISTINCT dismissals in the window and either no live stamp, or an open / in-window dismissed Signal whose stamp is missing or older than the group's latest live stamp; per-row comparison kept, so only changed rows are written |
| F2 fault after Allowed | Agreed mapping (coordinator, uac-r2, reviewer): a fault BEFORE or DURING authorization is the uniform 404 (AuthorizeAsync fails closed); a read fault or HTTP timeout AFTER Allowed (count read or caller trim read, fail-closed on non-success or page cap) is a 503 problem (`ontology.suppression.unavailable`, correlationId = trace id), logged WITH the exception; a real cancellation propagates. Note `GetDecisionPlanAsync` has no catch (a fault there is an unhandled 500); the 503 is this route's deliberate, clearer answer |
| F3 nightly re-reads | `sprk_countstowardsuppression` read once per policy in `ReadEnabledPoliciesAsync`; a No policy skips reconciliation with no grain reads; the flag is passed into `AssessAsync` (no per-group policy retrieve); the job's Signal read now carries `sprk_decisionrecord` so one review dismissing 3+ Signals does not pass the pre-filter |
| uac-r2 F1 hidden Signals | The count route trims the grain's Signals to those the CALLER's own `IDataverseUserClient` read returns (`sprk_signals?$select=sprk_signalid` with the grain filter) before counting `DismissalsCounted`, `DismissalsRecorded` and `SuppressedUntil`; a non-success or page-cap read throws (never a partial count). Nightly job and `ApplyAsync` unchanged (app-only) |
| uac-r2 F1 no-core variant | The no-core grain query now requires `sprk_corerecordid` null (app-only and caller reads) so core-bearing later episodes of the same To Do belong to the core grain |
| uac-r2 NITs | Ledger reason reworded (how the data is read) and line refs refreshed; tests added for a Signal with no policy, denied-404 equals not-found-404 (identical body), timeout |

## uac-r2 coordination (POML constraint)

`origin/master` @ `848f63913`. Open uac-r2 PR: #1639 (thread anchor) - no overlap. Reused, not rebuilt: `SignalCoreRecordAccess`
(036) over `IDataverseUserClient`. Not touched: `SecureChildLineage.cs`, `secure-record-owner-role.json`, `RecordOwnershipResolver`,
`RecordRouteAccessAuthorizationFilter`, `CallerRecordAccessProbe`, `ExternalCallerContext`, `CoreAncestorResolver`. One uac-r2-owned
file edited: the route ledger (`RouteAuthorizationGuardTests.Ledger.cs`), one `HandlerDecision` entry, no count change.
