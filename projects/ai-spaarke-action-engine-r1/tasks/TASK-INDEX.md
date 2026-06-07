# Task Index — AI Spaarke Action Engine R1

> **Last Updated**: 2026-05-30
> **Total Tasks**: 46 (1 architecture spike + 41 implementation + 4 deployment + 1 wrap-up)
> **Estimated Effort**: ~180 hours
> **Status**: All ⌐ — task 001 pending start

## Status Legend

- 🔲 not-started
- 🔄 in-progress
- ✅ completed
- ⛔ blocked
- ⏸️ deferred

---

## Task Registry

| ID | Title | Phase | Status | Dependencies | Parallel | Rigor |
|----|-------|-------|--------|--------------|----------|-------|
| 001 | Architecture Spike — Runtime + Scheduler | 0 | 🔲 | none | — | FULL |
| 010 | Dataverse schema — Action entities (sprk_action × 4) | 1 | 🔲 | 001 | A | FULL |
| 011 | Dataverse schema — Tool Registry entity (extended metadata) | 1 | 🔲 | 001 | A | FULL |
| 012 | Dataverse schema — Gate Approval entity | 1 | 🔲 | 001 | A | FULL |
| 013 | Dataverse schema — `sprk_aichatcontextmap` extension | 1 | 🔲 | 001 | A | FULL |
| 014 | BFF — `AddActionEngineModule()` + folder skeleton | 1 | 🔲 | 001 | B | FULL |
| 015 | BFF — `Services/Ai/PublicContracts/IActionEngineFacade.cs` | 1 | 🔲 | 001 | B | FULL |
| 016 | Azure AI Search — `spaarke-resource-registry-index` | 1 | 🔲 | 001, 011 | B | FULL |
| 020 | Tool Registry extended metadata model | 2 | 🔲 | 011, 014, 015 | — | FULL |
| 021 | Tool Registry endpoints + queries | 2 | 🔲 | 020 | — | FULL |
| 022 | Meta-tool — `FindResources` (NFR-01) | 2 | 🔲 | 020, 016 | C | FULL |
| 023 | Meta-tool — `GetResourceDetail` | 2 | 🔲 | 020 | C | FULL |
| 024 | Meta-tool — `InvokeResource` | 2 | 🔲 | 020 | C | FULL |
| 025 | Always-on tools registration | 2 | 🔲 | 020 | — | FULL |
| 026 | `IToolHandlerRegistry` + Phase deny-tools (LAVERN #8) | 2 | 🔲 | 020 | — | FULL |
| 030 | `IGateResolver` interface + 5 gate types | 3 | 🔲 | 012, 014, 015 | — | FULL |
| 031 | `DataverseQueueGateResolver` | 3 | 🔲 | 030 | D | FULL |
| 032 | `InteractiveInChatGateResolver` | 3 | 🔲 | 030 | D | FULL |
| 033 | `WebhookGateResolver` (HMAC) | 3 | 🔲 | 030 | D | FULL |
| 034 | `AutoApproveGateResolver` | 3 | 🔲 | 030 | D | FULL |
| 035 | Gate 5-min timeout auto-reject | 3 | 🔲 | 031, 032, 033, 034 | — | FULL |
| 036 | `GateApprovalCard` Fluent v9 shared component | 3 | 🔲 | 030 | — | FULL |
| 040 | `ActionEndpoints.cs` — CRUD + `/run` | 4 | 🔲 | 010, 014, 015, 030 | — | FULL |
| 041 | `ActionOrchestrationService.cs` | 4 | 🔲 | 026, 030, 040 | — | FULL |
| 042 | `ScheduledActionDispatchJobHandler.cs` | 4 | 🔲 | 041, 001 | — | FULL |
| 043 | Audit middleware extension (ADR-015 Tier 2 + Tier 3) | 4 | 🔲 | 026, 041 | — | FULL |
| 044 | Rate limiting on `/run` (ADR-016 soft) | 4 | 🔲 | 040 | — | FULL |
| 045 | Feature flag + kill-switch wiring (ADR-018) | 4 | 🔲 | 040 | — | FULL |
| 050 | SpaarkeAi — `ChatLaunchContext` URL parsing | 5 | 🔲 | 013 | — | FULL |
| 051 | Ribbon launchers (Matter/Project/Account/Contact) | 5 | 🔲 | 050 | — | FULL |
| 052 | Default `Spaarke Assistant — General` playbook seed | 5 | 🔲 | 013, 025, 032 | — | STANDARD |
| 053 | Template — Summarize a matter | 5 | 🔲 | 040, 041, 052 | E | STANDARD |
| 054 | Template — Weekly task digest | 5 | 🔲 | 040, 041, 042, 052 | E | STANDARD |
| 055 | Template — Find similar matters | 5 | 🔲 | 040, 041, 052, 016 | E | STANDARD |
| 060 | Integration test — Starter Templates (G1) | 6 | 🔲 | 053, 054, 055 | F | STANDARD |
| 061 | Integration test — Meta-tools discovery (G2) | 6 | 🔲 | 022, 023, 024 | F | STANDARD |
| 062 | Integration test — Gate resolution all paths (G3) | 6 | 🔲 | 031, 032, 033, 034, 035 | F | STANDARD |
| 063 | Integration test — Phase deny-tools (G4) | 6 | 🔲 | 026 | F | STANDARD |
| 064 | Load test — FindResources p95 < 200ms (G5/NFR-01) | 6 | 🔲 | 022, 016 | G | STANDARD |
| 065 | Publish-size validation ≤5MB (G8/NFR-10) | 6 | 🔲 | 045 | G | STANDARD |
| 066 | CVE scan — no new HIGH-severity (G9) | 6 | 🔲 | 045 | G | STANDARD |
| 070 | Deploy BFF Action Engine (calls bff-deploy) | 6 | 🔲 | 060, 061, 062, 063, 064, 065, 066 | — | STANDARD |
| 071 | Deploy Dataverse schema + seed (calls dataverse-deploy) | 6 | 🔲 | 060, 070 | — | STANDARD |
| 072 | Deploy SpaarkeAi code page + ribbons | 6 | 🔲 | 050, 051, 071 | — | STANDARD |
| 073 | Post-deploy validation | 6 | 🔲 | 070, 071, 072 | — | STANDARD |
| 090 | Project wrap-up (code-review + adr-check + repo-cleanup) | Wrap-up | 🔲 | 073 | — | FULL |

**Totals by phase**: Phase 0: 1 · Phase 1: 7 · Phase 2: 7 · Phase 3: 7 · Phase 4: 6 · Phase 5: 6 · Phase 6: 11 · Wrap-up: 1 = **46 tasks**

---

## Parallel Execution Plan

Tasks in the same group can run **concurrently** once prerequisites are met. Maximum concurrency: **6 agents per wave** (root CLAUDE.md guidance — API overload guard).

### Wave Plan

| Wave | Group | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|------|-------|-------|--------------|---------------|---------------------|
| 1 | — | **001** | none | `notes/decisions/`, `notes/spikes/`, `plan.md` | sequential (single critical task) |
| 2 | A | **010, 011, 012, 013** | 001 ✅ | Separate Dataverse entities | ✅ Yes (4 agents) |
| 3 | B | **014, 015, 016** | 001 ✅ | Distinct files: DI module / facade / infra deploy | ✅ Yes (3 agents) |
| 4 | — | **020** | 011, 014, 015 ✅ | `ToolRegistry/ToolRegistryModel.cs` | sequential (foundation for Phase 2) |
| 5 | C | **022, 023, 024** | 020, 016 ✅ | Separate meta-tool implementations under `MetaTools/` | ✅ Yes (3 agents) |
| 6 | — | **021, 025, 026** | 020 ✅ | Endpoints, AlwaysOnTools/, Dispatch/ (distinct) | ✅ Yes (3 agents — runs alongside Wave 5 if no shared file with Group C) |
| 7 | — | **030** | 012, 014, 015 ✅ | `Gates/IGateResolver.cs` + types | sequential (foundation for Phase 3) |
| 8 | D | **031, 032, 033, 034** | 030 ✅ | Separate resolver implementations under `Gates/` | ✅ Yes (4 agents) |
| 9 | — | **035, 036** | Group D ✅ (035), 030 ✅ (036) | Timeout service + GateApprovalCard (different surfaces) | ✅ Yes (2 agents) |
| 10 | — | **040** | 010, 014, 015, 030 ✅ | `ActionEndpoints.cs` (new file) | sequential (blocks 041–045) |
| 11 | D' | **041, 042, 043** | 040, 026, 030 ✅ | Orchestrator / Job handler / AuditEnrichmentMiddleware edit | ✅ Yes (3 agents) — disjoint files |
| 12 | — | **044, 045** | 040 ✅ | Both edit `ActionEndpoints.cs` | ❌ No — sequential, file-overlap |
| 13 | — | **050** | 013 ✅ | `src/solutions/SpaarkeAi/src/utils/launch-resolver.ts` + new types | sequential (blocks 051) |
| 14 | — | **051, 052** | 050 ✅ (051), 013/025/032 ✅ (052) | Ribbon XML vs Dataverse playbook seed (separate surfaces) | ✅ Yes (2 agents) |
| 15 | E | **053, 054, 055** | 040, 041, 052 + 042 (054) + 016 (055) ✅ | Three JPS Template JSONs | ✅ Yes (3 agents) |
| 16 | F | **060, 061, 062, 063** | 053-055 (060), 022-024 (061), 031-035 (062), 026 (063) ✅ | Four separate integration test classes | ✅ Yes (4 agents) |
| 17 | G | **064, 065, 066** | 022/016 (064), 045 (065/066) ✅ | Load test / publish-size measurement / CVE scan (independent) | ✅ Yes (3 agents) |
| 18 | — | **070** | All Wave 16 + 17 ✅ | BFF App Service deploy | sequential (operational order) |
| 19 | — | **071** | 060, 070 ✅ | Dataverse solution import + seed | sequential |
| 20 | — | **072** | 050, 051, 071 ✅ | SpaarkeAi code page deploy | sequential |
| 21 | — | **073** | 070, 071, 072 ✅ | Post-deploy validation script | sequential |
| 22 | — | **090** | 073 ✅ | Wrap-up: code-review + adr-check + repo-cleanup; edits README + plan.md | sequential (final) |

### How to Execute Parallel Groups

1. Check all prerequisites are ✅ in this index.
2. Invoke the `task-execute` skill with **multiple subagents in ONE message** — one per task in the wave.
3. Each subagent runs `task-execute` for its task and loads all `<knowledge>` files from the POML.
4. After ALL agents in a wave complete, run **build verification** between waves:
   - Any `.cs` change → `dotnet build src/server/api/Sprk.Bff.Api/`
   - Any `.ts`/`.tsx` change → `npm run build` in the relevant package
5. Update this index (🔲 → ✅) and proceed to the next wave.

### Auto-Demotion Rules (per task-create Step 3.8)

- Any task touching `.claude/` paths → main-session-only (permission boundary). None in this project.
- File-overlap within proposed parallel group → demote one task to sequential. Applied to tasks 044, 045 (both edit `ActionEndpoints.cs`).

---

## Critical Path

```
001 → {010,011,012,013}A → 020 → {022,023,024}C → 040 → {041,042,043} → 060 → 070 → 071 → 072 → 073 → 090
                                                                                                          ↑
                                          Phase 6 gates (064,065,066) and tests (060-063) feed → 070
```

Longest dependency chain: **001 → 011 → 020 → 022 → 060 → 070 → 071 → 072 → 073 → 090** (10 sequential hops).

---

## High-Risk Items

| Task | Risk | Reference |
|------|------|-----------|
| 001 | Wrong scheduler choice → cascade rework | Risk Register R2 |
| 014 + 065 | Publish-size delta exceeds 5MB budget | Risk Register R1; NFR-10; G8 |
| 022 + 064 | `FindResources` p95 misses 200ms target | NFR-01; G5 |
| 026 + 063 | Phase deny-tools enforcement misses cases | G4 |
| 035 + 062 | Gate timeout / state-machine edge cases | G3 |
| 043 + 066 | Audit volume + CVE introduction | G6, G9; ADR-015 |

---

## Decisions Log (recorded by task-execute completion)

| Date | Task | Decision | Reference |
|------|------|----------|-----------|
| — | — | — | — |

*Populated by `task-execute` when each task completes. Architecture decisions also go to `notes/decisions/`.*

---

## Graduation Criteria → Verifying Task

| Gate | Task | Spec ref |
|------|------|----------|
| G1 — Three Templates execute E2E | 060 | spec.md §Success Criteria |
| G2 — Conversational invocation resolves intent → Tool | 061 (+053 acceptance) | spec.md §Success Criteria |
| G3 — IGateResolver all paths + 5-min timeout | 062 | spec.md §Success Criteria |
| G4 — Phase deny-tools throws | 063 | spec.md §Success Criteria |
| G5 — FindResources p95 <200ms (NFR-01) | 064 | spec.md §NFR-01 |
| G6 — Every Tool dispatch writes audit (ADR-015) | 043 (+060 verification) | ADR-015 |
| G7 — All 8 hallucination guardrails | 026 + 030 + 062 + 063 | spec.md §Success Criteria |
| G8 — Publish-size delta ≤5MB (NFR-10) | 065 (+001 baseline) | ADR-029, NFR-10 |
| G9 — No new HIGH-sev CVE | 066 | bff-extensions.md |
| G10 — Runtime ADR documented | 001 | spec.md §15 |
| G11 — Endpoint-filter auth on every endpoint | 040 + 090 grep audit | ADR-008 |
| G12 — No CRUD-side AE-internal injection | 015 + 041 + 090 grep audit | ADR-013 refined |

---

*Updated automatically by `task-execute` upon task completion. Next: run `execute task 001` to start the architecture spike.*
