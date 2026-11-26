# Task 225 — the commit route shares a new secure-child Decision Record inline; the Decision Record records the D-212 privilege flag

Issues: #1980 (DI-97), #2630 (DI-143). Decisions: D-229 (inline share), D-212 (privilege flag). Branch `fix/ontology-225-dr-inline-share`
from `origin/master` `d43fc9e16d`. Rigor FULL (BFF `.cs`). Model opus @ high.

## Findings (step 1)

- **#1980.** `DecisionCommitService.CommitCoreAsync` writes the Decision Record at step (5) through `DecisionRecordWriter.WriteAsync` with the
  owner resolved at (1f) (`DecisionRecordOwner.Resolution`, uac-r2's `RecordOwnerResolution`, `IsSecureOwner` when the record goes to the
  Secure Record Owners team). Nothing shared the new row: the 2-minute `SecureChildShareReconciliationJob` did.
- uac-r2 already ships the per-child entry point this needs: `SecureChildShareSynchronizer.SyncChildAsync(table, id, ct)` (task 147 r1,
  "mirrors ONE child ... the same mirror the scheduled reconcile computes for that row"). `RecordCreationService` calls it after a secure
  create (the reference copied here: same call, same "log and leave it to the reconcile" handling). `sprk_decisionrecord` is a lineage
  child (`SecureChildLineage`). The synchronizer is scoped and registered unconditionally (`ExternalAccessModule`); `DecisionCommitService`
  is scoped, so it takes it by constructor. `DecisionRecordWriter` is a singleton and cannot.
- **#2630.** `DecisionCommitService.SignalColumns` read `sprk_signal.sprk_privilegeflagged` (no writer, DI-64) into `SignalRow` and
  `RecordRequest` copied `Any(r => r.PrivilegeFlagged)` to the record. `WorkItemPrivilege` (mcp-bff task 018, `SignalsModule`, scoped) is
  the shared rule; its remarks name this reuse.

## What changed

- `Services/Signals/DecisionCommitService.cs`
  - Constructor takes `SecureChildShareSynchronizer`, `WorkItemPrivilege` and the caller's `IDataverseUserClient` (all already registered,
    scoped; no new registration, service, endpoint or package).
  - `SignalColumns` reads `sprk_evidencerefs` instead of `sprk_privilegeflagged`; `SignalRow.PrivilegeFlagged` is replaced by
    `SignalRow.EvidenceRefs`.
  - Step (1d'): `PrivilegeFlaggedAsync` builds each covered Signal's sources with `WorkItemPrivilege.SourcesOf(SubjectEntity, SubjectId,
    EvidenceRefs)` and calls `FlaggedAsync` once with the caller's client. Any flagged Signal flags the record. Read before the first write,
    only when the review has no record yet. A throw (FlaggedAsync answers its own faults) is caught and flags (fail closed).
  - Step (5b): `ShareSecureRecordAsync` calls `SyncChildAsync("sprk_decisionrecord", recordId, CancellationToken.None)` for that one
    record: for a record written now when `recordOwner.Resolution.IsSecureOwner`, and (fix round 1) for a resumed review's existing record
    without that gate, since this request's owner answer is not the stored row's owner. Failed or Incomplete is logged
    (`DecisionCommitEvents.AfterRecordFailed`), a throw is caught and logged; the decision stays recorded.
  - Fix round 1: the finishing path (the ledger forgot, the record found by its review id) shares the found record too, after its closes,
    as its last step: a crash between the record write and the share, retried at once, reaches this path within seconds. No new read
    (the record id is already in hand); `SyncChildAsync` answers NotApplicable for a row the Secure team does not own.
- Tests: `tests/integration/data-mutation/Signals/DecisionCommitServiceTests.InlineShareAndPrivilege.cs` (new, 18 cases: 14, then 4 in fix round 1); the rig in
  `DecisionCommitServiceTests.cs` gains uac-r2's REAL synchronizer over `SecureChildShareWorld` + `FakeRecordShareTable` (each created
  Decision Record is stored in that world as written) and a caller answer for the classification read; two fixtures follow the constructor
  and record change (`DecisionCommitFollowUpsTests.cs`, `tests/unit/domain/Signals/DecisionCapabilityFixtures.cs`).

## Decisions made in the task

- **A share fault is not a user warning.** The wizard builds its "recorded" view from the commit response and does not re-read the record
  (#1980 console check), the reconcile repairs the share within one cycle, and `RecordCreationService` (the reference) logs only. Logged as
  a Warning with EventId 50363 (`AfterRecordFailed`, whose summary now names this step), record id and review id only.
- **Placement after the record write, before the Signal closes.** The share needs the row; running it first after the write shortens the
  window most. It runs after the record write, so it stays outside mcp-r1's ISS-010 Part C access window (closed before the write).
- **"Same grants as the reconcile"** is met by calling uac-r2's own per-child mirror, not a copy; the test proves it by running
  `ReconcileAllAsync` afterwards and asserting it writes nothing to the new record (while it does mirror a seeded out-of-line control).
- **Gate a record written now on the resolver's answer**, not every record: an ordinary new record makes no synchronizer call at all
  (must-not-fire test). `SyncChildAsync` would answer NotApplicable for an ordinary row anyway, but only after reads. A record an earlier
  attempt wrote (found branch, finishing path) is not gated (fix round 1): this request's owner answer says nothing about the stored row.
- **The outer catch is covered** (fix round 1): a synchronizer whose run-summary logger throws, the one fault that escapes
  `SyncChildAsync`, is logged with 50363 and the decision is still Recorded. No K3 needed.

## Fail-before proof

The original file cannot compile against the new rig (constructor), so each fix was removed by mutation and the commit-service
suite (389 tests; 393 in fix round 1) re-run, restoring the file after each:

| Mutation | Failed | Which |
|---|---|---|
| M1: inline share call removed | 3 | the inline-share test and both share-fault tests (they assert the logged warning) |
| M2: derivation removed (flag always false) | 6 | Privileged and Potentially Privileged evidence, read fault, unreadable communication, uninterpretable evidence, Privileged subject |
| M3: the Signal column read again as a source | 3 | None and null with the column set true, and the no-communication must-not-fire |
| R1 (fix round 1): owner gate put back on the found branch | 1 | the resumed review with an existing record |
| R2 (fix round 1): finishing-path share removed | 1 | finishing a review whose record exists |
| R3 (fix round 1): the outer catch rethrows | 1 (the throw test) |

## Verification

- `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~DecisionCommitServiceTests"`: 389 passed (14 new); fix round 1:
  393 passed (4 more). `--filter "FullyQualifiedName~DecisionCommit"`: 404 passed.
- `--filter "FullyQualifiedName~Signals"` (contract baselines included): 5565 passed, 0 failed, 6 skipped. No baseline moved. Fix round 1:
  5569 passed, 0 failed, 6 skipped.
- `tests/Spaarke.ArchTests` built explicitly, full run: 2845 passed, 0 failed. Not re-run in fix round 1 (only
  `DecisionCommitService.cs` and test files changed).
- Publish size: see the PR.

## Known limits

- Ledger replay (the tenant cache still holds the first outcome) does not share again: that outcome exists only because the first attempt
  ran to its end, inline share included (tried, and logged if it failed). An attempt that stopped between the write and the share left the
  ledger "in progress", and its retry reaches the finishing path, which now shares (fix round 1).
- K2: if the resolver says secure but the row is not Secure-team-owned when read, `SyncChildAsync` answers NotApplicable and nothing is
  logged; the reconcile owns that row as before.
- D-212's accepted limits stand (AI flag not a source; field security on the classification reads as not flagged; an untyped
  non-communication subject is over-flagged).
- K2: the commit derives a Signal's subject table from its regarding lookups (`SignalWriter.SubjectEntityOf`), while the worklist uses the catalog type (`SignalReadService` ~:1358); a subject that matches no lookup, or two, is read as an untyped communication and flagged. That errs toward flagging.

## Found, not fixed

- **Same class, outside this task (report to the main session):** `Services/Signals/SignalReevaluationJob.cs:965` writes the
  ConditionCleared Decision Record's flag from the Signal's `sprk_privilegeflagged` (read at :1523), the column nothing writes (DI-64).
  Every system-closed record therefore says not privileged. D-212 says "as the caller", and a system closure has no caller, so how to
  derive it there (app-only, or left unflagged) is an owner decision, not a copy of this fix. Grep (`sprk_privilegeflagged` under
  `Sprk.Bff.Api`): no other reader treats the Signal column as a source; the gate Decision Record copies from the gate decision, a
  different source.

- The register entries DI-64 / DI-97 / DI-143 in `notes/defer-issues.md` should point at this fix; that file is the main session's (it lags
  on master), so it is reported, not edited here.
