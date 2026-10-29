# Task 152: Do-lane suppression counts per person (D-145a, #1921)

> Branch `task/ontology-152-do-suppression-per-person` (worktree `C:\wts-152`), cut from `origin/docs/ontology-platform-design` at
> `3ef786aa9`. Rigor **FULL**. uac-r2 reviews the PR (D-123); the main session coordinates that.

## The design chosen

**The person grain is a split inside the service, not a new grain key at the callers.** `SuppressionGrain` (what the commit route and
the job name) is unchanged. Inside `SignalSuppressionService`, every partition of a Do-lane grain is split again by a new
`SuppressionHolder`, exactly as the secure-family partition already splits every grain (D-123):

| Do item | Holder | What counts toward it |
|---|---|---|
| Subject's person column names a contact | `Person(contact)` | Dismissals whose Decision Record's decider (`sprk_assignedtointernal`) is that contact |
| Subject names no individual, its table has no "mine" rule, or the row is gone | `Item(subject)` (per item, D-39) | Every dismissal of that item, as before |
| Decide lane | `Everyone` (not split) | Unchanged |

- **Person column:** `SignalReadService.DoLaneMineRules[table].PersonColumn` (task 150's rule, built on
  `OwnedChildWrite.ForPersonColumns`), read from the subject now. Not copied. Note: that map gives `sprk_assignedto` for `sprk_event`
  and `sprk_todo` (D-137 Q22), not `sprk_assignedto1` as the POML wrote; code wins.
- **Why the decider, too:** the subject's person is read now. A To Do that Bob dismissed and that was later reassigned to Alice (Bob
  leaves, his work moves) would otherwise carry Bob's dismissals into Alice's grain and mute her items. The Decision Record's decider is
  written at decision time (D-129, `DecisionRecordWriter`) and Decision Records are append-only, so "who dismissed" comes from data that
  exists today. **No new column.**
- **Lane:** the job passes `lane == LaneDo` to `ReconcileAsync`; the commit route and the count read take it from the policy's
  `sprk_lane` (read with `sprk_countstowardsuppression`, one read as before).
- **Planner:** `ExistingSignal.Holder` (set by `ReconcileAsync` with `Partition`) and `Plan(..., hitHolders)`; a stamp mutes a hit only
  on the same (partition, holder). The job reads hit holders with `HoldersOfHitsAsync` for the hits on stamped grains only; a fault
  skips the policy, exactly like a partition fault.
- **`GET /suppression` (D-141):** `SignalAccessDecision.CallerContactId` (set by `ConfirmDoLaneMineAsync`, which already resolves the
  caller's contact) is passed to `GetStatusForSignalAsync`. A Do item for a person reports that person's own count, and only to that
  person; any other "mine" caller (owner, creator, assigner) gets an empty status. A per-item Do item reports its own count.

## Pre-existing (policy, core) Do-lane stamps: re-derived on the first nightly pass

`ReconcileAsync` already works every grain that carries a live stamp. With the holder split, a stamp written before this change at
(policy, core) is kept on the rows of a holder whose own dismissals justify it (unchanged rows are not written, D-61), and lowered or
cleared on every other holder's rows (conditional on row version, as before). Test:
`T152_APreExistingCoreStamp_OnADoItem_StopsMutingOtherPeople_AfterOneNightlyPass`.

## §11 for the new surface

- **`SuppressionHolder`** (type). Existing: `SuppressionPartition` / `SuppressionGrain`. Extension: the holder is a second split beside
  the partition; folding the person into `SuppressionPartition` would break its other users (witness families, the commit route's
  one-family check). Cost of doing nothing: a colleague's dismissals mute your Do items (#1921, D-145a).
- **`HoldersOfHitsAsync` / `HoldersOfSubjectsAsync`** (methods in the existing service). Existing: `PartitionsOfHitsAsync`,
  `ReadSubjectRowsAsync`. Extension: those read lineage and owner columns only when a Secure team exists; this reads the "mine" person
  column always. Cost: without the hit's person the planner cannot tell Alice's next item from Bob's.
- **`ExistingSignal.Holder`, `Plan(hitHolders)`, `ReconcileAsync(doLane)`, `GetStatusForSignalAsync(callerContactId)`,
  `SignalAccessDecision.CallerContactId`**: optional parameters/fields on existing surface.
- No endpoint, registration, package, job or column.

## Tests

New (maintain-class, `tests/unit/domain/Signals`): five in `SignalSuppressionTests.DoLanePerPerson.cs` plus the reassignment case,
one job-level run in `SignalReevaluationJobTests`, one decision-level check in `DoLanePrivacyTests`.

Seeding proofs:
- Holder forced to `Everyone` (the (policy, core) key restored): the colleague, Bob-next, per-item, pre-existing-stamp and job-level
  tests fail (5 of 7); the Decide-lane test and the contact check pass, as they should.
- Decider filter removed: only `T152_BobsDismissals_OfToDosReassignedToAliceSince_NeverMuteAlice` fails.

## Known limits (K-class)

- K2: a dismissal with no decider on record (the caller had no linked contact, or a legacy dismissal with no Decision Record), or one
  made by the item's owner/creator/assigner rather than its person, counts toward no person. Fails toward raising, never toward muting.
- K4: `ApplyAsync`'s returned status in the Do lane is the first holder's of the partition; its only production caller ignores it.
- K4: the route passing `decision.CallerContactId` is covered at the decision level, not by a route test (the route fixture has no
  Do-lane identity setup); without it the count would be empty for every Do item (fail private, not fail open).
- Test-fixture gap (not a product defect): the job test `World` does not model `ReadGrainSignalsAsync`'s nested status filter, so a
  job-level test cannot reach a worked grain; the job test seeds justified stamps (as the existing #1661 job tests do) and stamping is
  proven at the service level.

## Adversarial pass 1 and fix round

Pass 1 confirmed the core rule (a dismissal counts only for the person who decided it, and only on that person's own Do items) on every
path, and found:

- **F1, fixed.** A "mine" caller who is not the item's person (an owner, creator or assigner) was told "n of 3" from the unsplit count.
  `GetStatusForSignalAsync` now returns `(0, 0, null, false)` for that caller, so the wizard says the dismissal does not count.
  Theory `T152_ACallerWhoIsNotTheItemsPerson_IsToldTheirDismissalCountsTowardNothing` covers an assigner on a work assignment and a
  creator on an event. Seeding proof: reverting to `assessed.PolicyCounts` makes both cases fail.
- **K3, fixed.** A Do-lane Signal whose policy has since left the Do lane is no longer answered from the unsplit count; it gets the
  empty status. Test `T152_ADoSignalOfAPolicyNoLongerInTheDoLane_IsNotAnsweredFromTheUnsplitCount`; removing the check makes it fail.
- **K4, fixed.** `AssessAsync` reuses the caller's holder map unchanged when the read adds nothing, and merges only when it does.
- **K1, deferred.** The main session files the issue.

rework: none task 152
