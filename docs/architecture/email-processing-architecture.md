# Email Processing Architecture

> **Last Updated**: April 5, 2026
> **Purpose**: Consolidated architecture documentation for the email-to-document pipeline — hybrid webhook/polling triggers, idempotency design, RFC 2822 .eml archival, attachment processing, RAG indexing, and document relationship model. Merges the R1 email-to-document-architecture.md and email-to-document-automation.md into a single reference.

---

## Overview

The email processing pipeline converts Dataverse email activities into SPE-hosted documents with automatic RAG indexing. It operates as the foundation layer that the Communication Service (R2) builds upon for inbound email handling. The pipeline uses a **hybrid trigger design** (Dataverse webhook primary + periodic polling backup) with multi-layer idempotency to guarantee exactly-once processing even when both trigger paths fire simultaneously.

The R2 Communication Service supersedes the standalone email processing components for inbound Graph-based email handling. This document covers the shared patterns (idempotency, .eml archival, attachment processing, RAG indexing) that remain in use across both R1 and R2 paths.

---

## Component Structure

| Component | Path | Responsibility |
|-----------|------|---------------|
| EmailToDocumentJobHandler | `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/EmailToDocumentJobHandler.cs` | Orchestrates email-to-document conversion for a single email |
| EmailPollingBackupService | `src/server/api/Sprk.Bff.Api/Services/Email/EmailPollingBackupService.cs` | BackgroundService (5-min cycle): catches emails missed by webhooks |
| IEmailAttachmentProcessor | `src/server/api/Sprk.Bff.Api/Services/Email/IEmailAttachmentProcessor.cs` | Attachment filtering and processing interface |
| GraphMessageToEmlConverter | `src/server/api/Sprk.Bff.Api/Services/Communication/GraphMessageToEmlConverter.cs` | Converts Graph Message objects to RFC 2822 .eml format |
| EmlGenerationService | `src/server/api/Sprk.Bff.Api/Services/Communication/EmlGenerationService.cs` | Generates .eml files for SPE archival |
| AppOnlyAnalysisService | `src/server/api/Sprk.Bff.Api/Services/Ai/AppOnlyAnalysisService.cs` | App-only AI analysis path for background jobs without user context |

---

## Data Flow

### Hybrid Trigger Design

Two paths ensure no email is missed:

**Webhook (primary)**: Dataverse Service Endpoint (WebHook type, HMAC-SHA256 auth) fires on `email.Create` (async post-operation). The BFF validates the HMAC signature and enqueues a `ProcessEmailToDocument` job. Delivers near-real-time processing.

**Polling backup**: `EmailPollingBackupService` (BackgroundService with `PeriodicTimer`) queries emails created in the last N hours without a corresponding document. Default interval: 5 minutes. Catches emails missed during BFF downtime or failed webhook deliveries.

Both paths submit to Service Bus with idempotency key `Email:{emailId}:Archive`, preventing duplicate processing when both triggers fire for the same email.

### Processing Pipeline

1. **Idempotency check**: Redis processing lock (5-min TTL) prevents concurrent processing; processed event marker (7-day retention) prevents re-processing after success
2. **Fetch email data**: Retrieve email entity and related data from Dataverse
3. **Generate .eml**: Convert to RFC 2822 format using `EmlGenerationService`
4. **Upload to SPE**: Create parent document in SharePoint Embedded via `SpeFileStore.UploadSmallAsync`
5. **Create Dataverse document record**: Link parent `sprk_document` to source `sprk_email` entity
6. **Process attachments**: Filter and upload each allowed attachment as child document
7. **Archive .eml to SPE**: Store the .eml file alongside attachments
8. **Auto-enqueue AI analysis**: If `AutoEnqueueAi = true`, enqueue RAG indexing job
9. **Feature-flagged classification**: Optionally trigger finance invoice classification (Finance Intelligence integration)

---

## Idempotency Design

Processing uses two Redis keys for exactly-once semantics:

| Key | TTL | Purpose |
|-----|-----|---------|
| Processing lock | 5 minutes | Prevents concurrent duplicate processing of the same email (e.g., webhook and poll firing simultaneously) |
| Processed event marker | 7 days | Prevents re-processing after successful completion |

If the idempotency key already exists, the job handler returns success immediately without reprocessing. This design handles the common case where both the webhook and the polling backup detect the same new email within the same window.

---

## Document Relationship Model

- **Parent document**: Created from the `.eml` file in SPE. Has `sprk_email` set to the source email entity.
- **Child documents**: Created for each allowed attachment. The `sprk_parentdocument` field links to the parent. `sprk_email` is intentionally **NOT** set on children.

This design avoids the alternate key constraint violation that would occur if both parent and child set `sprk_email` to the same email entity. The parent already owns the email relationship; children are linked through the parent.

---

## Attachment Filtering

Not all attachments are processed:

- **Blocked extensions**: `.exe`, `.js`, and other executable types are excluded
- **Signature images**: Attachments <=5KB with common image extensions (`.png`, `.jpg`, `.gif`) are excluded to reduce storage noise from email signatures
- **Remaining attachments**: Each is uploaded to SPE and linked as a child document

---

## Authentication: OBO vs App-Only

Email processing uses **app-only authentication** because webhooks and background jobs have no user context.

| Component | Auth Type | Why |
|-----------|-----------|-----|
| `EmailToDocumentJobHandler` | App-Only | No user context in job handlers |
| `SpeFileStore.UploadSmallAsync` | App-Only | No HttpContext available |
| `AnalysisOrchestrationService` | OBO | AI analysis needs user's SPE permissions |
| `AppOnlyAnalysisService` | App-Only | Separate service for background AI analysis without user context |

`AnalysisOrchestrationService` requires OBO via `HttpContext`, so a separate `AppOnlyAnalysisService` exists for background jobs that need AI analysis without a user session.

---

## RAG Integration

Email documents are automatically indexed after archival:

- **Auto-enqueue**: `AutoEnqueueAi` flag (default: `true`) controls whether the RAG indexing job is enqueued
- **Index**: `spaarke-knowledge-index-v2` (3072-dim vectors, `text-embedding-3-large`)
- **Multi-tenant isolation**: `tenantId` filter at search time ensures tenant data separation
- **Content**: Both the .eml body and attachment text are indexed as separate chunks

---

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Triggered by | Dataverse webhook | Service Endpoint (HMAC-SHA256) | `email.Create` async post-operation |
| Triggered by | Polling backup | `EmailPollingBackupService` | 5-min cycle, catches missed webhooks |
| Depends on | SPE/Documents | `SpeFileStore` | .eml and attachment upload |
| Depends on | Redis | `IDistributedCache` | Idempotency locks and markers |
| Depends on | Service Bus | `JobSubmissionService` | Job enqueue/dequeue |
| Consumed by | Finance Intelligence | Feature-flagged classification trigger | `AttachmentClassification` job |
| Consumed by | RAG/Knowledge | Auto-enqueued indexing | Knowledge base enrichment |
| Superseded by | Communication Service (R2) | `IncomingCommunicationProcessor` | R2 handles Graph-based inbound directly |

---

## Known Pitfalls

1. **`DefaultContainerId` must be Drive ID format**: Must be `b!xxx` format, not a raw GUID. Raw GUIDs are rejected by the Graph API.

2. **`sprk_filesize` is int32**: Dataverse "Whole Number" field — cast `(int)` when setting. Passing `long` causes a type mismatch error.

3. **`sprk_mimetype` is the correct field name**: Not `sprk_filetype`. Using the wrong field name silently fails to set the MIME type.

4. **Dataverse webhooks use WCF date format**: Dates arrive as `/Date(1234567890000)/`. Must use `NullableWcfDateTimeConverter` for deserialization.

5. **`FilePath` must use `fileHandle.WebUrl`**: Set to the SPE file URL, not a relative path. Relative paths break document navigation in the UI.

6. **Child document must NOT set `sprk_email`**: The email entity is already the parent via alternate key. Setting `sprk_email` on the child would violate the alternate key constraint.

7. **Polling backup window**: The polling service queries emails created in the "last N hours" — if the window is too short, it may miss emails during extended BFF outages. If too long, it wastes queries on already-processed emails (though idempotency handles this safely).

8. **OBO vs app-only for AI analysis**: `AnalysisOrchestrationService` requires OBO via `HttpContext`, which is unavailable in background jobs. Always use `AppOnlyAnalysisService` for email-triggered AI analysis.

---

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Hybrid triggers | Webhook + polling backup | Near-real-time with fault tolerance during BFF downtime | ADR-001 |
| Idempotency via Redis | Processing lock + processed marker | Exactly-once semantics across concurrent trigger paths | — |
| RFC 2822 .eml format | Industry standard | Compatible with email clients and AI Document Intelligence pipeline | — |
| Auto-enqueue RAG indexing | Default enabled | Email content becomes searchable knowledge without manual action | — |
| App-only auth | `GraphClientFactory.ForApp()` | No user context in webhooks/background jobs | ADR-008 |
| Child document excludes `sprk_email` | Avoid alternate key violation | Parent owns the email relationship | — |
| Attachment filtering | Blocked extensions + signature image filter | Reduces storage noise from executables and email signatures | — |

---

## Constraints

- **MUST**: Use `SpeFileStore` facade for all SPE operations (ADR-007)
- **MUST**: Submit all jobs with idempotency key `Email:{emailId}:Archive`
- **MUST**: Use app-only auth for all background job processing
- **MUST NOT**: Set `sprk_email` on child document records (alternate key constraint)
- **MUST NOT**: Pass `long` values to `sprk_filesize` (use `(int)` cast)
- **MUST NOT**: Use raw GUIDs for `DefaultContainerId` (must be Drive ID `b!xxx` format)

---

## Related

- [communication-service-architecture.md](communication-service-architecture.md) — R2 Communication Service that supersedes standalone email processing for inbound
- [sdap-auth-patterns.md](sdap-auth-patterns.md) — Auth patterns including app-only (Pattern 6)
- [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md) — BFF API patterns including job handlers
- [auth-azure-resources.md](auth-azure-resources.md) — Azure config including DefaultContainerId
- [finance-intelligence-architecture.md](finance-intelligence-architecture.md) — Invoice classification pipeline triggered by email processing

---

*Last Updated: April 5, 2026*
