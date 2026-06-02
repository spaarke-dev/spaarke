# Current Task — Spaarke Insights Engine Phase 1.5 (r2)

> **Purpose**: Active task state tracker. Managed by `task-execute` skill.
> **Lifecycle**: Reset between tasks; only the CURRENTLY-ACTIVE task lives here.

---

## Status

**Wave B COMPLETE** (architectural objective) — all 6 tasks closed, D-01 closed.
**Next**: Wave A (foundations) — 6 design-doc tasks, all parallel-safe.
**Current task**: 010 — Architecture overview refresh (Phase 1.5 framing)
**Status**: not-started — ready to begin (or start any A1-A6 task; all independent)

---

## Wave B summary (CLOSED)

| Task | Wave-item | Status |
|---|---|---|
| 001 | B1 Investigation | ✅ D-01 Q1+Q2+Q3 resolved via authoritative docs |
| 002 | B2 Create 6 (later 7) action rows | ✅ INS-FACT/IDXR/EVID/GRND/DECL/RART + INS-AGNT (Wave B4 prep) |
| 003 | B3 Deploy-Playbook.ps1 lint | ✅ Strict actionCode wiring lint; scripts/README.md updated |
| 004 | B4 Delete + redeploy nodes | ✅ -Force redeploy with new actionCode wiring; new playbook Guid `fd584739-965e-f111-ab0c-7c1e521b425f` |
| 005 | B5 Live smoke | ✅ partial — HTTP 200 + playbook executes end-to-end (D-01 dispatch fix proven); structured-decline-extraction follow-up identified |
| 006 | B6 Doc + close D-01 | ✅ D-01 closed; `notes/handoffs/wave-b5-smoke-results.md` documents results + follow-up |

**Architectural fix** (load-bearing for all subsequent Insights work):
- Schema: `sprk_analysisactiontype.sprk_executoractiontype` (int) — single source of truth for dispatch
- Data: 17 lookup rows populated (11 existing = 0, 6 Insights = 70-120, 1 AgentService = 60)
- Code: `AnalysisActionService.cs` reads from `entity.ActionTypeId.ExecutorActionType`
- Deployed: BFF commit `ef869a5b` live on Spaarke Dev
- Playbook: predict-matter-cost@v1 redeployed with all 8 nodes properly wired

**Known follow-up (not in D-01 scope)**: smoke test surfaces that `InsightsPlaybookExecutionCache.DrainEngineStreamAsync` is not extracting either `InsightArtifact` or `DeclineResponse` from the engine stream → orchestrator returns scaffold decline. See `wave-b5-smoke-results.md` "What still needs work" — to be addressed in a follow-up spike or task.

---

## Project context

- **Project**: `ai-spaarke-insights-engine-r2`
- **Branch**: `work/ai-spaarke-insights-engine-r2`
- **Decision record**: [`decisions/D-01-wave-b-root-cause-corrected.md`](decisions/D-01-wave-b-root-cause-corrected.md) — APPROVED + CLOSED 2026-06-02

---

## Wave sequencing

Wave B FIRST → A → C → D → E → wrap-up.

| Wave | Tasks | Status |
|---|---|---|
| **B** (Unblock synthesis) | 001–006 | ✅ COMPLETE |
| **A** (Foundations) | 010–015 | 🔲 NEXT |
| **C** (JPS compliance) | 020–024 | 🔲 |
| **D** (2D taxonomy + multi-entity) | 030–036 | 🔲 |
| **E** (Hybrid + Assistant) | 040–043 | 🔲 |
| Wrap-up | 090 | 🔲 |

---

## Next action

Begin Wave A. All 6 tasks (010-015) are parallel-safe and can run in any order. Recommend starting with A3 (012 — 2D taxonomy design) since it informs the most downstream work (D1 entity creation, D2 prompts, D3 schemas).

---

*Reset on task transition.*
