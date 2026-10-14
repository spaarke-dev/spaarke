# Task 143: D-130 owner for the Signal writers (2026-10-13)

Branch `task/ontology-143-signal-writers` (worktree `C:\wts-143`), base `docs/ontology-platform-design` (merged at `da8cee86d`,
which includes #1824 / task 142). Rigor FULL (bff-api, auth, security, testing).

## What changed

- **`SignalWriter`** takes every Signal's owner from `IRecordOwnershipResolver`, as returned (D-129/D-130), and writes it in the
  create with `RecordOwnerResolution.ApplyTo`:
  - a secure parent → Secure Record Owners;
  - an ordinary core record → that record's business-unit default team;
  - a refusal, or an answer that names no team → nothing written (skipped result, metered `owner_refused`).

  The question is `OwnershipContextOf`: core record first, then the subject, then every core-ancestor stamp, with `RequestedBy`.
  It is the same question `DecisionRecordWriter.OwnershipContextOf` asks.

  Removed: the direct `owningbusinessunit` write, the writer identity as owner (FR-14), and the no-core copy of the subject's raw
  `ownerid`. Created rows are read back. A secure row is checked by `owningteam` (`SecureOwnerMismatch`); an ordinary one by
  `ownerid` (`OwnerMismatch` / `NoCoreOwnerMismatch`); a read-back fault gives `OwnerReadBackFailed`. A reconcile re-checks the
  existing row the same way.
- **No-core path (D-134 parity with the Decision Record):**
  - The resolver is asked about the subject first. A refusal refuses, and a secure answer stands.
  - Only when that answer is ordinary and the subject is a personal To Do does `PersonalToDoAssignee` (the Decision Record's
    helper) find one eligible Assigned To user. The owner is then that user's business-unit team (`RecordOwnershipContext.ForUserTeam`).
  - A resolver fault, or an assignee read fault, fails closed as `NoCoreOwnerUnresolved`.
  - `DeriveCoreRecordAsync` returns the same team for the job's compare.
- **The job's no-core re-own** goes through uac-r2's `SecureChildReconciler.ReownOrdinaryRowAsync` (UAC task 183). It refuses a
  Secure-BU row or team and a user-owned row, and it writes conditionally on the version it read. `SignalReevaluationJob`
  counts each outcome: Reowned, AlreadyOwned, NotFound/Conflict = changed since read, otherwise refused.
- **#1783 / D-135:**
  - `sprk_signal` is added to `RecordCreatorPerson.StampedChildTables`.
  - It is added to `scripts/Set-ChildRecordCreatorPersonSchema.ps1` `$Tables`, with `$SolutionHomes` = SpaarkeMaster and
    OntologyPlatformSolution, as 142 did for the Decision Record.
  - Every Signal raised today is job-raised, so `RequestedBy` is null. The census records this as a PersonLess exemption for
    `SignalReevaluationJob.cs`.
- **Census (`RecordOwnerAssignmentCensusTests`):**
  - `SignalWriter.cs` is flipped to `Routed`.
  - The two PerUser owner-write entries and the FR-14 `BusinessUnitWrites` entry are removed (the list is now empty).
  - A PersonBearing entry is added for `SignalWriter.OwnershipContextOf`.
- **Telemetry:** the reasons are renamed `owner_mismatch` and `owner_read_back_failed`. No consumer outside `projects/` used the
  old strings.

## Fix round (two adversarial passes, opus)

The passes ran twice each, background then foreground, on the same head `ddcefd719`. All four reports were merged; nothing was
dropped.

| # | Finding | Class | Disposition |
|---|---|---|---|
| 1 | The job logs a no-core owner refusal or fault as "the core record ... cannot be derived", and the cause appears only at Debug (all four reports) | F1 | **Fixed.** `SignalHit.CoreRefusalCause` carries the writer's escalation. `LogRefusedCores` names an owner failure for `owner_refused` and `no_core_owner_unresolved`, and attaches the exception. The writer remarks now say where a no-core refusal surfaces in the job. |
| 2 | A re-own that Dataverse refuses (F2, 0x80040299) comes back from the reconciler as Failed text and was metered `signal_reassign_failed`, not `dataverse_access_denied` | F1 | **Fixed.** `IsAccessDeniedMessage` is factored out of `IsDataverseAccessDenied` and applied to `result.Detail`; the fault catch is classified the same way. |
| 3 | The criterion "the Signal and its DR agree (test)" only compared the question, not the owner | F3 | **Fixed.** A new theory runs `SignalWriter.WriteAsync` and `DecisionRecordWriter.ResolveOwnerAsync` over one `OwnershipDirectory` world with the real resolver, for four cases: one assignee, none, filed under a matter, and an assignee unit with no team (both refuse). The DR's assignee is read as `DecisionCommitService` reads it. |
| 4 | No test covered the assignee's team question (ForUserTeam) being refused or faulting | F3 | **Fixed.** Two tests: refused → skipped, nothing written, no fallback to the To Do's team; faulted → `NoCoreOwnerUnresolved`. |
| 5 | A re-own that did not land, combined with date drift, is reported twice; a 412 race fails the run | F2 (pass 2a) / K1 (pass 2b) | **Fixed.** `ApplyAsync` does not reconcile a Signal whose re-own did not land in this pass. It logs at Information, and the next pass retries both. |
| 6 | The live seam test's no-core assertion was weakened to "any team in the unit" | F3 | **Fixed.** It now asserts the exact default team of the To Do's business unit. The test is live-only and blocked by F2. |
| 7 | The `WriteAsync` `<exception>` doc was stale | K4 | **Fixed.** |
| 8 | The job double's `WriterOrg.Reown` did not move `owningbusinessunit` | K4 | **Fixed.** It now derives the unit from the team, as Dataverse does. |
| 9 | POML criterion 1 says "the acting user's BU default team (resolver no-parent case)" | doc | **Recorded.** Both writers ask with the subject as parent (D-35/D-39 as amended), so a no-core owner is the subject's unit team, or for a personal To Do, the assignee's. This matches 142. The criterion is met as amended, not as worded. |

Mutation checks on the fixes: each mutation below made the tests named after it fail.

| Mutation | Tests failed |
|---|---|
| Reconcile despite an unsettled re-own | the conflict-plus-date test |
| Generic reason on Failed | the access-denial test |
| Old core-record message | owner-failure theory, 2 cases |
| Fall back to the To Do's team when the assignee's team is refused | parity "assignee-unit-has-no-team", and the refused test |
| Cause not carried | owner-failure theory, 2 cases |

### Re-check of the fix diff (sonnet, scoped to 5e93df2ae)

No F-class findings. The refactor of IsAccessDeniedMessage is behaviour-identical, and SignalHit's new trailing optional parameter breaks no construction site. The reconcile skip loses nothing: before the fix that reconcile met CoreRecordChanged and wrote nothing.

- K3 (fixed): there was no positive control for the skip. Two tests are added:
  - a re-own that lands, plus a date change, still reconciles in the same pass (reassigned 1, updated 1);
  - a refused re-own, plus a date change, is counted refused once, with no core_record_changed report.

  Mutation check: dropping the Remove on Reowned fails the first test.
- K2 (accepted, not a regression): a Signal whose re-own is refused every night never has its date refreshed. Its trace is the nightly Warning for the refused re-own.

### Known limits (recorded, not fixed)

- **K: pre-143 rows.** A Signal written before 143 is writer- or user-owned. Its reconcile escalates `OwnerMismatch` /
  `CoreRecordChanged`, and its re-own is `RefusedNotTeamOwned` (and the event trigger retries that until it dead-letters).
  spaarkedev1 has 0 `sprk_signal` rows.
- **K: a core record that moves business unit.** An existing Signal escalates `OwnerMismatch` on reconcile rather than being
  re-owned.
- **K: a no-core answer that flips between ordinary and secure.** The re-own answers `RefusedSecure` every night (Warning plus
  metric), and the secure pass has no root to reach the row. A no-core item has no secure root, so this is unreachable in
  practice.
- **K: an assignee whose own unit is the Secure Record BU.** `ForUserTeam` answers Secure Record Owners. `DecisionRecordWriter`
  behaves the same way; the resolver documents it as a case that should never meet.
- **K: a core-path resolver fault propagates raw.** The no-core path wraps it. It is not silent: `GuardAsync` logs it at Error
  and meters `write_failed`.
- **K: a stale re-own target within one pass.** The If-Match guards the Signal's version, not the item's answer. A mid-pass
  Assigned To change lands one night late.
- **F2 (open owner decision, 0x80040299).** Business-unit default teams lack Read on `sprk_signal`, so in dev every ordinary
  Signal create, and every re-own, fails until the owner decides F2.
  - The code does not work around it with a user or writer owner, and grants or changes no role.
  - A create surfaces it as `dataverse_access_denied`. A re-own does too, since this fix round.
  - The live seam tests need F2 decided.

## Component justification (CLAUDE.md §11)

- **`SignalHit.CoreRefusalCause`** (an optional record parameter).
  - Existing: `CoreRefusalReason` carries the tag only.
  - Extension: this is that extension.
  - Cost of doing nothing: the job's only Error record of a no-core owner refusal carries neither the refusal code nor the
    inner fault (review F1).
- **`SignalWriter.IsAccessDeniedMessage`.**
  - Existing: `IsDataverseAccessDenied`'s inline message check.
  - Extension: it is factored out of that method, not a new rule.
  - Cost of doing nothing: F2's refusal on a re-own is metered under the wrong reason.
- No new endpoint, DI registration, package or Dataverse column. `IServiceScopeFactory` was already registered.
  `AssignedAccessStore` and `SecureChildReconciler` are uac-r2's existing scoped services, registered unconditionally in
  `ExternalAccessModule`.

## BFF hygiene (§10)

- **Publish size:** both sides built with `dotnet publish -c Release src/server/api/Sprk.Bff.Api/` (framework-dependent) and zipped
  with `Compress-Archive -CompressionLevel Optimal`.

  | Side | Files | Zip size (bytes) |
  |---|---|---|
  | Base `da8cee86d` (fresh worktree `C:\p143base`, removed afterwards) | 192 | 38,600,829 |
  | Branch | 192 | 38,602,232 |

  The delta is **+1,403 bytes**, with no new files.
- **CVEs:** no package changes. `dotnet list package --vulnerable --include-transitive` shows only the pre-existing,
  repo-wide `System.Security.Cryptography.Pkcs` 10.0.11 advisories (GHSA-846f-f764-76mx High, GHSA-2fmv-rv2c-59x7 Moderate,
  #1826). This task adds no new HIGH.

## Verification

- **Live check:** `scripts/Set-ChildRecordCreatorPersonSchema.ps1 -Verify` against spaarkedev1 (read-only, run once):
  **VERIFY PASS**, exit 0. It confirmed:
  - the `sprk_signal.sprk_createdbyperson` column and the relationship `sprk_signal_CreatedByPerson_systemuser`;
  - the field is secured, and the Readers, Writers and narrow-profile rows are OK;
  - the table is in SpaarkeMaster and OntologyPlatformSolution;
  - there are 0 app-created rows.

  No `-Apply` was run, and no live writes were made.
- **Pester:** `tests/scripts/Set-ChildRecordCreatorPersonSchema.Tests.ps1`, in a separate `pwsh -NoProfile`, passes 30/30.
  Pester mutations: dropping `sprk_signal` from `$Tables` fails 5 tests; dropping its `$SolutionHomes` entry fails 8.
- **Unit tests (filtered), after the fix round:** `Sprk.Bff.Api.Tests` filtered to Signal | RecordOwnership | PersonalToDo |
  SecureChild | RecordCreatorPerson | DecisionCommit | DecisionRecord passes **2076/2076**.
- **ArchTests (full suite):** **973/973**, including the 34 census tests.
- **Mutation checks before the fix round:** each of these removals made named tests fail:
  - the refusal skip;
  - the secure `owningteam` check;
  - secure-wins before the assignee;
  - the assignee-fault fail-closed;
  - the ordinary `ownerid` check;
  - the stamps in `OwnershipContextOf`;
  - the no-core reconcile owner compare;
  - the subject read in `SignalCoreRecordAccess` (criterion 5).
- **Test scope:** the stated contract and the closed acceptance set. The extra tests are the review's F-class cases and the
  real-resolver parity. Justification: the criterion "the Signal and its DR agree" can only be proved over the real resolver.
- **Not run:** the live seam tests, which are blocked by F2.

## Incident (local tooling, no effect)

A `[IO.File]::WriteAllText` call with a relative path resolved against the session's process directory, the main worktree,
instead of `C:\wts-143`. The main worktree's `git status` stayed clean, so nothing changed there. The edit was redone with an
absolute path.

The mutation script restores files with an older mtime. Because of that, an incremental build after a mutation run can keep
the last mutant. The final verification therefore used `--no-incremental`.
