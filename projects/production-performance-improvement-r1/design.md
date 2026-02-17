# Production Performance Improvement R1 — Design Specification

> **Version**: 1.0
> **Date**: 2026-02-17
> **Status**: Draft — Ready for Review
> **Author**: Development Team

---

## 1. Executive Summary

The Spaarke platform exhibits slow response times across BFF API interactions with SharePoint Embedded (Microsoft Graph), Azure AI services, and Dataverse. Analysis reveals that the root causes are not development-only issues — they are architectural gaps in caching, connection management, query optimization, and infrastructure configuration that will persist into production without targeted intervention.

This project delivers production-readiness improvements across three domains:

1. **BFF API Performance** — Implement the Redis caching layers prescribed by ADR-009 but not yet built, optimize Graph client management, and add response caching for the highest-traffic call patterns.

2. **Dataverse Performance** — Fix over-fetching queries (`ColumnSet(true)` / missing `$select`), add request batching, resolve thread-safety issues in the WebAPI client, and optimize PCF control data loading patterns.

3. **Azure Infrastructure** — Right-size service tiers for production, enable VNet and private endpoints, configure autoscaling, and establish deployment slot strategy.

The combined effect targets a **60-80% reduction in typical API response times** and establishes the infrastructure foundation for production deployment.

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

### 2.3 Azure Infrastructure — Service Tier Gaps

| Service | Dev (Live) | Prod (IaC) | Gap |
|---------|-----------|-------------|-----|
| App Service | Unknown (legacy) | P1v3 | No autoscaling, no deployment slots configured |
| Redis | Unknown | Premium P1 (6 GB) | No VNet injection, no data persistence, no clustering |
| Azure OpenAI | 10K TPM (gpt-4o-mini) | 200K TPM | Dev is severely throttled; prod needs PTU evaluation |
| AI Search | Standard, 1 replica | Standard2, 2 replicas | Deprecated index consuming storage; Model 2 on Basic (no semantic search) |
| Service Bus | Standard | Standard | Legacy queue names, no duplicate detection |
| All Services | Public endpoints | Public endpoints | No VNet, no private endpoints, no network isolation |

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

### 3.2 Out of Scope

- PCF React 16 remediation (separate project: `pcf-react-16-remediation`)
- Full VNet hub-spoke topology for multi-region deployment
- Azure Front Door / CDN integration
- Database-level Dataverse optimization (index creation requires Microsoft support)
- Application-level code refactoring beyond performance fixes
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

#### C1. Network Isolation (Priority 0 — Security)

Currently, **all Azure services use public endpoints with no network restrictions**. This is the single most critical production gap.

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

### Phase 1: Quick Wins (Highest Impact, Lowest Risk)

| Item | Domain | Effort | Impact |
|------|--------|--------|--------|
| B1. Explicit column selection | Dataverse | Low | Medium — reduces payload 60-80% |
| B3. Thread-safety fixes | Dataverse | Low | Critical — correctness bug |
| A3. GraphServiceClient pooling | BFF API | Low | Low-Medium — reduces object allocation |
| A4. Debug endpoint removal | BFF API | Low | Low — security and cleanliness |
| C8. AI Search cleanup | Infrastructure | Low | Low — cost savings |

### Phase 2: Core Caching (Highest Impact, Moderate Effort)

| Item | Domain | Effort | Impact |
|------|--------|--------|--------|
| A1. Graph metadata cache | BFF API | Medium | **High** — 90%+ latency reduction on cached ops |
| A2. Authorization data cache | BFF API | Medium | **High** — eliminates biggest per-request cost |
| B2. Request batching | Dataverse | Medium | Medium — reduces round-trips |
| B4. Pagination support | Dataverse | Low | Medium — prevents data loss at scale |

### Phase 3: Infrastructure Hardening

| Item | Domain | Effort | Impact |
|------|--------|--------|--------|
| C1. Network isolation (VNet + PE) | Infrastructure | High | **Critical** — security requirement for production |
| C2. Autoscaling | Infrastructure | Medium | High — availability under load |
| C3. Deployment slots | Infrastructure | Medium | High — zero-downtime deploys |
| C4. Redis hardening | Infrastructure | Medium | Medium — durability and security |
| C5-C7. Service hardening | Infrastructure | Medium | Medium — security posture |

### Phase 4: CI/CD Maturity

| Item | Domain | Effort | Impact |
|------|--------|--------|--------|
| D1. Test re-enablement | CI/CD | Medium | High — quality gate |
| D2. IaC deployment | CI/CD | Medium | High — repeatable infrastructure |
| D3. Environment promotion | CI/CD | Medium | High — controlled releases |
| D4. Slot integration | CI/CD | Low | Medium — zero-downtime in CI/CD |

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

---

## 9. Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| Redis (production instance) | Infrastructure | Available via Bicep | Premium P1 already defined |
| Azure VNet | Infrastructure | Not created | Requires Bicep deployment |
| App Service P1v3 plan | Infrastructure | Defined in Bicep | Not yet deployed for production |
| ADR-009 (Redis-first caching) | Architecture | Approved | Provides caching patterns |
| ADR-003 (Authorization) | Architecture | Approved | Defines cacheable vs non-cacheable boundaries |
| ADR-007 (SpeFileStore facade) | Architecture | Approved | All Graph caching goes through facade |

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
| Redis Premium P1 (6 GB) | ~$250/month (already budgeted in Bicep) |
| App Service P1v3 with autoscale (2-10 instances) | $146-$730/month based on load |
| VNet + Private Endpoints | ~$50-100/month for endpoint hours |
| AI Search Standard2 with 2 replicas | ~$500/month (already budgeted) |
| Reduced Azure OpenAI calls (caching) | **Savings** — fewer redundant calls |
| Reduced Dataverse API calls (batching) | **Savings** — fewer round-trips |

### Security

All infrastructure changes in Domain C improve security posture. Network isolation is a **prerequisite for production deployment** — not optional.

---

*End of design specification.*
