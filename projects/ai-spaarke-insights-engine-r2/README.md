# Spaarke Insights Engine — Phase 1.5 (r2)

> **Status**: ✅ **Complete (Phase 1.5)** — closed 2026-06-04 via task 090. 14/15 spec.md SCs met; SC-15 (SME calibration ≥50 observations) explicitly carried to Phase 2. 5 PRs shipped: #330 (Wave B), #334 (Waves A+C), #336 (Wave D), #337 (Wave E), #339 (Wave F contract v1.1). See [`notes/lessons-learned.md`](notes/lessons-learned.md) for the retrospective and [`PHASE-2-OUTLINE.md`](PHASE-2-OUTLINE.md) for r3 input.
> **Created**: 2026-05-30 · **Spec aligned**: 2026-05-31 · **Closed**: 2026-06-04
> **Owner**: Spaarke Engineering
> **Predecessor**: [`ai-spaarke-insights-engine-r1`](../ai-spaarke-insights-engine-r1/) (Phase 1 — shipped + deployed; 17/17 D-P deliverables complete)
> **Successor**: `ai-spaarke-insights-engine-r3` (Phase 2 — TBD per owner discussion; primary input is [`PHASE-2-OUTLINE.md`](PHASE-2-OUTLINE.md))

Phase 1.5 lifts the Insights Engine from Phase 1's plumbing prototype (one playbook, code-defined universal-ingest, single-entity matter scope, litigation-biased classification) to a **usable, multi-tenant, multi-practice-area, multi-entity insights platform** with both pre-authored playbook and ad-hoc RAG consumption paths, prompts SMEs can iterate without code deploys, and a Spaarke Assistant integration.

## What this folder is

Project documentation + tasks for Phase 1.5 — design, spec, plan, wave-organized task files. Implementation lives in `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/` (BFF) + Dataverse playbook + action rows.

## Why r2 (not just continuing r1)

Phase 1 (r1) shipped and deployed but live verification surfaced (1) a discrete unblock (predict-matter-cost end-to-end didn't fire because Insights node ActionTypes weren't bound to `sprk_analysisaction` rows) and (2) four architectural maturations the project owner identified as critical: prompts → JPS storage, single canonical universal-ingest playbook, 2D classification taxonomy, multi-entity subject scheme. r2 separates the Phase 1.5 scope cleanly from r1's wrap-up.

## Acceptance bar (per spec.md §2 / 15 success criteria)

1. `predict-matter-cost@v1` end-to-end returns real `InsightArtifact` or real structured `DeclineResponse` on Spaarke Dev (not the defensive scaffold decline)
2. Per-practice-area routing demonstrably works for ≥3 practice areas
3. Multi-entity subject (`project:`, `invoice:`) resolves live facts correctly
4. `POST /api/insights/search` (NEW) returns ranked Observations + LLM-synthesized summary with citations
5. Spaarke Assistant invokes either path (playbook OR RAG) via intent classifier
6. SME edits prompts on `sprk_analysisaction.sprk_systemprompt` without a code deploy
7. `IngestOrchestrator` retired; universal-ingest is a JPS playbook
8. Zero `.txt` prompt files in `Services/Ai/Insights/Prompts/`
9–15. (See [spec.md](spec.md) §"Success Criteria")

## Wave structure (5 waves)

**B executes FIRST** per owner direction (it's a ½-day unblock that proves Phase 1 design end-to-end before architectural refactors begin).

| Wave | Name | Tasks | Est. Effort | Order |
|---|---|---|---|---|
| **B** | Unblock synthesis | 6 tasks (001–006) | ~½ day | 1st |
| **A** | Foundations (design docs) | 6 tasks (010–015) | ~4 days | 2nd |
| **C** | JPS compliance refactor | 5 tasks (020–024) | ~4–6 days | 3rd |
| **D** | 2D taxonomy + multi-entity | 7 tasks (030–036) | ~1.5–2 weeks | 4th |
| **E** | Hybrid consumption + Assistant | 4 tasks (040–043) | ~1.5–2 weeks | 5th |
| Wrap-up | Lessons learned + archive | 1 task (090) | — | last |

Total: **29 tasks**. ~5–7 weeks of focused work. Parallelization possible within waves; cross-wave ordering is B → A → C → D → E.

## Architectural anchors (carried + corrected from r1)

| Anchor | Phase 1.5 state |
|---|---|
| Insights IS a JPS application | All workflows are JPS data executed by `PlaybookExecutionEngine` (code). No parallel orchestrators. |
| `sprk_analysisaction.sprk_systemprompt` IS the prompt-bearing primitive | r1 already uses this for non-Insights actions (e.g., "Classify Document"). Phase 1.5 retires `.txt` prompts by populating this field. **No new `sprk_prompt` entity.** |
| Single universal-ingest playbook (parameterized) | NOT many ingest playbooks — flexibility via JPS parameters at invocation |
| 2D classification taxonomy | practice-area × document-type; practice-areas sourced from existing `sprk_practicearea_ref` table (APPL, BNKF, CTRNS, IPPAT, IPTM, MA, …) |
| Multi-entity subject scheme | `matter:`, `project:`, `invoice:`, future entities; per-entity `ILiveFactResolver` |
| Hybrid consumption | `/api/insights/ask` (playbook) + `/api/insights/search` (RAG) + intent classifier |
| Cosmos NoSQL graph (D-P17) | Re-deferred to Phase 2; `IInsightGraph` stub remains |

## Documents

| File | Purpose |
|---|---|
| [design.md](design.md) | Owner-facing Phase 1.5 design — context, scope, 9 architectural decisions, risks, open questions |
| [spec.md](spec.md) | AI-optimized implementation spec — Terminology, FRs/NFRs, SCs, BFF Placement Review, Owner Clarifications |
| [plan.md](plan.md) | Wave-organized implementation plan with task numbering, dependencies, parallel groups |
| [CLAUDE.md](CLAUDE.md) | Project-scoped AI context (applicable ADRs, skills, knowledge docs, patterns, constraints) |
| [current-task.md](current-task.md) | Active task state tracker (managed by task-execute) |
| [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) | Task registry + status + parallel groups + critical path |

## Where to start

- **Phase 1.5 scope**: read [spec.md](spec.md) §Scope + §Success Criteria
- **Architectural decisions + rationale**: [design.md](design.md) §3 (D-P15-01..D-P15-09) and [design.md](design.md) §0 (corrections from r1)
- **Wave plan + task sequencing**: [plan.md](plan.md)
- **First task**: 001 — Wave B opens by creating 6 `sprk_analysisaction` rows in Spaarke Dev

## Related

- [r1 project](../ai-spaarke-insights-engine-r1/) — Phase 1 shipped, deployed; r2 picks up the architectural maturation work
- [Insights Engine Architecture](../../docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) — Spaarke-wide arch doc with Phase 1 + Phase 1.5 framing
- [Insights Engine Guide](../../docs/guides/INSIGHTS-ENGINE-GUIDE.md) — operator/developer reference
- [BFF Extensions Constraint](../../.claude/constraints/bff-extensions.md) — binding pre-merge checklist for any BFF additions (load before Wave C/D/E task execution)
