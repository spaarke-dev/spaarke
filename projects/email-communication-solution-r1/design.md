# Communication Service — Design Document

> **Project**: email-communication-solution-r1
> **Status**: Design
> **Author**: Ralph Schroeder + AI
> **Date**: 2026-02-20

---

## Executive Summary

Replace the current Dataverse email activity / server-side sync approach with a unified **Communication Service** that sends emails via Microsoft Graph through the BFF, tracks all outbound communications in a custom `sprk_communication` entity, and supports multi-entity association via the existing AssociationResolver pattern. This service becomes the single email pipeline for workspace UI, AI playbooks, background jobs, and future communication channels (SMS, notifications).

The design also rewires the existing email-to-document archival flow to work with `sprk_communication` records instead of (or alongside) Dataverse email activities.

---

## Problem Statement

### Current Pain Points

1. **Dataverse email activities are heavyweight** — activity parties, polymorphic regarding, status state machines, nav-prop discovery, retry logic. The Create Matter wizard's email code is ~200 lines of workarounds.

2. **Server-side sync requires per-user mailbox configuration** — each user must have an approved mailbox in Dataverse. This doesn't scale and blocks headless/background senders (playbooks, automations).

3. **Activity permissions are coarse-grained** — granting email activity access also grants phone call, task, appointment access. No entity-level granularity.

4. **No Timeline/Queue usage** — the platform doesn't use Dataverse Timeline, Queues, or other activity-centric features, so the activity entity overhead provides no benefit.

5. **Email-to-document archival is tightly coupled** — the existing `EmailEndpoints.cs` flow converts Dataverse email activities to `.eml` documents. A new communication entity needs its own archival path.

6. **No reusable email service** — each feature (Create Matter, Draft Summary, future playbooks) implements its own email sending logic.

---

## Goals

| # | Goal | Success Criteria |
|---|------|-----------------|
| G1 | Single email sending pipeline | One BFF endpoint callable from UI, playbooks, background jobs |
| G2 | No server-side sync dependency | Emails sent via Graph API (app-only or shared mailbox) |
| G3 | Custom tracking entity | `sprk_communication` with fine-grained security |
| G4 | Multi-entity association | Uses AssociationResolver pattern (not polymorphic regarding) |
| G5 | Attachment support | Attach SPE documents to outbound emails |
| G6 | Email-to-document archival | Outbound emails archived as documents in SPE |
| G7 | Communication application | Standalone form/app for composing and viewing communications |
| G8 | Playbook integration | AI tools can call email service as a tool action |
| G9 | Extensible to other channels | Schema supports SMS, in-app notifications in future |

---

## Architecture Overview

```
                                    ┌─────────────────────────┐
                                    │   Microsoft Graph API    │
                                    │   (sendMail / sendMail)  │
                                    └────────────▲────────────┘
                                                 │
                         ┌───────────────────────┴───────────────────────┐
                         │                BFF API                         │
                         │  ┌──────────────────────────────────────────┐  │
                         │  │  CommunicationEndpoints.cs               │  │
                         │  │  POST /api/communications/send           │  │
                         │  │  POST /api/communications/send-bulk      │  │
                         │  │  GET  /api/communications/{id}/status    │  │
                         │  └──────────────────┬───────────────────────┘  │
                         │                     │                          │
                         │  ┌──────────────────▼───────────────────────┐  │
                         │  │  CommunicationService.cs                 │  │
                         │  │  - Resolve recipients                    │  │
                         │  │  - Render templates (optional)           │  │
                         │  │  - Send via Graph                        │  │
                         │  │  - Create sprk_communication record      │  │
                         │  │  - Archive to SPE (optional)             │  │
                         │  │  - Enqueue AI analysis (optional)        │  │
                         │  └──────────────────────────────────────────┘  │
                         └───────────────────────────────────────────────┘
                                       ▲              │
                              Request  │              │  Dataverse REST API
                                       │              ▼
              ┌────────────────────────┐    ┌──────────────────────┐
              │  Callers               │    │  sprk_communication   │
              │  - Workspace UI        │    │  (Dataverse entity)   │
              │  - AI Playbooks        │    │                       │
              │  - Background Jobs     │    │  + AssociationResolver│
              │  - Power Automate      │    │    pattern for multi- │
              └────────────────────────┘    │    entity linking     │
                                            └──────────────────────┘
```

---

## Detailed Design

### 1. `sprk_communication` Entity (Dataverse)

> **Note**: Entity will be created manually in Dataverse. This section documents the schema for reference.

#### Core Fields

| Field | Logical Name | Type | Purpose |
|-------|-------------|------|---------|
| Name | `sprk_name` | Text (200) | Auto: "{Type}: {Subject}" |
| Type | `sprk_communicationtype` | Choice | Email=100000000, SMS=100000001, Notification=100000002 |
| Status | `sprk_communicationstatus` | Choice | Draft=100000000, Queued=100000001, Sent=100000002, Delivered=100000003, Failed=100000004, Bounced=100000005 |
| Direction | `sprk_direction` | Choice | Outbound=100000000, Inbound=100000001 |

#### Email-Specific Fields

| Field | Logical Name | Type | Purpose |
|-------|-------------|------|---------|
| To | `sprk_to` | Text (2000) | Semicolon-delimited recipient addresses |
| CC | `sprk_cc` | Text (2000) | CC addresses |
| BCC | `sprk_bcc` | Text (2000) | BCC addresses |
| From | `sprk_from` | Text (500) | Sender address (shared mailbox or user) |
| Subject | `sprk_subject` | Text (500) | Email subject |
| Body | `sprk_body` | Multiline (100K) | Body (HTML or plain text) |
| Body Format | `sprk_bodyformat` | Choice | PlainText=100000000, HTML=100000001 |

#### Tracking & Correlation

| Field | Logical Name | Type | Purpose |
|-------|-------------|------|---------|
| Graph Message ID | `sprk_graphmessageid` | Text (500) | Graph API message ID for delivery tracking |
| Sent At | `sprk_sentat` | DateTime | UTC timestamp of send |
| Sent By | `sprk_sentby` | Lookup (systemuser) | User who initiated |
| Error Message | `sprk_errormessage` | Multiline (4000) | Error details on failure |
| Retry Count | `sprk_retrycount` | Integer | Number of send attempts |
| Correlation ID | `sprk_correlationid` | Text (100) | For tracing across systems |

#### Primary Association Fields (AssociationResolver Pattern)

The **primary association** lives on the parent `sprk_communication` record for fast filtering in views and dashboards. This uses the same AssociationResolver PCF pattern as `sprk_event`:

| Field | Logical Name | Type | Target |
|-------|-------------|------|--------|
| Matter | `sprk_regardingmatter` | Lookup | sprk_matter |
| Project | `sprk_regardingproject` | Lookup | sprk_project |
| Invoice | `sprk_regardinginvoice` | Lookup | sprk_invoice |
| Analysis | `sprk_regardinganalysis` | Lookup | sprk_analysis |
| Account | `sprk_regardingaccount` | Lookup | account |
| Contact | `sprk_regardingcontact` | Lookup | contact |
| Work Assignment | `sprk_regardingworkassignment` | Lookup | sprk_workassignment |
| Budget | `sprk_regardingbudget` | Lookup | sprk_budget |

Plus denormalized unified fields (for views, filtering, and the AssociationResolver PCF):

| Field | Logical Name | Type | Purpose |
|-------|-------------|------|---------|
| Record Name | `sprk_regardingrecordname` | Text | Display name of primary associated record |
| Record ID | `sprk_regardingrecordid` | Text | GUID of primary associated record |
| Record Type | `sprk_regardingrecordtype` | Lookup | sprk_recordtype_ref |
| Record URL | `sprk_regardingrecordurl` | Text (URL) | Clickable link to primary associated record |
| Association Count | `sprk_associationcount` | Integer | Total number of associated records (including primary) |

**Why this pattern**: The AssociationResolver PCF control is already production-ready on `sprk_event`. Reusing the identical pattern on `sprk_communication` means:
- No new PCF development needed (same control, different form)
- Configuration-driven via `sprk_recordtype_ref` (add new entity types without code)
- Field mapping profiles can auto-populate communication fields from parent records
- Fine-grained security per lookup field
- Simple OData queries: `$filter=_sprk_regardingmatter_value eq '{matterId}'`

#### Multi-Record Association (Future — sprk_communicationassociation)

> **Deferred to future release.** The primary association pattern above is sufficient for current requirements. Multi-record association can be added later without schema changes to `sprk_communication`.

A future `sprk_communicationassociation` child entity would allow one communication to link to multiple records (e.g., 3 matters + 1 project + 1 invoice). The design:

- Same AssociationResolver pattern on child records (entity-specific lookups + role field)
- Roles: Primary, Related, CC/FYI, Billing
- Primary association stays on parent for fast view filtering
- Child records provide the complete association graph
- No changes to parent entity schema — purely additive

**Why this is safe to defer:**
- Parent `sprk_communication` primary association works standalone (same as `sprk_event` today)
- BFF API already accepts `associations[]` array — currently uses `[0]` for primary only
- Adding the child entity later requires no data migration
- No code changes to existing communications when multi-record is added

#### Document Attachment Fields

| Field | Logical Name | Type | Purpose |
|-------|-------------|------|---------|
| Has Attachments | `sprk_hasattachments` | Boolean | Quick filter flag |
| Attachment Count | `sprk_attachmentcount` | Integer | Number of attached documents |

Attachments are linked via a child entity (`sprk_communicationattachment`) that references both `sprk_communication` and `sprk_document`:

```
sprk_communicationattachment (intersection entity)
├── sprk_communication  → Lookup to sprk_communication
├── sprk_document       → Lookup to sprk_document
├── sprk_attachmenttype → Choice: File=100000000, InlineImage=100000001
└── sprk_name           → Auto: document name
```

This allows attaching existing SPE documents (already in `sprk_document`) to outbound emails without duplicating files.

---

### 2. BFF Communication Endpoints

#### `POST /api/communications/send`

Primary endpoint for sending a single communication.

**Request:**
```json
{
  "type": "email",
  "to": ["client@example.com", "partner@lawfirm.com"],
  "cc": [],
  "bcc": [],
  "subject": "New Matter: Smith v. Jones",
  "body": "<p>Dear Client,</p><p>We are pleased to confirm...</p>",
  "bodyFormat": "html",
  "fromMailbox": null,
  "associations": [
    { "entity": "sprk_matter", "id": "a1b2c3d4-...", "name": "Smith v. Jones", "role": "primary" }
  ],
  "attachmentDocumentIds": ["doc-guid-1", "doc-guid-2"],
  "archiveToSpe": true,
  "containerId": "container-guid",
  "templateId": null,
  "templateData": null,
  "initiatedBy": "user-guid",
  "correlationId": "create-matter-abc123"
}
```

**Response:**
```json
{
  "communicationId": "comm-guid",
  "status": "sent",
  "graphMessageId": "AAMk...",
  "sentAt": "2026-02-20T14:30:00Z",
  "archivedDocumentId": "doc-guid-or-null",
  "warnings": []
}
```

**Error Response (partial failure):**
```json
{
  "communicationId": "comm-guid",
  "status": "sent",
  "graphMessageId": "AAMk...",
  "warnings": ["Archival to SPE failed — email was sent but not archived."]
}
```

#### Processing Pipeline

```
Request received
  │
  ├─ 1. Validate request (required fields, email format)
  │
  ├─ 2. Resolve sender mailbox
  │     └─ fromMailbox ?? config default (e.g., noreply@firm.com)
  │
  ├─ 3. Build Graph sendMail payload
  │     ├─ ToRecipients, CcRecipients, BccRecipients
  │     ├─ Subject, Body (HTML or text)
  │     └─ Attachments (fetch from SPE by document IDs)
  │
  ├─ 4. Send via Microsoft Graph
  │     ├─ App-only: POST /users/{mailbox}/sendMail
  │     └─ Returns: message ID for tracking
  │
  ├─ 5. Create sprk_communication record (Dataverse REST API)
  │     ├─ Core fields (to, from, subject, body, status=Sent)
  │     ├─ Association fields (regarding lookup + denormalized)
  │     └─ Tracking fields (graphMessageId, sentAt, sentBy)
  │
  ├─ 6. Create attachment links (if attachmentDocumentIds provided)
  │     └─ Create sprk_communicationattachment records
  │
  ├─ 7. Archive to SPE (if archiveToSpe=true)
  │     ├─ Generate .eml or HTML file from email content
  │     ├─ Upload to SPE: /communications/{commId:N}/{fileName}
  │     ├─ Create sprk_document record
  │     │   ├─ DocumentType: Communication (new choice value)
  │     │   ├─ SourceType: CommunicationArchive (new choice value)
  │     │   └─ Link to matter via parent entity
  │     └─ Enqueue AI analysis (optional, non-blocking)
  │
  └─ 8. Return response
```

#### `POST /api/communications/send-bulk`

For distributing to multiple recipients with individual tracking (e.g., summary distribution to 5 people = 5 `sprk_communication` records).

```json
{
  "recipients": [
    { "to": ["alice@example.com"], "templateData": { "name": "Alice" } },
    { "to": ["bob@example.com"], "templateData": { "name": "Bob" } }
  ],
  "subject": "Matter Summary: Smith v. Jones",
  "bodyTemplate": "Dear {{name}},\n\nPlease find attached...",
  "bodyFormat": "html",
  "regardingEntity": "sprk_matter",
  "regardingId": "matter-guid",
  "attachmentDocumentIds": ["doc-guid"],
  "archiveToSpe": true
}
```

#### `GET /api/communications/{id}/status`

Check delivery status (for async/queued sends).

---

### 3. Graph sendMail Integration

**Authentication**: App-only using existing `GraphClientFactory.ForApp()`.

**Sender Mailbox**: A shared mailbox (e.g., `legal-notifications@firm.com`) configured in Azure AD. The app registration needs `Mail.Send` application permission.

**Key advantage**: No per-user mailbox configuration. Any user can trigger an email through the BFF, and it sends from the shared mailbox.

**Attachment handling**: For documents in SPE, the BFF:
1. Downloads the file content from SPE via `SpeFileStore`
2. Attaches as base64-encoded `FileAttachment` in the Graph sendMail payload
3. Limits: 150 attachments, 35MB total per Graph API limits (use large attachment session for >3MB files)

```csharp
// Simplified Graph sendMail call
var message = new Message
{
    Subject = request.Subject,
    Body = new ItemBody
    {
        ContentType = request.BodyFormat == "html" ? BodyType.Html : BodyType.Text,
        Content = request.Body,
    },
    ToRecipients = request.To.Select(email => new Recipient
    {
        EmailAddress = new EmailAddress { Address = email }
    }).ToList(),
    Attachments = attachments, // From SPE document download
};

await graphClient.Users[senderMailbox].SendMail
    .PostAsync(new SendMailPostRequestBody { Message = message });
```

---

### 4. Attachment-to-Document Integration

Outbound emails can include existing SPE documents as attachments. The flow:

```
sprk_document records (already in Dataverse/SPE)
    │
    ├─ User selects documents to attach in Communication form
    │  (or programmatically via attachmentDocumentIds)
    │
    ├─ BFF downloads file content from SPE (SpeFileStore.DownloadAsync)
    │
    ├─ BFF attaches to Graph sendMail payload (FileAttachment)
    │
    ├─ BFF creates sprk_communicationattachment records
    │  linking sprk_communication ↔ sprk_document
    │
    └─ Archived .eml includes attachments inline
```

**For the Communication form UI**: A document picker component that queries `sprk_document` records linked to the associated entity (e.g., all documents on the matter).

---

### 5. Email-to-Document Archival (Rewired)

#### Current Flow (email activities)

```
Dataverse email activity → Webhook → BFF EmailEndpoints → .eml → SPE → sprk_document
```

#### New Flow (sprk_communication)

```
sprk_communication (status=Sent) → BFF archives → .eml → SPE → sprk_document
```

**Two archival modes:**

1. **Inline archival** (default for outbound): During the `POST /api/communications/send` pipeline (step 7), the BFF generates the .eml and archives immediately. No webhook needed.

2. **Batch archival** (for existing records): A background job scans `sprk_communication` records where `sprk_communicationstatus = Sent` and `sprk_archiveddocumentid = null`, and archives them.

**Relationship to existing email-to-document**:
- The existing `EmailEndpoints.cs` flow for Dataverse email activities remains unchanged (for inbound email archival from Exchange)
- New `CommunicationEndpoints.cs` handles outbound communication archival
- Both create `sprk_document` records with appropriate `SourceType` values

---

### 6. AssociationResolver Integration

The `sprk_communication` entity reuses the exact same AssociationResolver PCF pattern as `sprk_event`.

**On the Communication form:**
1. Place the `AssociationResolver` PCF control
2. Bind to `sprk_regardingrecordtype` lookup field
3. Configure entity-specific lookup fields (same 8 entities as sprk_event)
4. Denormalized fields auto-populate on selection

**For programmatic creation (BFF):**
The `POST /api/communications/send` endpoint receives `associations[]` array. The BFF uses the first (primary) entry:
1. Looks up the `sprk_recordtype_ref` ID for the entity logical name
2. Sets the entity-specific lookup: `sprk_regarding{entity}@odata.bind`
3. Sets denormalized fields: `sprk_regardingrecordname`, `sprk_regardingrecordid`, `sprk_regardingrecordtype@odata.bind`, `sprk_regardingrecordurl`

This means UI and API callers both produce identical association data. The `associations[]` array format is future-ready for multi-record association (see deferred feature above).

---

### 7. Communication Application (Model-Driven Form)

A model-driven form for `sprk_communication` that serves as both a **compose view** and a **read/audit view**.

#### Compose Mode (new record)

```
┌──────────────────────────────────────────────────────────────┐
│  New Communication                                            │
│                                                               │
│  Type:  [Email ▼]          Status: Draft                     │
│                                                               │
│  ── Association ──────────────────────────────────────────── │
│  [AssociationResolver PCF]                                    │
│  Entity: [Matter ▼]  Record: [Smith v. Jones      ] [Select] │
│                                                               │
│  ── Email Details ───────────────────────────────────────── │
│  To:      [client@example.com                         ] [+]  │
│  CC:      [                                           ] [+]  │
│  Subject: [New Matter: Smith v. Jones                     ]  │
│                                                               │
│  Body:                                                        │
│  ┌──────────────────────────────────────────────────────────┐│
│  │ Rich text editor (HTML)                                   ││
│  │ Dear Client,                                              ││
│  │ We are pleased to confirm that your matter...             ││
│  └──────────────────────────────────────────────────────────┘│
│                                                               │
│  ── Attachments ─────────────────────────────────────────── │
│  [+ Attach Document]                                          │
│  📄 Engagement Letter.pdf (245 KB)                    [x]    │
│  📄 NDA Draft.docx (128 KB)                          [x]    │
│                                                               │
│  [Send]  [Save Draft]  [Cancel]                              │
└──────────────────────────────────────────────────────────────┘
```

#### Read Mode (sent record)

```
┌──────────────────────────────────────────────────────────────┐
│  Communication: Email: New Matter: Smith v. Jones             │
│  Status: Sent        Sent: 2026-02-20 2:30 PM               │
│  From: legal-notifications@firm.com                          │
│  Initiated by: Ralph Schroeder                                │
│                                                               │
│  ── Association ──────────────────────────────────────────── │
│  Matter: Smith v. Jones                     [Open]           │
│                                                               │
│  ── Email Details ───────────────────────────────────────── │
│  To: client@example.com                                       │
│  Subject: New Matter: Smith v. Jones                          │
│  Body: (rendered HTML)                                        │
│                                                               │
│  ── Attachments (2) ─────────────────────────────────────── │
│  📄 Engagement Letter.pdf  [Open in SPE]                     │
│  📄 NDA Draft.docx         [Open in SPE]                     │
│                                                               │
│  ── Tracking ────────────────────────────────────────────── │
│  Graph Message ID: AAMk...                                    │
│  Correlation ID: create-matter-abc123                         │
│  Archived Document: [🔗 View archived .eml]                 │
└──────────────────────────────────────────────────────────────┘
```

**Send button** on the form: Calls BFF `POST /api/communications/send` via JavaScript web resource or PCF command. This keeps the sending logic in the BFF (single pipeline).

---

### 8. Playbook / AI Tool Integration

The communication service exposes as an AI Tool handler:

```csharp
public class SendCommunicationToolHandler : IAiToolHandler
{
    public string ToolName => "send_communication";

    public ToolDefinition Definition => new()
    {
        Name = "send_communication",
        Description = "Send an email or other communication to specified recipients, linked to a matter or other entity.",
        Parameters = new
        {
            type = "object",
            properties = new
            {
                to = new { type = "array", items = new { type = "string" }, description = "Recipient email addresses" },
                subject = new { type = "string" },
                body = new { type = "string", description = "Email body (HTML supported)" },
                regardingEntity = new { type = "string", description = "Entity logical name (e.g., sprk_matter)" },
                regardingId = new { type = "string", description = "Record GUID" },
                attachmentDocumentIds = new { type = "array", items = new { type = "string" } },
            },
            required = new[] { "to", "subject", "body" },
        },
    };

    public async Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken ct)
    {
        var request = input.Deserialize<SendCommunicationRequest>();
        var result = await _communicationService.SendAsync(request, ct);
        return ToolResult.Success(new { communicationId = result.Id, status = result.Status });
    }
}
```

Playbook example:
```
"After creating the matter, send an introductory email to the client
 with the engagement letter attached."
→ AI calls send_communication tool with matter context + document IDs
```

---

### 9. Frontend Changes (Create Matter Wizard)

#### Simplified matterService.ts

Replace ~200 lines of Dataverse email activity code with:

```typescript
private async _sendCommunication(
  matterId: string,
  matterName: string,
  input: ISendEmailInput
): Promise<{ success: boolean; warning?: string }> {
  try {
    const response = await authenticatedFetch(
      `${getBffBaseUrl()}/api/communications/send`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          type: 'email',
          to: input.to.split(/[;,]/).map(a => a.trim()).filter(Boolean),
          subject: input.subject,
          body: input.body,
          bodyFormat: 'text',
          associations: [
            { entity: 'sprk_matter', id: matterId, name: matterName, role: 'primary' },
          ],
          archiveToSpe: true,
          containerId: this._containerId,
        }),
      }
    );

    if (!response.ok) {
      const err = await response.json().catch(() => ({ message: 'Unknown error' }));
      return { success: false, warning: `Email failed: ${err.message}` };
    }

    return { success: true };
  } catch (err) {
    const message = err instanceof Error ? err.message : 'Unknown error';
    return { success: false, warning: `Email failed: ${message}` };
  }
}
```

**Delete from matterService.ts:**
- `_buildEmailEntity()`
- `_resolveEmailToContact()`
- `_resolveToParties()`
- `_sendEmail()`
- `_createAndSendEmail()` (all retry logic)
- Email-related nav-prop discovery code

**Keep:**
- `_discoverNavProps()` — still used for matter creation lookups
- `_navPropCache` — still used for matter creation

---

## Migration Strategy

### Phase 1: BFF Email Service (Immediate)

**Goal**: Ship the BFF endpoint so Create Matter wizard works reliably.

1. Create `CommunicationEndpoints.cs` with `POST /api/communications/send`
2. Create `CommunicationService.cs` with Graph sendMail
3. Configure shared mailbox in Azure AD
4. Simplify `matterService.ts` to call BFF
5. Delete Dataverse email activity code from frontend

**Dataverse**: No `sprk_communication` entity yet. BFF sends email via Graph and returns success/failure. No Dataverse record created in Phase 1 (just send the email).

### Phase 2: sprk_communication Entity + Tracking

**Goal**: Add audit trail and entity tracking.

1. Create `sprk_communication` entity with core + email + tracking fields
2. Add primary association fields (AssociationResolver pattern — same 8 entity lookups + denormalized fields)
3. Update BFF to create `sprk_communication` record after successful send
4. Update BFF to set primary association on the communication record
5. Add communication subgrid to matter form (filtered by `_sprk_regardingmatter_value`)

### Phase 3: Communication Application

**Goal**: Standalone compose/view experience.

1. Create model-driven form for `sprk_communication`
2. Place AssociationResolver PCF on form
3. Add document attachment picker
4. Add "Send" command bar button (calls BFF)
5. Add communication views (My Sent, By Matter, By Project, Failed, etc.)

### Phase 4: Attachments + Archival

**Goal**: Full document integration.

1. Create `sprk_communicationattachment` intersection entity
2. Implement attachment download from SPE → Graph sendMail payload
3. Implement .eml archival to SPE on send
4. Create `sprk_document` records for archived communications
5. Rewire existing email-to-document patterns to work with `sprk_communication`

### Phase 5: Playbook Integration

**Goal**: AI tools can send communications.

1. Create `SendCommunicationToolHandler`
2. Register tool definition in AI Tool Framework
3. Test with playbook scenarios (post-matter-creation email, document distribution)

### Phase 6 (Future): Multi-Record Association

**Goal**: One communication linked to multiple records across entity types.

1. Create `sprk_communicationassociation` child entity
2. Add subgrid to communication form
3. Update BFF to create child association records from `associations[]` array
4. Update queries for cross-entity discovery

See "Multi-Record Association" section above for full design. This is purely additive — no changes to existing schema or code.

---

## Scope Boundaries

### In Scope

- Email sending via Microsoft Graph (outbound)
- `sprk_communication` entity for tracking
- Single-entity association via AssociationResolver pattern (multi-record deferred to future)
- Document attachment support (SPE documents)
- .eml archival to SPE
- Create Matter wizard rewire
- AI tool handler for playbooks

### Out of Scope (Future)

- **Multi-record association** (`sprk_communicationassociation` child entity — Phase 6, design documented above)
- Inbound email processing (keep existing email-to-document webhook flow)
- SMS / Teams message channels (schema supports it, implementation deferred)
- Email templates engine (server-side Liquid/Handlebars — Phase 5+)
- Read receipts / delivery notifications via Graph webhooks
- Bulk marketing email (use dedicated email marketing platform)

---

## Technical Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Send mechanism | Graph API (app-only) | No per-user mailbox config, works from any context |
| Sender identity | Shared mailbox | Consistent "from" address, no user mailbox dependency |
| Tracking entity | `sprk_communication` (custom) | Fine-grained security, no activity baggage |
| Association pattern | AssociationResolver (entity-specific lookups) | Proven pattern, production PCF exists, configuration-driven; multi-record deferred |
| Attachment model | Intersection entity referencing `sprk_document` | No file duplication, leverages existing SPE documents |
| Archival format | .eml in SPE | Consistent with existing email-to-document pipeline |
| Frontend integration | `authenticatedFetch` to BFF | Existing auth pattern, single code path |

---

## Dependencies

| Dependency | Status | Required For |
|------------|--------|-------------|
| Graph SDK in BFF | Available | Phase 1 |
| `Mail.Send` app permission | Needs configuration | Phase 1 |
| Shared mailbox in Azure AD | Needs creation | Phase 1 |
| `authenticatedFetch` in workspace SPA | Available | Phase 1 |
| `sprk_communication` entity | Manual creation | Phase 2 |
| AssociationResolver PCF | Available (production) | Phase 2-3 |
| `sprk_recordtype_ref` entry for communication | Manual creation | Phase 3 |
| `sprk_communicationattachment` entity | Manual creation | Phase 4 |
| AI Tool Framework | Available | Phase 5 |

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|-----------|
| Graph sendMail rate limits (10,000/day per mailbox) | Bulk sends could be throttled | Multiple shared mailboxes, queue with backpressure |
| Large attachments (>35MB Graph limit) | Send failure | Validate attachment size before send, use sharing links for large files |
| Shared mailbox requires Exchange Online license | Blocked if no license | Verify license availability in Phase 1 |
| `Mail.Send` permission is broad | Security concern | Scope to specific mailbox via application access policy |

---

## Relationship to Existing Email-to-Document

The existing `EmailEndpoints.cs` flow handles **inbound** email archival:
- Dataverse email activity → webhook → .eml → SPE → sprk_document

The new Communication Service handles **outbound** communication:
- sprk_communication (or direct API call) → Graph sendMail → optional .eml archive

Both flows create `sprk_document` records in SPE. They coexist:
- **Inbound**: Keep existing email-to-document webhook pipeline (unchanged)
- **Outbound**: New Communication Service pipeline

Over time, if the platform fully moves away from Dataverse email activities, the inbound flow could also be rewired to create `sprk_communication` records instead of relying on email activities.
