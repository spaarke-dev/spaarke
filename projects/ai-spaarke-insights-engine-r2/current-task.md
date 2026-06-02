# Current Task — Spaarke Insights Engine Phase 1.5 (r2)

> **Purpose**: Active task state tracker. Managed by `task-execute` skill.
> **Lifecycle**: Reset between tasks; only the CURRENTLY-ACTIVE task lives here.

---

## Status

**Current task**: 002 — Create 6 sprk_analysisaction rows via `/jps-action-create` skill (one per Insights ActionType)
**Task file**: [`tasks/002-update-playbook-json-action-refs.poml`](tasks/002-update-playbook-json-action-refs.poml)
**Wave**: B (Unblock synthesis — sequenced first per WB-1; path-b re-scope per D-01)
**Status**: not-started — ready to begin
**Started**: —
**Rigor**: STANDARD
**Next action**: Begin Step 1 of task 002 (re-read D-01 + investigation handoff notes, then invoke `/jps-action-create` skill for INS-LIVE-FACT)

---

## Task 001 — COMPLETED (2026-06-02)

| Acceptance criterion | Status |
|---|---|
| Each D-01 Q1, Q2, Q3 marked "Resolved per [doc citation]" with one-sentence finding | ✅ |
| Wave B tasks 002-008 confirmed still viable | ✅ — D-01 §4 unchanged |
| No further empirical investigation needed before task 002 starts | ✅ |

**Output**: [`notes/handoffs/wave-b1-investigation-notes.md`](notes/handoffs/wave-b1-investigation-notes.md) — the definitive reference for tasks 002-008. Includes the three-level node type system, the two ActionType dispatch paths, the canvas Designer mapping table, and the Wave B operational rule (do NOT open Insights playbooks in Designer).

**Key findings**:
- ActionType has TWO sources: `sprk_analysisaction.sprk_actiontype` (when node has sprk_actionid FK) AND `sprk_playbooknode.sprk_configjson.__actionType` (when node has no FK). Wave B will set BOTH for safety.
- Designer's canvas-type mapping has only 9 types — none for Insights (60-120). Opening an Insights playbook in Designer wipes configjson. → operational rule documented in task 006.

---

## Project context

- **Project**: `ai-spaarke-insights-engine-r2`
- **Branch**: `work/ai-spaarke-insights-engine-r2`
- **Decision record**: [`decisions/D-01-wave-b-root-cause-corrected.md`](decisions/D-01-wave-b-root-cause-corrected.md) — APPROVED; Q1+Q2+Q3 resolved

---

## Wave sequencing

Wave B FIRST → A → C → D → E → wrap-up.

| Wave | Tasks | Status |
|---|---|---|
| **B** (Unblock synthesis) | 001–006 | 🔄 in-progress (001 ✅; 002 ready) |
| **A** (Foundations) | 010–015 | 🔲 |
| **C** (JPS compliance) | 020–024 | 🔲 |
| **D** (2D taxonomy + multi-entity) | 030–036 | 🔲 |
| **E** (Hybrid + Assistant) | 040–043 | 🔲 |
| Wrap-up | 090 | 🔲 |

---

## Active task: 002 (ready to start)

### Goal

6 new sprk_analysisaction rows live in Spaarke Dev, each authored via `/jps-action-create` skill workflow. Action codes: INS-LIVE-FACT, INS-INDEX-RETRIEVE, INS-EVIDENCE-SUFFICIENCY, INS-GROUNDING-VERIFY, INS-DECLINE-TO-FIND, INS-RETURN-ARTIFACT. Each row: valid JPS JSON in sprk_systemprompt + sprk_actiontype set to matching enum integer (80/90/100/70/110/120).

### Safety gate (carried forward from task 001)

⚠️ This task creates real rows in Spaarke Dev (shared state). After designing the 6 JPS prompts via the skill, **pause and present them to owner for review** before executing the 6 `mcp__dataverse__create_record` calls. The skill workflow naturally provides preview output for each prompt before commit.

### Steps to be executed

1. Re-read D-01 + investigation handoff notes
2. For each of 6 ActionTypes: read the corresponding INodeExecutor source file
3. Invoke `/jps-action-create` for INS-LIVE-FACT (Step 3 in task POML)
4-8. Same for the other 5 ActionTypes
9. Create 6 rows in Spaarke Dev via `mcp__dataverse__create_record` (after owner review)
10. Verify rows queryable
11. Update D-01 + handoff notes with action code → Guid map

---

*Reset on task transition.*
