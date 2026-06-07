# Spaarke AI Platform Unification R6

> **Status**: Design phase (2026-06-06 — in progress)
> **Predecessor**: R5 closed with known limitations after SC-18 walkthrough surfaced systemic architecture gaps
> **Type**: Architecture phase, not feature project

## What R6 is

R6 is the architecture phase that closes the gap between the platform model Spaarke has been designing toward (data-driven playbooks, registered tools, multi-pane bidirectional state, smart memory) and what currently runs (mostly working code with several abstractions bypassed or unwired). R5 surfaced 9 distinct architecture gaps in a single SC-18 walkthrough cycle; R6 fixes their root causes.

R6 ships **9 architectural pillars**:

1. Persona as Data (Dataverse-driven persona + tenant overrides)
2. Tool Registry as Data (`sprk_analysistools` actually wired up)
3. Generic Playbook Selector (one `invoke_playbook` tool, not N specialized tools)
4. Playbook FK Resolution Fix (orchestrator goes through playbook → node → action)
5. Playbook Output-Type / Rendering Destination (node-level metadata for chat vs workspace vs both)
6. Workspace State Model + Bidirectional Events (workspace tabs are typed, persisted, agent-readable artifacts)
7. Memory + Context Window Management (compression + pinning + selective recall)
8. Command Router (slash/hash vocabulary with parser layer)
9. Workspace Widget Visibility Contract (`getAgentVisibleState()` per widget)

## Why R6 matters

After R6, R7+ feature work (Draft Response, Create Matter, Extract Risks, etc.) becomes "design a playbook in data, register tools, define a schema" — not "write a tool class, write an orchestrator, write a renderer, wire a tab." The investment is foundational: 6 weeks now to save months of integration friction later.

## Project artifacts

- [`design.md`](design.md) — comprehensive design doc (1200+ lines). Includes:
  - Current-state deep dive (how the platform actually works today, including the gaps)
  - R5 closeout summary
  - The 9 pillars in detail
  - Implementation sequencing (4 phases over ~6 weeks)
  - 8 open questions / decisions needed
  - Appendix with verbatim discussion notes

## R5 closeout (prerequisite)

Before R6 implementation can start, R5 needs to formally close:

1. Consolidate cycle-6 through cycle-9 fixes into one PR (~30 min: commit + push + auto-merge)
2. Update R5 TASK-INDEX.md: tasks 022, 030, 031, 035, 037 marked deferred-to-R6
3. Update R5 current-task.md to "R5 closed; R6 in design"
4. Write R5 lessons-learned note

## R6 status

Currently: **design.md complete**. Next steps:
- Spec.md (formal FRs derived from design)
- Plan.md (WBS per pillar)
- CLAUDE.md (project rules)
- Tasks/ folder (POML decomposition)
- Open questions Q1-Q8 reviewed + decided
- Architecture review (if applicable)
- Feature branch + worktree

See `design.md` §9 for the R6 initialization checklist.

---

*Authored 2026-06-06.*
