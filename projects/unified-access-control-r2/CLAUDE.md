# CLAUDE.md — `unified-access-control-r2`

> **Project context for Claude Code.** Loads with every task in this project.
> **Read `spec.md` + the relevant `notes/investigation/` pass before implementing.** Root [`CLAUDE.md`](../../CLAUDE.md) still applies.

---

## 🚨 Task execution protocol (MANDATORY)

**Every task in this project MUST be executed via the `task-execute` skill.** Do NOT read a `.poml` and implement manually — that skips knowledge loading, ADR constraints, checkpointing, and the Step 9.5 quality gates.

All authorization-path tasks are **`<rigor>FULL</rigor>`** → `code-review` + `adr-check` run at Step 9.5, unconditionally.

## What this project is

Spaarke has **two disjoint authorization systems** sharing a data resolver and nothing else. This project unifies them into ONE evaluator returning **`(recordId → rights)`**. The parent→child access cascade that seeded the project falls out of the model.

## The five facts that govern every decision here

1. **On the SPA/Teams surface, the BFF filter is the ENTIRE security boundary.** Reads are app-only, so Dataverse row-level security is inert. A bug here is a disclosure — a client seeing another client's matter — not a nuisance. On the MDA, Dataverse enforces natively and we write no code.
2. **Being referenced by a lookup grants ZERO access in Dataverse.** Access comes only from ownership, role privilege, team membership, share (POA), or the user hierarchy.
3. **Dataverse has NO per-record deny** (verified against current Microsoft docs, 2026-08-20). Isolation = scope the baseline, grant additively. Never "restrict a row".
4. **A `contact` is not a security principal.** It cannot be a POA share target and cannot be impersonated. That is *why* the contact plane must compute access rather than store it.
5. **`"No Access"` is a VETO, never a level.** Under highest-wins, `max()` would ignore it and an ethical wall would fail silently in exactly the case it exists for.

## The model

| Surface | Enforced by |
|---|---|
| MDA | Dataverse natively (role depth × owner/BU/team + sharing) — **no code** |
| SPA / Teams | The BFF evaluator — **the only boundary** |

| Type | Door | Record permission from |
|---|---|---|
| 1 systemuser, licensed | workforce Entra | Dataverse's real answer (impersonated read) ∪ contact grants |
| 2 customer employee, no licence | workforce Entra | `sprk_assigned*` ∪ org — **no business unit** |
| 3 external contact | CIAM | `sprk_assigned*` ∪ org — **no business unit** |

**Evaluator term order** — additive terms union with **highest wins**, then vetoes in this order:

```
max( dataverse-answer, explicit-grant, derived-member, org-expansion, inherited )
  → deny list  (ethical wall + per-child revocation)   → None
  → Restricted (sprk_accesspermission)                  → None for ALL contacts
  → Secure     (sprk_issecure) suppresses derived + org BEFORE the max, for EVERY principal kind
```

**Records**: *core* (project, matter, work assignment, service request) need direct grants. *Child* (invoice, communication, document, event, to-do, analysis) inherit **1 hop** via a denormalized core ancestor. **Matter does NOT inherit from Project** — both are core.

## Reuse, do not fork

| Need | Use this — it exists |
|---|---|
| Impersonated read | `Spaarke.Dataverse/DataverseImpersonation.cs` + `DataverseWebApiService.RetrieveMultipleImpersonatedAsync:953-989` — **live**; refuses `Guid.Empty` (fail-closed by construction) |
| The gate to extend | `Infrastructure/ExternalAccess/AccessibleRecordSetService.cs` |
| POA with teams + revoke | `Services/Ai/PlaybookSharingService.cs:302-350` — **consolidate** with `IDataverseAccessGrantService`, don't write a third client |
| Child scoping | `ExternalModuleRegistry` `ScopeDimension` + `Api/ExternalAccess/Tier2ScopeFilterInjector.cs` |

## Hard gates — do not merge without these

| Gate | Rule |
|---|---|
| **NFR-04** negative canary | Impersonated low-privilege read MUST return a strict subset AND **strictly fewer** rows than app-only. **Equality means impersonation is inert → fail the build.** Task 034 is a blocking merge gate for 036 |
| **NFR-05** role-depth assertion | No security role may reach the `Secure Projects` BU. A role edit that re-opens secure projects fails the build |
| **NFR-07** | Characterization suite exists BEFORE Phase 1 changes behaviour — the current baseline is near-zero |
| **FR-07 → FR-29** | Delegation ("you may grant if you have Write on the record") ships BEFORE the PCF "+ User" button. Otherwise that button is a one-click privilege escalation on a confidential matter |

## Parallel-safety rules

- **`parallel-safe:false`** for `Infrastructure/ExternalAccess/**`, `Api/ExternalAccess/**`, `Spaarke.Core/Auth/**`, `Spaarke.Dataverse/DataverseWebApiService.cs`. Two agents editing an authorization path concurrently produces a silent merge mess.
- The **three ADR-amendment tasks** (030 ADR-003, 031 ADR-028 A2, 040 ADR-034) edit `.claude/**` → **main-session-only**. Sub-agents CANNOT write there (root CLAUDE.md §3); "Edit denied" is the boundary working, not a bug.
- `AccessGrantModal.tsx` is shared by 065/066/067 — those serialize.

## Every BFF-touching task

State the **Placement Justification** in the PR citing [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md), and verify publish size **≤60 MB** (baseline ~44.96 MB incl. PDBs). Run `/conflict-check` before **every** BFF PR — this surface is shared with shipped `SPA-external-access-platform-r1/r2` + `teams-app-r1`, and draft `SPA-r3`.

## ADR tensions — all CLAUDE.md §6.5 **path B**

| ADR | Why | Task |
|---|---|---|
| ADR-003 | "Two seams", "rules only", "no new auth service layers", "per-request cache only" — none describe reality; the rules would force a shape that cannot carry rights or vetoes | 030 |
| ADR-028 A2 | Mandates workforce → ADR-034 membership derivation; we substitute Dataverse's real answer. Token stays workforce — only derivation changes | 031 |
| ADR-034 | The access-conferring allow-list becomes first-class and per-surface, covering org-typed lookups too | 040 |

The 1-hop cap needs **no** exception — the ancestor stamp makes every chain one hop.

## Out of scope

AI-search trimming for contacts (finding A-21 → AI/indexing owner) · field-level visibility · break-glass · organization-hierarchy cascade · GDPR erasure of grant rows · **the BU restructure itself** (UAT/environment work — spec § UAT & Environment Setup)

## Key documents

| Doc | Use |
|---|---|
| [`spec.md`](spec.md) | 32 FRs / 7 NFRs — the contract |
| [`design.md`](design.md) | The model and its reasoning |
| [`notes/design-register.md`](notes/design-register.md) | Every finding, decision, deferral, prerequisite (§A–I) |
| [`notes/investigation/10-finding-confirmations.md`](notes/investigation/10-finding-confirmations.md) | Per-finding evidence + failure scenarios — **read before any Phase 0 task** |
| [`notes/investigation/08-option-b-feasibility.md`](notes/investigation/08-option-b-feasibility.md) | The impersonation mechanism + fail-OPEN risk |
| [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) | Dependencies + parallel groups |
