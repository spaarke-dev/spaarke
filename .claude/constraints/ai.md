# AI/ML Constraints

> **Domain**: AI Features, Azure OpenAI, Document Intelligence
> **Source ADRs**: ADR-013, ADR-014, ADR-015, ADR-016
> **Last Updated**: 2025-12-18

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

### Data Governance (ADR-015)

- ✅ **MUST** send minimum text required to AI services
- ✅ **MUST** log only identifiers, sizes, timings, outcome codes
- ✅ **MUST** scope all persisted AI artifacts by tenant
- ✅ **MUST** define retention for stored AI outputs
- ✅ **MUST** version prompts/templates

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
- ❌ **MUST NOT** use Azure Functions for AI
- ❌ **MUST NOT** expose API keys to clients

### Caching (ADR-014)

- ❌ **MUST NOT** cache raw document bytes without ADR-015 approval
- ❌ **MUST NOT** cache streaming tokens
- ❌ **MUST NOT** inline string cache keys

### Data Governance (ADR-015)

- ❌ **MUST NOT** place document bytes in job payloads
- ❌ **MUST NOT** log document contents or extracted text
- ❌ **MUST NOT** log full prompts or model responses

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

| Class | Send to AI? | Log? |
|-------|-------------|------|
| Identifiers | ✅ | ✅ |
| Derived metadata | ✅ | ✅ |
| User prompts | ✅ (minimize) | ⚠️ length only |
| Document content | ⚠️ when required | ❌ |
| Secrets | ❌ | ❌ |

---

## Pattern Files (Complete Examples)

- [AI Endpoint Pattern](../patterns/ai/endpoint-registration.md)
- [AI Caching Pattern](../patterns/ai/caching.md)
- [AI Logging Pattern](../patterns/ai/logging.md)
- [AI Rate Limiting Pattern](../patterns/ai/rate-limiting.md)

---

## Source ADRs (Full Context)

| ADR | Focus | When to Load |
|-----|-------|--------------|
| [ADR-013](../adr/ADR-013-ai-architecture.md) | AI architecture | New AI features |
| [ADR-014](../adr/ADR-014-ai-caching.md) | AI caching | Cache implementation |
| [ADR-015](../adr/ADR-015-ai-data-governance.md) | Data governance | Logging, persistence |
| [ADR-016](../adr/ADR-016-ai-rate-limits.md) | Rate limiting | Throttling implementation |

---

**Lines**: ~130
**Purpose**: Single-file reference for all AI/ML constraints

