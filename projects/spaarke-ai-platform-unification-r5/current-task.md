# Current Task — Spaarke AI Platform Unification R5

> **Purpose**: Active task state tracker. Managed by `task-execute` skill per CLAUDE.md §7.
> **Status**: READY FOR EXECUTION — `/project-pipeline` complete; all 37 task POMLs generated
> **Last updated**: 2026-06-04

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Active flow** | `/project-pipeline` complete; ready for `task-execute` invocation on task 001 |
| **Pipeline status** | ✅ ALL STEPS COMPLETE — Steps 0–3 done; 37 POMLs on disk + pushed |
| **POML waves shipped** | Wave 1 → 9 (37 POMLs total): Phase 1 (001-009; 9 incl. gate), Phase 2 (010-031; 22 incl. sign-off gate + closure gate), Phase 3 (040-044; 5), wrap-up (090; 1) |
| **Branch** | `work/spaarke-ai-platform-unification-r5` on top of `origin/master` `7e20dc82` |
| **Next action** | User invokes `/task-execute` on `tasks/001-provision-session-files-index.poml` (first task; foundation; blocks downstream P1-G2..G5 wave) |
| **Status** | ready-to-execute (no task in flight) |

### Files Modified This Session (committed)
- `projects/spaarke-ai-platform-unification-r5/design.md` — initial draft + Insights coordination + v1.1 negotiation integration (commits `8ae4e59c`, `6a6c7a29`, `1ecf41a6`)
- `projects/spaarke-ai-platform-unification-r5/spec.md` — generated via /design-to-spec (commit `baa85b27`)
- `projects/spaarke-ai-platform-unification-r5/plan.md` — Step 2 artifact (commit `a0615634`)
- `projects/spaarke-ai-platform-unification-r5/CLAUDE.md` — Step 2 artifact (commit `a0615634`)
- `projects/spaarke-ai-platform-unification-r5/current-task.md` — Step 2 artifact + this checkpoint (commit `a0615634` + this edit)
- `projects/spaarke-ai-platform-unification-r5/tasks/TASK-INDEX.md` — Step 2 artifact (commit `a0615634`)
- `projects/spaarke-ai-platform-unification-r5/notes/insights-r2-coordination.md` — coordination doc + Insights team's §8 update
- `projects/spaarke-ai-platform-unification-r5/notes/insights-engine-assistant-integration-brief.md` — Insights team's binding contract v1.0
- `projects/spaarke-ai-platform-unification-r5/notes/insights-engine-contract-v1.1-request.md` — R5's v1.1 request + post-negotiation state
- `projects/spaarke-ai-platform-unification-r5/notes/insights-team-v1.1-response.md` — R5's response to Insights team feedback
- `.claude/adr/ADR-018-feature-flags.md` — Flag Scope Discipline section added (commit `ee25b49a`)

### Critical Context

R5 is the chat-driven "Summarize a Document" vertical slice + Insights tool integration. Configuration-first (no new ADRs, no new event channels, no new top-level DI registrations). ~36-44 tasks across 3 phases + wrap-up, ~2.5-3 weeks effort. The R5 chat agent becomes the Spaarke Assistant hosting Summarize + Insights tools.

Insights Engine R2 is shipping Wave F in parallel via Claude Code (operator-approved 2026-06-03 late). Wave F branch: `work/ai-spaarke-insights-engine-r2-wave-f`. Wave F adds v1.1 contract additions (SSE + clickable citations). R5 has graceful v1.0 fallback documented in design.md §4.12 + spec.md.

Pipeline pre-flight passed; branch is current with master. R5 lead has NOT yet signed off on `design-e3-tool-call-contract.md` v1.0 + 6 D-decisions (per spec.md §8.2) — required gate before Phase 2 §4.12 work begins, but does NOT block task POML generation.

### Reference materials for resumption
- **POML template**: `.claude/templates/task-execution.template.md` (sub-agents MUST follow strictly)
- **Plan (deliverables)**: `projects/spaarke-ai-platform-unification-r5/plan.md` — each D1-NN / D2-NN / D3-NN deliverable maps to one POML
- **Task registry**: `projects/spaarke-ai-platform-unification-r5/tasks/TASK-INDEX.md` — task IDs, slugs, dependencies, parallel-execution groups, knowledge tags
- **Project rules**: `projects/spaarke-ai-platform-unification-r5/CLAUDE.md` — R5-specific rules + reuse mandate + file paths
- **Spec**: `projects/spaarke-ai-platform-unification-r5/spec.md` — formal FRs/NFRs/DRs/PRs (cross-reference per task)
- **Design**: `projects/spaarke-ai-platform-unification-r5/design.md` — full design rationale

### Wave plan for remaining POML generation (~36 files)

Per project-pipeline Step 5 max-concurrency rule (6 agents per wave):

| Wave | Tasks | Count | Sequential vs parallel |
|---|---|---|---|
| **Wave 1 (in flight)** | 001 | 1 dispatched | Sub-agent ID `afb491f179109d5ff` |
| Wave 1b | 002, 003, 004, 005, 006 | 5 | All parallel (deps 001 OR independent) |
| Wave 2 | 007, 008, 009 | 3 | All parallel (deps satisfied by Wave 1) |
| Wave 3 | 010, 011, 012, 013, 014, 015 | 6 | All parallel within wave |
| Wave 4 | 016, 017, 018, 019, 020, 021 | 6 | All parallel within wave |
| Wave 5 | 022, 023, 024 | 3 | Mixed — 023 is operator-led gate; 022 + 024 parallel |
| Wave 6 | 025, 026, 027, 028, 029 | 5 | All parallel within wave |
| Wave 7 | 030, 031 | 2 | All parallel |
| Wave 8 | 040, 041, 042, 043, 044 | 5 | Mixed parallelism per Phase 3 |
| Wave 9 | 090 | 1 | Final wrap-up |

**Total POML files to generate**: 37. Plus updates to TASK-INDEX.md as POMLs land (status remains 🔲 until task-execute runs them, but file existence verified).

### Resume protocol (post-compaction)

If a fresh session picks up here:

1. Read this Quick Recovery section first
2. Check `projects/spaarke-ai-platform-unification-r5/tasks/` — verify what POML files exist
3. Read `.claude/templates/task-execution.template.md` once for structure reference
4. Read `projects/spaarke-ai-platform-unification-r5/plan.md` for deliverable details
5. Read `projects/spaarke-ai-platform-unification-r5/tasks/TASK-INDEX.md` for per-task IDs/slugs/deps/tags
6. Dispatch parallel sub-agents per the wave plan above. Each sub-agent brief should match the structure used for the in-flight task 001 agent (see prompt pattern in the previous session — concise but comprehensive: template path, deliverable spec, knowledge files, acceptance criteria)
7. After each wave: verify all POML files exist; commit the wave; push; proceed to next wave
8. After all POMLs land: verify with `ls projects/spaarke-ai-platform-unification-r5/tasks/*.poml | wc -l` should show 37; commit any final adjustments; push

---

## 🚦 Active Task

**Status**: NONE — task POML files in generation (wave 1 in flight)

**Next action**: Per Quick Recovery section above — verify task 001 POML, then dispatch remaining waves.

After task generation, the first task to execute will be **D1-01 Session-scoped AI Search index provision** (per plan.md Phase 1 critical path). Execution via `task-execute` skill at FULL rigor per CLAUDE.md §4.

---

## 📋 Project Status

| Artifact | Status |
|---|---|
| README.md | ✅ Exists (kickoff/project overview) |
| design.md | ✅ Authored + integrated with Insights coordination + v1.1 negotiation (commit `1ecf41a6`) |
| spec.md | ✅ Generated via `/design-to-spec` (commit `baa85b27`) — 17 FRs + 14 NFRs + 6 DRs + 5 PRs |
| plan.md | ✅ Authored 2026-06-03 late (this turn) — 3 phases, ~36–44 deliverables, parallel-execution groups, critical path |
| CLAUDE.md (project-specific) | ✅ Authored 2026-06-03 late (this turn) — R5-specific rules + reuse mandate + key file paths |
| current-task.md (this file) | ✅ Created (this turn) |
| tasks/ folder | ✅ 37 POMLs generated (2026-06-04 via parallel sub-agent waves) |
| tasks/TASK-INDEX.md | ✅ READY — header reflects POML generation complete |
| notes/ subfolders | ⚠️ Partial — `notes/` exists with coordination docs; debug/drafts/handoffs/spikes/ subdirectories not yet created |

---

## 📊 Phase Progress (per plan.md)

| Phase | Status | Deliverables |
|---|---|---|
| Phase 1: Platform Extensions | 🔲 NOT STARTED | 9 deliverables (D1-01 to D1-09); ~5 days |
| Phase 2: Vertical Slice + Insights Tool Integration | 🔲 NOT STARTED | 22 deliverables (D2-01 to D2-22); ~7–8 days |
| Phase 3: Polish + Future-Use Validation | 🔲 NOT STARTED | 5 deliverables (D3-01 to D3-05); ~2–3 days |
| Wrap-up (task 090) | 🔲 NOT STARTED | 1 task; ~0.5 day |

---

## 🔗 Pre-Implementation Gates

| Gate | Status | Owner |
|---|---|---|
| Branch rebased onto current origin/master | ✅ Done (2026-06-03 late; commit `baa85b27` on top of `7e20dc82`) | R5 (this session) |
| Master sync (worktree main repo) | ⚠️ Stale at `2252f1c6` — informational only; user-side resolve when convenient | Operator |
| Insights r2 Wave F approval | ✅ Approved by operator 2026-06-03 late; Insights team executing in parallel via Claude Code | Operator + Insights team |
| Insights v1.0 contract sign-off (R5 lead reviews `design-e3-tool-call-contract.md` v1.0 + records 6 D-decisions in §10) | 🔲 PENDING — required before Phase 2 §4.12 work begins | R5 lead (operator) |
| R5 response sent to Insights team | 🔲 PENDING send — file ready at `notes/insights-team-v1.1-response.md` | Operator |
| BFF baseline build verification | ⏭️ DEFERRED — no R5 code changes yet; will catch via Phase 1 task execution | task-execute |

---

## 📝 Last Significant Events

| Date | Event |
|---|---|
| 2026-06-03 (early) | R5 project folder seeded (README + kickoff notes); commit `cc7dd933` |
| 2026-06-03 (mid-day) | design.md drafted; ADR-018 Flag Scope Discipline section added; commit `8ae4e59c` |
| 2026-06-03 (mid-day) | Insights r2 v1.0 contract integrated; v1.1 request authored; commit `6a6c7a29` |
| 2026-06-03 (late) | Insights team v1.1 feedback integrated; design.md + v1.1 request doc updated; Insights response drafted; commit `1ecf41a6` |
| 2026-06-03 (late) | spec.md generated via `/design-to-spec`; commit `baa85b27` |
| 2026-06-03 (late) | Rebased onto `origin/master` `7e20dc82` (PR #338 docs); force-with-lease pushed |
| 2026-06-03 (late) | plan.md + CLAUDE.md + current-task.md authored (this turn) |
| **NEXT** | Task POML file generation via `task-create` |

---

## 🛠️ Workflow Reminders (per root CLAUDE.md)

### Task execution
- ALL R5 tasks invoked via `task-execute` skill (MANDATORY per CLAUDE.md §4)
- All R5 tasks run at **FULL rigor** (BFF code + AI infrastructure + new widgets)
- Quality gates run at task-execute Step 9.5 (`code-review` + `adr-check`)

### Trigger phrases
| User says | Action |
|---|---|
| "work on task X" | Invoke `task-execute` with task X POML |
| "continue" / "next task" | Read `tasks/TASK-INDEX.md`, find first 🔲, invoke `task-execute` |
| "continue with task X" / "resume task X" | Invoke `task-execute` with task X POML |
| "pick up where we left off" | Load this file (`current-task.md`), invoke `task-execute` per status |

### Checkpointing
- After every 3 completed task steps → silent checkpoint ("✅ Checkpoint.")
- After modifying 5+ files → checkpoint
- After any deployment → checkpoint
- Context > 60% → verbose checkpoint report; > 70% → STOP + `/compact`

### Cross-project coordination
- Update `notes/insights-r2-coordination.md` §8 changelog when Wave F status changes
- Check Insights team's Wave F branch (`work/ai-spaarke-insights-engine-r2-wave-f`) status before Phase 2 §4.12 work begins

---

## 🚨 Open Questions / Blockers (none currently blocking)

Per spec.md §8 Unresolved Questions (none block Phase 1):

- UR-01: Tool routing disambiguation when natural language could match both `summarize` AND `insights.query`. Resolution path: tool description refinement during Phase 2.
- UR-02: `StructuredOutputStreamWidget` rendering of Insights vs Summarize outputs. Resolution path: schema-driven rendering with optional `displayHints` config; designed during Phase 2.
- UR-03: Wave F SSE event protocol exact alignment with R5's `FieldDelta`. Resolution path: smoke test against Spaarke Dev when Wave F deploys.
- UR-04: `citations[].href` schema-plumbing spike outcome (Insights Wave F 0.5d spike). Resolution path: Wave F spike output (decision memo).
- UR-05: Per-task BFF publish-size projection. Resolution path: per-task verification per CLAUDE.md §10.

---

*This file is the single-task tracker. Project state (TASK-INDEX.md) tracks per-task status; this file tracks ACTIVE TASK only. When implementation begins, task-execute updates this file at task start + completion.*
