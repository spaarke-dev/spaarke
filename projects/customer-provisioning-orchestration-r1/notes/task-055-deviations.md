# Task 055 (H13 E2E acceptance-gate) — Deviations Log

**Task**: `projects/customer-provisioning-orchestration-r1/tasks/055-implement-h13-e2e-acceptance-gate-handler.poml`
**Handler**: `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/E2EAcceptance/H13E2EAcceptanceGateHandler.cs`
**Wave**: C4 Batch 4E — Handler Implementations (serial after 062)
**Rigor**: FULL
**Date**: 2026-08-18

---

## Path A — Documented exceptions (per CLAUDE.md §6.5)

### D-1. Cost drift > 20% is ADVISORY-WARN by default (NOT fail-run)

**ADR / spec citation**: spec.md §15 #14 + spec.md § Unresolved Questions. Task POML mandatory pre-work notes: *"cost drift > 20% is currently ambiguous (fail-run or advisory per spec.md Unresolved Questions) — implement as advisory-warn by default with explicit escalation trigger."*

**Deviation**: H13 emits an advisory warning on the run's `ErrorDetail` (prefixed `[advisory: cost-drift] ...`) + writes the `h13-cost-envelope` gate as `Verified` (not `Failed`) when observed drift exceeds `E2EAcceptanceOptions.CostDriftAdvisoryThreshold` (default 20 %). The Ready transition still happens if all other gates green. Opt-in fail-run via `E2EAcceptance:CostDriftFailsRun = true` (config flag) → H13 fails `QuarantineRequired` with `CostDriftExceeded`.

**Rationale**: Path A (project-scoped exception). Cost drift alone is not a customer-visible defect — it's a business signal for operator attention. Failing the run on advisory drift would silently strand runs whose only issue was over-baseline spend (e.g. a legitimately-more-expensive customer profile). The opt-in fail-run flag preserves the ability to enforce it once the owner resolves the Unresolved Question.

**Owner escalation**: needed before Phase F acceptance (task 089) to confirm whether the default should flip to fail-run at production launch.

### D-2. TrapVerifier + InvariantVerifier ship as PLACEHOLDER impls (return InfraFault)

**ADR / spec citation**: task POML escalation trigger — *"Any invariant/trap verifier requires access to a live customer stamp that isn't reachable from the test host — CAN be documented as 'verified via integration test at Phase F acceptance (task 089)' if unit test can't exercise."*

**Deviation**: The Wave-C4 production implementations of `IE2ETrapVerifier` + `IE2EInvariantVerifier` (`PlaceholderTrapVerifier` + `PlaceholderInvariantVerifier`) return `InfraFault` for every trap / invariant. The H13 handler therefore classifies `Resumable` when invoked against a live customer stamp without the real probe seams in place.

**Rationale**: Path A. Task 055's scope stops at handler orchestration + unit-test surface. Each of the 6 T1–T6 traps requires a distinct live-probe seam (Graph, ARM, Exchange PS, KV, SPE audit-log) whose live-Azure coverage belongs to the Phase F acceptance suite (task 089 — full end-to-end run against a real customer stamp). Same reasoning for the 5 I1–I5 invariants. Writing 11 individual live-probe impls inside this task would balloon scope well beyond the POML's stated bounds + duplicate work the Phase F project (task 089) is explicitly scoped for.

**Swap-out path**: DI registration lines in `E2EAcceptanceModule.cs` — swap `PlaceholderTrapVerifier` / `PlaceholderInvariantVerifier` for real impls without touching the H13 handler or its tests. Handler exercises the FAIL branches via unit-test fakes today; the real live-probe impls will exercise the SAME `TrapVerificationOutcome.Failed` / `InvariantVerificationOutcome.Failed` shape once wired.

**Follow-on tracking**: Phase F task 089 owns the full-run live acceptance; the individual live-probe seams should be scoped as sub-tasks of 089 when its handoff document is authored.

### D-3. `IRegistrySetupStatusUpdater` ships as a Wave-C4 placeholder (returns Success without Dataverse write)

**ADR / spec citation**: parity with task 042 H0.5's `IDataverseEnvironmentRegistryClient` placeholder-then-real evolution (Wave-C4 scaffold → Wave-C5 real impl).

**Deviation**: `DataverseRegistrySetupStatusUpdater` logs prominently + returns `Success` WITHOUT issuing a real Dataverse Web API PATCH. Real Wave-C5 impl swaps via DI registration change only.

**Rationale**: Path A. Live Dataverse Web API wiring against the Spaarke-internal registry environment requires the L2 Dataverse client seam that lands in Wave C5 (parity with H0.5). Landing a placeholder-then-real evolution is the ADR-010-approved pattern already used elsewhere in this project.

**Swap-out path**: Same as above — a single-line DI change in `E2EAcceptanceModule.cs`.

### D-4. `IE2EValidationRunner` production impl assumes the Phase-B extended script exists on disk

**ADR / spec citation**: POML mandatory pre-work #8 — *"READ `scripts/Validate-DeployedEnvironment.ps1` — see if it exists + what Phase B extension needs. If missing, note as escalation."*

**Status observed**: The r2-shipped `scripts/Validate-DeployedEnvironment.ps1` exists in the tree (verified at task authoring time) and covers BFF /healthz + env-var + CORS + dev-leakage + naming-conformance. The Phase-B extension (sample analysis + sample doc upload+index + workspace-layout render + wizard field-map) is NOT yet in the script — those checks are TRACKED as a separate task in the same wave.

**Deviation**: `ValidateDeployedEnvironmentScriptRunner` invokes the script AS-IS and parses whatever `PASS:` / `FAIL:` / `SKIP:` markers the current version emits. It surfaces the `Skipped` list on `E2EValidationOutcome.Success` so the operator sees which sample checks were validated vs deferred. When the Phase-B extension lands, the wrapper picks up the new markers automatically (filesystem-based discovery — no wrapper code change needed).

**Rationale**: Path A. Both the wrapper and the script extension are separate seams; blocking task 055 on the script extension would create a false coupling. The runner classifies Failure as `QuarantineRequired` (silent-fail actually observed) and infra-fault as `Resumable` — the interim posture is safe.

---

## Path C — Pivot-to-comply items (documented per CLAUDE.md §6.5)

### C-1. Aggregate-gate pattern — H13 collects ALL 6 outcomes before deciding classification

**ADR citation**: ADR-004 IJobHandler shape ("one message one handler one outcome").

**Pivot**: H13 is ONE IProvisioningHandler-shape impl that internally fans out to 6 collaborator seams + aggregates outcomes into ONE §4C classification per invocation — the "one outcome" contract is preserved. Individual seam outcomes are NOT independently written to Cosmos; the parent handler owns the single Cosmos write. Same shape as H14's parent handler (task 073).

**Deterministic aggregation priority** (declared in `H13E2EAcceptanceGateHandler.cs` step 9):
1. Trap Failed → QuarantineRequired + `TrapT{N}Failed`
2. Invariant Failed → QuarantineRequired + `InvariantI{N}Failed`
3. Naming FAILED → QuarantineRequired + `NamingConformanceFailed`
4. Extended validate FAILED → QuarantineRequired + `ExtendedValidationFailed`
5. Cost drift (opt-in fail-run) → QuarantineRequired + `CostDriftExceeded`
6. Trap InfraFault → Resumable + `TrapVerifierInfraFault`
7. Invariant InfraFault → Resumable + `InvariantVerifierInfraFault`
8. Cost query infra fault → Resumable + `CostQueryInfraFault`

**Unit test AC-21 asserts this priority explicitly** (trap Failed short-circuits over invariant Failed when both are present).

### C-2. `INamingConformanceChecker` is a NEW read-only seam distinct from H9's wrapped call

**ADR citation**: CLAUDE.md §11 component justification.

**Pivot**: `INamingConformanceChecker` is a new narrow seam rather than reusing H9's `IR3GateVerifier.NamingConformance` gate outcome. Justification (three questions per §11):

1. **Existing**: H9 already runs `naming-conformance-check.ps1` as part of its 5-gate pre-swap verification. `Validate-DeployedEnvironment.ps1` (Phase B) also wraps a call to it (per r1 task 021).
2. **Extension**: Cannot extend either. H9's gate produces a `R3GateStatus` that is `Skipped` when the script is not yet present — losing the distinction between "no violation" and "not run". H13 needs SC #17 as its OWN explicit pass/fail boundary — the extended validate script's wrapped call is INSIDE `IE2EValidationRunner`, so a failure there manifests as `ExtendedValidationFailed`, not `NamingConformanceFailed`.
3. **Cost-of-doing-nothing**: without a separate invocation, an operator reading the H13 run doc could not distinguish a naming violation caught DURING the extended validate script's wrapped call vs post-swap drift the independent invocation caught.

Distinct invocations produce distinct rejection codes, which is the intent of SC #17.

---

## Deferrals / follow-ons

| Item | Reason | Follow-on |
|---|---|---|
| Live-probe impls for T1–T6 traps | Requires customer-stamp reachability from L2 (see D-2) | Phase F task 089 sub-tasks |
| Live-probe impls for I1–I5 invariants | Requires customer-stamp reachability (see D-2) | Phase F task 089 sub-tasks |
| `DataverseRegistrySetupStatusUpdater` real Web API PATCH | Requires L2 Dataverse client seam (Wave-C5) | Wave-C5 task alongside H0.5 real registry client |
| Phase-B extended sample checks in `Validate-DeployedEnvironment.ps1` | Tracked as separate task in same wave (see D-4) | Wave-C4 script extension task |
| Cost drift default fail-run vs advisory | Unresolved Question in spec.md — needs owner decision (see D-1) | Owner escalation before Phase F |

---

## Summary of files touched

**New — H13 handler + 12 collaborator files (`Handlers/E2EAcceptance/`)**:
- `H13E2EAcceptanceGateHandler.cs`
- `H13AcceptanceOptions.cs`
- `H13RejectionCodes.cs` (contains `H13Rejections` + `H13Gates`)
- `IE2EValidationRunner.cs` + `ValidateDeployedEnvironmentScriptRunner.cs`
- `IE2ETrapVerifier.cs` + `PlaceholderTrapVerifier.cs`
- `IE2EInvariantVerifier.cs` + `PlaceholderInvariantVerifier.cs`
- `INamingConformanceChecker.cs` + `NamingConformanceScriptRunner.cs`
- `ICostEnvelopeChecker.cs` + `AzCliCostEnvelopeChecker.cs`
- `IRegistrySetupStatusUpdater.cs` + `DataverseRegistrySetupStatusUpdater.cs`
- `E2EAcceptanceModule.cs`

**Modified**:
- `Program.cs` — one `using` + one `builder.Services.AddH13E2EAcceptanceGateHandler(...)` line + block comment justifying placement + ADR tensions.

**New — Tests (`Sprk.Provisioning.ControlPlane.Tests/Handlers/`)**:
- `H13E2EAcceptanceGateHandlerTests.cs` — 44 tests covering all 22 ACs (AC-5 + AC-6 + AC-19 + AC-20 are `[Theory]` blocks so surface individually per case).

**Build + test results**:
- `dotnet build src/server/services/Sprk.Provisioning.ControlPlane/`: 0 warnings, 0 errors.
- `dotnet test src/server/services/Sprk.Provisioning.ControlPlane.Tests/`: 662 passed / 0 failed / 0 skipped (662 = 618 pre-055 + 44 new H13 tests).
