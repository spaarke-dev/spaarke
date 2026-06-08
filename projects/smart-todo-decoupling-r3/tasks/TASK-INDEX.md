# Task Index — Smart To Do — Decoupling from Events (R3)

> **Project**: smart-todo-decoupling-r3
> **Generated**: 2026-06-07 via `/project-pipeline`
> **Spec**: [`spec.md`](../spec.md) · **Plan**: [`plan.md`](../plan.md) · **Project Context**: [`CLAUDE.md`](../CLAUDE.md)

---

## Summary

| Metric | Value |
|---|---|
| **Total tasks** | 42 (38 from initial decomposition + 3 audit-surfaced: 006, 007, 008 + 1 task-002 follow-up: 009) |
| **Phases** | 9 (Phase 1 Schema · 2 Shared-Lib · 3 Code Page · 4 Wizard · 5 Subgrids · 6 Graph Foundation · 7 Sync Engine · 8 Outlook · 9 Cleanup) + final wrap-up |
| **FULL-rigor tasks** | 22 |
| **STANDARD-rigor tasks** | 11 |
| **MINIMAL-rigor tasks** | 4 |
| **Parallel-safe tasks** | 19 |
| **Main-session-only tasks** | 2 (083, 084 — touch root `CLAUDE.md`) |

---

## Task Table

| ID | Title | Phase | Rigor | Status | Dependencies | Parallel Group | Parallel Safe |
|----|-------|-------|-------|--------|--------------|----------------|---------------|
| 001 | Audit `sprk_eventtodo` and `sprk_event.sprk_todo*` references | 1 | STANDARD | ✅ | none | P1-W1 | ✅ |
| 002 | Create `sprk_todo` custom entity (full attribute set) | 1 | FULL | ✅ | 001 | P1-W2 | — |
| 003 | Register `sprk_todo` in `sprk_recordtype_ref` | 1 | STANDARD | ✅ | 002 | P1-W3 | ✅ |
| 004 | Remove four to-do fields from `sprk_event` (FINAL schema cut) | 1 | FULL | 🔲 | 001, 006, 007, 008, **011, 020, 023, 031** | P1-W-FINAL | — |
| 005 | Delete `sprk_eventtodo` entity (FINAL schema cut) | 1 | FULL | 🔲 | 001, 004, 007, 008, **011, 020, 023, 031** | P1-W-FINAL | — |
| 006 | Refactor `TodoGenerationService` to create `sprk_todo` (audit-surfaced) | 1 | FULL | ✅ | 002 | P1-W2.5 | ✅ |
| 007 | Refactor `ExternalAccess` BFF surface to `sprk_todo` (audit-surfaced) | 1 | FULL | ✅ | 002 | P1-W2.5 | ✅ |
| 008 | Migrate `external-spa` to `sprk_todo` (audit-surfaced) | 1 | FULL | ✅ | 002, 007 | P1-W3 | — |
| 009 | Customize `sprk_todo.statuscode` (Open/In Progress/Completed/Dismissed) — task-002 follow-up | 1 | STANDARD | ✅ | 002 | P1-W2.5 | ✅ |
| 010 | Hoist Kanban primitives to `@spaarke/ui-components/Kanban/` | 2 | FULL | ✅ | 001 | P2-W1 | — |
| 011 | Simplify `TodoDetail` to single-entity load/save | 2 | FULL | 🔲 | 002, 010 | P2-W2 | — |
| 012 | Update `@spaarke/ui-components` barrel + version bump | 2 | STANDARD | 🔲 | 010, 011 | P2-W3 | ✅ |
| 015 | Add `Tasks.ReadWrite` delegated scope to AAD app | 6 | STANDARD | 🔲 | none | P6-W1 | ✅ |
| 016 | Wire `Tasks.ReadWrite` through `GraphClientFactory` | 6 | FULL | 🔲 | 015 | P6-W2 | — |
| 017 | Define `MicrosoftToDoSync` user-pref schema | 6 | STANDARD | ✅ | none | P6-W1 | ✅ |
| 018 | Null-Object scaffolding for Graph sync (feature gate) | 6 | FULL | ✅ | none | P6-W2 | ✅ |
| 020 | Repoint SmartTodo kanban queries to `sprk_todo` | 3 | FULL | 🔲 | 002, 010, 011, 012 | P3-W1 | — |
| 021 | Add "My Tasks" filter to `KanbanHeader` | 3 | FULL | 🔲 | 020 | P3-W2 | ✅ |
| 022 | Integrate `AssociateToStep` in SmartTodo `TodoDetail` | 3 | FULL | 🔲 | 011, 020, 030 | P3-W2 | ✅ |
| 023 | Update `FeedTodoSyncContext` payload to todo-id | 3 | FULL | 🔲 | 020 | P3-W2 | — |
| 024 | Deploy SmartTodo Code Page | 3 | STANDARD | 🔲 | 020, 021, 022, 023 | P3-W3 | ✅ |
| 030 | Extend `AssociateToStep` to all 11 entity targets | 4 | FULL | 🔲 | 002, 010, 012 | P4-W1 | — |
| 031 | Rewrite `CreateTodo` wizard to target `sprk_todo` | 4 | FULL | 🔲 | 002, 030 | P4-W2 | — |
| 032 | Implement launch-context pre-fill | 4 | FULL | 🔲 | 031 | P4-W3 | ✅ |
| 040 | "To Dos" subgrid on 11 parent forms (consolidated) | 5 | STANDARD | 🔲 | 002, 030, 031, 032 | P5-W1 | ✅ |
| 060 | `DeepLinkBuilder` service | 7 | FULL | 🔲 | 002 | P7-W1 | ✅ |
| 061 | Outbound sync pipeline (plugin + ServiceBus + handler) | 7 | FULL | 🔲 | 002, 009, 016, 018, 060 | P7-W2 | — |
| 062 | Loop prevention (synchash + skip-flag + LWW) | 7 | FULL | 🔲 | 061 | P7-W3 | — |
| 063 | `SpaarkeListProvisioner` (real impl) | 7 | FULL | 🔲 | 015, 016, 017, 018 | P7-W2 | ✅ |
| 064 | Extend `GraphSubscriptionManager` + nightly renewal job | 7 | FULL | 🔲 | 063 | P7-W3 | — |
| 065 | `/api/graph/webhooks/todo` endpoint | 7 | FULL | 🔲 | 062, 064 | P7-W4 | — |
| 066 | Initial backfill on opt-in (`$batch`, backoff, resumable) | 7 | FULL | 🔲 | 061, 063, 064 | P7-W5 | ✅ |
| 070 | Outlook ribbon "Create To Do" action | 8 | FULL | 🔲 | 030, 031, 032 | P8-W1 | ✅ |
| 071 | Outlook indicator banner for linked todos | 8 | FULL | 🔲 | 002 | P8-W1 | ✅ |
| 072 | Deploy Outlook add-in | 8 | STANDARD | 🔲 | 070, 071 | P8-W2 | ✅ |
| 080 | Audit `TodoDetailSidePane` consumers (A-2) | 9 | STANDARD | 🔲 | 022 | P9-W1 | ✅ |
| 081 | Retire or refactor `TodoDetailSidePane` | 9 | STANDARD | 🔲 | 080 | P9-W2 | ✅ |
| 082 | Architecture doc: supersede + new `spaarke-todo-architecture.md` | 9 | MINIMAL | 🔲 | 005 | P9-W2 | ✅ |
| 083 | Update root `CLAUDE.md` §16 pointer table | 9 | MINIMAL | 🔲 | 082 | P9-W3 | — (main-session) |
| 084 | Fix `CLAUDE.md` §10 stale `ADR-030` link → `ADR-032` | 9 | MINIMAL | ✅ | none | P9-W3 | — (main-session) |
| 085 | Final repo-wide legacy-reference grep sweep | 9 | STANDARD | 🔲 | 005, 011, 020, 031, 081, 083 | P9-W4 | ✅ |
| 090 | **Project wrap-up** (code-review + adr-check + repo-cleanup + lessons-learned) | 9-Final | FULL | 🔲 | 085, 081, 083, 084, 024, 072 | P9-W5 | — |

---

## Parallel Execution Plan

Phases run in waves. Within each wave, tasks marked Parallel Safe ✅ can be dispatched concurrently (up to **6 agents per wave** per project-pipeline). Tasks marked `—` are sequential within their phase.

### Phase 1 — Dataverse Schema (sequential, blocks all)

**Updated 2026-06-07 (audit escalation)**: Tasks 006/007/008 inserted between 002 and 004/005 per user decision (refactor TodoGenerationService + ExternalAccess BFF + migrate external-spa rather than break those surfaces).

| Wave | Tasks | Prereq | Notes |
|------|-------|--------|-------|
| P1-W1 | 001 | none | Reference audit gates the schema cut |
| P1-W2 | 002 | 001 | Create `sprk_todo` entity (Dataverse env: spaarkedev1) |
| P1-W2.5 | 003, 006, 007 | 002 | Parallel-safe — 003 inserts data row; 006 refactors TodoGenerationService; 007 refactors ExternalAccess BFF |
| P1-W3 | 008 | 002, 007 | external-spa migration (consumes 007's new contract) |
| P1-W4 | 004, 005 | 001, 006, 007, 008 (004); 001, 004, 007, 008 (005) | Final schema cuts — only safe after all consumer refactors complete |

### Phase 2 + Phase 6 — Parallel-running phases (per spec A-7)

| Wave | Tasks | Prereq | Notes |
|------|-------|--------|-------|
| P2-W1 | 010 | 001 | Kanban hoist |
| P2-W2 | 011 | 002, 010 | TodoDetail simplify |
| P2-W3 | 012 | 010, 011 | Barrel + version bump |
| P6-W1 | 015, 017 | none | AAD scope add + user-pref schema (parallel-safe — independent surfaces) |
| P6-W2 | 016, 018 | 015 (016); none (018) | OBO scope wiring + Null-Object scaffolding |

### Phase 3 — SmartTodo Code Page Repoint

| Wave | Tasks | Prereq | Notes |
|------|-------|--------|-------|
| P3-W1 | 020 | 002, 010, 011, 012 | Query repoint blocks the rest of Phase 3 |
| P3-W2 | 021, 022, 023 | 020 (021, 023); 011, 020, 030 (022) | 021 + 022 parallel-safe; 023 modifies cross-block context (serialize) |
| P3-W3 | 024 | 020–023 | Deploy |

### Phase 4 — CreateTodo Wizard + AssociateToStep

| Wave | Tasks | Prereq | Notes |
|------|-------|--------|-------|
| P4-W1 | 030 | 002, 010, 012 | Extends `AssociateToStep` to 11 targets (foundational) |
| P4-W2 | 031 | 002, 030 | Wizard rewrite |
| P4-W3 | 032 | 031 | Launch-context pre-fill |

### Phase 5 — Parent-Form Subgrids (consolidated task with internal fan-out)

| Wave | Tasks | Prereq | Notes |
|------|-------|--------|-------|
| P5-W1 | 040 | 002, 030, 031, 032 | Single task with 11 sub-deliverables; sub-agent fan-out at execution time in two waves of 6+5 (file-isolated, parallel-safe) |

### Phase 7 — Graph Sync Engine

| Wave | Tasks | Prereq | Notes |
|------|-------|--------|-------|
| P7-W1 | 060 | 002 | DeepLinkBuilder (standalone) |
| P7-W2 | 061, 063 | 002, 016, 018, 060 (061); 015, 016, 017, 018 (063) | Outbound pipeline + Provisioner; parallel-safe (different services) |
| P7-W3 | 062, 064 | 061 (062); 063 (064) | Loop prevention + Subscription manager; SERIALIZE — both modify shared handler / subscription paths |
| P7-W4 | 065 | 062, 064 | Webhook endpoint (depends on both prior) |
| P7-W5 | 066 | 061, 063, 064 | Initial backfill |

### Phase 8 — Outlook Add-in

| Wave | Tasks | Prereq | Notes |
|------|-------|--------|-------|
| P8-W1 | 070, 071 | 030, 031, 032 (070); 002 (071) | Ribbon + Indicator parallel-safe (different parts of add-in) |
| P8-W2 | 072 | 070, 071 | Deploy |

### Phase 9 — Cleanup & Documentation

| Wave | Tasks | Prereq | Notes |
|------|-------|--------|-------|
| P9-W1 | 080 | 022 | Audit `TodoDetailSidePane` |
| P9-W2 | 081, 082 | 080 (081); 005 (082) | Retire/refactor + arch doc; parallel-safe |
| P9-W3 | 083, 084 | 082 (083); none (084) | **MAIN-SESSION-ONLY** — both touch root `CLAUDE.md`; sequential to avoid edit conflicts |
| P9-W4 | 085 | 005, 011, 020, 031, 081, 083 | Final legacy-reference grep sweep |
| P9-W5 | 090 | 085, 081, 083, 084, 024, 072 | **PROJECT WRAP-UP** — final gate |

---

## Critical Path

```
001 → 002 → 010 → 011 → 020 → 022 → 080 → 081 → 085 → 090
                              ↗
              030 → 031 → 032 → 070 ↘
                                     072 → 090
                              040 ↗
```

Longest dependency chain: 001 → 002 → 010 → 011 → 020 → 022 → 080 → 081 → 085 → 090 (**10 tasks**)
Parallel critical-path branch (Graph sync): 002 → 015 → 016 → 018 → 061 → 062 → 065 → 066 → 090

---

## Execution Strategy

**Recommended sequence** (assuming a single operator running in waves, not full autonomous parallel execution):

1. **Wave 1**: 001 (audit)
2. **Wave 2**: 002 (create entity)
3. **Wave 3**: 003, 004 in parallel (insert recordtype_ref + remove `sprk_event` fields)
4. **Wave 4**: 005 (delete `sprk_eventtodo`) — Phase 1 complete
5. **Wave 5**: Phase 2 (010 → 011 → 012) and Phase 6 (015+017 → 016+018) interleaved per A-7
6. **Wave 6**: Phase 3 (020 → 021+022+023 → 024) plus Phase 4 (030 → 031 → 032) interleaved
7. **Wave 7**: Phase 5 (040) — fan out to 11 sub-agents in two waves of 6+5
8. **Wave 8**: Phase 7 (060 → 061+063 → 062+064 → 065 → 066) — longest single-phase chain
9. **Wave 9**: Phase 8 (070+071 → 072)
10. **Wave 10**: Phase 9 (080 → 081+082 → 083+084 main-session → 085 → 090 wrap-up)

**Build verification between waves** — per project-pipeline orchestration rules:
- After any wave that modifies `.cs` files: `dotnet build src/server/api/Sprk.Bff.Api/`
- After any wave that modifies `.ts`/`.tsx` files: `npm run build` in affected package
- BFF publish-size check: every BFF-touching task per CLAUDE.md §10

**Permission boundary** — tasks 083 and 084 touch root `CLAUDE.md`; they MUST run in the main session (sub-agents cannot write to `.claude/` or other repo-level config). The Sub-Agent Write Boundary error "Edit denied on `CLAUDE.md`" would indicate this rule was violated.

---

## How to Execute

To start work, run:

```
/task-execute projects/smart-todo-decoupling-r3/tasks/001-audit-eventtodo-references.poml
```

To execute the next pending task (auto-detected from this index), say:

```
continue
```

or

```
next task
```

The `task-execute` skill will:
1. Read this index, find the next 🔲 with all dependencies satisfied
2. Load the task's POML + knowledge files + applicable ADRs
3. Execute with the rigor protocol from the task's `<rigor-hint>`
4. Update this index's status emoji on completion

For parallel execution of an entire wave, the orchestrator pattern is: ONE message with multiple Skill tool invocations (one per parallel-safe task in the wave). See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md) §8.0 for the multi-file work decomposition protocol.

---

*Generated by `/project-pipeline` on 2026-06-07 from spec.md + plan.md. Tasks 083 and 084 are main-session-only due to root `CLAUDE.md` write boundary.*
