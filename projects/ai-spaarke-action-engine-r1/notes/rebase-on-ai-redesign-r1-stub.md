# Action Engine R1 — Re-base Stub: New Baseline is `spaarke-ai-architecture-redesign-r1`

> **Filed**: 2026-07-05 by `spaarke-ai-architecture-redesign-r1` task 013 (FR-P0-11 portfolio reconciliation)
> **Status of this note**: spec-rebase STUB — the full re-spec happens at resumption; this note fixes the baseline and the resumption trigger so the project does not resume against a dead foundation.

## 1. What changed

Action Engine R1 was held at **Phase 0 (architecture spike, task 001)** pending "R7 ships" (Q14 decision, 2026-06-28, recorded in `spaarke-ai-platform-unification-r7` project files). R7 (Issue [#501](https://github.com/spaarke-dev/spaarke/issues/501)) is now **CLOSED / RE-SCOPED**: its remaining scope is absorbed by [`spaarke-ai-architecture-redesign-r1`](../../spaarke-ai-architecture-redesign-r1/) (Issue [#550](https://github.com/spaarke-dev/spaarke/issues/550)) — see the per-wave map at [`../../spaarke-ai-platform-unification-r7/notes/close-out-absorbed-by-ai-architecture-redesign-r1.md`](../../spaarke-ai-platform-unification-r7/notes/close-out-absorbed-by-ai-architecture-redesign-r1.md).

**New resumption trigger**: Action Engine R1 resumes after `spaarke-ai-architecture-redesign-r1` passes **gate G-P3** (end of Phase P3 — Consumer + client consolidation) AND owner directs resumption. Earliest useful design-review point is after **gate G-P2** (Phase P2 ships the agent loop + confirmation gate).

## 2. What Action Engine R1 re-bases ON (consume, don't rebuild)

The redesign delivers, as platform primitives, several things the original Action Engine spec planned to build itself:

| Original Action Engine concept | Redesign-r1 deliverable it re-bases on | Where |
|---|---|---|
| `IGateResolver` + gate primitive + `GateApprovalCard` side-effect safety | **THE Confirmation Gate** — `PendingPlanManager` generalized to the ONE pending store; gating driven by declared `side_effect_class` + Binding `risk` (no tool-name lists) | Task **031** / FR-P2-02 (Phase P2) |
| Unified Tool Registry (deterministic + probabilistic Tools as peers) | **Closed tool catalog** (`sprk_analysistool` + typed handlers, tool↔handler bijection enforced at boot) + closed Action catalog | FR-P0-03/FR-P0-04 (P0); ADR-039 |
| Three invocation paths (conversational / explicit UI / system-triggered) | **Event / Click / Text** — the ONLY three AI invocation routes; routing config lives solely in the Binding table (`sprk_playbookconsumer`) | Spec-wide MUST; FR-P1-03/FR-P1-04 |
| Three meta-tools (`FindResources` / `GetResourceDetail` / `InvokeResource`) | Agent-turn loop with capability-tools projection from the catalog (per-turn budget, session-context pre-filter, citation enforcement) | FR-P2-01 (P2) |
| Starter Action Templates (Summarize Matter, Weekly Task Digest, Find Similar Matters) | **Binding-modeled capabilities** — worked examples shipped by redesign: `draft-correspondence` (task **041** / FR-P3-02, Communication-service DRAFT-only, gated) and `create-task` (task **042** / FR-P3-03, `sprk_event(type=task)` + ledger refs); all remaining consumers become Binding rows (task **040** / FR-P3-01) | Phase P3 |
| Audit/observability of Action runs | **Session ledger** (ADR-040) — every output + tool chain persisted before rendering; `ExecutionTraceWidget` renders `ToolChain` entries | FR-P0-01/FR-P1-02/FR-P3-07 |

## 3. Consequences for the R1 spec at resumption

- The Phase 0 spike question set (scheduler choice, Hybrid D topology, publish-size cap) must be re-validated against the post-redesign BFF — do NOT reuse the 2026-05-29 spike framing unexamined.
- Any R1 scope item that duplicates a row in the table above is **cut** at re-spec time; R1's remaining distinct value is the management plane (authoring/discovery/scheduling UX, system-trigger sources like cron/webhooks) layered ON the redesign's catalog + gate + ledger.
- New capabilities are authored as **Action rows + Binding rows** (prompted) or **`coded` workflows** (composite) — never as new dispatch mechanisms (redesign MUST NOT: "add a second intent-detection mechanism"; "gate by tool-name lists"; "land new capability on the frozen engine").
- Re-spec entry point: run `/design-to-spec` against this stub + the redesign's shipped ADR-039/ADR-040 + `docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md` v0.4.

## 4. Pointers

- Redesign spec: [`projects/spaarke-ai-architecture-redesign-r1/spec.md`](../../spaarke-ai-architecture-redesign-r1/spec.md) — see FR-P2-01/02/03 (loop + gate + elicitation), FR-P3-01..03 (Binding capabilities), FR-P0-03/04 (catalogs)
- Redesign task index (tasks 031, 040, 041, 042): `projects/spaarke-ai-architecture-redesign-r1/tasks/TASK-INDEX.md`
- R7 close-out map: [`projects/spaarke-ai-platform-unification-r7/notes/close-out-absorbed-by-ai-architecture-redesign-r1.md`](../../spaarke-ai-platform-unification-r7/notes/close-out-absorbed-by-ai-architecture-redesign-r1.md)
- Portfolio: Action Engine Issue [#435](https://github.com/spaarke-dev/spaarke/issues/435) · redesign Issue [#550](https://github.com/spaarke-dev/spaarke/issues/550) · Epic [#421](https://github.com/spaarke-dev/spaarke/issues/421)
