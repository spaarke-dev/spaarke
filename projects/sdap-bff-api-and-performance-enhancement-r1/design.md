# SDAP BFF API & Performance Enhancement — Design Specification

> **Project**: sdap-bff-api-and-performance-enhancement-r1
> **Date**: March 4, 2026
> **Status**: Draft — Awaiting Review
> **Scope**: Architecture, performance, and production-readiness improvements to Sprk.Bff.Api
> **Supersedes**: `production-performance-improvement-r1/design.md` (merged into this project)

---

## 1. Executive Summary

The Sprk.Bff.Api has grown from a SharePoint Embedded gateway into a 7-domain platform serving 120+ endpoints with 99+ DI registrations. While this growth was by design (ADR-001, ADR-013), the internal structure, caching, query efficiency, and infrastructure have not kept pace. Users report slow file loading, slow search, and slow AI responses — problems that are interrelated through shared infrastructure bottlenecks in the single BFF process.

This project delivers improvements across **7 workstreams**:

| Workstream | Focus | Target Impact |
|-----------|-------|---------------|
| **A: Architecture Foundation** | Program.cs decomposition, feature flags | Maintainability, incident isolation |
| **B: Caching & Performance** | Graph metadata, authorization data, document caches | **90-97% latency reduction** on cached operations |
| **C: Dataverse Optimization** | Column selection, batching, thread safety | **50-60% payload reduction**, correctness fixes |
| **D: Resilience & Operations** | Priority queues, persistent state, resource budgets | Eliminate contention, prevent state loss |
| **E: AI Pipeline Performance** | Parallel tool execution, batch embeddings | **30-50% faster** chat responses |
| **F: Azure Infrastructure** | VNet, autoscaling, deployment slots | Production security, availability |
| **G: CI/CD Pipeline** | Tests, IaC, environment promotion | Deployment quality and safety |

**Guiding Principle**: Invest in internal structure (modular monolith), not external separation. No new features. No breaking changes to external API contracts.

---

## 2. Problem Statement

### 2.1 Current State

| Metric | Value | Concern |
|--------|-------|---------|
| Program.cs lines | 1,881 | High — cognitive load, merge conflicts |
| DI registrations | 99+ | Medium — exceeds ADR-010 "minimal" intent |
| Endpoint groups | 33 | Low — well-organized via MapXxxEndpoints() |
| Background job handlers | 13+ | Medium — all share one processor, no priority |
| Feature flag coverage | Partial (AI only) | Medium — no isolation for other domains |
| In-memory state | Analysis results, working documents | Critical — lost on restart |
| Caching layers built | 1 of 4 (OBO token only) | High — 3 prescribed caches not implemented |
| `ColumnSet(true)` usage | 6 service methods | Medium — 3-5x payload overhead |
| Thread-safety issues | 2 in DataverseWebApiService | Critical — correctness bugs |
| Azure network isolation | None (all public endpoints) | Critical — production security blocker |

### 2.2 User-Reported Performance Problems

Users report three categories of slowness, all traced to shared root causes:

**SPE Files Slow to Load** (307-755ms best case, 90s+ worst case):
```
[1] Dataverse: GetDocumentAsync()              50-200ms  ← NOT CACHED, over-fetches 60 columns
[2] OBO Token Exchange                          5-200ms  ← Redis cached (95% hit)
[3] Graph API: POST .../preview                250-350ms ← NOT CACHED
                                               ─────────
    Total:                                      307-755ms
```

**Semantic Search Slow** (250-500ms uncached):
```
[1] Generate query embedding                   150-200ms ← Cache miss = OpenAI call
[2] Azure AI Search hybrid query               100-300ms ← Sequential after embedding
                                               ─────────
    Total:                                      250-500ms
```

**AI Chat Responses Slow** (750-2530ms typical):
```
[1] Session + context resolution                30-130ms
[2] LLM initial processing                     100-500ms
[3] Tool: DocumentSearchTools                   105-500ms ← Sequential embedding + search
[4] Tool: KnowledgeRetrievalTools               105-500ms ← Another sequential chain
[5] LLM response generation                    200-1000ms
                                               ─────────
    Total:                                      540-2630ms
```

### 2.3 How They're Interrelated

All three paths share infrastructure in the single BFF process. Under load, they compound:

| Scenario | What Happens | User Impact |
|----------|-------------|-------------|
| Bulk RAG indexing running | Consumes OpenAI rate limit (`ai-stream: 10/min`) | Chat messages throttled or queued |
| Multiple users previewing files | Graph `graph-read` rate limit (100/min) consumed faster | Preview requests retry with backoff |
| Email batch processing | 5 Service Bus handlers making Dataverse + Graph calls | Thread pool pressure on all requests |
| AI analysis + chat simultaneous | Both compete for OpenAI `MaxConcurrentStreams: 3` | One waits while the other streams |

### 2.4 Explicitly Out of Scope

- Splitting into separate services / microservices
- Changing external API contracts (endpoint URLs, request/response shapes)
- Adding new features or endpoints
- PCF React 16 remediation (separate project)
- Full VNet hub-spoke topology for multi-region
- Database-level Dataverse optimization (requires Microsoft support)
- Load testing / performance benchmarking (follow-up project)

---

## 3. Workstream A: Architecture Foundation

### A1: Program.cs Decomposition

**Problem**: 1,881-line composition root. DI registration (1,117 lines / 59%), middleware (121 lines), and endpoint mapping (140 lines) interleaved with conditional logic.

**Solution**: Split into domain-specific startup modules. Program.cs reduces to ~150 lines.

**New files**:

| File | Extracted From | Lines |
|------|---------------|-------|
| `Infrastructure/Startup/AuthStartup.cs` | Auth + 26 policies (lines 114-251) | ~140 |
| `Infrastructure/Startup/CacheStartup.cs` | Redis/memory cache (lines 284-337) | ~55 |
| `Infrastructure/Startup/ResilienceStartup.cs` | Polly + rate limiting (lines 340-368, 932-1090) | ~230 |
| `Infrastructure/Startup/ObservabilityStartup.cs` | AppInsights + OTel + health checks | ~70 |
| `Infrastructure/Startup/MiddlewareStartup.cs` | Middleware pipeline (lines 1235-1356) | ~120 |
| `Infrastructure/Startup/EndpointStartup.cs` | Inline health/debug/status endpoints (lines 1361-1678) | ~320 |
| `Infrastructure/DI/EmailModule.cs` | Email processing services (lines 600-650) | ~55 |

**Existing modules expanded**: `AiModule.cs` absorbs ~200 lines of AI services. `WorkersModule.cs` absorbs ~65 lines of job handlers and hosted services.

**Target Program.cs** (~150 lines):
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddAuthentication(config);
builder.AddCaching(config);
builder.AddResilience(config);
builder.AddObservability(config);
builder.Services.AddSpaarkeCore();
builder.Services.AddDocumentsModule();
builder.Services.AddAiModules(config);    // Foundation + Analysis + Search (conditional)
builder.Services.AddWorkersModule(config);
builder.Services.AddOfficeModule();
builder.Services.AddFinanceModule(config);
builder.Services.AddWorkspaceServices(config);
builder.Services.AddCommunicationModule(config);
builder.Services.AddEmailModule(config);
var app = builder.Build();
app.UseSpaarkeMiddleware();
app.MapAllEndpoints(config);
app.Run();
```

**Effort**: 6-8 hours. **Risk**: Registration order changes. Mitigated by extracting in exact order + full test suite.

### A2: Domain Feature Flags

**Problem**: No ability to disable a malfunctioning domain without redeployment.

**Solution**: Every domain module can be disabled at runtime via configuration:

```json
{
  "Modules": {
    "Spe": { "Enabled": true },
    "Ai": { "Enabled": true },
    "Office": { "Enabled": true },
    "Finance": { "Enabled": true },
    "Communication": { "Enabled": true },
    "Workspace": { "Enabled": true },
    "Email": { "Enabled": true }
  }
}
```

When disabled: endpoints return 503, DI modules skip registration, background job handlers skip-and-complete messages.

**Domain dependency graph** (documented and enforced):
```
SPE / Documents  ← foundational (warn if disabled)
    ↑
    ├── AI Platform (needs file access)
    ├── Office Add-ins (needs file upload/download)
    ├── Email (needs file storage for EML)
    └── Finance (needs document storage)
Dataverse ← cannot be disabled (core metadata store)
```

**New files**: `Infrastructure/ModuleState.cs`, `Api/Filters/ModuleGateFilter.cs`.
**Modified files**: All 10 DI module files, `ServiceBusJobProcessor.cs`, `appsettings.json`.

**Effort**: 4-6 hours.

---

## 4. Workstream B: Caching & Performance

These implement the Redis caching layers prescribed by ADR-009 but not yet built.

### B1: Graph Metadata Cache

**Problem**: Graph API metadata (file info, folder listings, container-to-drive mappings) is fetched fresh on every request. This is the single largest latency contributor for repeat operations.

**Solution**: Distributed Redis cache with short TTLs:

| Cache Target | Key Pattern | TTL | Impact |
|-------------|-------------|-----|--------|
| File metadata (name, size, ETag) | `sdap:graph:metadata:{driveId}:{itemId}:v{etag}` | 5 min | Eliminates 100-300ms per cached file |
| Folder listing (children) | `sdap:graph:children:{driveId}:{folderId}:v{hash}` | 2 min | Eliminates 100-500ms per listing |
| Container-to-Drive ID mapping | `sdap:graph:drive:{containerId}` | 24 hr | Eliminates Drive resolution hop |
| Drive item permissions | `sdap:graph:perms:{driveId}:{itemId}` | 5 min | Eliminates permission lookup |

**Expected impact**: At 90%+ hit rate, most metadata operations drop from 200-500ms to ~5ms.

**Effort**: 8-10 hours.

### B2: Authorization Data Snapshot Cache

**Problem**: Authorization data (user roles, team memberships) is queried from Dataverse on every request (50-200ms).

**Solution**: Cache the underlying authorization *data* while still computing authorization *decisions* fresh per request (per ADR-009 and ADR-003):

| Cache Target | Key Pattern | TTL | Notes |
|-------------|-------------|-----|-------|
| User role assignments | `sdap:auth:roles:{userOid}` | 2 min | Security-sensitive |
| User team memberships | `sdap:auth:teams:{userOid}` | 2 min | Rarely changes mid-session |
| Resource access data | `sdap:auth:access:{userOid}:{resourceId}` | 60 sec | Per-resource |

**Expected impact**: Eliminates 50-200ms authorization overhead on most requests.

**Effort**: 6-8 hours.

### B3: GraphServiceClient Pooling

**Problem**: `GraphClientFactory.ForApp()` recreates credential and auth provider objects on every call, even though the underlying HttpClient is pooled.

**Solution**: Cache the singleton `GraphServiceClient` for app-only operations.

**Effort**: 2-3 hours.

### B4: Request-Scoped Document Metadata Cache

**Problem**: `GetDocumentAsync()` hits Dataverse on every file operation (50-200ms), even when the same document is accessed multiple times in the same request.

**Solution**: Extend existing `RequestCache` pattern to cover document metadata:

```csharp
var document = await requestCache.GetOrAddAsync(
    $"doc:{documentId}",
    () => dataverseService.GetDocumentAsync(documentId, ct));
```

This is per-request dedup (ADR-009), not distributed cache.

**Effort**: 2-3 hours.

### B5: Debug Endpoint Removal

**Problem**: Debug and temporary endpoints (`/debug/*`, anonymous health checks) present in all environments.

**Solution**: Conditionally exclude from non-development environments.

**Effort**: 1-2 hours.

---

## 5. Workstream C: Dataverse Query Optimization

### C1: Explicit Column Selection

**Problem**: 6 service methods use `ColumnSet(true)` returning 30-60+ columns when 5-10 are needed. WebAPI `GetDocumentAsync` has no `$select`.

**Fix**:

| Method | Current | Fix |
|--------|---------|-----|
| `GetDocumentAsync` (SDK) | `ColumnSet(true)` (~60 cols) | 8 columns |
| `GetProcessingJobAsync` | `ColumnSet(true)` (~30 cols) | 10 columns |
| `GetProcessingJobByIdempotencyKeyAsync` | `ColumnSet(true)` | 6 columns |
| `GetEmailArtifactAsync` | `ColumnSet(true)` | 12 columns |
| `GetAttachmentArtifactAsync` | `ColumnSet(true)` | 8 columns |
| `GetDocumentAsync` (WebAPI) | No `$select` | Add explicit `$select` |

**Expected impact**: 60-80% reduction in Dataverse response payloads.

**Effort**: 4-6 hours.

### C2: Request Batching

**Problem**: Multi-query operations make 3+ sequential Dataverse round-trips.

**Solution**: Use `$batch` OData endpoint:

| Operation | Current | Batched |
|-----------|---------|---------|
| Scorecard calculation (3 KPI queries) | 3 sequential HTTP calls | 1 `$batch` request |
| Field mapping profile + rules | 2 sequential calls | 1 call with `$expand` |
| Multi-entity authorization checks | N sequential calls | `$batch` where applicable |

**Effort**: 6-8 hours.

### C3: Thread-Safety Fixes

**Problem**: Two correctness bugs in `DataverseWebApiService`:
1. Mutable `_currentToken` field without synchronization → redundant token acquisitions under load
2. Mutable `DefaultRequestHeaders.Authorization` on shared `HttpClient` → race conditions

**Fix**:
1. `SemaphoreSlim`-guarded token refresh pattern
2. Per-request `HttpRequestMessage` headers instead of shared client headers

**Effort**: 3-4 hours. **Priority**: Critical — correctness bugs, not just performance.

### C4: Pagination Support

**Problem**: Unbounded queries silently truncate at Dataverse 5,000-record page limit.

**Solution**: Add paging cookie support to `GetDocumentsByContainerAsync` and other unbounded queries.

**Effort**: 3-4 hours.

---

## 6. Workstream D: Resilience & Operations

### D1: Priority-Based Job Queues

**Problem**: Single `ServiceBusJobProcessor` dispatches all 13+ job types through one consumer. Bulk indexing starves user-facing operations.

**Solution**: 3 priority tiers with independent queues and concurrency:

| Queue | Max Concurrent | Job Types |
|-------|---------------|-----------|
| `sdap-jobs-critical` | 5 | DocumentProcessing, AppOnlyDocumentAnalysis |
| `sdap-jobs-standard` | 3 | EmailToDocument, EmailAnalysis, InvoiceExtraction, IncomingCommunication |
| `sdap-jobs-bulk` | 2 | BatchProcessEmails, BulkRagIndexing, RagIndexing, InvoiceIndexing, ProfileSummary, AttachmentClassification, SpendSnapshot |

**Migration**: Deploy new queues alongside existing. Keep old queue listener active. Switch `JobSubmissionService` routing. Drain old queue. Remove old listener.

**Effort**: 8-10 hours.

### D2: Persistent Analysis State

**Problem**: Analysis results stored in-memory. App Service restart = all active analyses lost. #1 production blocker (OPS-05).

**Solution**: Redis (hot, 24h TTL) + Dataverse (durable) dual-write via `WorkingDocumentService`:

```
Write path:  Redis (fast) + Dataverse (async, fire-and-forget with retry)
Read path:   Redis first → Dataverse fallback → warm into Redis
```

**Effort**: 6-8 hours.

### D3: Request-Scoped Resource Budgets

**Problem**: A single chat message can trigger unlimited downstream calls. No cost attribution or protection from runaway operations.

**Solution**: Per-request budget tracking:

| Resource | Chat Message | Analysis | Batch Job |
|----------|-------------|----------|-----------|
| Graph API calls | 5 | 15 | 50 |
| OpenAI tokens (input) | 4,000 | 16,000 | 32,000 |
| AI Search queries | 3 | 10 | 20 |

Exceeding budget returns 429 with ProblemDetails. Starts in warn-only mode for 2 weeks.

**Effort**: 6-8 hours.

---

## 7. Workstream E: AI Pipeline Performance

### E1: Parallel Tool Execution in Chat Pipeline

**Problem**: Agent Framework executes tools sequentially. Two search tools = 2x latency.

**Solution**: Execute read-only tools in parallel via `Task.WhenAll`. Write tools (CreateTask, UpdateRecord) remain sequential.

```csharp
// Proposed:
var readOnlyTasks = toolCalls.Where(IsReadOnly).Select(ExecuteToolAsync);
var results = await Task.WhenAll(readOnlyTasks);
```

**Expected impact**: 2-tool chat messages drop from ~700-1000ms tool time to ~350-500ms.

**Effort**: 4-6 hours.

### E2: Batch Embedding API

**Problem**: Each tool call generates its own embedding independently (sequential OpenAI calls).

**Solution**: Batch embedding requests when multiple are needed in the same scope:

```csharp
// 2 embeddings in 200ms instead of 350-400ms
var embeddings = await openAi.GenerateBatchEmbeddingsAsync(new[] { query1, query2 });
```

**Effort**: 4-6 hours.

---

## 8. Workstream F: Azure Infrastructure

### F1: Network Isolation (Priority 0 — Security)

**Problem**: All Azure services use public endpoints with no network restrictions. **Production security blocker.**

**Solution**:
- Create VNet with 3 subnets: `snet-app`, `snet-redis`, `snet-pe`
- Private endpoints for: Key Vault, Storage, Service Bus, Azure OpenAI, AI Search, Document Intelligence
- VNet integration on App Service
- Disable public access on all resources once private endpoints confirmed
- NSG rules for inter-service communication

**Effort**: 16-20 hours.

### F2: App Service Autoscaling

| Metric | Scale Out | Scale In | Cooldown |
|--------|----------|----------|----------|
| CPU % | > 70% for 5 min | < 30% for 10 min | 5 min |
| Memory % | > 80% for 5 min | < 40% for 10 min | 5 min |
| HTTP Queue | > 100 for 2 min | < 10 for 10 min | 5 min |

Min: 2 instances. Max: 10. Default: 2.

**Effort**: 4-6 hours.

### F3: Deployment Slots

Configure staging slot on P1v3 plan. Deploy to staging → health check → swap to production.

**Effort**: 3-4 hours.

### F4: Redis Production Hardening

VNet injection, RDB persistence (15 min snapshots), `allkeys-lru` eviction, disable public access.

**Effort**: 3-4 hours.

### F5: Key Vault Hardening

Restrict network access to VNet, enable secret rotation policy, audit role assignments.

**Effort**: 2-3 hours.

### F6: Storage Account Hardening

Disable shared key access, restrict to VNet, lifecycle management for `ai-chunks` container.

**Effort**: 2-3 hours.

### F7: OpenAI Capacity Planning

Evaluate PTU for gpt-4o/gpt-4o-mini, restrict to private endpoint, remove deprecated `text-embedding-3-small` deployment.

**Effort**: 3-4 hours.

### F8: AI Search Cleanup

Delete deprecated `spaarke-knowledge-index` (1536-dim), evaluate 3 replicas for 99.9% write SLA, restrict public access.

**Effort**: 2-3 hours.

---

## 9. Workstream G: CI/CD Pipeline

### G1: Test Suite Re-enablement

Fix interface changes that caused test disablement. Re-enable `dotnet test` as deployment gate.

**Effort**: 4-6 hours.

### G2: Infrastructure as Code Deployment

Add Bicep deployment step with `dev.bicepparam`, `staging.bicepparam`, `prod.bicepparam`. Add `what-if` preview.

**Effort**: 6-8 hours.

### G3: Environment Promotion

Define dev → staging → prod progression with manual approval gate for prod.

**Effort**: 4-6 hours.

### G4: Deployment Slot Integration

CI/CD deploys to staging slot → health check → manual swap to production.

**Effort**: 2-3 hours.

---

## 10. Performance Analysis: Latency Traces

### 10.1 SPE File Preview — Current vs. Projected

| Step | Current | After Caching (B1/B2) | After Both |
|------|---------|---------------------|------------|
| Authorization (Dataverse) | 50-200ms | **3-5ms** (B2 cached) | 3-5ms |
| OBO Token | 5ms (cached) | 5ms | 5ms |
| Graph: Resolve Drive | 100-300ms | **3-5ms** (B1 cached) | 3-5ms |
| Graph: Preview/Content | 100-500ms | 100-500ms (must hit Graph) | **Lower** (VNet F1) |
| **Total** | **255-1,005ms** | **111-515ms** (first load) / **~15-25ms** (cached) | **~15-25ms** (cached) |

### 10.2 Chat Message (2 Tools) — Current vs. Projected

| Step | Current | After E1+E2 | After All |
|------|---------|------------|-----------|
| Session + context | 30-130ms | 30-130ms | 30-130ms |
| LLM processing | 100-500ms | 100-500ms | 100-500ms |
| Tool 1 (embedding + search) | 105-500ms | **Parallel** | Parallel + cached |
| Tool 2 (embedding + search) | 105-500ms | **Parallel** | Parallel + cached |
| LLM response | 200-1000ms | 200-1000ms | 200-1000ms |
| **Total** | **540-2,630ms** | **435-2,130ms** | **350-1,200ms** |

### 10.3 Combined Performance Targets

| Operation | Current | After Project | Improvement |
|-----------|---------|--------------|-------------|
| File listing (cached) | 250-1,000ms | **15-25ms** | 90-97% |
| Document download | 250-1,000ms | **110-520ms** | 45-55% |
| Semantic search (cache hit) | 105-310ms | 105-310ms | Irreducible |
| Chat message (2 tools) | 750-2,530ms | **350-1,200ms** | 50-55% |
| Chat during bulk indexing | 1,500-4,000ms+ | **500-1,200ms** | 60-70% |
| Dataverse entity read | 80-150ms | **40-80ms** | ~50% |
| Multi-query operations | 240ms+ | **~100ms** | ~60% |
| Analysis resume after restart | **Lost** | **< 50ms** | N/A → working |

### 10.4 Shared Resource Contention (Resolved by D1/D3)

```
┌──────────────────────────────────────────────────────────────┐
│  SHARED RESOURCES (single process)                            │
│                                                               │
│  Graph HTTP Client      OpenAI Client       .NET Thread Pool  │
│  graph-read: 100/min    ai-stream: 10/min   Shared by all     │
│  graph-write: 20/min    MaxConcurrent: 3                      │
│       ↑                      ↑                    ↑           │
│  D1: Priority queues    D3: Resource budgets  D1: Separate    │
│  reduce contention      bound per-request     job concurrency │
└──────────────────────────────────────────────────────────────┘
```

---

## 11. Implementation Phasing

### Phase 1: Foundation (Week 1)

Must be done first — provides the modular structure for all subsequent work.

| Item | Workstream | Effort | Dependency |
|------|-----------|--------|------------|
| A1: Program.cs Decomposition | A | 6-8 hrs | None |
| C3: Thread-Safety Fixes | C | 3-4 hrs | None (correctness bug) |
| C1: Explicit Column Selection | C | 4-6 hrs | None |
| B5: Debug Endpoint Removal | B | 1-2 hrs | None |

### Phase 2: Caching (Week 2)

Highest user-visible impact. Requires modular startup from Phase 1.

| Item | Workstream | Effort | Dependency |
|------|-----------|--------|------------|
| A2: Domain Feature Flags | A | 4-6 hrs | A1 |
| B1: Graph Metadata Cache | B | 8-10 hrs | A1 |
| B2: Authorization Data Cache | B | 6-8 hrs | A1 |
| B3: GraphServiceClient Pooling | B | 2-3 hrs | A1 |
| B4: Request-Scoped Document Cache | B | 2-3 hrs | None |

### Phase 3: Resilience & AI Performance (Week 3-4)

Can run in parallel across items.

| Item | Workstream | Effort | Dependency |
|------|-----------|--------|------------|
| D1: Priority Job Queues | D | 8-10 hrs | A1, Service Bus queues |
| D2: Persistent Analysis State | D | 6-8 hrs | A1 |
| D3: Resource Budgets | D | 6-8 hrs | A1 |
| E1: Parallel Tool Execution | E | 4-6 hrs | None |
| E2: Batch Embeddings | E | 4-6 hrs | None |
| C2: Request Batching | C | 6-8 hrs | C3 |
| C4: Pagination Support | C | 3-4 hrs | None |

### Phase 4: Infrastructure (Week 4-6)

Can start in parallel with Phase 3. Infrastructure changes are independent of code changes.

| Item | Workstream | Effort | Dependency |
|------|-----------|--------|------------|
| F1: Network Isolation (VNet + PE) | F | 16-20 hrs | Bicep templates |
| F2: Autoscaling | F | 4-6 hrs | None |
| F3: Deployment Slots | F | 3-4 hrs | None |
| F4: Redis Hardening | F | 3-4 hrs | F1 |
| F5-F6: Key Vault + Storage Hardening | F | 4-6 hrs | F1 |
| F7: OpenAI Capacity Planning | F | 3-4 hrs | None |
| F8: AI Search Cleanup | F | 2-3 hrs | None |

### Phase 5: CI/CD (Week 5-6)

Builds on infrastructure from Phase 4.

| Item | Workstream | Effort | Dependency |
|------|-----------|--------|------------|
| G1: Test Re-enablement | G | 4-6 hrs | None |
| G2: IaC Deployment | G | 6-8 hrs | F1 |
| G3: Environment Promotion | G | 4-6 hrs | G2 |
| G4: Slot Integration | G | 2-3 hrs | F3, G3 |

### Total Effort Summary

| Workstream | Items | Effort | Phase |
|-----------|-------|--------|-------|
| A: Architecture Foundation | 2 | 10-14 hrs | 1-2 |
| B: Caching & Performance | 5 | 19-26 hrs | 1-2 |
| C: Dataverse Optimization | 4 | 16-22 hrs | 1, 3 |
| D: Resilience & Operations | 3 | 20-26 hrs | 3 |
| E: AI Pipeline Performance | 2 | 8-12 hrs | 3 |
| F: Azure Infrastructure | 8 | 35-47 hrs | 4 |
| G: CI/CD Pipeline | 4 | 16-23 hrs | 5 |
| **Total** | **28 items** | **124-170 hrs** | **6 weeks** |

---

## 12. Risk Registry

### 12.1 Regression Risks

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|------------|
| DI registration order change breaks startup | Medium | High | Extract in exact order. Full test suite after each extraction. |
| Conditional DI blocks misplaced | Medium | High | Preserve exact `if` conditions. Startup logging of registered modules. |
| Middleware order changes | Low | Critical | Auth before AuthZ. Extract exactly as-is. |
| Cache staleness causes stale file metadata | Medium | Medium | Short TTLs (2-5 min), ETag-versioned keys, cache-aside pattern. |
| Authorization cache allows brief post-revocation access | Low | Medium | 60-second TTL. Acceptable per ADR-003. |
| Feature flag disables needed service | Medium | Medium | Default all to `true`. Document domain dependency graph. |
| Messages lost during queue migration | Low | Medium | Keep old queue active. Idempotent handlers (ADR-004). |
| Budget too restrictive | Medium | Low | Warn-only mode first. 2-week observation. |
| VNet migration causes service disruption | Medium | High | Deploy private endpoints alongside public first. Test. Then disable public. |
| `$batch` support varies across operations | Medium | Low | Test each scenario. Fall back to `Task.WhenAll` where batch fails. |
| Deployment slot swap cold start | Medium | Medium | Warm-up endpoint (`/healthz`) in slot configuration. |

### 12.2 External API Contract Guarantee

**No external API contracts will change.** All endpoint URLs, request/response schemas, HTTP status codes, authentication requirements, rate limiting behavior, and SSE streaming protocols remain identical.

Observable differences:
- New 503 responses if a module is explicitly disabled (requires admin action)
- Improved latency (positive change)
- Improved job processing fairness (positive change)

### 12.3 Rollback Strategy

Each workstream item is independently deployable and reversible:

| Item | Rollback Method |
|------|----------------|
| A1 (Decomposition) | Git revert — no runtime state |
| A2 (Feature flags) | Remove config section — defaults to enabled |
| B1-B4 (Caching) | Set TTL to 0 or disable cache key prefix |
| C1-C4 (Dataverse) | Git revert — no runtime state |
| D1 (Priority queues) | Route all to original queue |
| D2 (Persistent state) | Feature flag `Analysis:Persistence:Enabled` = false |
| D3 (Resource budgets) | Feature flag `ResourceBudgets:EnforcementMode` = "disabled" |
| E1-E2 (AI perf) | Feature flag or git revert |
| F1-F8 (Infrastructure) | Re-enable public endpoints (reverse migration) |
| G1-G4 (CI/CD) | Pipeline config rollback |

---

## 13. Success Criteria

| # | Criterion | Target | Measurement |
|---|-----------|--------|-------------|
| SC-01 | Program.cs line count | < 200 lines | Code metric |
| SC-02 | File listing response (cached) | < 50ms p95 | Application Insights |
| SC-03 | Document download response | < 600ms p95 | Application Insights |
| SC-04 | Chat message response (2 tools) | < 1,500ms p95 | Application Insights |
| SC-05 | Graph metadata cache hit rate | > 85% | Redis metrics + custom telemetry |
| SC-06 | Dataverse entity read response | < 100ms p95 | Application Insights |
| SC-07 | Zero `ColumnSet(true)` in production | 0 instances | Code search |
| SC-08 | Zero thread-safety issues in Dataverse client | 0 issues | Code review |
| SC-09 | Analysis survives App Service restart | 100% recovery | Integration test |
| SC-10 | Critical job pickup latency | < 30 seconds | Service Bus metrics |
| SC-11 | Bulk jobs don't starve critical jobs | 0 critical timeouts during bulk | Load test |
| SC-12 | All Azure services behind private endpoints | 100% | Azure Resource Graph |
| SC-13 | Autoscaling configured and tested | Pass | Scale trigger verification |
| SC-14 | Deployment slots operational | Pass | Staging → prod swap |
| SC-15 | CI/CD runs tests before deployment | Pass | Pipeline logs |
| SC-16 | Disable any domain via config | 503, no crash, others unaffected | Integration test |

---

## 14. ADR Compliance

| ADR | Items | Compliance |
|-----|-------|------------|
| ADR-001 | All | Minimal API preserved. No controllers. |
| ADR-003 | B2 | Cache data, not decisions. Per-request authorization recomputed. |
| ADR-004 | D1 | Priority queues use same job contract. Idempotency preserved. |
| ADR-007 | B1, B3 | All Graph caching goes through SpeFileStore facade. |
| ADR-008 | A2, D3 | Feature flags and budgets implemented as endpoint filters. |
| ADR-009 | B1, B2, B4, D2 | Redis-first. No IMemoryCache except per-request dedup. |
| ADR-010 | A1 | Decomposition improves readability. Registration count unchanged. |
| ADR-013 | E1, E2 | AI module remains part of BFF. No separation. |
| ADR-015 | D3 | Resource budgets log counts, not content. |
| ADR-019 | A2, D3 | Module-disabled 503 and budget-exceeded 429 use ProblemDetails. |

---

## 15. Cost Impact

| Change | Monthly Cost |
|--------|-------------|
| Redis Premium P1 (6 GB) | ~$250 (already budgeted) |
| App Service P1v3 autoscale (2-10 instances) | $146-$730 based on load |
| VNet + Private Endpoints | ~$50-100 |
| AI Search Standard2, 2 replicas | ~$500 (already budgeted) |
| **Savings**: Reduced Azure OpenAI calls (caching) | **-$50-200** |
| **Savings**: Reduced Dataverse API calls (batching) | **-$20-50** |
| Service Bus (2 additional queues) | ~$5 (per-operation pricing) |

---

## 16. Dependencies

| Dependency | Type | Status | Needed By |
|------------|------|--------|-----------|
| Redis production instance | Infrastructure | Available via Bicep | B1, B2, D2 |
| Azure VNet | Infrastructure | Not created | F1 |
| App Service P1v3 plan | Infrastructure | Defined in Bicep | F2, F3 |
| Service Bus queues (2 new) | Infrastructure | Not created | D1 |
| ADR-009 (Redis caching) | Architecture | Approved | B1, B2, D2 |
| ADR-003 (Authorization) | Architecture | Approved | B2 |
| ADR-007 (SpeFileStore facade) | Architecture | Approved | B1 |
| `sprk_analysisresult` entity | Dataverse | Exists, verify schema | D2 |

---

*This document should be transformed to spec.md via `/design-to-spec` when ready for project pipeline initialization.*
