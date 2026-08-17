# Design — Dataverse Access-Layer Unification (R1)

> **Status**: INITIALIZED (design only) · **Surface**: `Spaarke.Dataverse` + BFF · **Risk**: HIGH
> **Grounding**: `../code-quality-and-assurance-r3/notes/red-item-analyses/RED-4-dataverse-two-stack-ASSESSMENT.md` (Fable-verified)

## Hot-Path Declaration (CLAUDE.md §10)

```xml
<hot-path-declaration>
  <bff>Y</bff>                <!-- Spaarke.Dataverse shared lib consumed by BFF; DI in GraphModule -->
  <spaarke-ai>N</spaarke-ai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>Y</skill-directives> <!-- authors an ADR (.claude/adr + docs/adr), main-session-only -->
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

## Problem (verified)

Two `IDataverseService` impls: `DataverseServiceClientImpl` (SDK, primary) + `DataverseWebApiService` (REST,
serves events/field-mapping/impersonation/POA). The split is **historical** (REST built to avoid WCF on .NET 8)
— the SDK path already does raw OData PATCH (`ExecuteWebRequest`) AND `CallerId` impersonation
(`DataverseServiceClientImpl.cs:1875-1884`), so it is not a hard capability boundary. The split produces a
**split-brain routing trap** (one composite interface across two impls; a mis-route bug already shipped),
~1,100 LOC of runtime-dead duplicate code, two ~2,800-LOC god-classes, and secret-based auth on both.

## Goal

**One** `IDataverseService` implementation family, MI-authenticated, decomposed below the god-class ceiling,
with the NFR-06 impersonation row-level-security paths preserved and re-verified.

## Approach (phased)

0. **ADR** — target architecture: a single SDK-based implementation, decomposed into per-concern services;
   how impersonation (`Clone()`+`CallerId`), POA (principalobjectaccess), events, and field-mapping map onto it.
   Records the migration + the security-test plan. (Path B per CLAUDE.md §6.5 if it touches an ADR MUST.)
1. Consume #3b MI outcome (task 011/NG1) — target impl is MI-only (`DefaultAzureCredential`); grant
   `prvActOnBehalfOfAnotherUser` to the MI app-user for impersonated writes.
2. **Port the 4 WebApi-only capability groups onto the SDK path** behind the existing narrow interfaces
   (events, field-mapping, `RetrieveMultipleImpersonatedAsync`, POA grants). Contract-test each against BOTH
   impls before switching, so parity is proven.
3. Repoint DI (`GraphModule.cs`) to the single impl; **delete `DataverseWebApiService`** + `DataverseWebApiClient`.
4. **Decompose** the resulting implementation into per-concern services ≤ 2,000 LOC; remove both
   `GodClassGuardTests` waivers.

## Risks & mitigations

| Risk | Mitigation |
|---|---|
| **NFR-06 impersonation regression** (row-level security) | Contract + seam tests on every impersonated read/write path BEFORE deleting the REST impl; parity-test SDK vs REST impersonation; `prvActOnBehalfOfAnotherUser` on the MI |
| Behavior drift between the two impls of a ported method | Characterization-test each capability against both impls, switch behind the interface, keep the REST impl until parity is green (ADR-032 seam) |
| Leaky abstraction — consumers downcast to the concrete SDK class (`UnwrapServiceClient`) | The single impl keeps `OrganizationService`; downcast sites are unaffected |
| Highest-contention shared lib | `/conflict-check`; quiet windows; small reviewable PRs; land AFTER the interim hardening |

## Dependencies

Interim `dataverse-access-hardening` (fences the traps) → this project (retires them). #3b MI (task 011/NG1).
Needs its own ADR. INITIALIZE-ONLY; worktree + tasks at execution start.
