# Task Index — Spaarke Compose (R1)

> **Generated**: 2026-06-29 by `/project-pipeline` Step 3 (via `task-create`)
> **Extended**: 2026-07-01 — supplement scope tasks 091-102 + 110 added; see [`spec-supplement-2026-07-01-three-pane-pivot.md`](../spec-supplement-2026-07-01-three-pane-pivot.md)
> **Project**: [`spaarkeai-compose-r1`](../README.md)
> **Spec**: [`spec.md`](../spec.md) · **Supplement**: [`spec-supplement-2026-07-01-three-pane-pivot.md`](../spec-supplement-2026-07-01-three-pane-pivot.md) · **Plan**: [`plan.md`](../plan.md) · **CLAUDE.md**: [`CLAUDE.md`](../CLAUDE.md)
> **Portfolio**: [Issue #514](https://github.com/spaarke-dev/spaarke/issues/514) · [Epic #424 DOCUMENT INTELLIGENCE](https://github.com/spaarke-dev/spaarke/issues/424)

## Status Legend

| Symbol | Meaning |
|---|---|
| 🔲 | Not started |
| 🔄 | In progress / needs retry |
| ✅ | Completed |
| ⏸ | Deferred (with reason in task `<notes>`) |
| ❌ | Abandoned |

---

## Task Summary

| Phase | Tasks | Description |
|---|---|---|
| **Phase 0** (Spikes — blocking gate) | 001–004 | TipTap/DOCX, three-pane wiring, SPE check-out, consumer-routing |
| **Phase 1** (Dataverse + JPS) | 010–012 | Compose workspace layout row, playbookconsumer row, JPS scopes |
| **Phase 2** (BFF endpoints + services) | 020–027 | ConsumerType constant, 3 services, 7 endpoints, DI registration, tests |
| **Phase 3** (Shared library) | 030–033 | `@spaarke/document-operations` extraction from SemanticSearch |
| **Phase 4** (Frontend) | 040–046 | Section registration, data contracts, 4 Compose components, modal launch |
| **Phase 5** (SPE check-out lock) | 050–052 | Check-out acquisition, multi-tab UX, heartbeat |
| **Phase 6** (Smoke test) | 060–061 | E2E + automated `compose-summarize` round-trip |
| **Phase 7** (Testing + acceptance) | 070–072 | Success criteria, ADR-038 conformance, CVE/coverage |
| **Phase 8** (Deployment) | 080–081 | BFF deploy + publish-size, code-page + Dataverse deploy |
| **Baseline Wrap-up** | 090 | (SUPERSEDED by 110) Code-review + adr-check + test-diet + repo-cleanup + lessons-learned + archive |
| **Phase 7 SUPPLEMENT** (Three-pane pivot) | 091–093 | Move ComposeWorkspace to shared lib; remove Path A special-case; swap FU-3 placeholder |
| **Phase 8 SUPPLEMENT** (Streaming SSE backend) | 094–097 | IDocxTextExtractor; extend IInvokePlaybookAi; convert /api/compose/action to SSE |
| **Phase 9 SUPPLEMENT** (Assistant pane wiring) | 098–099 | ConversationPane consumes SSE; retire summary banner |
| **Phase 10 SUPPLEMENT** (Polish) | 100–102 | Workspace-tab suppression; modal 80×80; ADR-013 amendment |
| **Phase 11 SUPPLEMENT** (Expanded Wrap-up) | 110 | Full close-out for baseline + supplement scope |
| **Total** | **49 tasks** (37 baseline + 12 supplement + 1 renumbered wrap-up) | |

---

## Tasks

| ID | Title | Phase | Status | Tags | Rigor | Dependencies | Parallel Group | Parallel Safe |
|---|---|---|---|---|---|---|---|---|
| 001 | TipTap OOB + DOCX round-trip prototype | 0 | ✅ | spike, frontend, tiptap, docx, prototype | STANDARD | none | A | ✅ |
| 002 | Three-pane coordination wiring prototype | 0 | 🔲 | spike, frontend, typescript, prototype | STANDARD | none | A | ✅ |
| 003 | SPE check-out/check-in + Document promotion-on-Save | 0 | ✅ | spike, backend, bff-api, spe, prototype | STANDARD | none | A | ✅ |
| 004 | Consumer-routing E2E smoke + JPS scope registration | 0 | 🔲 | spike, backend, bff-api, ai, prototype | STANDARD | none | A | ✅ |
| 010 | Create `sprk_workspacelayout` Compose row | 1 | ✅ | dataverse, solution, data | STANDARD | 001,002,003,004 | B | ✅ |
| 011 | Create `sprk_playbookconsumer` compose-summarize row | 1 | ✅ | dataverse, solution, ai | STANDARD | 004 | B | ✅ |
| 012 | Register JPS scopes (compose-selection, compose-document) | 1 | ✅ | ai, jps, dataverse, solution | STANDARD | 004 | B | ✅ |
| 020 | Add `ConsumerTypes.ComposeSummarize` constant | 2 | ✅ | bff-api, backend | FULL | 001,002,003,004 | — | ❌ (blocks 021–027) |
| 021 | Create `Services/Compose/ComposeService.cs` | 2 | ✅ | bff-api, backend, services | FULL | 020 | C | ✅ |
| 022 | Create `Services/Compose/ComposeDocumentService.cs` | 2 | ✅ | bff-api, backend, services, spe | FULL | 020 | C | ✅ |
| 023 | Create `Services/Compose/ComposeSessionService.cs` | 2 | ✅ | bff-api, backend, services | FULL | 020 | C | ✅ |
| 024 | Create `Api/ComposeEndpoints.cs` (7 endpoints) | 2 | ✅ | bff-api, backend, endpoints, minimal-api | FULL | 021,022,023 | — | ❌ |
| 025 | Update Program.cs — register Compose DI | 2 | ✅ | bff-api, backend, di, config | FULL | 024 | — | ❌ |
| 026 | Unit tests for 3 Compose services | 2 | ✅ | bff-api, testing, unit-test | FULL | 021,022,023 | D | ✅ |
| 027 | Integration tests for 7 Compose endpoints | 2 | ✅ | bff-api, testing, integration-test | FULL | 024 | D | ✅ |
| 030 | Scaffold `@spaarke/document-operations` package | 3 | ✅ | frontend, typescript, shared-lib, refactoring | FULL | 001,002,003,004 | E | ✅ |
| 031 | Move `useDocumentActions` to shared lib | 3 | ✅ | frontend, typescript, refactoring | FULL | 030 | — | ❌ |
| 032 | Refactor SemanticSearch to consume shared lib | 3 | ✅ | frontend, typescript, refactoring | FULL | 031 | — | ❌ |
| 033 | Verify SemanticSearch tests pass; add consumer tests | 3 | ✅ | testing, frontend, integration-test | STANDARD | 032 | — | ❌ |
| 040 | Register `compose-editor` section type | 4 | ✅ | frontend, react, typescript, fluent-ui, widget | FULL | 001,002,003,004 | F | ✅ |
| 041 | Create six TypeScript data-contract interfaces | 4 | ✅ | frontend, typescript | FULL | 002 | F | ✅ |
| 042 | Create `ComposeWorkspace.tsx` | 4 | ✅ | frontend, react, typescript, fluent-ui, widget | FULL | 032,040,045 | — | ❌ |
| 043 | Create `ComposeToolbar.tsx` | 4 | 🔲 | frontend, react, typescript, fluent-ui | FULL | 032 | F2 | ✅ |
| 044 | Create `ComposeEmptyState.tsx` | 4 | ✅ | frontend, react, typescript, fluent-ui | FULL | 001 | F2 | ✅ |
| 045 | Create `ComposeEditor.tsx` (TipTap + DOCX bridge) | 4 | ✅ | frontend, react, typescript, fluent-ui, tiptap | FULL | 001,040 | — | ❌ |
| 046 | Wire modal launch (Path A) | 4 | 🔲 | frontend, react, typescript, fluent-ui | FULL | 042 | — | ❌ |
| 050 | Acquire SPE check-out on Compose open | 5 | ✅ | bff-api, backend, frontend, spe | FULL | 003,024,042 | — | ❌ |
| 051 | Multi-tab conflict UX | 5 | ✅ | frontend, typescript, fluent-ui | FULL | 050 | — | ❌ |
| 052 | Heartbeat + 15 min idle orphan release | 5 | ✅ | bff-api, backend, frontend | FULL | 050 | G | ✅ |
| 060 | E2E smoke test: compose-summarize | 6 | ✅ | testing, integration-test, ai, e2e-test | STANDARD | 010,011,012,026,043 | — | ❌ |
| 061 | Automated integration test: compose-summarize | 6 | ✅ | testing, integration-test | STANDARD | 060 | — | ❌ |
| 070 | Verify 22 spec success criteria | 7 | 🔲 | testing, integration-test, acceptance | STANDARD | 020–027,030–033,040–046,050–052,060,061 | — | ❌ |
| 071 | ADR-038 conformance check (no banned patterns) | 7 | 🔲 | testing, refactoring | STANDARD | 026,027,033,061 | H | ✅ |
| 072 | CVE scan + coverage observation | 7 | 🔲 | testing, devops | MINIMAL | 024,025 | H | ✅ |
| 080 | Deploy BFF + measure publish-size | 8 | 🔲 | deploy, azure, bff-api | FULL | 070,071,072 | — | ❌ |
| 081 | Deploy code-page + Dataverse artifacts | 8 | 🔲 | deploy, dataverse, solution | FULL | 070,071,072 | I | ✅ |
| 090 | Project wrap-up (code-review, adr-check, test-diet, archive) — **SUPERSEDED BY 110** | Wrap-up | ⏸ | project-wrap-up, refactoring | FULL | 080,081 | — | ❌ |
| 091 | Move ComposeWorkspace + hooks + types to `@spaarke/compose-components` | 7-supp | ✅ | refactoring, packaging, shared-lib, pattern-d | FULL | none | J | ❌ |
| 092 | App.tsx: remove Path A special-case; render ThreePaneShell always | 7-supp | ✅ | frontend, three-pane, app-root | FULL | 091 | — | ❌ |
| 093 | Swap FU-3 placeholder → real ComposeWorkspace | 7-supp | ✅ | frontend, section-registry, three-pane, pattern-d | FULL | 091,092 | — | ❌ |
| 094 | Add IDocxTextExtractor service (DocumentFormat.OpenXml) + tests | 8-supp | ✅ | bff-api, compose, docx, openxml | FULL | none | K | ✅ |
| 095 | Extend IInvokePlaybookAi with document-context overload | 8-supp | ✅ | bff-api, facade, adr-013 | FULL | 094 | — | ❌ |
| 096 | Update InvokePlaybookAi + NullInvokePlaybookAi implementations | 8-supp | ✅ (collapsed into 095) | bff-api, facade | STANDARD | 095 | — | ❌ |
| 097 | Convert /api/compose/action/{consumerType} to SSE streaming | 8-supp | ✅ | bff-api, compose, sse, endpoint | FULL | 094,095,096 | — | ❌ |
| 098 | ConversationPane consumes compose_summarize_request + SSE | 9-supp | ✅ | frontend, conversation-pane, sse | FULL | 097 | — | ❌ |
| 099 | Remove summary banner from ComposeBannerStack | 9-supp | ✅ | frontend, cleanup | STANDARD | 098 | — | ❌ |
| 100 | Workspace-tab suppression in compose mode | 10-supp | ✅ | frontend, workspace-shell | STANDARD | 092 | L | ✅ |
| 101 | Modal 80% × 80% in launch-resolver | 10-supp | ✅ | frontend, launch-resolver, trivial | MINIMAL | none | L | ✅ |
| 102 | ADR-013 amendment (Path B) — widened facade | 10-supp | ✅ | adr, documentation, path-b | STANDARD | 095 | L | ✅ |
| 110 | Expanded project wrap-up (baseline + supplement scope) | 11-supp | 🔲 | project-wrap-up | FULL | 091,092,093,094,095,096,097,098,099,100,101,102 | — | ❌ |

---

## Parallel Execution Plan

Tasks in the same group can run simultaneously once prerequisites are met. Max wave concurrency: **6 agents** (hard limit per project-pipeline Step 5 contract).

| Group | Tasks | Prerequisite | Files Touched | Safe to Parallelize | Notes |
|---|---|---|---|---|---|
| **A** | 001, 002, 003, 004 | none | `notes/spikes/*.md` per spike (separate files) | ✅ Yes | All four spikes are independent prototypes |
| **B** | 010, 011, 012 | A complete | Separate Dataverse rows + JPS scope catalog (different artifacts) | ✅ Yes | Dataverse + JPS work parallelizes |
| **C** | 021, 022, 023 | 020 ✅ | Separate `Services/Compose/*.cs` files | ✅ Yes | 3 independent BFF services |
| **D** | 026, 027 | 021/022/023 ✅ + 024 ✅ | Separate test files (`tests/unit/Sprk.Bff.Api.Tests/Services/Compose/`, `Api/ComposeEndpointsTests.cs`) | ✅ Yes | Unit tests + endpoint tests |
| **E** | 030 | A complete | `src/client/shared/Spaarke.DocumentOperations/` (new package) | ✅ Yes | Solo group — package skeleton parallelizes with Phase 2 |
| **F** | 040, 041 | A complete (002) | Separate files: SECTION_REGISTRY entry + new `src/solutions/SpaarkeAi/src/types/compose.ts` | ✅ Yes | Section reg + data contracts |
| **F2** | 043, 044 | 032 ✅ | Separate `.tsx` files (toolbar vs empty-state) | ✅ Yes | Independent UI components |
| **G** | 052 | 050 ✅ | BFF heartbeat endpoint + client timer (separate from 051's UX dialog) | ✅ Yes | Parallel with 051 |
| **H** | 071, 072 | All Phase 2–6 ✅ | Test scan + CVE report (separate notes files) | ✅ Yes | Independent observations |
| **I** | 081 | 070,071,072 ✅ | Code-page + Dataverse deploy (separate from BFF) | ✅ Yes | Parallel with 080 |

**How to execute parallel groups**:
1. Check all prerequisite tasks complete (✅) in Status
2. Invoke `task-execute` skill with multiple Skill tool invocations in ONE message (one per task in the group)
3. Each subagent runs `task-execute` for one task
4. Wait for all to complete before next group
5. **Build verification between waves**: after each wave, main session runs `dotnet build` (if `.cs` touched) and/or `npm run build` (if `.ts/.tsx` touched). Wave dispatch stops on first build break.

**.claude/ permission boundary**: No tasks in this project touch `.claude/` paths, so no auto-demotion-to-sequential is required. All `parallel-safe=true` markings are mechanically safe.

---

## Wave Plan (Recommended Execution Order)

| Wave | Tasks | Type | Build Check After |
|---|---|---|---|
| **W0** | 001, 002, 003, 004 | Parallel (4 agents) — Phase 0 Spikes | None (prototypes only; outputs to `notes/spikes/`) |
| **W1a** | 010, 011, 012 | Parallel (3 agents) — Phase 1 Dataverse + JPS | None (Dataverse + JPS only) |
| **W1b** | 020, 030, 040, 041 | Parallel (4 agents) — ConsumerType + shared lib pkg + section reg + data contracts | `dotnet build` (020); `npm run build` (030, 040, 041) |
| **W2** | 021, 022, 023, 031 | Parallel (4 agents) — Compose BFF services + hook move | `dotnet build` (021–023); `npm run build` (031) |
| **W3** | 024, 032 | Sequential dependencies (single agent each in wave) | `dotnet build` (024); `npm run build` (032) |
| **W4** | 025, 033, 043, 044, 045 | Mixed (DI reg serial; tests + UI components parallel where possible) | `dotnet build` (025); `npm run build` (033, 043, 044, 045) |
| **W5** | 026, 027, 042 | Mixed (BFF tests parallel; ComposeWorkspace serial) | `dotnet build` (026, 027); `npm run build` (042) |
| **W6** | 046, 050 | Sequential (modal launch + SPE check-out endpoint) | Build per language touched |
| **W7** | 051, 052 | Parallel (2 agents) — multi-tab UX + heartbeat | `npm run build` (051); `dotnet build` (052) |
| **W8** | 060, 061 | Sequential (smoke test → integration test) | `dotnet test` |
| **W9** | 070, 071, 072 | Parallel (3 agents) — testing/acceptance | None (read-only checks) |
| **W10** | 080, 081 | Parallel (2 agents) — deploy BFF + code-page/Dataverse | Smoke-test endpoints post-deploy |
| **W11** | 090 | Sequential — wrap-up gate | All build + tests + test-diet |

**Total estimated waves**: 12 (W0–W11). Critical path: W0 (spikes) → W1b (020 blocks all Phase 2) → W3 (024 blocks 025, 050) → W6 (050 blocks 051/052) → W8 (smoke test) → W11 (wrap-up).

---

## Component Justification Audit (per CLAUDE.md §11)

Tasks adding NEW surface (per Step 3.5.6) and their justification status:

| Task | New Surface | Justification Present | Concrete Cost-of-doing-nothing |
|---|---|---|---|
| 010 | New `sprk_workspacelayout` row | ✅ | FR-01 fails — no "Compose" entry in workspace picker |
| 011 | New `sprk_playbookconsumer` row | ✅ | FR-10 fails — `IConsumerRoutingService.ResolveAsync` returns null for compose-summarize |
| 012 | 2 new JPS scopes | ✅ | FR-08 fails — scopes absent from catalog |
| 020 | New constant in ConsumerTypes.cs | ✅ | FR-09 fails — code references undefined constant |
| 021 | New `ComposeService` | ✅ | FR-04/05/06 fail — upload/load/save/promote not orchestrated |
| 022 | New `ComposeDocumentService` | ✅ | FR-04/05 fail — SPE plumbing absent |
| 023 | New `ComposeSessionService` | ✅ | FR-07 fails — ChatSession DocumentId binding absent |
| 024 | 7 new endpoints | ✅ | FR-21 fails — `/api/compose/*` returns 404 |
| 025 | New DI registrations + endpoint mapping | ✅ | Endpoints registered but unreachable without DI |
| 030 | New `@spaarke/document-operations` package | ✅ | FR-13 fails — Compose duplicates `useDocumentActions` |
| 040 | New section type in SECTION_REGISTRY | ✅ | FR-02 fails — Compose layout cannot mount any section |
| 041 | Six new TypeScript interfaces | ✅ | FR-20 fails — three-pane data contracts not enforceable |
| 042 | New `ComposeWorkspace.tsx` | ✅ | FR-02 fails — workspace shell doesn't exist |
| 043 | New `ComposeToolbar.tsx` | ✅ | FR-12 fails — Open-in-Word + dispatch UI absent |
| 044 | New `ComposeEmptyState.tsx` | ✅ | FR-18 fails — empty state shows nothing |
| 045 | New `ComposeEditor.tsx` | ✅ | FR-02 fails — TipTap host doesn't exist |
| 046 | New modal-launch wiring | ✅ | FR-19 fails — command-bar "Open in Compose" doesn't open |
| 050 | New SPE checkout BFF endpoint | ✅ | FR-15 fails — check-out lock not acquired |
| 051 | New multi-tab UX dialog | ✅ | FR-16 fails — multi-tab conflict UX absent |
| 052 | New heartbeat endpoint + client timer | ✅ | FR-17 fails — orphan locks accumulate indefinitely |

Tasks NOT requiring justification (modify existing only): 001–004 (spikes throwaway), 031–033 (move existing hook + verify tests), 026, 027, 060, 061, 070–072, 080, 081, 090 (testing/deploy/wrap-up).

---

## Test-Modifying Override (per CLAUDE.md §8)

Per CLAUDE.md §8 test-modifying override (FR-B07 + ADR-038): **any task modifying `tests/**` runs FULL rigor protocol at task-execute Step 9.5 unconditionally** (overrides default STANDARD skip).

Tasks subject to this override:
- **026** (Compose service unit tests)
- **027** (Compose endpoint integration tests)
- **033** (SemanticSearch consumer test additions)
- **061** (compose-summarize roundtrip integration test)
- **071** (banned-pattern scan)
- **072** (CVE + coverage)
- **090** (wrap-up runs `/test-diet` per CLAUDE.md §7 project-close gate)

Each of the above POML files declares `rigor-hint=FULL` or `STANDARD` per its individual characteristics; the override applies regardless at task-execute time.

---

## Hot-Path Coordination (per `projects/INDEX.md`)

This project will be **the 15th BFF-touching + 9th SpaarkeAi-touching active worktree** when registered. Coordination notes:

- **BFF hot path**: 14 other projects modify `src/server/api/Sprk.Bff.Api/**`. Compose adds new files only (`Api/ComposeEndpoints.cs`, `Services/Compose/*.cs`) + appends DI registrations to `Program.cs`. Coordinate `Program.cs` edits via PR sequencing.
- **SpaarkeAi hot path**: 8 other projects modify `src/solutions/SpaarkeAi/**`. Compose adds new files only (`components/compose/*.tsx`) + registers a new section type. Section-registry entry is additive; conflict risk low.
- See `.claude/constraints/bff-extensions.md` §§ F.1–F.3 + § G for the binding pre-merge checklist.

---

## Next Action

Run the first wave (Wave 0 — Phase 0 Spikes) by saying:
- `"work on task 001"` (start one task), OR
- `"continue"` (start next pending task), OR
- `"execute wave 0"` (parallel dispatch — 4 agents)

Each invocation MUST go through `task-execute` (auto-detected by Claude Code per CLAUDE.md §4). The skill loads the task POML, applies the rigor protocol, runs quality gates at Step 9.5, and checkpoints every 3 steps.

---

*Generated by `/project-pipeline` Step 3. Updated by `task-execute` (status flips) and `/devops-project-sync` (Task Count / Tasks Completed fields on Issue #514).*
