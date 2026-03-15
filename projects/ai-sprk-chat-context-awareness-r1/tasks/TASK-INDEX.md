# Task Index — SprkChat Context Awareness R1

> **Project**: ai-sprk-chat-context-awareness-r1
> **Total Tasks**: 22
> **Created**: 2026-03-15

---

## Task Registry

| # | Title | Phase | Stream | Rigor | Group | Deps | Status |
|---|-------|-------|--------|-------|-------|------|--------|
| 001 | Add PageType to ChatHostContext record | 1A | backend | FULL | A | — | ✅ |
| 002 | Make ChatSession.PlaybookId nullable | 1A | backend | FULL | A | — | ✅ |
| 003 | Create sprk_aichatcontextmap entity | 1B | dataverse | FULL | A | — | ✅ |
| 004 | Create seed data JSON for PAC CLI | 1B | dataverse | STD | A | 003 | 🔲 |
| 010 | Create ChatContextMappingService | 1A | backend | FULL | B | 001,003 | ✅ |
| 011 | Create GET /context-mappings endpoint | 1A | backend | FULL | B | 001,010 | ✅ |
| 012 | Add pageType to IHostContext + detectPageType() | 1C | client | FULL | B | — | ✅ |
| 013 | Handle null PlaybookId in SprkChatAgent | 1A | backend | FULL | C | 002 | ✅ |
| 014 | Unit tests for ChatContextMappingService | 1A | backend | STD | C | 010 | ✅ |
| 015 | Replace DEFAULT_PLAYBOOK_MAP with API call | 1C | client | FULL | C | 011,012 | ✅ |
| 016 | Implement no-mapping fallback UI | 1C | client | FULL | C | 012 | ✅ |
| 017 | Phase 1 integration wiring + smoke test | 1-int | integration | STD | D | all P1 | ✅ |
| 018 | Deploy Phase 1 to dev | 1-dpl | deploy | STD | D | 017 | 🔲 |
| 020 | Wire SprkChatContextSelector to availablePlaybooks | 2 | client | FULL | E | 017 | ✅ |
| 021 | Implement playbook switching | 2 | client | FULL | E | 017 | ✅ |
| 022 | Client sessionStorage caching | 2 | client | STD | E | 015 | ✅ |
| 023 | Phase 2 tests | 2 | client | STD | H | 020-022 | 🔲 |
| 030 | Enrichment unit tests (tests-first) | 3 | backend | STD | F | 017 | ✅ |
| 031 | Implement system prompt enrichment | 3 | backend | FULL | H | 030 | ✅ |
| 032 | Audit telemetry for EntityName exclusion | 3 | backend | STD | F | 017 | ✅ |
| 033 | Enrichment integration test | 3 | backend | STD | I | 031 | 🔲 |
| 040 | Create admin form for sprk_aichatcontextmap | 4 | dataverse | STD | G | 003 | 🔲 |
| 041 | Cache eviction endpoint | 4 | backend | FULL | G | 010 | ✅ |
| 042 | Wire "Refresh Mappings" button | 4 | dataverse | FULL | H | 040,041 | 🔲 |
| 043 | Admin workflow tests | 4 | dataverse | STD | I | 042 | 🔲 |
| 050 | Full end-to-end integration test | 5 | integration | FULL | J | 023,033,043 | 🔲 |
| 051 | Performance validation | 5 | integration | STD | J | 050 | 🔲 |
| 052 | Deploy all phases to dev | 5 | deploy | STD | J | 050 | 🔲 |
| 090 | Project wrap-up | 5 | wrap-up | MIN | J | 052 | 🔲 |

---

## Parallel Execution Groups

These groups define tasks that can run **concurrently via separate Claude Code task agents**.

| Group | Tasks | Prerequisites | Max Agents | Notes |
|-------|-------|---------------|------------|-------|
| **A** | 001, 002, 003, 004 | None | 4 | BFF models + DV entity (no file overlap) |
| **B** | 010, 011, 012 | Group A | 3 | Service + endpoint + client detection |
| **C** | 013, 014, 015, 016 | Group B | 4 | Null handling + tests + client wiring |
| **D** | 017, 018 | Group C | 1 | Sequential: integration → deploy |
| **E** | 020, 021, 022 | Phase 1 done | 3 | Phase 2: selector + switching + cache |
| **F** | 030, 032 | Phase 1 done | 2 | Phase 3 tests + telemetry audit |
| **G** | 040, 041 | Phase 1 done | 2 | Phase 4 form + cache endpoint |
| **H** | 023, 031, 042 | E, F, G | 3 | Phase 2 tests + enrichment + admin wiring |
| **I** | 033, 043 | H | 2 | Integration tests (Phase 3 + Phase 4) |
| **J** | 050→051→052→090 | All | 1 | Sequential: final integration + deploy + wrap-up |

### Cross-Phase Parallelism

After Phase 1 (Group D) completes, Groups **E, F, G** can ALL run simultaneously:

```
Group D done ──→ Group E (Phase 2: 3 agents)
              ├─→ Group F (Phase 3: 2 agents)  ← 7 agents in parallel!
              └─→ Group G (Phase 4: 2 agents)
```

This means up to **7 concurrent task agents** can work after Phase 1.

---

## Dependency Graph

```
001 ─┐
002 ─┼─→ 010 ─→ 011 ─┐
003 ─┘                 ├─→ 015 ─┐
004 (after 003)        │        │
012 ───────────────────┘        │
013 (after 002) ────────────────┼─→ 017 ─→ 018
014 (after 010) ────────────────┤
016 (after 012) ────────────────┘
                                     │
                    ┌────────────────┘
                    │
                    ├─→ 020 ─┐
                    ├─→ 021 ─┼─→ 023 ─────────┐
                    ├─→ 022 ─┘                 │
                    │                           │
                    ├─→ 030 ─→ 031 ─→ 033 ─────┼─→ 050 ─→ 051 ─→ 052 ─→ 090
                    ├─→ 032                     │
                    │                           │
                    ├─→ 040 ─┐                 │
                    └─→ 041 ─┼─→ 042 ─→ 043 ──┘
                             │
```

---

## Rigor Distribution

| Level | Count | Tasks |
|-------|-------|-------|
| FULL | 13 | 001, 002, 003, 010, 011, 012, 013, 015, 016, 020, 021, 031, 041, 042, 050 |
| STANDARD | 12 | 004, 014, 017, 018, 022, 023, 030, 032, 033, 040, 043, 051, 052 |
| MINIMAL | 1 | 090 |

---

## Critical Path

```
001 → 010 → 011 → 015 → 017 → 018 → 030 → 031 → 033 → 050 → 051 → 052 → 090
```

Estimated critical path duration: ~28h (with sequential execution)
With parallel execution: ~16h (Groups A-D overlap, E/F/G overlap)

---

*Generated by Claude Code project-pipeline*
