# Task 204d — Path decision: Path SPLIT (verified already complete)

**Date**: 2026-08-26
**Task POML**: `projects/customer-provisioning-orchestration-r1/tasks/204d-classB-staging-slot-topology-split.poml`
**Rigor**: FULL (opus / xhigh)
**Owner directive**: SESSION 11 (2026-08-26) — "we need to complete ALL the pre-requisites so that as far as we know the full E2E is fully functional" — this is the explicit PRE-186 authorization the POML's `deferred-post-186` escalation trigger required.

---

## Decision

**Path SPLIT — verified ALREADY COMPLETE** (Wave G-1 tasks 100 / 101 / 102, 2026-08-19).

This task does NOT re-execute a structural refactor. Its scope collapses to:
1. Verification of the split (Step 1 of the POML).
2. A build-time guard test asserting the split invariant does not regress (Step 5).
3. Design-doc + punch-list annotation of the outcome (Steps 3 / 7).

Rationale: the POML enumerates two acceptable resolutions (Path SPLIT or Path FLAGS). Path SPLIT strictly dominates Path FLAGS on architectural cleanliness *when it is already done*, and dominates on §11 minimalism because introducing three new slot-sticky flags to a Worker that no longer has a staging slot would be pure surface expansion for zero behavioral change.

---

## Grep evidence (Step 1)

### Project structure — 4 csproj files present

```
src/server/services/Sprk.Provisioning.ControlPlane.Api/Sprk.Provisioning.ControlPlane.Api.csproj
src/server/services/Sprk.Provisioning.ControlPlane.Core/Sprk.Provisioning.ControlPlane.Core.csproj
src/server/services/Sprk.Provisioning.ControlPlane.Tests/Sprk.Provisioning.ControlPlane.Tests.csproj
src/server/services/Sprk.Provisioning.ControlPlane.Worker/Sprk.Provisioning.ControlPlane.Worker.csproj
```

Split lineage cited in-line by both `.Api` and `.Worker` csproj headers (task 100, DS-3 §3 Option 2 owner-lock, 2026-08-19).

### `.Api` composition root — REST intake only, zero handler execution

`src/server/services/Sprk.Provisioning.ControlPlane.Api/Program.cs` registers:
- `AddAuthModule` (JWT bearer + Operator/Reader policies)
- `AddSwaggerModule`
- `AddCosmosModule`
- `AddServiceBusModule` (registers `IHandlerEnqueuer` — the enqueue side of the wire; NOT the drain side)
- `AddTelemetryModule`
- `AddCustomerRunGuard` (I5 same-customer guard used by intake endpoints)
- `AddRollbackModule` (used by `POST /api/runs/{id}/clear-quarantine`)

Endpoint mappings: `MapHealthEndpoints`, `MapRunsEndpoints`, `MapRunLogsEndpoints`.

**Zero `AddHostedService` calls, zero `BackgroundService` subclasses, zero handler DI**. Grep confirms — the only `BackgroundService` occurrences in `.Api` are comments explaining that background work lives elsewhere (`Program.cs:30`, `Api/RunsEndpoints.cs:23`).

Grep of the four `.Core` modules `.Api` transitively pulls in for `AddHostedService` / `IHostedService` / `BackgroundService`:
- `Core/Concurrency/CustomerRunGuardModule.cs` — zero hits
- `Core/Rollback/RollbackModule.cs` — zero hits
- `Core/Modules/*.cs` (Cosmos, ServiceBus, Telemetry) — zero hits

`.Api`'s `WebApplicationFactory<Program>.Services` does NOT resolve any handler execution machinery.

### `.Worker` composition root — background processing only

`src/server/services/Sprk.Provisioning.ControlPlane.Worker/Program.cs` (1,046 lines) registers:
- Shared infra: `AddCosmosModule`, `AddServiceBusModule`, `AddTelemetryModule` (parity with `.Api` for the shared clients).
- 21 dispatchable `IProvisioningHandler` keyed registrations (H0, H0.5, H1, H2a, H2b, H3, H4, H4-shared, H4b, H5, H6, H7, H8, H9, H10, H11, H12a, H12b, H12c, H13, H14).
- Reconciler: `AddReconcilerModule` → `AddHostedService<StateReconcilerService>` inside `Core/Reconciler/ReconcilerModule.cs:98`.
- Crash recovery: `AddHostedService<CrashRecoveryStartupService>` (Worker `Program.cs:963`).
- Dispatcher: `AddHostedService<ProvisioningHandlerDispatcher>` (Worker `Program.cs:1013`) — the load-bearing `ServiceBusSessionProcessor` drain loop.

Endpoint surface: anonymous `/healthz` + `/ping` only — no auth, no swagger, no audit middleware.

### Bicep topology — Worker deploys slotless on the SAME plan

`infrastructure/bicep/modules/controlplane-worker-app-service.bicep`:
- Declares `Microsoft.Web/sites@2023-01-01` with `kind: 'app,linux'`, `serverFarmId: appServicePlanId` (SAME plan as `.Api`, $0 marginal cost — DS-3 §3 Option 2).
- **NO `Microsoft.Web/sites/slots` resource declared** — the site is intentionally slotless.
- File header ("WHY A NEW MODULE") explicitly cites the staging-slot shadow-worker defect DS-3 §1.3 as the reason for the dedicated module: "Parameterizing those away inside the existing module would re-open the shadow-worker defect task 100 was created to close."
- `platform-controlplane.bicep:447` wires the module.

`infrastructure/bicep/modules/controlplane-app-service.bicep` (the `.Api` module — pre-existing) continues to declare the staging slot for blue-green REST-deploy — which is correct, because `.Api` has no handler execution surface: a staging `.Api` slot serves anonymous `/ping` + accepts operator HTTP but cannot double-consume SB or double-write Cosmos (no dispatcher, no reconciler, no crash-recovery).

### Why the defect is structurally closed

The DS-3 §1.3 shadow-worker defect required *four* things simultaneously:
1. A staging slot on the host running BackgroundServices, **AND**
2. Both slots sharing config, **AND**
3. Reconciler timer polling Cosmos on both slots, **AND**
4. Dispatcher competing for SB session locks on both slots.

The split eliminates conditions (1), (3), and (4) at the host boundary: the *only* host with reconciler/dispatcher/crash-recovery is `.Worker`, and `.Worker` has no staging slot. `.Api` retains a staging slot for blue-green REST deploys but has none of (3)/(4) — a staging `.Api` slot has zero handler-execution attack surface.

---

## Path FLAGS was NOT chosen — rationale

Path FLAGS would add three `Enabled` config flags to the Worker (`Dispatcher:Enabled` / `Reconciler:Enabled` / `CrashRecovery:Enabled`), guarding each `AddHostedService` call and marking each Bicep app-setting `slotSetting: true`. This was the intended alternative when the split had not yet been done.

Post-Wave-G-1, applying Path FLAGS would:
- Add three new IOptions surfaces with fail-fast validators to close the ADR-032 F.1 "asymmetric conditional-registration" hole (per `bff-extensions.md` §F.1) — pure new surface, no behavioral gain.
- Require a staging slot on the Worker Bicep to make the flags meaningful — which is the exact topology change the split was created to *avoid*.
- Add another decision axis for operators (which flags to flip on which slot) where none exists today.

CLAUDE.md §11 minimality: Path FLAGS fails the three-question template — question 3 (cost-of-doing-nothing) yields "none" because the split already eliminates every failure mode Path FLAGS would guard against. Path SPLIT wins.

---

## Deliverables of this task (no structural refactor executed)

| # | Deliverable | Location |
|---|---|---|
| 1 | This path-decision doc | `projects/customer-provisioning-orchestration-r1/notes/task-204d-path-decision.md` |
| 2 | Build-time guard — asserts `.Api` DI graph contains ZERO `IHostedService` entries + zero `IProvisioningHandler` keyed registrations | `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Dispatch/ApiHostShadowWorkerGuardTests.cs` (new) |
| 3 | Punch-list B11 row annotation | `projects/customer-provisioning-orchestration-r1/notes/task-202-punch-list.md` |
| 4 | Design.md §4.2a sub-section (`.Api` / `.Worker` split contract) | `projects/customer-provisioning-orchestration-r1/design.md` |

Bicep changes: **NONE** — the `.Worker` module is already slotless; no FR-35 pre-check is required because no live app-setting is renamed or deleted.

---

## Owner sign-off trail

- POML `deferred-post-186` gate: satisfied by SESSION 11 owner directive.
- POML `Step-2-ambiguity` escalation: NOT triggered — evidence is unambiguous (Path SPLIT strictly dominates).
- POML `Path-SPLIT-circular-dep` escalation: NOT triggered — REST endpoints (`.Api`) do NOT need the dispatcher runtime; they enqueue via `IHandlerEnqueuer` (a shared `.Core` type registered in both hosts) and never invoke a handler directly.
- POML `FR-35 pre-check` escalation: NOT triggered — zero Bicep changes.
