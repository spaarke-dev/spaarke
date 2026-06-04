# Current Task — Spaarke Insights Engine Phase 2 (r3)

> **Purpose**: Active task state tracker. Managed by `task-execute` skill.
> **Lifecycle**: Project initialized 2026-06-04 after r2 task 090 wrap-up.

---

## 🎯 Active task — none (PAUSED pending audit project)

> **🚦 STATUS UPDATE 2026-06-04 (later)**: r3 design scoping is **PAUSED** by owner direction. A separate project [`bff-ai-architecture-audit-r1`](../bff-ai-architecture-audit-r1/) was initiated to audit the BFF's accumulated AI infrastructure and produce canonical-architecture decisions. r3 resumes after that audit completes (~2 weeks).
>
> **What's already locked in r3 design.md and remains valid**:
> - §2.1 Tier 1 cleanup (5 items, ~5d) — Wave 1 work; **safe to proceed independent of audit** (NullInsightsAi, v1.2 spe:// href, test-fixture hygiene, telemetry, index rename)
> - §2.2.1 Tier 2.5 reconciliation — current scope is **PROVISIONAL** pending audit findings. The audit may revise the canonical-architecture decision, expanding or contracting the reconciliation work.
>
> **What's parked**:
> - All further Tier 2 / 3 / 4 scope discussion
> - design.md continued refinement
> - spec.md / plan.md / task POML authoring

r3 is in **design phase / paused** — no implementation tasks yet exist. Active work shifts to the audit project; once audit completes, r3 resumes with informed wave 2 scope.

**Next action (audit project)**: owner-mediated discussion of [`bff-ai-architecture-audit-r1/design.md`](../bff-ai-architecture-audit-r1/design.md) methodology + scope.

**Next action (r3 — after audit)**: revisit r3 design.md §2 with audit's canonical-architecture decisions in hand. Lock final wave 2 / 3 scope. Then:

1. Author full `design.md` content (§1 framing, §2 selected items, §3 decisions, §5 out-of-scope, §6 risks, §8 question answers)
2. Decision records authored to [`decisions/`](decisions/)
3. Derive `spec.md` from design
4. Derive `plan.md` (wave structure + parallel groups + critical path)
5. Generate task POMLs to `tasks/`
6. Begin implementation per wave structure (likely Round 1 spike → Round 2 parallel → Round 3 docs per r2 proven pattern)

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
