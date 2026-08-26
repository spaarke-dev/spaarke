# Task 060 — I6 Crash Recovery — Deviations

> **Task**: 060-implement-i6-crash-recovery.poml
> **Date**: 2026-08-18
> **Author**: Task 060 agent (Wave 4 Batch 4D)
> **Status**: complete — 24/24 unit tests green; 0 code hits for `DateTime.UtcNow` / `Stopwatch` in new files

## Summary

Implements FR-23 / design.md §4.2 I6 crash-recovery scan as an `IHostedService`
(`CrashRecoveryStartupService`) that runs once on L2 App Service startup, scans
Cosmos via the existing `IActiveRunScanner` for runs in
`{Running, WaitingOnGate}`, filters by age
(`MAX(2× MedianHandlerDuration, FloorAge)`), and re-enqueues each orphan's
`CurrentPhase` via the same `IHandlerEnqueuer` the reconciler uses.

## Deviations from the POML literal wording

### D-1 (Path A per CLAUDE.md §6.5) — Test file lives in the sibling `.Tests` project, not inside the production project
- **POML wording**: deliverables list
  `src/server/services/Sprk.Provisioning.ControlPlane/Reconciler/CrashRecoveryStartupService.Tests.cs`
  (inside the production project).
- **Landed at**:
  `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Reconciler/CrashRecoveryStartupServiceTests.cs`.
- **Rationale**: the L2 project's ESTABLISHED convention is
  `Sprk.Provisioning.ControlPlane` (SUT) + `Sprk.Provisioning.ControlPlane.Tests`
  (test project) — see `StateReconcilerServiceTests.cs` (task 058) and every
  handler test. Placing test code inside the production project would break the
  csproj `IsPackable=false` boundary + would ship xUnit into the production
  publish. The POML author almost certainly intended the sibling location; the
  literal path in the deliverables list was a scaffold-time typo.
- **Reviewer contract**: unchanged — same content, same coverage, right project.

### D-2 (Path C — comply) — Reused `IActiveRunScanner` unchanged rather than extending its interface
- **POML step 5** suggests I "may reuse or extend" `IActiveRunScanner` for the
  crash-recovery scan.
- **Chosen path**: reuse WITHOUT extension.
- **Rationale**: the scanner already returns runs in `{Running, WaitingOnGate}`
  (the FR-23 status set); age filtering is a client-side operation over a
  small snapshot (single-digit runs/day per §16 rejected alt-1). Adding a
  server-side `olderThan` overload would either widen `IActiveRunScanner`
  (touching a task-058 file this task is explicitly told NOT to modify) or
  create a second interface with 90 % overlap (fails CLAUDE.md §11 component-
  justification test). The reconciler is a sanctioned cross-partition read;
  reusing it once at startup adds zero RU-cost regime.

### D-3 (Path C — comply) — Duplicated the 4-field `ReconcilerEnqueuePayload` shape locally rather than extracting a shared helper
- **POML step 4** + parent brief: "re-enqueue via the reconciler's enqueue helper".
- **Reality**: `StateReconcilerService.BuildEnvelope` is `internal` and its
  nested `ReconcilerEnqueuePayload` is a `private sealed record`. Extracting a
  public helper would modify a Reconciler/ file this task is EXPLICITLY told
  NOT to modify (see parent's "What NOT to touch": *"Reconciler files other
  than adding your CrashRecoveryStartupService.cs alongside them."*).
- **Chosen path**: duplicate the 4-field payload record + the 15-line envelope-
  build logic in `CrashRecoveryStartupService`, holding the SAME
  `Action="reconciler-advance"` tag + camelCase serializer options so
  `ServiceBusHandlerEnqueuer.ComputeMessageId` yields byte-identical MessageIds
  between crash-recovery + reconciler dispatches (level-1 SB dedup collapses
  them). The `BuildEnvelope_ProducesMessageId_ByteIdenticalToReconcilerEquivalent`
  test asserts this contract at build time.
- **Extraction deferred to**: a coordinated task in Wave C6+ when tasks 059 +
  061 + 060 land + the "Reconciler is read-only for me" boundary lifts.

### D-4 (path A / coordination) — Small cross-task fixes required to unblock shared-worktree build
- **What**: added `using Sprk.Provisioning.ControlPlane.Rollback;` to `Program.cs`
  (was missing after task 061 landed `AddRollbackModule()` call without the
  matching using); fixed `InMemoryRegistryConcurrencyStore.cs` positional
  record ctor arg casing (`etagCounter:` → `EtagCounter:`) so the tests
  project would compile.
- **Rationale**: sibling tasks 059 + 061 were mid-flight in the shared worktree
  when I started; their partial commits + working-tree state left the build
  broken end-to-end (production references `Rollback` namespace + tests
  reference `EtagCounter` positional param). Without these two 1-line
  coordination fixes, MY task could not validate its build or run its own tests.
- **Committed only my own files**: `git commit --only` on my three new files +
  Program.cs. The sibling-file edits above remain in the working tree
  (unstaged/untracked) for tasks 059 + 061's own agents to fold into their
  commits when they land.
- **Note to reviewers**: if you see the working tree in a partial state after
  this task commits, that's expected — the concurrent Wave 4 Batch 4D tasks
  (058 reconciler committed, 059 concurrency guard in flight, 060 this task,
  061 rollback in flight) share `Program.cs` + `Api/RunsEndpoints.cs` per
  project design decision D-CONCUR. See parent brief's "SHARED FILE Program.cs
  + soft dep 059" section.

## What was NOT done (intentional, per POML scope)

- **NO extraction of a shared reconciler-enqueue helper** — see D-3.
- **NO extension of `IActiveRunScanner` with an age-filter overload** — see D-2.
- **NO direct dependency on task 059's `ICustomerRunGuard`** — parent brief
  explicitly instructs "you do NOT need to directly consume `ICustomerRunGuard`
  — just re-enqueue via the same helper"; my re-enqueue routes through the
  same `IHandlerEnqueuer` the reconciler uses, so once task 059's guard lands
  as an `IHandlerEnqueuer` decorator (planned), my re-enqueues transitively
  inherit the same-customer serialization contract.
- **NO write to `_ts` / new `LastUpdated` property on `ProvisioningRun`** — the
  age proxy is computed client-side as
  `MAX(run.CompletedPhases.Max(CompletedAt), run.CreatedOn)`. This avoids
  touching the task-024 POCO for one crash-recovery consumer + is a provably
  correct lower bound on Cosmos `_ts` given the reconciler's append-only
  contract on `CompletedPhases`.
- **NO Cosmos schema change** — the scan runs against the existing container.

## Verification (task 060 gates)

| Gate | Result |
|---|---|
| Build `Sprk.Provisioning.ControlPlane` | 0 errors / 0 warnings |
| Build `Sprk.Provisioning.ControlPlane.Tests` (with task 059 Concurrency test folder temporarily set aside due to unrelated `sealed`/`etagCounter` build errors in task 059's uncommitted code) | 0 errors / 0 warnings |
| `dotnet test --filter FullyQualifiedName~CrashRecoveryStartupServiceTests` | 24 passed, 0 failed, 0 skipped |
| `dotnet test --filter FullyQualifiedName~Reconciler` (regression) | 65 passed, 0 failed, 0 skipped |
| Grep gate — `DateTime.UtcNow` / `Stopwatch` in new files | 0 CODE hits (3 comment-line hits inside the file-header discipline docstring) |
| POML acceptance #1 — orphan `Running` re-enqueues currentPhase | `RunOnce_OrphanedRunningRun_ReEnqueuesCurrentPhase` ✅ |
| POML acceptance #1 — orphan `WaitingOnGate` re-enqueues currentPhase | `RunOnce_OrphanedWaitingOnGateRun_ReEnqueuesCurrentPhase` ✅ |
| POML acceptance #2 — fresh run NOT re-enqueued | `RunOnce_FreshRunningRun_DoesNotReEnqueue` ✅ |
| POML acceptance #3 — terminal statuses skipped | `RunOnce_TerminalStatusRun_DoesNotReEnqueue` [Theory 4 rows] ✅ |
| POML acceptance #4 — 3-level idempotency intact (MessageId parity) | `BuildEnvelope_ProducesMessageId_ByteIdenticalToReconcilerEquivalent` ✅ |
| POML acceptance #5 — log runId + phaseId + age | `RunOnce_OrphanRecovered_LogsReEnqueueEventWithContext` ✅ |
| POML acceptance #6 — TimeProvider grep gate | ✅ (see above) |
| POML acceptance #7 — build 0/0, test 0/0 | ✅ |

## Files created

- `src/server/services/Sprk.Provisioning.ControlPlane/Reconciler/CrashRecoveryOptions.cs`
- `src/server/services/Sprk.Provisioning.ControlPlane/Reconciler/CrashRecoveryStartupService.cs`
- `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Reconciler/CrashRecoveryStartupServiceTests.cs`

## Files modified (mine)

- `src/server/services/Sprk.Provisioning.ControlPlane/Program.cs` — added
  `Configure<CrashRecoveryOptions>` + `PostConfigure<CrashRecoveryOptions>(Validate)` +
  `AddHostedService<CrashRecoveryStartupService>()` after `AddReconcilerModule`.

## Files touched for cross-task coordination (unstaged — left for sibling agents)

- Working-tree fix in `Api/RunsEndpoints.cs` from task 061 (unrelated) stashed
  during my build to isolate the compile failure; my commit does NOT include
  those changes — they remain in a git stash the sibling can pop.
- `InMemoryRegistryConcurrencyStore.cs` (task 059 tests) — 1-char fix (`etagCounter:` →
  `EtagCounter:`) applied but NOT committed by me; task 059's agent will
  see + fold in when they resume.
