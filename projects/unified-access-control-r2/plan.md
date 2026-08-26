# Unified Access Control R2 — Implementation Plan

> **Source**: [`spec.md`](spec.md) (32 FRs / 7 NFRs) · [`design.md`](design.md)
> **Status**: tasks generated 2026-08-21 · **Branch**: `work/unified-access-control-r2`
> **Task registry**: [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md)

---

## Overview

Unify Spaarke's two disjoint authorization systems into a single evaluator returning `(recordId → rights)`, close 22 confirmed enforcement findings, and make Secure Project actually isolate. Parent→child access inheritance — the requirement that seeded the project — falls out of the model.

## Architecture context

### Discovered resources

| ADR | Relevance | Tension |
|---|---|---|
| **ADR-003** authorization seams | `IAccessDataSource`, single `OperationAccessRule`, fail-closed | **Path B** — "two seams", "rules only", "no new auth service layers", "per-request cache only" none describe reality |
| **ADR-034** user↔record membership | Identity-table discovery, 1-hop cap, contact allow-list | **Path B** — allow-list becomes first-class + per-surface |
| **ADR-028 A1/A2** auth architecture | CIAM SPA + workforce Teams host, broker-only | **Path B** — narrow: token stays workforce, derivation policy changes |
| ADR-024 polymorphic resolver | The ancestor-stamp mechanism for child inheritance | none |
| ADR-008 endpoint filters · ADR-010 DI minimalism · ADR-038 testing | Filter-based authz; concrete registration; NFR-04 is a KEEP-path seam test | none |

### Canonical implementations to reuse (not fork)

| What | Where | Why |
|---|---|---|
| Impersonated read seam | `Spaarke.Dataverse/DataverseImpersonation.cs` + `DataverseWebApiService.RetrieveMultipleImpersonatedAsync:953-989` | **Built and live.** Refuses `Guid.Empty` — fail-closed by construction |
| The fail-closed gate to extend | `Infrastructure/ExternalAccess/AccessibleRecordSetService.cs` | Already principal-agnostic; ADR-028 A2 names it the decision point |
| POA client that already does teams + revoke | `Services/Ai/PlaybookSharingService.cs:302-350` | Consolidate with `IDataverseAccessGrantService` rather than writing a third |
| Child-derivation pattern | `ExternalModuleRegistry` `ScopeDimension` + `Api/ExternalAccess/Tier2ScopeFilterInjector.cs` | Shipping for documents + invoices |

### Constraints carried into every task

- **BFF placement** (CLAUDE.md §10): Placement Justification in the PR citing `.claude/constraints/bff-extensions.md`; publish ≤60 MB (baseline ~44.96 MB incl. PDBs)
- **Sub-agent write boundary** (CLAUDE.md §3): the three ADR-amendment tasks edit `.claude/**` → **main-session-only**, `parallel-safe:false`
- **Hot-file rule**: `Infrastructure/ExternalAccess/**`, `Api/ExternalAccess/**`, `Spaarke.Core/Auth/**`, `DataverseWebApiService.cs` → `parallel-safe:false`
- `/conflict-check` before **every** BFF PR — surface shared with shipped `SPA-external-access-platform-r1/r2` + `teams-app-r1`, and draft `SPA-r3`

---

## Phase breakdown

### Phase 0 — Enforcement remediation (tasks 001–029)
Close all 22 confirmed findings. **Characterization suite first** (NFR-07 — the current baseline is near-zero), then fixes ordered High → Low severity.

Deliverables: caller-scoped document download (FR-01) · `AuthorizationService` no longer app-scoped (FR-02) · every filter operation key resolves (FR-03) · Read-ceiling removed (FR-04) · caller-scoped capabilities (FR-05) · expiry enforced or UI removed (FR-06) · delegation rule on write endpoints (FR-07) · To Do PATCH scope check (FR-08) · idempotent grant + revoke-all (FR-09) · self-join guard (FR-10) · anonymous links tracked or disabled (FR-11) · no-hijack email check (FR-12) · auth-mode in cache key (FR-13) · deterministic paging (FR-14) · closure cascade (FR-15) · SPE revoke matcher (FR-16) · dead-code + `in`-clause bound + node-executor fix (FR-17)

**Gate**: every finding has a regression test. All FULL rigor — authorization-path `.cs`.

### Phase 1 — One evaluator (tasks 030–039)
`(recordId → rights)` replacing `HashSet<Guid>`; impersonated root sets for Type 1; the three vetoes.

Critical path: **030** (ADR-003 amendment, sanctions the shape) → **032** (evaluator spine) → **033** (consumer propagation + delete the blanket `Collaborate` stamp) → **036** (flag-gated impersonation swap) → **037** (Restricted + Secure vetoes) → **039** (deny veto + ordered-pipeline tests).

Off-path, parallel: **034** negative canary — *a blocking merge gate for 036*; **035** `ImpersonatedRootSetSource`; **038** deny-list store.

### Phase 2 — One definition of member (tasks 040–044)
**040** (ADR-034 amendment) → **041** access-conferring column registry, contact **and** organization → **042** standing-grant baseline levels → **043** org-expansion term → **044** seam suite pinning the Phase 1–2 contract.

### Phase 3 — Child inheritance (tasks 050–059)
`RegardingResolver` denormalizes the **ultimate core ancestor** (keeps everything 1 hop, avoids an ADR-034 hop amendment); core/child taxonomy; `sprk_event` / `sprk_communication` / `sprk_todo` as child modules — which requires generalizing the accessible-root-set model first, not merely adding descriptors.

### Phase 4 — Secure Project, Manage Access, wizard (tasks 060–079)
Service-account ownership + share-only; POA seam consolidation; PCF "+ User" picker and provenance rendering; wizard rework incl. removing the retired Power Pages copy.

🔴 **FR-07 delegation must ship before the "+ User" button** — otherwise it is a one-click privilege escalation on a confidential matter.
⚠️ **Live-dev acceptance is gated on UAT** (BU restructure — see spec § UAT & Environment Setup). Code ships and unit/integration-tests independently.

### Phase 5 — Attestation (tasks 080–089)
Append-only access event log + evaluator replay over Dataverse field audit. Derived access is **never** materialized into rows. The evaluator is versioned so historical answers stay reproducible.

### Wrap-up (090)
README → Complete · `notes/lessons-learned.md` · `/test-diet` per CLAUDE.md §7 · close register items H-8a/H-8b.

---

## Hard gates

| Gate | Rule |
|---|---|
| **NFR-04** | Impersonated low-privilege read returns a strict subset AND strictly fewer rows than app-only. **Equality = impersonation inert → build fails** |
| **NFR-05** | No security role reaches the `Secure Projects` BU. A role edit that re-opens secure projects fails the build |
| **NFR-07** | Characterization suite exists before Phase 1 changes behaviour |
| **FR-07 → FR-29** | Delegation enforced before the PCF "+ User" button ships |

## Risks

| Risk | Mitigation |
|---|---|
| Impersonation silently inert → org-wide disclosure | NFR-04 as a merge gate; the primitive already refuses `Guid.Empty` |
| Role-depth regression re-opens Secure Project | NFR-05 standing assertion, not a one-time audit |
| Near-zero behavioural baseline on the access path | Phase 0 characterization suite before Phase 1 |
| Contended external-access surface | `parallel-safe:false` on hot files; `/conflict-check` per PR; SPA-r3 notified |
| Dev is the sole live environment | Every prerequisite recorded as a re-provisioning obligation |

## Out of scope

AI-search trimming for contacts (A-21 → AI/indexing owner) · field-level visibility · break-glass · organization-hierarchy cascade · GDPR erasure · **the BU restructure itself** (UAT/environment)

## References

- [`spec.md`](spec.md) · [`design.md`](design.md) · [`notes/design-register.md`](notes/design-register.md) — the register (§A–I)
- [`notes/investigation/`](notes/investigation/) — 10 evidence passes, all claims cited `file:line`
- `.claude/constraints/bff-extensions.md` · `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md` · `docs/architecture/uac-access-control.md` (corrected 2026-08-20)
