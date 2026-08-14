# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-08-14 (EXECUTION STARTED — Phase 0)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **EXECUTION 2026-08-14.** Phase 0 COMPLETE. **Phase 1 assessment wave IN PROGRESS** (operator opted in: "use workflow"). |
| **Step** | Phase 0: 001 ✅ 002 ✅ 003 ✅. Phase 1: **019 ✅** (auth map closed against existing artifact — sections A–H verified) · **010 🔄 RUNNING** (Workflow `wfapjhd0d`/`wf_3dd07493-ec8`, background). 011,012,013,014,015,017 queued. 016 (aggregate) after all. |
| **Status** | Phase-0 committed `1aea2f47f` (NOT pushed). **010 assessment workflow launched** (first real execution of `quality-assessment.workflow.js` — proving the runtime before fanning out the rest). On clean return I: review the synthesized `workstreams/shared-client-libs/design.md`, append its SCORECARD row, mark 010 ✅, then **fan out 011 + 017 + 012–015 in parallel**. Workflow verify/synth = Fable xhigh (NFR-05). |
| **Next Action** | (1) Await 010 workflow completion (background notify) → review design + SCORECARD row + mark ✅. (2) Fan out remaining surface assessments in parallel via the same workflow (args per `notes/quality-assessment.workflow.README.md` §3). (3) STILL GATED (not autonomous): **BFF Tranche A (020,021,022,023,027)** + deployment (060–063) — outward-facing PRs, /conflict-check + operator review, one at a time. Auth tasks now unblocked by 019. |

### What happened since init (2026-08-06 → 2026-08-13) — all PLANNING, no execution
- **Initialized** via design-to-spec → project-pipeline (Project #741 under Epic #427; INDEX.md row; NG1 Idea #742). 27 tasks.
- **BFF workstream handoff + Fable verification** integrated: relocated BFF design → `workstreams/bff-api/design.md`; A/B tranche split (020→020+029, 021→021+028); §6 auth resolved to `@spaarke/auth`. 29 tasks.
- **Absorbed r1 deployment-complexity ask** (`notes/deployment-complexity-refactors-ask-2026-08-12.md`) → **Phase 6**; tasks **017** (#1 KV assess), **060** (#3a app-reg drop), **061** (#2 config validation), **062** (#4 Graph app-role constants). #3 SPLIT after Fable grounding (#3a=060 clean; **#3b shared-lib ClientSecret→MI migration → NG1/task-011**). NG1 reframed: deferred → **assess-then-decide (task 011)**. 33 tasks.
- **BFF Auth Surface Map** (owner-requested de-risk): task **019** + `notes/bff-auth-surface-map.md` (Fable). Gates 023/060/061/062. 34 tasks.
- **Resource/secret naming standardization** (owner, productization): task **063** + extended 017; r3 owns standard+gate, **r1 owns apply+live-env remediation** (handback in assessment doc). 35 tasks.
- **Live doc landmine FIXED** (committed): 3 docs told operators `Dataverse-ClientSecret` was safe to remove → crashes BFF. Corrected.
- **Portal confirmations**: A resolved (`BFF-API-ClientSecret`=`1e40baad`), B resolved (no separate `Dataverse-ClientSecret`), #3 CI resolved (OIDC). Remaining self-resolve in tasks (062 role census, PowerBi SP, email Service Endpoint).
- **NET10 (2026-08-14)**: merged net10 master (532 commits); BFF builds clean; baseline 44.96 MB. Integrated `notes/r3-handoff.md` (CVE-no-re-pin, #772 deferred-majors r3-owns via task 032, HELD pkgs, `DiGraphValidationTests` KEEP, ADR-010=153, demo/prod decommissioned→dev-only). **Fable re-verified ALL BFF/auth findings vs net10 HEAD → essentially all STILL HOLD** (§net10 HEAD Reconciliation in `workstreams/bff-api/design.md`); **#3b confirmed still needed**; resolved-by-master: MF-4 (022) + `56ae2188` stale refs (019); new: ~32 dead ServiceException catches (→020 optional). Pushed through `45a7eba51`.

### Critical Context
Standing quality PROGRAM, single worktree, surfaces = workstreams, assessment-first (Fable-verified, gating). Owner decisions live in `CLAUDE.md` §Decisions Made. **Nothing has been executed** — all 35 tasks are 🔲. **@spaarke/auth (ADR-028)** for auth; **#3b credential migration** is the sensitive one (identity-attribution change) on the NG1/011 track; **`BFF-API-ClientSecret`** = 1 KV secret / 5 config keys / 9 consumers (never-remove). Reference docs: `notes/bff-auth-surface-map.md`, `notes/deployment-refactors-assessment-2026-08-12.md`, `workstreams/bff-api/design.md`. **NOTE**: no "daily briefing" work in this project (confirmed 2026-08-13 — that's a different `spaarke-daily-update-service` worktree).

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | none |
| **Task File** | — |
| **Title** | — |
| **Phase** | — |
| **Status** | none |
| **Started** | — |

---

## Progress

### Completed Steps
*No steps completed yet*

### Current Step
*No active task*

### Files Modified (All Task)
*No files modified yet*

### Decisions Made
- 2026-08-06: Finance auth via `@spaarke/auth` (not HMAC) — Reason: owner directive, canonical ADR-028
- 2026-08-06: Assessment-first (Fable-verified) is the gating deliverable — Reason: can't task remediation against un-verified findings
- 2026-08-06: Initialize-only (no auto-execute) — Reason: operator opt-in required for Workflow assessments

---

## Next Action

**Next Step**: Portfolio registration (Epic #427) + INDEX row + NG1 Idea, then Phase 0 task 001 (rubric authoring).

**Pre-conditions**: Epic #427 exists; no orphan R3 Issue.

**Key Context**:
- Refer to `spec.md` FR-01..FR-04 for Phase 0 deliverables
- Refer to `design.md` §5 (rubric D1–D11) + §6 (assessment method)

**Expected Output**: `docs/standards/CODE-QUALITY-RUBRIC.md`, `notes/SCORECARD.md`, `quality-assessment` Workflow, portfolio Issue.

---

## Blockers

**Status**: None

---

## Session Notes

### Current Session
- Started: 2026-08-06
- Focus: Project initialization via /design-to-spec → /project-pipeline (initialize-only)

### Key Learnings
*None yet*

### Handoff Notes
See [`notes/SESSION-HANDOFF.md`](notes/SESSION-HANDOFF.md) for the read-first program handoff.

---

## Quick Reference

### Project Context
- **Project**: code-quality-and-assurance-r3
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)

### Applicable ADRs
- ADR-028: Spaarke Auth v2 — all client→BFF auth via `@spaarke/auth`
- ADR-013: AI facade — CRUD uses `PublicContracts/`
- ADR-032: Null-object kill-switch — preserve verified seams
- ADR-038: Testing — KEEP categories, coverage = observation
- ADR-010 / ADR-022 / ADR-002

### Knowledge Files Loaded
- `.claude/constraints/bff-extensions.md`, `docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`

---

## Recovery Instructions

1. **Quick Recovery**: Read the "Quick Recovery" section above (< 30 seconds)
2. **If more context needed**: Read `notes/SESSION-HANDOFF.md` + `spec.md`
3. **Load task file**: `tasks/{task-id}-*.poml`
4. **Resume**: From the "Next Action" section

**Commands**: `/project-continue`, `/context-handoff`, "where was I?"

---

*This file is the primary source of truth for active work state. Keep it updated.*
