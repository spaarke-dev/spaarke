# ADR-015: AI Data Governance (PII, Retention, Redaction, Logging)

| Field | Value |
|-------|-------|
| Status | **Accepted (Amended 2026-05-17)** |
| Date | 2025-12-12 |
| Updated | 2026-05-17 |
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

## Governed Data Stores (Amendment — 2026-05-17)

### Rationale

R2 introduces AI work history persistence (Cosmos DB) and compliance audit logging. The original ADR's blanket "no content in logs" rule was designed for application telemetry (App Insights) where content leakage is an uncontrolled privacy risk. Purpose-built governed stores have different characteristics:

- **Compliance audit** (Tier 2): Append-only, immutable, restricted access. Stores metadata + response hashes for tamper detection — never verbatim text. Required for legal malpractice defense and regulatory compliance.
- **Work history** (Tier 3): User-owned session data with GDPR erasure rights. Stores full conversation for session restore. Access restricted to owning user + admin. Retention-governed with explicit deletion support.

These tiers are **explicit exceptions** to the "no content in logs" constraint. The original constraint remains in full force for Tier 1 (application logs/telemetry).

### Three-Tier Data Governance Model

| Tier | Store | What's Stored | Retention | Access Control | GDPR Art. 17 |
|------|-------|--------------|-----------|----------------|--------------|
| **1: App Logs** | App Insights, Azure Monitor, structured logging | Identifiers, sizes, timings, error codes, correlation IDs. **No content.** | 90 days | SRE / platform team | N/A (no PII) |
| **2: Compliance Audit** | Cosmos DB `audit` container (immutable policy) | Response hash (SHA-256), tool names invoked, document IDs accessed, safety check results (pass/fail/scores), matter context ID. **No verbatim prompts or responses.** | 7 years (configurable per tenant, legal hold override) | Compliance officer role only | No — legal retention obligation supersedes |
| **3: Work History** | Cosmos DB `sessions`, `prompts`, `memory`, `feedback` containers | Full user + AI messages, widget state snapshots, matter-scoped facts, prompt templates, feedback text. User-owned data. | 90 days default (configurable). User can extend or delete. | Owning user + tenant admin | Yes — user can request deletion of their sessions, prompts, memory, feedback |

### Tier-Specific Constraints

**Tier 1 (App Logs)** — Original ADR-015 constraints apply without modification:
- MUST NOT log document contents, extracted text, email bodies
- MUST NOT log full prompts or model responses
- MUST log only identifiers, sizes, timings, outcome codes

**Tier 2 (Compliance Audit)**:
- MUST store only structured metadata (never verbatim text)
- MUST compute SHA-256 hash of full response for tamper detection
- MUST use append-only container policy (no updates, no deletes)
- MUST partition by `tenantId` with no cross-tenant query capability
- MUST configure retention policy at container provisioning (default: 7 years)
- MUST NOT store verbatim prompts or response text (hash only)
- MUST NOT allow programmatic deletion (except via retention policy expiry or legal hold release)

**Tier 3 (Work History)**:
- MUST partition by `tenantId` with no cross-tenant query capability
- MUST support user-initiated deletion (GDPR right to erasure, Art. 17)
- MUST define retention policy at container provisioning (default: 90 days)
- MUST encrypt at rest (Cosmos default) and in transit (TLS)
- MUST NOT retain data beyond retention period unless user explicitly extends
- MUST NOT expose Tier 3 data to users other than the owning user and tenant admin

### Cosmos DB Container Mapping

Provisioned by task AIPU2-002 (Cosmos DB infrastructure).

| Container | Tier | Partition Key | Immutable | Purpose |
|-----------|------|---------------|-----------|---------|
| `audit` | 2 | `/tenantId` | Yes (append-only) | Compliance log: every AI interaction recorded |
| `sessions` | 3 | `/tenantId` | No | Work history: messages, widget state, tool results |
| `prompts` | 3 | `/tenantId` | No | Saved prompt templates (personal/team ownership) |
| `memory` | 3 | `/tenantId` | No | Matter-scoped AI memory (structured facts) |
| `feedback` | 3 | `/tenantId` | No | Per-response feedback (thumbs up/down + text) |

### Access Control Matrix

| Role | Tier 1 (Logs) | Tier 2 (Audit) | Tier 3 (History) |
|------|---------------|----------------|-----------------|
| End user | No | No | Own data only |
| Tenant admin | No | Read-only | All tenant data |
| Compliance officer | No | Read-only | No (unless delegated) |
| SRE / Platform | Read/Write | No | No |

---

## Compliance checklist

- [ ] No raw content (document bytes, email bodies, extracted text) in Tier 1 logs.
- [ ] Job payloads contain identifiers only (ADR-004).
- [ ] Any caching of derived content follows ADR-014.
- [ ] Stored AI outputs have retention and authorization rules.
- [ ] Prompts/templates are versioned and not logged (Tier 1).
- [ ] Tier 2 audit entries contain hashes only, never verbatim text.
- [ ] Tier 3 work history supports user-initiated deletion (GDPR Art. 17).
- [ ] All Cosmos containers have retention policies configured at provisioning.

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

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-015 Concise](../../.claude/adr/ADR-015-ai-data-governance.md) - ~95 lines
- [AI Constraints](../../.claude/constraints/ai.md) - MUST/MUST NOT rules

**When to load this full ADR**: Historical context, data classification details, compliance checklists.
