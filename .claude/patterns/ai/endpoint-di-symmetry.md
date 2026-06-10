# Endpoint↔DI Registration Conditionality Symmetry Rule

> **Last Reviewed**: 2026-06-05
> **Reviewed By**: bff-ai-architecture-audit-r1 Migration PR #9
> **Status**: Verified (NEW load-bearing architectural rule)
> **Source**: [DR-008 §4.1](../../../projects/bff-ai-architecture-audit-r1/decisions/DR-008-di-configuration.md) · [canonical-architecture-decisions.md §4.1](../../../projects/bff-ai-architecture-audit-r1/notes/canonical-architecture-decisions.md)

## When
Whenever you add or modify a service registration in any `*Module.cs` file inside a feature-gated `if (flag) { ... }` block — OR when you map a new endpoint that injects such a service.

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` (`AddPublicContractsFacade` + `AddNullObjectsForCompoundOff`) — canonical symmetric pair
2. `src/server/api/Sprk.Bff.Api/Services/Ai/NullRagService.cs` — canonical P3 Fail-Fast Null peer (the double-gate gold standard)
3. `src/server/api/Sprk.Bff.Api/Endpoints/EndpointMappingExtensions.cs` — endpoint mapping (look for `if (flag) app.MapXxx()`)

## Constraints
- **`bff-extensions.md` §F.1**: asymmetric-registration anti-pattern is BANNED — if an endpoint maps unconditionally, its handler's injected services MUST also resolve unconditionally
- **ADR-032 P3**: Null-Object Fail-Fast — Null peer throws `FeatureDisabledException` so endpoint converts to 503 ProblemDetails (not 500)
- Recursive — the symmetry rule applies transitively through the entire ctor dependency chain (LATENT BUG #1 surfaced as exactly this kind of transitive break)

## Key Rules
- **Endpoint maps unconditionally** → service registration MUST be symmetric (real impl OR Null peer in BOTH gate branches)
- **Endpoint maps conditionally** (`if (flag) app.MapXxx()`) → service may be registered conditionally; mapping condition MUST match registration condition
- **Transitive enforcement**: if `IFoo` is registered in compound-ON, every ctor dep of `Foo` impl must also be available in compound-ON; the audit pattern is to register the Null peer for every transitively conditional dep
- **Verification**: at PR time, run the runtime fixture (W4-2) that boots the host with all 4 compound-gate combinations (`{Analysis,DocumentIntelligence} × {On,Off}`) and resolves every public endpoint's ctor params

## Anti-patterns (banned)
- Registering `IFoo` unconditionally while `IFoo`'s impl `Foo` requires conditional ctor deps (the LATENT BUG #1 pattern — fixed by PR #351)
- Defensive `IFoo? foo = null` to bypass the gate (defeats Null-Object kill-switch contract)
- Mapping endpoint unconditionally while its handler params are conditional (sites become 500 instead of 503 under OFF)
