# R4 Task Index

> **Project**: spaarke-ai-platform-unification-r4
> **Total tasks**: 32 (31 work tasks + 1 wrap-up)
> **Created**: 2026-05-26 via `/project-pipeline` → `/task-create`
> **Sources**: [`spec.md`](../spec.md) (FRs/NFRs/DRs/PRs) + [`plan.md`](../plan.md) (WBS) + [`plan.original.md`](../plan.original.md) (authoritative WBS detail)

---

## Status Legend

| Symbol | Meaning |
|---|---|
| 🔲 | not-started |
| 🔄 | in-progress / needs-retry |
| ⏸ | blocked |
| ✅ | completed |
| ❌ | deferred or abandoned |

---

## Task Roster

| ID | Title | Phase | Status | Item | Rigor | Deps | Parallel-group / Safe |
|---|---|---|---|---|---|---|---|
| 001 | E-1 R3 project wrap-up | 0 | ✅ | E-1 / PR-01 | STANDARD | none | A / ✅ |
| 002 | F-1 BFF placement-justification retroactive memo | 0 | ✅ | F-1 / NFR-02 | STANDARD | none | A / ✅ |
| 010 | W-1 Write SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md | 1 | ✅ | W-1 / DR-01 | STANDARD | none | B / ✅ |
| 011 | W-2 Rewrite BUILD-A-NEW-WORKSPACE-WIDGET.md | 1 | ✅ | W-2 / DR-02 | STANDARD | 010 ✅ | — / ❌ (deps) |
| 012 | A-2a Author ADR-030 (PaneEventBus, renumbered from 025) | 1 | ✅ | A-2a / DR-04 | STANDARD | none | — / ❌ (`.claude/`) |
| 013 | A-2b Author ADR-031 (stage lifecycle, renumbered from 026) | 1 | ✅ | A-2b / DR-04 | STANDARD | none | — / ❌ (`.claude/`) |
| 014 | C-1 Write DATA-ACCESS-DECISION-CRITERIA.md | 1 | ✅ | C-1 / DR-06 | STANDARD | none | B / ✅ |
| 015 | C-2 Write LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md | 1 | ✅ | C-2 / DR-07 | STANDARD | 010 ✅ | — / ❌ (deps) |
| 016 | D-2 Amend ADR-031 heavy library handling | 1 | 🔲 | D-2 / DR-05 | STANDARD | 013 | — / ❌ (`.claude/` + deps) |
| 017 | F-3 Document publish-size baseline rule | 1 | ✅ | F-3 / NFR-01 | STANDARD | none | — / ❌ (`.claude/` + CLAUDE.md) |
| 020 | F-2 BFF facade audit | 2 | ✅ | F-2 / NFR-03 | STANDARD | none | — / ✅ |
| 030 | A-5a Verify tab persistence | 3 | ✅ | A-5a / FR-05 | STANDARD | none | — / ✅ |
| 031 | A-5b Fix tab persistence | 3 | 🔲 ⏸ operator gate | A-5b / FR-05 | FULL | 030 ✅ | — / ❌ (deps + operator gate) |
| 040 | W-3 Fix WorkspaceLayoutWizard catalog drift | 4 | 🔲 | W-3 / FR-01 | FULL | none | D / ✅ |
| 041 | W-6 Document LegalWorkspace retirement | 4 | ✅ | W-6 / DR-03 | STANDARD | none | D / ✅ |
| 042 | W-4 Wire Assistant → Workspace mount source | 4 | 🔲 | W-4 / FR-02 | FULL | 010, 040 | — / ❌ (deps) |
| 043 | W-5 Wire Context → Workspace mount source | 4 | 🔲 | W-5 / FR-03 | FULL | 010 | E / ✅ (with 042 coord) |
| 050 | A-4 Attachment policy + raise cap to 25 MB | 5 | 🔲 | A-4 / FR-04 | FULL | none | F / ✅ |
| 051 | C-3 Consolidate dual useWorkspaceLayouts hooks | 5 | 🔲 | C-3 / FR-13 | FULL | none | F / ✅ |
| 052 | C-4 WorkspaceRenderer interface | 5 | 🔲 | C-4 / FR-14 | FULL | 051 | — / ❌ (deps) |
| 053 | B-4 WorkspaceLayoutDto.modifiedOn | 5 | 🔲 | B-4 / FR-07 | FULL | none | F / ✅ |
| 054 | B-5 BFF PUT → PATCH/ETag | 5 | 🔲 | B-5 / FR-08 | FULL | 053 | — / ❌ (same files as 053) |
| 055 | B-6 Reconcile CalendarSidePane CalendarSection | 5 | 🔲 | B-6 / FR-09 | FULL | none | G / ✅ |
| 060 | B-1 .gitignore for tracked build artifacts | 6 | 🔲 | B-1 / NFR-04 | MINIMAL | none | H / ✅ |
| 061 | B-2 @spaarke/ai-widgets tsc rootDir fix | 6 | 🔲 | B-2 / NFR-05 | STANDARD | none | H / ✅ |
| 062 | B-3 Telemetry constant rename | 6 | 🔲 | B-3 / FR-06 | STANDARD | none | H / ✅ |
| 063 | B-7 Extract useEventsBulkActions hook | 6 | 🔲 | B-7 / FR-10 | FULL | none | H / ✅ |
| 064 | B-8 CalendarDrawer.eventDates API | 6 | 🔲 | B-8 / FR-11 | FULL | 063 | — / ❌ (same lib as 063) |
| 065 | B-9 ESLint v9 migration | 6 | 🔲 | B-9 / NFR-06 | STANDARD | none | I / ✅ |
| 066 | B-10 Standalone EventsPage redeploy | 6 | 🔲 | B-10 / NFR-07 | MINIMAL | none | I / ✅ |
| 067 | B-11 Type-drift casts cleanup | 6 | 🔲 | B-11 / FR-12 | FULL | 063, 064 | — / ❌ (deps) |
| 090 | R4 Project Wrap-up | 7 | 🔲 | PR-02 | FULL | all 31 | — / ❌ (final) |

---

## Parallel Execution Plan

Tasks in the same group can run simultaneously once prerequisites are met. **Hard cap: 6 concurrent agents per wave** (per `task-execute` skill — API overload guard).

**Permission boundary (CLAUDE.md §3)**: Tasks touching `.claude/` paths are forced to `parallel-safe: false` and MUST run in the main session only — sub-agents cannot write to `.claude/`. These tasks (012, 013, 016, 017) run sequentially via the main session.

### Phase 0 — R3 wrap-up + retroactive memo (~4h) ✅ DONE

| Wave | Tasks | Concurrency | Prerequisite |
|---|---|---|---|
| Wave 0.1 (Group A, parallel) | 001, 002 | ✅ Completed in commit `4a877b1e` (2026-05-26) prior to pipeline run | none |

### Phase 1 — Documentation round (~21h)

| Wave | Tasks | Concurrency | Prerequisite |
|---|---|---|---|
| Wave 1.1 (Group B, parallel) | 010 ✅, 014 ✅ | Completed 2026-05-26 by parallel sub-agents | none |
| Wave 1.2 (sequential, main session) | 012 ✅, 013 ✅, 017 ✅ | Completed 2026-05-26 (012+013 via hybrid sub-agents writing docs/adr/ + main session writing .claude/adr/; 017 main session) | none — `.claude/` boundary |
| Wave 1.3 (after Wave 1.1) | 011, 015 | 2 agents | 010 ✅ |
| Wave 1.4 (sequential, main session) | 016 | 1 task | 013 ✅ + `.claude/` boundary |

### Phase 2 — BFF governance audit (~2h)

| Wave | Tasks | Concurrency | Prerequisite |
|---|---|---|---|
| Wave 2.1 | 020 | 1 task | none — can run anytime |

### Phase 3 — UQ-03 verify + fix (~10h)

| Wave | Tasks | Concurrency | Prerequisite |
|---|---|---|---|
| Wave 3.1 | 030 | 1 task | none — verify-first |
| Wave 3.2 | 031 | 1 task | 030 ✅ + operator approval of verification result |

### Phase 4 — Workspace builder + mount sources (~19h)

| Wave | Tasks | Concurrency | Prerequisite |
|---|---|---|---|
| Wave 4.1 (Group D, parallel) | 040, 041 | 2 agents | none |
| Wave 4.2 (Group E with coordination) | 042, 043 | 2 agents | 010 ✅ + 040 ✅ |

> **Note (042 vs 043)**: Both touch `PaneEventTypes.ts`. Coordinate via PR rebase — file overlap is union-type extension, not conflicting semantics.

### Phase 5 — Substantive code changes (~31h)

| Wave | Tasks | Concurrency | Prerequisite |
|---|---|---|---|
| Wave 5.1 (Group F, parallel) | 050, 051, 053 | 3 agents | none |
| Wave 5.2 | 052, 054, 055 | 3 agents | 051 ✅ (for 052), 053 ✅ (for 054), none (for 055 — group G) |

> **Build verification between waves is MANDATORY** per project-pipeline Step 5: after Wave 5.1, run `dotnet build src/server/api/Sprk.Bff.Api/` (because A-4 + B-4 touch BFF) and `npm run build` for each touched Vite package. STOP if either fails.

### Phase 6 — Build hygiene cluster (~21h)

| Wave | Tasks | Concurrency | Prerequisite |
|---|---|---|---|
| Wave 6.1 (Groups H+I mixed, parallel) | 060, 061, 062, 063, 065, 066 | 6 agents (cap) | none |
| Wave 6.2 | 064 | 1 task | 063 ✅ |
| Wave 6.3 | 067 | 1 task | 063 ✅ + 064 ✅ |

### Phase 7 — R4 wrap-up (~2h)

| Wave | Tasks | Concurrency | Prerequisite |
|---|---|---|---|
| Wave 7.1 | 090 | 1 task | All 30 preceding tasks ✅ |

---

## Critical Path

The longest dependency chain through R4:

```
001 (E-1 R3 wrap-up, ~2h)
  ↓ (no formal dep, but operator usually waits for clean R3 closure)
010 (W-1 dashboard model, ~6h)
  ↓
015 (C-2 embedded contract, ~3h)
  ↓
042 (W-4 Assistant mount, ~8h)
  ↓
050+051+053 wave 1 → 052+054+055 wave 2 (Phase 5, ~31h)
  ↓
060+061+062+063+065+066 wave 1 → 064 → 067 (Phase 6, ~21h)
  ↓
090 (R4 wrap-up, ~2h)
```

Sequential critical path: ~73h. Parallelized (with full Wave concurrency): ~50h (~6-7 working days).

---

## High-Risk Items

Per `plan.original.md` §8 Risk Register (10 items, R-1..R-10). Top risks tagged in TASK-INDEX:

| Risk | Affected tasks | Mitigation |
|---|---|---|
| **R-1** A-5 verification reveals different bug | 030, 031 | Verify-first; re-scope after 030 completes |
| **R-2** A-4 25 MB blows bundle / BFF text-content limits | 050 | Bundle impact from text extraction (capped chars); confirm in NFR-08 check |
| **R-3** C-3 consolidation breaks LW or SpaarkeAi | 051 | Test BOTH consumers end-to-end after consolidation |
| **R-5** B-2 tsc fix surfaces hidden type errors | 061 | Budget may stretch ~3h → ~6h |
| **R-6** W-6 LW retirement breaks unanticipated consumer | 041 | Audit `corporateworkspace` references BEFORE retiring deploy |
| **R-7** W-4/W-5 mount-source wiring exceeds estimate | 042, 043 | Scope reduction: dispatch + one widget; broader coverage to R5 |
| **R-8** B-11 type-drift cascades | 067 | Cluster fixes; carry-over budget if needed |

---

## How to Execute Parallel Groups

1. **Check all prerequisites are complete** (✅ in Status column)
2. **Verify file overlap is OK** (parallel-group + parallel-safe metadata in each POML; auto-demotion already applied for `.claude/` paths)
3. **Invoke task-execute via the Skill tool with multiple parallel invocations in ONE message** (e.g., for Wave 1.1: one message with two `Skill` calls, one per task)
4. **Wait for ALL agents in the wave to complete** before next wave
5. **Run build verification** between waves if any `.cs` or `.ts/.tsx` files were modified
6. **Update TASK-INDEX.md** statuses (🔲 → ✅) — task-execute Step N-1 does this per task
7. **Sequential tasks** (touching `.claude/` or with deps) — execute via single Skill invocation in main session, one at a time

---

## Quick Reference

- **Project CLAUDE.md**: [`../CLAUDE.md`](../CLAUDE.md)
- **Spec**: [`../spec.md`](../spec.md) (FR-XX, NFR-XX, DR-XX, PR-XX definitions + Acceptance criteria)
- **Plan (templated)**: [`../plan.md`](../plan.md)
- **Plan (authoritative, operator-authored)**: [`../plan.original.md`](../plan.original.md) — full WBS, per-task effort, risk register
- **Backlog**: [`../backlog.md`](../backlog.md) — per-item rationale + IN/DEFER scoping decisions
- **Current state**: [`../current-task.md`](../current-task.md)

---

*Maintained by `task-execute` skill — each task's final step updates its row in this index.*
