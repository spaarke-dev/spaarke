# Current Task — Spaarke Insights Engine Phase 2 (r3)

> **Purpose**: Active task state tracker. Managed by `task-execute` skill.
> **Lifecycle**: Project initialized 2026-06-04 after r2 task 090 wrap-up.

---

## 🎯 Active task — none (⏸ PAUSED pending R6 ship)

> **🚦 STATUS UPDATE 2026-06-10 (current)**: r3 remains **PAUSED**. The audit that originally caused the pause completed 2026-06-08 (✅ all 10 PRs merged including #341, #343, #344, #346, #347, #348, #351, #353, #357, #360). The audit's `r3-scope-recommendations.md` is now binding input.
>
> **NEW pause reason** (replaces audit-pending): R6 (`spaarke-ai-platform-unification-r6`) is reshaping consumer patterns that r3 Wave 2 touches. Coordinating "in-flight" R6 + r3 in parallel is impractical. Owner has pivoted capacity to [`ai-spaarke-insights-engine-widgets-r1`](../ai-spaarke-insights-engine-widgets-r1/) (NEW project 2026-06-10) for user-visible value first.
>
> **REQUIRED READING before resumption**: [`notes/pre-design-context-2026-06-10.md`](notes/pre-design-context-2026-06-10.md) — full pause rationale, r3↔R6 coordination analysis, 3 specific resolution points, resumption plan.
>
> **What's now SHIPPED from r3's original scope (via audit)**:
> - r3 Wave 1.1 `NullInsightsAi` facade ✅ — SUPERSEDED by audit Migration PR #1 (PR #351 merged 2026-06-04)
>
> **What's PARKED with r3**:
> - r3 Wave 1.2 (spe:// href resolution v1.2 contract)
> - r3 Wave 1.3 (test-fixture hygiene)
> - r3 Wave 1.4 (telemetry maturity + dashboards)
> - r3 Wave 1.5 (index rename `playbook-embeddings` → `spaarke-playbook-index`)
> - r3 Wave 2 (InsightsIntentClassifier ↔ PlaybookDispatcher reconciliation — locked at ~1 week scope per audit)
> - All Tier 2/3/4 design discussion
>
> **Resumption trigger** (all 3 conditions): R6 ships its surface-defining pillars (Pillar 3 + 5 + 6) AND owner directs resumption AND widgets-r1 reaches first proof-point.

**Next action at resumption**: see [`notes/pre-design-context-2026-06-10.md`](notes/pre-design-context-2026-06-10.md) §7 (step-by-step resumption sequence).

---

## Project status

| Phase | Status |
|---|---|
| Initialized | ✅ 2026-06-04 |
| `design.md` skeleton | ✅ authored |
| `design.md` focus areas | 🔄 pending owner discussion |
| `spec.md` | 🔲 deferred |
| `plan.md` | 🔲 deferred |
| Task POMLs | 🔲 deferred |
| Implementation | 🔲 deferred |

---

## Predecessor handoff (from r2 task 090)

r2 closed 2026-06-04 with all 5 waves + Wave F minor-version contract shipped. **14 of 15 spec.md SCs met**; SC-15 (SME calibration) carried to Phase 2. Single load-bearing architectural-debt carry-forward: `NullInsightsAi` facade (asymmetric registration on `IInsightsAi`, flagged by Wave E `adr-check`, deferred from Wave F to keep scope tight).

**Primary inputs to r3 design**:
1. [`r2/PHASE-2-OUTLINE.md`](../ai-spaarke-insights-engine-r2/PHASE-2-OUTLINE.md) — 4 tiers, 22 candidate items
2. [`r2/notes/lessons-learned.md`](../ai-spaarke-insights-engine-r2/notes/lessons-learned.md) — what worked / didn't / would do differently
3. [`r2/design-e3-tool-call-contract.md`](../ai-spaarke-insights-engine-r2/design-e3-tool-call-contract.md) v1.1 — locked Assistant contract

---

## Coordination

- **R5** (`spaarke-ai-platform-unification-r5`) — primary consumer; coord doc at [`spaarke-ai-platform-unification-r5/notes/insights-r2-coordination.md`](../spaarke-ai-platform-unification-r5/notes/insights-r2-coordination.md). r3 either appends to that doc's §8 changelog or opens a successor `insights-r3-coordination.md` when scope solidifies.
- **r1 wrap-up** — NOT in r3 scope (R1-1 carried forward from r2).
- **Spaarke Dev** — shared BFF App Service; deploys still coordinated per [`bff-extensions.md`](../../.claude/constraints/bff-extensions.md) §F.4.

---

*Initial state 2026-06-04 by main session post-r2-090. Will update as design.md focus areas lock + task POMLs are authored.*
