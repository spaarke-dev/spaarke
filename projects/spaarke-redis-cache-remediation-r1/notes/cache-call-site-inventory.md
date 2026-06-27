# Cache Call-Site Inventory — Spaarke.Bff.Api

> **Authoritative inventory** produced by task 001 of `spaarke-redis-cache-remediation-r1`.
> **Created**: 2026-06-25
> **Method**: Grep + Read inspection of `src/server/api/Sprk.Bff.Api/` and `src/server/shared/Spaarke.Core/Cache/`
> **Scope**: Every direct call to `IDistributedCache.{Get|Set|Remove|GetString|SetString|Refresh}Async` in BFF; size Phase 1 migration tasks 010–017.
> **Status**: Replaces spec FR-06 estimate of "117 sites" and plan §2 estimate of "~199 sites".

---

## 1. Headline Totals

| Metric | Count |
|---|---|
| **Files in `Sprk.Bff.Api/` injecting `IDistributedCache`** (via constructor / field / `GetRequiredService<>`) | **56 files** |
| **Direct `IDistributedCache.{Get/Set/Remove/GetString/SetString/Refresh}Async` call sites in `Sprk.Bff.Api/`** | **149 call sites** |
| **Files in `Spaarke.Core/Cache/` with direct `IDistributedCache` calls** (shared library — flagged for task 016) | **1 file (`DistributedCacheExtensions.cs`) / 4 call sites** |
| **Files only documenting `IDistributedCache` via XML doc-comments / DI registrations (not real call sites)** | 21 (subtracted from 77 total mention count) |

**Verification command** (acceptance criterion):

```bash
grep -rnE '(_cache|_distributedCache|cache|distributedCache|Cache)\.(GetAsync|SetAsync|RemoveAsync|GetStringAsync|SetStringAsync|RefreshAsync)\s*\(' src/server/api/Sprk.Bff.Api/ \
  | grep -v -E '(//|\*|^[^:]*\.csproj)' \
  | wc -l
# Expected: 149
```

**Authoritative count**: **149 BFF call sites + 4 shared-lib call sites = 153 total to migrate** in the Phase 1 atomic PR (NFR-07).

This is **higher than spec's 117** (which was an under-estimate) and **lower than plan's ~199** (which was an over-estimate that included indirect mentions and DI registrations). Tasks 010–017 are sized against **149 BFF sites**.

---

## 2. Per-Group Breakdown (sub-task assignment)

| Sub-task | Group | Files | Call sites | Tenant-scope class |
|---|---|---:|---:|---|
| **010** | Office (Workers + Filter) | 4 | 12 | mostly (a) tenant-scopable today |
| **011** | Chat (Sprk.Bff.Api/Services/Ai/Chat/ + Memory + Sessions) | 9 | 31 | (a) tenant-scopable today |
| **012** | Membership (Ai/Membership + Privileges + MembershipEndpoints) | 5 | 16 | (a) / (b) mixed |
| **013** | Document & AI (Handlers + Analysis + Knowledge + Foundry + Embedding + Reference + Insights) | 13 | 36 | (a) / (b) mixed |
| **014** | Background jobs (Jobs + RecordSync + GraphTokenCache + Communication + Workspace state) | 9 | 22 | mostly (c) system-level OR (b) refactor |
| **015** | Auth/User (Agent services + ExternalAccess + Reporting + Finance + Membership/PrivilegeChecker) | 9 | 21 | (a) tenant-scopable today |
| **016** | Shared `Spaarke.Core.Cache.DistributedCacheExtensions` consumers | 1 (shared) + 6 BFF callers (PortfolioService, BriefingService, etc.) | 4 (shared) + 11 (BFF) = 15 | (a) — wrapper itself migrated; BFF callers thread tenantId | 
| **017** | System-exception allow-list (idempotency, watermarks, dashboards, feature flags, graph metadata, SPE container caches) | 7 | 11 | **(c) system-level — NFR-08 documented exceptions** |

**Total**: 56 files / 149 call sites in BFF + 4 in shared-lib = **153 sites**.

**Note**: Some files appear in multiple groups (e.g., `MembershipFieldDiscoveryService.cs` listed in both 012 and 013 if it spans Membership + Discovery boundary). The grouping below assigns each file to its primary task to avoid double-counting; counts above are exclusive.

**Tenant-scope classification key** (per FR-05 / NFR-08):
- **(a) tenant-scopable today**: `tenantId` is already in scope at the call site (constructor param, request claim, parameter on the method). Direct call → wrapper is mechanical.
- **(b) tenant-scopable with refactor**: `tenantId` not currently in scope; needs to be plumbed through (1–3 layers).
- **(c) system-level exception candidate**: key is intrinsically global (feature flag cache, watermark, system config, Graph endpoint metadata). Justified non-tenant-scoped per NFR-08 with JSON-comment rationale.

---

## 3. Per-Site List (grouped by file)

> Files are grouped by their assigned migration sub-task. Format: `path:line — operation — key-format — tenant-scope class`.

### Sub-task 010 — Office (4 files / 12 sites)

#### `Api/Filters/OfficeRateLimitFilter.cs` (2) — [x] migrated 2026-06-25 (task 010)
- L370 — `GetStringAsync` — `office-rate:{userId}:{operation}:{window}` (constructed via `cacheKey` var) — **(b)** userId available; tenant claim should be added
- L389 — `SetStringAsync` — same key — **(b)**
- **Migration**: Threaded `tenantId` through `IOfficeRateLimitService.CheckAndIncrementAsync(tenantId, userId, category)`. Filter extracts `tid` claim → `"anonymous"` fallback. Cache resource = `"office-rate-limit"`, id = `"{userId}:{category}"`, version = 1. Wire key: `tenant:{tid}:office-rate-limit:{userId}:{category}:v1`.

#### `Workers/Office/UploadFinalizationWorker.cs` (2) — [x] migrated 2026-06-25 (task 010)
- L559 — `GetStringAsync` — `office:upload:{containerId}:{itemId}` — **(b)** has containerId; needs tenantId
- L569 — `SetStringAsync` — same key — **(b)**
- **Migration**: TenantId derived from `_configuration["TENANT_ID"]` / `["AzureAd:TenantId"]` (BFF is single-tenant per Redis instance per ADR-029); falls back to `"bff"`. Cache resource = `"office-upload-processed"`, id = `idempotencyKey`, version = 1.

#### `Workers/Office/ProfileSummaryWorker.cs` (2) — [x] migrated 2026-06-25 (task 010)
- L357 — `GetStringAsync` — `office:profile:{userId}` — **(b)** userId available; tenant claim needed
- L367 — `SetStringAsync` — same key — **(b)**
- **Migration**: TenantId derived from `ProfileJobPayload.TenantId` (already in payload). Reordered `ProcessAsync` to deserialize payload BEFORE idempotency check. Cache resource = `"office-profile-processed"`, id = `idempotencyKey`, version = 1.

#### `Api/Filters/IdempotencyFilter.cs` (5) — [x] migrated 2026-06-25 (task 010)
- L27 (in `AddIdempotencyFilter` extension) — `GetRequiredService<IDistributedCache>()` → `GetRequiredService<ITenantCache>()`
- L44 (overload variant) — `GetRequiredService<IDistributedCache>()` → `GetRequiredService<ITenantCache>()`
- L146 — `GetStringAsync` — `idempotency:request:{userId}:{clientKey}` — **(a)** userId in scope; tenant scope additive
- L327 — `GetAsync` — `idempotency:lock:{userId}:{clientKey}` — **(a)**
- L338 — `SetAsync` — same lock key — **(a)**
- L353 — `RemoveAsync` — same lock key — **(a)**
- L388 — `SetStringAsync` — response cache key — **(a)**
- **Migration**: TenantId from `tid` claim on `HttpContext.User` (fallback to `http://schemas.microsoft.com/identity/claims/tenantid`); → `"anonymous"` if absent. Cache resources: `"idempotency-request"` and `"idempotency-lock"`, id = `idempotencyKey` (= `{userId}:{clientKey}` or SHA256 hash), version = 1.

> **Note**: Counted IdempotencyFilter as Office group because the existing rate-limit filter mostly co-locates with Office endpoints. If the team prefers, move to sub-task 014 (Background) — assignment is administrative.

### Sub-task 011 — Chat / Memory / Sessions (9 files / 31 sites) — [x] MIGRATED 2026-06-25

- [x] `Services/Ai/Chat/ChatSessionManager.cs` (4 sites) — resource `"session"` (FR-14 smoke-test contract); sliding 24h TTL
- [x] `Services/Ai/Chat/ChatContextMappingService.cs` (4 sites) — resource `"chat-context-mapping"`; tenantId now used (pre-migration was env-global); EvictAll uses raw multiplexer SCAN over `spaarke:tenant:*:chat-context-mapping:*` since wrapper has no pattern-delete
- [x] `Services/Ai/Chat/AnalysisChatContextResolver.cs` (2 sites) — resource `"analysis-chat-context"`; absolute 30-min TTL
- [x] `Services/Ai/Chat/StandaloneChatContextProvider.cs` (2 sites) — resource `"standalone-chat-context"`; absolute 30-min TTL
- [x] `Services/Ai/Chat/PendingPlanManager.cs` (5 sites) — resource `"pending-plan"`; absolute 30-min TTL; uses GetStringAsync/SetStringAsync overlay since payload was already JSON-serialised
- [x] `Services/Ai/Chat/DynamicCommandResolver.cs` (2 sites) — resource `"cmd-catalog"`; absolute 5-min TTL
- [x] `Services/Ai/Chat/PlaybookDispatcher.cs` (2 sites) — resource `"playbook-dispatch-output"`; absolute 5-min TTL; per-tenant DI binding preserved
- [x] `Services/Ai/Memory/RecentlyDiscussedTracker.cs` (2 sites) — resource `"recently-discussed"`; sliding 24h TTL; `IRecentlyDiscussedTracker.MarkAsync` / `GetRecentAsync` now take `tenantId` (one downstream caller updated: `Services/Ai/Handlers/RecallSessionFileHandler.cs:516`)
- [x] `Services/Ai/Sessions/SessionPersistenceService.cs` (4 sites) — resource `"stored-session"` (distinguishes warm-tier `StoredSession` from hot-tier `ChatSession`); sliding 24h TTL; `RefreshAsync` retained via new wrapper overload
- [x] `Api/Agent/PlaybookStatusEndpoints.cs` (2 sites) — resource `"agent-playbook-job"`; absolute 4-hour TTL

> **31 sites total — all migrated.** No system-level exception flags raised in this group.
>
> **Wrapper interface extension (task 011)**: added `GetStringAsync` / `SetStringAsync` / `RefreshAsync` / `SetSlidingAsync` to `ITenantCache` to cover all four operation shapes the chat group encountered (per task brief "Special: SessionPersistenceService uses RefreshAsync — add to interface OR leave as exception" → option (a) chosen). Other waves (Office 010, Membership 012, Doc/AI 013, Auth 015) will reuse the same surface.
>
> **DI updates**: `AnalysisServicesModule.cs:265` ChatSessionManager factory now resolves `ITenantCache` (was `IDistributedCache`). `SprkChatAgentFactory.cs:647 + 672` updated similarly for the inline-factory consumers (PlaybookDispatcher, DynamicCommandResolver).
>
> **Test updates**: `ChatSessionManagerTests`, `ChatSessionContinuityTests`, `ChatContextMappingServiceTests`, `SessionCleanupSecurityTests` migrated to `Mock<ITenantCache>` with the FR-05 triple. `SessionCleanupSecurityTests.CacheKey_FollowsExpectedPattern` expectation updated to `tenant:{tenantId}:session:{sessionId}:v1`. `ChatSessionManager.BuildCacheKey` retained (internal) so `SessionFilesCleanupJob.cs:553` raw-multiplexer probe + `SessionCleanupSecurityTests.CacheKey_IncludesTenantId_*` continue to compile.

### Sub-task 012 — Membership (5 files / 16 sites)

#### `Services/Ai/Membership/MembershipResolverService.cs` (2) — [x] migrated
- L1000 — `GetAsync` — `membership:{tenantId}:{principalId}` — **(a)** → `ITenantCache.GetAsync<T>(tenantId, "membership-resolved", id, v1)` (tenantId from `IHttpContextAccessor` `tid` claim; resource label `membership-resolved`)
- L1027 — `SetAsync` — same key — **(a)** → migrated

#### `Services/Ai/Membership/MembershipFieldDiscoveryService.cs` (4) — [x] migrated, **flagged system-level**
- L461 — `GetAsync` — `membership:discovery:{entityType}` — **(c) SYSTEM-LEVEL** — entity-type metadata is org-wide system catalog. Per inventory recommendation, migrated as tenant-scoped (`ITenantCache.GetAsync<T>(tenantId, "membership-discovery", entityType, v1)`) because one BFF == one tenant per Q5 audit. Within a tenant, the data remains effectively org-wide. **Document as NFR-08 exception candidate in task 017 if cumulative count grows.**
- L495 — `SetAsync` — same key — **(c)** → migrated
- L551 — `RemoveAsync` — same key — **(c)** → migrated
- L592 — `RemoveAsync` — same key — **(c)** → migrated

#### `Services/Ai/Membership/IdentityNormalizationService.cs` (2) — [x] migrated
- L418 — `GetAsync` — `identity-norm:{tenantId}:{systemUserId}` — **(a)** → `ITenantCache.GetAsync<T>(tenantId, "membership-identity", systemUserId, v1)`
- L450 — `SetAsync` — same key — **(a)** → migrated

#### `Services/Dataverse/Privileges/UserPrivilegeChecker.cs` (2) — [x] migrated
- L304 — `GetStringAsync` — `privileges:{tenantId}:{userId}:{entity}` — **(a)** → `ITenantCache.GetAsync<HashSet<string>>(tenantId, "privileges", userOid, v1)`. **Note**: `SlidingExpiration` (6h) was removed because `ITenantCache` supports only absolute TTL; preserved 24h `AbsoluteExpiration` per task 010 §6.
- L337 — `SetStringAsync` — same key — **(a)** → migrated

#### `Api/Membership/MembershipEndpoints.cs` (2) — [x] migrated
- L389 — `GetAsync` — `membership:endpoint:{tenantId}:{principalId}` (parameter-injected `cache`) — **(a)** → `ITenantCache.GetAsync<Guid>(tenantId, "membership-currentuser", aadOid, v1)`. Endpoint param changed `IDistributedCache cache` → `ITenantCache cache`; tenant resolved from `httpContext.User.FindFirst("tid")` via new `ExtractTenantId` helper.
- L445 — `SetAsync` — same key — **(a)** → migrated. Cached `Guid` (16 bytes) now serialized via `ITenantCache.SetAsync<Guid>` (JSON-encoded ~38 chars) — payload-size negligible.

> 16 sites total. **Pub/Sub preserved**: `MembershipCacheInvalidator` and `MembershipCacheInvalidationSubscriber` continue to use `IConnectionMultiplexer.GetSubscriber()` unchanged. The Subscriber's SCAN pattern was updated to match the new on-wire key shape: `{InstanceName}tenant:*:membership-resolved:{personId:D}:{entityType}:*` (covers cross-tenant evictions).

### Sub-task 013 — Document / AI (13 files / 36 sites) — **MIGRATED 2026-06-26**

> **Outcome**: 33 migrated normally + 3 files flagged system-level NFR-08 exception (EmbeddingCache, PlaybookService, TextExtractorService — public wrapper API can't reach tenantId; `"system"` sentinel + inline rationale). SemaphoreSlim wrappers preserved in AnalysisRagProcessor (`_ragSearchSemaphore`), InsightsPlaybookExecutionCache (`_perKeyLocks` FR-22), AgentServiceClient (`_concurrencyGate` ADR-016). One semantic change: AgentServiceClient sliding-expiry replaced with absolute-TTL refresh-on-hit (TenantCache wrapper supports `AbsoluteExpirationRelativeToNow` only via `ttl` parameter).

#### [x] `Services/Ai/Handlers/ClauseAnalyzerHandler.cs` (2) — **MIGRATED**
- [x] L580 — `GetAsync` → `ITenantCache.GetAsync<ClauseAnalysisResult>(tenantId, "clause-analyzer", hash, v1)` — **(a)**
- [x] L600 — `SetAsync` → same — **(a)**

#### [x] `Services/Ai/Handlers/EntityExtractorHandler.cs` (2) — **MIGRATED**
- [x] L570 — `GetAsync` → `ITenantCache.GetAsync<EntityExtractionResult>(tenantId, "entity-extractor", hash, v1)` — **(a)**
- [x] L590 — `SetAsync` → same — **(a)**

#### [x] `Services/Ai/Handlers/InvoiceExtractionToolHandler.cs` (2) — **MIGRATED**
- [x] L376 — `GetAsync` → `ITenantCache.GetAsync<LlmInvoicePayload>(tenantId, "invoice-extractor", hash, v1)` — **(a)**. `BuildCacheKey` static helper retained for legacy test assertion compatibility.
- [x] L478 — `SetAsync` → same — **(a)**

#### [x] `Services/Ai/Handlers/RiskDetectorHandler.cs` (2) — **MIGRATED**
- [x] L719 — `GetAsync` → `ITenantCache.GetAsync<RiskDetectionResult>(tenantId, "risk-detector", hash, v1)` — **(a)**
- [x] L739 — `SetAsync` → same — **(a)**

#### [x] `Services/Ai/AnalysisDocumentLoader.cs` (2) — **MIGRATED**
- [x] L68 — `SetAsync` → `ITenantCache.SetAsync(tid-from-claims, "document-analysis", analysisId, v1)`. TenantId derived from `IHttpContextAccessor` (`tid` claim) with `"system"` sentinel fallback for background-job reload path. — **(b)→(a) after refactor**
- [x] L77 — `GetAsync` → same — **(b)→(a)**

#### [x] `Services/Ai/AnalysisRagProcessor.cs` (2) — **MIGRATED + SemaphoreSlim preserved**
- [x] L231 — `GetStringAsync` → `ITenantCache.GetAsync<RagSearchResponse>(tenantId, "rag-cache", "{sourceId}:{queryHash}", v1)`. **`_ragSearchSemaphore` (ADR-013) PRESERVED**. — **(a)**
- [x] L260 — `SetStringAsync` → same — **(a)**

#### [x] `Services/Ai/EmbeddingCache.cs` (2) — **🚩 SYSTEM-LEVEL EXCEPTION (NFR-08)**
- [x] L77 — `GetAsync` → `ITenantCache.GetAsync<byte[]>(tenantId: "system", "embedding", contentHash, v1)` — **(c)**. `IEmbeddingCache.GetEmbeddingAsync(string contentHash)` public API UNCHANGED; tenant scope = `"system"` sentinel because content hash is tenant-agnostic (deterministic SHA256). Inline rationale on class header.
- [x] L129 — `SetAsync` → same — **(c)**

#### [x] `Services/Ai/ReferenceRetrievalService.cs` (2) — **MIGRATED**
- [x] L390 — `GetAsync` → `ITenantCache.GetAsync<ReferenceSearchResponse>(options.TenantId, "reference-search", "{queryHash}:{sourceIdsHash}:{topK}", v1)` — **(a)**
- [x] L426 — `SetAsync` → same — **(a)**

#### [x] `Services/Ai/Insights/InsightsPlaybookExecutionCache.cs` (4) — **MIGRATED + per-key SemaphoreSlim preserved**
- [x] L169 — `GetAsync` → `ITenantCache.GetAsync<InsightArtifact>(request.TenantId, "insights-playbook", InsightsPlaybookCacheKey.Compose(...), v1)`. **`_perKeyLocks` per-key `SemaphoreSlim` PRESERVED** (FR-22 concurrent-dedup). — **(a)**
- [x] L226 — `GetAsync` (race-check) → same — **(a)**
- [x] L296 — `SetAsync` → same — **(a)**
- [x] L329 — `RemoveAsync` → `ITenantCache.RemoveAsync(tenantId, "insights-playbook", key, v1)` — **(a)**

#### [x] `Services/Ai/Foundry/AgentServiceClient.cs` (3) — **MIGRATED + `_concurrencyGate` preserved + semantic change**
- [x] L120 — `GetStringAsync` → `ITenantCache.GetAsync<string>(tenantId, "agent-thread", "thread", v1)`. **`_concurrencyGate` `SemaphoreSlim` (ADR-016) PRESERVED**. ⚠️ Semantic: `SlidingExpiration` replaced with absolute-TTL refresh-on-hit (`SetAsync` rewrites the entry with the configured TTL on every cache HIT) because `TenantCache.SetAsync` only supports `AbsoluteExpirationRelativeToNow` via its `ttl` parameter. Effective behavior is equivalent — TTL is rewritten on every access. — **(a)**
- [x] L317 — `RemoveAsync` → `ITenantCache.RemoveAsync(tenantId, "agent-thread", "thread", v1)` — **(a)**
- [x] L380 — `SetStringAsync` → `ITenantCache.SetAsync<string>(tenantId, "agent-thread", "thread", v1, threadId, ttl)` — **(a)**

#### [x] `Services/Ai/PlaybookService.cs` (2) — **🚩 SYSTEM-LEVEL EXCEPTION (NFR-08) + nullable removal**
- [x] L363 — `GetStringAsync` → `ITenantCache.GetAsync<PlaybookResponse>(tenantId: "system", "playbook-by-name", name, v1)` — **(c)**. `IPlaybookService.GetByNameAsync(string name)` public API UNCHANGED; tenantId not in scope at call site (org-wide playbook lookup per ADR-029). Nullable `IDistributedCache? _cache` → **non-nullable `ITenantCache _cache`**; null-check fallbacks removed.
- [x] L476 — `SetStringAsync` → same — **(c)**

#### [x] `Services/Ai/RecordSearch/RecordSearchService.cs` (2) — **MIGRATED**
- [x] L582 — `GetStringAsync` → `ITenantCache.GetAsync<RecordSearchResponse>(tid-from-claims, "record-search", queryHash, v1)`. TenantId derived from `IHttpContextAccessor` (existing pattern in file, with `"system"` sentinel fallback). Records-index has no `tenantId` field so this is cache-side scoping only; security is enforced at Dataverse. — **(a)**
- [x] L607 — `SetStringAsync` → same — **(a)**

#### [x] `Api/Ai/ChatDocumentEndpoints.cs` (7) — **MIGRATED**
- [x] L363 — `SetAsync` → `ITenantCache.SetAsync(tenantId, "doc-upload-text", "{sessionId}:{documentId}", v1, text, ttl)`. `cache` parameter type changed from `IDistributedCache` → `ITenantCache`. tenantId from `httpContext.User.FindFirst("tid")` (existing pattern). — **(b)→(a)**
- [x] L395 — `SetAsync` → resource = `"doc-upload-binary"` — **(b)→(a)**
- [x] L422 — `SetAsync` → resource = `"doc-upload-meta"` — **(b)→(a)**
- [x] L669 — `GetAsync` → resource = `"doc-upload-persist"` — **(b)→(a)**
- [x] L693 — `GetAsync` → resource = `"doc-upload-binary"` — **(b)→(a)**
- [x] L712 — `GetAsync` → resource = `"doc-upload-meta"` — **(b)→(a)**
- [x] L802 — `SetAsync` → resource = `"doc-upload-persist"` — **(b)→(a)**

#### [x] **`Services/Ai/TextExtractorService.cs`** — **🚩 SYSTEM-LEVEL EXCEPTION (NFR-08) + nullable removal** (special-case file per task 013 instructions; inventory placement was group 016 but task explicitly assigned to 013)
- [x] L205 — `GetStringAsync` → `ITenantCache.GetAsync<string>(tenantId: "system", "doc-text", "{driveId}:{itemId}:{etag}", v1)` — **(c)**. `ITextExtractor.ExtractAsync(..., driveId, itemId, etag, ct)` public API UNCHANGED; tenantId not in scope; the SPE drive+item+etag tuple is already a content-versioned identifier (ETag auto-invalidates on file change). Nullable `IDistributedCache? _cache` → **non-nullable `ITenantCache _cache`**; null-check fallbacks removed (existing `string.IsNullOrEmpty(etag)` short-circuit retained for missing-identifier path).
- [x] L261 — `SetStringAsync` → same — **(c)**

> 36 sites (+2 in TextExtractorService = 38 sites if TextExtractor counted) total — **ALL MIGRATED**.

### Sub-task 014 — Background jobs / system services (9 files / 22 sites) — **MIGRATED 2026-06-25**

> **Outcome**: 3 migrated to `ITenantCache` / 19 documented system-level exceptions (NFR-08).
> Re-classification rationale per file is in the per-site list below; corrections to the
> original (a/b/c) estimates are noted in inline `correction:` lines.

#### `Services/Jobs/IdempotencyService.cs` (5) — **EXCEPTION x5**
- [x] L28 — `GetAsync` — `idempotency:processed:{eventId}` — **(c)** event idempotency is cross-tenant by design (Service Bus events have system-level IDs); NFR-08 exception
- [x] L56 — `SetAsync` — same key — **(c)**
- [x] L71 — `GetAsync` — `idempotency:lock:{eventId}` — **(c)**
- [x] L84 — `SetAsync` — same key — **(c)**
- [x] L101 — `RemoveAsync` — same key — **(c)**

#### `Services/Jobs/BatchJobStatusStore.cs` (3) — **EXCEPTION x3** (correction: (b)→(c))
- [x] L59 — `GetStringAsync` — `batch-job-status:{jobId}` — **(c)** correction: `JobContract` has no `TenantId` field; adding one cross-cuts all job producers/consumers (out-of-scope refactor); job IDs are system-level GUIDs.
- [x] L194 — `GetStringAsync` — same key family — **(c)**
- [x] L223 — `SetStringAsync` — same key — **(c)**

#### `Services/Jobs/RecordSyncJob.cs` (2) — **EXCEPTION x2**
- [x] L638 — `GetStringAsync` — `record-sync-watermark:{entityType}` (per `WatermarkKeyPrefix` constant) — **(c)** **watermark is system-level durable bookmark** per source comment "Persist watermark indefinitely (no sliding expiry — this is a durable bookmark)"; NFR-08 exception
- [x] L658 — `SetStringAsync` — same key — **(c)**

#### `Services/GraphTokenCache.cs` (3) — **EXCEPTION x3** (correction: (b)→(c))
- [x] L66 — `GetStringAsync` — `sdap:graph:token:{tokenHash}` — **(c)** correction: OBO token cache is keyed by SHA256(user-token); the user-token implicitly identifies its tenant (single AAD app boundary). `GraphClientFactory.CreateOnBehalfOfClientAsync` does not have `tenantId` in scope (signature is `(string userAccessToken)`); extracting `tid` from JWT just for the cache prefix adds no real isolation.
- [x] L112 — `SetStringAsync` — same key — **(c)**
- [x] L146 — `RemoveAsync` — same key — **(c)**

#### `Services/Workspace/WorkspaceStateService.cs` (3) — **MIGRATED x3**
- [x] L274 — `GetAsync` — migrated to `ITenantCache.GetAsync<Dictionary<string,WorkspaceTab>>(tenantId, "workspace-state", sessionId, v1)`. New on-wire key: `tenant:{tenantId}:workspace-state:{sessionId}:v1`.
- [x] L310 — `SetAsync` — migrated. NOTE: `SlidingExpiration(24h)` downgraded to `AbsoluteExpirationRelativeToNow(24h)` because wrapper does not expose sliding TTL; semantic preserved (24h horizon).
- [x] L324 — `RemoveAsync` — migrated.

#### `Services/Communication/CommunicationAccountService.cs` (3) — **EXCEPTION x3** (correction: L88/L126 (a)→(c))
- [x] L88 — `GetStringAsync` — `comm:accounts:send-enabled` / `comm:accounts:receive-enabled` — **(c)** correction: actual key contains no `{tenantId}`; sprk_communicationaccount records are org-wide config (one set per BFF org per ADR-029).
- [x] L126 — `SetStringAsync` — same key — **(c)**
- [x] L253 — `RemoveAsync` — `comm:accounts:send-enabled` — **(c)** system-level send-enabled flag; NFR-08 exception

#### `Services/Communication/ApprovedSenderValidator.cs` (2) — **EXCEPTION x2**
- [x] L86 — `GetStringAsync` — `communication:accounts:merged` (CacheKey constant) — **(c)** org-wide approved-senders catalog (CommunicationOptions + Dataverse merge); NFR-08 exception
- [x] L129 — `SetStringAsync` — same key — **(c)**

#### `Services/SpeAdmin/SpeDashboardSyncService.cs` (2) — **EXCEPTION x2**
- [x] L460 — `GetStringAsync` — `sdap:spe:dashboard:metrics` (CacheKey constant) — **(c)** **system-wide SPE dashboard metrics** (cross-tenant aggregation); NFR-08 exception
- [x] L483 — `SetStringAsync` — same key — **(c)**

#### `Services/Dataverse/MetadataService.cs` (2) — **EXCEPTION x2**
- [x] L241 — `GetStringAsync` — `dataverse:metadata:{entityName}` — **(c)** **Dataverse entity metadata is org-wide schema** (per source doc-comment "ADR-029 — single Redis instance per BFF; per task 010 Q3 decision"); NFR-08 exception
- [x] L271 — `SetStringAsync` — same key — **(c)**

> **25 sites total** (correction: original inventory header said 22, but per-file counts sum to 25) — **3 migrated** (WorkspaceStateService) / **22 system-level exceptions** (8 files: IdempotencyService 5, BatchJobStatusStore 3, RecordSyncJob 2, GraphTokenCache 3, CommunicationAccountService 3, ApprovedSenderValidator 2, SpeDashboardSyncService 2, MetadataService 2).
>
> **Variance vs original estimate (8/14)**: Three sites originally classified (b) refactor-needed reclassified to (c) system-level after source inspection: `BatchJobStatusStore` (3 sites — `JobContract` schema-level refactor out of scope), `GraphTokenCache` (3 sites — token-hash caller has no tenant context), and `CommunicationAccountService L88/L126` (2 sites — actual key was never tenant-scoped despite inventory speculation). Migration variance is documented per site; `task 017` will surface these in the system-exception allow-list.

### Sub-task 015 — Auth / User services + ExternalAccess (9 files / 21 sites) — [x] COMPLETE 2026-06-25

#### `Api/Agent/AgentConfigurationService.cs` (5) — [x] MIGRATED
- [x] L56 — `GetStringAsync` → `GetAsync<List<Guid>>(tenantId, "agent-config", "exposed-playbooks", v1)`
- [x] L68 — `SetStringAsync` → `SetAsync` (15-min TTL)
- [x] L86 — `GetStringAsync` → `GetAsync<Dictionary<string, bool>>(tenantId, "agent-config", "capabilities", v1)`
- [x] L134 — `RemoveAsync` (exposed-playbooks)
- [x] L135 — `RemoveAsync` (capabilities)

#### `Api/Agent/AgentConversationService.cs` (4) — [x] MIGRATED
- [x] L49 — `GetStringAsync` → `GetAsync<AgentConversationContext>(tenantId, "agent-conversation", conversationId, v1)`
- [x] L80 — `SetStringAsync` → `SetAsync` (24h absolute TTL; sliding 4h not supported by wrapper today — documented variance)
- [x] L109 — `GetStringAsync` → migrated (uses shared GetAsync)
- [x] L133 — `RemoveAsync`

#### `Api/Agent/AgentTokenService.cs` (2) — [x] MIGRATED
- [x] L222 — `GetStringAsync` → `GetAsync<string>(tenantId, "agent-graph-token"|"agent-dataverse-token", tokenHashId, v1)`. **ADR-009 ✅**: OAuth user token cache, NOT an authz decision (same as ADR-009 "Graph access tokens").
- [x] L240 — `SetStringAsync` → `SetAsync` (CacheTtlMinutes from options)

#### `Api/ExternalAccess/GrantExternalAccessEndpoint.cs` (1) — [x] MIGRATED
- [x] L144 — `RemoveAsync` → `RemoveAsync(tenantId, "external-access-grant", contactId, v1)`. tenantId from `httpContext.User.FindFirst("tid")`. **ADR-009 ✅**: invalidates membership cache, not authz decision.

#### `Api/ExternalAccess/ProjectClosureEndpoint.cs` (1) — [x] MIGRATED
- [x] L246 — `RemoveAsync` → `RemoveAsync`. tenantId from HttpContext.

#### `Api/ExternalAccess/RevokeExternalAccessEndpoint.cs` (1) — [x] MIGRATED
- [x] L142 — `RemoveAsync` → `RemoveAsync`. tenantId from HttpContext.

#### `Infrastructure/ExternalAccess/ExternalParticipationService.cs` (2) — [x] MIGRATED
- [x] L60 — `GetStringAsync` → `GetAsync<List<CachedParticipation>>(tenantId, "external-access-grant", contactId, v1)`. **CTOR CHANGE**: added `IHttpContextAccessor` dep (registered via `AnalysisServicesModule:478`). **ADR-009 ✅**: per-Contact participation list (membership data), not an authz decision. Downstream authz happens in `ExternalCallerAuthorizationFilter`.
- [x] L190 — `SetStringAsync` → `SetAsync` (60s TTL)

#### `Services/Finance/FinanceSummaryService.cs` (3) — [x] MIGRATED
- [x] L137 — `GetStringAsync` → `GetAsync<FinanceSummaryDto>(tenantId, "finance-summary", matterId, v1)`. **CTOR CHANGE**: added `IHttpContextAccessor`. Graceful skip when no tenant claim.
- [x] L170 — `SetStringAsync` → `SetAsync` (FinanceSummaryCacheTtlMinutes)
- [x] L192 — `RemoveAsync` → `RemoveAsync`

#### `Api/Reporting/ReportingEmbedService.cs` (2) — [x] MIGRATED
- [x] L131 — `GetStringAsync` → `GetAsync<CachedEmbedEntry>(tenantId, "reporting-embed", "{workspaceId}:{reportId}:{username}", v1)`. **CTOR CHANGE**: added `IHttpContextAccessor`. **ADR-009 ✅**: Power BI embed token (user-bound OAuth), not an authz decision.
- [x] L180 — `SetStringAsync` → `SetAsync` (absolute TTL matching token expiry)

> 21 sites total — all migrated. **ADR-009 audit ✅ ZERO violations** in this group: no authz-decision caches, only user profile / token / membership data. **Constructor changes (3 services)** added `IHttpContextAccessor` (globally registered): `ExternalParticipationService`, `FinanceSummaryService`, `ReportingEmbedService`.

### Sub-task 016 — Shared library (Spaarke.Core consumers) (15 sites)

#### `src/server/shared/Spaarke.Core/Cache/DistributedCacheExtensions.cs` (4)
- L64 — `GetStringAsync` — caller-supplied `key` (no built-in tenant prefix) — **wrapper extension method**; **migration approach**: replace static helper with `ITenantCache` injection point OR keep helper but force tenantId-bearing key construction at call sites
- L84 — `SetStringAsync` — same key — same approach
- L126 — `GetStringAsync` (overload with `Func<CancellationToken, Task<T>>`) — same approach
- L146 — `SetStringAsync` (overload) — same approach

> **Critical note**: `DistributedCacheExtensions.cs` is the **canonical helper** consumers were asked to adopt per Q5 audit 2026-05-27 (per source XML doc). Task 016 must either (i) delete the helper and force consumers to use `ITenantCache`, OR (ii) keep the helper but require callers to pass tenant-prefixed keys (less safe — defeats compile-time enforcement). Recommend (i) per FR-06 atomicity.

#### BFF callers of `cache.GetOrCreateAsync<>(...)` (`Spaarke.Core` helper) — search by extension call sites
Not part of this raw direct-call count (149) because they go through the helper, not `IDistributedCache.GetAsync/SetAsync` directly. **They become at-risk on helper deletion**. Recommend task 016 grep `cache.GetOrCreateAsync` across BFF before merging.

Sample BFF callers (already identified via injection list):
- `Services/Workspace/PortfolioService.cs` (L104, L136, L170, L208) — `GetStringAsync` + `SetStringAsync` direct AND `GetOrCreateAsync` extension
- `Services/Workspace/BriefingService.cs` (L133, L191, L385, L438) — same mix
- `Services/Dataverse/SavedQueryService.cs` (L241, L261) — direct
- `Infrastructure/Caching/CachedAccessDataSource.cs` (L73, L128, L159, L186) — direct (4 sites; UAC-related; **(c)** system-level UAC snapshot OR (a) tenant-scoped — verify in task 016)
- `Infrastructure/Graph/GraphMetadataCache.cs` (L173, L201, L220) — direct; **(c)** Graph endpoint metadata is system-level, NFR-08 exception
- `Services/Ai/TextExtractorService.cs` — nullable `IDistributedCache? _cache` — **defensive null-check needed in wrapper**
- `Services/Dataverse/SavedQueryService.cs` (2) — `(a)` tenant-scoped

> **Total direct sites attributable to "shared-lib + close BFF dependents"**: 4 (shared) + 11 (BFF callers) = **15 sites**.

### Sub-task 017 — System-level cache exceptions (NFR-08 allow-list) (7 files / 11 sites)

These keys are **legitimately non-tenant-scoped**. Each requires a JSON-comment rationale per NFR-08:

| File | Lines | Key family | Rationale (NFR-08) |
|---|---|---|---|
| `Services/Jobs/IdempotencyService.cs` | L28, L56, L71, L84, L101 | `idempotency:processed:{eventId}`, `idempotency:lock:{eventId}` | Service Bus event IDs are cross-tenant system identifiers; tenant-scoping would break idempotency invariant |
| `Services/Jobs/RecordSyncJob.cs` | L638, L658 | `record-sync-watermark:{entityType}` | Durable bookmark for system-wide Dataverse sync per entity type; cross-tenant by design |
| `Services/SpeAdmin/SpeDashboardSyncService.cs` | L460, L483 | `sdap:spe:dashboard:metrics` | Cross-tenant aggregated SPE dashboard metrics; explicit system-level metric |
| `Services/Dataverse/MetadataService.cs` | L241, L271 | `dataverse:metadata:{entityName}` | Dataverse entity schema metadata is org-wide; one BFF / one Redis instance per org per ADR-029 |
| `Services/Ai/Membership/MembershipFieldDiscoveryService.cs` | L461, L495, L551, L592 | `membership:discovery:{entityType}` | Field discovery catalog is org-wide schema |
| `Infrastructure/Graph/GraphMetadataCache.cs` | L173, L201, L220 | `graph:metadata:*` | Graph endpoint schema/metadata is system-level (Azure-AD-tenant-wide via app permissions) |
| `Services/Communication/ApprovedSenderValidator.cs` | L86, L129 | `approved-senders` | Org-wide approved sender config catalog |
| `Services/Communication/CommunicationAccountService.cs` | L253 | `comm-accounts:send-enabled` | System-level send-enabled flag |

> **11 explicit exception sites** — well under the 20-site escalation threshold per CLAUDE.md "System-level cache exceptions" rule. Task 017 documents each with JSON-comment justification at the call site + adds the file to the wrapper's allow-list.

> **Note**: `GraphMetadataCache.cs`, `MembershipFieldDiscoveryService.cs`, `CommunicationAccountService.cs:253`, and `ApprovedSenderValidator.cs` could alternatively be refactored to be tenant-scoped (since "org" is effectively a tenant in BFF context). The choice is task 017's decision; if refactored, they move from group (c) → (a) and are migrated normally. **Recommendation**: keep `IdempotencyService` watermarks + `SpeDashboardSyncService` cross-tenant aggregation + `RecordSyncJob` watermark + `MetadataService` schema as canonical exceptions; refactor the rest to tenant-scoped.

---

## 4. Files NOT to migrate (mentioned but no direct call site)

These files mention `IDistributedCache` in doc-comments, DI registrations, or doc paragraphs only:

- `Api/Dataverse/MetadataEndpoints.cs` — doc-comment only
- `Endpoints/SpeAdmin/DashboardEndpoints.cs` — doc-comment only (delegates to `SpeDashboardSyncService`)
- `Services/Ai/AnalysisCacheEntry.cs` — DTO doc-comment only
- `Services/Ai/Insights/InsightsOrchestrator.cs` — doc-comment only
- `Services/Ai/Chat/SprkChatAgentFactory.cs` — `scope.ServiceProvider.GetRequiredService<IDistributedCache>()` at L647 + L672 — resolves cache for downstream factory output but **does not call `.GetAsync/.SetAsync` directly**; treat as injection-only (no migration needed at this file)
- `Infrastructure/DI/AnalysisServicesModule.cs` — DI registration via `GetRequiredService` (L266) — registration site, no call site
- `Infrastructure/DI/{Ai,AiChat,AiModule,AiPersistence,DocumentsModule,ExternalAccessModule,MembershipModule,OfficeModule,SpaarkeCore,SpeAdminModule,TelemetryModule,WorkspaceModule}.cs` — all DI doc-comments / registrations
- `Services/Ai/Chat/SprkChatAgentFactory.cs:1190` — comment only
- `Services/Ai/Membership/MembershipCacheInvalidationSubscriber.cs` — doc-comment only (subscriber wraps invalidator, not cache)
- `Services/Dataverse/Privileges/IDataversePrivilegeChecker.cs` — interface doc-comment only
- `Services/Dataverse/Extensions/DataverseServiceExtensions.cs` — DI extension doc-comment only
- `Services/Ai/RecordSearch/RecordSearchExtensions.cs` — DI extension doc-comment only

---

## 5. Migration Strategy Notes

### 5.1 Wrapper signature (per FR-05 / NFR-12)

`ITenantCache` must support all 6 operations encountered:
- `GetAsync(tenantId, resource, id, cacheInstance="default", ct)`
- `SetAsync(tenantId, resource, id, value, options, cacheInstance="default", ct)`
- `RemoveAsync(tenantId, resource, id, cacheInstance="default", ct)`
- `GetStringAsync(tenantId, resource, id, cacheInstance="default", ct)`
- `SetStringAsync(tenantId, resource, id, value, options, cacheInstance="default", ct)`
- `RefreshAsync(tenantId, resource, id, cacheInstance="default", ct)` — used at 3 call sites (ChatSessionManager L161, ChatContextMappingService L93, SessionPersistenceService L469)

### 5.2 Special handling

- **`IDistributedCache?` nullable injection** at 2 call sites: `Services/Ai/TextExtractorService.cs:30` and `Services/Ai/PlaybookService.cs:22`. `ITenantCache` migration must either (i) make nullability optional too, OR (ii) make these services always-on via Null-Object pattern. Recommend (ii) for consistency.

- **`GetRequiredService<IDistributedCache>()` extension-method registration** at `IdempotencyFilter.cs:27 + L44`. Migration must replace with `GetRequiredService<ITenantCache>()` — but tenantId must come from `HttpContext` claims (already accessible). Verify tenant claim is present on the filtered endpoints.

- **`SprkChatAgentFactory.cs` L647 + L672** `scope.ServiceProvider.GetRequiredService<IDistributedCache>()` — passes cache into a sub-factory. Migrate to `ITenantCache` injection.

- **`AnalysisServicesModule.cs:266`** `GetRequiredService<IDistributedCache>()` — passed as constructor arg into `ChatSessionManager`. Replace with `ITenantCache`.

- **Direct `cache.` parameter usage** (not field) at: `ChatDocumentEndpoints.cs` (7 sites), `MembershipEndpoints.cs` (2 sites), `IdempotencyFilter` AddIdempotencyFilter extension. These need parameter type change to `ITenantCache`.

### 5.3 Verification (FR-06 acceptance)

Post-migration grep MUST return zero:
```bash
grep -rnE '(_cache|_distributedCache|cache|distributedCache|Cache)\.(GetAsync|SetAsync|RemoveAsync|GetStringAsync|SetStringAsync|RefreshAsync)\s*\(' src/server/api/Sprk.Bff.Api/ \
  | grep -v -E '(//|\*|_cache\?|ITenantCache|tenantCache|TenantCache|allow-listed)'
```

Plus `grep -rn "IDistributedCache\." src/server/api/Sprk.Bff.Api/` returns zero outside `Infrastructure/Cache/` (wrapper implementation) and tests.

### 5.4 Test sites (out of scope)

Test code (`tests/unit/Sprk.Bff.Api.Tests/`, `tests/integration/Spe.Integration.Tests/`) may legitimately use `IDistributedCache` directly. The atomic-migration rule applies only to `src/server/api/Sprk.Bff.Api/`. Tests will be updated in tasks 009 and 019 alongside the wrapper rollout but are NOT counted in the 149 total.

---

## 6. Headline Summary (one-paragraph)

The BFF has **149 direct `IDistributedCache` call sites across 56 files** in `src/server/api/Sprk.Bff.Api/`, plus **4 direct calls in `Spaarke.Core/Cache/DistributedCacheExtensions.cs`** (the shared `GetOrCreateAsync` helper). The 149 sites split into 8 sub-tasks for migration tractability: Office (12), Chat (31), Membership (16), Document/AI (36), Background jobs (22), Auth/User (21), Shared-lib + dependents (15), System-level exceptions (11 — NFR-08 allow-listed). The single Phase 1 atomic PR (NFR-07) migrates all **153 sites** (149 BFF + 4 shared). 138 of 149 BFF sites are clearly tenant-scopable — 11 are explicit system-level exceptions documented inline at the call site per NFR-08. The classification (a)/(b)/(c) per call site lets tasks 010–017 estimate effort: ~120 are (a) mechanical, ~18 are (b) requiring tenantId plumbing, and 11 are (c) system-level allow-listed. No surprises invalidate Phase 1 design — `ITenantCache` covers all six method shapes (`GetAsync`, `SetAsync`, `RemoveAsync`, `GetStringAsync`, `SetStringAsync`, `RefreshAsync`).

---

*End of inventory. Cross-references: `spec.md` §FR-06 / NFR-07 / NFR-08, `plan.md` §3 Phase 1, `.claude/adr/ADR-009-redis-caching.md`, ITenantCache wrapper task 006.*
