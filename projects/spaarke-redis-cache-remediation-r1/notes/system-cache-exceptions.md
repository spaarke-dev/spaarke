# System-Level Cache Exception Allow-List (NFR-08)

> **Task**: 017 — Consolidate system-level cache exception allow-list
> **Source-of-truth (code)**: [`src/server/api/Sprk.Bff.Api/Infrastructure/Cache/SystemCacheKeys.cs`](../../../src/server/api/Sprk.Bff.Api/Infrastructure/Cache/SystemCacheKeys.cs)
> **Spec reference**: NFR-08 + Assumption §3 (escalate at >20 distinct logical resources)

## Summary

| Metric | Count |
|---|---|
| Distinct logical cache resources (allow-list entries) | **11** |
| Raw call-site annotations (`// SYSTEM-LEVEL EXCEPTION (NFR-08): ...`) | 22 |
| AI-wrapper `tenant = "system"` sentinel usages (paired read/write) | 3 services × 2 sites = 6 |
| **Threshold for escalation (spec NFR-08)** | >20 distinct logical resources |
| **Status** | ≤20 — **no escalation needed** |

Note on counting: spec NFR-08 caps distinct logical cache resources (allow-list entries), not raw call sites. Paired read/write/remove operations on the same resource count as one exception. The 22 inline annotation comments and 6 wrapper sites collapse to **11 distinct logical caches** below.

---

## Per-exception justification

Format per CLAUDE.md §11 (Component Justification — three-question template).
Each exception is keyed to its `SystemCacheKeys` constant.

### 1. `SystemCacheKeys.IdempotencyProcessed` — Service Bus event processed marker

- **Sites**: `src/server/api/Sprk.Bff.Api/Services/Jobs/IdempotencyService.cs:28, 57`
- **Raw key**: `idempotency:processed:{eventId}` (eventId = Service Bus message ID)
- **Existing**: No equivalent — idempotency markers are a distinct cache resource.
- **Extension**: Cannot extend a tenant-scoped resource — Service Bus events are cross-tenant by definition; the consumer does not know the originating tenant before deduplication.
- **Cost-of-doing-nothing**: Tenant-scoping the marker would allow the same event to be processed once per tenant key namespace, **breaking the exactly-once delivery invariant** that downstream side effects (email send, status updates) depend on.

### 2. `SystemCacheKeys.IdempotencyLock` — Service Bus event processing lock

- **Sites**: `Services/Jobs/IdempotencyService.cs:73, 87, 105`
- **Raw key**: `idempotency:lock:{eventId}`
- **Existing**: No equivalent — cross-instance lock cache is distinct from the processed marker.
- **Extension**: Cannot extend `IdempotencyProcessed` (different TTL semantics — lock = short-lived, processed = long-lived).
- **Cost-of-doing-nothing**: Tenant-scoped lock keys would allow two workers in different tenant namespaces to both claim the same Service Bus event, **producing duplicate side effects** (double-sent emails, double-updated records).

### 3. `SystemCacheKeys.BatchJob` — email-batch job status record

- **Sites**: `Services/Jobs/BatchJobStatusStore.cs:59, 195, 225`
- **Raw key**: `batch:job:{jobId}`
- **Existing**: No equivalent — batch-job status is a distinct resource (separate from per-email idempotency).
- **Extension**: Not viable — `JobContract` (the value type) carries no tenantId field; the status endpoint that reads this cache only has the jobId path parameter.
- **Cost-of-doing-nothing**: Tenant-scoping would require either (a) adding tenantId to the public status endpoint URL (breaking the existing client contract) or (b) failing every status lookup with "tenant required" errors. **The job status endpoint would become unusable.**

### 4. `SystemCacheKeys.RecordSyncWatermark` — Dataverse RecordSync per-entity watermark

- **Sites**: `Services/Jobs/RecordSyncJob.cs:637, 656`
- **Raw key**: `recordsync:watermark:{entityType}`
- **Existing**: No equivalent — watermark is a distinct durable bookmark resource.
- **Extension**: Cannot extend tenant-scoped — RecordSync polls Dataverse at the org level (ADR-029: one BFF / one Redis per org); the watermark is the highest `modifiedon` seen across the entire org for that entity type.
- **Cost-of-doing-nothing**: Per-tenant watermarks would **fragment the bookmark** so every restart re-reads records the org has already seen; on every poll cycle the job would process the same records repeatedly until each tenant's watermark caught up. Operationally: 10x+ unnecessary Dataverse calls and duplicate downstream emits.

### 5. `SystemCacheKeys.GraphToken` — OBO Graph access-token cache

- **Sites**: `Services/GraphTokenCache.cs:66, 113, 148`
- **Raw key**: `sdap:graph:token:{sha256(userToken)}`
- **Existing**: No equivalent — OBO token cache is distinct from app-only token caches managed by `DefaultAzureCredential`.
- **Extension**: Not viable — the cache is keyed by SHA256 of the user token, which **implicitly identifies its issuing tenant** (the token's `tid` claim). Adding a tenantId prefix would not add isolation. The caller (`GraphClientFactory.CreateOnBehalfOfClientAsync`) does not have tenantId in scope at the call site.
- **Cost-of-doing-nothing**: Refactoring to plumb tenantId through the OBO chain would touch every endpoint that calls `GraphClientFactory`, with **no isolation benefit** (the token hash is already user-and-tenant-unique by construction).

### 6. `SystemCacheKeys.DataverseEntityMetadata` — entity-metadata schema cache

- **Sites**: `Services/Dataverse/MetadataService.cs:241, 272`
- **Raw key**: `sdap:dv:entitymetadata:{logicalName}`
- **Existing**: No equivalent — schema metadata is a distinct cache from record-data caches.
- **Extension**: Cannot extend tenant-scoped — entity schemas (column lists, attribute types, option sets) are **org-wide configuration** (ADR-029: one BFF / one Dataverse org). Schema is identical for all "tenants" sharing the BFF.
- **Cost-of-doing-nothing**: Tenant-scoping would **multiply schema-cache reads** by the tenant count (typically 1, but architected for N), with zero additional isolation since the schema is identical across keys. Defeats the entire purpose of the cache.

### 7. `SystemCacheKeys.SpeDashboardMetrics` — SPE dashboard cross-tenant metrics

- **Sites**: `Services/SpeAdmin/SpeDashboardSyncService.cs:460, 484`
- **Raw key**: `sdap:spe:dashboard:metrics`
- **Existing**: No equivalent — SPE dashboard aggregate is a distinct admin-facing metric resource.
- **Extension**: Cannot extend tenant-scoped — the dashboard's purpose is to show **cross-tenant aggregates** (total containers, total drive size, container-count-by-status across the entire SPE org). Tenant-scoping would render the metric meaningless.
- **Cost-of-doing-nothing**: Tenant-scoping would require either per-tenant aggregation (which is a different feature — not what the SPE admin dashboard is) or returning empty results for the global view. **The admin dashboard would have no data to display.**

### 8. `SystemCacheKeys.CommApprovedSenders` — communication approved-senders merged list

- **Sites**: `Services/Communication/ApprovedSenderValidator.cs:86, 130`
- **Raw key**: `communication:accounts:merged`
- **Existing**: Could in principle merge with `CommAccountFlags` (#9), but the two have different invariants (this one is the merged config + Dataverse list; #9 is two separate flag sets).
- **Extension**: Not viable as tenant-scoped — `CommunicationOptions` (config) is process-wide and `sprk_communicationaccount` records are org-wide (one set per BFF org per ADR-029).
- **Cost-of-doing-nothing**: Tenant-scoping would require a tenant-id-aware caller chain for inbound-email validation (where tenant context **does not exist** — emails arrive before any user authenticates). **Inbound email processing would fail at the validation step.**

### 9. `SystemCacheKeys.CommAccountFlags` — send/receive-enabled flag caches

- **Sites**: `Services/Communication/CommunicationAccountService.cs:88, 127, 255`
- **Raw keys**: `comm:accounts:send-enabled`, `comm:accounts:receive-enabled`
- **Existing**: Distinct from #8 (this is two flag-bit caches; #8 is the merged config list).
- **Extension**: Could collapse to one constant naming both keys; kept as `CommAccountFlags` to document that the two flag caches share semantics (org-wide, same TTL, same invalidation path).
- **Cost-of-doing-nothing**: Tenant-scoped flag caches would require tenant-context for outbound email-send decisions, which **does not exist** for system-initiated emails (digests, notifications, briefing summaries). **Outbound system emails would fail enable-check.**

### 10. `SystemCacheKeys.Embedding` — content-addressed embedding cache (AI wrapper)

- **Sites**: `Services/Ai/EmbeddingCache.cs:84, 135` (uses `SystemTenantSentinel` against `ITenantCache`)
- **On-wire key**: `spaarke:tenant:system:embedding:{contentHash}:v1`
- **Existing**: No equivalent — embedding cache is distinct from RAG result caches and document-text caches.
- **Extension**: Cannot extend tenant-scoped — the public API `IEmbeddingCache.GetEmbeddingAsync(string contentHash)` takes **only** a contentHash, and embeddings of identical content under the same model are **deterministic and tenant-agnostic**. Two tenants embedding the same paragraph get the same vector.
- **Cost-of-doing-nothing**: Tenant-scoping would **eliminate the cache hit rate's primary driver** — duplicate content across tenants (boilerplate clauses, templates, repeated golden references). Embedding API cost would multiply by tenant count for no functional benefit.

### 11. `SystemCacheKeys.PlaybookByName` — playbook-by-name lookup (AI wrapper)

- **Sites**: `Services/Ai/PlaybookService.cs:366, 468` (uses `SystemTenantSentinel`)
- **On-wire key**: `spaarke:tenant:system:playbook-by-name:{name}:v1`
- **Existing**: No equivalent — playbook lookup by ID exists elsewhere, but lookup-by-name is the wider hot path.
- **Extension**: Not viable — `IPlaybookService.GetByNameAsync(string name)` takes only a name; **playbooks are org-wide** per ADR-029 (one Dataverse org per BFF, all playbooks share that org). The signature has no tenantId in scope and adding it would be a no-op (all tenants would resolve to the same playbook).
- **Cost-of-doing-nothing**: Tenant-scoping would multiply the cache footprint by tenant count without adding isolation, and would fail to serve playbook resolution when the caller (e.g., a system job, a chat session in pre-auth bootstrap) has no tenant context yet.

### 12. `SystemCacheKeys.DocText` — document-text extraction cache (AI wrapper)

- **Sites**: `Services/Ai/TextExtractorService.cs:211, 269` (uses `SystemTenantSentinel`)
- **On-wire key**: `spaarke:tenant:system:doc-text:{driveId}:{itemId}:{etag}:v1`
- **Existing**: No equivalent — extracted-text cache is distinct from the binary download cache.
- **Extension**: Cannot extend tenant-scoped — the SPE drive+item+etag tuple is **already a content-versioned identifier**. ETag changes auto-invalidate when the file changes. `ITextExtractor.ExtractAsync` does not have tenantId in scope at the call site.
- **Cost-of-doing-nothing**: Tenant-scoping would **prevent reuse across tenants that share container access** (rare but supported in SPE multi-tenant scenarios). Worse, the public API would need to add a tenantId parameter, requiring breaking changes across all 7+ callers (analysis pipeline, embedding pipeline, RAG indexer, etc.).

---

## Validation

```
Distinct logical cache resources: 11
Spec NFR-08 escalation threshold: > 20
Status: PASS (no escalation needed)
```

Adding to this list requires:
1. Architecture review (per `SystemCacheKeys.cs` xmldoc).
2. New three-question justification entry in this file.
3. A new constant in `SystemCacheKeys.cs`.
4. If the count exceeds 20, an architecture-level redesign per NFR-08 (escalation gate).
