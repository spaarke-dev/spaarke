# AI/ML Constraints

> **Domain**: AI Features, Azure OpenAI, Document Intelligence
> **Source ADRs**: ADR-013, ADR-014, ADR-015, ADR-016
> **Last Updated**: 2026-05-17
> **Last Reviewed**: 2026-05-17
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Current (pattern file links corrected)

---

## When to Load This File

Load when:
- Creating AI endpoints or services
- Implementing document analysis features
- Working with Azure OpenAI, AI Search, or Document Intelligence
- Adding caching for AI operations
- Implementing rate limiting for AI features

---

## MUST Rules

### Architecture (ADR-013)

- ✅ **MUST** follow ADR-001 Minimal API patterns for AI endpoints
- ✅ **MUST** use endpoint filters for AI authorization (ADR-008)
- ✅ **MUST** access files through SpeFileStore only (ADR-007)
- ✅ **MUST** use Job Contract for background AI work (ADR-004)
- ✅ **MUST** apply rate limiting to all AI endpoints

### Caching (ADR-014)

- ✅ **MUST** use Redis for cross-request caching (ADR-009)
- ✅ **MUST** use `RequestCache` for per-request collapse
- ✅ **MUST** centralize cache keys/TTLs in code
- ✅ **MUST** include version input in cache keys
- ✅ **MUST** scope cache keys by tenant

### Data Governance (ADR-015, amended 2026-05-17)

- ✅ **MUST** send minimum text required to AI services
- ✅ **MUST** log only identifiers, sizes, timings, outcome codes (Tier 1 app logs)
- ✅ **MUST** scope all persisted AI artifacts by tenant (`/tenantId` partition key)
- ✅ **MUST** define retention for stored AI outputs
- ✅ **MUST** version prompts/templates
- ✅ **MUST** store only metadata + hashes in Tier 2 audit log (never verbatim text)
- ✅ **MUST** apply immutable policy to Tier 2 audit container (append-only)
- ✅ **MUST** support GDPR right-to-erasure for Tier 3 work history data

### Rate Limits (ADR-016)

- ✅ **MUST** apply rate limiting to all AI endpoints
- ✅ **MUST** bound concurrency for upstream AI calls
- ✅ **MUST** use async jobs for large/batch work
- ✅ **MUST** return clear `429`/`503` under load

---

## MUST NOT Rules

### Architecture (ADR-013)

- ❌ **MUST NOT** create separate AI microservice
- ❌ **MUST NOT** call Azure AI directly from PCF
- ❌ **MUST NOT** host AI BFF endpoints in Azure Functions (Functions are permitted for out-of-band AI integration work — e.g., Dataverse → AI Search sync, closure-extraction pipelines, scheduled re-indexers — see ADR-001)
- ❌ **MUST NOT** expose API keys to clients

### Caching (ADR-014)

- ❌ **MUST NOT** cache raw document bytes without ADR-015 approval
- ❌ **MUST NOT** cache streaming tokens
- ❌ **MUST NOT** inline string cache keys

### Data Governance (ADR-015, amended 2026-05-17)

- ❌ **MUST NOT** place document bytes in job payloads
- ❌ **MUST NOT** log document contents or extracted text (Tier 1 app logs)
- ❌ **MUST NOT** log full prompts or model responses (Tier 1 app logs)
- ❌ **MUST NOT** store verbatim text in Tier 2 audit log (hash only)
- ❌ **MUST NOT** allow programmatic deletion of Tier 2 audit entries

### Rate Limits (ADR-016)

- ❌ **MUST NOT** rely on upstream throttling as control
- ❌ **MUST NOT** allow unbounded `Task.WhenAll` on throttled services
- ❌ **MUST NOT** retry without bounds

---

## Quick Reference Patterns

### AI Endpoint Registration

```csharp
var group = app.MapGroup("/api/ai/analysis")
    .RequireAuthorization()
    .WithTags("AI");

group.MapPost("/execute", StreamAnalysis)
    .RequireRateLimiting("ai-stream")
    .AddEndpointFilter<AnalysisAuthorizationFilter>();
```

### Cache Key Pattern

```csharp
var key = DistributedCacheExtensions.CreateKey(
    "ai-embedding", tenantId, documentId, $"v:{rowVersion}");
```

### Data Classification

| Class | Send to AI? | Tier 1 (App Logs) | Tier 2 (Audit) | Tier 3 (Work History) |
|-------|-------------|-------------------|----------------|----------------------|
| Identifiers | ✅ | ✅ | ✅ | ✅ |
| Derived metadata | ✅ | ✅ | ✅ | ✅ |
| User prompts | ✅ (minimize) | ⚠️ length only | ❌ (hash only) | ✅ (user-owned) |
| AI responses | N/A | ⚠️ length only | ❌ (hash only) | ✅ (user-owned) |
| Document content | ⚠️ when required | ❌ | ❌ | ❌ |
| Secrets | ❌ | ❌ | ❌ | ❌ |

---

## Pattern Files (Complete Examples)

- [Analysis Scopes Pattern](../patterns/ai/analysis-scopes.md)
- [Streaming Endpoints Pattern](../patterns/ai/streaming-endpoints.md)
- [Text Extraction Pattern](../patterns/ai/text-extraction.md)
- [Distributed Cache Pattern](../patterns/caching/distributed-cache.md)
- [API Resilience Pattern](../patterns/api/resilience.md)

---

## Source ADRs (Full Context)

| ADR | Focus | When to Load |
|-----|-------|--------------|
| [ADR-013](../adr/ADR-013-ai-architecture.md) | AI architecture | New AI features |
| [ADR-014](../adr/ADR-014-ai-caching-and-reuse-policy.md) | AI caching | Cache implementation |
| [ADR-015](../adr/ADR-015-ai-data-governance.md) | Data governance | Logging, persistence |
| [ADR-016](../adr/ADR-016-ai-cost-rate-limit-and-backpressure.md) | Rate limiting | Throttling implementation |

---

**Lines**: ~130
**Purpose**: Single-file reference for all AI/ML constraints

