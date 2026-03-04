# SDAP BFF API & Performance Enhancement — Implementation Plan

> **Version**: 1.0
> **Created**: 2026-03-04
> **Source**: [spec.md](spec.md)

## Architecture Context

### Discovered Resources

**ADRs (10 applicable)**:
- ADR-001: Minimal API + BackgroundService — `.claude/adr/ADR-001-minimal-api.md`
- ADR-003: Lean Authorization — `.claude/adr/ADR-003-authorization.md`
- ADR-004: Async Job Contract — `.claude/adr/ADR-004-job-contract.md`
- ADR-007: SpeFileStore Facade — `.claude/adr/ADR-007-spe-file-store.md`
- ADR-008: Endpoint Filters — `.claude/adr/ADR-008-endpoint-filters.md`
- ADR-009: Redis-First Caching — `.claude/adr/ADR-009-redis-caching.md`
- ADR-010: DI Minimalism — `.claude/adr/ADR-010-di-minimalism.md`
- ADR-013: AI Architecture — `.claude/adr/ADR-013-ai-architecture.md`
- ADR-015: AI Data Governance — `.claude/adr/ADR-015-ai-data-governance.md`
- ADR-019: ProblemDetails — `.claude/adr/ADR-019-problem-details.md`

**Patterns (12 applicable)**:
- `.claude/patterns/api/service-registration.md` — DI module pattern
- `.claude/patterns/api/endpoint-filters.md` — Authorization filter pattern
- `.claude/patterns/api/endpoint-definition.md` — Endpoint route group pattern
- `.claude/patterns/api/error-handling.md` — ProblemDetails pattern
- `.claude/patterns/api/background-workers.md` — Job handler pattern
- `.claude/patterns/api/resilience.md` — Polly retry/circuit breaker
- `.claude/patterns/caching/distributed-cache.md` — Redis cache-aside pattern
- `.claude/patterns/caching/request-cache.md` — Per-request dedup pattern
- `.claude/patterns/caching/token-cache.md` — OBO token cache pattern
- `.claude/patterns/dataverse/entity-operations.md` — Column selection, batching
- `.claude/patterns/ai/streaming-endpoints.md` — SSE streaming pattern
- `.claude/patterns/testing/integration-tests.md` — Integration test pattern

**Constraints (6 applicable)**:
- `.claude/constraints/api.md` — API development rules
- `.claude/constraints/data.md` — Dataverse query rules
- `.claude/constraints/jobs.md` — Background worker rules
- `.claude/constraints/ai.md` — AI feature rules
- `.claude/constraints/azure-deployment.md` — Deployment safety
- `.claude/constraints/testing.md` — Test rules

**Scripts (3 applicable)**:
- `scripts/Deploy-BffApi.ps1` — BFF API deployment
- `scripts/Run-LoadTest.ps1` — Load testing
- `scripts/maintenance/Clean-DevEnvironment.ps1` — Dev cleanup

**Existing Code Reference**:
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/` — 8 existing DI modules
- `src/server/api/Sprk.Bff.Api/Api/Filters/` — 17 endpoint filters
- `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeFileStore.cs` — Graph facade
- `src/server/api/Sprk.Bff.Api/Infrastructure/Resilience/` — Circuit breaker, retry policies
- `tests/Spaarke.ArchTests/` — 6 architecture compliance tests
- `infrastructure/bicep/modules/` — 14 Bicep modules

### Guiding Principles

1. **Modular monolith** — Invest in internal structure, not external separation
2. **No breaking changes** — External API contracts remain identical
3. **Independent deployability** — Each item is reversible in isolation
4. **Redis graceful degradation** — Cache unavailability degrades performance, not availability
5. **Organic cache warming** — No proactive warm-up; accept first-request latency

---

## Phase Breakdown

### Phase 1: Foundation (Week 1)

Must complete first — provides modular structure for all subsequent work.

| Task Group | Item | Deliverable | Effort | Dependencies |
|-----------|------|-------------|--------|-------------|
| **P1-A** | A1 | Program.cs decomposed into startup modules + DI modules | 6-8 hrs | None |
| **P1-B** | C3 | Thread-safety fixes in DataverseWebApiService | 3-4 hrs | None |
| **P1-C** | C1 | All ColumnSet(true) replaced with explicit columns | 4-6 hrs | None |
| **P1-D** | B5 | Debug endpoints removed from non-dev environments | 1-2 hrs | None |

**Parallel Opportunity**: P1-B, P1-C, P1-D can all execute in parallel. P1-A (A1) is critical path — blocks Phase 2.

### Phase 2: Caching (Week 2)

Highest user-visible impact. Depends on A1 modular startup.

| Task Group | Item | Deliverable | Effort | Dependencies |
|-----------|------|-------------|--------|-------------|
| **P2-A** | A2 | Domain feature flags (ModuleState, ModuleGateFilter) | 4-6 hrs | A1 |
| **P2-B** | B1 | Graph metadata Redis cache with ETag-versioned keys | 8-10 hrs | A1 |
| **P2-C** | B2 | Authorization data snapshot Redis cache | 6-8 hrs | A1 |
| **P2-D** | B3 | GraphServiceClient singleton pooling | 2-3 hrs | A1 |
| **P2-E** | B4 | Request-scoped document metadata cache | 2-3 hrs | None |

**Parallel Opportunity**: All P2 items can execute in parallel once A1 is complete. B4 can even start during Phase 1.

### Phase 3: Resilience & AI Performance (Weeks 3-4)

Can start items with no A1 dependency in parallel with Phase 2.

| Task Group | Item | Deliverable | Effort | Dependencies |
|-----------|------|-------------|--------|-------------|
| **P3-A** | D1 | Priority-based job queues (3 tiers) | 8-10 hrs | A1 |
| **P3-B** | D2 | Persistent analysis state (Redis + Dataverse) | 6-8 hrs | A1 |
| **P3-C** | D3 | Request-scoped resource budgets | 6-8 hrs | A1 |
| **P3-D** | E1 | Parallel read-only tool execution | 4-6 hrs | None |
| **P3-E** | E2 | Batch embedding API | 4-6 hrs | None |
| **P3-F** | C2 | Dataverse request batching ($batch) | 6-8 hrs | C3 |
| **P3-G** | C4 | Pagination support for unbounded queries | 3-4 hrs | None |

**Parallel Opportunity**: P3-D, P3-E, P3-G can start alongside Phase 2. P3-A, P3-B, P3-C can run in parallel once A1 done. P3-F requires C3.

### Phase 4: Infrastructure (Weeks 4-6)

Independent of code changes. Can start F2/F3/F7/F8 in parallel with Phase 3.

| Task Group | Item | Deliverable | Effort | Dependencies |
|-----------|------|-------------|--------|-------------|
| **P4-A** | F1 | VNet + private endpoints + NSG rules | 16-20 hrs | Bicep templates |
| **P4-B** | F2 | App Service autoscaling rules | 4-6 hrs | None |
| **P4-C** | F3 | Deployment slot configuration | 3-4 hrs | None |
| **P4-D** | F4 | Redis production hardening | 3-4 hrs | F1 |
| **P4-E** | F5-F6 | Key Vault + Storage hardening | 4-6 hrs | F1 |
| **P4-F** | F7 | OpenAI capacity planning | 3-4 hrs | None |
| **P4-G** | F8 | AI Search cleanup | 2-3 hrs | None |

**Parallel Opportunity**: P4-B, P4-C, P4-F, P4-G can all start immediately. P4-D, P4-E require F1.

### Phase 5: CI/CD (Weeks 5-6)

Builds on infrastructure from Phase 4.

| Task Group | Item | Deliverable | Effort | Dependencies |
|-----------|------|-------------|--------|-------------|
| **P5-A** | G1 | Test suite re-enabled and gating deployments | 4-6 hrs | None |
| **P5-B** | G2 | IaC deployment pipeline with Bicep | 6-8 hrs | F1 |
| **P5-C** | G3 | Environment promotion (dev → staging → prod) | 4-6 hrs | G2 |
| **P5-D** | G4 | Deployment slot integration in CI/CD | 2-3 hrs | F3, G3 |

**Parallel Opportunity**: P5-A can start anytime. P5-B requires F1.

---

## Parallel Execution Strategy

### Agent Team Task Groups

These groups identify tasks that can be executed **simultaneously by separate Claude Code agents** (subagents or teammates).

```
PHASE 1 PARALLELISM
═══════════════════
Wave 1.1: [A1-audit, C3, C1, B5]          ← 4 agents, all independent
Wave 1.2: [A1-extract-startup]             ← sequential (depends on audit)
Wave 1.3: [A1-extract-di-finalize]         ← sequential (depends on extract)

PHASE 2 PARALLELISM (after A1 complete)
═══════════════════
Wave 2.1: [A2, B1-infra, B2, B3, B4]      ← 5 agents, all independent after A1
Wave 2.2: [A2-integration, B1-impl]        ← depends on Wave 2.1

PHASE 3 PARALLELISM (after A1 + C3)
═══════════════════
Wave 3.1: [E1, E2, C4]                    ← 3 agents, no code deps (can start w/ Phase 2)
Wave 3.2: [D1-infra, D2-redis, D3-budget] ← 3 agents, after A1
Wave 3.3: [C2-batch]                       ← after C3
Wave 3.4: [D1-routing, D2-dv, D3-filter]  ← depends on Wave 3.2

PHASE 4 PARALLELISM (infrastructure)
═══════════════════
Wave 4.1: [F1-vnet, F2, F3, F7, F8]       ← 5 agents, independent
Wave 4.2: [F1-pe, F1-nsg]                 ← after F1-vnet
Wave 4.3: [F4, F5, F6]                    ← after F1 complete

PHASE 5 PARALLELISM
═══════════════════
Wave 5.1: [G1, G2]                        ← 2 agents (G1 independent, G2 after F1)
Wave 5.2: [G3]                            ← after G2
Wave 5.3: [G4]                            ← after F3 + G3
```

### Critical Path

```
A1 (6-8h) → B1 (8-10h) → [integration testing] → F1 (16-20h) → G2 (6-8h) → G3 (4-6h) → G4 (2-3h)
Total critical path: ~45-55 hours (minimum calendar time)
```

### File Ownership Rules for Parallel Execution

| Agent Scope | Owns These Files | Must Not Touch |
|-------------|-----------------|----------------|
| A1 agent | Program.cs, Infrastructure/Startup/*, Infrastructure/DI/* | Services/*, Api/* |
| B1 agent | Infrastructure/Graph/SpeFileStore.cs cache layer | Program.cs, Filters/* |
| B2 agent | Services/AuthorizationService.cs cache layer | SpeFileStore.cs |
| C1 agent | Services/DataverseService.cs, Services/DataverseWebApiService.cs columns | Threading code |
| C3 agent | Services/DataverseWebApiService.cs thread safety | Column selection |
| D1 agent | Workers/ServiceBusJobProcessor.cs, Services/Jobs/* | AI services |
| E1 agent | Services/Ai/Chat/Tools/* | Job handlers |
| F1 agent | infrastructure/bicep/* | src/ code |

---

## Risk Mitigation

| Risk | Mitigation | Owner |
|------|-----------|-------|
| DI registration order break | Extract in exact order + full test suite | A1 task |
| Cache staleness | ETag-versioned keys, short TTLs (2-5 min) | B1/B2 tasks |
| Queue migration message loss | Keep old queue active, idempotent handlers | D1 task |
| VNet disruption | Deploy private endpoints alongside public first | F1 task |
| Resource budgets too restrictive | Warn-only mode first, 2-week observation | D3 task |

---

## References

- [Design Document](design.md)
- [AI Specification](spec.md)
- [BFF API Module Guide](../../src/server/api/Sprk.Bff.Api/CLAUDE.md)
- [SDAP Architecture](../../docs/architecture/sdap-overview.md)
- [BFF API Patterns](../../docs/architecture/sdap-bff-api-patterns.md)
- [AI Architecture Guide](../../docs/guides/SPAARKE-AI-ARCHITECTURE.md)
- [Azure Resources](../../docs/architecture/auth-azure-resources.md)
