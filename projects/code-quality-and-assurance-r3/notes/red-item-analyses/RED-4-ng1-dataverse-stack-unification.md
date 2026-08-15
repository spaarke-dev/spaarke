# RED-4 — NG1: Dataverse Two-Stack Unification (+ #3b credential migration)

> **Type**: remediation-project seed (architecture) · **Origin**: r3 review (2026-08-15) + existing NG1 (Idea #742, task 011)
> **Surface**: `Spaarke.Dataverse` shared lib + BFF · **Effort**: L · **Value**: High · **Risk**: **High** (identity-attribution + dual god-services)

## Summary

Two parallel Dataverse implementations both implement `IDataverseService`:
- `DataverseServiceClientImpl : IDataverseService, IDisposable` — **2,864 LOC** (the SDK `ServiceClient` stack).
- `DataverseWebApiService : IDataverseService` — **2,822 LOC** (the raw Web-API/HTTP stack).

This is archived-R3 item #10 (UPGRADED to a live bug in r3) + NG1 (Idea #742). r3 landed the tactical
fixes (13→1 downcast via `UnwrapServiceClient`; `DataverseServiceClientDowncastTests` guard) but the
**structural duplication remains** — ~5,700 LOC across two stacks serving one interface, plus the #3b
shared-lib `ClientSecret`→Managed-Identity credential migration entangled with it.

## Evidence

- `DataverseServiceClientImpl.cs` 2,864 LOC + `DataverseWebApiService.cs` 2,822 LOC — the two largest
  shared-lib files; both are `GodClassGuardTests` waivers.
- Both implement the same `IDataverseService`; consumers pick a stack by DI registration.
- The r3 config-deployment assessment (task 017) graded shared-server-libs D1 = B– largely on these two.
- #3b: the BFF's own Dataverse path is still secret-based (ADR-028 §24 mandates MI — the secret paths are
  violations); the migration is an identity-attribution change (who the calls run as), not a refactor.

## Why it matters

1. **Duplication**: every Dataverse capability is potentially implemented twice; drift between the two
   stacks is a latent correctness hazard (the r3 "always-failing casts" bug was a symptom).
2. **Two god-classes**: neither can shrink under the ratchet while both exist; unification is the natural
   decomposition vehicle.
3. **Credential/identity (#3b)**: secret-based Dataverse access contradicts ADR-028; MI migration changes
   the Dataverse Application User the calls attribute to — must be done deliberately, per-environment.

## Proposed approach (assess-then-decide — this is task 011's charter)

**Do NOT lift-and-shift.** Merging two 2,800-LOC classes naively yields one ~5,600-LOC class (worse).

1. **Decide the target stack.** Determine whether `ServiceClient` (SDK) or Web-API (HTTP) is the
   go-forward implementation (SDK gives typed ops + MI via `DefaultAzureCredential`; Web-API gives
   control + smaller footprint). Likely SDK for MI alignment.
2. **Carve `IDataverseService` into cohesive sub-interfaces** (query / mutate / metadata / privilege) so
   the unified implementation decomposes into per-concern services (≤ ceiling), not one mega-class.
3. **Migrate the losing stack's consumers** to the target behind the sub-interfaces; delete the loser.
4. **#3b credential migration** (separate, gated): flip the target stack to `DefaultAzureCredential` (MI),
   register the BFF MI as a Dataverse Application User in each env FIRST (dev-only now — demo/prod
   decommissioned), verify attribution, then remove the `ClientSecret` path. Keep `Dataverse-ClientSecret`
   until the migration is proven live (never-remove until then).

## Risks & mitigations

- **Risk (High)**: identity-attribution change breaks row-level security / ownership if the MI isn't a
  correctly-privileged Application User. **Mitigation**: env-by-env, dev first, verify `whoami`/ownership
  on a canary write before cutover; rehearse rollback (re-enable secret path).
- **Risk**: behavior drift between the two stacks means the "losing" stack had subtly different semantics
  some consumer relied on. **Mitigation**: contract-test each `IDataverseService` operation against both
  stacks before deleting the loser; migrate consumers behind a feature flag (ADR-032 null-object seam).
- **Risk**: large blast radius on the most-contended shared lib. **Mitigation**: this is a standalone
  architecture project (its own ADR), not a drip; `/conflict-check`; sequence when BFF worktree contention
  is low.

## Acceptance criteria

- One `IDataverseService` implementation family (decomposed to per-concern services, each ≤ ceiling);
  the losing stack deleted; both god-class waivers removed. #3b: target stack on MI in dev, secret path
  removed only after live attribution proof; `Dataverse-ClientSecret` retained until then.

## Dependencies / coordination

This IS the NG1 track — **task 011 assess-then-decide** (Idea #742). Needs its own ADR (identity model).
Coordinate with every BFF worktree (highest-contention shared lib). Demo/prod credential re-verify is
deferred until those environments are re-provisioned.
