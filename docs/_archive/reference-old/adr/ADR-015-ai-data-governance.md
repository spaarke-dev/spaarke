# ADR-015: AI Data Governance (PII, Retention, Redaction, Logging)

| Field | Value |
|-------|-------|
| Status | **Proposed** |
| Date | 2025-12-12 |
| Updated | 2025-12-12 |
| Authors | Spaarke Engineering |
| Sprint | TBD |

---

## Context

AI features require sending data to external services (Azure OpenAI, Document Intelligence, AI Search) and storing some AI outputs. Without explicit rules, teams tend to:

- Over-send data (privacy and compliance risk)
- Log or persist sensitive text inadvertently
- Reuse/caches unsafe artifacts
- Produce inconsistent “what data went where” auditability

## Decision

### Decision Rules

| Rule | Requirement |
|------|-------------|
| **Minimize data** | Send the minimum text required to achieve the outcome; prefer identifiers + retrieval at runtime when possible. |
| **No raw bytes in jobs** | Do not place document bytes/attachments/email bodies in Service Bus job payloads (ADR-004). |
| **No content in logs** | Logs must not include document contents, extracted text, email body, or full prompts/responses. |
| **PII-safe telemetry** | Telemetry records sizes, timings, outcome codes, and correlation IDs; not content. |
| **Explicit retention** | Any persistence of AI inputs/outputs must define retention and deletion behavior. |
| **Safe caching only** | Caching of AI artifacts must comply with this ADR and ADR-014. |
| **Tenant boundaries** | All persisted AI artifacts must be tenant-scoped; never mix tenants. |

## Scope

Applies to:
- AI endpoints and services (`src/server/api/Sprk.Bff.Api/Api/Ai/*`, `Services/Ai/*`)
- AI-adjacent job handlers (`src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/*`)
- Client surfaces that assemble prompts (PCF controls and shared UI library)

## Non-goals

- Replacing enterprise compliance frameworks (this is an engineering policy)
- Mandating a single redaction provider/tool

## Data classification (Engineering)

| Class | Examples | Allowed to send to AI services? | Allowed to log? |
|------|----------|----------------------------------|-----------------|
| **Identifiers** | `documentId`, `driveId`, `itemId`, record IDs | ✅ | ✅ |
| **Derived metadata** | file name, MIME type, sizes | ✅ | ✅ |
| **User-entered prompts** | chat message text | ✅ (minimize) | ⚠️ only length, not content |
| **Document content** | extracted text, email body, attachments content | ⚠️ only when required | ❌ |
| **Secrets** | access tokens, keys | ❌ | ❌ |

## Operationalization

### Logging and ProblemDetails

- Use ProblemDetails responses consistently for API failures.
- Prefer `Sprk.Bff.Api.Infrastructure.Errors.ProblemDetailsHelper` for consistent error codes and correlation extensions.
- Log only:
  - Correlation identifiers (`HttpContext.TraceIdentifier`)
  - Document/record IDs (GUIDs)
  - Sizes and counts (bytes, token estimates)
  - Error codes and upstream request IDs (where available)

### Prompt/template discipline

- Prompts/templates must be versioned (e.g., `promptVersion`) so outputs are reproducible and cache keys can be versioned (ADR-014).
- Never log full prompts or model responses.

### Storage and retention

If AI outputs are stored (Dataverse, SPE, or other), define:
- Storage location and schema
- Retention duration
- Who can read the stored output (authorization requirements)
- How deletion is triggered (manual delete, record deletion cascade, retention job)

## Failure modes

- **Leaking content via logs** → high severity incident.
- **Over-caching sensitive data** → retention + privacy violation.
- **Inability to audit** what data was sent → compliance gap.

## AI-Directed Coding Guidance

When implementing an AI feature:
- Assume everything is sensitive until classified.
- Do not log prompts, extracted text, or model outputs.
- Push content handling into a small number of services (single chokepoints).
- Emit telemetry only for counts/timings/outcomes.

## Compliance checklist

- [ ] No raw content (document bytes, email bodies, extracted text) in logs.
- [ ] Job payloads contain identifiers only (ADR-004).
- [ ] Any caching of derived content follows ADR-014.
- [ ] Stored AI outputs have retention and authorization rules.
- [ ] Prompts/templates are versioned and not logged.

## Related ADRs

- [ADR-004: Async job contract](./ADR-004-async-job-contract.md)
- [ADR-009: Redis-first caching](./ADR-009-caching-redis-first.md)
- [ADR-013: AI architecture](./ADR-013-ai-architecture.md)
- [ADR-014: AI caching and reuse policy](./ADR-014-ai-caching-and-reuse-policy.md)

---

## Revision History

| Date | Version | Changes | Author |
|------|---------|---------|--------|
| 2025-12-12 | 0.1 | Initial proposed ADR | Spaarke Engineering |
