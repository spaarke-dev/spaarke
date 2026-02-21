# Communication Service — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-02-20
> **Source**: design.md
> **Branch**: work/email-communication-solution-r1

---

## Executive Summary

Replace heavyweight Dataverse email activities with a unified **Communication Service** that sends outbound emails via Microsoft Graph (app-only, shared mailbox) through the BFF, tracks all communications in a custom `sprk_communication` entity, and supports multi-entity association via the existing AssociationResolver pattern. This becomes the single email pipeline for workspace UI, AI playbooks, background jobs, and future channels (SMS, notifications).

---

## Scope

### In Scope (Phases 1-5)

- **Phase 1 — BFF Email Service**: `CommunicationEndpoints.cs` + `CommunicationService.cs` with Graph sendMail, Create Matter wizard rewire
- **Phase 2 — Entity + Tracking**: `sprk_communication` Dataverse entity with primary association fields (AssociationResolver pattern), BFF creates records after send
- **Phase 3 — Communication Application**: Model-driven form (compose + read views), AssociationResolver PCF on form, communication views/subgrids
- **Phase 4 — Attachments + Archival**: `sprk_communicationattachment` intersection entity, SPE document attachment to emails, .eml archival to SPE
- **Phase 5 — Playbook Integration**: `SendCommunicationToolHandler` AI tool, playbook email scenarios

### Out of Scope

- Multi-record association (`sprk_communicationassociation` child entity — Phase 6, future)
- Inbound email processing (keep existing `EmailEndpoints.cs` webhook pipeline)
- SMS / Teams message channels (schema supports it, implementation deferred)
- Email templates engine (server-side Liquid/Handlebars)
- Read receipts / delivery notifications via Graph webhooks
- Bulk marketing email
- Background retry on send failure (fail immediately in Phase 1; retry mechanism deferred)

### Affected Areas

- `src/server/api/Sprk.Bff.Api/Api/` — New `CommunicationEndpoints.cs`
- `src/server/api/Sprk.Bff.Api/Services/Communication/` — New `CommunicationService.cs` and models
- `src/server/api/Sprk.Bff.Api/Services/Ai/Tools/` — New `SendCommunicationToolHandler.cs`
- `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/` — Existing `GraphClientFactory.ForApp()` (reuse)
- `src/server/api/Sprk.Bff.Api/Services/Spe/` — Existing `SpeFileStore` for attachment downloads (reuse)
- `src/solutions/LegalWorkspace/src/components/CreateMatter/matterService.ts` — Simplify email code (workspace worktree)
- Dataverse — Manual entity creation: `sprk_communication`, `sprk_communicationattachment`

---

## Requirements

### Functional Requirements

#### Phase 1: BFF Email Service

1. **FR-01**: `POST /api/communications/send` endpoint accepts email request (to, cc, bcc, subject, body, bodyFormat, fromMailbox, associations, correlationId) and sends via Graph sendMail.
   - Acceptance: Email delivered to recipient, response includes status and graphMessageId.

2. **FR-02**: Sender mailbox resolution with **two-tier approved sender model**:
   - **Phase 1 (BFF config)**: Approved senders defined in `CommunicationOptions.ApprovedSenders[]` (appsettings.json). Includes default sender flag. Callers specify `fromMailbox`; BFF validates against config list. Falls back to default sender if omitted.
   - **Phase 2+ (Dataverse override)**: Optional `sprk_approvedsender` config entity allows admins to add/remove approved senders without deployment. BFF merges config + Dataverse lists at runtime (Dataverse entries take precedence).
   - Senders can be shared mailboxes, service accounts, or role-based addresses (e.g., `billing@firm.com`) — they do NOT need to be Dataverse users. The `sprk_sentby` field separately tracks the initiating user.
   - Acceptance: Only approved senders allowed. Invalid `fromMailbox` returns ProblemDetails with `INVALID_SENDER` error code.

3. **FR-03**: Graph sendMail integration using `GraphClientFactory.ForApp()` with `Mail.Send` application permission.
   - Acceptance: App-only authentication, no per-user mailbox configuration required.

4. **FR-04**: Request validation — required fields (to, subject, body), email format validation, bodyFormat validation.
   - Acceptance: Invalid requests return ProblemDetails with specific error codes.

5. **FR-05**: Simplify Create Matter wizard `matterService.ts` — replace ~200 lines of Dataverse email activity code with single `authenticatedFetch` call to BFF.
   - Acceptance: Delete `_buildEmailEntity()`, `_resolveEmailToContact()`, `_resolveToParties()`, `_sendEmail()`, `_createAndSendEmail()`. Keep `_discoverNavProps()` and `_navPropCache`.

#### Phase 2: Entity + Tracking

6. **FR-06**: Create `sprk_communication` Dataverse entity with core fields (type, status, direction), email fields (to, cc, bcc, from, subject, body, bodyFormat), and tracking fields (graphMessageId, sentAt, sentBy, errorMessage, retryCount, correlationId).
   - Acceptance: Entity exists in Dataverse with all fields per design schema.

7. **FR-07**: Primary association fields on `sprk_communication` using AssociationResolver pattern — 8 entity-specific lookups (matter, project, invoice, analysis, **organization**, **person**, workassignment, budget) plus denormalized unified fields. **Note**: Actual field names differ from design: `sprk_regardingorganization` (not account), `sprk_regardingperson` (not contact). See Entity Schema Reference for full mapping.
   - Acceptance: Same field pattern as `sprk_event`. AssociationResolver PCF works on communication form without new PCF development.

8. **FR-08**: BFF creates `sprk_communication` record after successful Graph send — sets core fields, email fields, tracking fields, and primary association from `associations[0]`.
   - Acceptance: Every sent email has a corresponding `sprk_communication` record with correct association.

9. **FR-09**: `GET /api/communications/{id}/status` endpoint returns communication status.
   - Acceptance: Returns current status from `sprk_communication` record.

#### Phase 3: Communication Application

10. **FR-10**: Model-driven form for `sprk_communication` with compose mode (new record: draft → send) and read mode (sent record: audit view).
    - Acceptance: Users can compose and send emails from the form, and view sent communication details.

11. **FR-11**: AssociationResolver PCF control placed on communication form, bound to `sprk_regardingrecordtype` lookup, configured with same 8 entity types.
    - Acceptance: Users can associate communication with any supported entity type.

12. **FR-12**: Communication views — My Sent, By Matter, By Project, Failed, All Communications.
    - Acceptance: Views filterable by status, entity association, date range.

13. **FR-13**: Communication subgrid on Matter form filtered by `_sprk_regardingmatter_value`.
    - Acceptance: Matter form shows related communications.

14. **FR-14**: "Send" command bar button on communication form calls BFF `POST /api/communications/send`.
    - Acceptance: Clicking Send on a Draft communication sends it and updates status.

#### Phase 4: Attachments + Archival

15. **FR-15**: `sprk_communicationattachment` intersection entity linking `sprk_communication` to `sprk_document` with attachment type (File, InlineImage).
    - Acceptance: Entity exists, supports many-to-many relationship.

16. **FR-16**: Attachment download from SPE — BFF downloads file content via `SpeFileStore.DownloadAsync()` and attaches as base64 `FileAttachment` in Graph sendMail payload.
    - Acceptance: Email received with attached documents. Limit: 150 attachments, 35MB total.

17. **FR-17**: .eml archival to SPE on send — generate .eml from email content, upload to SPE path `/communications/{commId:N}/{fileName}`, create `sprk_document` record with DocumentType=Communication, SourceType=CommunicationArchive.
    - Acceptance: Sent email archived as .eml document in SPE, linked to matter.

18. **FR-18**: Document attachment picker on communication form — query `sprk_document` records linked to associated entity.
    - Acceptance: Users can select existing SPE documents to attach to outbound email.

19. **FR-19**: `POST /api/communications/send-bulk` endpoint for distributing to multiple recipients with individual tracking (one `sprk_communication` per recipient).
    - Acceptance: Bulk send creates N communication records for N recipients.

#### Phase 5: Playbook Integration

20. **FR-20**: `SendCommunicationToolHandler : IAiToolHandler` with tool name `send_communication`, accepting to, subject, body, regardingEntity, regardingId, attachmentDocumentIds.
    - Acceptance: AI playbooks can send emails via tool call, auto-discovered by `IToolHandlerRegistry`.

21. **FR-21**: Tool returns communicationId and status on success, structured error on failure.
    - Acceptance: Playbook receives actionable result for next-step decisions.

### Non-Functional Requirements

- **NFR-01**: No per-user mailbox configuration — all sends use app-only Graph auth via shared mailbox.
- **NFR-02**: Fine-grained security — `sprk_communication` entity with per-field security, not coarse-grained activity permissions.
- **NFR-03**: Attachment limits — validate before send: max 150 attachments, 35MB total per Graph API limits.
- **NFR-04**: On send failure, fail immediately and return error with ProblemDetails. No background retry in current scope.
- **NFR-05**: All error responses use ProblemDetails (RFC 7807) with stable `errorCode` extension and correlation ID.
- **NFR-06**: Existing inbound email-to-document pipeline (`EmailEndpoints.cs`) remains unchanged.

---

## Technical Constraints

### Applicable ADRs

| ADR | Title | Key Constraint |
|-----|-------|----------------|
| ADR-001 | Minimal API + BackgroundService | All endpoints as Minimal API. No Azure Functions. |
| ADR-004 | Async Job Contract | Idempotent handlers for any async work (batch archival). |
| ADR-007 | SpeFileStore Facade | Route all SPE/Graph file operations through `SpeFileStore`. No Graph SDK types leak above facade. |
| ADR-008 | Endpoint Filters for Auth | Use endpoint filters for authorization. No global middleware. |
| ADR-010 | DI Minimalism | Concrete registrations. Feature module extension (`AddCommunicationModule()`). ≤15 non-framework DI registrations. |
| ADR-013 | AI Architecture | Extend BFF, not separate service. Tool handlers implement `IAiToolHandler`. |
| ADR-019 | ProblemDetails & Errors | RFC 7807 ProblemDetails. Include `errorCode` extension. Never leak email content in errors. |

### MUST Rules

- MUST use Minimal API for `CommunicationEndpoints.cs`
- MUST use `GraphClientFactory.ForApp()` for shared mailbox Graph client
- MUST use endpoint filters for communication endpoint authorization
- MUST route SPE file operations through `SpeFileStore` facade (attachment downloads, archival uploads)
- MUST register services as concretes via `AddCommunicationModule()` feature extension
- MUST return ProblemDetails with error codes (`COMMUNICATION_SEND_FAILED`, `COMMUNICATION_NOT_FOUND`, `ATTACHMENT_TOO_LARGE`, `INVALID_RECIPIENT`)
- MUST include correlation ID in all error responses and logs
- MUST implement `SendCommunicationToolHandler` as `IAiToolHandler` for auto-discovery

### MUST NOT Rules

- MUST NOT inject `GraphServiceClient` outside `SpeFileStore`/`GraphClientFactory`
- MUST NOT create global authorization middleware for communication endpoints
- MUST NOT create interfaces without genuine implementation seams (use concretes)
- MUST NOT leak email content, recipient addresses, or API keys in error responses
- MUST NOT create Azure Functions for email processing
- MUST NOT modify existing `EmailEndpoints.cs` inbound pipeline

### Existing Patterns to Follow

| Pattern | Reference File | Usage |
|---------|---------------|-------|
| Minimal API endpoint structure | `src/server/api/Sprk.Bff.Api/Api/EmailEndpoints.cs` | Endpoint routing, group structure |
| Graph client creation | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` | `ForApp()` for app-only auth |
| AI tool handler | `src/server/api/Sprk.Bff.Api/Services/Ai/Tools/DataverseUpdateToolHandler.cs` | `IAiToolHandler` implementation |
| Dataverse record creation | `src/server/api/Sprk.Bff.Api/Services/DataverseWebApiService.cs` | Regarding field mapping, OData bind |
| Regarding field resolution | `src/server/api/Sprk.Bff.Api/Models/RegardingRecordType.cs` | Entity type → lookup field mapping |
| AssociationResolver PCF | `src/client/pcf/AssociationResolver/` | Reuse on communication form (no new PCF) |
| Send email via Graph | `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/SendEmailNodeExecutor.cs` | Graph sendMail pattern |
| SpeFileStore operations | `src/server/api/Sprk.Bff.Api/Services/Spe/SpeFileStore.cs` | Download/upload file content |

---

## Success Criteria

1. [ ] **G1**: Single `POST /api/communications/send` endpoint callable from UI, playbooks, and background jobs — Verify: Send email from API, Create Matter wizard, and AI tool
2. [ ] **G2**: No server-side sync dependency — Verify: Send email without user mailbox configuration
3. [ ] **G3**: `sprk_communication` records created for all outbound emails — Verify: Query Dataverse after send
4. [ ] **G4**: Association via AssociationResolver pattern on communication entity — Verify: AssociationResolver PCF works on form, OData filter by `_sprk_regardingmatter_value`
5. [ ] **G5**: SPE documents attached to outbound emails — Verify: Received email contains attached documents
6. [ ] **G6**: Sent emails archived as .eml in SPE — Verify: `sprk_document` record created with SourceType=CommunicationArchive
7. [ ] **G7**: Communication form for compose and view — Verify: Create and send email from form, view sent details
8. [ ] **G8**: AI playbooks can send emails via tool call — Verify: Playbook executes `send_communication` tool successfully
9. [ ] **G9**: Schema supports future channels — Verify: `sprk_communicationtype` choice includes SMS, Notification values

---

## Dependencies

### Prerequisites (Available)

| Dependency | Status | Required For |
|------------|--------|-------------|
| Graph SDK in BFF | Available | Phase 1 |
| `GraphClientFactory.ForApp()` | Available | Phase 1 |
| `authenticatedFetch` in workspace SPA | Available | Phase 1 |
| AssociationResolver PCF | Available (production) | Phase 2-3 |
| `RegardingRecordType` mapping | Available | Phase 2 |
| `SpeFileStore` facade | Available | Phase 4 |
| AI Tool Framework (`IAiToolHandler`) | Available | Phase 5 |
| `IDataverseService` | Available | Phase 2+ |

### Prerequisites (Need Configuration)

| Dependency | Action Required | Required For |
|------------|----------------|-------------|
| `Mail.Send` app permission | Add to app registration in Azure AD | Phase 1 |
| Shared mailbox | Create in Azure AD (or identify existing) | Phase 1 |
| Application access policy | Scope `Mail.Send` to specific mailbox | Phase 1 |
| `sprk_communication` entity | **ALREADY EXISTS** — add `sprk_hasattachments`, `sprk_attachmentcount` fields | Phase 4 |
| `sprk_recordtype_ref` entry | Add "Communication" record type | Phase 3 |
| `sprk_approvedsender` config entity | Optional manual creation in Dataverse | Phase 2+ |
| `sprk_communicationattachment` entity | Manual creation in Dataverse | Phase 4 |

---

## Entity Schema Reference

> **Source of truth**: `docs/data-model/sprk_communication-data-schema.md` (exported from Dataverse)
> **Entity status**: ALREADY EXISTS in Dataverse. No manual creation needed for Phase 2.

### sprk_communication — Actual Dataverse Schema

**IMPORTANT**: The design document used preliminary field names. The actual Dataverse entity has differences noted below. All BFF code MUST use the actual logical names.

| Category | Fields (Actual Logical Names) | Notes |
|----------|-------------------------------|-------|
| Core | `sprk_name` (Text 850, auto), `sprk_communiationtype` (choice), `statuscode` (standard status), `sprk_direction` (choice) | **Note**: Type field has typo: `sprk_communiationtype` (missing 'c'). Status uses standard Dataverse `statuscode`, NOT a custom field. |
| Email | `sprk_to` (Text 1000), `sprk_cc` (Text 1000), `sprk_bcc` (Text 1000), `sprk_from` (Text 1000), `sprk_subject` (Text 2000), `sprk_body` (Multiline 100000), `sprk_bodyformat` (choice) | Field sizes differ from design — use actual values. |
| Tracking | `sprk_graphmessageid` (Text 1000), `sprk_sentat` (DateTime), `sprk_sentby` (Lookup→systemuser), `sprk_errormessage` (Multiline 4000), `sprk_retrycount` (Whole number), `sprk_correlationid` (Text 100) | |
| Association | `sprk_regardingmatter`, `sprk_regardingproject`, `sprk_regardinginvoice`, `sprk_regardinganalysis`, `sprk_regardingorganization` (→sprk_organization), `sprk_regardingperson` (→contact), `sprk_regardingworkassignment`, `sprk_regardingbudget` + `sprk_regardingrecordname`, `sprk_regardingrecordid`, `sprk_regardingrecordtype` (→sprk_recordtype_ref), `sprk_regardingrecordurl`, `sprk_associationcount` | **Differs from design**: `sprk_regardingorganization` (not regardingaccount), `sprk_regardingperson` (not regardingcontact) |
| Attachments | NOT YET CREATED | `sprk_hasattachments` and `sprk_attachmentcount` need to be added in Phase 4 |

#### Choice Values (Actual)

| Field | Values |
|-------|--------|
| `sprk_communiationtype` | Email=100000000, Teams Message=100000001, SMS=100000002, Notification=100000003 |
| `statuscode` | Draft=1, Deleted=2, Queued=659490001, Send=659490002, Delivered=659490003, Failed=659490004, Bounded=659490005, Recalled=659490006 |
| `sprk_direction` | Incoming=100000000, Outgoing=100000001 |
| `sprk_bodyformat` | PlainText=100000000, HTML=100000001 |

#### Design-to-Actual Field Mapping

| Design Name | Actual Name | Reason |
|-------------|-------------|--------|
| `sprk_communicationtype` | `sprk_communiationtype` | Typo in Dataverse (missing 'c') — use actual |
| `sprk_communicationstatus` | `statuscode` | Standard Dataverse status field |
| `sprk_regardingaccount` | `sprk_regardingorganization` | Targets `sprk_organization` |
| `sprk_regardingcontact` | `sprk_regardingperson` | Targets `contact` |
| Status: Sent=100000002 | `statuscode`: Send=659490002 | Different values |
| Direction: Outbound=100000000 | `sprk_direction`: Outgoing=100000001 | Swapped values |

### sprk_communicationattachment (Phase 4)

| Field | Type | Purpose |
|-------|------|---------|
| `sprk_communication` | Lookup | Parent communication |
| `sprk_document` | Lookup | Linked SPE document |
| `sprk_attachmenttype` | Choice | File=100000000, InlineImage=100000001 |
| `sprk_name` | Text | Auto: document name |

---

## API Contract Reference

### POST /api/communications/send

```json
// Request
{
  "type": "email",
  "to": ["email@example.com"],
  "cc": [],
  "bcc": [],
  "subject": "Subject line",
  "body": "<p>HTML body</p>",
  "bodyFormat": "html",
  "fromMailbox": null,
  "associations": [
    { "entity": "sprk_matter", "id": "guid", "name": "Display Name", "role": "primary" }
  ],
  "attachmentDocumentIds": ["doc-guid-1"],
  "archiveToSpe": true,
  "containerId": "container-guid",
  "correlationId": "trace-id"
}

// Response (success)
{
  "communicationId": "comm-guid",
  "status": "sent",
  "graphMessageId": "AAMk...",
  "sentAt": "2026-02-20T14:30:00Z",
  "archivedDocumentId": "doc-guid-or-null",
  "warnings": []
}
```

---

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Phase scope | Should spec cover all 5 phases or subset? | All 5 phases (1-5) | Full task decomposition across all phases |
| Wizard changes | Include Create Matter wizard rewire in this project? | Yes, include in project | Tasks will reference workspace worktree files (`spaarke-wt-home-corporate-workspace-r1`) |
| Retry strategy | On Graph sendMail failure, retry or fail? | Fail immediately, return error | No background retry mechanism needed. Simplifies Phase 1. Retry can be added later. |
| Sender control | Restrict fromMailbox to whitelist? | BFF config + Dataverse override | Phase 1: approved senders in appsettings.json. Phase 2+: optional `sprk_approvedsender` Dataverse entity for admin-managed senders. Senders can be shared mailboxes, service accounts, role-based addresses — NOT required to be Dataverse users. `sprk_sentby` separately tracks initiating user. |

## Assumptions

- **Shared mailbox**: Will be configured in Azure AD before Phase 1 implementation begins. BFF reads mailbox address from configuration (`CommunicationOptions.DefaultMailbox`).
- **Exchange Online license**: Shared mailbox has appropriate Exchange Online license for Graph sendMail.
- **Application access policy**: `Mail.Send` permission scoped via application access policy (security best practice, not blocking for dev).
- **Template fields**: Include `templateId` and `templateData` in request model for API stability, but template rendering is out of scope.
- **Large attachments**: Files >3MB use standard base64 attachment (not large file upload session) until hitting 35MB limit. Large file upload session deferred.

## Unresolved Questions

- [ ] **Shared mailbox address**: What is the actual shared mailbox address (or will it be created)? — Blocks: Phase 1 configuration
- [ ] **Mail.Send permission**: Does the app registration already have `Mail.Send` application permission? — Blocks: Phase 1 testing

---

*AI-optimized specification. Original design: design.md*
