# Null-Object Kill-Switch Anti-Pattern Detection Pattern

> **Last Reviewed**: 2026-08-25
> **Reviewed By**: customer-provisioning-orchestration-r1 task 203a per punch list row A07
> **Status**: Content filled (task 203a). Was skeleton from task 202.

## When

Load this pattern when:
- Adding a service registration inside a feature-flag `if (options.EnableX) { ... }` block in `*Module.cs` DI files.
- Debugging `Unable to resolve service for type 'IX' while attempting to activate 'YService'` runtime errors on services that use ADR-032 P1/P2/P3 pattern.
- Reviewing a PR that touches DI modules (BFF or L2 control-plane).
- Investigating an F.1 asymmetric-registration incident report.
- Authoring a new module where some services are feature-gated.

## Read These Files (canonical source)

1. `.claude/adr/ADR-032-bff-nullobject-kill-switch.md` — the canonical P1 (null impl outside gate) / P2 (real impl inside) / P3 (kill-switch on Options) pattern.
2. `.claude/constraints/bff-extensions.md` § F.1 — Asymmetric-Registration Tier 1.5 Anti-Pattern (BINDING per r2 task 081 / D-13). Includes the static-scan recipe.
3. `.claude/constraints/bff-extensions.md` § F.2 — Fixture-Config-FIRST Inspection Protocol (when a test is Skip'd suspecting DI issue, FIRST inspect fixture config).
4. `.claude/constraints/bff-extensions.md` § F.3 — Empirical-Reproduction-FIRST Protocol (before applying a ledger entry's recommended fix, hand-trace + reproduce empirically).
5. `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` — worked example. 2026-08-24 commit `e3a15db91` hoisted `IActionSeam` out of the DocIntel/Analysis compound gate. Read to understand the fix mechanic.
6. `src/server/api/Sprk.Bff.Api/Infrastructure/DI/CommunicationModule.cs` :195 — the unconditional consumer of `IActionSeam` that caused the SESSION 5 crash. The consumer side of the case study.
7. `tests/Spaarke.ArchTests/ADR032/*.cs` — existing forcing-function ArchTest (catches "kill-switch declared" but NOT the SESSION 5 class: "unconditional consumer + conditionally-registered dependency"). Class-B row B01 in task-202-punch-list adds that missing ArchTest.

## Constraints

- Every conditionally-registered service (inside `if (flag)`) MUST have a null-object counterpart registered UNCONDITIONALLY per ADR-032 P1/P2/P3.
- No consumer outside the gate may inject a conditionally-registered service directly — it must inject the INTERFACE, and the kill-switch resolves the interface to null-impl or real-impl at runtime.
- Interface + kill-switch declaration is the correct discipline; F.1 anti-pattern is missing that discipline.
- Fixture-config first (F.2): when a test suspects a DI issue, verify fixture config isn't the actual root cause BEFORE assuming DI is broken.
- Empirical reproduction first (F.3): before applying a ledger entry's fix, hand-trace the failure + reproduce it. Ledger entries can go stale; the current failure may have a different root cause.

## Key Rules (walk this for every module change)

1. **When authoring a new module**: does the service have any consumer OUTSIDE its gate?
   - **YES** → register the INTERFACE UNCONDITIONALLY (outside the gate) via ADR-032 P1/P2/P3. Real impl inside the gate; null impl outside; last-write-wins resolves to real when gate is on.
   - **NO** → conditional registration inside the gate is fine.
2. **Static-scan verification** (from bff-extensions.md § F.1): for each `if (options.EnableX)` block in `*Module.cs`, list every service registered inside; for each such service, grep all `*.cs` for ctor injection outside the gate. Any match = F.1 anti-pattern; fix per rule 4.
3. **Runtime crash `Unable to resolve service for type 'IX'`** — always suspect asymmetric registration FIRST. Grep `IX` in all Module.cs files; identify gates; identify consumers; check for the F.1 pattern.
4. **Fix mechanic**: hoist the INTERFACE registration OUT of the gate. If implementation must vary by flag, use ADR-032 P3 (kill-switch on Options → null vs real impl decided at runtime by the flag).
5. **IActionSeam case study** (SESSION 5 / commit `e3a15db91`):
   - Consumer: `CommunicationRiActionService` at `CommunicationModule.cs:195` injected `IActionSeam` unconditionally.
   - Provider: `IActionSeam` registered inside compound `if (DocIntel && Analysis)` at `AnalysisServicesModule.cs:1425`.
   - Symptom: BFF SIGABRT on Host.StartAsync when DocIntel or Analysis was disabled.
   - Fix: hoist `IActionSeam` to top-of-module unconditional block matching the `IPinnedContextRepository` / `IContextEventEmitter` / `IFileSummarizeAi` precedent.
   - Follow-on: nightly ArchTest that catches this specific class of anti-pattern (Class-B row B01 in punch list, filed against BFF-owning worktree).

## Anti-patterns this catches

- ❌ Registering a service conditionally + having ANY consumer outside the gate inject it → runtime SIGABRT when the flag is off.
- ❌ "The flag defaults to true, so it'll be fine in practice" → the flag exists to be togglable; toggling it off should NEVER SIGABRT the process.
- ❌ Adding a null-impl inside the gate too → both real and null registered when flag is on → last-write-wins is order-dependent and fragile.
- ❌ Skipping the fixture-config check (F.2) and rewriting DI code that's actually correct → wasted hours; the test fixture had a non-contract value all along.
- ❌ Applying a fix from a ledger entry without empirical reproduction (F.3) → fix targets stale symptoms; real root cause survives.

## Recovery recipes

- **BFF SIGABRT `Unable to resolve service for type 'IX'`**: grep `IX` in `**/*Module.cs`; identify the registration; check if it's inside `if (options.EnableY)`; grep `IX` in `**/*.cs` for consumers; check if any consumer is outside the gate. If yes → F.1 anti-pattern → hoist the registration.
- **Test intermittently fails on DI resolution**: apply F.2 first — inspect the test fixture config for non-contract values (e.g., a Skip'd test may have left a stale option). Only if fixture is clean, investigate DI.
- **Fix from a ledger entry doesn't resolve the symptom**: apply F.3 — hand-trace the current failure, don't assume the ledger's root cause is still current. File a path-b decision record if root cause differs.

## Worked example — IActionSeam hoist (SESSION 5 fix)

**Before** (commit `HEAD^` — the F.1 anti-pattern):

```csharp
// AnalysisServicesModule.cs
public static IServiceCollection AddAnalysisServices(this IServiceCollection services, ...)
{
    if (options.EnableDocIntel && options.EnableAnalysis)  // ← compound gate
    {
        // ~1400 lines of gated registrations, including...
        services.AddSingleton<IActionSeam, ActionSeam>();  // line 1425 — INSIDE the gate
    }
    return services;
}

// CommunicationModule.cs
public static IServiceCollection AddCommunicationServices(this IServiceCollection services, ...)
{
    // Line 195 — UNCONDITIONAL consumer of IActionSeam
    services.AddSingleton<ICommunicationRiActionService, CommunicationRiActionService>();
    // CommunicationRiActionService ctor: public CommunicationRiActionService(IActionSeam actionSeam, ...)
    return services;
}
```

When DocIntel or Analysis is disabled → `IActionSeam` never registered → `CommunicationRiActionService` fails to resolve at `Host.StartAsync` → BFF SIGABRT exit code 134.

**After** (commit `e3a15db91`):

```csharp
// AnalysisServicesModule.cs
public static IServiceCollection AddAnalysisServices(this IServiceCollection services, ...)
{
    // Top-of-module unconditional registrations (matches IPinnedContextRepository / IContextEventEmitter / IFileSummarizeAi precedent)
    services.AddSingleton<IActionSeam, ActionSeam>();  // HOISTED — line ~160

    if (options.EnableDocIntel && options.EnableAnalysis)
    {
        // ~1400 lines of gated registrations, WITHOUT the IActionSeam registration
        // (removed from line 1425)
    }
    return services;
}
```

**Follow-on ArchTest** (Class-B row B01, filed against BFF-owning worktree):

```csharp
// tests/Spaarke.ArchTests/ADR032/AsymmetricRegistrationTier15Tests.cs (planned)
[Fact]
public void No_Unconditional_Consumer_Depends_On_Conditionally_Registered_Service()
{
    // 1. Reflect all *Module.cs types; find AddXxxServices methods.
    // 2. For each method, walk the syntax tree to find `if (options.EnableXxx) { ... }` blocks.
    // 3. Collect all service types registered INSIDE those gates.
    // 4. For each gated service type, grep all *.cs files for ctor parameters of that type.
    // 5. If any consumer is OUTSIDE the gate (i.e., in a class that is registered unconditionally), FAIL.
    // Assertion: gated services must either have null-impl registered outside OR have no unconditional consumers.
}
```

Sub-rules for the ArchTest author: (a) verify with Roslyn `SyntaxTree` — regex scan is too fragile; (b) treat compound gates (`if (a && b)`) as OR of the composing conditions when computing "outside the gate"; (c) allow explicit `[AsymmetricRegistrationJustified("ADR-conflict-path-A-per-{doc}")]` attribute as an escape hatch for reviewed exceptions.

## Cross-refs

- Related ADR: ADR-032 (Null-Object Kill-Switch Pattern) — canonical P1/P2/P3
- Related constraint: `.claude/constraints/bff-extensions.md` § F.1 / F.2 / F.3
- Related pattern: [handler-registration-completeness.md](handler-registration-completeness.md) (analogous 3-file dance for handlers)
- Related lesson: SESSION 5 commit `e3a15db91` (IActionSeam hoist)
- Related follow-on: Class-B row B01 in `projects/customer-provisioning-orchestration-r1/notes/task-202-punch-list.md` (nightly ArchTest)
