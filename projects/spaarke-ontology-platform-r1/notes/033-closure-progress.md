# Task 033 — closure semantics: progress and decisions

Lane: `task/ontology-033-closure` (worktree `C:\wts-033`), branched from `origin/docs/ontology-platform-design` @ `c6845f474`.
Rigor FULL (POML), effort high. The POML status, `TASK-INDEX.md` and `current-task.md` belong to the main session and are
not edited here.

## What was built

| File | What |
|---|---|
| `Services/Signals/SignalClosureService.cs` (new) | `ClosePolicyRetiredNowAsync(policyId)` (task 100), `CloseSupersededAsync(policyId)` (task 101), and two internal steps the nightly job runs: `SupersedeInPassAsync` and `ClosePoliciesNotInForceAsync`. Every close goes through `SignalWriter.CloseAsync` (system resolutions only, optimistic concurrency, never a Decision Record, never `sprk_resolvedby`, never `sprk_suppresseduntil`) |
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
  `CloseSupersededAsync` (closes only; the job raises the new episode).
- **Negative cases**: Resolved Signals are never read as open by any closure path; a person's resolution after the read is
  refused by the row-version check (`changedSinceRead`); `sprk_decisionrecord`, `sprk_resolvedby`, `sprk_suppresseduntil` are
  never written; no Signal field other than status/resolution/resolvedon/lastevaluated changes, so Off-then-On re-raises from
  the intact PolicyRetired episode (031's planner, D-32) and leaves it unchanged.
- **Dismissal counter / (policy, project) grain**: the counter reads `Dismissed` Signals and `sprk_suppresseduntil`; a
  system close writes neither. A test closes project-grouped Signals (Superseded and ConditionCleared) and asserts no Dismissed
  row, no `sprk_suppresseduntil`, and that the planner then raises a new hit on the same (policy, project).

## Decisions and deviations

1. **`sprk_inforceto` is not read to decide supersession.** The rule is "the Signal cites a version other than the policy's
   current version". Reason: publishing stamps `sprk_inforceto` and moves the pointer together (D-14), so a cited version that is
   not current always has it set; and a CURRENT version with it set has no successor to re-raise under, so closing its Signals
   would reopen them on the same version every night (episode inflation). The owner's amended constraint says "read
   sprk_inforceto"; if task 101 can ever stamp before moving the pointer and calls `CloseSupersededAsync` between the two
   writes, the call must come after the pointer moves. Needs owner awareness (see the PR).
2. **"Deleted" policy is not handled.** FR-16 says "disabled or deleted". Retire (D-22) replaced deletion as the way a policy
   stops, and no role deletes `sprk_policy`; a hard-deleted policy would also orphan the `sprk_policy` lookup the closure reads
   by. Known limit, not filed: no path creates the state.
3. **`ExistingSignal` reused for the callable read** (placeholders for the fields the closure does not need) rather than a
   second record type; the closure needs only id, row version, status and cited version.
4. **Shared-file edits** (lanes 032 / 034 also near the job): ctor gains one parameter; `EvaluatePolicyAsync` gains 4 lines after
   the Signals are read; `RunCounts` gains `AddClosure`; the planner and `SignalWriter` are untouched.
5. **§11 (component justification)**: Existing — `SignalWriter.CloseAsync` closes one Signal and the job decides only
   ConditionCleared; nothing reads Signals by policy/state for admin-triggered closure. Extension — the new class is the only
   place tasks 100/101 can call without injecting a scheduled job. Cost of doing nothing — Off/Retired/published policies'
   Signals stay Open in the worklist and 100/101 cannot close them immediately.
6. **ADR-052 / ADR-036**: no new background work (the closure runs inside the existing `signal-reevaluation` job).
