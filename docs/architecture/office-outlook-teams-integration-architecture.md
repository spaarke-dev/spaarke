# Office Add-ins Integration Architecture

> **Last Updated**: April 5, 2026
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Current
> **Purpose**: Architecture of the SDAP Office Add-ins for Outlook and Word integration with SharePoint Embedded

---

## Overview

The SDAP Office Add-ins provide integration between Microsoft Office applications (Outlook and Word) and the Spaarke Document Access Platform (SDAP). Users can save emails, attachments, and documents directly to SharePoint Embedded containers with AI-powered metadata extraction via the Document Profile playbook.

The add-ins are React 18 task pane applications hosted on Azure Static Web Apps. They authenticate via MSAL.js using the Dialog API (not NAA), call the BFF API for all backend operations, and use a multi-stage Service Bus pipeline for asynchronous file processing, AI profiling, and search indexing. A Host Adapter pattern (`IHostAdapter`) abstracts differences between Outlook and Word, making UI components portable across both hosts.

### Key Capabilities

- **Email Artifact Capture**: Save emails with full metadata (sender, recipients, dates, subjects)
- **Attachment Processing**: Extract and process email attachments with AI analysis
- **Document Integration**: Save Word documents with version tracking
- **AI-Powered Metadata**: Automatic extraction of topics, entities, and summaries via Document Profile playbook
- **Unified Experience**: Consistent UI across Outlook and Word using Fluent UI v9

---

## Component Structure

### Add-in Source Layout

| Component | Path | Responsibility |
|-----------|------|---------------|
| Outlook adapter | `src/client/office-addins/outlook/OutlookHostAdapter.ts` | Email/attachment access via `Office.context.mailbox` |
| Outlook manifest | `src/client/office-addins/outlook/outlook-manifest.xml` | XML manifest for M365 Admin Center deployment |
| Word adapter | `src/client/office-addins/word/WordHostAdapter.ts` | Document access via `Office.context.document` |
| Word manifest | `src/client/office-addins/word/word-manifest.xml` | XML manifest for M365 Admin Center deployment |
| Shared task pane | `src/client/office-addins/shared/taskpane/` | React 18 UI components (SavePanel, FolderPicker, StatusDisplay) |
| Dialog auth service | `src/client/office-addins/shared/auth/DialogAuthService.ts` | Dialog API authentication (popup → MSAL → messageParent) |
| NAA auth service | `src/client/office-addins/shared/auth/NaaAuthService.ts` | Future NAA authentication (disabled in V1) |
| Auth config | `src/client/office-addins/shared/auth/authConfig.ts` | Client ID, tenant ID, BFF scope configuration |
| API client | `src/client/office-addins/shared/services/ApiClient.ts` | BFF API client with Bearer token |
| Auth service | `src/client/office-addins/shared/services/AuthService.ts` | Token acquisition orchestrator (Dialog API primary) |

### Architecture Diagram

```
+-----------------------------------------------------------------------------+
|  Office Add-in Runtime (Task Pane -- React 18)                              |
|  +----------------+  Host Adapter Layer  +----------------+                 |
|  | OutlookAdapter |  (IHostAdapter)      |  WordAdapter   |                 |
|  +----------------+                      +----------------+                 |
|  AuthService: Dialog API -> MSAL.js 3.x -> token cache (memory only)       |
+-----------------------------------+-----------------------------------------+
                                    | HTTPS (Bearer Token)
+-----------------------------------v-----------------------------------------+
|  BFF API (spe-api-dev-67e2xz.azurewebsites.net)                            |
|  POST /api/office/emails | POST /api/office/attachments | POST /office/docs |
|  Token Exchange: User Token -> BFF Token -> Microsoft Graph Token (OBO)     |
+------------------+------------------------+---------------------------------+
                   |                        |
          SharePoint Embedded          Azure AI Services
          + Dataverse (CRM)            (OpenAI, Doc Intel, AI Search)
```

---

## Data Flow: Email Save

1. User clicks "Save to Spaarke" button in Outlook task pane
2. `OutlookAdapter.getCurrentItem()` retrieves email metadata (sender, recipients, subject, dates)
3. `OutlookAdapter.getAttachments()` retrieves attachment list
4. `AuthService.getAccessToken()` obtains BFF token via Dialog API (cached with 5-minute expiry buffer)
5. `POST /api/office/emails` — BFF receives email metadata + attachment content
6. BFF uploads email body to SPE container, creates `sprk_emailartifact` in Dataverse
7. BFF queues `office-upload-finalization` job to Service Bus — returns job ID
8. `UploadFinalizationWorker` processes: moves temp files to SPE, creates Dataverse records, queues next stages
9. `ProfileSummaryWorker` generates AI document profile via "Document Profile" playbook
10. `IndexingWorkerHostedService` indexes document content in Azure AI Search for RAG
11. Task pane polls `GET /api/office/process/{id}/status` via SSE for progress updates

---

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Auth method | Dialog API (not NAA) | NAA requires dynamic broker URIs (`brk-{GUID}://`) that cannot be pre-registered; not yet GA | — |
| Manifest format | XML (not Unified Manifest) | Unified Manifest requires NAA; XML is production-proven with M365 Admin Center | — |
| Processing model | Service Bus pipeline (not synchronous) | Email/doc processing is time-intensive; parallel stages with retry/dead-letter semantics | — |
| Error categorization | Core vs Enhancement | Core (upload, record creation) must succeed; Enhancement (AI, indexing) failures are warnings | — |
| Host abstraction | `IHostAdapter` interface | Components portable across Outlook and Word without modification | — |
| Token storage | Memory only (not localStorage/sessionStorage) | Office add-in task pane lifecycle is transient; memory cache with 5-minute expiry buffer | — |
| UI framework | Fluent UI v9 with dark mode | Consistent with platform design system per ADR-021 | ADR-021 |

### Why Dialog API (Not NAA) for Authentication

NAA (Nested App Authentication) provides seamless authentication without popups but requires Azure AD configuration with dynamic broker URIs (`brk-{GUID}://`) that are generated per Office session and cannot be pre-registered. This makes NAA impractical for current deployments.

**Dialog API** is the production authentication method: opens a popup for MSAL.js redirect flow, returns the token via `Office.context.ui.messageParent()`, and caches it in memory with a 5-minute expiry buffer. Works universally across all Office hosts and versions.

**Future**: When Microsoft provides a stable `brk-multihub://` URI for registration, NAA can be enabled for seamless authentication. The `NaaAuthService.ts` file already exists as a placeholder.

### Why Graceful Degradation for AI and Indexing

Core operations (file upload, Dataverse record creation) must succeed or the job fails. Enhancement operations (AI profile extraction, RAG indexing) are optional — their failure means the document is saved without AI fields or search indexing, but the document itself is accessible. This ensures the core save workflow is reliable even if Azure OpenAI or AI Search are temporarily unavailable.

---

## Manifest Requirements

> **CRITICAL**: These requirements were validated through production testing. Non-compliance causes M365 Admin Center validation failures.

### Common Requirements (All Add-ins)

| Element | Requirement |
|---------|-------------|
| **Version** | Must be 4-part format: `X.X.X.X` (not `X.X.X`) |
| **Icon URLs** | Must return HTTP 200 — all icon sizes must be accessible |
| **DefaultLocale** | Required (`en-US`) |
| **AppDomains** | Required — list all domains the add-in uses |

### Outlook Add-in Critical Rules

| Rule | Reason |
|------|--------|
| **NO FunctionFile** | Causes validation failures in M365 Admin Center |
| **Single VersionOverrides V1.0** | Do NOT nest V1.1 inside V1.0 |
| **RuleCollection Mode="Or"** | Use collection, not single Rule |
| **DisableEntityHighlighting** | Must be present in manifest |
| **FormType="Read"** for MessageReadCommandSurface | Match extension point to form type |

### Manifest Format Strategy

| Format | Status | Use When |
|--------|--------|---------|
| **XML Manifest** (V1, current) | Production GA | Now — works with Dialog API, full Admin Center support |
| **Unified Manifest** (V2, future) | Preview — NAA not GA | When NAA is GA with stable broker URI format |

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

**Job status tracked** in `sprk_processingjob` via SSE polling (`GET /api/office/process/{id}/status`).

**Queue configuration**: `MaxConcurrentCalls=5`, `AutoCompleteMessages=false`, `MaxAutoLockRenewalDuration=10 minutes`, dead-letter enabled.

**Idempotency**: Each processing stage is guarded by a Redis cache key with 7-day TTL to prevent reprocessing.

---

## Authentication

### App Registration

| Property | Value |
|----------|-------|
| Application (Client) ID | `c1258e2d-1688-49d2-ac99-a7485ebd9995` |
| Directory (Tenant) ID | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| BFF API App ID | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |

> **CRITICAL**: The Office Add-in client ID MUST be registered as an authorized client application in the BFF API's "Expose an API" configuration (Azure Portal -> App registrations -> SDAP-BFF-SPE-API -> Expose an API -> Authorized client applications). Without this, the add-in receives 401 errors.

### API Permissions Required

| API | Permission | Purpose |
|-----|------------|---------|
| Microsoft Graph | `User.Read`, `Mail.Read`, `Files.ReadWrite.All` | Profile, email, SPE file access |
| BFF API | `access_as_user` | API access |

### Token Lifecycle

Tokens are cached in memory with expiration tracking. A 5-minute buffer ensures tokens are refreshed before expiry. Re-authentication triggers automatically when the token expires. Same browser session = silent auth (no login prompts after first Dialog API flow).

---

## Constraints

- **MUST** use Dialog API for authentication — NAA is not yet GA
- **MUST** use XML manifests — Unified Manifest requires NAA
- **MUST** use 4-part version format (`X.X.X.X`) in manifests
- **MUST NOT** include `FunctionFile` element in Outlook manifest
- **MUST NOT** nest VersionOverrides V1.1 inside V1.0
- **MUST** include `DisableEntityHighlighting` in Outlook manifest
- **MUST** treat AI profiling and search indexing as non-fatal enhancements
- **MUST** register add-in client ID as authorized client on BFF API app registration

---

## Known Pitfalls

| Pitfall | Symptom | Resolution |
|---------|---------|------------|
| FunctionFile in Outlook manifest | M365 Admin Center rejects the manifest | Remove all `<FunctionFile>` elements from the Outlook XML manifest |
| Nested VersionOverrides | Validation failure on upload | Use a single `VersionOverridesV1_0` block — do not nest V1.1 inside V1.0 |
| 3-part version number | Admin Center rejects manifest | Use `X.X.X.X` format (4 parts), not `X.X.X` |
| NAA broker URI not registered | `acquireTokenSilent` fails with interaction_required | Fall back to Dialog API; NAA requires pre-registered `brk-{GUID}://` URIs which vary per session |
| 401 from BFF API | Add-in token rejected by BFF | Ensure add-in client ID is listed in BFF API's "Authorized client applications" in Azure Portal |
| Icon URLs return 404 | Manifest fails validation | Verify all icon URLs (`32x32`, `64x64`, `80x80`) return HTTP 200 from hosting domain |
| Dialog popup blocked | Auth flow never completes | Office Desktop clients may block popups; user must allow popups for the add-in domain |
| AI service unavailable | Document saved without AI fields | By design — enhancement failures are logged as warnings, core save completes successfully |

---

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Depends on | BFF API | `/api/office/*` endpoints | Email, attachment, document save operations |
| Depends on | Office.js API | `Office.context.mailbox`, `Office.context.document` | Host-specific content access |
| Depends on | MSAL.js 3.x | Dialog API authentication | Token acquisition via popup flow |
| Depends on | Azure Static Web App | `spaarke-office-addins` | Add-in hosting (HTML/JS/CSS) |
| Depends on | Service Bus | Three processing queues | Async file processing pipeline |
| Depends on | Azure AI Services | OpenAI, Doc Intel, AI Search | Enhancement stage (non-fatal) |
| Consumed by | Outlook Desktop/Web | Task pane extension | Email and attachment capture |
| Consumed by | Word Desktop/Web | Task pane extension | Document save and versioning |

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

## Related

- [ADR-021](../../.claude/adr/ADR-021-fluent-design-system.md) — Fluent UI v9 requirements
- [sdap-auth-patterns.md](sdap-auth-patterns.md) — Authentication patterns
- [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md) — BFF API endpoint patterns
- [email-processing-architecture.md](email-processing-architecture.md) — Email-to-document conversion pipeline

---

*Last Updated: April 5, 2026*
