# Email-to-Document Automation Architecture

> **Version**: 1.2
> **Date**: January 17, 2026
> **Status**: Core Implementation Complete, RAG Indexing Active

> **Note**: This file covers the original email-to-document pipeline (Phase 2 + RAG). The R2 Communication Service supersedes the standalone email processing components — see [communication-service-architecture.md](communication-service-architecture.md) for the current architecture. This file is retained for historical context on the idempotency and hybrid trigger decisions.

---

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Hybrid triggers (webhook + polling backup) | Webhooks are primary (real-time); 5-minute polling catches emails missed during deployments |
| Idempotency via Redis locks | Guaranteed exactly-once processing; prevents duplicate `.eml` files when both trigger paths fire |
| RFC 2822 `.eml` format | Industry standard; supported by email clients and AI Document Intelligence pipeline |
| Auto-enqueue for RAG indexing | Email content becomes searchable knowledge without manual action |
| App-only auth (no OBO) | Webhooks and background jobs have no user context; see [sdap-auth-patterns.md](sdap-auth-patterns.md) Pattern 6 |

---

## Hybrid Trigger Design

**Webhook (primary)**: Dataverse Service Endpoint (WebHook type, HMAC-SHA256 auth) fires on `email.Create` (async post-operation). The BFF validates the HMAC signature and enqueues a job.

**Polling backup**: `EmailPollingBackupService` (BackgroundService with PeriodicTimer) queries emails created in the last N hours without a corresponding document. Default interval: 5 minutes. Catches emails missed during BFF downtime or failed webhook deliveries.

Both paths submit a `ProcessEmailToDocument` job to Service Bus with idempotency key `Email:{emailId}:Archive`, preventing duplicate processing.

---

## Idempotency Design

Processing uses two Redis keys:
- **Processing lock** (5-minute TTL): Prevents concurrent duplicate processing of the same email.
- **Processed event marker** (7-day retention): Prevents re-processing after success.

If the idempotency key already exists, the job handler returns success immediately without reprocessing.

---

## Authentication: OBO vs App-Only

Email processing uses **app-only authentication** because webhooks and background jobs have no user context.

| Component | Auth Type | Why |
|-----------|-----------|-----|
| `EmailToDocumentJobHandler` | App-Only | No user context in job handlers |
| `SpeFileStore.UploadSmallAsync` | App-Only | No HttpContext available |
| `AnalysisOrchestrationService` | OBO | AI analysis needs user's SPE permissions |

**Important**: AI analysis of email documents requires a separate **AppOnlyAnalysisService** because `AnalysisOrchestrationService` requires OBO authentication via `HttpContext`. See `Future: AI Analysis Architecture` section in the full design doc.

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

---

## Related Documentation

| Document | Purpose |
|----------|---------|
| [communication-service-architecture.md](communication-service-architecture.md) | R2 superseding architecture |
| [sdap-auth-patterns.md](sdap-auth-patterns.md) | Auth patterns including app-only (Pattern 6) |
| [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md) | BFF API patterns including job handlers |
| [auth-azure-resources.md](auth-azure-resources.md) | Azure config including DefaultContainerId |

---

*Last Updated: January 17, 2026*
