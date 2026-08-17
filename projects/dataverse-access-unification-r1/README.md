# Dataverse Access-Layer Unification — R1

> **Status**: INITIALIZED (folder + design only; execution operator-gated) · **Epic**: Code Quality (#427)
> **Origin**: code-quality-and-assurance-r3 RED-4 Fable-verified assessment
> (`../code-quality-and-assurance-r3/notes/red-item-analyses/RED-4-dataverse-two-stack-ASSESSMENT.md`)
> **Type**: architecture / refactor (ADR-backed) · **Surface**: `Spaarke.Dataverse` shared lib + BFF · **Risk**: HIGH

## One-liner

Converge the two `IDataverseService` implementations (`DataverseServiceClientImpl` SDK + `DataverseWebApiService`
REST, ~5,686 LOC combined) to a **single implementation** — porting events / field-mapping / impersonation / POA
onto the SDK path (which already demonstrates both raw-OData and `CallerId` impersonation) and deleting the REST
class — then decompose the resulting god-class. Permanently retires the split-brain routing trap.

## Why this is a project (not the interim hardening)

The interim hardening (`dataverse-access-hardening`, done separately) *fences* the traps — removes dead code,
converts silent-empty stubs to throws, fixes the `UpdateRecordFieldsAsync` split-brain, documents the routing
table. **This project RETIRES them**: one implementation, no routing table to mis-roll, MI-only auth.

**Fable's counter-argument for doing this (not just fencing):** the most security-critical Dataverse surface —
impersonated row-level access (NFR-06) — currently lives in a majority-dead class reached by concrete-class
injection that bypasses the interface layer; piecemeal cleanup historically never graduates to the real fix. If
Dataverse keeps growing (9 narrow interfaces and counting), paying once for the single-impl target + decomposition
+ MI amortizes the security-test burden and permanently retires the trap.

## Scope (phased — see design.md)

0. **ADR** on the target Dataverse-access architecture (single impl; how impersonation/POA/events map onto SDK).
1. Consume the **#3b MI migration** outcome (task 011/NG1) — target impl is MI-only.
2. Port the 4 WebApi-only capability groups (events, field-mapping, impersonated reads, POA) onto the SDK path.
3. Delete `DataverseWebApiService`; collapse the DI routing to a single binding.
4. **Decompose** the resulting god-class below the 2,000-line ratchet ceiling (removes the waivers).

## Prerequisites / sequencing

- **After** the interim hardening branch lands (it removes the dead code + split-brain this project would
  otherwise have to reconcile).
- **After / alongside** #3b MI migration (task 011/NG1).
- Highest-contention shared lib — `/conflict-check` before every PR; land in quiet windows.

## Graduation criteria

- [ ] ADR merged; one `IDataverseService` implementation family; `DataverseWebApiService` deleted.
- [ ] NFR-06 impersonation row-level-security paths re-verified (contract + seam tests green).
- [ ] Resulting files ≤ 2,000 LOC (both Dataverse god-class waivers removed from `GodClassGuardTests`).
- [ ] MI-only auth (no `ClientSecret` on the Dataverse path); startup + smoke green per env.
