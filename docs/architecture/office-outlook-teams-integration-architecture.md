# Office Add-ins Integration Architecture

> **Last Updated**: January 24, 2026
> **Status**: Production Ready
> **Project**: SDAP Office Integration

---

## Overview

The SDAP Office Add-ins provide integration between Microsoft Office applications (Outlook and Word) and the Spaarke Document Access Platform (SDAP). Users can save emails, attachments, and documents directly to SharePoint Embedded containers with AI-powered metadata extraction.

### Key Capabilities

- **Email Artifact Capture**: Save emails with full metadata (sender, recipients, dates, subjects)
- **Attachment Processing**: Extract and process email attachments with AI analysis
- **Document Integration**: Save Word documents with version tracking
- **AI-Powered Metadata**: Automatic extraction of topics, entities, and summaries via Document Profile playbook
- **Unified Experience**: Consistent UI across Outlook and Word using Fluent UI v9

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  Office Add-in Runtime (Task Pane — React 18)                               │
│  ┌──────────────┐  Host Adapter Layer  ┌──────────────┐                    │
│  │ OutlookAdapter│  (IHostAdapter)      │  WordAdapter  │                    │
│  └──────────────┘                       └──────────────┘                    │
│  AuthService: Dialog API → MSAL.js 3.x → token cache (memory only)          │
└────────────────────────────────┬────────────────────────────────────────────┘
                                 │ HTTPS (Bearer Token)
┌────────────────────────────────▼────────────────────────────────────────────┐
│  BFF API (spe-api-dev-67e2xz.azurewebsites.net)                             │
│  POST /api/office/emails | POST /api/office/attachments | POST /office/docs │
│  Token Exchange: User Token → BFF Token → Microsoft Graph Token (OBO)       │
└──────────────────────┬──────────────────────────┬───────────────────────────┘
                       │                          │
              SharePoint Embedded           Azure AI Services
              + Dataverse (CRM)             (OpenAI, Doc Intel, AI Search)
```

---

## Design Decisions

### Why Dialog API (Not NAA) for Authentication?

NAA (Nested App Authentication) provides seamless authentication without popups but requires Azure AD configuration with dynamic broker URIs (`brk-{GUID}://`) that are generated per Office session and cannot be pre-registered. This makes NAA impractical for current deployments.

**Dialog API** is the production authentication method: opens a popup for MSAL.js redirect flow, returns the token via `Office.context.ui.messageParent()`, and caches it in memory with a 5-minute expiry buffer. Works universally across all Office hosts and versions.

**Future**: When Microsoft provides a stable `brk-multihub://` URI for registration, NAA can be enabled for seamless authentication.

### Why XML Manifest (Not Unified Manifest)?

The Unified Manifest (JSON) is in preview as of January 2026 and requires NAA — which is not yet GA. XML manifests are production-proven, fully supported by M365 Admin Center, and work with Dialog API authentication. Migration to Unified Manifest will happen when NAA reaches GA and the broker URI format is stable.

### Why Service Bus Pipeline (Not Synchronous Processing)?

Email and document processing involves time-intensive operations (file uploads, AI analysis, search indexing) that must not block the user experience. A multi-stage Service Bus pipeline enables:
- Parallel execution of independent stages (AI profile and search indexing run concurrently)
- Retry/dead-letter semantics for transient failures
- Graceful degradation: AI profile and indexing failures log warnings but do not fail the job
- Idempotency: each stage is guarded by a Redis cache key with 7-day TTL

### Why Graceful Degradation for AI and Indexing?

Core operations (file upload, Dataverse record creation) must succeed or the job fails. Enhancement operations (AI profile extraction, RAG indexing) are optional — their failure means the document is saved without AI fields or search indexing, but the document itself is accessible. This ensures the core save workflow is reliable even if Azure OpenAI or AI Search are temporarily unavailable.

### Host Adapter Pattern

All UI components receive an `IHostAdapter` interface rather than calling `Office.context.mailbox` or `Office.context.document` directly. This makes components testable and portable across Outlook and Word without modification. `OutlookAdapter` and `WordAdapter` implement the interface.

---

## Manifest Requirements

> **CRITICAL**: These requirements were validated through production testing. Non-compliance causes M365 Admin Center validation failures.

### Common Requirements (All Add-ins)

| Element | Requirement |
|---------|-------------|
| **Version** | Must be 4-part format: `X.X.X.X` |
| **Icon URLs** | Must return HTTP 200 |
| **DefaultLocale** | Required (`en-US`) |
| **AppDomains** | Required — list all domains the add-in uses |

### Outlook Add-in Critical Rules

| Rule | Reason |
|------|--------|
| **NO FunctionFile** | Causes validation failures in M365 Admin Center |
| **Single VersionOverrides V1.0** | Do NOT nest V1.1 inside V1.0 |
| **RuleCollection Mode="Or"** | Use collection, not single Rule |
| **DisableEntityHighlighting** | Must be present |
| **FormType="Read"** for MessageReadCommandSurface | Match extension point to form type |

---

## Background Workers Pipeline

Three Service Bus queues process saves asynchronously:

| Queue | Worker | Purpose |
|-------|--------|---------|
| `office-upload-finalization` | `UploadFinalizationWorker` | Move temp files to SPE, create Dataverse records, link relationships, queue next stages |
| `office-profile` | `ProfileSummaryWorker` | Generate AI document profile via "Document Profile" playbook |
| `office-indexing` | `IndexingWorkerHostedService` | Index document content in Azure AI Search for RAG |

**Error categories**:
- **Core** (file upload, record creation): must succeed or job fails
- **Enhancement** (AI profile, indexing): failures logged as warnings, job completes without those fields

**Job status tracked in `sprk_processingjob`** via SSE polling (`GET /api/office/process/{id}/status`).

**Queue configuration**: MaxConcurrentCalls=5, AutoCompleteMessages=false, MaxAutoLockRenewalDuration=10 minutes, dead-letter enabled.

---

## Authentication

### App Registration

| Property | Value |
|----------|-------|
| Application (Client) ID | `c1258e2d-1688-49d2-ac99-a7485ebd9995` |
| Directory (Tenant) ID | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| BFF API App ID | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |

> **CRITICAL**: The Office Add-in client ID MUST be registered as an authorized client application in the BFF API's "Expose an API" configuration (Azure Portal → App registrations → SDAP-BFF-SPE-API → Expose an API → Authorized client applications). Without this, the add-in receives 401 errors.

### API Permissions Required

| API | Permission | Purpose |
|-----|------------|---------|
| Microsoft Graph | `User.Read`, `Mail.Read`, `Files.ReadWrite.All` | Profile, email, SPE file access |
| BFF API | `access_as_user` | API access |

---

## Azure Resources

| Service | Resource Name | Purpose |
|---------|---------------|---------|
| App Service | `spe-api-dev-67e2xz` | BFF API |
| Azure OpenAI | `spaarke-openai-dev` | Entity extraction, summarization |
| Document Intelligence | `spaarke-docintel-dev` | Document parsing, OCR |
| AI Search | `spaarke-search-dev` | Full-text search indexing |
| Static Web App | `spaarke-office-addins` | Add-in hosting (dev) |

---

## Manifest Format Strategy

| Format | Status | Use When |
|--------|--------|---------|
| **XML Manifest** (V1, current) | Production GA | Now — works with Dialog API, full Admin Center support |
| **Unified Manifest** (V2, future) | Preview — NAA not GA | When NAA is GA with stable broker URI, need combined Office+Teams deployment |

---

## Related Documentation

- [ADR-021](../../.claude/adr/ADR-021-fluent-ui-v9-design-system.md) - Fluent UI v9 requirements
- [Auth Standards](../standards/auth-standards.md) - Authentication patterns
- [Office Add-ins Admin Guide](../guides/office-addins-admin-guide.md) - Deployment and administration
- [Office Add-ins Deployment Checklist](../guides/office-addins-deployment-checklist.md) - Pre-deployment validation
