# SDAP Office Integration - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-01-19
> **Revised**: 2026-01-20 (Per SPEC-REVIEW-AND-REVISIONS.md + technical feedback)
> **Source**: design.md
> **Project**: 1 of 3 in Office + Teams Integration Initiative

---

## Executive Summary

Build a unified Office Integration Platform enabling Outlook and Word add-ins to save content to Spaarke DMS and share documents from Spaarke. The platform uses a shared React task pane UI with host-specific adapters, backed by .NET Minimal API endpoints and async job processing. This foundational project establishes APIs and patterns that Teams and External Portal projects will build upon.

---

## Glossary

| Term | Definition |
|------|------------|
| **Document** | A Spaarke Dataverse entity (`sprk_document`) representing a managed document with metadata and association |
| **File** | The actual binary (docx, pdf, xlsx, etc.) stored in SharePoint Embedded, always 1:1 with a Document |
| **Association Target** | An entity that a Document is associated with: Matter, Project, Invoice, Account, or Contact |
| **NAA** | Nested App Authentication - the Microsoft-recommended auth pattern for Office Add-ins (replaces legacy tokens) |

---

## Document/File Relationship

```
┌─────────────────────────────────────────────────────────────────┐
│                    USER'S WORKING FILE                          │
│    (Word .docx, Outlook email, Excel .xlsx, PDF, etc.)          │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ "Save to Spaarke"
                              │ (creates Document + uploads File)
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                 sprk_document (Spaarke Document)                │
│                                                                 │
│  Metadata:                                                      │
│  - sprk_documentname (display name)                             │
│  - sprk_filename (original filename)                            │
│  - sprk_filesize, sprk_contenttype                              │
│                                                                 │
│  SPE File Reference (1:1):                                      │
│  - sprk_graphdriveid (SPE container/drive ID)                   │
│  - sprk_graphitemid (SPE item ID)                               │
│                                                                 │
│  Association Lookups (exactly ONE must be populated):           │
│  - sprk_matter   → Lookup(sprk_matter)                          │
│  - sprk_project  → Lookup(sprk_project)                         │
│  - sprk_invoice  → Lookup(sprk_invoice)                         │
│  - sprk_account  → Lookup(account)                              │
│  - sprk_contact  → Lookup(contact)                              │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ 1:1 relationship
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│              SharePoint Embedded (File Storage)                 │
│  Actual binary file stored here, accessed via Graph API         │
│  Mediated through SpeFileStore facade                           │
└─────────────────────────────────────────────────────────────────┘
```

**Key Rules**:
1. A File CANNOT exist without an associated Document
2. Document:File is 1:1 (one Document = one File in SPE)
3. A Document MUST be associated to exactly ONE of: Matter, Project, Invoice, Account, or Contact
4. "Document Only" (no association) is NOT allowed

**Future Consideration**: V1 requires exactly one primary association. Future versions may support additional associations via a link table for scenarios where documents relate to multiple entities (e.g., a document relevant to both a Matter and a Project).

---

## Scope

### In Scope

- **Outlook Add-in (New Outlook + Web)**
  - Read mode: "Save to Spaarke" - save emails and attachments as Documents associated to Matter/Project/Invoice/Account/Contact
  - Compose mode: "Share from Spaarke" - insert links or attach copies
  - Compose mode: "Grant access" - create invitations for external recipients (stub for External Portal)

- **Word Add-in (Desktop + Web)**
  - "Save to Spaarke" - save Word document as Document associated to an entity
  - "Save new version" - update version lineage for linked documents
  - "Share / Insert link / Attach copy" - same model as Outlook compose
  - "Grant access" - external collaboration (stub for External Portal)

- **Shared Office Integration Platform**
  - React task pane UI with Fluent UI v9
  - Host adapter interface (Outlook adapter, Word adapter)
  - Typed API client for `/office/*` endpoints
  - Job status manager with SSE (fallback: polling)

- **Backend APIs**
  - `/office/save` - submit email/doc for filing
  - `/office/jobs/{jobId}` - job status polling
  - `/office/jobs/{jobId}/stream` - job status SSE stream
  - `/office/share/links` - generate share links
  - `/office/share/attach` - attachment package for compose
  - `/office/quickcreate/{entityType}` - inline entity creation
  - `/office/search/entities` - search association targets
  - `/office/search/documents` - search for sharing
  - `/office/recent` - recent items

- **Background Processing**
  - Upload finalization worker
  - Profile summary worker
  - Indexing worker
  - Deep analysis worker (optional)

- **Dataverse Schema Extensions**
  - EmailArtifact (email metadata/body snapshot)
  - AttachmentArtifact (source attachment metadata)
  - ProcessingJob (job tracking per ADR-004)

### Out of Scope

- **Classic Outlook (COM add-in)** - V1 targets New Outlook and Outlook Web only
- **"Open from Spaarke" in Word** - Users open documents from Spaarke application
- **Teams app** - Separate project (SDAP-teams-app)
- **External portal / Power Pages** - Separate project (SDAP-external-portal)
- **Mailbox automation** - Feature-flagged for future (not V1)
- **Mobile Office apps** - Desktop and web clients only
- **"Document Only" saves** - User must select association target

### Affected Areas

| Area | Path | Changes |
|------|------|---------|
| BFF API | `src/server/api/Sprk.Bff.Api/` | New `/office/*` endpoints, workers |
| Office Add-in | `src/client/office-addins/` | New React task pane, manifests |
| Shared UI | `src/client/shared/` | Reusable components for task pane |
| Dataverse | `src/solutions/` | Schema extensions |

---

## Requirements

### Functional Requirements

| ID | Requirement | Acceptance Criteria |
|----|-------------|---------------------|
| **FR-01** | Save Outlook email to Spaarke | User can save email body + selected attachments as Document(s) associated to Matter/Project/Invoice/Account/Contact; returns jobId within 3 seconds |
| **FR-02** | Save attachments selectively | User can toggle individual attachments on/off before saving |
| **FR-03** | Quick Create entity | User can create Matter/Project/Invoice/Account/Contact inline with minimal required fields; extensible via code |
| **FR-04** | Search association targets | Typeahead search returns Matters/Projects/Invoices/Accounts/Contacts within 500ms |
| **FR-05** | Recent items | User sees recently used association targets in picker |
| **FR-06** | Share via link insertion | User can insert Spaarke document links into Outlook compose; links resolve through Spaarke access checks |
| **FR-07** | Share via attachment | User can attach document copies from Spaarke to Outlook compose |
| **FR-08** | Grant access to external | User can mark "Grant access to recipients"; creates invitation stubs for External Portal integration |
| **FR-09** | Save Word document | User can save Word document as Spaarke Document associated to Matter/Project/Invoice/Account/Contact |
| **FR-10** | Version Word document | If document originated from Spaarke, "Save version" updates lineage; otherwise creates new + versions |
| **FR-11** | Job status display | Task pane shows stage-based status; updates via SSE (fallback: 3-second polling) |
| **FR-12** | Duplicate detection | If email/doc already saved with same association, return existing Document and notify user |
| **FR-13** | Processing options | User can toggle Profile summary, RAG index, Deep analysis (policy-driven defaults) |
| **FR-14** | Mandatory association | User must select association target; cannot save without association |

### Non-Functional Requirements

| ID | Requirement | Target |
|----|-------------|--------|
| **NFR-01** | Save response time | API returns jobId within 3 seconds; heavy processing async |
| **NFR-02** | Task pane responsiveness | UI remains interactive during operations; no blocking modals |
| **NFR-03** | Attachment size limit | 25MB per file, 100MB total per email (code-configurable) |
| **NFR-04** | Job status updates | SSE primary; polling fallback at 3 seconds (configurable) |
| **NFR-05** | Idempotency | Save/share endpoints idempotent by SHA256 of canonical payload |
| **NFR-06** | Authorization | All endpoints enforce authN/authZ via endpoint filters (ADR-008) |
| **NFR-07** | Error responses | All errors return ProblemDetails with correlation ID |
| **NFR-08** | Observability | Correlation IDs flow add-in → API → worker; structured logs per stage |
| **NFR-09** | Accessibility | WCAG 2.1 AA compliance; keyboard navigation; screen reader support |
| **NFR-10** | Dark mode | Full dark mode and high-contrast support via Fluent UI v9 tokens |

---

## Technical Constraints

### Applicable ADRs

| ADR | Relevance |
|-----|-----------|
| **ADR-001** | Minimal API + BackgroundService - no Azure Functions |
| **ADR-004** | Job contract and processing pattern |
| **ADR-007** | SpeFileStore facade - no Graph SDK types exposed |
| **ADR-008** | Endpoint filters for authorization - no global middleware |

### MUST Rules

- ✅ MUST implement NAA as primary authentication method
- ✅ MUST implement Dialog API fallback for clients that don't support NAA
- ✅ MUST use Unified Manifest for Outlook add-in (GA, production-ready)
- ✅ MUST use XML add-in-only manifest for Word add-in (unified manifest is preview for Word)
- ✅ MUST use Minimal API for all `/office/*` endpoints
- ✅ MUST use BackgroundService + Service Bus for async processing
- ✅ MUST return `202 Accepted` with jobId and statusUrl from save endpoints
- ✅ MUST route all SPE operations through `SpeFileStore` facade
- ✅ MUST NOT expose Graph SDK types in DTOs
- ✅ MUST use endpoint filters for resource authorization
- ✅ MUST use Fluent UI v9 (`@fluentui/react-components`) exclusively
- ✅ MUST wrap task pane UI in `FluentProvider` with theme
- ✅ MUST use design tokens for all colors, spacing, typography
- ✅ MUST support light, dark, and high-contrast modes
- ✅ MUST require association target for all document saves

### MUST NOT Rules

- ❌ MUST NOT create Azure Functions
- ❌ MUST NOT inject GraphServiceClient outside SpeFileStore
- ❌ MUST NOT use global middleware for authorization
- ❌ MUST NOT use Fluent v8 (`@fluentui/react`)
- ❌ MUST NOT hard-code colors (hex, rgb, named)
- ❌ MUST NOT target Classic Outlook in V1
- ❌ MUST NOT use legacy Exchange tokens (getCallbackTokenAsync, getUserIdentityTokenAsync)
- ❌ MUST NOT allow "Document Only" saves (no association)

### Existing Patterns to Follow

| Pattern | Location |
|---------|----------|
| Endpoint definition | `.claude/patterns/api/endpoint-definition.md` |
| Background workers | `.claude/patterns/api/background-workers.md` |
| Endpoint filters | `.claude/patterns/api/endpoint-filters.md` |
| SpeFileStore usage | `.claude/patterns/data/spefilestore-usage.md` |

---

## Authentication Architecture

### Client-Side Authentication (Task Pane)

**Primary: Nested App Authentication (NAA)**

NAA is the Microsoft-recommended authentication pattern for Office Add-ins as of 2025. Legacy Exchange tokens were deprecated in August 2025.

```typescript
import { createNestablePublicClientApplication } from "@azure/msal-browser";

const msalConfig = {
  auth: {
    clientId: "SPAARKE_OFFICE_ADDIN_CLIENT_ID",
    authority: "https://login.microsoftonline.com/common"
  },
  cache: {
    cacheLocation: "localStorage"
  }
};

const pca = await createNestablePublicClientApplication(msalConfig);

// Token acquisition
async function getAccessToken(scopes: string[]): Promise<string> {
  try {
    // Try silent acquisition first
    const result = await pca.acquireTokenSilent({ scopes });
    return result.accessToken;
  } catch (error) {
    // Fall back to interactive if silent fails
    const result = await pca.acquireTokenPopup({ scopes });
    return result.accessToken;
  }
}
```

**Fallback: Dialog API**

For older Outlook clients that don't support NAA:
- Use Office Dialog API to open authentication popup
- Reference: Microsoft sample Outlook-Add-in-SSO-NAA-IE

### Server-Side Authentication (BFF API)

**Token Validation**:
- Validate Azure AD tokens from NAA (NOT legacy Exchange tokens)
- Validate audience matches Spaarke BFF API app registration
- Extract user claims for authorization decisions

**OBO Flow (When Needed)**:
- Use OBO only when BFF needs to call Graph/SPE on user's behalf
- BFF has its own app registration with client secret
- Exchange user's token for Graph token via OBO

### App Registration Requirements

**Spaarke Office Add-in (Public Client)**:
- Application type: Single-page application (SPA)
- Redirect URIs:
  - `brk-multihub://localhost` (NAA broker)
  - `https://{addin-domain}/taskpane.html` (fallback)
- API permissions (delegated):
  - `api://{bff-api-id}/user_impersonation`
  - `User.Read` (Microsoft Graph, for user info)

**Spaarke BFF API (Confidential Client)**:
- Application type: Web API
- Expose API: `api://{bff-api-id}`
- Scopes: `user_impersonation`
- API permissions (delegated, for OBO):
  - `Files.ReadWrite.All` (Graph - for SPE)
  - `Sites.ReadWrite.All` (Graph - for SPE)

**V1 Scope Note**: No mailbox Graph permissions (`Mail.Read`, `Mail.ReadWrite`) are required for V1. All email/attachment content is retrieved client-side via Office.js APIs. Server-side mailbox access is a future consideration (see Email Content Retrieval section).

---

## Manifest Strategy

### Outlook Add-in: Unified Manifest (Primary)

Use the unified JSON manifest format for Outlook:
- Production-ready (GA) for Outlook
- Single app model across Microsoft 365
- Future-proof for Copilot agent integration
- Simplified deployment via Microsoft 365 admin center

**Manifest Location**: `/src/client/office-addins/outlook/manifest.json`

**Key Configuration**:
```json
{
  "$schema": "https://developer.microsoft.com/json-schemas/teams/vDevPreview/MicrosoftTeams.schema.json",
  "manifestVersion": "devPreview",
  "id": "{GUID}",
  "version": "1.0.0",
  "name": {
    "short": "Spaarke",
    "full": "Spaarke Document Management"
  },
  "extensions": [
    {
      "requirements": {
        "scopes": ["mail"],
        "capabilities": [{ "name": "Mailbox", "minVersion": "1.8" }]
      },
      "runtimes": [...],
      "ribbons": [...]
    }
  ]
}
```

**Platform Support Notes**:
- For clients that don't directly support unified manifest, publish via Microsoft 365 admin center or AppSource
- Admin center auto-generates XML add-in-only manifest for unsupported platforms

### Word Add-in: XML Add-in-Only Manifest (Required)

Unified manifest is **preview** for Word (as of Jan 2026). Use XML manifest for production:
- Production-ready and stable
- Required for Word Desktop on Mac (unified manifest not directly supported)
- Full feature parity with unified manifest for Word scenarios

**Manifest Location**: `/src/client/office-addins/word/manifest.xml`

**Key Configuration**:
```xml
<?xml version="1.0" encoding="UTF-8"?>
<OfficeApp xmlns="http://schemas.microsoft.com/office/appforoffice/1.1"
           xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
           xsi:type="TaskPaneApp">
  <Id>{GUID}</Id>
  <Version>1.0.0</Version>
  <ProviderName>Spaarke</ProviderName>
  <DefaultLocale>en-US</DefaultLocale>
  <DisplayName DefaultValue="Spaarke" />
  <Description DefaultValue="Save and share documents with Spaarke DMS" />
  <Hosts>
    <Host Name="Document" />
  </Hosts>
  <Requirements>
    <Sets>
      <Set Name="WordApi" MinVersion="1.3" />
    </Sets>
  </Requirements>
  <DefaultSettings>
    <SourceLocation DefaultValue="https://{addin-domain}/taskpane.html" />
  </DefaultSettings>
  <Permissions>ReadWriteDocument</Permissions>
</OfficeApp>
```

### Tooling

- Use **Microsoft 365 Agents Toolkit** for Outlook development (unified manifest)
- Use **Yeoman Office Add-in Generator** or manual XML for Word development
- Both manifests share the same task pane codebase

---

## Office.js Requirement Sets

### Outlook Add-in Requirements

| Requirement Set | Min Version | Purpose | Required |
|-----------------|-------------|---------|----------|
| Mailbox | 1.5 | Basic email access | Yes |
| Mailbox | 1.8 | getAttachmentContentAsync (attachment binary) | Yes |
| Mailbox | 1.10 | Event-based activation | No (future) |
| IdentityAPI | 1.3 | SSO fallback (if NAA unavailable) | Recommended |
| DialogAPI | 1.1 | Auth fallback dialog | Recommended |

### Word Add-in Requirements

| Requirement Set | Min Version | Purpose | Required |
|-----------------|-------------|---------|----------|
| WordApi | 1.3 | Document access, content controls | Yes |
| IdentityAPI | 1.3 | SSO fallback (if NAA unavailable) | Recommended |
| DialogAPI | 1.1 | Auth fallback dialog | Recommended |

### Runtime Feature Detection

Always check capability at runtime:
```typescript
if (Office.context.requirements.isSetSupported('Mailbox', '1.8')) {
  // Safe to use getAttachmentContentAsync
} else {
  // Fall back to alternative approach or show error
}
```

---

## Email Content Retrieval (Outlook Read Mode)

### Email Body Retrieval

```typescript
Office.context.mailbox.item.body.getAsync(
  Office.CoercionType.Html,
  (result) => {
    if (result.status === Office.AsyncResultStatus.Succeeded) {
      const htmlBody = result.value;
    }
  }
);
```

### Attachment Metadata

```typescript
const attachments = Office.context.mailbox.item.attachments;
// Each attachment has: id, name, contentType, size, isInline, attachmentType
```

### Attachment Content Retrieval (Requires Mailbox 1.8+)

```typescript
Office.context.mailbox.item.getAttachmentContentAsync(
  attachmentId,
  (result) => {
    if (result.status === Office.AsyncResultStatus.Succeeded) {
      const content = result.value;
      // content.content: base64-encoded string
      // content.format: Office.MailboxEnums.AttachmentContentFormat
    }
  }
);
```

### Large Attachment Handling (V1)

For V1, all attachment retrieval is client-side via `getAttachmentContentAsync()`.

**Size Limits**:
- Maximum single attachment: 25MB
- Maximum total per email: 100MB
- Attachments exceeding limits: Show error, suggest user save attachment locally first

**V1 Constraint**: If attachment exceeds browser memory limits, user must save locally and upload via Spaarke web application.

### Future Consideration: Server-Side Retrieval

> **Out of Scope for V1** - Server-side attachment retrieval requires additional Graph mailbox permissions (`Mail.Read` minimum), admin consent posture changes, and handling of EWS vs REST message ID canonicalization.

If implemented in future versions:
- Add delegated `Mail.Read` scope to BFF app registration
- Define canonical message ID strategy (EWS vs REST conversion)
- Handle ID drift/invalidation scenarios
- BFF would use Graph OBO: `GET /me/messages/{id}/attachments/{id}/$value`

---

## Architecture Overview

### Component Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                    Office Integration Platform                   │
├─────────────────────────────────────────────────────────────────┤
│  ┌─────────────────┐  ┌─────────────────┐                      │
│  │  Outlook Add-in │  │   Word Add-in   │                      │
│  │ (Unified Manifest)│ │  (XML Manifest) │                      │
│  └────────┬────────┘  └────────┬────────┘                      │
│           │                    │                                │
│           ▼                    ▼                                │
│  ┌─────────────────────────────────────────┐                   │
│  │         Shared Task Pane UI             │                   │
│  │  (React + Fluent v9 + Office.js + NAA)  │                   │
│  ├─────────────────────────────────────────┤                   │
│  │  ┌─────────────┐  ┌─────────────────┐   │                   │
│  │  │   Outlook   │  │      Word       │   │                   │
│  │  │   Adapter   │  │     Adapter     │   │                   │
│  │  └─────────────┘  └─────────────────┘   │                   │
│  └─────────────────────────────────────────┘                   │
│                         │                                       │
│                         ▼                                       │
│  ┌─────────────────────────────────────────┐                   │
│  │           API Client Layer              │                   │
│  │    (Typed client for /office/* APIs)    │                   │
│  └─────────────────────────────────────────┘                   │
└─────────────────────────────────────────────────────────────────┘
                          │
                          ▼ HTTPS (NAA Bearer Token)
┌─────────────────────────────────────────────────────────────────┐
│                      Spaarke BFF API                            │
├─────────────────────────────────────────────────────────────────┤
│  ┌─────────────────────────────────────────┐                   │
│  │        /office/* Endpoints              │                   │
│  │  (Minimal API + Endpoint Filters)       │                   │
│  └─────────────────────────────────────────┘                   │
│                         │                                       │
│           ┌─────────────┼─────────────┐                        │
│           ▼             ▼             ▼                        │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐              │
│  │SpeFileStore │ │  UAC/AuthZ  │ │  Dataverse  │              │
│  │  (SPE ops)  │ │  (Access)   │ │  (Records)  │              │
│  └─────────────┘ └─────────────┘ └─────────────┘              │
│                         │                                       │
│                         ▼ Service Bus                          │
│  ┌─────────────────────────────────────────┐                   │
│  │        Background Workers               │                   │
│  │  Upload | Profile | Index | Analyze     │                   │
│  └─────────────────────────────────────────┘                   │
└─────────────────────────────────────────────────────────────────┘
```

### Valid Association Targets

| Entity | Logical Name | Display Name | Quick Create |
|--------|--------------|--------------|--------------|
| Matter | sprk_matter | Matter | Yes |
| Project | sprk_project | Project | Yes |
| Invoice | sprk_invoice | Invoice | Yes |
| Account | account | Account | Yes |
| Contact | contact | Contact | Yes |

### Task Pane State Diagram

```
┌─────────────┐
│  LOADING    │ ← Initial load, NAA auth check
└─────┬───────┘
      │ Auth success
      ▼
┌─────────────┐
│    IDLE     │ ← Ready for user action
└─────┬───────┘
      │ User selects "Save to Spaarke"
      ▼
┌─────────────┐
│  SELECTING  │ ← User picking association target (REQUIRED)
└─────┬───────┘
      │ User clicks "Save" (association selected)
      ▼
┌─────────────┐
│  UPLOADING  │ ← Sending to BFF, showing progress
└─────┬───────┘
      │ Upload complete
      ▼
┌─────────────┐
│ PROCESSING  │ ← SSE/polling job status, showing stages
└─────┬───────┘
      │ Job complete
      ▼
┌─────────────┐
│  COMPLETE   │ ← Show success, links to document
└─────────────┘
      │ User clicks "Done" or starts new save
      ▼
    (back to IDLE)

Error states can occur from UPLOADING or PROCESSING → ERROR
ERROR shows retry option → back to SELECTING or IDLE
```

---

## API Contracts

### POST /office/save

Submit email or document for filing to Spaarke.

**Request Headers**:
```
Authorization: Bearer {NAA token}
Content-Type: application/json
X-Idempotency-Key: {sha256-hash}
X-Correlation-Id: {uuid} (optional, generated if not provided)
```

**Request Body**:
```json
{
  "sourceType": "OutlookEmail | OutlookAttachment | WordDocument",
  "associationType": "Matter | Project | Invoice | Account | Contact",
  "associationId": "guid (required)",
  "content": {
    "emailId": "string (for OutlookEmail)",
    "includeBody": true,
    "attachmentIds": ["id1", "id2"],
    "documentUrl": "string (for WordDocument)",
    "documentName": "string"
  },
  "processing": {
    "profileSummary": true,
    "ragIndex": true,
    "deepAnalysis": false
  },
  "metadata": {
    "description": "string (optional)",
    "tags": ["tag1", "tag2"]
  }
}
```

**Validation Rules**:
- `sourceType`: Required, must be valid enum value
- `associationType`: Required (no "Document Only" option)
- `associationId`: Required, must be valid GUID
- `content.emailId`: Required if sourceType is OutlookEmail
- `content.attachmentIds`: At least one required if saving attachments

**Response (202 Accepted)** - New job created:
```json
{
  "jobId": "guid",
  "documentId": "guid (stub, may change)",
  "statusUrl": "/office/jobs/{jobId}",
  "streamUrl": "/office/jobs/{jobId}/stream",
  "status": "Queued",
  "duplicate": false,
  "correlationId": "uuid"
}
```

**Response (200 OK)** - Duplicate detected:
```json
{
  "jobId": "existing-job-guid",
  "documentId": "existing-document-guid",
  "statusUrl": "/office/jobs/{jobId}",
  "status": "Completed",
  "duplicate": true,
  "message": "This item was previously saved to this association target",
  "correlationId": "uuid"
}
```

### GET /office/jobs/{jobId}

Get job status for polling.

**Response**:
```json
{
  "jobId": "guid",
  "status": "Running",
  "stages": [
    { "name": "RecordsCreated", "status": "Completed" },
    { "name": "FileUploaded", "status": "Completed" },
    { "name": "ProfileSummary", "status": "Running" },
    { "name": "Indexed", "status": "Pending" },
    { "name": "DeepAnalysis", "status": "Skipped" }
  ],
  "documentId": "guid",
  "documentUrl": "https://...",
  "associationUrl": "https://...",
  "errorCode": null,
  "errorMessage": null
}
```

### GET /office/jobs/{jobId}/stream

Server-Sent Events stream for real-time status updates.

**Request**:
```
GET /office/jobs/{jobId}/stream
Accept: text/event-stream
Authorization: Bearer {token}
```

**Event Format**:
```
event: stage-update
data: {"stage":"FileUploaded","status":"Completed","timestamp":"2026-01-20T10:30:00Z"}

event: job-complete
data: {"status":"Completed","documentId":"guid","documentUrl":"https://..."}
```

**Client Implementation (fetch + ReadableStream)**:

Native `EventSource` does not support custom headers including `Authorization`. Use fetch with ReadableStream for SSE with bearer auth:

```typescript
async function subscribeToJobStatus(jobId: string, token: string): Promise<void> {
  const response = await fetch(`/office/jobs/${jobId}/stream`, {
    headers: {
      'Accept': 'text/event-stream',
      'Authorization': `Bearer ${token}`
    }
  });

  if (!response.ok) {
    throw new Error(`SSE connection failed: ${response.status}`);
  }

  const reader = response.body!.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });
    const lines = buffer.split('\n');
    buffer = lines.pop() || ''; // Keep incomplete line in buffer

    for (const line of lines) {
      if (line.startsWith('event: ')) {
        const eventType = line.slice(7);
        // Next line should be data
      } else if (line.startsWith('data: ')) {
        const data = JSON.parse(line.slice(6));
        handleSSEEvent(data);
      }
    }
  }
}

function handleSSEEvent(data: { stage?: string; status: string; documentId?: string }) {
  if (data.stage) {
    updateStageUI(data.stage, data.status);
  }
  if (data.status === 'Completed' || data.status === 'Failed') {
    showCompletion(data);
  }
}
```

**Fallback**: If SSE connection fails, fall back to polling via `GET /office/jobs/{jobId}` at 3-second intervals.

### GET /office/search/entities

Search for association targets.

**Request**:
```
GET /office/search/entities?q=acme&type=Matter,Account&limit=20
```

**Query Parameters**:
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| q | string | Yes | Search term (min 2 chars) |
| type | string | No | Comma-separated entity types (default: all) |
| limit | number | No | Max results (default: 20, max: 50) |

**Response**:
```json
{
  "results": [
    {
      "id": "guid",
      "entityType": "Matter",
      "logicalName": "sprk_matter",
      "name": "Smith vs Jones",
      "displayInfo": "Client: Acme Corp | Status: Active",
      "iconUrl": "/icons/matter.svg"
    },
    {
      "id": "guid",
      "entityType": "Account",
      "logicalName": "account",
      "name": "Acme Corporation",
      "displayInfo": "Industry: Manufacturing | City: Chicago",
      "iconUrl": "/icons/account.svg"
    }
  ],
  "totalCount": 42,
  "hasMore": true
}
```

### POST /office/quickcreate/{entityType}

Create a new entity with minimal fields.

**Valid entityType values**: `matter`, `project`, `invoice`, `account`, `contact`

**Request (Matter/Project/Invoice)**:
```json
{
  "name": "string (required)",
  "description": "string (optional)",
  "clientId": "guid (Account lookup, optional)"
}
```

**Request (Account)**:
```json
{
  "name": "string (required)",
  "description": "string (optional)",
  "industry": "string (optional)",
  "city": "string (optional)"
}
```

**Request (Contact)**:
```json
{
  "firstName": "string (required)",
  "lastName": "string (required)",
  "email": "string (optional)",
  "accountId": "guid (Account lookup, optional)"
}
```

**Response (201 Created)**:
```json
{
  "id": "guid",
  "entityType": "Matter",
  "logicalName": "sprk_matter",
  "name": "Smith vs Jones",
  "url": "https://org.crm.dynamics.com/main.aspx?..."
}
```

**Extensibility**: Field sets are code-defined in `QuickCreateFieldsProvider`. Add fields by modifying provider, not configuration.

### POST /office/share/links

Generate share links for documents.

**Request**:
```json
{
  "documentIds": ["guid1", "guid2"],
  "recipients": ["email1@example.com", "email2@external.com"],
  "grantAccess": true,
  "role": "ViewOnly | Download"
}
```

**Response**:
```json
{
  "links": [
    { "documentId": "guid1", "url": "https://...", "title": "Document.docx" }
  ],
  "invitations": [
    { "email": "email2@external.com", "status": "Created", "invitationId": "guid" }
  ]
}
```

### POST /office/share/attach

Get document content for attachment to Outlook compose.

**Request**:
```json
{
  "documentIds": ["guid1", "guid2"]
}
```

**Response**:
```json
{
  "attachments": [
    {
      "documentId": "guid1",
      "filename": "Contract.docx",
      "contentType": "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
      "size": 245678,
      "downloadUrl": "https://{bff}/office/share/attach/{token}",
      "urlExpiry": "2026-01-20T11:00:00Z"
    }
  ]
}
```

**Attachment Delivery Mechanism**:

| Method | Description | Use Case |
|--------|-------------|----------|
| **URL-based (Primary)** | Pre-signed URL with short TTL | Default for all files |
| **Base64 (Fallback)** | Base64-encoded content in response | Small files < 1MB if URL fails |

**URL-based Attachment Flow**:
1. Client calls `POST /office/share/attach` with document IDs
2. BFF validates user access to each document
3. BFF generates pre-signed download URL with 5-minute TTL
4. Client calls `Office.context.mailbox.item.addFileAttachmentAsync(url, filename)`
5. Outlook fetches content from URL and attaches

**Security Constraints**:
- Download URL contains cryptographic token (not document ID)
- Token TTL: 5 minutes (single use)
- Token bound to requesting user
- Response headers: `Cache-Control: no-store, no-cache`
- Content-Disposition: `attachment; filename="{filename}"`

**Size Limits**:
- Subject to Outlook attachment limits (typically 25MB per file)
- Large files (>25MB): Show warning, suggest sharing via link instead

### GET /office/recent

Get recent items for quick access.

**Response**:
```json
{
  "recentAssociations": [
    { "id": "guid", "entityType": "Matter", "name": "Smith vs Jones", "lastUsed": "..." }
  ],
  "recentDocuments": [...],
  "favorites": [...]
}
```

---

## Idempotency Specification

### Key Format

```
idempotencyKey = SHA256(canonicalPayload)
```

### Canonical Payload Structure (Outlook Email)

```json
{
  "sourceType": "OutlookEmail",
  "emailId": "AAMkAGI2...",
  "attachmentIds": ["ATT001", "ATT002"],
  "associationType": "Matter",
  "associationId": "guid",
  "includeBody": true
}
```

### Server Handling

1. Compute SHA256 of canonical JSON (sorted keys, no whitespace)
2. Check Redis for existing mapping: `idempotency:{hash}` → `{jobId, documentId}`
3. If exists: Return existing job/document info with `duplicate: true`
4. If not: Create new job, store mapping with 24h TTL

### Client Responsibility

- Compute idempotencyKey before calling API
- Include in header: `X-Idempotency-Key: {hash}`

---

## Error Handling

### ProblemDetails Format

All API errors return RFC 7807 ProblemDetails:

```json
{
  "type": "https://spaarke.com/errors/office/validation-error",
  "title": "Validation Error",
  "status": 400,
  "detail": "Association target is required",
  "instance": "/office/save",
  "correlationId": "abc-123-def",
  "errorCode": "OFFICE_003",
  "errors": {
    "associationType": ["Association type is required"],
    "associationId": ["Association ID is required"]
  }
}
```

### Error Code Catalog

| Code | Type | Status | Title | When Used |
|------|------|--------|-------|-----------|
| OFFICE_001 | validation-error | 400 | Invalid source type | sourceType not recognized |
| OFFICE_002 | validation-error | 400 | Invalid association type | associationType not recognized |
| OFFICE_003 | validation-error | 400 | Association required | associationId missing |
| OFFICE_004 | validation-error | 400 | Attachment too large | Single file > 25MB |
| OFFICE_005 | validation-error | 400 | Total size exceeded | Combined > 100MB |
| OFFICE_006 | validation-error | 400 | Blocked file type | Dangerous extension |
| OFFICE_007 | not-found | 404 | Association target not found | Entity doesn't exist |
| OFFICE_008 | not-found | 404 | Job not found | Job ID invalid or expired |
| OFFICE_009 | forbidden | 403 | Access denied | User lacks permission |
| OFFICE_010 | forbidden | 403 | Cannot create entity | User lacks create permission |
| OFFICE_011 | conflict | 409 | Document already exists | Duplicate detected |
| OFFICE_012 | service-error | 502 | SPE upload failed | SharePoint Embedded error |
| OFFICE_013 | service-error | 502 | Graph API error | Microsoft Graph failure |
| OFFICE_014 | service-error | 502 | Dataverse error | Dataverse operation failed |
| OFFICE_015 | unavailable | 503 | Processing unavailable | Workers offline |

---

## Rate Limiting

| Endpoint Pattern | Limit | Window | Scope |
|------------------|-------|--------|-------|
| POST /office/save | 10 | 1 minute | Per user |
| POST /office/quickcreate/* | 5 | 1 minute | Per user |
| GET /office/search/* | 30 | 1 minute | Per user |
| GET /office/jobs/* | 60 | 1 minute | Per user |
| POST /office/share/* | 20 | 1 minute | Per user |

**Response when limited**:
- Status: 429 Too Many Requests
- Header: `Retry-After: {seconds}`
- Body: ProblemDetails with type `/rate-limited`

---

## File Type Handling

### Blocked File Types

Dangerous/executable files that cannot be saved:
```
.exe, .dll, .bat, .cmd, .ps1, .vbs, .js, .jar, .msi, .scr, .com, .pif, .reg
```

### Validation

1. Check file extension against blocked list
2. Verify MIME type matches extension
3. For Office documents, validate file signature (magic bytes)

---

## Dataverse Schema

### sprk_emailartifact Table

| Column | Type | Description |
|--------|------|-------------|
| sprk_emailartifactid | GUID | Primary key |
| sprk_document | Lookup(sprk_document) | Associated Document |
| sprk_outlookmessageid | String(500) | Outlook message ID (indexed) |
| sprk_internetmessageid | String(500) | Internet message ID (RFC 2822) |
| sprk_conversationid | String(200) | Conversation thread ID |
| sprk_subject | String(500) | Email subject |
| sprk_sender | String(320) | Sender email |
| sprk_sendername | String(200) | Sender display name |
| sprk_recipients | Memo | JSON: { to: [], cc: [], bcc: [] } |
| sprk_sentdate | DateTime | Email sent timestamp |
| sprk_receiveddate | DateTime | Email received timestamp |
| sprk_bodypreview | String(500) | First 500 chars |
| sprk_hasattachments | Boolean | Has attachments flag |
| sprk_importance | OptionSet | Low=0, Normal=1, High=2 |

### sprk_attachmentartifact Table

| Column | Type | Description |
|--------|------|-------------|
| sprk_attachmentartifactid | GUID | Primary key |
| sprk_emailartifact | Lookup(sprk_emailartifact) | Source email |
| sprk_document | Lookup(sprk_document) | Created Document |
| sprk_outlookattachmentid | String(200) | Outlook attachment ID (indexed) |
| sprk_originalfilename | String(256) | Original filename |
| sprk_contenttype | String(100) | MIME type |
| sprk_size | Integer | Size in bytes |
| sprk_isinline | Boolean | Inline attachment flag |

### sprk_processingjob Table (Per ADR-004)

| Column | Type | Description |
|--------|------|-------------|
| sprk_processingjobid | GUID | Primary key |
| sprk_jobtype | OptionSet | EmailSave=1, AttachmentSave=2, DocumentSave=3, DocumentVersion=4 |
| sprk_status | OptionSet | Queued=1, Running=2, Completed=3, Failed=4, PartialSuccess=5, NeedsAttention=6 |
| sprk_subjectid | String(100) | Document ID (once known) |
| sprk_associationtype | OptionSet | Matter=1, Project=2, Invoice=3, Account=4, Contact=5 |
| sprk_associationid | String(50) | Association target GUID |
| sprk_correlationid | String(50) | Distributed tracing ID (indexed) |
| sprk_idempotencykey | String(256) | SHA256 dedup key (indexed) |
| sprk_queuemessageid | String(100) | Service Bus message ID |
| sprk_stagestatuses | Memo | JSON: { RecordsCreated: "Completed", ... } |
| sprk_attempt | Integer | Current attempt (1-based) |
| sprk_maxattempts | Integer | Max retries (default: 3) |
| sprk_createdon | DateTime | Job created |
| sprk_startedon | DateTime | Processing started |
| sprk_completedon | DateTime | Processing completed |
| sprk_errorcode | String(20) | Error code if failed |
| sprk_errormessage | Memo | Error details |
| sprk_createdby | Lookup(SystemUser) | Initiating user |

### Dataverse Indexes

| Table | Index Name | Columns | Purpose |
|-------|------------|---------|---------|
| sprk_emailartifact | idx_email_messageid | sprk_outlookmessageid | Duplicate detection |
| sprk_emailartifact | idx_email_document | sprk_document | Find email for document |
| sprk_attachmentartifact | idx_attach_outlookid | sprk_outlookattachmentid | Duplicate detection |
| sprk_attachmentartifact | idx_attach_email | sprk_emailartifact | Find attachments for email |
| sprk_processingjob | idx_job_idempotency | sprk_idempotencykey | Fast duplicate lookup |
| sprk_processingjob | idx_job_status | sprk_status, sprk_createdon | Queue queries |
| sprk_processingjob | idx_job_correlation | sprk_correlationid | Distributed tracing |

---

## Accessibility Requirements (WCAG 2.1 AA)

### Keyboard Navigation
- All interactive elements focusable via Tab
- Enter/Space activates buttons
- Escape closes dialogs/dropdowns
- Arrow keys navigate lists
- Focus trap in modal dialogs

### Screen Reader Support
- All images have alt text
- Form fields have associated labels
- Status updates announced via aria-live regions
- Error messages associated with fields via aria-describedby

### Visual Requirements
- Minimum contrast ratio 4.5:1 for text
- Focus indicators visible (not just color change)
- No information conveyed by color alone
- Support for Windows High Contrast mode

---

## Telemetry Events

| Event Name | Properties | Purpose |
|------------|------------|---------|
| office.addin.initialized | host, hostVersion, addinVersion, platform | Track add-in loads |
| office.save.started | sourceType, associationType, attachmentCount, totalSize | Track save initiations |
| office.save.completed | jobId, durationMs, status, documentId | Track completions |
| office.save.failed | jobId, errorCode, errorMessage, correlationId | Track failures |
| office.search.executed | entityTypes, queryLength, resultCount, durationMs | Track search usage |
| office.quickcreate.executed | entityType, durationMs, success | Track quick create |
| office.share.executed | documentCount, recipientCount, grantAccess | Track sharing |
| office.auth.completed | method (NAA/Dialog), durationMs | Track auth performance |
| office.auth.failed | method, errorCode | Track auth failures |

---

## Success Criteria

| # | Criterion | Verification Method |
|---|-----------|---------------------|
| 1 | Outlook add-in installs and loads in New Outlook and Outlook Web | Manual test in both clients |
| 2 | Word add-in installs and loads in Word Desktop and Word Web | Manual test in both clients |
| 3 | NAA authentication works silently | Auth flow test |
| 4 | User can save email with attachments to a Matter | E2E test with job completion |
| 5 | User can save email to Account or Contact | E2E test |
| 6 | User can create Matter/Account/Contact inline with Quick Create | E2E test with entity verification |
| 7 | Job status updates via SSE within 1 second of change | Timing test |
| 8 | Duplicate email returns existing document | Unit test with same idempotency key |
| 9 | Save without association target returns OFFICE_003 error | Negative test |
| 10 | User can insert document links into compose | E2E test with link verification |
| 11 | All endpoints return ProblemDetails on error | Integration tests |
| 12 | Dark mode displays correctly | Visual test with dark theme |
| 13 | Keyboard navigation works in task pane | Accessibility audit |

---

## Dependencies

### Prerequisites

| Dependency | Status | Notes |
|------------|--------|-------|
| Matter, Project, Invoice entities | ✅ Exist | Use existing |
| Account, Contact entities | ✅ Exist | Standard Dataverse |
| Document entity | ✅ Exists | Use existing |
| SpeFileStore | ✅ Exists | Use existing facade |
| UAC module | ✅ Exists | Use for authorization |
| Service Bus | ✅ Exists | Use for job queuing |

### External Dependencies

| Dependency | Purpose | Notes |
|------------|---------|-------|
| Office.js | Add-in API | Include via CDN |
| MSAL.js 3.x | NAA authentication | NPM package |
| Microsoft 365 Admin | Deployment | Unified manifest deployment |
| Azure AD App Registrations | Auth | Two registrations (add-in + BFF) |

### Cross-Project Dependencies

| Artifact | Provider | Status |
|----------|----------|--------|
| ExternalUser, Invitation, AccessGrant | SDAP-external-portal | Stub until available |
| POST /external/invitations | SDAP-external-portal | Stub until available |

---

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Classic Outlook | Is Classic Outlook in scope? | Out of scope for V1 | Target New Outlook + Web only |
| Existing entities | Do Matter/Project/Invoice exist? | Yes, already exist | Use existing schema |
| Quick Create fields (M/P/I) | What fields? | Name, Description, Client | Code-based extensibility |
| Quick Create fields (Account) | What fields? | Name, Description, Industry, City | May refine later |
| Quick Create fields (Contact) | What fields? | FirstName, LastName, Email, AccountId | May refine later |
| Job updates | How often? | SSE primary, 3s polling fallback | Real-time preferred |
| Duplicate handling | What happens? | Return existing + notify user | Idempotency key based |
| Attachment limits | Max size? | 25MB per file, 100MB total | Code-configurable |
| Association requirement | Can save without? | No - must select target | Mandatory association |
| Open from Spaarke | In Word scope? | No - users open from Spaarke app | Removed from scope |
| Word manifest | Unified or XML? | XML (unified is preview for Word) | XML manifest required for Word production |
| Outlook manifest | Unified or XML? | Unified (GA for Outlook) | Unified manifest for Outlook |
| SSE auth | How to auth SSE? | fetch + ReadableStream with bearer | Native EventSource doesn't support headers |
| Server-side attachment | In V1 scope? | No - client-side only via Office.js | No mailbox Graph permissions for V1 |
| Multi-association | Multiple targets per doc? | V1: exactly one; future: link table | Single primary association for V1 |

---

## Test Plan Overview

### Client Matrix

| Client | Platform | In Scope |
|--------|----------|----------|
| New Outlook | Windows/Mac | ✅ Yes |
| Outlook Web | Browser | ✅ Yes |
| Classic Outlook | Windows | ❌ No |
| Word Desktop | Windows/Mac | ✅ Yes |
| Word Web | Browser | ✅ Yes |

### Test Categories

1. **Unit Tests**: API endpoints, workers, adapters
2. **Integration Tests**: Full save/share flows, NAA auth
3. **E2E Tests**: Manual testing in actual Office clients
4. **Accessibility Tests**: Keyboard nav, screen reader, contrast
5. **Performance Tests**: Response times, SSE reliability

---

## Reference Links

- [NAA Documentation](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/enable-nested-app-authentication-in-your-add-in)
- [Unified Manifest Overview](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/unified-manifest-overview)
- [Mailbox Requirement Sets](https://learn.microsoft.com/en-us/javascript/api/requirement-sets/outlook/outlook-api-requirement-sets)
- [Office Add-ins at Build 2025](https://devblogs.microsoft.com/microsoft365dev/office-addins-at-build-2025/)

---

*AI-optimized specification. Original design: design.md. Revised per SPEC-REVIEW-AND-REVISIONS.md and additional technical feedback (manifest strategy, SSE auth, attachment handling).*
