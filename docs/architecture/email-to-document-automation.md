# Email-to-Document Automation Architecture

> **Last Updated**: January 2026
> **Status**: Production (R1 pipeline — see communication-service-architecture.md for R2)
> **Project**: email-to-document-automation-r2
> **Related ADRs**: ADR-001 (Minimal API + BackgroundService), ADR-008 (Endpoint Filters)

> **Note**: This file documents the R1 email-to-document pipeline. The R2 Communication Service supersedes the standalone components — see [communication-service-architecture.md](communication-service-architecture.md) for the current architecture. This file is retained for historical context on idempotency and hybrid trigger decisions.

---

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Hybrid triggers (webhook + polling backup) | Webhooks are primary (real-time); 5-minute polling catches emails missed during deployments or BFF downtime |
| Idempotency via Redis locks | Guarantees exactly-once processing; prevents duplicate `.eml` files when both trigger paths fire simultaneously |
| RFC 2822 `.eml` format | Industry standard; supported by email clients and AI Document Intelligence pipeline |
| Auto-enqueue for RAG indexing | Email content becomes searchable knowledge without manual action |
| App-only auth (no OBO) | Webhooks and background jobs have no user context; see [sdap-auth-patterns.md](sdap-auth-patterns.md) Pattern 6 |
| Child document excludes `sprk_email` field | The email entity is already the parent via alternate key; setting `sprk_email` on the child would violate the alternate key constraint |
| Attachment filtering | Blocked extensions (`.exe`, `.js`, etc.) and signature images (≤5KB, common image extensions) are excluded to reduce storage noise |
| AI analysis via AppOnlyAnalysisService | `AnalysisOrchestrationService` requires OBO via `HttpContext`; background jobs have no user context, so a separate app-only analysis path is required |

---

## Hybrid Trigger Design

**Webhook (primary)**: Dataverse Service Endpoint (WebHook type, HMAC-SHA256 auth) fires on `email.Create` (async post-operation). The BFF validates the HMAC signature and enqueues a `ProcessEmailToDocument` job.

**Polling backup**: `EmailPollingBackupService` (BackgroundService with PeriodicTimer) queries emails created in the last N hours without a corresponding document. Default interval: 5 minutes. Catches emails missed during BFF downtime or failed webhook deliveries.

Both paths submit to Service Bus with idempotency key `Email:{emailId}:Archive`, preventing duplicate processing.

---

## Idempotency Design

Processing uses two Redis keys:
- **Processing lock** (5-minute TTL): Prevents concurrent duplicate processing of the same email.
- **Processed event marker** (7-day retention): Prevents re-processing after success.

If the idempotency key already exists, the job handler returns success immediately without reprocessing.

---

## Document Relationship Model

- **Parent document**: Created from the `.eml` file in SPE. Has `sprk_email` set to the source email entity.
- **Child documents**: Created for each allowed attachment. The `sprk_parentdocument` field links to the parent. `sprk_email` is intentionally NOT set on children — the parent already owns the relationship.

This design avoids the alternate key constraint violation that would occur if both parent and child set `sprk_email` to the same email entity.

---

## Authentication: OBO vs App-Only

Email processing uses **app-only authentication** because webhooks and background jobs have no user context.

| Component | Auth Type | Why |
|-----------|-----------|-----|
| `EmailToDocumentJobHandler` | App-Only | No user context in job handlers |
| `SpeFileStore.UploadSmallAsync` | App-Only | No HttpContext available |
| `AnalysisOrchestrationService` | OBO | AI analysis needs user's SPE permissions |
| `AppOnlyAnalysisService` | App-Only | Separate service for background AI analysis without user context |

---

## RAG Integration

Email documents are automatically indexed after archival:
- `AutoEnqueueAi` flag (default: `true`) controls whether the RAG indexing job is enqueued.
- Index: `spaarke-knowledge-index-v2` (3072-dim vectors, `text-embedding-3-large`).
- Multi-tenant isolation via `tenantId` filter at search time.

---

## Key Operational Notes

- `DefaultContainerId` **must be Drive ID format** (`b!xxx`), not a raw GUID. Raw GUIDs are rejected by the Graph API.
- `sprk_filesize` is a Dataverse "Whole Number" (int32) — cast `(int)` when setting; passing `long` causes a type mismatch.
- `sprk_mimetype` is the correct field name (not `sprk_filetype`).
- Dataverse webhooks send dates in WCF format (`/Date(1234567890000)/`) — use `NullableWcfDateTimeConverter`.
- `FilePath` must be set to `fileHandle.WebUrl` (the SPE file URL), not a relative path.

---

## Related Documentation

| Document | Purpose |
|----------|---------|
| [communication-service-architecture.md](communication-service-architecture.md) | R2 superseding architecture |
| [sdap-auth-patterns.md](sdap-auth-patterns.md) | Auth patterns including app-only (Pattern 6) |
| [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md) | BFF API patterns including job handlers |
| [auth-azure-resources.md](auth-azure-resources.md) | Azure config including DefaultContainerId |

---

*Last Updated: January 2026*
