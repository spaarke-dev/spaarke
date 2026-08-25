# Handler Registration Completeness Pattern

> **Last Reviewed**: 2026-08-24
> **Reviewed By**: customer-provisioning-orchestration-r1 task 202 (SKELETON — task 203 fills)
> **Status**: Skeleton

## When
Adding a new `IProvisioningHandler` implementation, or debugging "handler ID X not resolvable" at dispatch.

## Read These Files (task 203 fills)
1. `src/server/api/Sprk.Provisioning.ControlPlane.Core/Handlers/HandlerIds.cs` — const list of Dispatchable IDs.
2. `src/server/api/Sprk.Provisioning.ControlPlane.Core/DI/HandlerDispatchRegistrationModule.cs` — keyed forwarders.
3. `src/server/api/Sprk.Provisioning.ControlPlane.Worker/Program.cs` — concrete DI registrations.
4. `tests/…/HandlerRegistrationCompletenessTests.cs` — ArchTest that asserts every Dispatchable ID resolves to a keyed `IProvisioningHandler`.

## Constraints
- Every new handler is a **3-file dance** (HandlerIds + HandlerDispatchRegistrationModule + Worker/Program.cs). Missing any of the three → dispatch throws at runtime.
- `HandlerRegistrationCompletenessTests` MUST pass (21/21 at time of task 201 landing).
- Handler contract: `IProvisioningHandler` returns `HandlerResult` (Success | Failure | Deferred | Rollback).

## Key Rules (task 203 fills detail)
1. Add const to `HandlerIds` first; test compilation surfaces missing keyed forwarder.
2. Add keyed forwarder in `HandlerDispatchRegistrationModule`; test compilation surfaces missing concrete registration.
3. Add concrete DI in `Worker/Program.cs`; `HandlerRegistrationCompletenessTests` now passes N+1 / N+1.
4. If handler is feature-gated (per ADR-032): register kill-switch Null impl outside the gate; conditional real impl inside. See [null-object-kill-switch-anti-pattern.md](null-object-kill-switch-anti-pattern.md).
