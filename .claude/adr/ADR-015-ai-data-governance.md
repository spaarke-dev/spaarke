# ADR-015: AI Data Governance (Concise)

> **Status**: Proposed
> **Domain**: AI/ML Data Privacy
> **Last Updated**: 2025-12-18

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

## Storage Requirements

If AI outputs are stored, define:
1. **Location** (Dataverse, SPE, other)
2. **Retention duration**
3. **Authorization requirements** (who can read)
4. **Deletion trigger** (manual, cascade, retention job)

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

