# TASK-INDEX — SDAP BFF API & Performance Enhancement

> **Total Tasks**: 51
> **Phases**: 5 implementation + 1 verification + 1 wrap-up
> **Parallel Groups**: 12 waves for concurrent agent execution

## Task Registry

### Phase 1: Foundation (Week 1)

| # | Task | Status | Tags | Parallel | Deps | Est |
|---|------|--------|------|----------|------|-----|
| 001 | A1: Audit Program.cs composition root | 🔲 | bff-api, architecture | Wave-1.1 | — | 2h |
| 002 | A1: Extract startup modules from Program.cs | 🔲 | bff-api, architecture | — | 001 | 3h |
| 003 | A1: Extract remaining DI modules, finalize Program.cs | 🔲 | bff-api, architecture | — | 002 | 2h |
| 004 | C3: Fix thread-safety bugs in DataverseWebApiService | 🔲 | bff-api, dataverse, critical | Wave-1.1 | — | 3h |
| 005 | C1: Replace ColumnSet(true) with explicit columns | 🔲 | bff-api, dataverse | Wave-1.1 | — | 4h |
| 006 | B5: Remove debug endpoints from non-dev environments | 🔲 | bff-api, security | Wave-1.1 | — | 1h |
| 007 | Phase 1 integration verification | 🔲 | testing, verification | — (gate) | 003,004,005,006 | 2h |

### Phase 2: Caching (Week 2)

| # | Task | Status | Tags | Parallel | Deps | Est |
|---|------|--------|------|----------|------|-----|
| 010 | A2: Feature flag infrastructure (ModuleState, ModuleGateFilter) | 🔲 | bff-api, feature-flags | Wave-2.1 | 003 | 3h |
| 011 | A2: Integrate feature flags into all DI modules | 🔲 | bff-api, feature-flags | — | 010 | 3h |
| 012 | B1: Graph metadata cache infrastructure | 🔲 | bff-api, caching, redis | Wave-2.1 | 003 | 4h |
| 013 | B1: Graph metadata cache — SpeFileStore integration | 🔲 | bff-api, caching, redis | — | 012 | 5h |
| 014 | B2: Authorization data snapshot cache | 🔲 | bff-api, caching, auth | Wave-2.1 | 003 | 6h |
| 015 | B3: GraphServiceClient singleton pooling | 🔲 | bff-api, performance | Wave-2.1 | 003 | 2h |
| 016 | B4: Request-scoped document metadata cache | 🔲 | bff-api, caching | Wave-2.1 | — | 2h |
| 017 | Phase 2 integration verification | 🔲 | testing, verification | — (gate) | 011,013,014,015,016 | 2h |
| 018 | Deploy Phase 2 to dev environment | 🔲 | deploy | — (gate) | 017 | 1h |

### Phase 3: Resilience & AI Performance (Weeks 3-4)

| # | Task | Status | Tags | Parallel | Deps | Est |
|---|------|--------|------|----------|------|-----|
| 020 | D1: Priority job queue infrastructure | 🔲 | bff-api, jobs, resilience | Wave-3.2 | 003 | 4h |
| 021 | D1: Priority queue routing and migration | 🔲 | bff-api, jobs, resilience | — | 020 | 4h |
| 022 | D2: Persistent analysis state — Redis hot layer | 🔲 | bff-api, caching, resilience | Wave-3.2 | 003 | 3h |
| 023 | D2: Persistent analysis state — Dataverse durable layer | 🔲 | bff-api, dataverse, resilience | — | 022 | 4h |
| 024 | D3: Resource budget tracking infrastructure | 🔲 | bff-api, resilience | Wave-3.2 | 003 | 3h |
| 025 | D3: Resource budget endpoint filter and enforcement | 🔲 | bff-api, resilience, api | — | 024 | 3h |
| 026 | E1: Parallel read-only tool execution in chat pipeline | 🔲 | bff-api, ai, performance | Wave-3.1 | — | 4h |
| 027 | E2: Batch embedding API | 🔲 | bff-api, ai, performance | Wave-3.1 | — | 4h |
| 028 | C2: Dataverse request batching ($batch) | 🔲 | bff-api, dataverse | — | 004 | 5h |
| 029 | C4: Pagination support for unbounded queries | 🔲 | bff-api, dataverse | Wave-3.1 | — | 3h |
| 030 | Phase 3 integration verification | 🔲 | testing, verification | — (gate) | 021,023,025,026,027,028,029 | 2h |
| 031 | Deploy Phase 3 to dev environment | 🔲 | deploy | — (gate) | 030 | 1h |

### Phase 4: Infrastructure (Weeks 4-6)

| # | Task | Status | Tags | Parallel | Deps | Est |
|---|------|--------|------|----------|------|-----|
| 040 | F1: VNet and subnet creation (Bicep) | 🔲 | infrastructure, azure, security | Wave-4.1 | — | 5h |
| 041 | F1: Private endpoints for all services | 🔲 | infrastructure, azure, security | Wave-4.2 | 040 | 8h |
| 042 | F1: NSG rules and App Service VNet integration | 🔲 | infrastructure, azure, security | Wave-4.2 | 040 | 4h |
| 043 | F2: App Service autoscaling rules | 🔲 | infrastructure, azure | Wave-4.1 | — | 4h |
| 044 | F3: Deployment slot configuration | 🔲 | infrastructure, azure | Wave-4.1 | — | 3h |
| 045 | F4: Redis production hardening | 🔲 | infrastructure, azure, security | Wave-4.3 | 041 | 3h |
| 046 | F5: Key Vault hardening | 🔲 | infrastructure, azure, security | Wave-4.3 | 041 | 2h |
| 047 | F6: Storage account hardening | 🔲 | infrastructure, azure, security | Wave-4.3 | 041 | 2h |
| 048 | F7: OpenAI capacity planning | 🔲 | infrastructure, azure, ai | Wave-4.1 | — | 3h |
| 049 | F8: AI Search cleanup | 🔲 | infrastructure, azure, ai | Wave-4.1 | — | 2h |
| 050 | F1: Disable public access on all resources | 🔲 | infrastructure, azure, security | — (gate) | 041,042,045,046,047 | 3h |
| 051 | Phase 4 infrastructure verification | 🔲 | testing, verification | — (gate) | 043,044,048,049,050 | 2h |

### Phase 5: CI/CD (Weeks 5-6)

| # | Task | Status | Tags | Parallel | Deps | Est |
|---|------|--------|------|----------|------|-----|
| 052 | G1: Test suite re-enablement | 🔲 | testing, ci-cd | Wave-5.1 | — | 5h |
| 053 | G2: IaC Bicep deployment pipeline (templates) | 🔲 | infrastructure, ci-cd | Wave-5.1 | 050 | 4h |
| 054 | G2: IaC CI/CD pipeline configuration | 🔲 | ci-cd | — | 053 | 3h |
| 055 | G3: Environment promotion pipeline | 🔲 | ci-cd | — | 054 | 4h |
| 056 | G4: Deployment slot CI/CD integration | 🔲 | ci-cd | — | 044,055 | 2h |
| 057 | Phase 5 CI/CD verification | 🔲 | testing, verification | — (gate) | 052,054,055,056 | 2h |

### Phase 6: Final Verification & Wrap-up

| # | Task | Status | Tags | Parallel | Deps | Est |
|---|------|--------|------|----------|------|-----|
| 058 | End-to-end integration testing | 🔲 | testing, verification | — | 031,051,057 | 4h |
| 059 | Documentation updates | 🔲 | documentation | — | 058 | 2h |
| 090 | Project wrap-up | 🔲 | project-management | — (final) | 058,059 | 2h |

---

## Parallel Execution Groups

These groups define tasks that **CAN execute simultaneously** using separate Claude Code agents (subagents or teammates). Each wave requires its prerequisites to be complete before any task in the wave can start.

### Execution Wave Diagram

```
TIME →

WAVE 1.1 ─── [001] [004] [005] [006]     ← 4 parallel agents
               │
WAVE 1.2 ─── [002]                        ← sequential (A1 chain)
               │
WAVE 1.3 ─── [003]                        ← sequential (A1 chain)
               │
GATE ──────── [007]                        ← Phase 1 verification
               │
         ┌─────┴──────┬──────────┐
WAVE 2.1 ┤ [010] [012] [014] [015] [016]  ← 5 parallel agents
         │   │     │
WAVE 2.2 │ [011] [013]                    ← 2 parallel (after Wave 2.1 items)
         │
GATE ──── [017] → [018]                   ← Phase 2 verification + deploy
         │
    ┌────┴────┬──────────┐
WAVE 3.1 │ [026] [027] [029]              ← 3 parallel (no code deps, can overlap Wave 2)
         │
WAVE 3.2 ┤ [020] [022] [024]              ← 3 parallel agents
         │   │     │     │
WAVE 3.3 ┤ [021] [023] [025] [028]        ← 4 parallel (after Wave 3.2 items + C3)
         │
GATE ──── [030] → [031]                   ← Phase 3 verification + deploy

WAVE 4.1 ─── [040] [043] [044] [048] [049] ← 5 parallel agents (infra independent)
               │
WAVE 4.2 ─── [041] [042]                  ← 2 parallel (after VNet)
               │
WAVE 4.3 ─── [045] [046] [047]            ← 3 parallel (after private endpoints)
               │
GATE ──────── [050] → [051]               ← Disable public + verify

WAVE 5.1 ─── [052] [053]                  ← 2 parallel
               │
WAVE 5.2 ─── [054] → [055] → [056]       ← sequential chain
               │
GATE ──────── [057]                        ← Phase 5 verification

FINAL ─────── [058] → [059] → [090]       ← Sequential wrap-up
```

### Parallel Group Reference

| Wave | Tasks | Prerequisite | Max Agents | File Ownership |
|------|-------|-------------|------------|----------------|
| **1.1** | 001, 004, 005, 006 | None | 4 | 001: Program.cs (read), 004: DataverseWebApiService.cs, 005: DataverseService.cs, 006: Endpoints |
| **2.1** | 010, 012, 014, 015, 016 | A1 complete (003) | 5 | 010: ModuleState/Filter, 012: new cache service, 014: AuthService cache, 015: GraphClientFactory, 016: RequestCache |
| **2.2** | 011, 013 | Wave 2.1 items | 2 | 011: DI modules, 013: SpeFileStore |
| **3.1** | 026, 027, 029 | None | 3 | 026: Ai/Chat/Tools, 027: Ai embedding service, 029: DataverseService pagination |
| **3.2** | 020, 022, 024 | A1 complete (003) | 3 | 020: Workers/Jobs, 022: WorkingDocumentService, 024: new ResourceBudget |
| **3.3** | 021, 023, 025, 028 | Wave 3.2 + C3 | 4 | 021: JobSubmission, 023: Dataverse entities, 025: Filters, 028: batch helper |
| **4.1** | 040, 043, 044, 048, 049 | None | 5 | All in infrastructure/bicep/ (separate modules) |
| **4.2** | 041, 042 | VNet (040) | 2 | 041: private-endpoint.bicep, 042: NSG + app-service.bicep |
| **4.3** | 045, 046, 047 | PE (041) | 3 | 045: redis.bicep, 046: key-vault.bicep, 047: storage-account.bicep |
| **5.1** | 052, 053 | F1 complete | 2 | 052: tests/, 053: infrastructure/bicep/params/ |

### Agent Team Configuration (for Agent Teams mode)

```
# Example: Phase 1 parallel execution with 4 teammates
Create an agent team for Phase 1 Foundation:
- Teammate 1 (Agent-A1): Task 001 — Audit Program.cs (owns: Program.cs read-only, notes/)
- Teammate 2 (Agent-C3): Task 004 — Thread-safety fixes (owns: DataverseWebApiService.cs)
- Teammate 3 (Agent-C1): Task 005 — Column selection (owns: DataverseService.cs, column queries)
- Teammate 4 (Agent-B5): Task 006 — Debug endpoints (owns: endpoint registration code)
Use Sonnet for each teammate. File ownership boundaries enforced.

# Example: Phase 2 parallel execution with 5 teammates
Create an agent team for Phase 2 Caching (after A1 complete):
- Teammate 1: Task 010 — Feature flag infra (owns: Infrastructure/ModuleState.cs, Api/Filters/ModuleGateFilter.cs)
- Teammate 2: Task 012 — Graph cache infra (owns: Services/Caching/GraphMetadataCacheService.cs)
- Teammate 3: Task 014 — Auth data cache (owns: Services/Caching/AuthDataCacheService.cs)
- Teammate 4: Task 015 — Graph client pooling (owns: Infrastructure/Graph/GraphClientFactory.cs)
- Teammate 5: Task 016 — Request-scoped cache (owns: Infrastructure/RequestCache.cs extensions)
```

### Subagent Execution Pattern (for Task tool)

```
# Parallel execution via Task tool (subagents)
# In a single message, invoke multiple task-execute calls:

Task 1: skill=task-execute, args="tasks/001-a1-audit-program-cs.poml"
Task 2: skill=task-execute, args="tasks/004-c3-thread-safety-fixes.poml"
Task 3: skill=task-execute, args="tasks/005-c1-explicit-column-selection.poml"
Task 4: skill=task-execute, args="tasks/006-b5-debug-endpoint-removal.poml"

# Each subagent runs in its own context with full task-execute protocol
# Monitor via TaskOutput tool
```

---

## Dependency Graph

```
CRITICAL PATH (longest chain):
001 → 002 → 003 → 012 → 013 → [verify] → F1(040→041→050) → G2(053→054) → G3(055) → G4(056) → [final]

SECONDARY PATHS:
003 → 010 → 011 → [verify]
003 → 014 → [verify]
003 → 020 → 021 → [verify]
003 → 022 → 023 → [verify]
003 → 024 → 025 → [verify]
004 → 028 → [verify]
(none) → 026, 027, 029 → [verify]
(none) → 016 → [verify]
040 → 041 → 045, 046, 047 → 050
(none) → 043, 044, 048, 049 → [verify]
(none) → 052 → [verify]
```

## Critical Path Items

| Task | Why Critical | Risk |
|------|-------------|------|
| 001-003 | A1 decomposition blocks ALL Phase 2/3 work | HIGH — DI order changes |
| 040-041 | F1 VNet blocks ALL resource hardening + CI/CD IaC | MEDIUM — VNet CIDR planning |
| 053-055 | G2-G3 pipeline blocks deployment automation | LOW — independent of code |

## High-Risk Items

| Task | Risk | Mitigation |
|------|------|------------|
| 002-003 | DI registration order change breaks startup | Extract in exact order, test after each extraction |
| 004 | Thread-safety fix may change behavior | Add concurrent tests, verify under load |
| 021 | Queue migration may lose messages | Keep old queue active, idempotent handlers |
| 050 | Disabling public access may break connectivity | Test all services first, have rollback procedure |

---

## Status Legend

| Symbol | Meaning |
|--------|---------|
| 🔲 | Pending |
| 🔄 | In Progress |
| ✅ | Completed |
| ⏸️ | Blocked |
| ❌ | Failed / Needs Rework |
