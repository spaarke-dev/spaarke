# AI Document Summary Architecture

> **Last Updated**: January 28, 2026
> **Purpose**: Document creation process flows for all entry points into the Spaarke document pipeline

> **PARTIALLY SUPERSEDED**: The AI analysis sections of this document are outdated.
> For current AI architecture, see [`AI-ARCHITECTURE.md`](AI-ARCHITECTURE.md) (v3.2+).
> The **document creation process flows** (upload, email, Outlook, Word) below remain accurate.

---

## Table of Contents

1. [File Upload via UniversalDocumentUpload PCF Control](#1-file-upload-via-universaldocumentupload-pcf-control)
2. [Email-to-Document Automation Process](#2-email-to-document-automation-process)
3. [Outlook Add-in Save Process](#3-outlook-add-in-save-process)
4. [Word Add-in Save Process](#4-word-add-in-save-process)
5. [Comparison Summary](#5-comparison-summary)

---

## 1. File Upload via UniversalDocumentUpload PCF Control

### Overview

User-initiated file upload through PCF control in Model-Driven Apps, Custom Pages, or Canvas Apps. Files are uploaded to SharePoint Embedded (SPE) and Document records created in Dataverse with optional AI analysis.

### Process Flow

```
┌──────────────────────────────────────────────────────────────┐
│ USER ACTION: Select Files → Enter Metadata → Click Upload   │
└────────────────────────┬─────────────────────────────────────┘
                         │
                         ▼
        ┌────────────────────────────────────────┐
        │  PHASE 1: FILE UPLOAD TO SPE           │
        │  PUT /api/containers/{id}/files/{*path} │
        │  Result: Graph DriveItem               │
        │          (id, webUrl, size)             │
        └────────────────┬───────────────────────┘
                         │ Returns: driveId, itemId, fileName
                         ▼
        ┌────────────────────────────────────────┐
        │  PHASE 2: CREATE DOCUMENT RECORD       │
        │  Entity: sprk_document                 │
        │  Fields: GraphDriveId, GraphItemId,    │
        │          FileName, FileSize, etc.       │
        │  Result: Document GUID                 │
        └────────────────┬───────────────────────┘
                         │ documentId
            ┌────────────┴────────────┐
            │                         │
            ▼                         ▼
    ┌─────────────────┐      ┌─────────────────┐
    │  PHASE 3: AI    │      │  PHASE 4: RAG   │
    │  SUMMARY        │      │  INDEXING       │
    │  (Optional)     │      │  (Optional)     │
    │                 │      │                 │
    │ Concurrent: 3   │      │ Fire-and-forget │
    │ files max       │      │                 │
    │                 │      │ Non-blocking:   │
    │ Non-blocking:   │      │   Failures don't│
    │   Failures don't│      │   fail upload   │
    │   fail upload   │      │                 │
    └─────────────────┘      └─────────────────┘
```

### Key Design Decisions

- File upload, Document creation, AI summary, and RAG indexing are independent phases
- AI summary runs concurrently (max 3 files) — failure does not fail the upload
- RAG indexing is fire-and-forget — failure does not fail the upload
- File limits: max 10 files, 10MB each, 100MB total

---

## 2. Email-to-Document Automation Process

### Overview

Automated conversion of Dataverse email activities to Document records with .eml files stored in SPE. Triggered by webhook on email creation, processes email + attachments, and queues AI analysis via Service Bus workers.

### Process Flow

```
┌────────────────────────────────────────────────────────┐
│  EMAIL ARRIVAL IN DATAVERSE                            │
│  (User creates email activity OR webhook from Outlook) │
└──────────────────────────┬─────────────────────────────┘
                           │
            ┌──────────────┴──────────────┐
            │                             │
            ▼                             ▼
    ┌───────────────┐          ┌────────────────────┐
    │  PRIMARY:     │          │  BACKUP:           │
    │  WEBHOOK      │          │  POLLING SERVICE   │
    │  TRIGGER      │          │  (5-min intervals) │
    │  POST /api/v1/│          │                    │
    │  emails/      │          │ Queries unprocessed│
    │  webhook-     │          │ emails (24hr       │
    │  trigger      │          │ lookback)          │
    └───────┬───────┘          └────────┬───────────┘
            │                           │
            └──────────┬────────────────┘
                       │ Both enqueue to Service Bus
                       ▼
        ┌──────────────────────────────────────┐
        │  SERVICE BUS: sdap-jobs QUEUE        │
        │  JobType: "ProcessEmailToDocument"   │
        │  IdempotencyKey: "Email:{id}:Archive"│
        └──────────────┬───────────────────────┘
                       │
                       ▼
        ┌──────────────────────────────────────┐
        │  JOB HANDLER:                        │
        │  EmailToDocumentJobHandler           │
        │                                      │
        │ 1. Check Idempotency (Redis)         │
        │ 2. Acquire Processing Lock           │
        │ 3. Circuit Breaker: Mark InProgress  │
        │ 4. Convert Email to .eml (MimeKit)   │
        │ 5. Upload .eml to SPE                │
        │ 6. Create Document Record            │
        │ 7. Update with Email Metadata        │
        │ 8. Process Attachments               │
        │    → Filter (signatures, etc.)       │
        │    → Upload each to SPE (subpath)    │
        │    → Create child Document records   │
        │ 9. Enqueue AI Analysis (if enabled)  │
        │    → AppOnlyDocumentAnalysis job     │
        │    → Playbook: "Document Profile"    │
        │10. Enqueue RAG Indexing (if enabled) │
        │11. Mark Processed (Redis, 7d TTL)    │
        │12. Circuit Breaker: Mark Completed   │
        └──────────────┬───────────────────────┘
                       │
            ┌──────────┴──────────┐
            │                     │
            ▼                     ▼
    ┌──────────────┐      ┌─────────────────┐
    │  AI ANALYSIS │      │  RAG INDEXING   │
    │  (Parallel)  │      │  (Parallel)     │
    └──────────────┘      └─────────────────┘
```

### Key Design Decisions

- Dual trigger pattern (webhook primary, polling backup) ensures no emails are missed
- Service Bus provides durability and decoupling from the trigger
- Idempotency via Redis (7-day TTL) prevents duplicate processing from both trigger sources
- Circuit breaker pattern on `sprk_documentprocessingstatus` prevents concurrent processing of the same email
- Attachments are filtered (blocked extensions, signature images, small images) before creating child Documents
- AI analysis and RAG indexing run in parallel after Document creation

---

## 3. Outlook Add-in Save Process

### Overview

User-initiated save from Outlook add-in taskpane. Uploads email/attachment to SPE, creates Document + artifact records, processes attachments, and queues AI analysis via Service Bus workers. Returns 202 immediately; background workers handle the rest.

### Process Flow

```
┌───────────────────────────────────────────────────────┐
│  USER ACTION: Click "Save to Spaarke" in Taskpane    │
│  (Email or Attachment selected, Target Entity chosen) │
└──────────────────────────┬────────────────────────────┘
                           │
                           ▼
        ┌──────────────────────────────────────────┐
        │  CLIENT: SaveFlow → POST /office/save    │
        │  (SHA-256 idempotency key computed)      │
        └──────────────┬───────────────────────────┘
                       │ Authorization: Bearer {token}
                       ▼
        ┌──────────────────────────────────────────┐
        │  API: POST /office/save                  │
        │                                          │
        │ 1. Idempotency Check                     │
        │    → If duplicate: Return 200 with flag  │
        │ 2. Create ProcessingJob Record           │
        │    → Status: Queued                      │
        │ 3. Enrich Email from Graph (OBO)         │
        │ 4. Build .eml or Attachment Stream       │
        │ 5. Upload to SPE                         │
        │ 6. Create Document Record                │
        │ 7. Queue UploadFinalization Job          │
        │    → Queue: office-upload-finalization   │
        │    → TriggerAiProcessing: true/false     │
        │ 8. Return 202 Accepted                   │
        │    → { jobId, statusUrl, streamUrl }     │
        └──────────────┬───────────────────────────┘
                       │
                       ▼
        ┌──────────────────────────────────────────┐
        │  SERVICE BUS:                            │
        │  office-upload-finalization QUEUE        │
        └──────────────┬───────────────────────────┘
                       │
                       ▼
        ┌──────────────────────────────────────────┐
        │  BACKGROUND WORKER:                      │
        │  UploadFinalizationWorker                │
        │  (BackgroundService, ADR-001)            │
        │                                          │
        │ 1. Check Idempotency (Redis, 7d TTL)     │
        │ 2. Create Artifact Records               │
        │    → Email: EmailArtifact entity         │
        │    → Attachment: AttachmentArtifact      │
        │ 3. Process Email Attachments             │
        │    → Download .eml from SPE              │
        │    → Extract attachments (MimeKit)       │
        │    → Filter (signatures, etc.)           │
        │    → Respect user's attachment selection │
        │    → Upload each to SPE                  │
        │    → Create child Document records       │
        │ 4. Queue AI Processing (if enabled)      │
        │    → AppOnlyDocumentAnalysis to sdap-jobs│
        │ 5. Mark Job Complete                     │
        └──────────────┬───────────────────────────┘
                       │
                       ▼
        ┌──────────────────────────────────────────┐
        │  JOB HANDLER:                            │
        │  AppOnlyDocumentAnalysisJobHandler       │
        │  (Same pipeline as all other processes)  │
        └──────────────────────────────────────────┘
```

### Key Design Decisions

- Two-stage async pattern: API returns 202 immediately; worker handles all heavy work
- Two Service Bus queues: `office-upload-finalization` (artifact/attachment work) → `sdap-jobs` (AI analysis)
- Client polls `GET /office/jobs/{jobId}` or subscribes to SSE stream for progress
- EmailArtifact + AttachmentArtifact records are created only for Outlook add-in (not email-to-document automation)
- User's attachment selection is honored — only selected attachments become child Documents

---

## 4. Word Add-in Save Process

### Overview

The Word add-in save process **shares the same architecture** as the Outlook add-in with minor differences in content type and metadata.

### Shared Architecture

All components are shared with the Outlook add-in: SaveFlow component, useSaveFlow hook, `POST /office/save` endpoint, OfficeService, UploadFinalizationWorker, Service Bus queues, and AppOnlyDocumentAnalysisJobHandler.

### Differences from Outlook Add-in

- `contentType: "Document"` instead of "Email" or "Attachment"
- `sprk_documenttype: 100000000` (Word document) instead of email type codes
- No EmailArtifact or AttachmentArtifact records created
- No attachment extraction (Word documents don't have child attachments)
- Same AI analysis pipeline applies (Document Profile playbook)

---

## 5. Comparison Summary

| Process | Trigger | Artifact Records | Attachment Processing | AI Analysis Queue |
|---------|---------|-----------------|----------------------|-------------------|
| **File Upload (PCF)** | User click | None | N/A | Optional (fire-and-forget) |
| **Email-to-Document** | Webhook/Polling | None | Yes (child Documents) | Queued to sdap-jobs |
| **Outlook Add-in** | User click | EmailArtifact/AttachmentArtifact | Yes (child Documents) | Queued to sdap-jobs |
| **Word Add-in** | User click | None | No | Queued to sdap-jobs |

### Service Bus Queues

| Queue | Used By | Purpose |
|-------|---------|---------|
| `sdap-jobs` | All processes | Main job queue for AI analysis |
| `office-upload-finalization` | Outlook/Word add-in only | Finalize upload, create artifacts, process attachments |

### Common Infrastructure

All processes share:
- SharePoint Embedded (SPE) for file storage
- Dataverse `sprk_document` entity for metadata
- AppOnlyDocumentAnalysisJobHandler for AI analysis
- Azure OpenAI for AI analysis
- Azure AI Search for RAG indexing (optional)

### Idempotency Mechanisms

| Process | Mechanism | Storage |
|---------|-----------|---------|
| **File Upload (PCF)** | None (client-side only) | N/A |
| **Email-to-Document** | IdempotencyKey in Redis | `email:processed:{key}` (7d TTL) |
| **Outlook/Word Add-in** | IdempotencyKey in Dataverse + Redis | ProcessingJob entity + Redis (7d TTL) |

### Error Handling

| Process | Retry Logic | Dead Letter |
|---------|-------------|-------------|
| **File Upload (PCF)** | None | N/A |
| **Email-to-Document** | Exponential backoff (3 max) | Yes |
| **Outlook/Word Add-in** | Exponential backoff (3 max) | Yes |

---

*End of Document*
