# Unified Access Control — R2

> **Portfolio**: [Project #808](https://github.com/spaarke-dev/spaarke/issues/808) under [Epic #535](https://github.com/spaarke-dev/spaarke/issues/535) `[Epic]: ENTITY FUNCTIONALITY` · [Board #2](https://github.com/users/spaarke-dev/projects/2)
>
> **Status**: **In progress: wrap-up housekeeping done (#2685); remaining 191 (D-13) and 193 (D-14)** (2026-11-11) · **Start**: 2026-08-19
> **What stays open:** the owner tests (U-1..U-13) and decisions D-1..D-12 (decided: "Accept all") plus D-13/D-14 (decided 2026-11-11: "Build in uac-r2 now") in [`notes/wrap-up-uat-and-decisions-2026-11-11.md`](notes/wrap-up-uat-and-decisions-2026-11-11.md), including the D-2 deploy steps for the 198 chain (merged, not deployed; inert until fm 054b verifies) and the D-3 / D-5 small fixes; the hand-offs the owner confirms in one batch in [`notes/defer-disposition-2026-11-11.md`](notes/defer-disposition-2026-11-11.md); tasks **191** (D-13) and **193** (D-14), to be built in uac-r2; task **090**, which closes after 191 and 193 with a final test-diet over their tests; 194 closed under round 90 after its post-194b gate (2026-11-11); 186 closed under round 90 with its NOT-RUN checks 3 and 6 carried as U-12, and 178 deferred to uac-secure-project-r3; and the known gap FR-32 (point-in-time replay, #1303). Test diet: [`notes/test-diet-report.md`](notes/test-diet-report.md). Lessons: [`notes/lessons-learned.md`](notes/lessons-learned.md).
> **Worktree**: `c:/code_files/spaarke-wt-unified-access-control-r2` · **Branch**: `work/unified-access-control-r2`
> **Origin**: smart-todo-r5 UAT follow-up — "child access cascade is part of our unified access control system — think it all the way through"

---

## What this project is

Spaarke has **two disjoint authorization systems** sharing a data resolver and nothing else — `Spaarke.Core/Auth` (the subsystem `docs/architecture/uac-access-control.md` calls "UAC") and `Infrastructure/ExternalAccess/**` (built by the external-access SPA and Teams projects). A CIAM contact can never transit the first; internal BFF endpoints never transit the second. Neither enforces what its documentation claims.

This project **unifies them into one evaluator** returning `(recordId → rights)` with explicit, reviewable policy, and closes the enforcement gaps found on the way. The parent→child access cascade that seeded the project falls out of the model rather than being built as a bespoke mechanism.

### The two load-bearing facts

1. **Being referenced by a lookup grants ZERO access in Dataverse.** Access comes only from ownership, security-role privilege, team membership, share (POA), or the user hierarchy. A cascade must *actively grant* — which is why the general case needs code.
2. **Dataverse has no per-record deny** (verified against current Microsoft documentation, 2026-08-20). Isolation is achieved by scoping the baseline and granting additively — never by restricting a row.

## The model

| Surface | Enforced by |
|---|---|
| **MDA** | Dataverse natively (role depth × owner/BU/team + sharing). No code |
| **SPA / Teams** | **The BFF filter, and nothing else** — reads are app-only, so Dataverse row security is inert |

| User type | Door | Record permission from |
|---|---|---|
| **1** systemuser, licensed | workforce Entra | Dataverse's real answer (impersonated read) ∪ contact grants |
| **2** customer employee, no licence | workforce Entra | `sprk_assigned*` ∪ org |
| **3** external contact | CIAM | `sprk_assigned*` ∪ org |

Types 2 and 3 are identical on record permission — they differ only by credential. **Core** records (project, matter, work assignment, service request) need direct grants; **child** records (invoice, communication, document, event, to-do, analysis) inherit one hop via a denormalized core ancestor.

## Documents

| Doc | Purpose |
|---|---|
| [`design.md`](design.md) | **The design.** Model, Secure Project, phases, ADR tensions |
| [`spec.md`](spec.md) | **The spec.** 32 FRs / 7 NFRs / 6 phases, owner clarifications |
| [`notes/design-register.md`](notes/design-register.md) | Consolidated item register (§A–I) — every finding, decision, deferral and prerequisite |
| [`notes/investigation/`](notes/investigation/) | 10 evidence passes, all claims cited `file:line` |
| [`unified-access-control-cascade.md`](unified-access-control-cascade.md) | Original investigation (2026-08-18) — **superseded by `design.md`** |
| [`spa-external-access-model-briefing.md`](spa-external-access-model-briefing.md) | SPA-project briefing (2026-08-20). Partly corrected by pass 01 |
| [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) | Task registry, dependencies, parallel groups |

## Graduation criteria

- [ ] All 22 Phase 0 findings closed, each with a regression test
- [ ] One evaluator; no caller-scoped path passes `userAccessToken: null`
- [ ] **Negative canary green** — an impersonated low-privilege read returns a strict subset AND strictly fewer rows than app-only. Equality means impersonation is inert (NFR-04)
- [ ] **Role-depth assertion green** — no role reaches the `Secure Projects` BU (NFR-05)
- [ ] A user in the Operations subtree cannot read a `Secure Projects`-owned record *(live dev — gated on UAT)*
- [ ] A shared user reads a secure project in **both** MDA and SPA *(live dev — gated on UAT)*
- [ ] Manage Access answers "who can see this and why" with provenance per row
- [ ] A contact with Project access sees its invoices, events, communications and To Dos
- [ ] ~~Point-in-time attestation answerable~~ — **deferred (known gap, FR-32 → [#1303](https://github.com/spaarke-dev/spaarke/issues/1303))**. Interim: manual Dataverse audit review, which needs org auditing on ([#2027](https://github.com/spaarke-dev/spaarke/issues/2027) for customer environments)
- [ ] BFF publish size ≤60 MB (baseline ~44.96 MB incl. PDBs)

## Out of scope

AI-search trimming for contacts (finding A-21, files to the AI/indexing owner) · field-level visibility · break-glass · organization-hierarchy cascade · GDPR erasure of grant rows · **the BU restructure itself** (environment work — see spec § UAT & Environment Setup)

## Related prior work

- [`projects/unified-access-control-r1/`](../unified-access-control-r1/) — **historical.** Its intents largely shipped under other names (`sprk_externalaccesscontrol` → `sprk_externalrecordaccess`); its SPE permission gap is **still open** and is Phase 0 FR-01
- [`projects/dataverse-access-unification-r1/`](../dataverse-access-unification-r1/) — PAUSED 2026-08-19; its validation note found five Dataverse access stacks and flagged fail-OPEN row-level-security risk on a near-zero test baseline
- `spaarke-SPA-external-access-platform-r1/r2` (shipped) · `teams-app-r1` (shipped) · `SPA-r3` (draft — assumes the dual-plane model, must be notified)
