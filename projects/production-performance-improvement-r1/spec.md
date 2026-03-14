# Production Performance Improvement R1 - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-11
> **Source**: design.md (v2.0)

## Executive Summary

The Spaarke platform requires comprehensive production-readiness improvements before beta user access. The BFF API exhibits 250ms–1,200ms response times for file operations due to missing caches, the AI analysis pipeline takes 45+ seconds due to sequential processing and no extracted-text caching, and the infrastructure defaults are development-grade (Basic Redis, B1 App Service, basic AI Search). Additionally, 5 API endpoint groups are unauthenticated, ~5,500 lines of obsolete handler code remain in DI, 3 Workspace services return mock data, and 100 files contain unguarded JSON serialization in log calls.

This project delivers 7 domains of work across 6 phases targeting: 60–80% reduction in API response times, 40–60% reduction in AI analysis times on repeat analyses, resolution of all critical security gaps, real Workspace data queries, and production-grade infrastructure.

**Beta scale target**: 15–50 concurrent users, ~200 analyses/day.

## Scope

### In Scope

**Domain A — BFF API Caching & Connection Optimization**
- A1. Graph metadata response caching in Redis (file metadata, folder listings, container-to-drive mappings, permissions)
- A2. Authorization data snapshot caching (user roles, team memberships, resource access)
- A3. GraphServiceClient singleton pooling for app-only operations
- A4. Debug endpoint removal from non-development environments

**Domain B — Dataverse Query & Access Optimization**
- B1. Replace all `ColumnSet(true)` with explicit column sets (6 methods)
- B2. Implement `$batch` for multi-query operations (scorecard, field mapping, multi-entity auth)
- B3. Fix thread-safety issues in `DataverseWebApiService` (token refresh race, mutable headers)
- B4. Add pagination support for unbounded queries (paging cookie)

**Domain C — Azure Infrastructure Production Readiness**
- C0. Correct Bicep tier defaults (B1→S1, Basic Redis→Standard C1, basic AI Search→standard with 2 replicas)
- C1. VNet creation with private endpoints (post-beta)
- C2. App Service autoscaling rules (CPU, memory, HTTP queue)
- C3. Deployment slot configuration (staging + health check + swap)
- C4. Redis VNet injection and RDB persistence
- C5–C7. Key Vault, Storage, OpenAI hardening
- C8. Delete deprecated AI Search index

**Domain D — CI/CD & Deployment Pipeline**
- D1. Re-enable test suite as deployment gate
- D2. Bicep infrastructure deployment with parameter files
- D3. Environment promotion (dev → staging → prod) with approval gates
- D4. Deployment slot swap integration

**Domain E — AI Pipeline Optimization**
- E1. Parallelize RAG knowledge source searches (`Task.WhenAll`)
- E2. Cache extracted document text in Redis (by docId + ETag)
- E3. Add Document Intelligence timeout (30s) and circuit breaker
- E4. Tune OpenAI parameters (MaxOutputTokens, per-action model selection)
- E5. Cache RAG search results (doc+query composite key, 15-min TTL)

**Domain F — Code Quality & Production Readiness**
- F1. Restore `.RequireAuthorization()` on 5 AI endpoint groups (MSAL bearer auth)
- F2. Implement 37 missing authorization filters in OfficeEndpoints (Task 033)
- F3. Remove 6 obsolete tool handlers (~5,500 lines) — JPS replacements verified complete
- F4. Refactor ScopeResolverService god class (2,538 lines, 39 methods → 5 focused services)
- F5. Implement real Dataverse queries for 3 mock Workspace services (PortfolioService, WorkspaceAiService, TodoGenerationService)
- F6. Production safety fixes (CORS fail-fast, Console.WriteLine→ILogger, debug endpoint exclusion, scorecard auth)

**Domain G — Logging & Observability Optimization**
- G1. Guard all `JsonSerializer.Serialize()` log calls with `IsEnabled()` checks (100 files)
- G2. Remove `[DEBUG]` tags from production log messages (7 instances)
- G3. Move per-item loop logging to batch summaries (20 files)
- G4. Add per-service log level configuration for production
- G5. Remove string allocations from log parameters

### Out of Scope

- PCF React 16 remediation (separate project: `pcf-react-16-remediation`)
- Full VNet hub-spoke topology for multi-region deployment
- Azure Front Door / CDN integration
- Database-level Dataverse optimization (index creation requires Microsoft support)
- New feature development beyond completing mock Workspace services
- Load testing / performance benchmarking (recommended as follow-up project)

### Affected Areas

- `src/server/api/Sprk.Bff.Api/` — Primary target (~75,000 lines, 524 files)
  - `Infrastructure/Graph/` — SpeFileStore, GraphClientFactory, DriveItemOperations (Domains A, E)
  - `Services/Ai/` — AnalysisOrchestrationService, TextExtractorService, ScopeResolverService, OpenAiClient, obsolete handlers (Domains E, F)
  - `Services/Workspace/` — PortfolioService, WorkspaceAiService, TodoGenerationService (Domain F)
  - `Services/Communication/` — IncomingAssociationResolver logging (Domain G)
  - `Api/Ai/` — Playbook, Node, PlaybookRun endpoints auth (Domain F)
  - `Api/Office/` — OfficeEndpoints authorization filters (Domain F)
  - `Program.cs` — Console.WriteLine, CORS fallback, DI registrations (Domain F)
  - `Configuration/` — DocumentIntelligenceOptions, appsettings.json (Domains E, G)
- `infrastructure/bicep/` — Bicep templates and parameter files (Domain C)
  - `stacks/model2-full.bicep` — Tier defaults
  - `modules/` — app-service, redis, ai-search, openai modules
- `.github/workflows/` — CI/CD pipeline (Domain D)

## Requirements

### Functional Requirements

1. **FR-01**: Graph metadata (file name/size/ETag, folder listings, container-to-drive mappings) MUST be cached in Redis with ETag-versioned keys and TTLs ≤15 min. — Acceptance: Cache hit rate >85% measured via Redis metrics.
2. **FR-02**: Authorization data snapshots (user roles, team memberships) MUST be cached in Redis with ≤2 min TTL, while authorization decisions are computed fresh per request. — Acceptance: Authorization overhead drops from 50–200ms to <10ms on cache hit.
3. **FR-03**: All `ColumnSet(true)` MUST be replaced with explicit column lists. All WebAPI queries MUST include `$select`. — Acceptance: Zero `ColumnSet(true)` instances in production code.
4. **FR-04**: `DataverseWebApiService` MUST use `SemaphoreSlim` for token refresh and per-request `HttpRequestMessage` headers instead of mutable `DefaultRequestHeaders`. — Acceptance: No thread-safety issues under concurrent load testing.
5. **FR-05**: RAG knowledge source searches MUST execute in parallel (`Task.WhenAll`) instead of sequentially. — Acceptance: 3-source RAG search completes in ~3s instead of ~9s.
6. **FR-06**: Extracted document text MUST be cached in Redis keyed by `{driveId}:{itemId}:v{etag}` with 24-hour TTL. — Acceptance: Repeat analysis of same document skips Document Intelligence (cache hit rate >80%).
7. **FR-07**: Document Intelligence calls MUST have a 30-second timeout with graceful degradation message on timeout. — Acceptance: No analysis request blocks indefinitely.
8. **FR-08**: All 5 AI endpoint groups (`/api/ai/playbooks`, `/api/ai/playbook-builder`, `/api/ai/playbooks/{id}/nodes`, playbook runs) MUST require MSAL bearer token authentication. — Acceptance: Zero `.AllowAnonymous()` on production endpoints.
9. **FR-09**: 37 Office authorization filter TODOs MUST be implemented using endpoint filters per ADR-008. — Acceptance: Zero `TODO: Task 033` comments remain.
10. **FR-10**: 6 obsolete tool handlers (ClauseAnalyzer, DateExtractor, EntityExtractor, FinancialCalculator, RiskDetector, ClauseComparison) MUST be removed from codebase and DI registrations. — Acceptance: Zero `[Obsolete]` handlers compiled. JPS replacements verified functional.
11. **FR-11**: PortfolioService MUST return real Dataverse data (matters, spend, budget, health metrics) for the authenticated user. — Acceptance: Portfolio dashboard shows user's actual matters.
12. **FR-12**: WorkspaceAiService MUST fetch real entity descriptions from Dataverse instead of hardcoded strings. — Acceptance: AI Summary dialog shows real entity data.
13. **FR-13**: TodoGenerationService rules 2, 4, 5 (budget alerts, pending invoices, assigned tasks) MUST execute real Dataverse queries. — Acceptance: All 5 to-do generation rules produce results from real data.
14. **FR-14**: All `JsonSerializer.Serialize()` calls in log statements MUST be guarded with `_logger.IsEnabled()` checks. — Acceptance: Zero unguarded serialization in log calls.
15. **FR-15**: Bicep Model 2 defaults MUST be production-grade: App Service ≥S1, Redis ≥Standard C1, AI Search ≥standard with ≥2 replicas. — Acceptance: Bicep parameter audit passes.

### Non-Functional Requirements

- **NFR-01**: File listing response time (cached) < 50ms p95.
- **NFR-02**: Document download response time < 600ms p95.
- **NFR-03**: Dataverse entity read response time < 100ms p95.
- **NFR-04**: AI analysis time (repeat, cached text) < 30s p95.
- **NFR-05**: AI analysis time (first run, small file) < 45s p95.
- **NFR-06**: System MUST support 15–50 concurrent users and ~200 AI analyses/day at beta scale.
- **NFR-07**: Redis cache MUST survive instance restarts (RDB persistence on Standard+ tier).
- **NFR-08**: Zero-downtime deployments via staging slot swap.
- **NFR-09**: CI/CD pipeline MUST run tests as a deployment gate.

## Technical Constraints

### Applicable ADRs

- **ADR-001**: Minimal API + BackgroundService — No Azure Functions. Single App Service runtime.
- **ADR-002**: Thin plugins — <200 LoC, <50ms p95. No HTTP/Graph calls from plugins.
- **ADR-003**: Authorization seams — Cache data not decisions. UAC snapshots per-request only.
- **ADR-007**: SpeFileStore facade — No Graph SDK types above facade. All SPE operations through facade.
- **ADR-008**: Endpoint filters — No global auth middleware. Resource checks via endpoint filters.
- **ADR-009**: Redis-first caching — `IDistributedCache` for cross-request. No L1 cache without profiling.
- **ADR-010**: DI minimalism — ≤15 non-framework registrations. Register concretes, not interfaces.
- **ADR-013**: AI Architecture — Extend BFF, not separate service. Rate limit AI endpoints.
- **ADR-015**: AI Data Governance — Log only identifiers/sizes/timings, not document content or prompts.

### MUST Rules (from ADRs)

- ✅ MUST use `IDistributedCache` (Redis) for all cross-request caching
- ✅ MUST version cache keys with ETag/rowversion
- ✅ MUST cache authorization **data** (roles, teams), compute **decisions** fresh
- ✅ MUST route all Graph/SPE operations through `SpeFileStore` facade
- ✅ MUST use endpoint filters for resource authorization (not global middleware)
- ✅ MUST scope all cache keys by tenant
- ✅ MUST use feature module DI extensions (`AddSpaarkeCore`, `AddDocumentsModule`, etc.)
- ✅ MUST register concretes by default, interfaces only for genuine seams
- ✅ MUST apply rate limiting to AI endpoints
- ✅ MUST log only identifiers, sizes, timings — never document content or prompts
- ❌ MUST NOT add L1 (in-memory) cache without profiling proof
- ❌ MUST NOT cache authorization decisions across requests
- ❌ MUST NOT inject GraphServiceClient outside SpeFileStore
- ❌ MUST NOT create separate AI microservice
- ❌ MUST NOT allow unbounded `Task.WhenAll` on throttled services (add semaphore)
- ❌ MUST NOT cache streaming tokens
- ❌ MUST NOT use Azure Functions

### Existing Patterns to Follow

- See `Services/GraphTokenCache.cs` for Redis caching pattern (OBO token cache — production-proven)
- See `Api/FileAccessEndpoints.cs` for endpoint filter authorization pattern
- See `Services/Ai/Handlers/GenericAnalysisHandler.cs` for JPS-based analysis (replaces obsolete handlers)
- See `.claude/patterns/api/` for endpoint, caching, and DI patterns
- See `.claude/constraints/` for per-topic MUST/MUST NOT rules

## Success Criteria

1. [ ] **SC-01**: File listing response time (cached) < 50ms p95 — Verify: Application Insights
2. [ ] **SC-02**: Document download response time < 600ms p95 — Verify: Application Insights
3. [ ] **SC-03**: Dataverse entity read response time < 100ms p95 — Verify: Application Insights
4. [ ] **SC-04**: Graph metadata cache hit rate > 85% — Verify: Redis metrics + custom telemetry
5. [ ] **SC-05**: Zero `ColumnSet(true)` in production code — Verify: Code search
6. [ ] **SC-06**: All Azure services behind private endpoints — Verify: Azure Resource Graph (post-beta)
7. [ ] **SC-07**: Autoscaling configured and tested — Verify: Scale-out triggers verified
8. [ ] **SC-08**: Deployment slots operational — Verify: Staging → prod swap demonstrated
9. [ ] **SC-09**: CI/CD runs tests before deployment — Verify: Pipeline logs
10. [ ] **SC-10**: No thread-safety issues in Dataverse client — Verify: Code review + load test
11. [ ] **SC-11**: No debug endpoints in non-dev builds — Verify: Environment-conditional build
12. [ ] **SC-12**: AI analysis time (repeat, cached text) < 30s p95 — Verify: Application Insights
13. [ ] **SC-13**: AI analysis time (first run, small file) < 45s p95 — Verify: Application Insights
14. [ ] **SC-14**: Zero unauthenticated endpoints in production — Verify: Code search for `.AllowAnonymous()`
15. [ ] **SC-15**: All authorization filters implemented — Verify: Zero `TODO Task 033`
16. [ ] **SC-16**: Zero obsolete handlers in DI — Verify: Code review
17. [ ] **SC-17**: Zero unguarded JSON serialization in logs — Verify: Code search
18. [ ] **SC-18**: Bicep defaults production-grade — Verify: Bicep parameter audit
19. [ ] **SC-19**: Document text cache hit rate > 80% — Verify: Redis metrics
20. [ ] **SC-20**: Zero `Console.WriteLine` in production code — Verify: Code search
21. [ ] **SC-21**: Portfolio dashboard shows real user data — Verify: Manual test with beta user
22. [ ] **SC-22**: All 5 to-do generation rules produce results — Verify: Background job logs

## Dependencies

### Prerequisites

- Redis production instance available via Bicep (**requires tier upgrade from Basic to Standard**)
- App Service plan (**requires upgrade from B1 to S1**)
- AI Search (**requires upgrade from basic to standard**)
- Azure OpenAI capacity (**requires TPM verification — currently 120 in Bicep, need ≥200**)
- JPS migration complete for all 5 analysis types (**verified: all JPS configs exist, 217 tests passing**)

### External Dependencies

- ADR-009 (Redis-first caching) — Approved, provides caching patterns
- ADR-003 (Authorization) — Approved, defines cacheable vs non-cacheable boundaries
- ADR-007 (SpeFileStore facade) — Approved, all Graph caching through facade
- ADR-008 (Endpoint filters) — Approved, pattern for F1/F2 authorization implementation
- ADR-013 (AI Architecture) — Approved, guides E1-E5 AI pipeline changes
- ADR-015 (AI Data Governance) — Approved, logging constraints for Domain G
- Task 033 design (Office auth filters) — Not started, needed for F2

### Dataverse Schema Dependencies (Domain F5)

- `sprk_matter` entity — fields needed for PortfolioService (name, budget, spend, status, assigned user)
- `sprk_event` entity — fields needed for TodoGeneration rules 1/3 (name, due date, status)
- `sprk_invoice` entity — fields needed for TodoGeneration rule 4 (name, status, amount)
- `sprk_task` entity — fields needed for TodoGeneration rule 5 (subject, assigned user, status)

## Phasing

### Phase 1: Beta Blockers (Must Complete Before User Access)

- C0. Bicep tier corrections (Low effort, CRITICAL)
- F1. Secure unauthenticated endpoints — MSAL auth (Medium effort, CRITICAL)
- F2. Implement 37 authorization filters (Medium effort, CRITICAL)
- B3. Thread-safety fixes (Low effort, Critical correctness)
- F6. Production safety fixes (Low effort, High)

### Phase 2: Quick Performance Wins

- E1. Parallelize RAG searches (Low effort, saves 5-8s)
- E3. Document Intelligence timeout (Low effort, prevents infinite waits)
- E4. OpenAI parameter tuning (Config change, saves 2-5s)
- B1. Explicit column selection (Low effort)
- A3. GraphServiceClient pooling (Low effort)
- A4. Debug endpoint removal (Low effort)
- G2. Remove [DEBUG] log tags (Low effort)

### Phase 3: Core Caching

- E2. Cache extracted document text (Medium effort, saves 5-30s on repeat)
- A1. Graph metadata cache (Medium effort, 90%+ latency reduction)
- A2. Authorization data cache (Medium effort, eliminates per-request auth cost)
- E5. Cache RAG search results (Medium effort)
- B2. Request batching (Medium effort)
- B4. Pagination support (Low effort)

### Phase 4: Code Quality, Logging & Workspace

- G1. Guard serialization in log calls (Medium effort)
- G3–G5. Loop batching, log level config, string allocations (Low effort each)
- F3. Remove obsolete handlers (Medium effort, ~5,500 lines)
- F5. Implement real Workspace Dataverse queries (Medium-High effort)
- C8. AI Search cleanup (Low effort)

### Phase 5: Infrastructure Hardening

- C1. Network isolation / VNet + private endpoints (High effort)
- C2. Autoscaling (Medium effort)
- C3. Deployment slots (Medium effort)
- C4–C7. Redis, Key Vault, Storage, OpenAI hardening (Medium effort each)

### Phase 6: CI/CD Maturity & Refactoring

- D1–D4. Test re-enablement, IaC deployment, environment promotion, slot integration
- F4. Refactor ScopeResolverService god class (High effort, post-beta)

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Auth strategy for F1 | Should unauthenticated AI endpoints use MSAL bearer auth or API key auth? | **Same MSAL auth** as other BFF endpoints | Restore `.RequireAuthorization()` — no new auth mechanism needed |
| Obsolete handler removal (F3) | Remove now or keep as fallback during beta? | **Remove now** — JPS replacements verified complete (217 tests) | Delete ~5,500 lines of C# code. Old approach is outdated; will rebuild smarter if needed. |
| Mock Workspace services (F5) | Implement real queries, exclude from beta, or ship with mock labels? | **Implement real Dataverse queries** | Significant scope addition. All 3 services (Portfolio, AI Summary, Todo Generation) need real data. |
| Beta scale | Expected concurrent users and analysis volume? | **15–50 concurrent users, ~200 analyses/day** | Standard Redis C1 (1GB) sufficient. Need rate limiting on AI endpoints. OpenAI TPM must be ≥200. |

## Assumptions

- **Redis Standard C1 (1GB)** is sufficient for beta scale (15–50 users). Will upgrade to C2 if eviction rate > 0 during beta.
- **App Service S1** supports beta load. Will add autoscaling (Phase 5) for broader production.
- **Azure OpenAI 200 TPM** for gpt-4o-mini sufficient for ~200 analyses/day. Monitor token burn rate.
- **JPS configurations are deployed to Dataverse** for all 5 analysis types before obsolete handler removal.
- **Dataverse entity schemas** (sprk_matter, sprk_event, sprk_invoice, sprk_task) have the fields needed for Workspace queries. Field discovery needed during F5 implementation.
- **VNet / private endpoints** deferred to post-beta (Phase 5). Public endpoints acceptable for controlled beta access.

## Unresolved Questions

- [ ] **Task 033 design**: What is the authorization model for Office endpoints? Need OfficeAuthFilter and JobOwnershipFilter designs before F2 can start. — Blocks: F2 (37 authorization filters)
- [ ] **Dataverse field names**: Which exact fields exist on `sprk_invoice` and `sprk_task` entities for TodoGeneration rules 4 & 5? — Blocks: F5 (TodoGenerationService real queries)
- [ ] **OpenAI actual TPM**: Documentation says 10 TPM (dev), Bicep says 120. What's actually deployed? Need `az cognitiveservices account deployment list` verification. — Blocks: C0 accuracy
- [ ] **ScopeResolverService refactor scope**: When splitting the god class (F4), should the 5 new services share a base class or be fully independent? — Blocks: F4 (post-beta, low urgency)

---

*AI-optimized specification. Original: design.md (v2.0, 2026-03-11)*
