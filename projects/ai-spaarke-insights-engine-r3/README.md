# Spaarke Insights Engine — Phase 2 (r3)

> **Status**: ⏸ **PAUSED** as of 2026-06-10 — pending R6 ship + owner direction to resume.
> **Pause context + resumption plan**: [`notes/pre-design-context-2026-06-10.md`](notes/pre-design-context-2026-06-10.md) — READ THIS FIRST if picking up r3 at any point in the future.
> **Predecessor**: [`ai-spaarke-insights-engine-r2`](../ai-spaarke-insights-engine-r2/) (Phase 1.5 — ✅ Complete; 14/15 SCs met; PRs #330, #334, #336, #337, #339)
> **Predecessor wrap-up**: [`r2/notes/lessons-learned.md`](../ai-spaarke-insights-engine-r2/notes/lessons-learned.md), [`r2/PHASE-2-OUTLINE.md`](../ai-spaarke-insights-engine-r2/PHASE-2-OUTLINE.md)
> **Predecessor's predecessor**: [`ai-spaarke-insights-engine-r1`](../ai-spaarke-insights-engine-r1/) (Phase 1 plumbing — ✅ shipped + deployed)
> **Parallel projects**: [`spaarke-ai-platform-unification-r6`](../spaarke-ai-platform-unification-r6/) (in design — r3 paused for R6), [`ai-spaarke-insights-engine-widgets-r1`](../ai-spaarke-insights-engine-widgets-r1/) (NEW 2026-06-10 — capacity pivoted here while r3 paused)

---

## 1. Purpose

r3 is Phase 2 of the Spaarke Insights Engine. Phase 1.5 (r2) established the hybrid playbook + RAG consumption model, 2D taxonomy, multi-entity subjects, and the Spaarke Assistant tool-call contract (v1.1). Phase 2 builds on that substrate.

**Scope is owner-driven** — to be defined in [`design.md`](design.md) based on the [Phase 2 outline](../ai-spaarke-insights-engine-r2/PHASE-2-OUTLINE.md) Tier 1 architectural-cleanup items and selected Tier 2–4 capability items.

## 2. Status

| Phase | Status |
|---|---|
| Design discussion (design.md) | 🔄 in flight (owner-mediated focus-area discussion) |
| spec.md | 🔲 not started (derives from design.md) |
| plan.md (wave breakdown) | 🔲 not started (derives from design + spec) |
| Task POMLs | 🔲 not started |
| Implementation | 🔲 not started |

## 3. Working artifacts

| File | Purpose | Status |
|---|---|---|
| [`design.md`](design.md) | Phase 2 design — focus areas, decisions, technical anchors | 🔄 skeleton authored; owner to fill focus areas |
| [`CLAUDE.md`](CLAUDE.md) | Project-scoped AI context (terminology, constraints, key files) | 🔄 skeleton authored; will solidify as design completes |
| [`current-task.md`](current-task.md) | Active task tracker | 🔄 initial state (no active task) |
| `spec.md` | Implementation specification | 🔲 deferred until design solidifies |
| `plan.md` | Wave structure + parallel groups + critical path | 🔲 deferred until spec exists |
| [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) | Per-task status registry | 🔲 placeholder until task POMLs authored |
| [`notes/`](notes/) | Spikes, handoffs, decisions, drafts | 🔲 empty |
| [`decisions/`](decisions/) | Decision records | 🔲 empty |

## 4. Predecessor context

r2 closed 2026-06-04 with all 5 waves shipped + Wave F minor-version contract bump:

| r2 Wave | What shipped | PR |
|---|---|---|
| B | Synthesis unblock (6 sprk_analysisaction rows + node dispatch) | #330 |
| A + C | Foundations (6 design docs) + JPS compliance (universal-ingest@v1 playbook + prompt migration + facade rewire + legacy retirement) | #334 |
| D | 2D taxonomy (practice-area × document-type) + multi-entity subjects (matter/project/invoice) + Index v2 scope | #336 |
| E | Hybrid consumption (`/api/insights/search` + intent classifier) + Spaarke Assistant tool-call (`/api/insights/assistant/query` v1.0) | #337 |
| F | Contract v1.1 minor-version (SSE streaming + clickable `citations[].href`) | #339 |

**Single explicit Phase 2 carry-forward** from r2 spec: **SC-15 (SME calibration ≥50 obs w/ measurable improvement)** — requires production data volume + SME engagement. Substrate (Wave D6 index + Wave D7 fixtures) is shipped; calibration loop is r3 candidate.

**Single load-bearing architectural-debt item** from r2 (flagged by Wave E `adr-check`, intentionally not bundled with Wave E/F): `NullInsightsAi` facade — asymmetric registration close. Tier 1.1 in PHASE-2-OUTLINE. Recommended as r3 first work item.

## 5. Coordination

- **R5** (`spaarke-ai-platform-unification-r5`) — consuming Insights v1.0 + v1.1 via the unified Assistant tool-call endpoint. Cross-worktree coordination doc lives at [`projects/spaarke-ai-platform-unification-r5/notes/insights-r2-coordination.md`](../spaarke-ai-platform-unification-r5/notes/insights-r2-coordination.md). r3 should append to that same doc (or open a successor `insights-r3-coordination.md`) when scope solidifies.
- **r1 wrap-up** is NOT in r3 scope (per r2 R1-1 carried forward).

---

*Created 2026-06-04 by main session after r2 task 090 wrap-up. Next action: owner-mediated discussion of design.md focus areas (Tier 1 cleanup vs. Tier 2 capability vs. mix).*
