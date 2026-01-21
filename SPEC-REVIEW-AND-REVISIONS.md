# SDAP Office Integration Spec Review and Required Revisions

**Document Purpose**: Comprehensive review of spec.md with required corrections and enhancements for Claude Code to implement.

**Review Date**: 2026-01-20  
**Reviewer**: Senior Microsoft Developer Review  
**Source Documents**: spec.md, design.md, Spaarke ADRs, Microsoft Office Add-in documentation (January 2026)

---

## Executive Summary

The spec.md provides a solid foundation but requires **significant revisions** before implementation. This document identifies issues in three categories:

| Category | Count | Impact |
|----------|-------|--------|
| 🔴 Critical (Must Fix) | 7 | Blocks successful implementation |
| 🟡 High Priority (Should Fix) | 8 | Causes defects or inconsistencies |
| 🟢 Medium Priority (Consider) | 6 | Improves quality and maintainability |

**Key Issues**:
1. Authentication model is outdated (must use NAA, not legacy tokens)
2. "Workspace" concept introduced incorrectly (does not exist in Spaarke)
3. Data model misunderstands Document/File relationship
4. Word "Open from Spaarke" feature should be removed
5. Missing Office.js requirement sets and API specifications
6. Manifest strategy contradicts latest Microsoft guidance

---

## Part 1: Conceptual Corrections

### 1.1 Remove "Workspace" Concept Entirely

**Problem**: The spec introduces "workspace" as a concept throughout, but this does not exist in Spaarke.

**Spaarke Entity Model**:
- **Document**: Canonical entity representing a managed document in Spaarke
- **File**: The actual binary (docx, pdf, xlsx, etc.) stored in SharePoint Embedded, always associated 1:1 with a Document
- **Association Targets**: Matter, Project, Invoice, Account, Contact - existing entities that Documents can be associated with

**Current Spec Errors**:
- Line 467-478: `DocumentWorkspaceLink` table - DELETE ENTIRELY
- Line 391-403: `/office/search/workspaces` endpoint - RENAME
- Throughout: Replace "workspace" terminology with "association target" or explicit entity names

**Correction**: A Document is associated to ONE of: Matter, Project, Invoice, Account, or Contact via lookup fields on the Document entity itself. No separate link table is needed.

### 1.2 Correct Document/File Relationship

**Problem**: Spec conflates "document" (the file being worked on) with "Document" (the Spaarke entity).

**Clarified Model**:
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
│  - sprk_filesize                                                │
│  - sprk_contenttype (MIME type)                                 │
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
│                                                                 │
│  Actual binary file stored here                                 │
│  - Accessed via Microsoft Graph API                             │
│  - Mediated through SpeFileStore facade                         │
└─────────────────────────────────────────────────────────────────┘
```

**Key Rules**:
1. A File CANNOT exist without an associated Document (Document is always created first or simultaneously)
2. Document:File is 1:1 (one Document = one File in SPE)
3. A Document MUST be associated to exactly one of: Matter, Project, Invoice, Account, or Contact
4. "Document Only" (no association) is NOT allowed in current model

### 1.3 Remove "Open from Spaarke" Feature (Word)

**Problem**: FR-11 specifies "User can search and open documents from Spaarke into Word"

**Resolution**: Remove this feature entirely. Users will open documents from within the Spaarke application (via Document entity forms or PCF grids), not from within Word.

**Revised Word Add-in Scope**:
- ✅ Save to Spaarke (create Document + upload File + associate to entity)
- ✅ Save new version (update existing Document's File in SPE)
- ✅ Share / Insert link / Attach copy
- ✅ Grant access (stub for External Portal)
- ❌ ~~Open from Spaarke~~ - REMOVED

---

## Part 2: Critical Issues (🔴 Must Fix)

### 2.1 Authentication Model Must Use NAA

**Problem**: The spec references "MSAL authentication" and "OBO token exchange" without specifying Nested App Authentication (NAA), which is now required.

**Background**:
- Legacy Exchange tokens (`getCallbackTokenAsync`, `getUserIdentityTokenAsync`) were turned off across all Microsoft 365 tenants by August 2025
- NAA is the Microsoft-recommended authentication pattern for Office Add-ins as of 2025
- NAA provides direct Graph API access from client code without requiring a middle-tier server for token exchange

**Current Spec Gap** (Line 549):
```
| Auth flow | OBO token exchange works for Graph/SPE ops | API auth implementation |
```

**Required Revision**: Replace authentication assumptions with explicit NAA specification:

```markdown
### Authentication Architecture

#### Client-Side Authentication (Task Pane)

**Primary: Nested App Authentication (NAA)**
- Use MSAL.js 3.x with `createNestablePublicClientApplication()`
- NAA provides SSO without requiring Office.js `getAccessToken()`
- Acquire tokens directly for:
  - Microsoft Graph API (for user info, if needed)
  - Spaarke BFF API (custom scope)

**Configuration**:
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
```

**Token Acquisition Pattern**:
```typescript
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
- For older Outlook clients that don't support NAA
- Use Office Dialog API to open authentication popup
- Reference: Microsoft sample Outlook-Add-in-SSO-NAA-IE

#### Server-Side Authentication (BFF API)

**Token Validation**:
- Validate Azure AD tokens from NAA (NOT legacy Exchange tokens)
- Validate audience matches Spaarke BFF API app registration
- Extract user claims for authorization decisions

**OBO Flow (When Needed)**:
- Use OBO only when BFF needs to call Graph/SPE on user's behalf
- BFF has its own app registration with client secret
- Exchange user's token for Graph token via OBO

**App-Only Operations (Workers)**:
- Use client credentials flow for background processing
- No user context - service principal identity

#### App Registration Requirements

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
```

### 2.2 Manifest Strategy Should Use Unified Manifest

**Problem**: Line 144 states "MUST use XML manifest (not unified) for Word + Outlook stability"

**Current State (January 2026)**:
- Unified manifest is **GA for Outlook** (production-ready since 2024)
- Unified manifest is **GA for Word, Excel, PowerPoint** (released at Build 2025)
- Microsoft is pushing unified manifest for all new development
- Unified manifest enables future Copilot agent integration

**Required Revision**:

```markdown
### Manifest Strategy

#### Primary: Unified Manifest for Microsoft 365 (Recommended)

Use the unified JSON manifest format for both Outlook and Word add-ins:
- Production-ready for all target hosts (Outlook, Word)
- Single app model across Microsoft 365
- Future-proof for Copilot agent integration
- Simplified deployment via Microsoft 365 admin center

**Manifest Location**: `/src/client/office-addins/manifest.json`

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
        "capabilities": [{ "name": "Mailbox", "minVersion": "1.5" }]
      },
      "runtimes": [...],
      "ribbons": [...]
    },
    {
      "requirements": {
        "scopes": ["document"],
        "capabilities": [{ "name": "WordApi", "minVersion": "1.3" }]
      },
      "runtimes": [...],
      "ribbons": [...]
    }
  ]
}
```

#### Fallback: XML Add-in Only Manifest

For platforms that don't directly support unified manifest:
- Microsoft 365 admin center auto-generates XML from unified manifest
- No manual XML maintenance required
- Covers older Office versions

#### Tooling

- Use **Microsoft 365 Agents Toolkit** for development
- Supports unified manifest creation and debugging
- Easy conversion from existing XML manifests
```

### 2.3 Add Office.js Requirement Sets Specification

**Problem**: The spec doesn't specify which Office.js API requirement sets are needed.

**Impact**: Without this, Claude Code may use APIs unavailable on target platforms.

**Required Addition**:

```markdown
### Office.js Requirement Sets

#### Outlook Add-in Requirements

| Requirement Set | Min Version | Purpose | Required |
|-----------------|-------------|---------|----------|
| Mailbox | 1.5 | Basic email access, getCallbackTokenAsync | Yes |
| Mailbox | 1.8 | getAttachmentContentAsync (attachment binary) | Yes |
| Mailbox | 1.10 | Event-based activation | No (future) |
| IdentityAPI | 1.3 | SSO fallback (if NAA unavailable) | Recommended |
| DialogAPI | 1.1 | Auth fallback dialog | Recommended |

#### Word Add-in Requirements

| Requirement Set | Min Version | Purpose | Required |
|-----------------|-------------|---------|----------|
| WordApi | 1.3 | Document access, content controls | Yes |
| IdentityAPI | 1.3 | SSO fallback (if NAA unavailable) | Recommended |
| DialogAPI | 1.1 | Auth fallback dialog | Recommended |

#### Manifest Requirements Declaration

**Unified Manifest**:
```json
"extensions": [{
  "requirements": {
    "scopes": ["mail"],
    "capabilities": [
      { "name": "Mailbox", "minVersion": "1.8" }
    ]
  }
}]
```

**XML Manifest** (if needed for fallback):
```xml
<Requirements>
  <Sets>
    <Set Name="Mailbox" MinVersion="1.8"/>
  </Sets>
</Requirements>
```

#### Runtime Feature Detection

Always check capability at runtime before using:
```typescript
if (Office.context.requirements.isSetSupported('Mailbox', '1.8')) {
  // Safe to use getAttachmentContentAsync
} else {
  // Fall back to alternative approach or show error
}
```
```

### 2.4 Add Email/Attachment Content Retrieval Specification

**Problem**: FR-01 says "save email body + selected attachments" but doesn't specify the Office.js APIs.

**Required Addition**:

```markdown
### Email Content Retrieval (Outlook Read Mode)

#### Email Body Retrieval

```typescript
// Get email body as HTML
Office.context.mailbox.item.body.getAsync(
  Office.CoercionType.Html,
  (result) => {
    if (result.status === Office.AsyncResultStatus.Succeeded) {
      const htmlBody = result.value;
      // Process HTML body
    }
  }
);
```

**Notes**:
- Use `Office.CoercionType.Html` for rich formatting
- Use `Office.CoercionType.Text` for plain text
- Body may be large - consider size limits

#### Attachment Metadata

```typescript
// Get attachment list (metadata only, no content)
const attachments = Office.context.mailbox.item.attachments;

// Each attachment has:
// - id: string (Outlook attachment ID)
// - name: string (filename)
// - contentType: string (MIME type)
// - size: number (bytes)
// - isInline: boolean
// - attachmentType: Office.MailboxEnums.AttachmentType
```

#### Attachment Content Retrieval (Requires Mailbox 1.8+)

```typescript
// Get attachment binary content
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

**Size Limits**:
- Individual attachment: 25 MB (Exchange constraint)
- Total per email: 100 MB (configurable in code)
- Large attachments: Consider chunked upload to BFF API

#### Alternative: Graph API via BFF (for large attachments)

If attachment is too large for client-side handling:
1. Send attachment ID to BFF API
2. BFF uses Graph API with OBO token: `GET /me/messages/{id}/attachments/{id}/$value`
3. BFF streams directly to SPE

```typescript
// Client sends metadata only
const response = await fetch('/office/save', {
  method: 'POST',
  body: JSON.stringify({
    sourceType: 'OutlookEmail',
    emailId: Office.context.mailbox.item.itemId,
    attachmentIds: ['ATT001', 'ATT002'],
    useServerSideRetrieval: true  // BFF fetches via Graph
  })
});
```
```

### 2.5 Correct Association Model (No "Document Only" Option)

**Problem**: The current spec implies documents can exist without association.

**Clarification from Product Owner**: A Document MUST be associated to exactly one of: Matter, Project, Invoice, Account, or Contact. "Document Only" is not allowed.

**Required Revision**:

```markdown
### Document Association Rules

1. **Mandatory Association**: Every Document MUST be associated to exactly ONE of:
   - Matter (sprk_matter)
   - Project (sprk_project)
   - Invoice (sprk_invoice)
   - Account (account)
   - Contact (contact)

2. **No Orphan Documents**: "Save as Document Only" is NOT supported. User must select an association target.

3. **Single Association**: A Document can only be associated to ONE entity (not multiple). The association lookup fields on sprk_document are mutually exclusive.

4. **Validation**: BFF API must reject save requests without a valid associationType and associationId.

### Task Pane UX Implication

The "Save to Spaarke" dialog MUST require association selection:
- No "Document Only" or "Save without association" option
- User must search/select or create a Matter, Project, Invoice, Account, or Contact
- Submit button disabled until valid association selected
```

### 2.6 Include Account and Contact as Association Targets

**Problem**: design.md and original spec only mention Matter/Project/Invoice, but ARCHITECTURE.md shows Account and Contact are also valid.

**Clarification from Product Owner**: Include Account and Contact as valid association targets.

**Required Revision**:

Update all references to association targets:

```markdown
### Valid Association Targets

| Entity | Logical Name | Display Name | Quick Create |
|--------|--------------|--------------|--------------|
| Matter | sprk_matter | Matter | Yes |
| Project | sprk_project | Project | Yes |
| Invoice | sprk_invoice | Invoice | Yes |
| Account | account | Account | Yes |
| Contact | contact | Contact | Yes |

### API Changes

**POST /office/save**:
```json
{
  "associationType": "Matter | Project | Invoice | Account | Contact",
  "associationId": "guid (required)"
}
```

**GET /office/search/entities**:
```
?type=Matter|Project|Invoice|Account|Contact
```

**POST /office/quickcreate/{entityType}**:
- Valid entityType values: `matter`, `project`, `invoice`, `account`, `contact`
```

### 2.7 Add Referenced ADRs (ADR-017, ADR-019)

**Problem**: Lines 128-129 reference ADR-017 and ADR-019 which don't exist.

**Required Action**: Either create these ADRs or inline the requirements. Recommended: inline in spec since these are straightforward patterns.

**Required Addition**:

```markdown
### Job Status Pattern (replaces ADR-017 reference)

All async operations follow this pattern:

1. **Initiation**: POST endpoint returns `202 Accepted` with:
   ```json
   {
     "jobId": "guid",
     "statusUrl": "/office/jobs/{jobId}",
     "status": "Queued"
   }
   ```

2. **Polling**: Client polls GET /office/jobs/{jobId} for status updates

3. **Completion**: Final status is one of:
   - `Completed` - all stages successful
   - `Failed` - unrecoverable error
   - `PartialSuccess` - some stages failed (e.g., indexing failed but document saved)
   - `NeedsAttention` - requires user action

4. **Idempotency**: Repeated POST with same idempotencyKey returns existing jobId

### ProblemDetails Error Format (replaces ADR-019 reference)

All API errors return RFC 7807 ProblemDetails:

```json
{
  "type": "https://spaarke.com/errors/office/validation-error",
  "title": "Validation Error",
  "status": 400,
  "detail": "Association target is required",
  "instance": "/office/save",
  "correlationId": "abc-123-def",
  "errors": {
    "associationType": ["Association type is required"],
    "associationId": ["Association ID is required when type is specified"]
  }
}
```

**Standard Error Types**:
| Type Suffix | Status | Usage |
|-------------|--------|-------|
| /validation-error | 400 | Invalid request data |
| /not-found | 404 | Entity not found |
| /forbidden | 403 | Access denied |
| /conflict | 409 | Duplicate/conflict |
| /service-error | 502 | Downstream service failure |
| /unavailable | 503 | Service temporarily unavailable |
```

---

## Part 3: High Priority Issues (🟡 Should Fix)

### 3.1 Define Idempotency Key Format

**Problem**: NFR-05 mentions idempotency but format is ambiguous.

**Required Addition**:

```markdown
### Idempotency Key Specification

#### Format
```
idempotencyKey = SHA256(canonicalPayload)
```

#### Canonical Payload Structure (Outlook Email)
```json
{
  "sourceType": "OutlookEmail",
  "emailId": "AAMkAGI2...",
  "attachmentIds": ["ATT001", "ATT002"],  // Sorted alphabetically
  "associationType": "Matter",
  "associationId": "guid",
  "includeBody": true
}
```

#### Canonical Payload Structure (Word Document)
```json
{
  "sourceType": "WordDocument",
  "documentUrl": "https://...",
  "associationType": "Project",
  "associationId": "guid"
}
```

#### Server Handling
1. Compute SHA256 of canonical JSON (sorted keys, no whitespace)
2. Check Redis for existing mapping: `idempotency:{hash}` → `{jobId, documentId}`
3. If exists: Return existing job/document info with `duplicate: true`
4. If not: Create new job, store mapping with 24h TTL

#### Client Responsibility
- Client computes idempotencyKey before calling API
- Include in request header: `X-Idempotency-Key: {hash}`
- Also include in request body for transparency
```

### 3.2 Add Error Code Catalog

**Problem**: No standardized error codes defined.

**Required Addition**:

```markdown
### Error Code Catalog

| Code | Type | Status | Title | When Used |
|------|------|--------|-------|-----------|
| OFFICE_001 | validation-error | 400 | Invalid source type | sourceType not recognized |
| OFFICE_002 | validation-error | 400 | Invalid association type | associationType not recognized |
| OFFICE_003 | validation-error | 400 | Association required | associationId missing |
| OFFICE_004 | validation-error | 400 | Attachment too large | Single file > 25MB |
| OFFICE_005 | validation-error | 400 | Total size exceeded | Combined > 100MB |
| OFFICE_006 | validation-error | 400 | Blocked file type | Dangerous extension |
| OFFICE_007 | not-found | 404 | Association target not found | Matter/Project/etc. doesn't exist |
| OFFICE_008 | not-found | 404 | Job not found | Job ID invalid or expired |
| OFFICE_009 | forbidden | 403 | Access denied | User lacks permission to association target |
| OFFICE_010 | forbidden | 403 | Cannot create entity | User lacks create permission |
| OFFICE_011 | conflict | 409 | Document already exists | Duplicate detected (return existing) |
| OFFICE_012 | service-error | 502 | SPE upload failed | SharePoint Embedded error |
| OFFICE_013 | service-error | 502 | Graph API error | Microsoft Graph failure |
| OFFICE_014 | service-error | 502 | Dataverse error | Dataverse operation failed |
| OFFICE_015 | unavailable | 503 | Processing unavailable | Workers offline |

Each error response includes:
- `errorCode`: The code from this table
- `correlationId`: Request trace ID
- `detail`: Human-readable specific message
```

### 3.3 Add ProcessingJob ADR-004 Compliance Fields

**Problem**: ProcessingJob table missing fields required by ADR-004.

**Required Revision** to ProcessingJob schema:

```markdown
### ProcessingJob Table (Complete per ADR-004)

| Column | Type | Description |
|--------|------|-------------|
| sprk_processingjobid | GUID | Primary key |
| sprk_jobtype | OptionSet | EmailSave=1, DocumentSave=2, DocumentVersion=3 |
| sprk_status | OptionSet | Queued=1, Running=2, Completed=3, Failed=4, PartialSuccess=5, NeedsAttention=6 |
| sprk_subjectid | String(100) | Related document ID (once created) |
| sprk_correlationid | String(50) | Correlation ID for distributed tracing |
| sprk_idempotencykey | String(256) | SHA256 hash for deduplication (indexed) |
| sprk_queuemessageid | String(100) | Service Bus message ID |
| sprk_stagestatuses | Memo | JSON object: { stageName: status } |
| sprk_attempt | Integer | Current retry attempt (starts at 1) |
| sprk_maxattempts | Integer | Max retry attempts (default: 3) |
| sprk_createdon | DateTime | Job created timestamp |
| sprk_startedon | DateTime | Processing started timestamp |
| sprk_completedon | DateTime | Processing completed timestamp |
| sprk_errorcode | String(20) | Error code if failed |
| sprk_errormessage | Memo | Detailed error message |
| sprk_createdby | Lookup(SystemUser) | User who initiated |

**Indexes**:
| Index Name | Columns | Purpose |
|------------|---------|---------|
| idx_job_idempotency | sprk_idempotencykey | Fast duplicate lookup |
| idx_job_status | sprk_status, sprk_createdon | Queue processing queries |
| idx_job_correlation | sprk_correlationid | Distributed tracing |
```

### 3.4 Add SSE Option for Real-Time Updates

**Problem**: Spec only mentions polling (3-second interval).

**Required Addition**:

```markdown
### Job Status Updates

#### Primary: Server-Sent Events (SSE)
```
GET /office/jobs/{jobId}/stream
Accept: text/event-stream
```

**Event Format**:
```
event: stage-update
data: {"stage":"FileUploaded","status":"Completed","timestamp":"2026-01-20T10:30:00Z"}

event: job-complete
data: {"status":"Completed","documentId":"guid","documentUrl":"https://..."}
```

**Benefits**:
- Real-time updates (no 3-second delay)
- Lower server load than polling
- Automatic reconnection support

**Client Implementation**:
```typescript
const eventSource = new EventSource(`/office/jobs/${jobId}/stream`);

eventSource.addEventListener('stage-update', (e) => {
  const data = JSON.parse(e.data);
  updateStageUI(data.stage, data.status);
});

eventSource.addEventListener('job-complete', (e) => {
  const data = JSON.parse(e.data);
  showCompletion(data);
  eventSource.close();
});

eventSource.onerror = () => {
  // Fall back to polling
  eventSource.close();
  startPolling(jobId);
};
```

#### Fallback: Polling
```
GET /office/jobs/{jobId}
```

**When to use**:
- SSE connection fails
- Browser doesn't support SSE (rare)
- Explicitly disabled by configuration

**Interval**: 3 seconds (configurable via `JOB_POLL_INTERVAL_MS`)
```

### 3.5 Remove DocumentWorkspaceLink Table

**Problem**: This table was designed for a "workspace" concept that doesn't exist.

**Required Action**: DELETE the DocumentWorkspaceLink table definition (Lines 467-478) entirely.

**Rationale**: The Document entity already has lookup fields for association targets. No separate link table is needed.

### 3.6 Rename /office/search/workspaces Endpoint

**Problem**: Endpoint name uses non-existent "workspace" concept.

**Required Revision**:

```markdown
### GET /office/search/entities

Search for association targets (Matter, Project, Invoice, Account, Contact).

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

**Search Behavior**:
- Searches name/title fields
- Filters by user's read access (UAC enforced)
- Orders by relevance, then recent activity
```

### 3.7 Update Quick Create to Include Account and Contact

**Problem**: FR-03 only mentions Matter/Project/Invoice.

**Required Revision**:

```markdown
### POST /office/quickcreate/{entityType}

Create a new entity with minimal fields for immediate document association.

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

**Authorization**: User must have Create permission on target entity type.
```

### 3.8 Add Dataverse Indexes

**Problem**: No indexes specified for new tables.

**Required Addition**:

```markdown
### Dataverse Index Specifications

#### sprk_emailartifact Indexes
| Index Name | Columns | Purpose |
|------------|---------|---------|
| idx_email_messageid | sprk_outlookmessageid | Duplicate detection |
| idx_email_document | sprk_document | Find email for document |

#### sprk_attachmentartifact Indexes
| Index Name | Columns | Purpose |
|------------|---------|---------|
| idx_attach_outlookid | sprk_outlookattachmentid | Duplicate detection |
| idx_attach_email | sprk_emailartifact | Find attachments for email |
| idx_attach_document | sprk_document | Find source attachment for document |

#### sprk_processingjob Indexes
| Index Name | Columns | Purpose |
|------------|---------|---------|
| idx_job_idempotency | sprk_idempotencykey | Fast duplicate lookup |
| idx_job_status | sprk_status, sprk_createdon | Queue processing queries |
| idx_job_correlation | sprk_correlationid | Distributed tracing |
```

---

## Part 4: Medium Priority Issues (🟢 Consider)

### 4.1 Add Rate Limiting Specification

```markdown
### Rate Limiting

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
```

### 4.2 Add Offline Handling Specification

```markdown
### Offline Handling

#### Detection
- Monitor `navigator.onLine` property
- Listen to `window.online` and `window.offline` events
- Detect API failures with network error codes

#### Behavior
- Show "You're offline" indicator in task pane header
- Disable save/share buttons when offline
- Queue is NOT supported in V1 (user must retry when online)

#### Reconnection
- Auto-retry pending job status checks when back online
- Show "Back online" notification
- Refresh any stale data (recent items, search results)
```

### 4.3 Add Telemetry Event Catalog

```markdown
### Telemetry Events

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

**Implementation**: Use Application Insights SDK or custom telemetry endpoint.
```

### 4.4 Add Task Pane State Diagram

```markdown
### Task Pane States

```
┌─────────────┐
│  LOADING    │ ← Initial load, auth check
└─────┬───────┘
      │ Auth success
      ▼
┌─────────────┐
│    IDLE     │ ← Ready for user action
└─────┬───────┘
      │ User selects "Save to Spaarke"
      ▼
┌─────────────┐
│  SELECTING  │ ← User picking association target
└─────┬───────┘
      │ User clicks "Save"
      ▼
┌─────────────┐
│  UPLOADING  │ ← Sending to BFF, showing progress
└─────┬───────┘
      │ Upload complete
      ▼
┌─────────────┐
│ PROCESSING  │ ← Polling job status, showing stages
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
```

### 4.5 Add File Type Restrictions

```markdown
### File Type Handling

#### Allowed File Types (Attachments)
All file types are allowed EXCEPT those in the blocked list.

#### Blocked File Types
Dangerous/executable files that cannot be saved:
```
.exe, .dll, .bat, .cmd, .ps1, .vbs, .js, .jar, .msi, .scr, .com, .pif, .reg
```

#### File Type Detection
1. Check file extension against blocked list
2. Verify MIME type matches extension (prevent extension spoofing)
3. For Office documents, validate file signature (magic bytes)

#### Validation Response
```json
{
  "valid": false,
  "blockedFiles": [
    { "name": "script.exe", "reason": "Executable files are not allowed" }
  ]
}
```
```

### 4.6 Add Accessibility Requirements

```markdown
### Accessibility Requirements (WCAG 2.1 AA)

#### Keyboard Navigation
- All interactive elements focusable via Tab
- Enter/Space activates buttons
- Escape closes dialogs/dropdowns
- Arrow keys navigate lists
- Focus trap in modal dialogs

#### Screen Reader Support
- All images have alt text
- Form fields have associated labels
- Status updates announced via aria-live regions
- Error messages associated with fields via aria-describedby

#### Visual Requirements
- Minimum contrast ratio 4.5:1 for text
- Focus indicators visible (not just color change)
- No information conveyed by color alone
- Support for Windows High Contrast mode

#### Testing
- Test with NVDA/JAWS on Windows
- Test with VoiceOver on Mac
- Run axe-core automated checks
- Manual keyboard-only testing
```

---

## Part 5: Updated Functional Requirements

Replace the existing FR table with this corrected version:

```markdown
### Functional Requirements

| ID | Requirement | Acceptance Criteria |
|----|-------------|---------------------|
| **FR-01** | Save Outlook email to Spaarke | User can save email body + selected attachments as Document(s) associated to Matter/Project/Invoice/Account/Contact; returns jobId within 3 seconds |
| **FR-02** | Save attachments selectively | User can toggle individual attachments on/off before saving |
| **FR-03** | Quick Create entity | User can create Matter/Project/Invoice/Account/Contact inline with minimal required fields |
| **FR-04** | Search association targets | Typeahead search returns Matters/Projects/Invoices/Accounts/Contacts within 500ms |
| **FR-05** | Recent items | User sees recently used association targets in picker |
| **FR-06** | Share via link insertion | User can insert Spaarke document links into Outlook compose |
| **FR-07** | Share via attachment | User can attach document copies from Spaarke to Outlook compose |
| **FR-08** | Grant access to external | User can mark "Grant access to recipients"; creates invitation stubs for External Portal |
| **FR-09** | Save Word document | User can save Word document as Spaarke Document associated to Matter/Project/Invoice/Account/Contact |
| **FR-10** | Version Word document | If document originated from Spaarke, "Save version" updates version lineage |
| ~~FR-11~~ | ~~Open from Spaarke~~ | ~~REMOVED - Users open documents from Spaarke application~~ |
| **FR-12** | Job status display | Task pane shows stage-based progress; updates via SSE (fallback: polling) |
| **FR-13** | Duplicate detection | If email/doc already saved with same association, return existing Document and notify user |
| **FR-14** | Processing options | User can toggle Profile summary, RAG index, Deep analysis (policy-driven defaults) |
| **FR-15** | Mandatory association | User must select association target; cannot save "Document Only" |
```

---

## Part 6: Updated API Contracts

### POST /office/save (Revised)

```markdown
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
  "associationId": "guid",
  "content": {
    // For OutlookEmail:
    "emailId": "string",
    "includeBody": true,
    "attachmentIds": ["id1", "id2"],
    
    // For WordDocument:
    "documentUrl": "string",
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
- `content.documentUrl`: Required if sourceType is WordDocument

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

**Error Responses**:
- 400: Validation error (OFFICE_001 - OFFICE_006)
- 403: Access denied to association target (OFFICE_009)
- 404: Association target not found (OFFICE_007)
```

---

## Part 7: Schema Corrections

### Tables to REMOVE

| Table | Reason |
|-------|--------|
| DocumentWorkspaceLink | "Workspace" concept doesn't exist; Document has direct lookups |

### Tables to ADD/MODIFY

```markdown
### sprk_emailartifact (New Table)

Stores metadata about saved Outlook emails.

| Column | Type | Description |
|--------|------|-------------|
| sprk_emailartifactid | GUID | Primary key |
| sprk_document | Lookup(sprk_document) | Associated Document record |
| sprk_outlookmessageid | String(500) | Outlook message ID (for dedup) |
| sprk_internetmessageid | String(500) | Internet message ID (RFC 2822) |
| sprk_conversationid | String(200) | Conversation thread ID |
| sprk_subject | String(500) | Email subject |
| sprk_sender | String(320) | Sender email address |
| sprk_senderName | String(200) | Sender display name |
| sprk_recipients | Memo | JSON: { to: [], cc: [], bcc: [] } |
| sprk_sentdate | DateTime | Email sent timestamp |
| sprk_receiveddate | DateTime | Email received timestamp |
| sprk_bodypreview | String(500) | First 500 chars of body |
| sprk_hasattachments | Boolean | Has attachments flag |
| sprk_importance | OptionSet | Low=0, Normal=1, High=2 |

### sprk_attachmentartifact (New Table)

Stores metadata about email attachments that were saved.

| Column | Type | Description |
|--------|------|-------------|
| sprk_attachmentartifactid | GUID | Primary key |
| sprk_emailartifact | Lookup(sprk_emailartifact) | Source email |
| sprk_document | Lookup(sprk_document) | Created Document |
| sprk_outlookattachmentid | String(200) | Outlook attachment ID |
| sprk_originalfilename | String(256) | Original filename |
| sprk_contenttype | String(100) | MIME type |
| sprk_size | Integer | Size in bytes |
| sprk_isinline | Boolean | Inline attachment flag |

### sprk_processingjob (New Table) - Per ADR-004

| Column | Type | Description |
|--------|------|-------------|
| sprk_processingjobid | GUID | Primary key |
| sprk_jobtype | OptionSet | EmailSave=1, AttachmentSave=2, DocumentSave=3, DocumentVersion=4 |
| sprk_status | OptionSet | Queued=1, Running=2, Completed=3, Failed=4, PartialSuccess=5, NeedsAttention=6 |
| sprk_subjectid | String(100) | Document ID (once known) |
| sprk_associationtype | OptionSet | Matter=1, Project=2, Invoice=3, Account=4, Contact=5 |
| sprk_associationid | String(50) | Association target GUID |
| sprk_correlationid | String(50) | Distributed tracing ID |
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
```

---

## Part 8: Implementation Checklist for Claude Code

When revising spec.md, ensure:

### Terminology
- [ ] Remove all instances of "workspace" - replace with "association target" or explicit entity names
- [ ] Clarify Document vs File distinction in glossary section
- [ ] Remove "Document Only" as an option

### Features
- [ ] Remove FR-11 (Open from Spaarke)
- [ ] Add Account and Contact to all association target lists
- [ ] Add Quick Create support for Account and Contact

### Authentication
- [ ] Replace generic "MSAL" references with explicit NAA specification
- [ ] Add NAA code examples
- [ ] Add Dialog API fallback specification
- [ ] Add app registration requirements

### Manifest
- [ ] Update to recommend unified manifest (GA for both Outlook and Word)
- [ ] Add unified manifest configuration examples
- [ ] Keep XML as fallback option, not primary

### APIs
- [ ] Rename `/office/search/workspaces` to `/office/search/entities`
- [ ] Update POST /office/save to require associationType
- [ ] Remove "None" from associationType options
- [ ] Add SSE endpoint for job status
- [ ] Add error code catalog

### Schema
- [ ] Remove DocumentWorkspaceLink table
- [ ] Add ADR-004 fields to ProcessingJob
- [ ] Add indexes for all new tables
- [ ] Add Account/Contact to association types

### Non-Functional
- [ ] Add requirement sets specification
- [ ] Add rate limiting specification
- [ ] Add error code catalog
- [ ] Add telemetry events
- [ ] Add accessibility requirements

---

## Appendix: Key Reference Links

- [NAA Documentation](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/enable-nested-app-authentication-in-your-add-in)
- [Unified Manifest Overview](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/unified-manifest-overview)
- [Mailbox Requirement Sets](https://learn.microsoft.com/en-us/javascript/api/requirement-sets/outlook/outlook-api-requirement-sets)
- [Office Add-ins at Build 2025](https://devblogs.microsoft.com/microsoft365dev/office-addins-at-build-2025/)
- [NAA FAQ and Legacy Token Deprecation](https://learn.microsoft.com/en-us/office/dev/add-ins/outlook/faq-nested-app-auth-outlook-legacy-tokens)
