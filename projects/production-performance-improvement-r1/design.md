# Production Performance Improvement R1 — Design Specification

> **Version**: 2.0
> **Date**: 2026-03-11
> **Status**: Draft — Expanded for Beta Readiness
> **Author**: Development Team

---

## 1. Executive Summary

The Spaarke platform exhibits slow response times across BFF API interactions with SharePoint Embedded (Microsoft Graph), Azure AI services, and Dataverse. Analysis reveals that the root causes are not development-only issues — they are architectural gaps in caching, connection management, query optimization, and infrastructure configuration that will persist into production without targeted intervention.

This project delivers production-readiness improvements across **seven domains**:

1. **BFF API Performance** — Implement the Redis caching layers prescribed by ADR-009 but not yet built, optimize Graph client management, and add response caching for the highest-traffic call patterns.

2. **Dataverse Performance** — Fix over-fetching queries (`ColumnSet(true)` / missing `$select`), add request batching, resolve thread-safety issues in the WebAPI client, and optimize PCF control data loading patterns.

3. **Azure Infrastructure** — Correct development-grade Bicep defaults (B1 App Service, Basic Redis, basic AI Search) to production tiers, enable VNet and private endpoints, configure autoscaling, and establish deployment slot strategy.

4. **CI/CD Pipeline** — Re-enable tests, add IaC deployment, establish environment promotion with approval gates.

5. **AI Pipeline Optimization** — Address the #1 user complaint (45+ second analysis times) by parallelizing RAG searches, caching extracted document text, adding timeouts, and tuning OpenAI parameters.

6. **Code Quality & Production Readiness** — Secure 5 unauthenticated endpoint groups, implement 37 missing authorization filters, remove ~5,500 lines of obsolete handler code, and fix mock services returning empty data.

7. **Logging & Observability** — Guard 100 files with unprotected JSON serialization in log calls, remove development debug tags, batch loop logging, and tune per-service log levels.

The combined effect targets a **60-80% reduction in typical API response times**, a **40-60% reduction in AI analysis times** on repeat analyses, and resolves **critical security gaps** blocking beta user access.

---

## 2. Current State Analysis

### 2.1 BFF API — Hot Path Latency Breakdown

Every user request currently traverses multiple external services sequentially with minimal caching:

```
User Request (e.g., list files, open document)
  → JWT Validation .................. 1-5ms
  → Dataverse Authorization Query ... 50-200ms   ← NOT cached across requests
  → OBO Token Exchange .............. 5-200ms    ← Cached in Redis (working well)
  → Graph API Call #1 (metadata) .... 100-300ms  ← NOT cached
  → Graph API Call #2 (content) ..... 100-500ms  ← NOT cached (streaming)
                                      ─────────
  Total:                              ~250ms best → ~1,200ms worst
```

**Key finding**: The OBO token cache is the only production-grade cache currently implemented. Graph metadata, container-to-drive mappings, and authorization data all hit external services on every request.

### 2.2 Dataverse — Query and Access Patterns

| Issue | Impact | Frequency |
|-------|--------|-----------|
| `ColumnSet(true)` in 6 service methods | Returns 30-60+ columns when 5-10 are needed; 3-5x payload overhead | Every document/job/email query |
| Missing `$select` in WebAPI `GetDocumentAsync` | Full entity payload (~60 fields) | Every document access |
| No `ExecuteMultiple` / `$batch` usage | 3+ sequential round-trips where 1 would suffice | Scorecard calculation, multi-entity loads |
| Thread-unsafe token refresh in `DataverseWebApiService` | Redundant token acquisitions under concurrent load | All WebAPI operations |
| Mutable `DefaultRequestHeaders` on shared `HttpClient` | Thread-safety issue under load | All WebAPI operations |
| No pagination handling for unbounded queries | Silent truncation at 5,000 records | Container document listing |
| N+1 query in field mapping profile load | 2 sequential calls instead of `$expand` | Field mapping operations |
| Multiple PCF controls on same form make duplicate queries | No cross-control cache or deduplication | Every form load with 3+ controls |

### 2.3 AI Analysis Pipeline — Latency Breakdown

The #1 user performance complaint. Even small files take 45+ seconds:

```
AI Analysis Request (e.g., clause analysis on 5KB PDF)
  → Document Retrieval .............. 100-200ms   ← Dataverse metadata query
  → Document Intelligence .......... 5-30s       ← WaitUntil.Completed, NO timeout, NO cache
  → RAG Knowledge Search ........... 6-9s        ← SEQUENTIAL (3 sources × 2-3s each)
  → Prompt Building ................ 50-200ms    ← Local string assembly
  → OpenAI Streaming ............... 10-40s      ← Model inference (MaxOutputTokens=1000)
  → Finalization ................... 1-3s        ← Dataverse persist + job queue
                                     ────────
  Total:                             ~21-79s (typical: 45s+)
```

**Key findings**: Document text extraction is never cached (re-extracts on every analysis). RAG searches are sequential, not parallel. No timeout on Document Intelligence — a stuck request blocks indefinitely.

### 2.4 Code Quality — Accumulated Technical Debt

The BFF API is **~75,000 lines across 524 files**, built incrementally across multiple AI-directed projects:

| Issue | Count | Severity |
|-------|-------|----------|
| Unauthenticated endpoints (marked TEMPORARY) | 5 endpoint groups | **CRITICAL — security** |
| Missing authorization filters (TODO Task 033) | 37 TODOs | **CRITICAL — security** |
| Obsolete handlers still registered in DI | 6 handlers (~5,500 lines) | High — dead code, DI bloat |
| God class (ScopeResolverService) | 2,538 lines, 39 public methods | High — maintainability |
| Mock/stub services returning empty data | 3 services | High — user-facing bugs |
| Console.WriteLine in production code | 18 calls | Medium — observability gap |
| Commented-out code | 331 files | Medium — maintainability |
| TODO/FIXME comments | 65 items | Medium — unfinished work |

### 2.5 Logging — Volume and Performance Impact

**3,113 logging calls across 223 files**:

| Pattern | Count | Performance Impact |
|---------|-------|--------------------|
| `JsonSerializer.Serialize()` in log calls (no level guard) | 100 files | 10-20% overhead from unnecessary allocations |
| `[DEBUG]` tags in LogError/LogInformation | 7 instances | Noise in production logs |
| Per-item logging inside loops | 20 files | O(n) log volume for batch operations |
| Methods with 5+ log statements | 172 methods | Chatty traces complicate debugging |
| LogInformation calls (many should be LogDebug) | 1,099 | Excessive production log volume |

### 2.6 Azure Infrastructure — Service Tier Gaps

**UPDATED**: Bicep audit reveals Model 2 defaults are **development-grade**, not production-ready:

| Service | Model 2 Bicep Default | Required for Beta | Gap |
|---------|----------------------|-------------------|-----|
| App Service | **B1 (Basic)** | S1+ (Standard) | B1 has no SLA, shared CPU, can't scale |
| Redis | **Basic C0 (250MB)** | Standard C1+ (1GB) | No HA, no persistence, evicts at 10+ users |
| AI Search | **basic** | standard | Basic lacks semantic search at scale, no HA |
| Azure OpenAI | 10K TPM (dev) / 120 TPM (Bicep) | ≥200K TPM | Severely throttled; capacity mismatch in docs vs code |
| AI Search Replicas | 1 | 2+ | No redundancy; unavailable if node fails |
| Service Bus | Standard | Standard | OK for beta |
| All Services | Public endpoints | Public endpoints | Acceptable for beta; private endpoints for production |

### 2.4 CI/CD Pipeline

| Issue | Impact |
|-------|--------|
| Tests disabled in deployment pipeline | No quality gate before deploy |
| No infrastructure deployment via Bicep in CI/CD | Manual infrastructure management |
| No environment promotion (dev → staging → prod) | Direct-to-dev deployment only |
| Integration tests are placeholder stubs | No automated regression validation |
| No deployment slots used | Downtime during deployments |

---

## 3. Scope

### 3.1 In Scope

**Domain A: BFF API Caching & Connection Optimization**
- Graph metadata response caching in Redis (ADR-009 implementation)
- Container-to-Drive ID mapping cache
- Authorization data snapshot caching (cache data, not decisions per ADR-009)
- GraphServiceClient pooling for app-only operations
- Removal of debug/temporary endpoints from production builds
- Response compression and HTTP/2 optimization

**Domain B: Dataverse Query & Access Optimization**
- Replace all `ColumnSet(true)` with explicit column sets
- Add `$select` to all WebAPI queries missing it
- Implement `$batch` for multi-query operations (scorecard calculator, etc.)
- Fix thread-safety issues in `DataverseWebApiService` token refresh
- Fix mutable `DefaultRequestHeaders` pattern
- Add pagination support for unbounded queries
- Use `$expand` for parent/child entity loads (field mapping profiles)
- Remove `[DATAVERSE-DEBUG]` logging from production code paths

**Domain C: Azure Infrastructure Production Readiness**
- VNet creation with subnets for App Service, Redis, and private endpoints
- Private endpoints for Key Vault, Storage, Service Bus, OpenAI, AI Search, Document Intelligence
- App Service autoscaling rules (CPU, memory, HTTP queue)
- Deployment slot configuration for zero-downtime deployments
- Redis VNet injection and RDB persistence
- Key Vault network restriction and secret rotation policy
- Storage account shared key access disablement
- Delete deprecated AI Search index (`spaarke-knowledge-index`)
- OpenAI PTU evaluation for predictable workloads

**Domain D: CI/CD & Deployment Pipeline**
- Re-enable test suite in deployment pipeline
- Add Bicep infrastructure deployment using parameter files
- Environment-based deployment with approval gates (dev → staging → prod)
- OIDC-based authentication for all deployment environments
- Deployment slot swap strategy for production

**Domain E: AI Pipeline Optimization**
- Parallelize RAG knowledge source searches (sequential → `Task.WhenAll`)
- Cache extracted document text in Redis (eliminate repeat Document Intelligence calls)
- Add Document Intelligence timeout and cancellation
- Tune OpenAI parameters (MaxOutputTokens, model selection per action complexity)
- Cache RAG search results with document+query composite keys

**Domain F: Code Quality & Production Readiness**
- Remove or secure 5 unauthenticated API endpoint groups (marked TEMPORARY)
- Implement 37 missing authorization filters in OfficeEndpoints (Task 033 TODOs)
- Remove 6 obsolete tool handlers (~5,500 lines) or complete JPS migration
- Refactor ScopeResolverService god class (39 public methods → focused services)
- Replace mock/stub Workspace services with real implementations or remove endpoints
- Fix localhost CORS fallback to fail-fast in production
- Replace 18 `Console.WriteLine` calls in Program.cs with structured logging
- Remove commented-out code and resolve critical TODO/FIXME items

**Domain G: Logging & Observability Optimization**
- Guard all `JsonSerializer.Serialize()` log calls with `IsEnabled()` checks (100 files)
- Remove `[DEBUG]` tags from production log messages (7 instances)
- Move per-item loop logging to batch summaries (20 files)
- Tune per-service log levels for production vs development
- Demote verbose `LogInformation` calls to `LogDebug` where appropriate
- Remove string allocations from log parameters (Substring, concatenation)

### 3.2 Out of Scope

- PCF React 16 remediation (separate project: `pcf-react-16-remediation`)
- Full VNet hub-spoke topology for multi-region deployment
- Azure Front Door / CDN integration
- Database-level Dataverse optimization (index creation requires Microsoft support)
- New feature development
- Load testing / performance benchmarking (recommended as follow-up project)

---

## 4. Deliverables

### Domain A: BFF API Caching & Connection Optimization

#### A1. Graph Metadata Cache

Implement distributed Redis cache for Graph API responses per ADR-009.

| Cache Target | Key Pattern | TTL | Invalidation |
|-------------|-------------|-----|--------------|
| File metadata (name, size, ETag, dates) | `sdap:graph:metadata:{driveId}:{itemId}:v{etag}` | 5 min | ETag-versioned keys auto-expire |
| Folder listing (children) | `sdap:graph:children:{driveId}:{folderId}:v{hash}` | 2 min | Short TTL for freshness |
| Container-to-Drive ID mapping | `sdap:graph:drive:{containerId}` | 24 hr | Stable mapping, long TTL |
| Drive item permissions | `sdap:graph:perms:{driveId}:{itemId}` | 5 min | Short TTL for security sensitivity |

**Expected impact**: Eliminates 100-300ms per request for cached files. At 90%+ cache hit rate, most metadata operations drop from 200-500ms to ~5ms.

#### A2. Authorization Data Snapshot Cache

Cache the underlying authorization data (user roles, team memberships, security profile) while still computing authorization decisions fresh per request (per ADR-009 and ADR-003).

| Cache Target | Key Pattern | TTL | Notes |
|-------------|-------------|-----|-------|
| User role assignments | `sdap:auth:roles:{userOid}` | 2 min | Short TTL; security-sensitive |
| User team memberships | `sdap:auth:teams:{userOid}` | 2 min | Rarely changes mid-session |
| Resource access data | `sdap:auth:access:{userOid}:{resourceId}` | 60 sec | Very short; per-resource |

**Expected impact**: Reduces 50-200ms authorization overhead to ~5ms for cached users (most requests).

#### A3. GraphServiceClient Pooling

Replace per-call `GraphServiceClient` instantiation in `GraphClientFactory.ForApp()` with a cached singleton instance for app-only operations. The underlying `HttpClient` is already pooled via `IHttpClientFactory`, but the credential and auth provider objects are recreated unnecessarily.

#### A4. Debug Endpoint Removal

Conditionally exclude debug and temporary endpoints from non-development environments:
- `/debug/*` endpoints
- Temporary `/healthz/dataverse/doc/{id}` with `AllowAnonymous`
- Any `localhost` references in web resources

---

### Domain B: Dataverse Query Optimization

#### B1. Explicit Column Selection

Replace all `ColumnSet(true)` usage with explicit column lists in `DataverseServiceClientImpl`:

| Method | Current | Fix |
|--------|---------|-----|
| `GetDocumentAsync` | `ColumnSet(true)` (~60 cols) | Explicit ~8 columns needed |
| `GetProcessingJobAsync` | `ColumnSet(true)` (~30 cols) | Explicit ~10 columns needed |
| `GetProcessingJobByIdempotencyKeyAsync` | `ColumnSet(true)` | Explicit ~6 columns needed |
| `GetEmailArtifactAsync` | `ColumnSet(true)` | Explicit ~12 columns needed |
| `GetAttachmentArtifactAsync` | `ColumnSet(true)` | Explicit ~8 columns needed |
| `GetDocumentAsync` (WebAPI) | No `$select` | Add `$select` with required fields |

**Expected impact**: 60-80% reduction in Dataverse response payload sizes; measurable latency improvement on large entities.

#### B2. Request Batching

Implement `$batch` OData endpoint usage for operations that currently make multiple sequential calls:

| Operation | Current | Batched |
|-----------|---------|---------|
| Scorecard calculation (3 KPI area queries) | 3 sequential HTTP calls | 1 `$batch` request |
| Field mapping profile + rules | 2 sequential calls | 1 call with `$expand` |
| Multi-entity authorization checks | N sequential calls | `$batch` where applicable |

#### B3. Thread-Safety Fixes

Fix `DataverseWebApiService`:
- Replace mutable `_currentToken` field with `SemaphoreSlim`-guarded refresh pattern
- Replace `DefaultRequestHeaders.Authorization` mutation with per-request `HttpRequestMessage` headers
- Both fixes are correctness issues, not just performance, and should be addressed before production

#### B4. Pagination Support

Add paging cookie support to `GetDocumentsByContainerAsync` and any other unbounded queries to prevent silent truncation at the Dataverse 5,000-record page limit.

---

### Domain C: Azure Infrastructure

#### C0. Bicep Tier Corrections (Priority 0 — Beta Blocker)

The Model 2 Bicep template (`infrastructure/bicep/stacks/model2-full.bicep`) defaults to development-grade tiers. These must be corrected before any beta deployment:

| Parameter | Current Default | Required Default | File:Line |
|-----------|----------------|------------------|-----------|
| `appServiceSku` | `B1` | `S1` | `model2-full.bicep:31` |
| Redis `sku` | `Basic` | `Standard` | `model2-full.bicep:114` |
| Redis `capacity` | `0` (C0, 250MB) | `1` (C1, 1GB) | `model2-full.bicep:115` |
| `aiSearchSku` | `basic` | `standard` | `model2-full.bicep:35` |
| AI Search `replicaCount` | `1` | `2` | `model2-full.bicep:261` |
| OpenAI `gpt-4o-mini` capacity | `120` | `200+` | `model2-full.bicep:241` |

**Effort**: 30 minutes (parameter changes + redeploy).

#### C1. Network Isolation (Post-Beta — Security)

Currently, **all Azure services use public endpoints with no network restrictions**. This is acceptable for initial beta but must be addressed before broader production access.

**Deliverables**:
- Create VNet with 3 subnets: `snet-app` (App Service integration), `snet-redis` (Redis VNet injection), `snet-pe` (private endpoints)
- Deploy private endpoints for: Key Vault, Storage Account, Service Bus, Azure OpenAI, AI Search, Document Intelligence
- Enable VNet integration on App Service
- Disable public access on all resources once private endpoints are confirmed
- Add NSG rules for inter-service communication

#### C2. App Service Autoscaling

Configure autoscale rules for the P1v3 App Service Plan:

| Metric | Scale Out Threshold | Scale In Threshold | Cooldown |
|--------|-------------------|--------------------|----------|
| CPU % | > 70% for 5 min | < 30% for 10 min | 5 min |
| Memory % | > 80% for 5 min | < 40% for 10 min | 5 min |
| HTTP Queue Length | > 100 for 2 min | < 10 for 10 min | 5 min |

- Min instances: 2 (availability)
- Max instances: 10 (cost control)
- Default: 2

#### C3. Deployment Slots

Configure staging deployment slot on P1v3 plan:
- Deploy to staging slot
- Run health checks against staging (`/healthz`)
- Swap staging → production (zero downtime)
- Auto-swap disabled (require manual approval or CI/CD gate)

#### C4. Redis Production Hardening

- Enable VNet injection (Premium P1 supports this)
- Enable RDB persistence (snapshot every 15 min)
- Configure `maxmemory-policy: allkeys-lru` (allow eviction when full)
- Disable public network access

#### C5. Key Vault Hardening

- Restrict network access to VNet + App Service outbound IPs
- Enable secret rotation policy for client secrets
- Audit role assignments for least privilege

#### C6. Storage Account Hardening

- Disable shared key access (`allowSharedKeyAccess: false`)
- Restrict network access to VNet
- Add lifecycle management policy for `ai-chunks` container (move to Cool after 30 days)

#### C7. OpenAI Capacity Planning

- Evaluate Provisioned Throughput Units (PTU) for gpt-4o and gpt-4o-mini based on projected usage
- Restrict network access to VNet / private endpoint
- Remove deprecated `text-embedding-3-small` deployment
- Document model upgrade strategy

#### C8. AI Search Cleanup

- Delete deprecated `spaarke-knowledge-index` (1536-dim, replaced by v2)
- Evaluate increasing to 3 replicas for 99.9% write SLA
- Restrict public access

---

### Domain D: CI/CD Pipeline

#### D1. Test Suite Re-enablement

- Fix interface updates that caused test disablement
- Re-enable `dotnet test` in the deployment pipeline
- Add test results as a deployment gate (fail = no deploy)

#### D2. Infrastructure as Code Deployment

- Add Bicep deployment step to CI/CD pipeline
- Use `dev.bicepparam`, `staging.bicepparam`, `prod.bicepparam` for environment targeting
- Add `what-if` preview step for infrastructure changes

#### D3. Environment Promotion

- Define environment progression: dev → staging → prod
- Add manual approval gate for staging → prod promotion
- Enable OIDC-based auth for all environments

#### D4. Deployment Slot Integration

- Update CI/CD to deploy to staging slot
- Add post-deployment health check
- Add slot swap step with manual approval

---

### Domain E: AI Pipeline Optimization

The AI analysis pipeline is the **#1 user performance complaint** (45+ seconds for small files). Root causes are sequential processing, missing caches, and no timeouts on external AI services.

#### E1. Parallelize RAG Knowledge Searches

The `AnalysisOrchestrationService.ProcessRagKnowledgeAsync()` searches RAG knowledge sources **sequentially** in a `foreach` loop. Each Azure AI Search query takes 2-3 seconds.

```csharp
// Current (sequential — ~8 seconds for 3 sources):
foreach (var source in knowledge)
{
    var searchResult = await _ragService.SearchAsync(ragQuery.SearchText, searchOptions, ct);
}

// Fix (parallel — ~3 seconds for 3 sources):
var searchTasks = knowledge.Select(source => SearchSourceAsync(source, ct));
var results = await Task.WhenAll(searchTasks);
```

**Expected impact**: Saves 5-8 seconds per analysis with multiple knowledge sources.

#### E2. Cache Extracted Document Text

Document Intelligence extraction (5-30 seconds) runs on **every analysis** of the same document, even when the document hasn't changed. Cache the extracted text in Redis using document ID + ETag versioning.

| Cache Target | Key Pattern | TTL | Invalidation |
|-------------|-------------|-----|--------------|
| Extracted text (Doc Intelligence) | `sdap:ai:text:{driveId}:{itemId}:v{etag}` | 24 hr | ETag-versioned keys auto-expire |
| Native text (UTF-8 read) | `sdap:ai:text:{driveId}:{itemId}:v{etag}` | 24 hr | Same pattern, different extractor |

**Expected impact**: Eliminates 5-30 seconds on repeat analysis of same document (common workflow: user runs multiple analysis types on same file).

#### E3. Document Intelligence Timeout & Resilience

`TextExtractorService.ExtractViaDocIntelAsync()` uses `WaitUntil.Completed` with **no timeout**. A stuck Document Intelligence request blocks the analysis indefinitely.

**Fixes**:
- Add 30-second timeout via `CancellationTokenSource.CreateLinkedTokenSource()`
- Add circuit breaker (Polly) for Document Intelligence calls
- Return graceful degradation message if extraction times out

#### E4. OpenAI Parameter Tuning

| Parameter | Current | Proposed | Impact |
|-----------|---------|----------|--------|
| `MaxOutputTokens` | 1000 | 500 (configurable per action) | Saves 2-5 seconds on inference |
| `Temperature` | 0.3 | 0.3 (no change) | — |
| Model selection | Always `gpt-4o-mini` | Per-action: simple → `gpt-4o-mini`, complex → `gpt-4o` | Better quality/speed tradeoff |

#### E5. Cache RAG Search Results

Cache RAG search results by document+query composite key to avoid redundant Azure AI Search calls when the same document is analyzed multiple times with similar queries.

| Cache Target | Key Pattern | TTL | Notes |
|-------------|-------------|-----|-------|
| RAG search results | `sdap:ai:rag:{indexName}:{queryHash}` | 15 min | Short TTL; results may change with index updates |

**Expected impact**: Saves 2-8 seconds on repeat/similar analyses.

#### AI Analysis — Before vs. After

| Step | Current | After Optimization |
|------|---------|-------------------|
| Document text extraction | 5-30s (Doc Intelligence) | **~5ms** (cached) or 5-30s (first run) |
| RAG knowledge search (3 sources) | 6-9s (sequential) | **2-3s** (parallel) |
| OpenAI inference | 10-40s | **8-25s** (reduced output tokens) |
| **Total (first analysis)** | **21-79s** | **~15-58s** |
| **Total (repeat analysis, cached)** | **21-79s** | **~10-28s** |

---

### Domain F: Code Quality & Production Readiness

Code audit reveals **critical security gaps and dead code** accumulated across multiple AI-directed development projects. These must be resolved before beta user access.

#### F1. Secure Unauthenticated Endpoints (CRITICAL — Security Blocker)

**5 API endpoint groups** are marked `TEMPORARY` with `.AllowAnonymous()`:

| Endpoint Group | File | Line | Status |
|---------------|------|------|--------|
| `/api/ai/playbook-builder` | `Api/Ai/AiPlaybookBuilderEndpoints.cs` | 24 | `.AllowAnonymous()` |
| `/api/ai/playbooks` | `Api/Ai/PlaybookEndpoints.cs` | 20 | `.AllowAnonymous()` |
| `/api/ai/playbooks/{id}/nodes` | `Api/Ai/NodeEndpoints.cs` | 19 | `.AllowAnonymous()` |
| `/api/ai/playbooks/{id}/...` | `Api/Ai/PlaybookRunEndpoints.cs` | 25 | `.AllowAnonymous()` |
| `/api/ai/playbooks/runs` | `Api/Ai/PlaybookRunEndpoints.cs` | 29 | `.AllowAnonymous()` |

**Fix**: Restore `.RequireAuthorization()` and implement MSAL auth or API key authentication for PlaybookBuilderHost PCF control.

#### F2. Implement Missing Authorization Filters (CRITICAL)

**37 TODO comments** in `Api/Office/OfficeEndpoints.cs` reference unimplemented authorization filters:

```csharp
// TODO: Task 033 - .AddOfficeAuthFilter()
// TODO: Task 033 - .AddJobOwnershipFilter()
```

**Fix**: Implement `OfficeAuthFilter` and `JobOwnershipFilter` endpoint filters per ADR-008 pattern, or apply existing authorization patterns from other endpoint groups.

#### F3. Remove Obsolete Tool Handlers (~5,500 lines)

6 tool handlers marked `[Obsolete("Use GenericAnalysisHandler with JPS configuration")]` are still registered in DI and referenced by name in orchestration services:

| Handler | Lines | Status |
|---------|-------|--------|
| `ClauseAnalyzerHandler.cs` | 932 | Obsolete, still registered |
| `DateExtractorHandler.cs` | 809 | Obsolete, still registered |
| `EntityExtractorHandler.cs` | ~800 | Obsolete, **still referenced in AnalysisOrchestrationService** |
| `FinancialCalculatorHandler.cs` | 929 | Obsolete, still registered |
| `RiskDetectorHandler.cs` | 932 | Obsolete, still registered |
| `ClauseComparisonHandler.cs` | 958 | Likely obsolete, not decorated |

**Fix**: Verify all JPS configurations exist for each handler, remove handlers, update DI registrations, remove string-based references in orchestration services.

#### F4. Refactor ScopeResolverService God Class

`Services/Ai/ScopeResolverService.cs` — **2,538 lines, 39 public methods** — violates single responsibility:

**Split into**:
- `AnalysisActionService` — CRUD for Actions
- `AnalysisSkillService` — CRUD for Skills
- `AnalysisKnowledgeService` — CRUD for Knowledge
- `AnalysisToolService` — CRUD for Tools
- `ScopeResolverService` — scope resolution only (original purpose)

#### F5. Replace Mock Workspace Services

Several Workspace services return empty/mock data with TODO comments:

| Service | File | Issue |
|---------|------|-------|
| `WorkspaceAiService` | `Services/Workspace/WorkspaceAiService.cs:103` | TODO: Replace with real Dataverse query |
| `TodoGenerationService` | `Services/Workspace/TodoGenerationService.cs` | 8 TODOs for Dataverse queries |
| `PortfolioService` | `Services/Workspace/PortfolioService.cs:242` | Mock implementations |

**Fix**: Implement real Dataverse queries or remove endpoints from production builds (conditional compilation).

#### F6. Production Safety Fixes

| Issue | File | Fix |
|-------|------|-----|
| Localhost CORS fallback | `Program.cs:809-820` | Change to fail-fast (throw) if no CORS origins configured |
| 18 `Console.WriteLine` calls | `Program.cs:385-720` | Replace with `ILogger` structured logging |
| Debug endpoints accessible | Various `/debug/*` routes | Exclude via `#if DEBUG` or environment check |
| Scorecard endpoint anonymous | `ScorecardCalculatorEndpoints.cs:25` | Implement API key or service auth |

---

### Domain G: Logging & Observability Optimization

The BFF API contains **3,113 logging calls across 223 files**. While structured logging is used correctly (no string interpolation), there are performance-impacting patterns and development artifacts that affect both performance and maintainability.

#### G1. Guard Serialization in Log Calls (CRITICAL — Performance)

**100 files** use `JsonSerializer.Serialize()` inside log calls without checking if the log level is enabled:

```csharp
// Current (allocates even when Debug is disabled):
_logger.LogDebug("Tool result: {Data}", JsonSerializer.Serialize(toolResult));

// Fix (zero-cost when Debug is disabled):
if (_logger.IsEnabled(LogLevel.Debug))
    _logger.LogDebug("Tool result: {Data}", JsonSerializer.Serialize(toolResult));
```

**Top offenders**: `BuilderToolExecutor.cs` (14 calls), `OfficeService.cs` (5), `DocumentCheckoutService.cs` (4), `AnalysisOrchestrationService.cs` (4).

**Expected impact**: Eliminates 10-20% overhead from unnecessary heap allocations in hot paths.

#### G2. Remove Development Log Tags

**7 instances** of `[DEBUG]` prefix in production log messages (all in `Program.cs`):

```csharp
// Current:
logger.LogError(ex, "[DEBUG] Error peeking office DLQ");

// Fix: Move to LogDebug or remove entirely
logger.LogDebug(ex, "Error peeking office DLQ");
```

#### G3. Batch Loop Logging

**20 files** emit per-item logs inside `foreach` loops. Example from `IncomingAssociationResolver.cs`:

```csharp
// Current (logs per regex pattern per email):
foreach (var pattern in MatterReferencePatterns)
{
    _logger.LogDebug("Extracted reference '{Reference}' from subject '{Subject}'", ...);
}

// Fix (batch summary):
_logger.LogDebug("Tested {PatternCount} patterns against subject, {MatchCount} matched",
    patterns.Length, matches.Count);
```

#### G4. Production Log Level Configuration

Current `appsettings.json` runs at `Information` level for all namespaces. Add per-service tuning:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.Graph": "Warning",
      "Sprk.Bff.Api.Services.Ai.ScopeResolverService": "Warning",
      "Sprk.Bff.Api.Services.Ai.AnalysisOrchestrationService": "Warning",
      "Sprk.Bff.Api.Infrastructure.Graph.DriveItemOperations": "Warning"
    }
  }
}
```

**Top 3 chattiest services** (combined 330 log statements): `ScopeResolverService` (142), `AnalysisOrchestrationService` (106), `DriveItemOperations` (82).

#### G5. Remove String Allocations from Log Parameters

Replace string operations in log parameters with cheap alternatives:

```csharp
// Current (allocates substring on every call):
_logger.LogInformation("Body preview: {Preview}",
    request.Email.Body?.Substring(0, 50) ?? "(empty)");

// Fix (no allocation):
_logger.LogInformation("Body present: {HasBody}, Length: {Length}",
    !string.IsNullOrEmpty(request.Email.Body), request.Email.Body?.Length ?? 0);
```

---

## 5. Expected Performance Impact

### Before vs. After — Typical Hot Paths

#### File Listing (most common operation)

| Step | Current | After Optimization |
|------|---------|-------------------|
| JWT Validation | 1-5ms | 1-5ms |
| Authorization (Dataverse) | 50-200ms | **3-5ms** (cached data snapshots) |
| OBO Token | 5ms (cached) | 5ms (cached) |
| Graph: Resolve Drive | 100-300ms | **3-5ms** (cached mapping) |
| Graph: List Children | 100-500ms | **3-5ms** (cached listing) |
| **Total** | **256-1,010ms** | **~15-25ms** (cached) |

#### Document Download

| Step | Current | After Optimization |
|------|---------|-------------------|
| JWT Validation | 1-5ms | 1-5ms |
| Authorization (Dataverse) | 50-200ms | **3-5ms** (cached) |
| OBO Token | 5ms (cached) | 5ms (cached) |
| Graph: Metadata | 100-300ms | **3-5ms** (cached) |
| Graph: Content Stream | 100-500ms | 100-500ms (must hit Graph) |
| **Total** | **256-1,010ms** | **~112-520ms** |

#### Dataverse Entity Read

| Step | Current | After Optimization |
|------|---------|-------------------|
| Query with `ColumnSet(true)` | 80-150ms | **40-80ms** (explicit columns, ~50% payload reduction) |
| Query with missing `$select` | 80-150ms | **40-80ms** (explicit `$select`) |
| Scorecard (3 parallel queries) | 3 × 80ms = 240ms | **~100ms** (single `$batch`) |

### Summary

| Scenario | Current Latency | Projected Latency | Improvement |
|----------|----------------|-------------------|-------------|
| File listing (cached) | 250-1,000ms | 15-25ms | **90-97%** |
| File listing (cold) | 250-1,000ms | 250-1,000ms | Baseline (first hit) |
| Document download | 250-1,000ms | 110-520ms | **45-55%** |
| Dataverse entity read | 80-150ms | 40-80ms | **~50%** |
| Multi-query operations | 240ms+ | ~100ms | **~60%** |
| Form load (5 PCF controls) | 500-2,000ms | 200-500ms | **60-75%** |

---

## 6. Priority and Phasing

### Phase 1: Beta Blockers (Must Complete Before User Access)

| Item | Domain | Effort | Impact |
|------|--------|--------|--------|
| **C0. Bicep tier corrections** | Infrastructure | Low | **CRITICAL** — system fails at 10+ users without this |
| **F1. Secure unauthenticated endpoints** | Code Quality | Medium | **CRITICAL** — security blocker for any user access |
| **F2. Implement authorization filters** | Code Quality | Medium | **CRITICAL** — 37 unprotected Office endpoints |
| B3. Thread-safety fixes | Dataverse | Low | **Critical** — correctness bug under concurrent load |
| F6. Production safety fixes | Code Quality | Low | High — CORS fallback, Console.WriteLine, debug endpoints |

### Phase 2: Quick Performance Wins (Highest Impact, Lowest Risk)

| Item | Domain | Effort | Impact |
|------|--------|--------|--------|
| **E1. Parallelize RAG searches** | AI Pipeline | Low | **High** — saves 5-8s per analysis |
| **E3. Document Intelligence timeout** | AI Pipeline | Low | **High** — prevents infinite waits |
| **E4. OpenAI parameter tuning** | AI Pipeline | Config | Medium — saves 2-5s per analysis |
| B1. Explicit column selection | Dataverse | Low | Medium — reduces payload 60-80% |
| A3. GraphServiceClient pooling | BFF API | Low | Low-Medium — reduces object allocation |
| A4. Debug endpoint removal | BFF API | Low | Low — security and cleanliness |
| G2. Remove [DEBUG] log tags | Logging | Low | Low — log hygiene |

### Phase 3: Core Caching (Highest Impact, Moderate Effort)

| Item | Domain | Effort | Impact |
|------|--------|--------|--------|
| **E2. Cache extracted document text** | AI Pipeline | Medium | **High** — saves 5-30s on repeat analysis |
| A1. Graph metadata cache | BFF API | Medium | **High** — 90%+ latency reduction on cached ops |
| A2. Authorization data cache | BFF API | Medium | **High** — eliminates biggest per-request cost |
| **E5. Cache RAG search results** | AI Pipeline | Medium | Medium — saves 2-8s on repeat queries |
| B2. Request batching | Dataverse | Medium | Medium — reduces round-trips |
| B4. Pagination support | Dataverse | Low | Medium — prevents data loss at scale |

### Phase 4: Code Quality & Logging (Maintainability)

| Item | Domain | Effort | Impact |
|------|--------|--------|--------|
| **G1. Guard serialization in log calls** | Logging | Medium | Medium — 10-20% overhead reduction in hot paths |
| **G3. Batch loop logging** | Logging | Low | Low-Medium — reduces log noise |
| **G4. Production log level config** | Logging | Low | Low — operational clarity |
| **G5. Remove string allocations in logs** | Logging | Low | Low — micro-optimization |
| **F3. Remove obsolete handlers** | Code Quality | Medium | Medium — removes ~5,500 lines dead code |
| **F5. Replace mock Workspace services** | Code Quality | Medium | Medium — prevents user-facing empty results |
| C8. AI Search cleanup | Infrastructure | Low | Low — cost savings |

### Phase 5: Infrastructure Hardening

| Item | Domain | Effort | Impact |
|------|--------|--------|--------|
| C1. Network isolation (VNet + PE) | Infrastructure | High | **Critical** — security for broader production |
| C2. Autoscaling | Infrastructure | Medium | High — availability under load |
| C3. Deployment slots | Infrastructure | Medium | High — zero-downtime deploys |
| C4. Redis hardening | Infrastructure | Medium | Medium — durability and security |
| C5-C7. Service hardening | Infrastructure | Medium | Medium — security posture |

### Phase 6: CI/CD Maturity & Refactoring

| Item | Domain | Effort | Impact |
|------|--------|--------|--------|
| D1. Test re-enablement | CI/CD | Medium | High — quality gate |
| D2. IaC deployment | CI/CD | Medium | High — repeatable infrastructure |
| D3. Environment promotion | CI/CD | Medium | High — controlled releases |
| D4. Slot integration | CI/CD | Low | Medium — zero-downtime in CI/CD |
| **F4. Refactor ScopeResolverService** | Code Quality | High | Medium — maintainability (post-beta) |

---

## 7. Risks and Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Cache staleness causes stale file metadata | Medium | Medium | Short TTLs (2-5 min), ETag-versioned keys, cache-aside pattern |
| Authorization cache allows brief access after permission revocation | Medium | Low | 60-second TTL on resource access data; acceptable per ADR-003 |
| VNet migration causes service disruption | High | Medium | Deploy private endpoints alongside public first; test; then disable public |
| `$batch` support varies across Dataverse operations | Low | Medium | Test each batch scenario; fall back to parallel `Task.WhenAll` where batch fails |
| Autoscaling costs exceed budget | Medium | Low | Set max instance count; configure alerts at 70% of max |
| Deployment slot swap causes cold start | Medium | Medium | Enable warm-up endpoint (`/healthz`) in slot configuration |
| Removing obsolete handlers breaks existing analysis actions | High | Medium | Verify JPS configuration exists for each handler before removal; keep handler code in git history |
| Parallel RAG searches overwhelm AI Search | Low | Low | AI Search Standard with 2 replicas handles concurrent queries; add semaphore if needed |
| Auth filter implementation breaks existing Office integrations | High | Medium | Test each endpoint with Office Add-in before removing AllowAnonymous; staged rollout |
| Cached document text becomes stale after re-upload | Low | Low | ETag-versioned cache keys auto-invalidate when document changes |
| Beta users hit OpenAI rate limits | High | Medium | Verify actual TPM capacity; implement client-side retry with backoff; monitor token burn rate |
| Infrastructure tier upgrades increase monthly costs | Medium | High | Standard C1 Redis +$50/mo, Standard AI Search +$250/mo, S1 App Service +$70/mo — total ~$370/mo increase; budget approval needed |

---

## 8. Success Criteria

| # | Criterion | Target | Measurement |
|---|-----------|--------|-------------|
| SC-01 | File listing response time (cached) | < 50ms p95 | Application Insights request telemetry |
| SC-02 | Document download response time | < 600ms p95 | Application Insights |
| SC-03 | Dataverse entity read response time | < 100ms p95 | Application Insights |
| SC-04 | Graph metadata cache hit rate | > 85% | Redis metrics + custom telemetry |
| SC-05 | Zero `ColumnSet(true)` usage in production code | 0 instances | Code search |
| SC-06 | All Azure services behind private endpoints | 100% | Azure Resource Graph query |
| SC-07 | Autoscaling configured and tested | Pass | Scale-out triggers verified |
| SC-08 | Deployment slots operational | Pass | Staging → prod swap demonstrated |
| SC-09 | CI/CD pipeline runs tests before deployment | Pass | Pipeline execution logs |
| SC-10 | No thread-safety issues in Dataverse client | 0 issues | Code review + load test |
| SC-11 | No debug endpoints in non-dev environments | 0 endpoints | Environment-conditional build verification |
| SC-12 | AI analysis time (repeat, cached text) | < 30s p95 | Application Insights custom telemetry |
| SC-13 | AI analysis time (first run, small file) | < 45s p95 | Application Insights |
| SC-14 | Zero unauthenticated endpoints in production | 0 instances | Code search for `.AllowAnonymous()` |
| SC-15 | All authorization filters implemented | 0 TODO Task 033 | Code search |
| SC-16 | Zero obsolete handlers in DI | 0 `[Obsolete]` handlers registered | Code review |
| SC-17 | Zero unguarded JSON serialization in logs | 0 instances | Code search for `Serialize` in log calls without `IsEnabled` guard |
| SC-18 | Bicep defaults production-grade | All tiers ≥ Standard | Bicep parameter audit |
| SC-19 | Document text cache hit rate (repeat analysis) | > 80% | Redis metrics |
| SC-20 | Zero `Console.WriteLine` in production code | 0 instances | Code search |

---

## 9. Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| Redis (production instance) | Infrastructure | Available via Bicep | **Requires tier upgrade from Basic to Standard** |
| Azure VNet | Infrastructure | Not created | Requires Bicep deployment (Phase 5) |
| App Service plan | Infrastructure | Defined in Bicep | **Requires upgrade from B1 to S1** |
| AI Search | Infrastructure | Defined in Bicep | **Requires upgrade from basic to standard** |
| Azure OpenAI capacity | Infrastructure | Partially configured | **Requires TPM verification and increase** |
| ADR-009 (Redis-first caching) | Architecture | Approved | Provides caching patterns |
| ADR-003 (Authorization) | Architecture | Approved | Defines cacheable vs non-cacheable boundaries |
| ADR-007 (SpeFileStore facade) | Architecture | Approved | All Graph caching goes through facade |
| ADR-008 (Endpoint filters) | Architecture | Approved | Pattern for F1/F2 authorization filter implementation |
| ADR-013 (AI Architecture) | Architecture | Approved | Guides E1-E5 AI pipeline changes |
| JPS migration completeness | Code | In Progress | F3 (remove obsolete handlers) depends on all JPS configs existing |
| Task 033 (Office auth filters) | Code | Not Started | F2 depends on filter implementation design |

---

## 10. Non-Functional Considerations

### Observability

- Add cache hit/miss telemetry to Application Insights custom metrics
- Add Dataverse query duration tracking (custom dependency telemetry)
- Add Graph API call duration tracking per operation type
- Dashboard for cache effectiveness, p50/p95/p99 latency, error rates

### Cost Impact

| Change | Cost Effect |
|--------|------------|
| Redis Basic C0 → Standard C1 (1 GB) | +$50/month (from ~$17 to ~$67) |
| App Service B1 → S1 (Standard) | +$70/month (from ~$55 to ~$125) |
| AI Search basic → standard (2 replicas) | +$500/month (from ~$75 to ~$575) |
| VNet + Private Endpoints (Phase 5) | ~$50-100/month for endpoint hours |
| Azure OpenAI capacity increase (120 → 200+ TPM) | Marginal (~$0 unless PTU) |
| Reduced Azure OpenAI calls (E2/E5 caching) | **Savings** — fewer redundant calls |
| Reduced Document Intelligence calls (E2 text caching) | **Savings** — major; $1.50/1000 pages avoided on repeat analysis |
| Reduced Dataverse API calls (batching) | **Savings** — fewer round-trips |
| Reduced AI Search calls (E5 RAG caching) | **Savings** — fewer search units consumed |
| **Net monthly increase for beta** | **~$620/month** (infrastructure tiers + VNet) |

### Security

All infrastructure changes in Domain C improve security posture. Network isolation is a **prerequisite for production deployment** — not optional.

---

*End of design specification.*
