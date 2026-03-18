# Task Index — SprkChat Platform Enhancement R2

> **Total Tasks**: 50
> **Status**: 50/50 complete
> **Last Updated**: 2026-03-17

## Status Legend
- 🔲 Pending
- 🔄 In Progress
- ✅ Complete
- ⏸️ Blocked

---

## Phase 1: Foundation & Infrastructure

| # | Task | Status | Parallel | Dependencies | Rigor |
|---|------|--------|----------|-------------|-------|
| 001 | Dataverse schema extensions | ✅ | — | — | STANDARD |
| 002 | JPS schema extensions | ✅ | — | — | FULL |
| 003 | Playbook embeddings AI Search index | ✅ | — | — | FULL |
| 004 | Shared renderMarkdown() utility | ✅ | — | — | FULL |
| 005 | Seed playbook trigger metadata | ✅ | — | 001 | STANDARD |
| 006 | Deploy Phase 1 | ✅ | — | 001-005 | STANDARD |

> **Phase 1 parallelism**: Tasks 001, 002, 003, 004 can run concurrently (no shared files). Task 005 waits for 001.

---

## Phase 2: BFF API Services (PARALLEL with Phase 3 after 006)

### Group A — SSE Streaming + Document Context

| # | Task | Status | Parallel | Dependencies | Rigor |
|---|------|--------|----------|-------------|-------|
| 010 | SSE streaming enhancement | ✅ | A | 006 | FULL |
| 011 | Document context injection service | ✅ | A | 006 | FULL |
| 012 | Multi-document context aggregation | ✅ | A | 011 | FULL |
| 013 | Document upload endpoint | ✅ | A | 011 | FULL |
| 014 | Document SPE persistence endpoint | ✅ | A | 013 | FULL |

### Group B — Playbook Dispatcher + Web Search

| # | Task | Status | Parallel | Dependencies | Rigor |
|---|------|--------|----------|-------------|-------|
| 015 | PlaybookDispatcher service | ✅ | B | 003, 006 | FULL |
| 016 | Playbook embedding pipeline | ✅ | B | 003, 006 | FULL |
| 017 | WebSearchTools real implementation | ✅ | B | 006 | FULL |
| 018 | Playbook output handler | ✅ | B | 015 | FULL |
| 019 | Dynamic command resolution service | ✅ | B | 001, 006 | FULL |

### Sequential — Context Resolver + Integration

| # | Task | Status | Parallel | Dependencies | Rigor |
|---|------|--------|----------|-------------|-------|
| 020 | AnalysisChatContextResolver real impl | ✅ | — | 001, 019 | FULL |
| 021 | Scope capabilities service | ✅ | — | 001, 019, 020 | FULL |
| 022 | Open in Word endpoint | ✅ | — | 006 | FULL |
| 023 | SSE write-back enhancement | ✅ | — | 010 | FULL |
| 024 | Rate limiting for new endpoints | ✅ | 2026-03-17 | 010-022 | FULL |
| 025 | Deploy Phase 2 (BFF API) | ✅ | — | 010-024 | STANDARD |

---

## Phase 3: Frontend UI Components (PARALLEL with Phase 2 after 006)

### Group C — Markdown + SSE + Citations

| # | Task | Status | Parallel | Dependencies | Rigor |
|---|------|--------|----------|-------------|-------|
| 030 | Markdown rendering integration | ✅ | C | 004 | FULL |
| 031 | SSE streaming UI | ✅ | C | 004, 006 | FULL |
| 032 | Citation cards enhancement | ✅ | C | 030 | FULL |
| 033 | Plan preview streaming | ✅ | C | 031 | FULL |
| 034 | Dark mode audit | ✅ | C | 030-033 | STANDARD |

### Group D — Slash Commands + Scope Capabilities

| # | Task | Status | Parallel | Dependencies | Rigor |
|---|------|--------|----------|-------------|-------|
| 035 | Dynamic slash command resolution | ✅ | D | 006 | FULL |
| 036 | SlashCommandMenu enhancement | ✅ | D | 035 | FULL |
| 037 | Scope capability commands UI | ✅ | D | 035 | FULL |
| 038 | Command catalog client cache | ✅ | D | 035 | STANDARD |
| 039 | Action confirmation UX (HITL) | ✅ | D | 036 | FULL |

### Group E — Upload + Word + Multi-doc

| # | Task | Status | Parallel | Dependencies | Rigor |
|---|------|--------|----------|-------------|-------|
| 040 | Document upload zone | ✅ | E | 006 | FULL |
| 041 | Upload processing feedback | ✅ | E | 040 | FULL |
| 042 | Save to matter files UX | ✅ | E | 040 | FULL |
| 043 | Multi-document selector | ✅ | E | 006 | FULL |
| 044 | Open in Word button | ✅ | E | 006 | FULL |
| 045 | Deploy Phase 3 (frontend) | ✅ | — | 030-044 | STANDARD |

---

## Phase 4: Integration & Write-back (Requires Phase 2 + 3)

| # | Task | Status | Parallel | Dependencies | Rigor |
|---|------|--------|----------|-------------|-------|
| 050 | SSE streaming integration | ✅ | — | 010, 031 | FULL |
| 051 | Write-back via BroadcastChannel | ✅ | — | 023, 031 | FULL |
| 052 | Playbook dispatch → dialog handoff | ✅ | — | 015, 018, 039 | FULL |
| 053 | Dynamic command integration | ✅ | — | 019-021, 035-037 | FULL |
| 054 | Document context integration | ✅ | — | 011, 012 | FULL |
| 055 | Web search integration | ✅ | — | 017, 032 | FULL |
| 056 | Upload integration | ✅ | — | 013, 014, 040-042 | FULL |
| 057 | Open in Word integration | ✅ | — | 022, 044 | FULL |
| 058 | Deploy Phase 4 (full stack) | ✅ | — | 050-057 | STANDARD |

---

## Phase 5: Testing & Hardening

| # | Task | Status | Parallel | Dependencies | Rigor |
|---|------|--------|----------|-------------|-------|
| 060 | SSE streaming E2E tests | ✅ | 2026-03-17 | 050 | STANDARD |
| 061 | Playbook Dispatcher E2E tests | ✅ | 2026-03-17 | 052 | STANDARD |
| 062 | Document context E2E tests | ✅ | 2026-03-17 | 054 | STANDARD |
| 063 | Upload E2E tests | ✅ | 2026-03-17 | 056 | STANDARD |
| 064 | Context resolver E2E tests | ✅ | 2026-03-17 | 053 | STANDARD |
| 065 | Rate limiting verification | ✅ | 2026-03-17 | 024 | STANDARD |
| 066 | Security audit | ✅ | — | 058 | FULL |
| 067 | Deploy Phase 5 (final) | ✅ | 2026-03-17 | 060-066 | STANDARD |

---

## Phase 6: Wrap-up

| # | Task | Status | Parallel | Dependencies | Rigor |
|---|------|--------|----------|-------------|-------|
| 090 | Project wrap-up | ✅ | — | 067 | MINIMAL |

---

## Parallel Execution Groups

| Group | Phase | Tasks | Prerequisite | Concurrent Agents | File Ownership |
|-------|-------|-------|-------------|-------------------|----------------|
| **A** | 2 | 010, 011, 012, 013, 014 | 006 complete | Agent 1 | `Services/Ai/Chat/` (streaming + document) |
| **B** | 2 | 015, 016, 017, 018, 019 | 006 complete | Agent 2 | `PlaybookDispatcher.cs`, `WebSearchTools.cs`, `PlaybookDispatchEndpoints.cs` |
| **C** | 3 | 030, 031, 032, 033, 034 | 004 complete | Agent 3 | `SprkChatMessage*.tsx`, `renderMarkdown.ts` |
| **D** | 3 | 035, 036, 037, 038, 039 | 006 complete | Agent 4 | `SprkChatInput.tsx`, `SlashCommandMenu/` |
| **E** | 3 | 040, 041, 042, 043, 044 | 006 complete | Agent 5 | `SprkChatUploadZone.tsx`, `SprkChatContextSelector.tsx` |

**Maximum concurrent agents**: 5 (Groups A+B+C+D+E after Phase 1 completes)

### Recommended Execution Timeline

```
Phase 1:  [001+002+003+004] → [005] → [006]
                                         │
          ┌──────────────────────────────┤
          ▼                              ▼
Phase 2:  Group A → [020→021] → [024]   Phase 3:  Group C → [034]
          Group B → [022,023] → [025]              Group D → [039]
                                                   Group E → [045]
          ├─────────────┬────────────────┤
                        ▼
Phase 4:  [050-057] → [058]
                        │
                        ▼
Phase 5:  [060-066] → [067]
                        │
                        ▼
Phase 6:  [090]
```

---

## Critical Path

```
001 → 005 → 006 → 010 → 023 → 051 → 058 → 066 → 067 → 090
                   ↓
                   015 → 018 → 052
                   ↓
                   019 → 020 → 021 → 053
```

**Longest path**: ~25 sequential tasks (with parallelism reducing wall-clock time significantly)

---

## High-Risk Items

| Task | Risk | Mitigation |
|------|------|-----------|
| 015 | PlaybookDispatcher semantic matching accuracy | Two-stage matching, fallback to keyword |
| 020 | ContextResolver stub replacement | Thorough Dataverse query testing |
| 011 | Conversation-aware chunking latency | Async re-selection, cache recent chunks |
| 024 | DI registration budget (ADR-010) | Factory instantiation pattern |

---

*Generated by project-pipeline. Updated during task execution.*
