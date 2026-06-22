# Current Task State — Spaarke AI Platform Chat Routing Redesign (R1)

> **Last Updated**: 2026-06-21 (by `/context-handoff` at pipeline mid-execution)
> **Recovery**: Read "Quick Recovery" section first. After compact, the next instruction will be to spawn 6 parallel POML-generation agents.

---

## Quick Recovery (READ THIS FIRST — <30 seconds)

| Field | Value |
|-------|-------|
| **Project** | `spaarke-ai-platform-chat-routing-redesign-r1` |
| **Branch** | `work/spaarke-ai-platform-chat-routing-redesign-r1` (worktree) |
| **Pipeline step** | `/project-pipeline` Step 3 (task-create) — POML generation in progress |
| **Task** | none active — pipeline orchestration in progress |
| **Status** | mid-pipeline — handoff checkpoint before parallel-agent POML generation |
| **Next Action** | **Spawn 6 parallel `general-purpose` agents to generate the 113 deferred POML files (📄 in TASK-INDEX). Partitioning + agent prompts ready (see "Parallel POML Generation Plan" section below).** |

### Files Modified This Session (uncommitted — STAGED for project-init commit later)

Modified files:
- `.claude/CHANGELOG.md` — ADR-030 v2 amendment entry (2026-06-21 chat-routing-redesign-r1)
- `.claude/adr/ADR-030-pane-event-bus.md` — v2 amendment: `memory` channel added (5-channel union)
- `docs/adr/ADR-030-pane-event-bus.md` — full ADR matching amendment
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/design.md` — v3.3 reframing markers (Insights reuse boundary; /summarize bug fixed in R6)

New files:
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/spec.md` — 45 FRs + 19 NFRs (reconciled with architecture v3.3)
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/architecture/stateful-chat-architecture.md` — 1087 lines, binding for WP5
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/README.md`
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/plan.md` — 120-task pipeline plan
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/CLAUDE.md` — AI context loader
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/current-task.md` — THIS FILE
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/tasks/TASK-INDEX.md` — audited; 120 tasks, 55 waves
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/tasks/001-delete-legalworkspace-creatematter-deadcode.poml`
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/tasks/002-pcf-useaisummary-duplicate-resolution.poml`
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/tasks/003-scrub-stale-guid-comments.poml`
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/tasks/004-phase-0-smoke-test.poml`
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/tasks/010-stand-up-by-code-endpoint.poml`
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/tasks/015-migrate-sessionsummarizeorchestrator-to-stable-code.poml`
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/tasks/150-project-wrap-up.poml`
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/notes/{debug,spikes,drafts,handoffs}/.gitkeep`

### Critical Context

1. **`/project-pipeline` is mid-execution**. Phase 0-3 (Steps 0–3) complete; Steps 4–5 pending. Steps 4 = two project-init commits. Step 5 (auto-task-execute) deliberately SKIPPED per plan — task 001 runs in a separate fresh session.
2. **TASK-INDEX.md was audited and rewritten** with 7 critical fixes (CRIT-1 through CRIT-7) covering parallel-execution race conditions. Total tasks: **120** (not the 82 originally written; arithmetic was wrong). Audit history recorded in TASK-INDEX itself.
3. **Existing 7 POML files are correct** with audited numbering (000 doesn't exist yet — to be generated; 001-004 + 010 + 015 + 150 are materialized; rest are 📄 stubs deferred to parallel-agent generation).
4. **No git commits yet for this session's work** — all changes uncommitted, intentionally staged for a single "project init" commit. The plan was two commits (1: in-session edits as seed; 2: generated artifacts) but with all the edits, a single commit is now cleaner.
5. **ADR-030 v2 amendment is the only `.claude/` change** in this branch. Already complete; just needs to be committed.

---

## Parallel POML Generation Plan (post-compact resume target)

After compact, the next user message will say something like "spawn the 6 parallel agents" or "continue". When that happens:

### Goal
Generate 113 POML files (the 📄 stubs in TASK-INDEX) for the deferred tasks. Use 6 parallel `general-purpose` agents per CLAUDE.md max-concurrency rule.

### Agent partitioning

| Agent | Scope | Task IDs | Count |
|---|---|---|---|
| **A** | Phase 0 remaining + Phase 1 remaining | 000, 011, 012, 013, 014, 016, 017, 018, 019, 020, 021, 022, 023, 024, 025, 026, 027 | 17 |
| **B** | Phase 2 + Phase 3 | 030–040 + 045–055 | 22 |
| **C** | Phase 4a + 4b + 4c (waves 4-A through 4-F) | 060–064, 066–074, 076–080 | 19 |
| **D** | Phase 4d + 4e (waves 4-G through 4-N) | 083–099 | 17 |
| **E** | Phase 4f + Phase 5 | 100–105 + 110–119 | 16 |
| **F** | Phase 6 + Phase 7 | 120–132 + 140–148 | 22 |
| **Total** | | | **113** |

### Each agent prompt should include (in one parallel-spawn message — 6 Agent tool calls)

1. **Identity**: "You are generating POML task files for the chat-routing-redesign-r1 project. Output to `projects/spaarke-ai-platform-chat-routing-redesign-r1/tasks/`."
2. **Authoritative inputs to read** (paths):
   - `projects/spaarke-ai-platform-chat-routing-redesign-r1/tasks/TASK-INDEX.md` — task metadata (canonical)
   - `projects/spaarke-ai-platform-chat-routing-redesign-r1/spec.md` — FR/NFR acceptance
   - `projects/spaarke-ai-platform-chat-routing-redesign-r1/architecture/stateful-chat-architecture.md` — WP5 binding
   - `projects/spaarke-ai-platform-chat-routing-redesign-r1/CLAUDE.md` — context loading rules
   - **Reference POML exemplars** (for structural pattern): `tasks/001-delete-legalworkspace-creatematter-deadcode.poml`, `tasks/015-migrate-sessionsummarizeorchestrator-to-stable-code.poml`, `tasks/150-project-wrap-up.poml`
   - `.claude/templates/task-execution.template.md` — required POML elements
   - `.claude/skills/task-create/SKILL.md` Step 3.5/3.65/3.8 — Tag-to-knowledge mapping, UI tests, parallel-grouping rules
3. **POML structure requirements** (per task-create skill):
   - Root: `<task id="{NNN}" project="spaarke-ai-platform-chat-routing-redesign-r1">`
   - Required sections: metadata, prompt, role, goal, context, constraints, knowledge, steps, tools, outputs, acceptance-criteria, notes, execution
   - `<rigor-hint>` + `<rigor-reason>` (FULL/STANDARD/MINIMAL per task-create Step 3.5.5)
   - `<parallel-group>` + `<parallel-safe>` per TASK-INDEX Wave column
   - `<knowledge><files>` MUST be non-empty (per task-create Step 3.4 tag-to-knowledge mapping)
   - For PCF/frontend tasks: include `<ui-tests>` section
4. **Style**: Concise but complete. ~80-150 lines per POML. Match the structural pattern of the 3 exemplars exactly.
5. **Naming**: `{NNN}-{kebab-slug}.poml`. Slug should be 3-5 words descriptive of the task.
6. **Output**: Each agent generates POML files for its task ID range. Returns a summary listing files created and any issues.
7. **DO NOT modify**: TASK-INDEX.md (only main session updates the index), spec.md, design.md, architecture.md, CLAUDE.md, README.md, plan.md, existing POML files.

### After agents return

Main session validates:
1. Spot-check 3-5 random POML files (one per agent) for XML validity + non-empty `<knowledge><files>` + correct `<parallel-safe>` per index
2. Count files materialized: `ls tasks/*.poml | wc -l` should be 120 (7 existing + 113 new)
3. Update TASK-INDEX `POML Materialization Plan` table to reflect 120/120 materialized

### Then Step 4 commit

Single commit (combined seed + generated):
```
feat(chat-routing-redesign-r1): init project — spec, architecture, ADR-030 v2 amendment, plan, README, CLAUDE.md, 120 task POMLs

- spec.md (45 FRs across 6 WPs + 19 NFRs incl. NFR-A1-A7 architectural principles)
- architecture/stateful-chat-architecture.md (6-tier memory + Insights reuse boundary; binding for WP5)
- design.md v3.3 reframing markers
- ADR-030 v2 amendment (memory channel) — concise + full ADR + CHANGELOG entry
- README.md, plan.md (120 tasks, 8 phases, 55 waves), CLAUDE.md, current-task.md
- tasks/TASK-INDEX.md (audited; CRIT-1 through CRIT-7 fixes applied)
- 120 POML task files

🤖 Generated with Claude Code
```

No push. No PR creation. (Per plan: user pushes deliberately later.)

---

## Pipeline Progress Tracker

| Step | Status | Notes |
|---|---|---|
| Plan mode setup | ✅ | Plan approved 2026-06-21 |
| Step 1.5 PR overlap check | ✅ | PR #401 (R6 hotfix) blocks Phase 7; PR #406 minor; dependabot noted |
| Step 2a Resource discovery | ✅ | 12 ADRs identified; skills/patterns mapped; Dataverse schema validated via MCP |
| Step 2b project-setup | ✅ | README, plan.md, CLAUDE.md, current-task.md, folder structure created |
| Step 2b enrichment | ✅ | plan.md + CLAUDE.md enriched with discovered resources |
| Step 3 task-create | 🔄 in progress | TASK-INDEX rewritten + audited; 7/120 POMLs materialized; 113 to generate via parallel agents |
| Audit pass | ✅ | CRIT-1 through CRIT-7 fixes applied; arithmetic corrected; renumber cascade done; Phase 7 reordered |
| Step 4 commits | ⏭️ pending | Single combined commit planned (was 2; collapsed to 1) |
| Step 5 auto-task-execute | ⏭️ DELIBERATELY SKIPPED | Task 001 runs in fresh session |

---

## Active Task

| Field | Value |
|---|---|
| **Task ID** | none (pipeline orchestration) |
| **Phase** | — |
| **Status** | none |

---

## Key Decisions Made This Session

| Time | Decision | Rationale |
|---|---|---|
| Plan approval | Renumber cascade for Phase 1 (013–026 → 014–027 after inserting new 013) | User chose; cleaner integer IDs |
| Plan approval | Phase 7 reorder: code-review + adr-check BEFORE UAT | User chose; standard quality-gate sequence |
| Audit | New task 000 (R6 readiness check) added | Prevents silent start on incomplete R6 closeout |
| Audit | New task 013 (WorkspaceOptions extension) added | Resolves CRIT-1 file-overlap race in Pattern A wave |
| Audit | New task 070 (DI registration for upload services) added | Resolves CRIT-3 |
| Audit | New task 091 (DI registration for 8 tool handlers) added | Resolves CRIT-4 |
| Audit | Wave 4-A split into 4-A1 (060 alone) + 4-A2 (061-064 parallel) | Resolves CRIT-2 sequential dependency on PaneEventTypes.ts edit |
| Audit | Cross-wave dependency exceptions table added (098 needs 060; 103 needs 064) | Resolves CRIT-5 |
| Audit | Wave 7-B made sequential (single agent atomic commit for CapabilityRouter deletion + FR-23 tool filtering) | Resolves CRIT-6 |
| Pipeline | 6-parallel-agent strategy for POML generation | User explicitly noted past projects use this; my single-stream approach was wrong |
| Compaction | Compact before parallel-agent spawn | Context at 62%; CLAUDE.md threshold; quality concern requires headroom |

---

## Blockers

**Status**: None blocking. Compact pending.

---

## Recovery Instructions (post-compact)

When the new session starts:

1. **First user message** likely "continue" or "spawn the agents" or similar
2. **Read this file's "Quick Recovery" section** first (< 30 seconds)
3. **Read the "Parallel POML Generation Plan" section** above for exact agent partitioning + prompts
4. **Read `tasks/TASK-INDEX.md`** for the canonical task metadata
5. **Optionally re-load** spec.md / architecture / CLAUDE.md (depending on what context the agents need)
6. **Spawn the 6 parallel agents** in a single message (6 Agent tool calls)
7. **After agents return**, spot-check 3-5 POMLs for quality, then proceed to Step 4 single commit
8. **Final report** to user with commit SHA + total POML count

### Critical guards
- DO NOT auto-start `/task-execute 001` — that's a separate fresh session per the plan
- DO NOT push or open a PR — user pushes deliberately later
- DO confirm tasks in 1-E (016, 017, 018, 019) are now truly parallel-safe because task 013 pre-creates the typed options they consume

---

## Quick Reference

### Project Context
- **Project**: `spaarke-ai-platform-chat-routing-redesign-r1`
- **Worktree**: `c:\code_files\spaarke-wt-spaarke-ai-platform-chat-routing-redesign-r1\`
- **Branch**: `work/spaarke-ai-platform-chat-routing-redesign-r1`
- **Pipeline plan file**: `C:\Users\RalphSchroeder\.claude\plans\yes-in-plan-mode-silly-gadget.md`

### Key files
- [`spec.md`](spec.md), [`design.md`](design.md), [`architecture/stateful-chat-architecture.md`](architecture/stateful-chat-architecture.md), [`README.md`](README.md), [`plan.md`](plan.md), [`CLAUDE.md`](CLAUDE.md), [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md)
- ADR-030 v2 amendment: [`../../.claude/adr/ADR-030-pane-event-bus.md`](../../.claude/adr/ADR-030-pane-event-bus.md)
- POML exemplars on disk: 001, 002, 003, 004, 010, 015, 150

### Applicable ADRs (active this project)

ADR-001, ADR-008, ADR-010, ADR-013, ADR-014, ADR-015, ADR-018, ADR-019, ADR-029, **ADR-030 v2 (amended by this project)**, ADR-032, ADR-033. See [`CLAUDE.md`](CLAUDE.md) for per-ADR reasoning.

---

*This file is the primary source of truth for active work state. Updated by `/context-handoff` at compact-prep checkpoint.*
