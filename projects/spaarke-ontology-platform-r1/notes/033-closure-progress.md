# Task 033 — closure semantics: progress and decisions

Lane: `task/ontology-033-closure` (worktree `C:\wts-033`), branched from `origin/docs/ontology-platform-design` @ `c6845f474`.
Rigor FULL (POML), effort high. The POML status, `TASK-INDEX.md` and `current-task.md` belong to the main session and are
not edited here.

## What was built

| File | What |
|---|---|
| `Services/Signals/SignalClosureService.cs` (new) | `ClosePolicyRetiredNowAsync(policyId)` (task 100) and two internal steps the nightly job runs: `SupersedeInPassAsync` and `ClosePoliciesNotInForceAsync`. Outcomes carry `IsComplete` (callers must check it). Every close goes through `SignalWriter.CloseAsync` (system resolutions only, optimistic concurrency, never a Decision Record, never `sprk_resolvedby`, never `sprk_suppresseduntil`) |
| `Services/Signals/SignalReevaluationJob.cs` (shared, small edit) | New ctor param `SignalClosureService`; in-pass supersede right after the Signals are read, so the existing planner re-raises the subjects the new version holds in the SAME pass; a final Off/Retired step after the policy loop; `sprk_policyversion` read onto `ExistingSignal` (new optional trailing field `PolicyVersionId`); `supersededClosed` / `policyRetiredClosed` in the run result; closure refusals count as refused (the run fails, never silent) |
| `Infrastructure/DI/SignalsModule.cs` (shared, one line) | `AddSingleton<SignalClosureService>()` |
| `tests/unit/domain/Signals/SignalReevaluationJobTests.cs` | 10 new tests (harness extended: policy ids/versions, publish, Off/Retire, open-by-policy read) |

## How each acceptance criterion is met

- **Aged-out → ConditionCleared**: no new code. The job's existing "not a hit" rule closes it (the rule's `now-30d` bound
  excludes the old communication, so the subject is not a hit). A test asserts the close and the window bound in the query.
- **Superseded**: open Signal citing a version other than `sprk_policy.sprk_currentversion` → `Superseded`; then the planner
  (unchanged, 031) sees a Resolved/Superseded latest episode and raises n+1 citing the new version when the new version still
  holds. A subject the new version no longer holds stays Superseded (it is not closed a second time as ConditionCleared).
- **PolicyRetired**: policies with `sprk_enabled = No` or `statecode` Inactive are read each night (two simple queries) and
  their open Signals closed. Nothing is evaluated for them. Idempotent: no open Signal, no write.
- **Callables**: `ClosePolicyRetiredNowAsync` (no precondition check: the admin route that calls it owns authorization) and
  `SignalReevaluationJob.ReevaluatePolicyAsync(policyId)` (task 101: supersede AND raise the new episode in one pass; see the contract below).
- **Negative cases**: Resolved Signals are never read as open by any closure path; a person's resolution after the read is
  refused by the row-version check (`changedSinceRead`); `sprk_decisionrecord`, `sprk_resolvedby`, `sprk_suppresseduntil` are
  never written; no Signal field other than status/resolution/resolvedon/lastevaluated changes, so Off-then-On re-raises from
  the intact PolicyRetired episode (031's planner, D-32) and leaves it unchanged.
- **Dismissal counter / (policy, project) grain**: the counter reads `Dismissed` Signals and `sprk_suppresseduntil`; a
  system close writes neither. A test closes project-grouped Signals (Superseded and ConditionCleared) and asserts no Dismissed
  row, no `sprk_suppresseduntil`, and that the planner then raises a new hit on the same (policy, project).

## Decisions and deviations

1. **`sprk_inforceto` guard (review round 1).** Supersession is decided by the pointer (a Signal cites a version other than
   `sprk_currentversion`). `sprk_inforceto` is read on the CURRENT version as a guard: if it is set and `<= now`, no version is
   in force, so the policy is not evaluated, nothing is closed or raised, `PolicyFailures` is counted (the run is not
   successful) and EventId 50309 `PolicyVersionNotInForce` is logged. Closing would just reopen the same version every night.
   Controls: a current version with no, or a future, `sprk_inforceto` runs normally.
2. **"Deleted" policy: now handled (task 102b fix round 3, 2026-10-17).** FR-16 says "disabled or deleted". Retire (D-22) replaced
   deletion as the way a policy stops, but the state now has a path: deleting a matter deletes a rule limited to it
   (`sprk_policy_matter` CascadeDelete=Cascade, owner 2026-10-17), and an Ontology Administrator can hard-delete a policy. The
   policy's Signals keep their rows with `sprk_policy` nulled (RemoveLink), so the closure above, which reads by that lookup,
   never reaches them. The nightly job's deleted-policy sweep
   (`SignalClosureService.CloseSignalsOfDeletedPoliciesAsync`) closes every Open or Acknowledged Signal whose `sprk_policy` is
   empty and whose dedupe key names no existing policy code, as `PolicyRetired`. See `102b-rule-matter-scope.md`, "Fix round 3".
3. **`ExistingSignal` reused for the callable read** (placeholders for the fields the closure does not need) rather than a
   second record type; the closure needs only id, row version, status and cited version.
4. **Shared-file edits** (lanes 032 / 034 also near the job): ctor gains one parameter; `EvaluatePolicyAsync` gains 4 lines after
   the Signals are read; `RunCounts` gains `AddClosure`; the planner and `SignalWriter` are untouched.
5. **§11 (component justification)**: Existing — `SignalWriter.CloseAsync` closes one Signal and the job decides only
   ConditionCleared; nothing reads Signals by policy/state for admin-triggered closure. Extension — the new class is the only
   place tasks 100/101 can call without injecting a scheduled job. Cost of doing nothing — Off/Retired/published policies'
   Signals stay Open in the worklist and 100/101 cannot close them immediately.
6. **ADR-052 / ADR-036**: no new background work (the closure runs inside the existing `signal-reevaluation` job).

## Task 101 contract (publish)

Task 101 publishes in this order, then makes ONE call: (1) create the new `sprk_policyversion`; (2) stamp the old version's
`sprk_inforceto`; (3) move `sprk_policy.sprk_currentversion` to the new version; (4) call
`SignalReevaluationJob.ReevaluatePolicyAsync(policyId)` (the job is a concrete singleton, injectable directly).

- It runs the same single-policy pass the nightly run runs: open Signals citing an older version close `Superseded`, and each
  subject the new version still holds gets a new episode (`|n+1`) citing the new version, so still-true items do not leave
  the worklist until 02:30. There is deliberately no close-only supersede.
- The returned `PolicyReevaluationResult.Success` MUST be checked; false means something was refused or no version is in force
  (step 2 before step 3, or a stamped current version), and the nightly run retries.
- Not for an Off or Retired policy: it throws `InvalidOperationException`; use `SignalClosureService.ClosePolicyRetiredNowAsync`
  (task 100) and check `IsComplete`.

## Review round 1 (items fixed)

1. Acceptance criterion 6: `CloseSupersededAsync` removed; `ReevaluatePolicyAsync` is the single path (supersede + raise).
2. `SignalClosureOutcome.IsComplete` (Failed, ChangedSinceRead and PolicyFailures all zero) with tests; documented as mandatory.
3. `sprk_inforceto` guard above, with a Theory (-1d, 0d fail; +30d, none run normally).
4. Tests added: Off/Retired step closure failure fails the run; a supersede close skipped because a person resolved the Signal is
   counted once (the planner no longer retries a stale row: `plan.Closes/Reconciles/Reassigns` drop the ids the supersede
   targeted but did not close, `createsDeferred` covers a re-file); the aged-out test now seeds an old communication and the
   fake honours the FetchXML link date bounds, so it fails without the window.
5. Heartbeat line gained `supersededClosed` and `policyRetiredClosed`.
