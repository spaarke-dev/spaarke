# Task 061 — §4C rollback semantics — Deviations & Design Notes

**Task**: 061 — Implement §4C rollback semantics — 4-class failure taxonomy + Quarantined state + clear-quarantine audit
**Date**: 2026-08-18
**Rigor**: FULL
**Model / Effort**: sonnet / xhigh
**Branch**: work/customer-provisioning-orchestration-r1

---

## D-061-1 — `FailureClass` enum: stays in `Handlers/HandlerResult.cs`, NOT moved to `Rollback/`

**POML wording**: "New (or extract): `Rollback/FailureClass.cs`, ..."

**Decision**: KEEP the existing `FailureClass` enum in place at
`src/server/services/Sprk.Provisioning.ControlPlane/Handlers/HandlerResult.cs`
(namespace `Sprk.Provisioning.ControlPlane.Handlers`). Do NOT move to
`Rollback/FailureClass.cs`.

**Rationale (path C — pivot to comply)**:
- **Existing**: `FailureClass` is already referenced by **57 files** across the
  L2 project — every wave-3 handler (`H1..H14`), their rejection-code catalogs
  (`Bff­DeployRejectionCodes.cs`, `KvSecretsPopulationRejectionCodes.cs`,
  `AiSearchIndexRejectionCodes.cs`, etc.), their tests, and the discriminated
  `HandlerResult.Failure(FailureClass, ...)` union. Grep evidence:
  `grep -rl "FailureClass" src/server/services` → 57 hits.
- **Extension**: The enum is already used correctly by every consumer; the ONLY
  addition needed for §4C is the state-transition mapping — the classification
  itself is authoritative.
- **Cost-of-doing-nothing** (if moved): 57 file-touches to change `using
  Sprk.Provisioning.ControlPlane.Handlers;` to `using Sprk.Provisioning.
  ControlPlane.Rollback;`. Zero behavioral change; large surface change
  invites merge conflict with parallel Batch 4D siblings (059, 060) that
  already have their own hot-file coordination. NOT justified per CLAUDE.md §11.

**How `Rollback/` consumes it**: `RollbackTransitions.cs` uses
`using Sprk.Provisioning.ControlPlane.Handlers;` — one namespace hop. No
duplicate enum; no re-export type alias needed (C# has none anyway).

**Escalation check**: This is Path C (pivot to comply in spirit) per CLAUDE.md
§6.5 — no ADR conflict, just a lightweight scope-optimization deviation.

---

## D-061-2 — Reconciler `ApplyHandlerOutcomeAsync` shipped as INTERNAL seam, not yet wired into dispatch

**POML step 3**: "Modify StateReconcilerService (from task 058) to invoke
IFailureClassifier on handler outcome + apply the resulting Cosmos state
transition per §4C table."

**What shipped**: `StateReconcilerService.ApplyHandlerOutcomeAsync(run, ifMatchEtag, HandlerResult, handlerId, ct)`
— internal method that:
1. Resolves `IFailureClassifier` + `IProvisioningRunRepository` + `IHandlerEnqueuer` from the per-tick DI scope.
2. On `HandlerResult.Success`: returns `HandlerOutcomeApplied(TargetStatus=run.Status, Reenqueued=false, FailureClass=null)` — reconciler does NOT double-write on Success (handlers own the CompletedPhases append per §5.1).
3. On `HandlerResult.Failure`: calls `RollbackTransitions.MapToRunStatus` + `ShouldReEnqueue`, mutates run in-memory (Status + CurrentPhase + ErrorDetail + Quarantine metadata on QuarantineRequired + CompletedOn on terminal transitions), calls `ReplaceRunAsync` with ETag; on RetryableWithCleanup also re-enqueues via `IHandlerEnqueuer` (deterministic MessageId path re-fires; SB dedup + Redis idempotency own retry-safety per ADR-036).

**Why internal, not integrated into the tick loop**: The reconciler's current dispatch model per task 058 is intake-shaped — it ENQUEUES handlers based on DAG completion but does NOT synchronously execute them. Handlers execute in the BFF's `IJobHandler` infrastructure (out-of-process per ADR-036) and self-write their outcomes to Cosmos (`CompletedPhases` on Success; direct Status write on Failure — see `H2bAiSearchIndexHandler.FailAsync` for the current pattern). The reconciler consumes those Cosmos state changes on its next tick.

`ApplyHandlerOutcomeAsync` is the SINGLE authoritative implementation of the §4C table for any future consumer:
- A follow-on task that moves BFF-side handler-outcome interpretation into L2 (out of scope for task 061).
- Handlers refactored to return their result via a Service Bus reply message instead of self-writing (deferred).
- Test dispatchers driving the reconciler synchronously.

Value delivered by task 061: **single source of truth for the §4C mapping**. Every future consumer of the state transitions calls one place; a new failure class added to the enum WITHOUT a corresponding switch branch fails the build at CS8524 (warning-as-error via `TreatWarningsAsErrors` inherited from `Directory.Build.props`).

**Handlers still contain the current per-file transition ternary**: 17 handlers still have `Status = failureClass == FailureClass.QuarantineRequired ? RunStatus.Quarantined : RunStatus.Failed;` and equivalent `Quarantine = new QuarantineInfo { ... }` blocks in their `FailAsync` helpers. **Retrofitting the handlers to use `RollbackTransitions` is DEFERRED to a follow-on task** — the current pattern is functionally correct + already passing tests; a bulk refactor is orthogonal to task 061's scope (state-transition table authority + endpoint wiring) and would touch 17 handler files + their tests. Left as a `refactor: handlers → RollbackTransitions` opportunity.

---

## D-061-3 — `QuarantineClearService` synchronously transitions Cosmos state (endpoint no longer just enqueues)

**Task 057 shipped shape**: `POST /api/runs/{id}/clear-quarantine` validated + enqueued + audit-logged. State transition was to be applied by task 061 downstream via reconciler dispatch of the enqueued envelope.

**What shipped in task 061**: The endpoint now invokes `IQuarantineClearService.ClearAsync(customerId, runId, reason, actorObjectId, ct)` SYNCHRONOUSLY before enqueue + audit-log:
- The service performs the Cosmos state transition (`Quarantined → Failed` + `QuarantineInfo.State = Cleared` + `ClearedBy` + `ClearedAt`) using ETag-safe `ReplaceRunAsync`.
- Endpoint interprets the discriminated `QuarantineClearResult` (Success / NotFound / Conflict / ConcurrencyConflict) into HTTP status:
  - `NotFound` → 404
  - `Conflict(currentStatus)` → 409 with wrong-state detail message
  - `ConcurrencyConflict(current)` → 409 with concurrent-writer detail message
  - `Success` → continues to enqueue + audit-log + 202
- Enqueue + audit-log fire ONLY on Success (regression from prior "enqueue happens always" would be a spec FR-24 violation per acceptance §6/§7).

**Rationale for synchronous transition** (vs "enqueue then reconciler transitions"):
- Operator-facing endpoint: the 202 semantics MUST reflect completed state transition intent — a 202 followed by silent no-op (reconciler couldn't apply because run wasn't Quarantined) would be a confusing UX gap. The 409 wrong-state signals the operator immediately.
- Cosmos + audit-log write are BOTH synchronous already for POST /api/runs; parity with the create-run endpoint's synchronous Cosmos write posture.
- The reconciler retains its intake-shaped scope — no need to introduce a "consume clear-quarantine envelope" handler in wave C5. The envelope is retained (fire-and-forget) for observability + potential future audit-cleanup workers.

---

## D-061-4 — Customer-run-guard release deferred to task 059's post-clear hook

**Spec FR-24 SCOPE**: "clear-quarantine ... releases the same-customer serialization guard (via task 059 hook) so a new run can start against the same customerId."

**What shipped**: `QuarantineClearService` transitions Cosmos state (`Quarantined → Failed`) which is the source-of-truth signal for task 059's `ICustomerRunGuard.TryAcquireAsync`. The guard reads the winning run's Cosmos status; after clear-quarantine, the winning run is `Failed` (not `Quarantined`), so `TryAcquireAsync` no longer returns `AcquireConflictReasonCodes.Quarantined` for that customer.

**What did NOT ship** (deferred):
- Direct `ICustomerRunGuard.ReleaseAsync` call from `QuarantineClearService`. Task 061's `Rollback/` code does NOT reference `ICustomerRunGuard` (`Sprk.Provisioning.ControlPlane.Concurrency` namespace) — the design intentionally couples via the Cosmos state doc, not via cross-module method call.
- `RollbackTransitions.ShouldReleaseCustomerGuard(FailureClass)` returns `false` for `QuarantineRequired` (spec FR-24 SCOPE keeps guard held); `true` for `SuccessfulButDrifted` (Completed transition). This is available for a follow-on hook if task 059's guard needs an explicit `Release` call — currently the Cosmos-state read is sufficient.

**Escalation check**: Not required — the spec's "via task 059 hook" is satisfied by task 059 reading the Cosmos state that task 061 writes. Zero coupling in code; loose coupling via shared state store.

---

## D-061-5 — `RollbackTransitions` returns `RunStatus.Failed` for BOTH Resumable AND RetryableWithCleanup

**Design §4C state-transition table** (design.md lines 204-209):
- `Running → Failed` (handler threw, retryable)
- `Running → Quarantined` (handler wrote un-recoverable partial state)
- `Failed → Running` (operator called resume_run)
- `Quarantined → Cancelled` (operator explicitly abandons)

**Decision**: BOTH `FailureClass.Resumable` AND `FailureClass.RetryableWithCleanup` map to `RunStatus.Failed` in `RollbackTransitions.MapToRunStatus`. The difference between the two classes is captured by `RollbackTransitions.ShouldReEnqueue`:
- `Resumable → ShouldReEnqueue = false` (operator resumes via POST /api/runs/{id}/resume)
- `RetryableWithCleanup → ShouldReEnqueue = true` (auto-retry — handler idempotency owns cleanup)

**Rationale**: The Cosmos `RunStatus` enum has no distinct "auto-retry-in-flight" value; both classes are terminal-in-status but differ in follow-on action. The state-transition table's "Running → Failed" row covers both. The `ShouldReEnqueue` boolean encodes the retry-policy distinction cleanly without expanding the RunStatus enum.

---

## D-061-6 — `SuccessfulButDrifted` mapped to `RunStatus.Completed`, not to `Failed`

**Design §4C class 4**: "Handler completed successfully but downstream config drift ... invalidates the state. H13 acceptance detects; operator re-runs affected phases with `resumeFromPhase` param."

**Decision**: `RollbackTransitions.MapToRunStatus(SuccessfulButDrifted) → RunStatus.Completed` (NOT Failed).

**Rationale**: The class name is literal — "Successful-but-drifted" means the handler succeeded; drift was detected AFTER the fact by H13's acceptance-sample verifier. The run's overall state IS Completed (no failed handler); the drift is a separate signal that operator must reconcile via `POST /api/runs/{id}/resume?resumeFromPhase=X`. Mapping to `Failed` would misrepresent the DAG state (no handler failed).

Handlers that detect drift SHOULD NOT return `HandlerResult.Failure(SuccessfulButDrifted, ...)` — they should return `Success` + write a drift signal to a separate metadata field. `SuccessfulButDrifted` in `FailureClass` is present per the design.md §4C taxonomy but semantically represents a POST-completion re-classification signal, not a handler-level failure. This is documented for future consumers.

---

## Summary — what shipped

**New files** (`src/server/services/Sprk.Provisioning.ControlPlane/Rollback/`):
- `RollbackTransitions.cs` — pure-static §4C table (3 exhaustive-switch methods).
- `IFailureClassifier.cs` + `FailureClassifier.cs` — policy seam for HandlerResult.Failure pass-through + escaped-exception SAFE-default mapping.
- `IQuarantineClearService.cs` + `QuarantineClearService.cs` — Quarantined → Failed transition with 4 discriminated-union result types.
- `RollbackModule.cs` — DI composition extension (`AddRollbackModule`).

**Modified files**:
- `Program.cs` — `AddRollbackModule()` + `using Sprk.Provisioning.ControlPlane.Rollback;`. Placement after `AddHostedService<CrashRecoveryStartupService>()` (last DI hunk); no conflict with parallel Batch 4D siblings (059/060 land above).
- `Api/RunsEndpoints.cs` — `using Sprk.Provisioning.ControlPlane.Rollback;`; `Conflict(HttpContext, string)` helper; `ClearQuarantine` handler now invokes `IQuarantineClearService`; returns 409 on wrong-state OR concurrent-writer; enqueue + audit-log fire only on Success. `+ .ProducesProblem(StatusCodes.Status409Conflict)` metadata.
- `Reconciler/StateReconcilerService.cs` — added `using Handlers + Repositories + Rollback` namespaces + internal `ApplyHandlerOutcomeAsync` method + internal `HandlerOutcomeApplied` record for outcome-transition wiring (see D-061-2 for the internal-scope rationale).

**New tests** (`src/server/services/Sprk.Provisioning.ControlPlane.Tests/Rollback/`):
- `RollbackTransitionsTests.cs` — 4-class × 3-method exhaustive-mapping tests + enum-coverage tests + undefined-enum UnreachableException tests. 12 test methods; ~28 individual test cases.
- `FailureClassifierTests.cs` — pass-through + escaped-exception mapping + safe-default tests. 6 test methods; ~13 individual test cases.
- `QuarantineClearServiceTests.cs` — happy + wrong-state (6 statuses via theory) + not-found + concurrent-conflict + missing-reason + null-actor + time-discipline tests. 12 test methods; ~19 individual test cases.

**Modified tests** (`src/server/services/Sprk.Provisioning.ControlPlane.Tests/Api/RunsEndpointsTests.cs`):
- Added `ClearQuarantine_OnNonQuarantinedRun_Returns409_WrongState_AndDoesNotAuditLog` — 5-status theory (Running / WaitingOnGate / Completed / Failed / Cancelled). Verifies 409 status + no enqueue + no audit-log on wrong-state.

**Build**: L2 project + tests both compile 0/0. Full test suite: **618 tests pass**, 0 fail, 0 skipped.

**Baseline preserved**: All pre-existing task-057 + task-058 + task-059 + task-060 tests continue to pass. The existing `ClearQuarantine_WithReasonAndOperator_Returns202_AndAuditLogsActorTidAndReason` happy-path test still passes because `QuarantineClearService` correctly interprets the seeded `Status = Quarantined` and transitions it, letting the audit-log + enqueue path continue.
