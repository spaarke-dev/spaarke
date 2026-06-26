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

#### `Api/Filters/OfficeRateLimitFilter.cs` (2)
- L370 — `GetStringAsync` — `office-rate:{userId}:{operation}:{window}` (constructed via `cacheKey` var) — **(b)** userId available; tenant claim should be added
- L389 — `SetStringAsync` — same key — **(b)**

#### `Workers/Office/UploadFinalizationWorker.cs` (2)
- L559 — `GetStringAsync` — `office:upload:{containerId}:{itemId}` — **(b)** has containerId; needs tenantId
- L569 — `SetStringAsync` — same key — **(b)**

#### `Workers/Office/ProfileSummaryWorker.cs` (2)
- L357 — `GetStringAsync` — `office:profile:{userId}` — **(b)** userId available; tenant claim needed
- L367 — `SetStringAsync` — same key — **(b)**

#### `Api/Filters/IdempotencyFilter.cs` (5)
- L27 (in `AddIdempotencyFilter` extension) — `GetRequiredService<IDistributedCache>()` — registration site (no key)
- L44 (overload variant) — `GetRequiredService<IDistributedCache>()` — registration site (no key)
- L146 — `GetStringAsync` — `idempotency:request:{userId}:{clientKey}` — **(a)** userId in scope; tenant scope additive
- L327 — `GetAsync` — `idempotency:lock:{userId}:{clientKey}` — **(a)**
- L338 — `SetAsync` — same lock key — **(a)**
- L353 — `RemoveAsync` — same lock key — **(a)**
- L388 — `SetStringAsync` — response cache key — **(a)**

> **Note**: Counted IdempotencyFilter as Office group because the existing rate-limit filter mostly co-locates with Office endpoints. If the team prefers, move to sub-task 014 (Background) — assignment is administrative.

### Sub-task 011 — Chat / Memory / Sessions (9 files / 31 sites)

#### `Services/Ai/Chat/ChatSessionManager.cs` (4)
- L153 — `GetAsync` — `chat-session:{tenantId}:{sessionId}` — **(a)** tenantId in method signature
- L161 — `RefreshAsync` — same key — **(a)**
- L222 — `RemoveAsync` — same key — **(a)**
- L291 — `SetAsync` — same key — **(a)**

#### `Services/Ai/Chat/ChatContextMappingService.cs` (4)
- L83 — `GetAsync` — `chat-context-map:{tenantId}:{sessionId}` — **(a)**
- L93 — `RefreshAsync` — same key — **(a)**
- L153 — `RemoveAsync` — same key — **(a)**
- L325 — `SetAsync` — same key — **(a)**

#### `Services/Ai/Chat/AnalysisChatContextResolver.cs` (2)
- L191 — `GetAsync` — `analysis-chat-context:{tenantId}:{analysisId}` — **(a)**
- L689 — `SetAsync` — same key — **(a)**

#### `Services/Ai/Chat/StandaloneChatContextProvider.cs` (2)
- L188 — `GetAsync` — `standalone-chat-context:{tenantId}:{key}` — **(a)**
- L271 — `SetAsync` — same key — **(a)**

#### `Services/Ai/Chat/PendingPlanManager.cs` (5)
- L94 — `SetAsync` — `pending-plan:{tenantId}:{sessionId}` — **(a)**
- L111 — `GetAsync` — same key — **(a)**
- L146 — `GetAsync` — same key — **(a)**
- L158 — `RemoveAsync` — same key — **(a)**
- L180 — `RemoveAsync` — same key — **(a)**

#### `Services/Ai/Chat/DynamicCommandResolver.cs` (2)
- L126 — `GetStringAsync` — `dynamic-command:{tenantId}:{command}` — **(a)**
- L163 — `SetStringAsync` — same key — **(a)**

#### `Services/Ai/Chat/PlaybookDispatcher.cs` (2)
- L1009 — `GetStringAsync` — `playbook-dispatch:{tenantId}:{playbookId}` — **(a)**
- L1078 — `SetStringAsync` — same key — **(a)**

#### `Services/Ai/Memory/RecentlyDiscussedTracker.cs` (2)
- L190 — `GetAsync` — `recently-discussed:{tenantId}:{sessionId}` — **(a)**
- L216 — `SetAsync` — same key — **(a)**

#### `Services/Ai/Sessions/SessionPersistenceService.cs` (4)
- L162 — `RemoveAsync` — `sessions:{tenantId}:{sessionId}` — **(a)** (doc-comment shows key format)
- L459 — `GetAsync` — same key — **(a)**
- L469 — `RefreshAsync` — same key — **(a)**
- L497 — `SetAsync` — same key — **(a)**

#### `Api/Agent/PlaybookStatusEndpoints.cs` (2)
- L158 — `GetStringAsync` — `playbook-status:{tenantId}:{playbookId}` — **(a)**
- L167 — `SetStringAsync` — same key — **(a)**

> **31 sites total** (added 2 PlaybookStatusEndpoints).

### Sub-task 012 — Membership (5 files / 16 sites)

#### `Services/Ai/Membership/MembershipResolverService.cs` (2)
- L1000 — `GetAsync` — `membership:{tenantId}:{principalId}` — **(a)**
- L1027 — `SetAsync` — same key — **(a)**

#### `Services/Ai/Membership/MembershipFieldDiscoveryService.cs` (4)
- L461 — `GetAsync` — `membership:discovery:{entityType}` — **(c)** entity-type metadata is org-wide system catalog; candidate exception OR refactor to scope to org/tenant
- L495 — `SetAsync` — same key — **(c)**
- L551 — `RemoveAsync` — same key — **(c)**
- L592 — `RemoveAsync` — same key — **(c)**

#### `Services/Ai/Membership/IdentityNormalizationService.cs` (2)
- L418 — `GetAsync` — `identity-norm:{tenantId}:{systemUserId}` — **(a)** (per source comment "10-minute TTL per ADR-009")
- L450 — `SetAsync` — same key — **(a)**

#### `Services/Dataverse/Privileges/UserPrivilegeChecker.cs` (2)
- L304 — `GetStringAsync` — `privileges:{tenantId}:{userId}:{entity}` — **(a)**
- L337 — `SetStringAsync` — same key — **(a)**

#### `Api/Membership/MembershipEndpoints.cs` (2)
- L389 — `GetAsync` — `membership:endpoint:{tenantId}:{principalId}` (parameter-injected `cache`) — **(a)**
- L445 — `SetAsync` — same key — **(a)**

> 16 sites total.

### Sub-task 013 — Document / AI (13 files / 36 sites)

#### `Services/Ai/Handlers/ClauseAnalyzerHandler.cs` (2)
- L580 — `GetAsync` — `clause:{tenantId}:{documentId}` — **(a)**
- L600 — `SetAsync` — same key — **(a)**

#### `Services/Ai/Handlers/EntityExtractorHandler.cs` (2)
- L570 — `GetAsync` — `entity-extract:{tenantId}:{documentId}` — **(a)**
- L590 — `SetAsync` — same key — **(a)**

#### `Services/Ai/Handlers/InvoiceExtractionToolHandler.cs` (2)
- L376 — `GetAsync` — `invoice-extract:{tenantId}:{documentId}` — **(a)**
- L478 — `SetAsync` — same key — **(a)**

#### `Services/Ai/Handlers/RiskDetectorHandler.cs` (2)
- L719 — `GetAsync` — `risk-detect:{tenantId}:{documentId}` — **(a)**
- L739 — `SetAsync` — same key — **(a)**

#### `Services/Ai/AnalysisDocumentLoader.cs` (2)
- L68 — `SetAsync` — `analysis-doc:{analysisId}` — **(b)** (per source comment "AnalysisCacheKeyPrefix"); needs tenantId addition
- L77 — `GetAsync` — same key — **(b)**

#### `Services/Ai/AnalysisRagProcessor.cs` (2)
- L231 — `GetStringAsync` — `rag-cache:{tenantId}:{queryHash}` — **(a)**
- L260 — `SetStringAsync` — same key — **(a)**

#### `Services/Ai/EmbeddingCache.cs` (2)
- L77 — `GetAsync` — `embedding:{contentHash}` — **(b)** content-hash is collision-resistant, but cross-tenant cache poisoning is theoretically possible; recommend (a) refactor to `embedding:{tenantId}:{hash}` OR (c) document as system-level cache (content hash is tenant-agnostic)
- L129 — `SetAsync` — same key — **(b)**

#### `Services/Ai/ReferenceRetrievalService.cs` (2)
- L390 — `GetAsync` — `reference:{tenantId}:{refId}` — **(a)**
- L426 — `SetAsync` — same key — **(a)**

#### `Services/Ai/Insights/InsightsPlaybookExecutionCache.cs` (4)
- L169 — `GetAsync` — `insights:{tenantId}:{playbookId}:{subjectHash}` — **(a)** (source shows `request.TenantId`)
- L226 — `GetAsync` — same key (race-check) — **(a)**
- L296 — `SetAsync` — same key — **(a)**
- L329 — `RemoveAsync` — same key — **(a)**

#### `Services/Ai/Foundry/AgentServiceClient.cs` (3)
- L120 — `GetStringAsync` — `agent-thread:{tenantId}:{sessionId}` — **(a)** ("AgentServiceClient" docs say "sliding expiry so resumable conversations")
- L317 — `RemoveAsync` — same key — **(a)**
- L380 — `SetStringAsync` — same key — **(a)**

#### `Services/Ai/PlaybookService.cs` (2)
- L363 — `GetStringAsync` — `playbook:{tenantId}:{playbookId}` — **(a)** (nullable `IDistributedCache? _cache` — null-check required in wrapper)
- L476 — `SetStringAsync` — same key — **(a)**

#### `Services/Ai/RecordSearch/RecordSearchService.cs` (2)
- L582 — `GetStringAsync` (`_distributedCache` field) — `record-search:{tenantId}:{queryHash}` — **(a)**
- L607 — `SetStringAsync` — same key — **(a)**

#### `Api/Ai/ChatDocumentEndpoints.cs` (7)
- L363 — `SetAsync` — `doc-upload:{sessionId}:{documentId}` — **(b)** sessionId implicit tenant; needs explicit `tenantId` parameter
- L395 — `SetAsync` — `doc-binary:{sessionId}:{documentId}` — **(b)**
- L422 — `SetAsync` — `doc-upload-meta:{sessionId}:{documentId}` — **(b)**
- L669 — `GetAsync` — persist key (variable `persistKey`) — **(b)**
- L693 — `GetAsync` — `doc-binary:{sessionId}:{documentId}` — **(b)**
- L712 — `GetAsync` — `doc-upload-meta:{sessionId}:{documentId}` — **(b)**
- L802 — `SetAsync` — variable cache key — **(b)**

> 36 sites total. Several **(b)** sites in this group require threading `tenantId` through endpoint parameters / session manager.

### Sub-task 014 — Background jobs / system services (9 files / 22 sites)

#### `Services/Jobs/IdempotencyService.cs` (5)
- L28 — `GetAsync` — `idempotency:processed:{eventId}` — **(c)** event idempotency is cross-tenant by design (Service Bus events have system-level IDs); NFR-08 exception
- L56 — `SetAsync` — same key — **(c)**
- L71 — `GetAsync` — `idempotency:lock:{eventId}` — **(c)**
- L84 — `SetAsync` — same key — **(c)**
- L101 — `RemoveAsync` — same key — **(c)**

#### `Services/Jobs/BatchJobStatusStore.cs` (3)
- L59 — `GetStringAsync` — `batch-job-status:{jobId}` — **(b)** can include tenantId from job context
- L194 — `GetStringAsync` — same key family — **(b)**
- L223 — `SetStringAsync` — same key — **(b)**

#### `Services/Jobs/RecordSyncJob.cs` (2)
- L638 — `GetStringAsync` — `record-sync-watermark:{entityType}` (per `WatermarkKeyPrefix` constant) — **(c)** **watermark is system-level durable bookmark** per source comment "Persist watermark indefinitely (no sliding expiry — this is a durable bookmark)"; NFR-08 exception
- L658 — `SetStringAsync` — same key — **(c)**

#### `Services/GraphTokenCache.cs` (3)
- L66 — `GetStringAsync` — `sdap:graph:token:{tokenHash}` — **(b)** tokenHash is user-derived (SHA256 of token); could include tenantId for cross-tenant key isolation. Recommend (a) refactor to `graph-token:{tenantId}:{tokenHash}`
- L112 — `SetStringAsync` — same key — **(b)**
- L146 — `RemoveAsync` — same key — **(b)**

#### `Services/Workspace/WorkspaceStateService.cs` (3)
- L274 — `GetAsync` — `workspace:{tenantId}:{sessionId}` — **(a)** (per source comment "Per-tenant isolation per ADR-014 + NFR-16 (binding)" — already tenant-scoped)
- L310 — `SetAsync` — same key — **(a)**
- L324 — `RemoveAsync` — same key — **(a)**

#### `Services/Communication/CommunicationAccountService.cs` (3)
- L88 — `GetStringAsync` — `comm-accounts:{tenantId}` — **(a)** (or (b) — verify from source)
- L126 — `SetStringAsync` — same key — **(a)**
- L253 — `RemoveAsync` — `comm-accounts:send-enabled` — **(c)** system-level send-enabled flag; NFR-08 exception

#### `Services/Communication/ApprovedSenderValidator.cs` (2)
- L86 — `GetStringAsync` — `approved-senders` (CacheKey constant) — **(c)** system-level config catalog; NFR-08 exception OR refactor per tenant
- L129 — `SetStringAsync` — same key — **(c)**

#### `Services/SpeAdmin/SpeDashboardSyncService.cs` (2)
- L460 — `GetStringAsync` — `sdap:spe:dashboard:metrics` (CacheKey constant) — **(c)** **system-wide SPE dashboard metrics** (cross-tenant aggregation); NFR-08 exception
- L483 — `SetStringAsync` — same key — **(c)**

#### `Services/Dataverse/MetadataService.cs` (2)
- L241 — `GetStringAsync` — `dataverse:metadata:{entityName}` — **(c)** **Dataverse entity metadata is org-wide schema** (per source doc-comment "ADR-029 — single Redis instance per BFF; per task 010 Q3 decision"); NFR-08 exception
- L271 — `SetStringAsync` — same key — **(c)**

> 22 sites total. **Many (c) exceptions in this group** — `IdempotencyService` (5), `RecordSyncJob` watermark (2), `CommunicationAccountService` send-enabled flag (1), `ApprovedSenderValidator` config (2), `SpeDashboardSyncService` (2), `MetadataService` (2) → **14 of 22 are system-level exception candidates**.

### Sub-task 015 — Auth / User services + ExternalAccess (9 files / 21 sites)

#### `Api/Agent/AgentConfigurationService.cs` (5)
- L56 — `GetStringAsync` — `{CacheKeyPrefix}{tenantId}:exposed-playbooks` — **(a)** tenantId in source
- L68 — `SetStringAsync` — same key — **(a)**
- L86 — `GetStringAsync` — `{CacheKeyPrefix}{tenantId}:capabilities` — **(a)**
- L134 — `RemoveAsync` — `{CacheKeyPrefix}{tenantId}:exposed-playbooks` — **(a)**
- L135 — `RemoveAsync` — `{CacheKeyPrefix}{tenantId}:capabilities` — **(a)**

#### `Api/Agent/AgentConversationService.cs` (4)
- L49 — `GetStringAsync` — `agent-conv:{tenantId}:{conversationId}` — **(a)**
- L80 — `SetStringAsync` — same key — **(a)**
- L109 — `GetStringAsync` — same family — **(a)**
- L133 — `RemoveAsync` — same key — **(a)**

#### `Api/Agent/AgentTokenService.cs` (2)
- L222 — `GetStringAsync` — `agent-token:{tenantId}:{tokenKey}` — **(a)**
- L240 — `SetStringAsync` — same key — **(a)**

#### `Api/ExternalAccess/GrantExternalAccessEndpoint.cs` (1)
- L144 — `RemoveAsync` — `external-access:{tenantId}:{principalId}` (variable cache key) — **(a)**

#### `Api/ExternalAccess/ProjectClosureEndpoint.cs` (1)
- L246 — `RemoveAsync` — `external-access:{tenantId}:{projectId}` — **(a)**

#### `Api/ExternalAccess/RevokeExternalAccessEndpoint.cs` (1)
- L142 — `RemoveAsync` — `external-access:{tenantId}:{principalId}` — **(a)**

#### `Infrastructure/ExternalAccess/ExternalParticipationService.cs` (2)
- L60 — `GetStringAsync` — `external-participation:{tenantId}:{principalId}:{containerId}` — **(a)** (per ADR-009 comment "60s TTL")
- L190 — `SetStringAsync` — same key — **(a)**

#### `Services/Finance/FinanceSummaryService.cs` (3)
- L137 — `GetStringAsync` — `finance:{tenantId}:{matterId}` — **(a)**
- L170 — `SetStringAsync` — same key — **(a)**
- L192 — `RemoveAsync` — same key — **(a)**

#### `Api/Reporting/ReportingEmbedService.cs` (2)
- L131 — `GetStringAsync` — `reporting-embed:{tenantId}:{reportId}` — **(a)**
- L180 — `SetStringAsync` — same key — **(a)**

> 21 sites total. **All (a)** — straightforward atomic migration.

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
