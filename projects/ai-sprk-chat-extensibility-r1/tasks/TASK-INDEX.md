# Task Index — SprkChat Analysis Workspace Command Center

> **Last Updated**: 2026-03-25
> **Total Tasks**: 22
> **Status**: Ready for Execution

---

## Task Registry

### Phase 0: Scope Enforcement + Side Pane Lifecycle

| # | Task | Status | Est. | Group | Dependencies |
|---|------|--------|------|-------|-------------|
| 001 | Remove SidePaneManager injection from Corporate Workspace | 🔲 | 2h | A | none |
| 002 | Remove global ribbon button for SprkChat | 🔲 | 2h | A | none |
| 003 | Implement side pane close on navigation away | 🔲 | 3h | B | none |
| 004 | Implement auto-reopen with session persistence | 🔲 | 3h | B | none |

### Phase 1: Smart Chat Foundation

| # | Task | Status | Est. | Group | Dependencies |
|---|------|--------|------|-------|-------------|
| 010 | Create context enrichment types and hook | 🔲 | 4h | C | Phase 0 |
| 011 | BFF context signal processing | 🔲 | 4h | — | 010 |
| 012 | Build SlashCommandMenu component | 🔲 | 6h | C | Phase 0 |
| 013 | Implement system commands | 🔲 | 4h | C | Phase 0 |
| 014 | Integrate dynamic command registry | 🔲 | 4h | — | 012, 013 |
| 015 | Enable playbook switching from menu | 🔲 | 3h | — | 014 |
| 016 | Natural language routing enhancements | 🔲 | 3h | — | 011 |
| 017 | Phase 1 integration tests | 🔲 | 4h | — | 010-016 |

### Phase 2: Quick-Action Chips

| # | Task | Status | Est. | Group | Dependencies |
|---|------|--------|------|-------|-------------|
| 020 | Enhance QuickActionChips with playbook capabilities | 🔲 | 4h | D | 010, 014 |
| 021 | Analysis-type-aware chip set configuration | 🔲 | 3h | D | 010 |
| 022 | Phase 2 integration tests | 🔲 | 2h | — | 020, 021 |

### Phase 3: Compound Actions + Plan Preview

| # | Task | Status | Est. | Group | Dependencies |
|---|------|--------|------|-------|-------------|
| 030 | Enhance PlanPreviewCard with edit/cancel | 🔲 | 4h | E | 016 |
| 031 | Build CompoundActionProgress component | 🔲 | 4h | — | 030 |
| 032 | Implement email drafting tool and preview | 🔲 | 5h | E | 016 |
| 033 | Implement email send flow | 🔲 | 4h | — | 032 |
| 034 | Implement write-back with confirmation | 🔲 | 5h | E | 016 |
| 035 | BFF compound action orchestration | 🔲 | 5h | — | 030, 031 |
| 036 | Phase 3 integration tests | 🔲 | 4h | — | 030-035 |

### Phase 4: Prompt Templates + Playbook Library

| # | Task | Status | Est. | Group | Dependencies |
|---|------|--------|------|-------|-------------|
| 040 | Prompt template data model and Dataverse config | 🔲 | 4h | F | Phase 3 |
| 041 | Template parameter fill-in UI | 🔲 | 4h | — | 040 |
| 042 | Build playbook library browser component | 🔲 | 5h | F | Phase 3 |
| 043 | Phase 4 integration tests | 🔲 | 3h | — | 040-042 |

### Wrap-Up

| # | Task | Status | Est. | Group | Dependencies |
|---|------|--------|------|-------|-------------|
| 090 | Project wrap-up and documentation | 🔲 | 3h | — | All |

---

## Parallel Execution Groups

Tasks within the same group can run concurrently via Claude Code task agents.

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| **A** | 001, 002 | none | Independent removal tasks — different files |
| **B** | 003, 004 | none | Independent lifecycle features — different files |
| **C** | 010, 012, 013 | Phase 0 complete | Context types, SlashCommandMenu, system commands — independent components |
| **D** | 020, 021 | 010, 014 complete | Chip enhancements — different files |
| **E** | 030, 032, 034 | 016 complete | PlanPreview, email tool, write-back — independent components |
| **F** | 040, 042 | Phase 3 complete | Template data model, playbook browser — independent concerns |

### Recommended Execution Order

```
┌─── Group A: [001, 002] ──────────────────────┐  (parallel, 2 agents)
├─── Group B: [003, 004] ──────────────────────┤  (parallel, 2 agents — can overlap with A)
│                                                │
├─── Group C: [010, 012, 013] ─────────────────┤  (parallel, 3 agents — after Phase 0)
├─── Serial:  011 (after 010) ─────────────────┤
├─── Serial:  014 (after 012, 013) ────────────┤
├─── Serial:  015 (after 014) ─────────────────┤
├─── Serial:  016 (after 011) ─────────────────┤
├─── Serial:  017 (after all Phase 1) ─────────┤
│                                                │
├─── Group D: [020, 021] ─────────────────────┤  (parallel, 2 agents — after 010, 014)
├─── Serial:  022 (after 020, 021) ────────────┤
│                                                │
├─── Group E: [030, 032, 034] ─────────────────┤  (parallel, 3 agents — after 016)
├─── Serial:  031 (after 030) ─────────────────┤
├─── Serial:  033 (after 032) ─────────────────┤
├─── Serial:  035 (after 030, 031) ────────────┤
├─── Serial:  036 (after all Phase 3) ─────────┤
│                                                │
├─── Group F: [040, 042] ─────────────────────┤  (parallel, 2 agents — after Phase 3)
├─── Serial:  041 (after 040) ─────────────────┤
├─── Serial:  043 (after 040-042) ─────────────┤
│                                                │
└─── Serial:  090 (after all tasks) ───────────┘
```

---

## Critical Path

The longest dependency chain determines minimum execution time:

```
Phase 0 (any) → 010 → 011 → 016 → 030 → 031 → 035 → 036 → 040 → 041 → 043 → 090
```

**Critical path estimated effort**: ~47 hours (serial minimum)
**With parallel execution**: ~30-35 hours (groups A/B, C, D, E, F run concurrently)

---

## High-Risk Items

| Task | Risk | Impact | Mitigation |
|------|------|--------|------------|
| 012 | SlashCommandMenu performance (100ms target) | NFR violation | Virtualized list, lazy rendering |
| 032 | Email recipient resolution | Functional gap | Use matter party/role relationships |
| 035 | Compound action partial failure | Data consistency | Preserve partial results + error UI |
| 003 | Side pane race conditions | UX bugs | Dual mechanism (useEffect + poll) |

---

## Status Legend

| Symbol | Meaning |
|--------|---------|
| 🔲 | Not started |
| 🔄 | In progress |
| ✅ | Complete |
| ⛔ | Blocked |
| ⏭️ | Skipped |

---

*For Claude Code: Use parallel groups to spawn concurrent task agents. Each agent invokes task-execute independently.*
