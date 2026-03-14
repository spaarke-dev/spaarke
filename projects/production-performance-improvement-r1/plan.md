# Production Performance Improvement R1 — Implementation Plan

> **Last Updated**: 2026-03-11
> **Status**: Planning
> **Specification**: [spec.md](spec.md)

## Executive Summary

### Purpose
Prepare the Spaarke platform for beta user access by resolving security gaps, optimizing AI and API performance, cleaning up accumulated technical debt, and upgrading infrastructure to production-grade tiers.

### Scope
- 7 domains (A-G): BFF caching, Dataverse queries, Infrastructure, CI/CD, AI pipeline, Code quality, Logging
- 30+ deliverables across 6 phases
- Target: 15-50 concurrent beta users, ~200 analyses/day

### Estimated Effort
- Phase 1 (Beta Blockers): ~3-4 days
- Phase 2 (Quick Perf Wins): ~2-3 days
- Phase 3 (Core Caching): ~4-5 days
- Phase 4 (Code Quality & Logging): ~4-5 days
- Phase 5 (Infrastructure Hardening): ~3-4 days
- Phase 6 (CI/CD & Refactoring): ~4-5 days
- **Total**: ~20-26 days estimated

## Architecture Context

### Design Constraints (from ADRs)

| ADR | Constraint | Domains Affected |
|-----|-----------|-----------------|
| ADR-001 | No Azure Functions; single App Service runtime | C, D |
| ADR-003 | Cache auth **data** not **decisions**; UAC snapshots per-request only | A |
| ADR-007 | All Graph/SPE operations through SpeFileStore facade | A, E |
| ADR-008 | Endpoint filters for resource authorization; no global middleware | F |
| ADR-009 | Redis-first (`IDistributedCache`); no L1 without profiling proof; ETag-versioned keys | A, E |
| ADR-010 | ≤15 non-framework DI registrations; register concretes | F |
| ADR-013 | Extend BFF for AI; no separate microservice; rate limit AI endpoints | E |
| ADR-015 | Log only identifiers/sizes/timings; never document content or prompts | G |
| ADR-019 | BFF API patterns (Minimal API, endpoint groups) | F |

### Key Technical Decisions

| Decision | Approach | Rationale |
|----------|----------|-----------|
| Caching layer | Redis via `IDistributedCache` | ADR-009; proven pattern in GraphTokenCache |
| Cache key versioning | ETag/rowversion in key suffix | Auto-invalidation without explicit purge |
| Auth security (F1) | Restore `.RequireAuthorization()` with MSAL | Same mechanism as all BFF endpoints |
| Obsolete handlers (F3) | Remove immediately | JPS replacements verified; 217 tests passing |
| Workspace services (F5) | Implement real Dataverse queries | Beta users need real data |
| God class refactor (F4) | Defer to Phase 6 (post-beta) | Low urgency; high effort |

### Discovered Resources

**Constraint Files**:
- `.claude/constraints/api.md` — BFF API constraints
- `.claude/constraints/auth.md` — Authorization patterns
- `.claude/constraints/data.md` — Dataverse query patterns
- `.claude/constraints/ai.md` — AI architecture constraints
- `.claude/constraints/jobs.md` — Background job patterns
- `.claude/constraints/azure-deployment.md` — Azure deployment rules
- `.claude/constraints/testing.md` — Testing requirements

**Pattern Files**:
- `.claude/patterns/api/distributed-cache.md` — Redis caching patterns
- `.claude/patterns/api/token-cache.md` — OBO token cache pattern
- `.claude/patterns/api/request-cache.md` — Per-request dedup pattern
- `.claude/patterns/api/resilience.md` — Polly resilience patterns
- `.claude/patterns/api/endpoint-filters.md` — Authorization filter pattern
- `.claude/patterns/api/streaming-endpoints.md` — AI streaming pattern
- `.claude/patterns/api/text-extraction.md` — Document text extraction
- `.claude/patterns/api/error-handling.md` — ProblemDetails pattern
- `.claude/patterns/api/service-registration.md` — DI registration pattern

**Canonical Implementations**:
- `Services/GraphTokenCache.cs` — Production-proven Redis cache pattern
- `Services/Ai/EmbeddingCache.cs` — AI embedding cache pattern
- `Infrastructure/Caching/CacheMetrics.cs` — Cache telemetry pattern
- `Infrastructure/Caching/RequestCache.cs` — Per-request dedup
- `Infrastructure/Caching/DistributedCacheExtensions.cs` — Cache helper methods
- `Infrastructure/Graph/GraphHttpMessageHandler.cs` — Graph HTTP handler
- `Infrastructure/Http/ResilientHttpHandler.cs` — Polly resilience

**Scripts**:
- `scripts/Test-SdapBffApi.ps1` — API health and endpoint testing
- `scripts/test-sdap-api-health.js` — Health check script
- `scripts/Deploy-PCFWebResources.ps1` — PCF deployment (if needed)

**Applicable Skills**:
- `azure-deploy` — Azure infrastructure deployment
- `code-review` — Code review checklist
- `adr-check` — ADR compliance validation
- `adr-aware` — Auto-load ADRs (always-apply)
- `script-aware` — Reuse existing scripts (always-apply)
- `spaarke-conventions` — Naming conventions (always-apply)

## Implementation Approach

### Phase Structure

```
Phase 1: Beta Blockers          ┃ MUST complete before any user access
  C0, F1, F2, B3, F6           ┃ Security + correctness + infrastructure
                                ┃
Phase 2: Quick Perf Wins        ┃ Highest impact, lowest risk
  E1, E3, E4, B1, A3, A4, G2   ┃ AI pipeline + Dataverse + cleanup
                                ┃
Phase 3: Core Caching           ┃ Highest impact, moderate effort
  E2, A1, A2, E5, B2, B4       ┃ Redis caching across all layers
                                ┃
Phase 4: Code Quality & Logging ┃ Maintainability + observability
  G1, G3-G5, F3, F5, C8        ┃ Dead code removal + real Workspace queries
                                ┃
Phase 5: Infrastructure         ┃ Post-beta security hardening
  C1-C7                         ┃ VNet + autoscaling + service hardening
                                ┃
Phase 6: CI/CD & Refactoring    ┃ Long-term maturity
  D1-D4, F4                     ┃ Test gates + IaC + god class refactor
```

### Critical Path

1. **C0 (Bicep tiers)** → Blocks all beta access (system fails at 10+ users)
2. **F1 (Secure endpoints)** → Blocks beta access (security blocker)
3. **F2 (Auth filters)** → Blocks beta access (requires Task 033 design first)
4. **B3 (Thread-safety)** → Blocks beta access (correctness under concurrent load)
5. **E1+E2+E3 (AI pipeline)** → Primary user complaint; must show improvement early

### High-Risk Items

- **F2 (37 auth filters)**: Requires Task 033 filter design; may break Office Add-in integrations
- **F3 (Remove obsolete handlers)**: Must verify all JPS configs functional before deletion
- **F5 (Mock → real Workspace)**: Requires Dataverse schema discovery for field names
- **C1 (VNet)**: Network isolation may disrupt service connectivity if misconfigured

## Phase Breakdown

### Phase 1: Beta Blockers (Must Complete Before User Access)

**Objectives**:
1. Correct infrastructure tiers to support beta load
2. Secure all unauthenticated API endpoints
3. Implement missing authorization filters
4. Fix thread-safety correctness issues
5. Apply production safety fixes

**Deliverables**:
- [ ] C0: Bicep tier corrections (B1→S1, Basic Redis→Standard C1, basic→standard AI Search)
- [ ] F1: Restore `.RequireAuthorization()` on 5 AI endpoint groups
- [ ] F2: Implement 37 authorization filters in OfficeEndpoints (depends on Task 033 design)
- [ ] B3: Fix DataverseWebApiService thread-safety (SemaphoreSlim + per-request headers)
- [ ] F6: Production safety (CORS fail-fast, Console.WriteLine→ILogger, debug endpoint exclusion)

**Inputs**: spec.md, ADR-008 (endpoint filters), ADR-009 (Redis tiers), Bicep templates
**Outputs**: Secured endpoints, corrected Bicep, thread-safe Dataverse client

---

### Phase 2: Quick Performance Wins

**Objectives**:
1. Reduce AI analysis time by 5-13 seconds through parallelization and timeouts
2. Reduce Dataverse query payloads by 60-80%
3. Remove development artifacts from production paths

**Deliverables**:
- [ ] E1: Parallelize RAG knowledge searches (sequential → Task.WhenAll)
- [ ] E3: Add Document Intelligence 30s timeout + circuit breaker
- [ ] E4: Tune OpenAI parameters (MaxOutputTokens, per-action model selection)
- [ ] B1: Replace all ColumnSet(true) with explicit column lists (6 methods)
- [ ] A3: GraphServiceClient singleton pooling for app-only operations
- [ ] A4: Debug endpoint removal from non-dev environments
- [ ] G2: Remove [DEBUG] log tags (7 instances)

**Inputs**: AnalysisOrchestrationService.cs, TextExtractorService.cs, OpenAiClient.cs, DataverseServiceClientImpl.cs
**Outputs**: Faster AI pipeline, leaner Dataverse queries, cleaner production code

---

### Phase 3: Core Caching

**Objectives**:
1. Eliminate repeat Document Intelligence calls via text caching
2. Implement Graph metadata caching for 90%+ latency reduction
3. Cache authorization data snapshots
4. Cache RAG search results for repeat analyses
5. Implement Dataverse request batching

**Deliverables**:
- [ ] E2: Cache extracted document text in Redis (docId + ETag, 24h TTL)
- [ ] A1: Graph metadata cache (file metadata, folder listings, container-to-drive, permissions)
- [ ] A2: Authorization data snapshot cache (roles, teams, resource access)
- [ ] E5: Cache RAG search results (doc+query composite key, 15-min TTL)
- [ ] B2: Implement $batch for multi-query operations (scorecard, field mapping, multi-entity auth)
- [ ] B4: Add pagination support for unbounded queries (paging cookie)

**Inputs**: ADR-009 patterns, GraphTokenCache.cs (canonical), EmbeddingCache.cs, CacheMetrics.cs
**Outputs**: Redis caching across all layers, dramatically reduced latency

---

### Phase 4: Code Quality, Logging & Workspace

**Objectives**:
1. Guard all JSON serialization in log calls for 10-20% overhead reduction
2. Batch loop logging and tune production log levels
3. Remove ~5,500 lines of obsolete handler code
4. Implement real Dataverse queries for 3 mock Workspace services
5. Clean up deprecated AI Search index

**Deliverables**:
- [ ] G1: Guard JsonSerializer.Serialize() log calls with IsEnabled() checks (100 files)
- [ ] G3: Move per-item loop logging to batch summaries (20 files)
- [ ] G4: Add per-service log level configuration for production
- [ ] G5: Remove string allocations from log parameters
- [ ] F3: Remove 6 obsolete tool handlers (~5,500 lines) + DI registrations
- [ ] F5: Implement real Dataverse queries for PortfolioService, WorkspaceAiService, TodoGenerationService
- [ ] C8: Delete deprecated AI Search index (spaarke-knowledge-index)

**Inputs**: Log audit results, JPS verification, Dataverse entity schemas
**Outputs**: Cleaner codebase, real Workspace data, better logging performance

---

### Phase 5: Infrastructure Hardening

**Objectives**:
1. Establish network isolation for production security
2. Configure autoscaling for load handling
3. Set up deployment slots for zero-downtime deploys
4. Harden individual service configurations

**Deliverables**:
- [ ] C1: VNet creation with subnets + private endpoints for all services
- [ ] C2: App Service autoscaling rules (CPU, memory, HTTP queue)
- [ ] C3: Deployment slot configuration (staging + health check + swap)
- [ ] C4: Redis VNet injection + RDB persistence
- [ ] C5: Key Vault network restriction + secret rotation
- [ ] C6: Storage account shared key disablement + lifecycle policies
- [ ] C7: OpenAI network restriction + capacity planning

**Inputs**: Bicep templates, Azure documentation, ADR-001 (no Azure Functions)
**Outputs**: Hardened production infrastructure

---

### Phase 6: CI/CD Maturity & Refactoring

**Objectives**:
1. Re-enable test suite as deployment gate
2. Add infrastructure-as-code deployment to pipeline
3. Establish environment promotion with approval gates
4. Refactor ScopeResolverService god class (post-beta)

**Deliverables**:
- [ ] D1: Re-enable test suite as deployment gate
- [ ] D2: Bicep deployment with parameter files (dev, staging, prod)
- [ ] D3: Environment promotion (dev → staging → prod) with approval gates
- [ ] D4: Deployment slot swap integration in CI/CD
- [ ] F4: Refactor ScopeResolverService (2,538 lines → 5 focused services)

**Inputs**: .github/workflows/, Bicep templates, ScopeResolverService.cs
**Outputs**: Mature CI/CD pipeline, maintainable AI services

## Dependencies

### External Dependencies
- Azure subscription with capacity for Standard Redis, Standard AI Search, S1 App Service
- Azure OpenAI TPM verification and potential increase (120 → 200+)
- Budget approval for ~$620/month infrastructure cost increase

### Internal Dependencies
- Task 033 design (Office auth filters) — blocks F2
- Dataverse entity schema discovery — blocks F5
- JPS deployment verification — prerequisite for F3

## Testing Strategy

### Unit Tests
- All new caching services must have unit tests
- Thread-safety fixes verified via concurrent test scenarios
- Obsolete handler removal verified by existing 217 JPS tests passing

### Integration Tests
- Cache hit/miss scenarios for each cache layer
- Authorization filter enforcement per endpoint
- Dataverse batch request verification
- AI pipeline end-to-end with cached vs uncached paths

### Manual Verification
- Beta user scenario testing (15-50 concurrent users)
- Office Add-in compatibility after auth filter implementation
- Portfolio dashboard real data verification

## Acceptance Criteria

### Technical Acceptance
- All 22 success criteria (SC-01 through SC-22) pass
- Zero security vulnerabilities (unauthenticated endpoints, missing auth)
- All ADR constraints satisfied (verified by adr-check skill)

### Business Acceptance
- Beta users can access system without errors
- AI analysis perceived as noticeably faster on repeat runs
- Portfolio and Workspace show real user data

## Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|--------|------------|
| R1 | Auth filter changes break Office integrations | Medium | High | Staged rollout; test each endpoint with Office Add-in |
| R2 | Obsolete handler removal breaks analysis | Medium | High | Verify JPS configs; keep code in git history |
| R3 | Infrastructure costs exceed budget | High | Medium | ~$620/mo approved; caching provides offsetting savings |
| R4 | Cache staleness causes stale data | Medium | Medium | Short TTLs, ETag-versioned keys |
| R5 | VNet migration disrupts services | Medium | High | Deploy private endpoints alongside public first |
| R6 | OpenAI rate limits at beta scale | Medium | High | Verify TPM; implement client-side retry |
| R7 | Dataverse schema mismatch for Workspace queries | Medium | Medium | Discovery task before F5 implementation |

## Next Steps

1. Review this plan for completeness and accuracy
2. Task files will be generated via task-create
3. Feature branch created for isolation
4. Begin Phase 1 execution

---

*Implementation plan for Production Performance Improvement R1. Source: [spec.md](spec.md)*
