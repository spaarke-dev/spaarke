# Task Index — Spaarke Multi-Container Multi-Index Routing

> **Last Updated**: 2026-06-07
> **Total Tasks**: 43 (1 prerequisite + 41 implementation + 1 wrap-up)
> **Status Legend**: 🔲 not-started · 🔄 in-progress · ✅ completed · ⏸️ blocked · 🚫 deferred

---

## Tasks

| ID | Title | Phase | Status | Dependencies | Parallel Group | Rigor |
|----|-------|-------|--------|--------------|----------------|-------|
| 001 | Operator BU value setup + MCP verification | A.5 — Prerequisite | ✅ | none | — | STANDARD |
| 010 | Extend `IKnowledgeDeploymentService` interface + impl with allow-list | B — BFF resolver | ✅ | none | — | FULL |
| 011 | Add `SearchIndexName` to request DTOs (`SemanticSearchRequest`, `RagSearchRequest`, `RecordSearchRequest`) | B | ✅ | 010 | Group B1 | FULL |
| 012 | Add `AiSearch.AllowedIndexes` to appsettings + startup INFO log | B | ✅ | 010 | Group B1 | STANDARD |
| 013 | Thread `SearchIndexName` through `SemanticSearchService.cs` | B | ✅ | 010, 011 | Group B2 | FULL |
| 014 | Thread `SearchIndexName` through `RagService.cs` | B | ✅ | 010, 011 | Group B2 | FULL |
| 015 | Thread `SearchIndexName` through `RecordSearchService.cs` | B | ✅ | 010, 011 | Group B2 | FULL |
| 016 | Update `SemanticSearchEndpoints.cs` to pass DTO field to resolver | B | ✅ | 013, 014, 015 | — | FULL |
| 017 | BFF unit + integration tests for FR-BFF-01..07 | B | ✅ | 016 | — | STANDARD |
| 018 | BFF publish-size check + deploy via `/bff-deploy` | B — Deploy | 🔲 | 017 | — | STANDARD |
| 020 | Update shared `EntityCreationService.ts` for `sprk_searchindexname` cascade | A — Wizards | ✅ | 018 | — | FULL |
| 021 | `CreateMatterWizard` — set `sprk_searchindexname` from BU (FR-WIZ-01) | A | ✅ | 020 | Group A1 | FULL |
| 022 | `CreateProjectWizard` — fix latent G2 + set both fields (FR-WIZ-02) | A | ✅ | 020 | Group A1 | FULL |
| 023 | `CreateInvoiceWizard` — verify + fix gap + set both fields (FR-WIZ-03) | A | 🚫 | 020 | Group A1 | DEFERRED |
| 024 | `CreateWorkAssignmentWizard` — verify + fix gap + set both fields (FR-WIZ-04) | A | ✅ | 020 | Group A1 | FULL |
| 025 | `CreateEventWizard` — verify + fix gap + set both fields (FR-WIZ-05) | A | ✅ | 020 | Group A1 | FULL |
| 026 | DocumentUploadWizard — `resolveSearchIndexNameForRecord` chain (FR-WIZ-06) | A | ✅ | 020 | Group A2 | FULL |
| 027 | DocumentUploadWizard — `DocumentRecordService.buildRecordPayload` updates (FR-WIZ-07) | A | ✅ | 026 | — | FULL |
| 028 | INV-5 unit tests for all wizards (FR-WIZ-08) | A | ✅ | 021, 022, 023, 024, 025, 027 | — | STANDARD |
| 029 | Wizard code-page deploy via `/code-page-deploy` (all 6) | A — Deploy | 🔲 | 028 | — | STANDARD |
| 030 | PCF manifest — add `searchIndexName` bound property (FR-PCF-01) | D — PCF v1.1.74 | ✅ | 018 | — | FULL |
| 031 | PCF — `SemanticSearchApiService.search()` includes `searchIndexName` (FR-PCF-02) | D | ✅ | 030 | Group D1 | FULL |
| 032 | PCF — `NavigationService` includes `searchIndexName` + full filter parity envelope (FR-PCF-03 + FR-PARITY-01) | D + D.1 | ✅ | 030 | Group D1 | FULL |
| 033 | PCF — 5-location version bump v1.1.73 → v1.1.74 (FR-PCF-04) | D | ✅ | 031, 032 | — | STANDARD |
| 034 | PCF unit + UI tests (request shape, envelope shape, dark-mode) | D | 🔲 | 033 | — | STANDARD |
| 035 | PCF build + deploy via `/pcf-deploy` (NFR-10 clean-rebuild) | D — Deploy | 🔲 | 034 | — | STANDARD |
| 040 | Code page — extend `types/index.ts` `AppUrlParams` + `parseUrlParams.ts` (FR-CP-01) | E — Code page | ✅ | 035 | — | FULL |
| 041 | Code page — `App.tsx` removes void-discards + seeds filter state (FR-CP-02 + FR-CP-03) | E | ✅ | 040 | — | FULL |
| 042 | Code page — `useSemanticSearch.ts` + `useRecordSearch.ts` include `searchIndexName` (FR-CP-04) | E | ✅ | 040 | Group E1 | FULL |
| 043 | Code page — UI tests + parameter parsing tests | E | ✅ | 041, 042 | — | STANDARD |
| 044 | Code page — build + deploy via `/code-page-deploy` (NFR-11 clean-rebuild) | E — Deploy | 🔲 | 043 | — | STANDARD |
| 050 | Backfill — `Backfill-MultiContainerMultiIndex-ParentRecords.ps1` (FR-BF-01) | F — Backfill | ✅ | 029, 044 | Group F1 | FULL |
| 051 | Backfill — `Backfill-MultiContainerMultiIndex-Documents.ps1` (FR-BF-02) | F | ✅ | 029, 044 | Group F1 | FULL |
| 052 | Backfill — `Audit-MultiContainerMultiIndex-Drift.ps1` (FR-BF-03) | F | ✅ | 029, 044 | Group F1 | FULL |
| 053 | Backfill — test-environment dry run + INV-5-safety verification | F | 🔲 | 050, 051, 052 | — | STANDARD |
| 060 | Docs — `MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md` (FR-DOC-01) | G — Docs | ✅ | none | Group G1 | MINIMAL |
| 061 | Docs — update `SPAARKEAI-WORKSPACE-ARCHITECTURE.md` (FR-DOC-02) | G | ✅ | none | Group G1 | MINIMAL |
| 070 | UAT — BFF smoke (allow-list rejection + valid request routing) | H — Deploy + UAT | 🔲 | 018 | — | STANDARD |
| 071 | UAT — Wizards smoke (MCP-verify both fields populated on each entity create) | H | 🔲 | 029, 070 | — | STANDARD |
| 072 | UAT — PCF smoke on Protected Matter (BFF log verification) | H | 🔲 | 035, 070 | — | STANDARD |
| 073 | UAT — Filter-parity walkthrough (PCF vs code page side-by-side) | H | 🔲 | 044, 072 | — | STANDARD |
| 074 | UAT — BU-change coexistence proof (INV-3) | H | 🔲 | 071 | — | STANDARD |
| 090 | Project wrap-up — code-review + adr-check + repo-cleanup + lessons-learned | Wrap-up | 🔲 | 053, 060, 061, 073, 074 | — | FULL |

---

## Parallel Execution Groups

Tasks in the same group can run simultaneously once prerequisites are met. Sub-agent boundary rule (CLAUDE.md §3): tasks touching `.claude/` paths are auto-demoted to sequential — none in this project touch `.claude/`.

| Group | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|-------|-------|--------------|---------------|---------------------|
| B1 | 011, 012 | 010 ✅ | Models/Ai/*.cs (011) + appsettings.json (012) — disjoint | ✅ Yes |
| B2 | 013, 014, 015 | 010, 011 ✅ | SemanticSearchService.cs · RagService.cs · RecordSearchService.cs — disjoint | ✅ Yes |
| A1 | 021, 022, 023, 024, 025 | 020 ✅ | Five distinct code-page service files — disjoint | ✅ Yes (5 agents — under 6-cap) |
| A2 | 026 (followed by 027 serial) | 020 ✅ | DocumentUploadWizard files — 026/027 serial (shared file) | ⚠️ 026 only (027 depends on 026) |
| D1 | 031, 032 | 030 ✅ | SemanticSearchApiService.ts · NavigationService.ts — disjoint | ✅ Yes |
| E1 | 042 (single task — no parallel sibling) | 040 ✅ | useSemanticSearch.ts + useRecordSearch.ts (same task — both hooks updated together) | — |
| F1 | 050, 051, 052 | 029, 044 ✅ | Three new PowerShell scripts — disjoint | ✅ Yes |
| G1 | 060, 061 | none | New runbook + existing arch doc — disjoint | ✅ Yes |

**Wave structure** (recommended autonomous execution):

```
Wave 1 (serial):       001                                            — Operator BU setup
Wave 2 (serial):       010                                            — BFF interface + impl + allow-list
Wave 3 (parallel ×2):  011, 012                                       — Group B1
Wave 4 (parallel ×3):  013, 014, 015                                  — Group B2
Wave 5 (serial):       016                                            — BFF endpoint
Wave 6 (serial):       017                                            — BFF tests
Wave 7 (serial):       018                                            — BFF deploy
Wave 8 (serial):       020                                            — Shared service update (Phase A blocker)
Wave 9 (parallel ×5):  021, 022, 023, 024, 025                        — Group A1 (5 wizards)
Wave 10 (parallel ×2): 026, 028 ⚠️ 028 must wait for 027 too           — actually serial: 026 → 027 → 028
Wave 11 (serial):      029                                            — Wizard deploy
Wave 12 (serial):      030                                            — PCF manifest
Wave 13 (parallel ×2): 031, 032                                       — Group D1
Wave 14 (serial):      033 → 034 → 035                                — Version bump, tests, deploy
Wave 15 (serial):      040 → 041                                      — Code-page types + App.tsx
Wave 16 (in 041):       042 runs after 040 in parallel with 041       — Actually simpler: serial 040 → 041 → 042 → 043 → 044
Wave 17 (parallel ×2): 060, 061                                       — Group G1 (docs)  — can run anytime; suggest after 044
Wave 18 (parallel ×3): 050, 051, 052                                  — Group F1 (backfill scripts) after 044
Wave 19 (serial):      053                                            — Backfill dry run
Wave 20 (serial):      070 → 071 → 072 → 073 → 074                    — Phase H UAT
Wave 21 (serial):      090                                            — Wrap-up
```

**How to Execute Parallel Groups**:
1. Check all prerequisites are ✅ in Status column above
2. Invoke Skill tool with multiple `task-execute` invocations in ONE message (one per task in the group)
3. Each agent runs task-execute for one task independently
4. Wait for all to complete before next wave
5. **MAX CONCURRENCY: 6 agents per wave** (hard limit per CLAUDE.md §3) — Group A1 with 5 tasks is fine

---

## Critical Path

The longest dependency chain determines minimum project duration:

```
001 → 010 → 011 → 013/014/015 → 016 → 017 → 018 → 020 → 021..025 → 028 → 029 →
030 → 031/032 → 033 → 034 → 035 → 040 → 041 → 042 → 043 → 044 → 050/051/052 → 053 →
070 → 071 → 072 → 073 → 074 → 090
```

Rough sequence: ~28 sequential steps with parallel waves shortening real-time duration.

---

## High-Risk Items

| Risk | Affected Tasks | Mitigation |
|------|---------------|------------|
| Stale `dist/` of `@spaarke/auth` or `@spaarke/ui-components` poisons bundles | 035 (PCF deploy), 044 (Code page deploy) | NFR-10/11 enforced in deploy tasks; `feedback_stale-shared-lib-dist-poisons-codepage-bundle` |
| Unmapped SPE container in backfill | 050, 051, 053 | Halt-loud behavior (FR-BF-01/02) |
| BFF allow-list misconfigured per environment | 012, 018, 070 | Startup INFO log; operator runbook |
| PR #363 (v1.1.73) sequencing | 030–035 (PCF v1.1.74) | This PR rebases post-#363-merge |
| BFF publish size > 60 MB (NFR-01) | 018 | Per-task verification per CLAUDE.md §10 bullet 4; baseline 45.65 MB |

---

## Task Status Tracking

Updated by `task-execute` skill at task completion (Step 9.8). Use `🔲 → 🔄 → ✅` progression.

**To view current state at any time**: `cat tasks/TASK-INDEX.md | head -50`
