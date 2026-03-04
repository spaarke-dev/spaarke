# SDAP BFF API & Performance Enhancement — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-04
> **Source**: `projects/sdap-bff-api-and-performance-enhancement-r1/design.md`
> **Branch**: `work/sdap-bff-api-and-performance-enhancement-r1`

## Executive Summary

The Sprk.Bff.Api has grown from a SharePoint Embedded gateway into a 7-domain platform (120+ endpoints, 99+ DI registrations) whose internal structure, caching, query efficiency, and infrastructure have not kept pace. Users report slow file loading, search, and AI responses — interrelated through shared resource bottlenecks in the single BFF process.

This project delivers **internal improvements only** across 7 workstreams (28 items) targeting architecture decomposition, Redis caching, Dataverse query optimization, resilience, AI pipeline performance, Azure network isolation, and CI/CD hardening. **No new features. No breaking API changes.**

## Scope

### In Scope

**Workstream A — Architecture Foundation**
- A1: Decompose 1,881-line `Program.cs` into domain-specific startup modules (~150-line target)
- A2: Add domain-level feature flags via `appsettings.json` for runtime module isolation

**Workstream B — Caching & Performance**
- B1: Graph metadata Redis cache (file metadata, folder listings, container-to-drive mappings)
- B2: Authorization data snapshot Redis cache (roles, teams, resource access)
- B3: `GraphServiceClient` singleton pooling for app-only operations
- B4: Request-scoped document metadata cache (per-request dedup)
- B5: Debug endpoint removal from non-development environments

**Workstream C — Dataverse Query Optimization**
- C1: Replace 6 `ColumnSet(true)` usages with explicit column selection
- C2: Batch multi-query operations using OData `$batch`
- C3: Fix 2 thread-safety bugs in `DataverseWebApiService` (critical correctness)
- C4: Add pagination support for unbounded queries

**Workstream D — Resilience & Operations**
- D1: Priority-based job queues (3 tiers: critical/standard/bulk)
- D2: Persistent analysis state (Redis hot + Dataverse durable dual-write)
- D3: Request-scoped resource budgets (Graph, OpenAI, AI Search limits per request)

**Workstream E — AI Pipeline Performance**
- E1: Parallel read-only tool execution in chat pipeline via `Task.WhenAll`
- E2: Batch embedding API for multi-embedding requests

**Workstream F — Azure Infrastructure**
- F1: VNet with 3 subnets + private endpoints for all Azure services (security blocker)
- F2: App Service autoscaling (2-10 instances, CPU/memory/queue triggers)
- F3: Deployment slot (staging → health check → swap)
- F4: Redis production hardening (VNet injection, RDB persistence, eviction policy)
- F5: Key Vault network restriction and secret rotation
- F6: Storage account hardening (disable shared key, VNet restriction)
- F7: OpenAI capacity planning (PTU evaluation, deprecated deployment cleanup)
- F8: AI Search cleanup (deprecated index deletion, replica evaluation)

**Workstream G — CI/CD Pipeline**
- G1: Test suite re-enablement (fix interface changes, gate deployments)
- G2: Infrastructure as Code deployment (Bicep with environment parameters)
- G3: Environment promotion (dev → staging → prod with manual approval)
- G4: Deployment slot integration in CI/CD pipeline

### Out of Scope

- Splitting into separate services / microservices
- Changing external API contracts (endpoint URLs, request/response shapes)
- Adding new features or endpoints
- PCF React 16 remediation (separate project)
- Full VNet hub-spoke topology for multi-region
- Database-level Dataverse optimization (requires Microsoft support)
- Load testing / performance benchmarking (follow-up project)

### Affected Areas

- `src/server/api/Sprk.Bff.Api/Program.cs` — Decomposition target (A1)
- `src/server/api/Sprk.Bff.Api/Infrastructure/Startup/` — New startup modules (A1)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/` — Existing DI modules expanded (A1, A2)
- `src/server/api/Sprk.Bff.Api/Infrastructure/ModuleState.cs` — Feature flag state (A2)
- `src/server/api/Sprk.Bff.Api/Api/Filters/` — ModuleGateFilter, resource budget filter (A2, D3)
- `src/server/api/Sprk.Bff.Api/Services/SpeFileStore.cs` — Graph caching integration (B1, B3)
- `src/server/api/Sprk.Bff.Api/Services/AuthorizationService.cs` — Auth data caching (B2)
- `src/server/api/Sprk.Bff.Api/Services/DataverseWebApiService.cs` — Thread-safety, column selection, batching (C1-C4)
- `src/server/api/Sprk.Bff.Api/Services/DataverseService.cs` — Column selection (C1)
- `src/server/api/Sprk.Bff.Api/Workers/ServiceBusJobProcessor.cs` — Priority queues (D1)
- `src/server/api/Sprk.Bff.Api/Services/WorkingDocumentService.cs` — Persistent state (D2)
- `src/server/api/Sprk.Bff.Api/Ai/` — Parallel tools, batch embeddings (E1, E2)
- `infrastructure/` — Bicep templates for VNet, private endpoints, autoscaling (F1-F8)
- `tests/` — Unit + integration tests for new caching and resilience code

## Requirements

### Functional Requirements

1. **FR-01**: Program.cs decomposes into domain-specific startup modules with < 200 lines remaining — Acceptance: Code metric, identical runtime behavior verified by full test suite
2. **FR-02**: Every domain module (SPE, AI, Office, Finance, Communication, Workspace, Email) can be disabled via `Modules:{Domain}:Enabled` config — Acceptance: Disabled module returns 503, other modules unaffected, no crash
3. **FR-03**: Graph metadata (file info, folder listings, container-to-drive mappings) cached in Redis with configurable TTLs (2-24hr) — Acceptance: Cache hit rate > 85%, ETag-versioned keys
4. **FR-04**: Authorization data (roles, teams, resource access) cached in Redis with short TTLs (60-120s), decisions still computed fresh per request — Acceptance: Authorization overhead < 10ms on cache hit
5. **FR-05**: `GraphServiceClient` singleton cached for app-only operations — Acceptance: No per-call credential/auth provider recreation
6. **FR-06**: Request-scoped document metadata dedup via existing `RequestCache` pattern — Acceptance: Same document fetched once per request regardless of access count
7. **FR-07**: Debug/temporary endpoints excluded from non-development environments — Acceptance: `/debug/*` returns 404 in staging/production
8. **FR-08**: All 6 `ColumnSet(true)` usages replaced with explicit column lists — Acceptance: Zero `ColumnSet(true)` in codebase, 50-80% payload reduction verified
9. **FR-09**: Multi-query operations batched using OData `$batch` — Acceptance: Scorecard calculation uses 1 HTTP call instead of 3
10. **FR-10**: Thread-safety bugs in `DataverseWebApiService` fixed — Acceptance: No mutable shared state, per-request headers, SemaphoreSlim-guarded token refresh
11. **FR-11**: Pagination support for unbounded Dataverse queries — Acceptance: Queries > 5,000 records return complete results via paging cookies
12. **FR-12**: 3-tier priority job queues (critical/standard/bulk) with independent concurrency — Acceptance: Critical job pickup < 30s, bulk jobs don't starve critical
13. **FR-13**: Analysis results persisted to Redis (hot, 24h TTL) + Dataverse (durable) — Acceptance: Analysis survives App Service restart with 100% recovery
14. **FR-14**: Per-request resource budgets (Graph: 5-50, OpenAI: 4K-32K tokens, AI Search: 3-20 queries) — Acceptance: Exceeding budget returns 429 ProblemDetails, starts in warn-only mode
15. **FR-15**: Read-only AI tools execute in parallel via `Task.WhenAll` — Acceptance: 2-tool chat messages < 1,500ms p95
16. **FR-16**: Batch embedding API for multi-embedding requests — Acceptance: 2 embeddings in ~200ms instead of 350-400ms
17. **FR-17**: VNet with 3 subnets + private endpoints for all Azure services — Acceptance: All services behind private endpoints, public access disabled
18. **FR-18**: App Service autoscaling (2-10 instances) — Acceptance: Scales out at CPU > 70%/5min, scales in at CPU < 30%/10min
19. **FR-19**: Deployment slot with staging → health check → swap — Acceptance: Zero-downtime deployments verified
20. **FR-20**: Redis hardening (VNet injection, RDB persistence, eviction policy) — Acceptance: Private endpoint only, 15-min snapshot interval
21. **FR-21**: Key Vault and Storage account network restrictions — Acceptance: VNet-only access, shared key disabled on storage
22. **FR-22**: OpenAI capacity planning and deprecated deployment cleanup — Acceptance: PTU evaluation documented, `text-embedding-3-small` removed
23. **FR-23**: AI Search cleanup — Acceptance: `spaarke-knowledge-index` deleted, replica count evaluated
24. **FR-24**: Test suite re-enabled and gating deployments — Acceptance: `dotnet test` passes, blocks deployment on failure
25. **FR-25**: IaC deployment via Bicep with environment parameters — Acceptance: `what-if` preview + deploy for dev/staging/prod
26. **FR-26**: Environment promotion with manual prod approval gate — Acceptance: dev → staging auto, staging → prod manual
27. **FR-27**: Deployment slot integration in CI/CD — Acceptance: Pipeline deploys to staging, health checks, swaps

### Non-Functional Requirements

- **NFR-01**: File listing response (cached) < 50ms p95
- **NFR-02**: Document download response < 600ms p95
- **NFR-03**: Chat message response (2 tools) < 1,500ms p95
- **NFR-04**: Graph metadata cache hit rate > 85%
- **NFR-05**: Dataverse entity read < 100ms p95
- **NFR-06**: Critical job pickup latency < 30 seconds
- **NFR-07**: Zero external API contract changes (URLs, schemas, status codes, auth, rate limiting, SSE)
- **NFR-08**: Each workstream item independently deployable and reversible
- **NFR-09**: Redis unavailability degrades gracefully (bypass cache, hit source directly, log warnings) — no 503 failures
- **NFR-10**: Caches warm organically through user requests after deployment — no proactive warm-up needed
- **NFR-11**: All new caching and resilience code has unit tests + integration tests

## Technical Constraints

### Applicable ADRs

- **ADR-001**: Minimal API + BackgroundService — No Azure Functions, no controllers
- **ADR-003**: Lean Authorization — Cache data not decisions, per-request UAC snapshots only
- **ADR-004**: Async Job Contract — Idempotent handlers, deterministic keys, CorrelationId propagation
- **ADR-007**: SpeFileStore Facade — All Graph operations through facade, no leaked Graph SDK types
- **ADR-008**: Endpoint Filters — No global auth middleware, use endpoint filters for resource authorization
- **ADR-009**: Redis-First Caching — `IDistributedCache` for cross-request, `RequestCache` for within-request, no `IMemoryCache` without profiling proof
- **ADR-010**: DI Minimalism — Concretes by default, feature module extensions, ≤15 non-framework registrations
- **ADR-013**: AI Architecture — AI stays in BFF, no separate service, rate limit all AI endpoints
- **ADR-015**: AI Data Governance — Minimum text to AI, log only identifiers/sizes/timings, tenant-scoped artifacts
- **ADR-019**: ProblemDetails & Error Handling — All HTTP failures return ProblemDetails with errorCode and correlationId

### MUST Rules

- ✅ MUST use Minimal API patterns for all endpoints (ADR-001)
- ✅ MUST route all SPE operations through `SpeFileStore` facade (ADR-007)
- ✅ MUST use endpoint filters for resource authorization (ADR-008)
- ✅ MUST use `IDistributedCache` (Redis) for cross-request caching (ADR-009)
- ✅ MUST use `RequestCache` for within-request dedup (ADR-009)
- ✅ MUST version cache keys with rowversion/ETag (ADR-009)
- ✅ MUST use short TTLs for security-sensitive data (ADR-009)
- ✅ MUST return ProblemDetails for all HTTP failures (ADR-019)
- ✅ MUST include correlationId in all errors (ADR-019)
- ✅ MUST use feature module extensions for DI (ADR-010)
- ✅ MUST implement job handlers as idempotent (ADR-004)
- ✅ MUST log only identifiers, sizes, timings — never content (ADR-015)
- ❌ MUST NOT cache authorization decisions — cache underlying data only (ADR-003, ADR-009)
- ❌ MUST NOT inject `GraphServiceClient` outside `SpeFileStore` (ADR-007)
- ❌ MUST NOT use global middleware for resource authorization (ADR-008)
- ❌ MUST NOT use `IMemoryCache` without documented profiling justification (ADR-009)
- ❌ MUST NOT create separate AI microservice (ADR-013)
- ❌ MUST NOT log document contents, prompts, or model responses (ADR-015)
- ❌ MUST NOT leak document content in error responses (ADR-019)

### Existing Patterns to Follow

- See `src/server/api/Sprk.Bff.Api/Infrastructure/DI/` for existing module pattern (`AddSpaarkeCore`, `AddDocumentsModule`, etc.)
- See `src/server/api/Sprk.Bff.Api/Api/Filters/` for endpoint filter pattern
- See `src/server/api/Sprk.Bff.Api/Infrastructure/RequestCache.cs` for per-request cache pattern
- See `.claude/patterns/api/` for detailed API patterns

## Success Criteria

1. [ ] **SC-01**: Program.cs < 200 lines — Verify: code metric
2. [ ] **SC-02**: File listing response (cached) < 50ms p95 — Verify: Application Insights
3. [ ] **SC-03**: Document download response < 600ms p95 — Verify: Application Insights
4. [ ] **SC-04**: Chat message response (2 tools) < 1,500ms p95 — Verify: Application Insights
5. [ ] **SC-05**: Graph metadata cache hit rate > 85% — Verify: Redis metrics + custom telemetry
6. [ ] **SC-06**: Dataverse entity read < 100ms p95 — Verify: Application Insights
7. [ ] **SC-07**: Zero `ColumnSet(true)` in production — Verify: code search
8. [ ] **SC-08**: Zero thread-safety issues in Dataverse client — Verify: code review
9. [ ] **SC-09**: Analysis survives App Service restart — Verify: integration test
10. [ ] **SC-10**: Critical job pickup latency < 30s — Verify: Service Bus metrics
11. [ ] **SC-11**: Bulk jobs don't starve critical jobs — Verify: concurrent test
12. [ ] **SC-12**: All Azure services behind private endpoints — Verify: Azure Resource Graph
13. [ ] **SC-13**: Autoscaling configured and tested — Verify: scale trigger verification
14. [ ] **SC-14**: Deployment slots operational — Verify: staging → prod swap
15. [ ] **SC-15**: CI/CD runs tests before deployment — Verify: pipeline logs
16. [ ] **SC-16**: Disable any domain via config → 503, no crash, others unaffected — Verify: integration test

## Implementation Phasing

### Phase 1: Foundation (Week 1)
| Item | Effort | Dependencies |
|------|--------|-------------|
| A1: Program.cs Decomposition | 6-8 hrs | None |
| C3: Thread-Safety Fixes | 3-4 hrs | None |
| C1: Explicit Column Selection | 4-6 hrs | None |
| B5: Debug Endpoint Removal | 1-2 hrs | None |

### Phase 2: Caching (Week 2)
| Item | Effort | Dependencies |
|------|--------|-------------|
| A2: Domain Feature Flags | 4-6 hrs | A1 |
| B1: Graph Metadata Cache | 8-10 hrs | A1 |
| B2: Authorization Data Cache | 6-8 hrs | A1 |
| B3: GraphServiceClient Pooling | 2-3 hrs | A1 |
| B4: Request-Scoped Document Cache | 2-3 hrs | None |

### Phase 3: Resilience & AI Performance (Weeks 3-4)
| Item | Effort | Dependencies |
|------|--------|-------------|
| D1: Priority Job Queues | 8-10 hrs | A1, Service Bus queues |
| D2: Persistent Analysis State | 6-8 hrs | A1 |
| D3: Resource Budgets | 6-8 hrs | A1 |
| E1: Parallel Tool Execution | 4-6 hrs | None |
| E2: Batch Embeddings | 4-6 hrs | None |
| C2: Request Batching | 6-8 hrs | C3 |
| C4: Pagination Support | 3-4 hrs | None |

### Phase 4: Infrastructure (Weeks 4-6)
| Item | Effort | Dependencies |
|------|--------|-------------|
| F1: Network Isolation (VNet + PE) | 16-20 hrs | Bicep templates |
| F2: Autoscaling | 4-6 hrs | None |
| F3: Deployment Slots | 3-4 hrs | None |
| F4: Redis Hardening | 3-4 hrs | F1 |
| F5-F6: Key Vault + Storage Hardening | 4-6 hrs | F1 |
| F7: OpenAI Capacity Planning | 3-4 hrs | None |
| F8: AI Search Cleanup | 2-3 hrs | None |

### Phase 5: CI/CD (Weeks 5-6)
| Item | Effort | Dependencies |
|------|--------|-------------|
| G1: Test Re-enablement | 4-6 hrs | None |
| G2: IaC Deployment | 6-8 hrs | F1 |
| G3: Environment Promotion | 4-6 hrs | G2 |
| G4: Slot Integration | 2-3 hrs | F3, G3 |

**Total**: 28 items, 124-170 hours estimated, ~6 weeks

## Dependencies

### Prerequisites
- Redis production instance (available via Bicep)
- ADR-009 (Redis caching) — Approved
- ADR-003 (Authorization) — Approved
- ADR-007 (SpeFileStore facade) — Approved

### External Dependencies
- Azure VNet — Not created, required for F1
- App Service P1v3 plan — Defined in Bicep, required for F2/F3
- Service Bus queues (2 new) — Not created, required for D1
- `sprk_analysisresult` Dataverse entity — Exists, verify schema for D2

## Risk Registry

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|------------|
| DI registration order change breaks startup | Medium | High | Extract in exact order + full test suite |
| Middleware order changes | Low | Critical | Auth before AuthZ, extract exactly as-is |
| Cache staleness causes stale file metadata | Medium | Medium | Short TTLs (2-5 min), ETag-versioned keys |
| Authorization cache allows brief post-revocation access | Low | Medium | 60-second TTL, acceptable per ADR-003 |
| Feature flag disables needed service | Medium | Medium | Default all to `true`, document dependency graph |
| Messages lost during queue migration | Low | Medium | Keep old queue active, idempotent handlers (ADR-004) |
| Resource budgets too restrictive | Medium | Low | Warn-only mode first, 2-week observation |
| VNet migration causes service disruption | Medium | High | Deploy private endpoints alongside public first, then disable public |
| `$batch` support varies across operations | Medium | Low | Test each scenario, fall back to `Task.WhenAll` |
| Deployment slot swap cold start | Medium | Medium | Warm-up endpoint (`/healthz`) in slot configuration |

### Rollback Strategy

Each item is independently deployable and reversible:
- **A1**: Git revert (no runtime state)
- **A2**: Remove config section (defaults to enabled)
- **B1-B4**: Set TTL to 0 or disable cache key prefix
- **C1-C4**: Git revert (no runtime state)
- **D1**: Route all to original queue
- **D2**: Feature flag `Analysis:Persistence:Enabled = false`
- **D3**: Feature flag `ResourceBudgets:EnforcementMode = "disabled"`
- **E1-E2**: Feature flag or git revert
- **F1-F8**: Re-enable public endpoints
- **G1-G4**: Pipeline config rollback

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Redis failure mode | When Redis is unavailable, should caching layers degrade gracefully or fail fast? | **Degrade gracefully** — bypass cache, hit source directly, log warnings | Cache implementations must use try/catch around Redis calls and fall back to direct source access |
| Cache warming | Should caches be proactively warmed on startup or warm organically? | **Organic warming** — caches start empty, warm through user requests | No startup warm-up code needed; accept higher latency on first requests post-deploy |
| Feature flag backend | Should feature flags use appsettings.json or Azure App Configuration? | **appsettings.json only** — simple config-based, matches current pattern | Feature flag toggling requires deployment or App Service config change (no runtime portal) |
| Test scope | What test expectations for new caching and resilience code? | **Unit + integration tests** — unit for logic, integration with testcontainers/emulators | Must set up Redis and Service Bus test infrastructure; integration tests validate real behavior |

## Assumptions

- **Redis availability**: Redis is treated as a soft dependency — unavailability degrades performance but doesn't cause failures
- **Cache TTLs**: Default values in design doc are starting points; can be tuned post-deployment based on telemetry
- **Service Bus queues**: 2 new queues can be provisioned via Bicep without manual intervention
- **Dataverse schema**: `sprk_analysisresult` entity exists with adequate columns for persistent analysis state
- **VNet CIDR**: Address space can be allocated without conflicts in existing Azure subscription
- **PTU evaluation**: OpenAI PTU decision is informational — actual purchase requires separate budget approval

## Unresolved Questions

- [ ] **Exact VNet CIDR ranges** — Need to verify available address space in subscription — Blocks: F1
- [ ] **PTU commitment level** — Requires cost-benefit analysis with actual usage data — Blocks: F7 (evaluation only, not purchase)
- [ ] **`sprk_analysisresult` schema verification** — Need to confirm entity has required columns for D2 — Blocks: D2

---

*AI-optimized specification. Original design: `projects/sdap-bff-api-and-performance-enhancement-r1/design.md`*
