# ADR-014: AI Caching and Reuse Policy

| Field | Value |
|-------|-------|
| Status | **Proposed** |
| Date | 2025-12-12 |
| Updated | 2025-12-12 |
| Authors | Spaarke Engineering |
| Sprint | TBD |

---

## Context

Spaarke is expanding AI usage (Azure OpenAI, AI Search, Document Intelligence) and expects large-volume data exchange between the application and AI services. Without a consistent caching/reuse policy we risk:

- Higher latency and unpredictable tail performance
- Excessive cost (repeated extraction/embedding/completions)
- Upstream throttling (Graph/SPE, Document Intelligence, OpenAI)
- Inconsistent implementation (ad-hoc keys, TTLs, and unsafe caching of sensitive content)

ADR-009 establishes the general caching stance (Redis-first + per-request cache). This ADR adds **AI-specific** rules for what to cache, how to key, and how to keep caching safe.

## Decision

### Decision Rules

| Rule | Requirement |
|------|-------------|
| **Redis-first** | Cross-request caching uses `IDistributedCache` (Redis) per ADR-009. |
| **Request collapse** | Within a request, use `Spaarke.Core.Cache.RequestCache` to collapse duplicate loads. |
| **Centralize keys/TTLs** | Cache keys and TTL defaults must be defined in code (single place), not duplicated across endpoints/services. |
| **Versioned keys** | Keys must include a version input (e.g., Dataverse row version, file ETag/lastModified, or explicit schema version) to avoid stale/incorrect reuse. |
| **No unsafe content caching** | Do not cache raw document bytes, email bodies, or other high-risk PII payloads unless explicitly approved by ADR-015 rules and encryption/retention requirements are met. |
| **Tenant isolation** | Keys must be tenant-scoped (and user-scoped when content is user-context derived). |
| **Don’t assume caching** | ADR documents must not claim a specific key/TTL unless it is implemented in code. |

### Cacheable Artifacts (Default)

| Artifact | Typical Source | Cacheable? | Notes |
|---------|----------------|-----------|------|
| File metadata (name/size/ETag) | SPE/Graph | ✅ | Short TTL; tenant-scoped. |
| Extracted text | Document Intelligence / native extract | ✅* | Cache only if governed by ADR-015; prefer storing derived text in Dataverse when appropriate. |
| Embeddings | OpenAI embeddings | ✅ | Long TTL; versioned by embedding model + content version. |
| AI Search results | AI Search | ✅ | Short TTL; include query hash + filters. |
| Model completion outputs (summaries/entities) | OpenAI | ✅* | Only if safe to reuse; version by prompt template + model + content version. |
| Streaming tokens | OpenAI SSE | ❌ | Stream tokens aren’t stable cache artifacts; cache only final outcome. |

`✅*` requires ADR-015 data governance compliance.

## Scope

Applies to:
- AI endpoints (`src/server/api/Sprk.Bff.Api/Api/Ai/*`)
- AI services (`src/server/api/Sprk.Bff.Api/Services/Ai/*`)
- Job handlers that call AI (`src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/*`)

## Non-goals

- Picking the “perfect” TTLs up front
- Forcing caching for every AI call
- Adding a new cache vendor beyond Redis

## Operationalization

### Code touchpoints (Current)

- L1 per-request cache: `src/server/shared/Spaarke.Core/Cache/RequestCache.cs`
- Redis patterns: `src/server/shared/Spaarke.Core/Cache/DistributedCacheExtensions.cs`
- AI entry points: `src/server/api/Sprk.Bff.Api/Api/Ai/DocumentIntelligenceEndpoints.cs`, `AnalysisEndpoints.cs`, `RecordMatchEndpoints.cs`

### Required implementation pattern

- Use `RequestCache` to collapse duplicates within a request (e.g., repeated metadata loads).
- Use `IDistributedCache` + `DistributedCacheExtensions.GetOrCreateAsync(...)` for cross-request reuse.
- Use `DistributedCacheExtensions.CreateKey(category, identifier, parts...)` for consistent key formatting.

### Key design requirements

Keys must include:
- **Tenant identifier** (or an equivalent “environment/tenant” discriminator)
- **Artifact category** (e.g., `ai-text`, `ai-embedding`, `ai-search`)
- **Stable identifiers** (e.g., `documentId`, `driveId:itemId`, or canonical subject ID)
- **Version** (via the `:v:{version}` suffix support in `DistributedCacheExtensions`)

## Failure modes

- **Stale reuse** → wrong summaries/entities. Prevent with versioned keys.
- **Cross-tenant leakage** → critical security incident. Prevent with tenant-scoped keys and isolation.
- **Cross-user leakage** (OBO-derived content cached globally) → security incident. Prevent by scoping or avoiding caching those artifacts.
- **Cache stampede** → downstream throttling. Prevent with `GetOrCreateAsync` single-flight patterns at the call-site and bounded concurrency (ADR-016).

## AI-Directed Coding Guidance

When adding caching to an AI path:
- Identify the **artifact** and the **version input** (rowVersion/ETag/prompt version).
- Choose `RequestCache` (same request) vs Redis (cross request).
- Use a centralized key builder (do not inline string keys).
- Default to caching **derived** artifacts (text, embeddings) rather than raw content.

## Compliance checklist

- [ ] Redis caching uses `IDistributedCache` and `DistributedCacheExtensions` (ADR-009).
- [ ] Keys are tenant-scoped and versioned.
- [ ] No raw bytes / high-risk PII cached unless ADR-015 allows it.
- [ ] TTLs are explicit and centrally defined.
- [ ] Cache hit/miss telemetry exists for expensive artifacts.

## Related ADRs

- [ADR-009: Caching policy — Redis-first with per-request cache](./ADR-009-caching-redis-first.md)
- [ADR-013: AI Architecture](./ADR-013-ai-architecture.md)
- [ADR-015: AI Data Governance (PII, retention, logging)](./ADR-015-ai-data-governance.md)
- [ADR-016: AI cost/rate-limit & backpressure](./ADR-016-ai-cost-rate-limit-and-backpressure.md)

---

## Revision History

| Date | Version | Changes | Author |
|------|---------|---------|--------|
| 2025-12-12 | 0.1 | Initial proposed ADR | Spaarke Engineering |

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-014 Concise](../../.claude/adr/ADR-014-ai-caching.md) - ~95 lines
- [AI Constraints](../../.claude/constraints/ai.md) - MUST/MUST NOT rules

**When to load this full ADR**: Historical context, operationalization details, code touchpoints.
