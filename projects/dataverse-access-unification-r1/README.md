# Dataverse Access-Layer Unification — R1

> ## 🟡 PAUSED (open, not archived) — 2026-08-19
>
> A code-grounded validation ([`notes/validation-2026-08-19.md`](notes/validation-2026-08-19.md)) found the
> justification substantially weaker than this README claimed: of three surviving reasons, **one is intact, one
> is defused, one is false**; there are **five** Dataverse access stacks, not two, and this project retires the
> one the interim hardening already fenced; and `DataverseWebApiClient` (45 refs / 16 files) cannot be deleted
> here as originally scoped.
>
> **Assessment: not necessary; as scoped, more risk than reward** — the failure mode is fail-OPEN row-level
> security on a near-zero test baseline, verifiable only on dev, in the repo's most contended shared lib.
> RED-4 itself called this work OPTIONAL; its option B (hardening) shipped and is holding.
>
> **Three residual items were extracted to the hardening track** and are not gated on this project. Re-evaluate
> after `spaarke-auth-v4-dataverse-MI` completes, or when a resume trigger fires — both listed in
> [`design.md` § "Pause & resume"](design.md).

> **Status**: PAUSED 2026-08-19 (was INITIALIZED; folder + design only) · **Epic**: Code Quality (#427)
> **Origin**: code-quality-and-assurance-r3 RED-4 Fable-verified assessment
> (`../code-quality-and-assurance-r3/notes/red-item-analyses/RED-4-dataverse-two-stack-ASSESSMENT.md`)
> **Type**: architecture / refactor (ADR-backed) · **Surface**: `Spaarke.Dataverse` shared lib + BFF · **Risk**: HIGH

## One-liner

Converge the two `IDataverseService` implementations (`DataverseServiceClientImpl` SDK **2,975** +
`DataverseWebApiService` REST **1,468** = 4,443 LOC, re-measured 2026-08-19) to a **single implementation** —
porting events / field-mapping / impersonation / POA onto the SDK connection (which already demonstrates both
raw-OData and `CallerId` impersonation) and deleting the REST class — then decompose the resulting god-class.
Permanently retires the split-brain routing trap.

> **Read [`design.md`](design.md) before acting on this README.** A 2026-08-19 validation pass re-measured the
> baseline after the interim hardening + #3b landed and revised the scope: `DataverseWebApiClient` is **not**
> deleted here (45 refs across 16 SpeAdmin/ExternalAccess files), capabilities port into **new per-concern
> services** rather than into the god class, and the remaining justification is defect-prevention — not dead
> code and not a security win.

## Why this is a project (not the interim hardening)

The interim hardening (`dataverse-access-hardening`, done separately) *fences* the traps — removes dead code,
converts silent-empty stubs to throws, fixes the `UpdateRecordFieldsAsync` split-brain, documents the routing
table. **This project RETIRES them**: one implementation, no routing table to mis-roll, MI-only auth.

**Counter-argument for doing this (not just fencing) — restated 2026-08-19:** the original RED-4 framing ("lives
in a **majority-dead** class") is **stale** — the hardening deleted that dead code. What survives: the most
security-critical Dataverse surface, impersonated row-level access (NFR-06), is still reached by **concrete-class
injection that bypasses the interface layer**; `UpdateRecordFieldsAsync` still has two live impls selected by
which alias a consumer injects; and every new narrow interface must be hand-routed in DI, where a mis-route fails
at runtime rather than at compile time. If Dataverse keeps growing (9 narrow interfaces and counting), paying
once for the single-impl target amortizes the security-test burden and permanently retires the trap. See
[`design.md` § "Justification after the hardening landed"](design.md) for the full §11 test.

## Scope (phased — see design.md)

0. **ADR** on the target Dataverse-access architecture (single impl; how impersonation/POA/events map onto SDK).
1. Consume the **#3b MI migration** outcome (task 011/NG1) — target impl is MI-only.
2. Port the 4 WebApi-only capability groups (events, field-mapping, impersonated reads, POA) onto the SDK path.
3. Delete `DataverseWebApiService`; collapse the DI routing (`GraphModule.cs` **and** `CommunicationModule.cs`,
   whose two seam adapters bind the concrete type) to a single binding.
4. **Decompose** the residual `DataverseServiceClientImpl` on
   [COMPONENT-COMPLEXITY](../../docs/standards/COMPONENT-COMPLEXITY.md) grounds — responsibilities and cohesion,
   not line count (the LOC ratchet was retired 2026-08-20). Separable into a follow-on project — Phases 0–3
   already retire the trap.

## Prerequisites / sequencing

- ✅ Interim hardening — **merged to `master`** (dead code deleted, routing map published, DEF-2 fixed).
- ✅ #3b MI migration (task 011/NG1) — **done, live on dev** (2026-08-17).
- `spaarke-auth-v4-dataverse-MI` is **not** a dependency in either direction; coordinate PR timing only.
- Highest-contention shared lib — `/conflict-check` before every PR; land in quiet windows.

## Graduation criteria

- [ ] ADR merged; one `IDataverseService` implementation family; `DataverseWebApiService` deleted.
- [ ] NFR-06 impersonation row-level-security paths re-verified — named suites green, including the
      **negative canary** (impersonated low-privilege read returns strictly fewer rows than the app-only read).
- [ ] Resulting components pass the [COMPONENT-COMPLEXITY](../../docs/standards/COMPONENT-COMPLEXITY.md) review
      — single responsibility, cohesive, reasonable ctor deps. **Not** a LOC threshold (ratchet retired
      2026-08-20). *(Phase 4 — drops if Phase 4 is split off.)*
- [ ] No `ClientSecretCredential` constructed anywhere inside the `IDataverseService` family; the single impl
      resolves the DI-registered `TokenCredential` (ADR-028 A4 shared-provider rule). `DataverseAccessDataSource`
      explicitly excluded (auth-v4's).
- [ ] Startup + smoke green **on dev** (sole live environment); demo/prod re-verify — including the MI app-user
      `prvActOnBehalfOfAnotherUser` grant — recorded as a deferred obligation for re-provisioning.
