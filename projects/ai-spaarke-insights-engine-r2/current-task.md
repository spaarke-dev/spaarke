# Current Task — Spaarke Insights Engine Phase 1.5 (r2)

> **Purpose**: Active task state tracker. Managed by `task-execute` skill.
> **Lifecycle**: Reset between tasks; only the CURRENTLY-ACTIVE task lives here.

---

## Status

**Current task**: 001 — Investigate playbook-architecture + scope-model-index (resolve D-01 open questions)
**Task file**: [`tasks/001-create-insights-action-rows.poml`](tasks/001-create-insights-action-rows.poml)
**Wave**: B (Unblock synthesis — sequenced first per owner direction WB-1; re-scoped 2026-06-02 per D-01 path-b + JPS-skills owner direction)
**Status**: paused — awaiting owner approval to resume execution of re-scoped task 001
**Started**: 2026-06-02 (original); paused 2026-06-02 after empirical investigation surfaced broader failure mode (see D-01)
**Rigor**: STANDARD
**Decision record**: [`decisions/D-01-wave-b-root-cause-corrected.md`](decisions/D-01-wave-b-root-cause-corrected.md) — APPROVED path-b re-scope + JPS-skills constraint
**Next action**: Resume task 001 (now: read playbook-architecture.md + scope-model-index.json → resolve D-01 Q1+Q2+Q3) after owner confirms the re-scope changes.

---

## Project context (loads on task-execute Step 0)

- **Project**: `ai-spaarke-insights-engine-r2`
- **Branch**: `work/ai-spaarke-insights-engine-r2`
- **Worktree**: `c:\code_files\spaarke-wt-ai-spaarke-insights-engine-r2`
- **Spec**: [`spec.md`](spec.md)
- **Plan**: [`plan.md`](plan.md)
- **Project CLAUDE.md**: [`CLAUDE.md`](CLAUDE.md)
- **Task index**: [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md)

---

## Wave sequencing (per owner direction WB-1)

Wave B FIRST → A → C → D → E → wrap-up.

| Wave | Tasks | Status |
|---|---|---|
| **B** (Unblock synthesis) | 001–006 | 🔄 in-progress (001 active) |
| **A** (Foundations) | 010–015 | 🔲 not-started |
| **C** (JPS compliance) | 020–024 | 🔲 not-started |
| **D** (2D taxonomy + multi-entity) | 030–036 | 🔲 not-started |
| **E** (Hybrid + Assistant) | 040–043 | 🔲 not-started |
| Wrap-up | 090 | 🔲 not-started |

---

## Active task: 001

### Goal

6 new sprk_analysisaction rows in Spaarke Dev, each carrying JPS-formatted system prompt where applicable, for Insights node ActionTypes (LiveFact, IndexRetrieve, EvidenceSufficiency, GroundingVerify, DeclineToFind, ReturnInsightArtifact). These rows enable the existing predict-matter-cost@v1 playbook's nodes to dispatch correctly (currently failing because action wiring is absent — defensive scaffold decline at the orchestrator).

### Knowledge files loaded

- (pending) `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Prompts/*.txt` (3 prompt files)
- (pending) `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/predict-matter-cost.playbook.json`
- (pending) `src/server/api/Sprk.Bff.Api/Services/Insights/Graph/` (node executor directory)
- (pending) `.claude/skills/dataverse-mcp-usage/SKILL.md`
- (pending) Existing "Classify Document" sprk_analysisaction row (via MCP read_query)

### Constraints

- spec.md PR-1: All prompts go into sprk_analysisaction.sprk_systemprompt (existing JPS primitive; no new sprk_prompt entity)
- spec.md NFR-03: Every Insights playbook node must reference a sprk_analysisaction row
- ADR-027: Schema/data changes flow through managed-solution promotion path (Spaarke Dev for B1 acceptable)

### Steps completed

(none yet)

### Files to be modified

- (planned) New 6 sprk_analysisaction rows in Spaarke Dev (data, not source code)
- (planned) `projects/ai-spaarke-insights-engine-r2/notes/drafts/wave-b-action-codes.md` (NEW — final action codes + JPS prompt content per row)

### Decisions made

- 2026-06-02: Rigor STANDARD chosen — task is data-only (no .cs/.ts modifications); 6 boundary steps; constraints + ADRs apply.

### Safety gate

⚠️ This task creates real rows in Spaarke Dev (shared state). After designing the 6 prompts in JPS format, **pause and present them to owner for review** before executing `mcp__dataverse__create_record`.

---

*Reset on task transition.*
