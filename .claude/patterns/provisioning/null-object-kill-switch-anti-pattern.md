# Null-Object Kill-Switch Anti-Pattern Detection Pattern

> **Last Reviewed**: 2026-08-24
> **Reviewed By**: customer-provisioning-orchestration-r1 task 202 (SKELETON — task 203 fills)
> **Status**: Skeleton

## When
Adding a service registration inside a feature-flag `if (flag)` block in `*Module.cs` DI files, OR debugging "IX unresolved" runtime errors on services registered via ADR-032 P1/P2/P3.

## Read These Files (task 203 fills)
1. `.claude/adr/ADR-032-bff-nullobject-kill-switch.md` — canonical P1 (null impl outside gate) / P2 (real impl inside) / P3 (kill-switch on Options) pattern.
2. `.claude/constraints/bff-extensions.md` § F.1 — Asymmetric-Registration Tier 1.5 Anti-Pattern (BINDING per r2 task 081 / D-13).
3. `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` — worked example (2026-08-24 commit `e3a15db91` hoisted `IActionSeam` out of DocIntel/Analysis compound gate).
4. `src/server/api/Sprk.Bff.Api/Infrastructure/DI/CommunicationModule.cs` :195 — the unconditional consumer of `IActionSeam` that caused the SESSION 5 crash.
5. `tests/Spaarke.ArchTests/ADR032/*.cs` — existing forcing-function ArchTest (catches "kill-switch declared", but NOT the SESSION 5 class: "unconditional consumer + conditionally-registered dependency"). CLASS-B row B01 in task-202-punch-list adds that ArchTest.

## Constraints
- Every conditionally-registered service (inside `if (flag)`) MUST have a null-object counterpart registered UNCONDITIONALLY per ADR-032 P1/P2/P3.
- No consumer outside the gate may inject a conditionally-registered service directly — it must inject the interface and the kill-switch resolves to null-impl or real-impl at runtime.
- Interface + kill-switch declaration is the correct discipline; F.1 anti-pattern is missing that discipline.

## Key Rules (task 203 fills detail)
1. When authoring a new module: does the service have any consumer OUTSIDE its gate?
   - YES → register the interface UNCONDITIONALLY (outside gate) via ADR-032 P1/P2/P3.
   - NO → conditional registration inside gate is fine.
2. Verification: static-scan recipe from bff-extensions.md § F.1 — for each `if (options.EnableX)` block in `*Module.cs`, list every service registered inside; for each such service, grep all `*.cs` for ctor injection outside the gate.
3. Runtime crash `Unable to resolve service for type 'IX'` — always suspect asymmetric registration first. Grep `IX` in all Module.cs files; identify gates; identify consumers.
4. Fix mechanic: hoist the interface registration OUT of the gate; if implementation must vary by flag, use ADR-032 P3 (kill-switch on Options → null vs real impl).
5. IActionSeam case study (SESSION 5 / `e3a15db91`): consumer `CommunicationRiActionService` at `CommunicationModule.cs:195` injected `IActionSeam` unconditionally; provider registered `IActionSeam` inside compound `if (DocIntel && Analysis)` at `AnalysisServicesModule.cs:1425`. Fix: hoist to top-of-module unconditional block matching `IPinnedContextRepository` precedent. See task-202-punch-list.md § "IActionSeam case study" + CLASS-B row B01 (nightly ArchTest).
