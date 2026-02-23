# Communication Service Architecture

> **Last Updated**: February 21, 2026
> **Purpose**: Architecture documentation for the Email Communication Service module — BFF API endpoints, Graph API integration, Dataverse tracking, SPE archival, and AI playbook integration.
> **Status**: Implemented (R1 Complete)
> **Branch**: `work/email-communication-solution-r1`

---

## Table of Contents

- [Overview](#overview)
- [Architecture Principles](#architecture-principles)
- [System Architecture](#system-architecture)
- [Component Inventory](#component-inventory)
- [Send Pipeline](#send-pipeline)
- [API Endpoints](#api-endpoints)
- [Approved Sender Resolution](#approved-sender-resolution)
- [Association Mapping](#association-mapping)
- [Attachment Handling](#attachment-handling)
- [EML Archival](#eml-archival)
- [Dataverse Entity Schema](#dataverse-entity-schema)
- [Configuration](#configuration)
- [Error Handling](#error-handling)
- [Security](#security)
- [UI Integration](#ui-integration)
- [AI Playbook Integration](#ai-playbook-integration)
- [ADR Compliance](#adr-compliance)

---

## Overview

The Communication Service replaces heavyweight Dataverse email activities with a lightweight BFF-mediated approach using Microsoft Graph API. Instead of creating Dataverse `email` activities (which require complex activity party resolution), the service sends emails via Graph `sendMail` and creates a simple `sprk_communication` tracking record.

### Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Graph API over Dataverse email activities | Avoids complex activity party resolution, faster send, no plugin overhead |
| Best-effort tracking | Email send is the critical path; Dataverse record, SPE archival, and attachment records are non-fatal |
| Approved sender model | Prevents arbitrary sender spoofing; allows shared mailbox sending |
| Per-endpoint authorization filter | Follows ADR-008; avoids global middleware |
| Singleton service registration | Follows ADR-010; stateless service with injected dependencies |

---

## Architecture Principles

1. **Graph Send is Critical Path**: If Graph `sendMail` fails, the entire operation fails with `SdapProblemException`. No partial success.
2. **Best-Effort Tracking**: Dataverse record creation, SPE archival, and attachment record creation are wrapped in try/catch. Failures are logged as warnings and returned as `ArchivalWarning` or `AttachmentRecordWarning` in the response.
3. **No Retry Logic**: Failures are immediate. Callers (UI or AI playbook) handle retry decisions.
4. **Sender Validation Before Send**: The approved sender list is validated synchronously before any Graph call.
5. **Correlation ID Tracing**: Every operation is tagged with a `correlationId` for end-to-end tracing through logs.

---

## System Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    Model-Driven App (UCI)                       │
│  ┌──────────────────────────┐  ┌─────────────────────────────┐ │
│  │  Communication Form      │  │  Send Button (Ribbon)       │ │
│  │  sprk_communication      │  │  sprk_communication_send.js │ │
│  └──────────────────────────┘  └──────────┬──────────────────┘ │
└───────────────────────────────────────────┼─────────────────────┘
                                            │ POST /api/communications/send
                                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                        BFF API (.NET 8)                         │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  CommunicationEndpoints.cs                               │   │
│  │  ├── POST /send          → SendCommunicationAsync        │   │
│  │  ├── POST /send-bulk     → SendBulkCommunicationAsync    │   │
│  │  └── GET  /{id}/status   → GetCommunicationStatusAsync   │   │
│  └──────────────────┬───────────────────────────────────────┘   │
│                     │                                           │
│  ┌──────────────────▼───────────────────────────────────────┐   │
│  │  CommunicationAuthorizationFilter (ADR-008)              │   │
│  │  Validates: IsAuthenticated + oid/nameidentifier claim   │   │
│  └──────────────────┬───────────────────────────────────────┘   │
│                     │                                           │
│  ┌──────────────────▼───────────────────────────────────────┐   │
│  │  CommunicationService (sealed)                           │   │
│  │  7-Step Send Pipeline:                                   │   │
│  │                                                          │   │
│  │  1. Validate Request (required fields)                   │   │
│  │  2. Download Attachments from SPE (if any)               │   │
│  │  3. Resolve Approved Sender                              │   │
│  │  4. Build Graph Message payload                          │   │
│  │  5. Send via Graph sendMail  ◄── CRITICAL PATH           │   │
│  │  6. Create Dataverse record  ◄── best-effort             │   │
│  │  7. Archive .eml to SPE      ◄── best-effort             │   │
│  │  8. Create attachment records ◄── best-effort             │   │
│  └──┬─────────┬──────────┬──────────┬───────────────────────┘   │
│     │         │          │          │                            │
│     ▼         ▼          ▼          ▼                            │
│  ┌──────┐ ┌──────┐ ┌──────────┐ ┌────────────────┐             │
│  │Graph │ │Dv Svc│ │SpeFile   │ │EmlGeneration   │             │
│  │Client│ │      │ │Store     │ │Service         │             │
│  │Factor│ │      │ │(ADR-007) │ │(MimeKit)       │             │
│  └──┬───┘ └──┬───┘ └────┬─────┘ └───────┬────────┘             │
└─────┼────────┼──────────┼────────────────┼──────────────────────┘
      │        │          │                │
      ▼        ▼          ▼                ▼
  ┌──────┐ ┌───────┐ ┌──────────┐   ┌──────────────┐
  │MS    │ │Dv Web │ │SharePoint│   │RFC 2822 .eml │
  │Graph │ │API    │ │Embedded  │   │(in-memory)   │
  │API   │ │       │ │(SPE)     │   │              │
  └──────┘ └───────┘ └──────────┘   └──────────────┘
```

---

## Component Inventory

### Backend (BFF API)

| File | Type | Purpose |
|------|------|---------|
| `Api/CommunicationEndpoints.cs` | Minimal API | Route definitions: `/send`, `/send-bulk`, `/{id}/status` |
| `Api/Filters/CommunicationAuthorizationFilter.cs` | Endpoint Filter | Per-endpoint auth (ADR-008) |
| `Services/Communication/CommunicationService.cs` | Service | Core 7-step send pipeline |
| `Services/Communication/ApprovedSenderValidator.cs` | Service | Two-tier sender resolution (config + Dataverse + Redis) |
| `Services/Communication/EmlGenerationService.cs` | Service | RFC 2822 .eml generation via MimeKit |
| `Configuration/CommunicationOptions.cs` | Options | `Communication` config section binding |

### Models

| File | Type | Purpose |
|------|------|---------|
| `Models/SendCommunicationRequest.cs` | DTO | POST /send request body |
| `Models/SendCommunicationResponse.cs` | DTO | POST /send response body |
| `Models/BulkSendRequest.cs` | DTO | POST /send-bulk request body |
| `Models/BulkSendResponse.cs` | DTO | POST /send-bulk response body |
| `Models/CommunicationStatusResponse.cs` | DTO | GET /status response body |
| `Models/CommunicationAssociation.cs` | DTO | Entity association for regarding lookup |
| `Models/CommunicationType.cs` | Enum | Email, TeamsMessage, SMS, Notification |
| `Models/CommunicationStatus.cs` | Enum | Draft, Queued, Send, Delivered, Failed, Bounded, Recalled |
| `Models/CommunicationDirection.cs` | Enum | Incoming, Outgoing |
| `Models/BodyFormat.cs` | Enum | HTML, PlainText |
| `Models/ApprovedSenderResult.cs` | DTO | Sender validation result |

### Frontend (Dataverse UI)

| File | Type | Purpose |
|------|------|---------|
| `WebResources/sprk_communication_send.js` | Web Resource | Send button click handler, form validation, BFF API call |
| Communication Ribbon XML | RibbonDiffXml | Send button in form command bar |

---

## Send Pipeline

The `CommunicationService.SendAsync()` method executes a 7-step pipeline:

```
Step 1: ValidateRequest
  ├── To[] required (≥1 recipient)
  ├── Subject required
  └── Body required
  ↓ (throws SdapProblemException on failure)

Step 1b: DownloadAndBuildAttachments (conditional)
  ├── Validate count ≤ 150
  ├── For each attachmentDocumentId:
  │   ├── Get metadata from SpeFileStore
  │   ├── Validate total size ≤ 35MB
  │   └── Download content as byte[]
  └── Build List<FileAttachment>
  ↓ (throws SdapProblemException on failure)

Step 2: Resolve Approved Sender
  ├── fromMailbox == null → resolve default sender
  └── fromMailbox != null → validate against approved list
  ↓ (throws SdapProblemException if invalid)

Step 3: Build Graph Message
  ├── Map Subject, Body (HTML or PlainText)
  ├── Map From (resolved sender)
  ├── Map To[], Cc[], Bcc[] recipients
  └── Attach FileAttachments (if any)
  ↓

Step 4: Send via Graph API  ◄── CRITICAL PATH
  ├── graphClient = GraphClientFactory.ForApp()
  ├── graphClient.Users[sender].SendMail.PostAsync()
  └── SaveToSentItems = true
  ↓ (throws SdapProblemException on ODataError)

Step 5: Create Dataverse Record  ◄── BEST-EFFORT
  ├── Build sprk_communication entity
  ├── Map association fields (regarding lookup + denormalized)
  ├── Set status to Send (659490002)
  └── dataverseService.CreateAsync()
  ↓ (catch: log warning, continue)

Step 6: Archive to SPE  ◄── BEST-EFFORT (if ArchiveToSpe=true)
  ├── Generate .eml via EmlGenerationService
  ├── Upload to SPE at /communications/{id}/{filename}.eml
  └── Create sprk_document record linking to archived file
  ↓ (catch: set ArchivalWarning, continue)

Step 7: Create Attachment Records  ◄── BEST-EFFORT
  ├── For each attachmentDocumentId:
  │   └── Create sprk_communicationattachment record
  └── Link to sprk_communication + sprk_document
  ↓ (catch: set AttachmentRecordWarning, continue)

Return SendCommunicationResponse
```

---

## API Endpoints

### Route Group

```
/api/communications  (RequireAuthorization, Tag: "Communications")
```

### POST /api/communications/send

Send a single email communication.

**Request Body** (`SendCommunicationRequest`):

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `to` | `string[]` | Yes | — | Recipient email addresses (≥1) |
| `cc` | `string[]` | No | null | CC recipients |
| `bcc` | `string[]` | No | null | BCC recipients |
| `subject` | `string` | Yes | — | Email subject line |
| `body` | `string` | Yes | — | Email body content |
| `bodyFormat` | `BodyFormat` | No | `HTML` | `HTML` or `PlainText` |
| `fromMailbox` | `string` | No | null | Sender mailbox (null = default) |
| `communicationType` | `CommunicationType` | No | `Email` | Channel type |
| `associations` | `CommunicationAssociation[]` | No | null | Entity associations |
| `correlationId` | `string` | No | auto-generated | Tracing correlation ID |
| `archiveToSpe` | `bool` | No | `false` | Archive .eml to SPE |
| `attachmentDocumentIds` | `string[]` | No | null | SPE document IDs to attach |

**Response** (`SendCommunicationResponse`):

| Field | Type | Description |
|-------|------|-------------|
| `communicationId` | `Guid?` | Dataverse record ID (null if Dataverse failed) |
| `graphMessageId` | `string` | Graph message tracking ID |
| `status` | `CommunicationStatus` | Current status (typically `Send`) |
| `sentAt` | `DateTimeOffset` | Send timestamp |
| `from` | `string` | Sender email used |
| `correlationId` | `string` | Tracing correlation ID |
| `archivedDocumentId` | `Guid?` | SPE document ID (if archived) |
| `archivalWarning` | `string?` | Warning if archival failed |
| `attachmentCount` | `int` | Number of attachments sent |
| `attachmentRecordWarning` | `string?` | Warning if attachment records failed |

**Status Codes**: `200 OK`, `400 Bad Request`, `403 Forbidden`, `500 Internal Server Error`

### POST /api/communications/send-bulk

Send the same email to multiple recipients (1-50). Each recipient gets their own `sprk_communication` record.

**Request Body** (`BulkSendRequest`):

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `recipients` | `BulkRecipient[]` | Yes | Array of recipients (1-50) |
| `subject` | `string` | Yes | Shared subject line |
| `body` | `string` | Yes | Shared body content |
| `bodyFormat` | `BodyFormat` | No | `HTML` or `PlainText` |
| `fromMailbox` | `string` | No | Sender mailbox |
| `communicationType` | `CommunicationType` | No | Channel type |
| `attachmentDocumentIds` | `string[]` | No | Shared attachments |
| `archiveToSpe` | `bool` | No | Archive each send |
| `associations` | `CommunicationAssociation[]` | No | Shared associations |

**Response** (`BulkSendResponse`):

| Field | Type | Description |
|-------|------|-------------|
| `totalRecipients` | `int` | Total recipients in request |
| `succeeded` | `int` | Count of successful sends |
| `failed` | `int` | Count of failed sends |
| `results` | `BulkSendResult[]` | Per-recipient results |

**Status Codes**: `200 OK` (all succeeded), `207 Multi-Status` (partial), `400`, `403`, `500`

**Rate Limiting**: 100ms inter-send delay between sequential sends for Graph API awareness.

### GET /api/communications/{id}/status

Look up communication status from Dataverse.

**Response** (`CommunicationStatusResponse`):

| Field | Type | Description |
|-------|------|-------------|
| `communicationId` | `Guid` | Record ID |
| `status` | `CommunicationStatus` | Current status |
| `graphMessageId` | `string?` | Graph tracking ID |
| `sentAt` | `DateTimeOffset?` | Send timestamp |
| `from` | `string?` | Sender email |

**Status Codes**: `200 OK`, `404 Not Found`

---

## Approved Sender Resolution

The `ApprovedSenderValidator` uses a two-tier model to validate sender mailboxes:

### Tier 1: Configuration (Synchronous)

```json
{
  "Communication": {
    "ApprovedSenders": [
      {
        "Email": "noreply@spaarke.com",
        "DisplayName": "Spaarke Notifications",
        "IsDefault": true
      },
      {
        "Email": "legal@spaarke.com",
        "DisplayName": "Legal Department",
        "IsDefault": false
      }
    ],
    "DefaultMailbox": "noreply@spaarke.com"
  }
}
```

### Tier 2: Dataverse + Redis Cache (Asynchronous)

1. Check Redis cache (key: `communication:approved-senders`, TTL: 5 minutes)
2. If cache miss: query Dataverse `sprk_approvedsender` entity for active records
3. Merge: config senders as base, Dataverse senders overlay (Dataverse wins on email match)
4. Cache merged result in Redis

### Resolution Priority

| `fromMailbox` | Behavior |
|---------------|----------|
| `null` | Return sender with `IsDefault=true`, else `DefaultMailbox` match, else first sender |
| `"legal@spaarke.com"` | Match against approved list (case-insensitive). If found, return it. If not, return error `INVALID_SENDER` |

### Error Codes

| Code | Scenario |
|------|----------|
| `INVALID_SENDER` | Requested sender not in approved list |
| `NO_DEFAULT_SENDER` | No sender configured and no default available |

---

## Association Mapping

Communications can be linked to 8 entity types via the `associations[]` field:

### Regarding Lookup Map

| Entity Type | Lookup Field | Entity Set Name |
|-------------|-------------|-----------------|
| `sprk_matter` | `sprk_regardingmatter` | `sprk_matters` |
| `sprk_project` | `sprk_regardingproject` | `sprk_projects` |
| `sprk_organization` | `sprk_regardingorganization` | `sprk_organizations` |
| `contact` | `sprk_regardingperson` | `contacts` |
| `sprk_analysis` | `sprk_regardinganalysis` | `sprk_analysises` |
| `sprk_budget` | `sprk_regardingbudget` | `sprk_budgets` |
| `sprk_invoice` | `sprk_regardinginvoice` | `sprk_invoices` |
| `sprk_workassignment` | `sprk_regardingworkassignment` | `sprk_workassignments` |

### Mapping Behavior

- `associations[0]` (primary) → sets the regarding lookup field + denormalized text fields
- Denormalized fields: `sprk_regardingrecordname`, `sprk_regardingrecordid`, `sprk_regardingrecordurl`
- `sprk_associationcount` → total number of associations provided
- Unknown entity types → warning logged, lookup not set

---

## Attachment Handling

### Limits

| Limit | Value | Enforced At |
|-------|-------|-------------|
| Max attachment count | 150 | `DownloadAndBuildAttachmentsAsync` |
| Max total size | 35 MB (36,700,160 bytes) | Cumulative check during download |

### Flow

```
Request.AttachmentDocumentIds[]
  │
  ▼ (for each ID)
  SpeFileStore.GetFileMetadataAsync(driveId, itemId)
  → validate cumulative size
  SpeFileStore.DownloadFileAsync(driveId, itemId)
  → read to byte[]
  → build Graph FileAttachment {Name, ContentType, ContentBytes}
  │
  ▼
  Attach to Graph Message.Attachments[]
  │
  ▼ (after Graph send succeeds)
  Create sprk_communicationattachment records in Dataverse (best-effort)
```

### Content Type Inference

The service infers MIME types from file extensions for 20+ common types (PDF, DOCX, XLSX, PNG, etc.), falling back to `application/octet-stream` for unknown extensions.

---

## EML Archival

When `archiveToSpe=true`, the service archives the sent email as an RFC 2822 `.eml` file:

### Generation (EmlGenerationService)

- Uses **MimeKit** library for RFC 2822 compliance
- Supports HTML and PlainText body formats
- Supports multipart/mixed for attachments (base64 encoded)
- File naming: `{sanitized_subject}_{yyyyMMdd_HHmmss}.eml`

### Storage Path

```
/communications/{communicationId:N}/{sanitized_subject}_{timestamp}.eml
```

### Dataverse Linkage

After upload, creates a `sprk_document` record:
- `sprk_documenttype` = Communication (100000002)
- `sprk_sourcetype` = CommunicationArchive (100000001)
- `sprk_communication` = EntityReference to the communication record
- `sprk_speitemid` / `sprk_spedriveid` = SPE file identifiers

---

## Dataverse Entity Schema

### sprk_communication

| Field | Type | Description |
|-------|------|-------------|
| `sprk_name` | String(200) | Auto-generated: "Email: {subject}" |
| `sprk_communiationtype` | OptionSet | Email, TeamsMessage, SMS, Notification |
| `statuscode` | OptionSet | Draft(1), Queued, Send, Delivered, Failed, Bounded, Recalled |
| `statecode` | OptionSet | Active(0), Inactive(1) |
| `sprk_direction` | OptionSet | Incoming(0), Outgoing(1) |
| `sprk_bodyformat` | OptionSet | HTML(0), PlainText(1) |
| `sprk_to` | String | Semicolon-delimited recipient list |
| `sprk_cc` | String | Semicolon-delimited CC list |
| `sprk_bcc` | String | Semicolon-delimited BCC list |
| `sprk_from` | String | Sender email address |
| `sprk_subject` | String | Email subject |
| `sprk_body` | Multiline | Email body content |
| `sprk_graphmessageid` | String | Graph message / correlation ID |
| `sprk_sentat` | DateTime | Send timestamp |
| `sprk_correlationid` | String | Tracing correlation ID |
| `sprk_hasattachments` | Boolean | Whether attachments were included |
| `sprk_attachmentcount` | Integer | Number of attachments |
| `sprk_associationcount` | Integer | Number of entity associations |
| `sprk_regardingrecordname` | String(100) | Denormalized: primary association name |
| `sprk_regardingrecordid` | String(100) | Denormalized: primary association ID |
| `sprk_regardingrecordurl` | String(200) | Denormalized: primary association URL |
| `sprk_regarding{entity}` | Lookup | 8 entity-specific lookup fields |

### sprk_communicationattachment

| Field | Type | Description |
|-------|------|-------------|
| `sprk_name` | String(200) | File display name |
| `sprk_communication` | Lookup | Parent communication record |
| `sprk_document` | Lookup | SPE document record |
| `sprk_attachmenttype` | OptionSet | File(100000000) |

### Communication Status Values

| Value | Label | Description |
|-------|-------|-------------|
| 1 | Draft | New record, not yet sent |
| 659490001 | Queued | Queued for sending |
| 659490002 | Send | Successfully sent via Graph |
| 659490003 | Delivered | Delivery confirmed |
| 659490004 | Failed | Send failed |
| 659490005 | Bounded | Bounced back |
| 659490006 | Recalled | Recalled by sender |

---

## Configuration

### appsettings.json

```json
{
  "Communication": {
    "ApprovedSenders": [
      {
        "Email": "noreply@spaarke.com",
        "DisplayName": "Spaarke Notifications",
        "IsDefault": true
      }
    ],
    "DefaultMailbox": "noreply@spaarke.com",
    "ArchiveContainerId": "{spe-container-drive-id}"
  }
}
```

### DI Registration

All services are registered as singletons per ADR-010:

```csharp
services.Configure<CommunicationOptions>(config.GetSection("Communication"));
services.AddSingleton<CommunicationService>();
services.AddSingleton<ApprovedSenderValidator>();
services.AddSingleton<EmlGenerationService>();
services.AddSingleton<CommunicationAuthorizationFilter>();
```

---

## Error Handling

All errors use `SdapProblemException` which produces RFC 7807 ProblemDetails responses (ADR-019).

### Error Codes

| Code | HTTP | Scenario |
|------|------|----------|
| `VALIDATION_ERROR` | 400 | Missing To, Subject, or Body |
| `INVALID_SENDER` | 400 | Sender not in approved list |
| `NO_DEFAULT_SENDER` | 400 | No sender configured |
| `ATTACHMENT_LIMIT_EXCEEDED` | 400 | >150 attachments or >35MB total |
| `ATTACHMENT_NOT_FOUND` | 404 | SPE document not found |
| `ATTACHMENT_DOWNLOAD_FAILED` | 502 | Failed to download from SPE |
| `ATTACHMENT_CONFIG_ERROR` | 500 | ArchiveContainerId not configured |
| `GRAPH_SEND_FAILED` | 502/500 | Graph sendMail API error |
| `COMMUNICATION_NOT_FOUND` | 404 | Status lookup — record not found |
| `COMMUNICATION_NOT_AUTHORIZED` | 403 | User lacks valid identity |

### ProblemDetails Extensions

All error responses include:
- `correlationId`: For log tracing
- `graphErrorCode`: (Graph errors only) Original Graph error code
- `failedDocumentId` / `attachmentIndex`: (Attachment errors) Which attachment failed

---

## Security

### Authentication

- All endpoints require authentication via `RequireAuthorization()`
- `CommunicationAuthorizationFilter` validates:
  - `user.Identity.IsAuthenticated == true`
  - Valid `oid` or `NameIdentifier` claim present
- Graph API calls use app-only authentication via `GraphClientFactory.ForApp()`

### Sender Controls

- Only mailboxes in the approved senders list can be used as `From`
- Config-based senders are the baseline; Dataverse senders can extend the list
- All sender resolution is case-insensitive

### Data Protection

- Email body content is stored in Dataverse (subject to Dataverse RBAC)
- BCC recipients are stored on the Dataverse record (not exposed in form views by default)
- Archived .eml files in SPE inherit container-level permissions

---

## UI Integration

### Send Button (Ribbon)

The Send button is added to the `sprk_communication` main form command bar via RibbonDiffXml:

| Element | ID | Description |
|---------|----|-------------|
| CustomAction | `sprk.communication.send.CustomAction` | Places button in Actions group |
| Button | `sprk.communication.send.Button` | Visible button with `ModernImage="Send"` |
| Command | `sprk.communication.send.Command` | Links button to JavaScript handler |
| EnableRule | `sprk.communication.isStatusDraft.EnableRule` | Enabled only when `statuscode=1` (Draft) |

### Web Resource: sprk_communication_send.js

| Function | Purpose |
|----------|---------|
| `isStatusDraft(formContext)` | Enable rule: returns `true` only when Draft |
| `sendCommunication(executionContext)` | Button handler: validate → collect → send → update |
| `_buildRequest(formContext)` | Collects form fields into SendCommunicationRequest DTO |
| `_collectAssociations(formContext)` | Reads 8 regarding lookup fields into associations[] |
| `_sendRequest(formContext, request)` | POST to BFF with auth token |
| `_handleSuccess(formContext, response)` | Update status to Send, save, show notification |
| `_handleError(formContext, problemDetails)` | Parse ProblemDetails, show error notification |

### BFF URL Resolution

The web resource resolves the BFF API base URL in this order:
1. Dataverse environment variable `sprk_BffApiBaseUrl`
2. Hardcoded default: `https://spe-api-dev-67e2xz.azurewebsites.net`

---

## AI Playbook Integration

The `SendCommunicationToolHandler` implements `IAiToolHandler` (ADR-013) to allow AI playbooks to send communications programmatically.

### Tool Registration

The handler is auto-discovered via the `IAiToolHandler` interface and registered in the AI Tool Framework.

### Tool Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `to` | `string` | Yes | Comma-separated recipient emails |
| `subject` | `string` | Yes | Email subject |
| `body` | `string` | Yes | Email body (HTML) |
| `fromMailbox` | `string` | No | Sender mailbox |
| `associations` | `object[]` | No | Entity associations |

---

## ADR Compliance

| ADR | Compliance | Implementation |
|-----|-----------|----------------|
| ADR-001 | ✅ | Minimal API endpoints in `CommunicationEndpoints.cs` |
| ADR-007 | ✅ | `SpeFileStore` facade for all SPE operations (attachment download, .eml upload) |
| ADR-008 | ✅ | `CommunicationAuthorizationFilter` as endpoint filter, not global middleware |
| ADR-010 | ✅ | All services registered as singletons; concrete types (no unnecessary interfaces) |
| ADR-013 | ✅ | `SendCommunicationToolHandler` extends BFF via `IAiToolHandler` |
| ADR-019 | ✅ | `SdapProblemException` for all error responses with ProblemDetails format |

---

## Appendix: Sequence Diagram — Single Send

```
Client          Endpoints       AuthFilter      CommService     SenderValidator    Graph           Dataverse       SPE
  │                │                │               │                │               │               │              │
  │ POST /send     │                │               │                │               │               │              │
  │───────────────>│                │               │                │               │               │              │
  │                │ InvokeAsync    │               │                │               │               │              │
  │                │───────────────>│               │                │               │               │              │
  │                │      OK       │               │                │               │               │              │
  │                │<──────────────│               │                │               │               │              │
  │                │                                │                │               │               │              │
  │                │ SendAsync(req)                 │                │               │               │              │
  │                │──────────────────────────────->│                │               │               │              │
  │                │                                │ Resolve(from)  │               │               │              │
  │                │                                │───────────────>│               │               │              │
  │                │                                │   sender       │               │               │              │
  │                │                                │<───────────────│               │               │              │
  │                │                                │                                │               │              │
  │                │                                │ sendMail(msg)                  │               │              │
  │                │                                │───────────────────────────────>│               │              │
  │                │                                │              OK               │               │              │
  │                │                                │<──────────────────────────────│               │              │
  │                │                                │                                               │              │
  │                │                                │ CreateAsync(sprk_communication)                │              │
  │                │                                │──────────────────────────────────────────────>│              │
  │                │                                │              recordId                         │              │
  │                │                                │<─────────────────────────────────────────────│              │
  │                │                                │                                                             │
  │                │                                │ UploadSmallAsync(.eml)                                      │
  │                │                                │────────────────────────────────────────────────────────────>│
  │                │                                │              fileHandle                                    │
  │                │                                │<───────────────────────────────────────────────────────────│
  │                │                                │                                                             │
  │                │   SendCommunicationResponse    │                                                             │
  │                │<──────────────────────────────│                                                             │
  │  200 OK        │                                                                                              │
  │<───────────────│                                                                                              │
```

---

*Architecture document for the Email Communication Solution R1. See also: [Deployment Guide](../guides/COMMUNICATION-DEPLOYMENT-GUIDE.md) | [User Guide](../guides/communication-user-guide.md)*
