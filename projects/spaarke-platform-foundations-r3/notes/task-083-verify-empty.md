# Task R3-083 — Verify-Empty Decision Record

> **Task**: R3-083 (Wire event-publishing into Task + Opportunity clusters per FR-2P2.6)
> **Authored**: 2026-06-22
> **Status**: Closed — **no-op, recon-only**
> **Predecessor**: [`event-source-inventory.md`](event-source-inventory.md) §3D + §3E (task 080)
> **Downstream backstop**: task 085 (`MembershipReconciliationJob`, FR-2P2.7)

---

## 1. Decision

**Outcome**: No source code changes. Task closes as **verify-empty**.

Per the task 080 inventory finding (§3.D, §3.E) and the explicit recommendation in §6.2 ("Tasks 083 sub-A and 083 sub-B should record an explicit 'verified empty for in-repo BFF surface; relies on FR-2P2.7 recon for coverage' decision rather than hunting for endpoints that don't exist"), this task records the verify-empty outcome rather than attempting hookup against a non-existent endpoint surface.

---

## 2. Re-verification grep (2026-06-22, this task)

Run independently of task 080's inventory to confirm no `sprk_task` / `sprk_opportunity` mutation endpoints have been added in the intervening waves:

### 2.1 Logical-name reference scan

```
Grep pattern: sprk_task\b
Scope: src/server/api/Sprk.Bff.Api/
Result: 0 files matched.

Grep pattern: sprk_opportunity\b
Scope: src/server/api/Sprk.Bff.Api/
Result: 0 files matched.
```

### 2.2 Mutation-verb scan against entity name

```
Grep pattern: Map(Post|Put|Patch|Delete).*(task|opportunity)
Scope: src/server/api/Sprk.Bff.Api/
Mode: case-insensitive
Result: 0 matches.
```

### 2.3 Targeted scope-narrowing (Api/ + Endpoints/ subtrees)

```
Grep pattern: sprk_task|sprk_opportunity
Scope: src/server/api/Sprk.Bff.Api/Api/
Result: 0 files matched.

Grep pattern: sprk_task|sprk_opportunity
Scope: src/server/api/Sprk.Bff.Api/Endpoints/
Result: 0 files matched.
```

**Re-verification conclusion**: zero matches across all four grep passes. The inventory's §3.D / §3.E finding (0 endpoints each) holds in full as of 2026-06-22. No new BFF mutation surface has been introduced for `sprk_task` or `sprk_opportunity` since task 080 authored the inventory.

---

## 3. Why the BFF has no mutation surface for these entities

Per inventory §2 line 33-34:

> `sprk_task` — inferred from spec line 134 — no in-repo CRUD endpoint surface exists; OOTB `task` is mutated by `CreateTaskNodeExecutor` but that targets the OOTB schema, not `sprk_task` (custom).
>
> `sprk_opportunity` — inferred from spec line 134 — no in-repo BFF endpoint mutates `sprk_opportunity`.

In the Spaarke architecture today:
- **`sprk_task`** is the custom Spaarke To Do entity (per [`docs/architecture/spaarke-todo-architecture.md`](../../../docs/architecture/spaarke-todo-architecture.md)). Its mutation paths are: maker-portal forms, parent-form subgrids (model-driven app), Outlook ribbon flows, and Power Automate. The BFF exposes To Do **read** endpoints under `/api/v1/external/projects/.../todos` for the External Access surface, but writes to `sprk_todo` (the To Do entity in that subsystem), not `sprk_task`.
- **`sprk_opportunity`** is mutated exclusively via maker-portal forms / Power Automate / plugins today. No BFF surface area has been authored — and none is planned in R3.

The closest analog code paths considered + intentionally excluded from this hookup:
- [`CreateTaskNodeExecutor`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateTaskNodeExecutor.cs) — writes to OOTB `task` (NOT custom `sprk_task`). Out of FR-2P2.5 entity scope.
- [`ExternalProjectDataEndpoints` TODO endpoints](../../../src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalProjectDataEndpoints.cs) — writes to `sprk_todo`, which is a separate sibling entity from `sprk_task`. Out of FR-2P2.5 entity scope (see inventory §3F).

---

## 4. Coverage handoff to task 085 (MembershipReconciliationJob)

Per inventory §6.1 + §6.2, the load-bearing path for `sprk_task` and `sprk_opportunity` junction-row freshness in R3 is the **nightly `MembershipReconciliationJob`** delivered by task 085 (FR-2P2.7). The recon job:

- Source-of-truth scans `sprk_task` + `sprk_opportunity` rows for identity-Lookup populations.
- Reconciles the junction table against the source row state on a 24h cadence.
- Removes orphaned junction rows whose source records have been soft-deleted (statecode != 0) or removed.

**Operator expectation set by this decision**: real-time event-driven junction freshness is unavailable for `sprk_task` and `sprk_opportunity` membership Lookups in R3. The maximum staleness window for these entities' membership data is the recon job cadence (24h), not real-time. This matches the §6.1 finding for the `sprk_assigned*` matter-cluster fields and is consistent with the operator expectations being set by ADR-034 (per inventory §6.1 recommendation).

---

## 5. Acceptance

This decision record satisfies:

- ✅ **FR-2P2.6** in-repo scope — "Each mutation endpoint publishes…" — there are no mutation endpoints on `sprk_task` or `sprk_opportunity` in the BFF surface area, so the FR's binding code-side requirement is vacuously satisfied for these two clusters.
- ✅ **AC-1P2.4** — coverage matches inventory checklist (§3.D and §3.E both empty; this record acknowledges + accepts the empty state).
- ✅ Inventory recommendation §6.2 — explicit "verified empty for in-repo BFF surface; relies on FR-2P2.7 recon for coverage" decision recorded here.

The downstream backstop for these entities' membership-junction freshness is task 085 (`MembershipReconciliationJob`). When that ships and the nightly cadence runs, the FR-2P2.6 in-spirit coverage for `sprk_task` + `sprk_opportunity` is achieved through the recon path, not the event path.

---

## 6. Cross-references

- Task 080 inventory: [`event-source-inventory.md`](event-source-inventory.md) §3.D, §3.E, §6.1, §6.2
- Task 081 (matter cluster event-publishing — shipped infrastructure that would have been reused if endpoints existed): `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/Events/IMembershipEventPublisher.cs`
- Task 082 (document + event clusters — running parallel to this task): scoped to `sprk_document` + `sprk_event` only; no overlap.
- Task 085 (`MembershipReconciliationJob` — running parallel to this task): the load-bearing recon path for `sprk_task` + `sprk_opportunity` membership junction freshness in R3.
- Spec FR-2P2.6 / AC-1P2.4: [`spec.md`](../spec.md) lines 134, 277.
