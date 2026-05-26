# ADR-015: AI Data Governance (Concise)

> **Status**: Accepted (Amended 2026-05-17)
> **Domain**: AI/ML Data Privacy
> **Last Updated**: 2026-05-17

---

## Decision

Apply **data minimization** and **logging hygiene** to all AI operations. Never log content; always scope by tenant; define retention for any persistence.

**Rationale**: AI features involve sensitive data. Explicit rules prevent accidental privacy violations.

---

## Constraints

### ✅ MUST

- **MUST** send minimum text required to achieve outcome
- **MUST** log only identifiers, sizes, timings, outcome codes
- **MUST** scope all persisted AI artifacts by tenant
- **MUST** define retention and deletion behavior for stored outputs
- **MUST** version prompts/templates for reproducibility

### ❌ MUST NOT

- **MUST NOT** place document bytes in Service Bus job payloads (ADR-004)
- **MUST NOT** log document contents, extracted text, or email bodies
- **MUST NOT** log full prompts or model responses
- **MUST NOT** cache sensitive content without explicit approval

---

## Data Classification

| Class | Examples | Send to AI? | Log? |
|-------|----------|-------------|------|
| Identifiers | `documentId`, `driveId` | ✅ | ✅ |
| Derived metadata | file name, MIME type | ✅ | ✅ |
| User prompts | chat message text | ✅ (minimize) | ⚠️ length only |
| Document content | extracted text, email body | ⚠️ when required | ❌ |
| Secrets | access tokens, keys | ❌ | ❌ |

---

## Logging Rules

**Allowed in logs:**
- Correlation identifiers (`HttpContext.TraceIdentifier`)
- Document/record IDs (GUIDs)
- Sizes and counts (bytes, token estimates)
- Error codes and upstream request IDs

**Never in logs:**
- Document contents or extracted text
- Email bodies or attachment content
- Full prompts or model responses
- Secrets or tokens

**See**: [AI Logging Pattern](../patterns/ai/logging.md)

---

## Governed Data Stores (Amendment — 2026-05-17)

The constraints above apply to **application logs** (Tier 1). R2 introduces two additional governed storage tiers with explicit exceptions to the "no content in logs" rule.

| Tier | Store | Content Allowed | Retention | Access | GDPR Erasure |
|------|-------|----------------|-----------|--------|--------------|
| **Tier 1: App Logs** | App Insights / Azure Monitor | Metadata only (IDs, sizes, timings) | 90 days | SRE | N/A |
| **Tier 2: Compliance Audit** | Cosmos DB `audit` container | Response hash (SHA-256), tool names, doc IDs, safety scores. **No verbatim text.** | 7 years (configurable) | Compliance role only | No (legal hold) |
| **Tier 3: Work History** | Cosmos DB `sessions`, `prompts`, `memory`, `feedback` | Full messages, widget state, matter facts. User-owned data. | 90 days default | Owning user + admin | Yes (Art. 17) |

### Tier-Specific MUST Rules

- **MUST** treat Tier 1 (app logs) as strict — ADR-015 original constraints apply in full
- **MUST** store only metadata + hashes in Tier 2 audit log — never verbatim prompts or responses
- **MUST** partition all Tier 2/3 data by `tenantId` — no cross-tenant queries
- **MUST** apply immutable policy to Tier 2 (append-only, no updates/deletes)
- **MUST** support user-initiated deletion in Tier 3 (GDPR right to erasure)
- **MUST** define retention policy on every Cosmos container at provisioning time

### Tier-Specific MUST NOT Rules

- **MUST NOT** store verbatim AI response text in Tier 2 audit log (hash only)
- **MUST NOT** store Tier 3 work history without tenant-scoped partition key
- **MUST NOT** exempt Tier 3 from GDPR deletion requirements

### Cosmos DB Container Mapping (cross-ref: AIPU2-002)

| Container | Tier | Partition Key | Purpose |
|-----------|------|---------------|---------|
| `audit` | 2 | `/tenantId` | Append-only compliance log |
| `sessions` | 3 | `/tenantId` | Work history (messages, widgets, artifacts) |
| `prompts` | 3 | `/tenantId` | Saved prompt templates |
| `memory` | 3 | `/tenantId` | Matter-scoped AI memory (structured facts) |
| `feedback` | 3 | `/tenantId` | Per-response feedback (thumbs + text) |

---

## Storage Requirements

If AI outputs are stored, define:
1. **Location** (Dataverse, SPE, Cosmos DB — specify tier above)
2. **Retention duration** (must match tier defaults or document exception)
3. **Authorization requirements** (who can read)
4. **Deletion trigger** (manual, cascade, retention job, GDPR request)

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-004](ADR-004-job-contract.md) | No content in job payloads |
| [ADR-009](ADR-009-redis-caching.md) | Safe caching |
| [ADR-013](ADR-013-ai-architecture.md) | AI architecture |
| [ADR-014](ADR-014-ai-caching.md) | Caching policy |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-015-ai-data-governance.md](../../docs/adr/ADR-015-ai-data-governance.md)

---

**Lines**: ~95

