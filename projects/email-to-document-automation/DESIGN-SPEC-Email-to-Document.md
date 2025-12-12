# Design Specification: Email to Document Feature

**Document Version:** 1.0  
**Status:** Draft  
**Date:** 2025-12-11  
**Authors:** Spaarke Engineering  
**Feature Code:** SDAP-EMAIL-DOC  

---

## 1. Executive Summary

### 1.1 Purpose

This specification defines the architecture and implementation approach for converting Power Platform email activities into SDAP Document records with associated .eml files stored in SharePoint Embedded (SPE). The feature bridges the gap between Server-Side Sync email activities (which have no physical file representation) and the SDAP document management pipeline.

### 1.2 Business Value

- **Compliance & Archival**: Emails stored as RFC 5322 compliant .eml files for legal discovery and retention requirements
- **AI Processing**: Emails enter the existing AIDocumentIntelligence pipeline for summarization and entity extraction
- **Unified Document Management**: All correspondence managed through consistent SDAP document workflows
- **Attachment Preservation**: Email attachments stored both embedded (complete archive) and as separate searchable documents

### 1.3 Scope

| In Scope | Out of Scope |
|----------|--------------|
| Inbound email processing (received) | Calendar items / meetings |
| Outbound email processing (sent) | Draft emails |
| Email attachments (embedded + separate) | Email threading / conversation grouping |
| Manual "Save to Document" action | Bulk migration of historical emails |
| Automatic rule-based processing | Exchange connector modifications |
| Integration with existing document pipeline | Changes to Server-Side Sync configuration |

---

## 2. Architecture Overview

### 2.1 Conceptual Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           EMAIL SOURCES                                     │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   ┌─────────────────────┐              ┌─────────────────────┐              │
│   │  Server-Side Sync   │              │   Manual Action     │              │
│   │  (Exchange → DV)    │              │   ("Save to Doc")   │              │
│   └──────────┬──────────┘              └──────────┬──────────┘              │
│              │                                    │                         │
│              │ email activity created             │ user clicks button      │
│              ▼                                    ▼                         │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │                     ENTRY POINT ROUTER                              │   │
│   │                                                                     │   │
│   │   ┌─────────────────────┐         ┌─────────────────────┐           │   │
│   │   │  Filter Engine      │         │  Direct API Call    │           │   │
│   │   │  (evaluate rules)   │         │  (bypass filters)   │           │   │
│   │   └──────────┬──────────┘         └──────────┬──────────┘           │   │
│   │              │                               │                      │   │
│   │              │ matches rule?                 │                      │   │
│   │              ▼                               │                      │   │
│   │   ┌─────────────────────┐                    │                      │   │
│   │   │  Auto: Queue job    │◄───────────────────┘                      │   │
│   │   │  Ignore: No action  │                                           │   │
│   │   │  Review: Flag only  │                                           │   │
│   │   └─────────────────────┘                                           │   │
│   └─────────────────────────────────────────────────────────────────────┘   │
│                              │                                              │
└──────────────────────────────┼──────────────────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                        PROCESSING PIPELINE                                  │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │  1. EML GENERATION SERVICE                                          │   │
│   │                                                                     │   │
│   │  • Fetch email record + metadata from Dataverse                     │   │
│   │  • Fetch all attachments (activitymimeattachment)                   │   │
│   │  • Generate RFC 5322 compliant .eml with embedded attachments       │   │
│   │  • Extract attachment streams for separate document creation        │   │
│   │                                                                     │   │
│   │  Output:                                                            │   │
│   │  • Complete .eml stream                                             │   │
│   │  • List of attachment metadata + streams                            │   │
│   │  • Email metadata (subject, sender, recipients, dates)              │   │
│   └──────────────────────────────────────────────────────────────────────┘  │
│                              │                                              │
│                              ▼                                              │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │  2. EXISTING DOCUMENT CREATION PIPELINE                             │   │
│   │                                                                     │   │
│   │  Called once for .eml (parent document):                            │   │
│   │  • Upload file stream to SPE (flat container)                       │   │
│   │  • Create sprk_document record                                      │   │
│   │  • Set email association fields                                     │   │
│   │  • Trigger AI processing                                            │   │
│   │                                                                     │   │
│   │  Called N times for attachments (child documents):                  │   │
│   │  • Upload attachment stream to SPE                                  │   │
│   │  • Create sprk_document record with parent reference                │   │
│   │  • Set relationship type = "Email Attachment"                       │   │
│   │  • Trigger AI processing for each                                   │   │
│   └─────────────────────────────────────────────────────────────────────┘   │
│                              │                                              │
│                              ▼                                              │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │  3. AI DOCUMENT INTELLIGENCE (EXISTING)                             │   │
│   │                                                                     │   │
│   │  • Process .eml file (supports this format)                         │   │
│   │  • Extract: sender, recipients, dates, key entities                 │   │
│   │  • Generate summary                                                 │   │
│   │  • Update sprk_document with AI results                             │   │
│   │                                                                     │   │
│   │  • Process each attachment separately                               │   │
│   │  • Individual summaries and entity extraction                       │   │
│   └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 2.2 ADR Compliance Matrix

| ADR | Requirement | This Design |
|-----|-------------|-------------|
| ADR-001 | Minimal API + BackgroundService; no Azure Functions | ✅ All processing via BackgroundService workers |
| ADR-002 | No heavy plugins; thin Dataverse integration | ✅ No plugins; webhook or polling only |
| ADR-003 | Lean authorization seams | ✅ Uses existing AuthorizationService |
| ADR-004 | Async job contract with uniform processing | ✅ Standard JobEnvelope via Service Bus |
| ADR-005 | Flat storage in SPE | ✅ Single container; metadata-based associations |
| ADR-006 | PCF over webresources | ✅ Ribbon button uses minimal JS; could be PCF |
| ADR-007 | SPE storage seam minimalism | ✅ Uses existing SpeFileStore |
| ADR-008 | Endpoint filters for authorization | ✅ API endpoints use existing filter pattern |
| ADR-009 | Redis-first caching | ✅ Filter rules cached in Redis |
| ADR-010 | DI minimalism | ✅ Minimal new registrations; reuse existing services |

---

## 3. Data Model

### 3.1 Entity Relationship Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              email (Activity)                               │
│                         [Dataverse System Entity]                           │
├─────────────────────────────────────────────────────────────────────────────┤
│  activityid (PK)                                                            │
│  subject                                                                    │
│  description (body)                                                         │
│  sender                                                                     │
│  torecipients                                                               │
│  ccrecipients                                                               │
│  bccrecipients                                                              │
│  directioncode (Outgoing = true, Incoming = false)                          │
│  statuscode (Sent = 3, Received = 4)                                        │
│  messageid (RFC 5322 Message-ID)                                            │
│  regardingobjectid → [sprk_matter | sprk_project | account | ...]           │
│                                                                             │
│  Child: activitymimeattachment (1:N)                                        │
└─────────────────────────────────────────────────────────────────────────────┘
          │                                          │
          │ 1:1                                      │ 1:N (via regarding)
          ▼                                          ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                           sprk_document                                     │
│                         [SDAP Document Entity]                              │
├─────────────────────────────────────────────────────────────────────────────┤
│  sprk_documentid (PK)                                                       │
│                                                                             │
│  -- Core Document Fields --                                                 │
│  sprk_documentname                                                          │
│  sprk_filename                                                              │
│  sprk_filesize                                                              │
│  sprk_documenttype (OptionSet: includes "Email" value)                      │
│                                                                             │
│  -- SPE Storage Fields --                                                   │
│  sprk_graphdriveid (container/drive ID)                                     │
│  sprk_graphitemid (file item ID)                                            │
│                                                                             │
│  -- Email Association Fields (EXISTING - verify schema) --                  │
│  sprk_email (Lookup → email)                      ◄─── Link to source email │
│  sprk_emailmessageid (text, RFC 5322 Message-ID)                            │
│  sprk_emailsubject (text)                                                   │
│  sprk_emailsender (text)                                                    │
│  sprk_emailreceiveddate (datetime)                                          │
│  sprk_emaildirection (OptionSet: Inbound/Outbound)                          │
│                                                                             │
│  -- Parent/Child Relationship Fields (EXISTING - verify schema) --          │
│  sprk_parentdocument (Lookup → sprk_document)     ◄─── For attachments      │
│  sprk_relationshiptype (OptionSet)                                          │
│      Values: "Source", "Related", "Email Attachment", "Version"             │
│                                                                             │
│  -- Business Context Lookups --                                             │
│  sprk_matter (Lookup → sprk_matter)                                         │
│  sprk_project (Lookup → sprk_project)                                       │
│  sprk_account (Lookup → account)                                            │
│  ... other entity lookups ...                                               │
│                                                                             │
│  -- AI Processing Fields --                                                 │
│  sprk_aisummary (multi-line text)                                           │
│  sprk_aiprocessingstate (OptionSet: Pending/Processing/Completed/Failed)    │
│  sprk_aiprocesseddate (datetime)                                            │
│  sprk_extractedentities (multi-line text / JSON)                            │
│                                                                             │
│  -- Audit Fields --                                                         │
│  createdon, modifiedon, createdby, modifiedby, ownerid                      │
└─────────────────────────────────────────────────────────────────────────────┘
          │
          │ Stored In
          ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                        SharePoint Embedded                                  │
│                         [Flat Container]                                    │
├─────────────────────────────────────────────────────────────────────────────┤
│  Container ID: {configured default container}                               │
│                                                                             │
│  Files:                                                                     │
│  • {subject}_{date}_{guid}.eml     (parent document - complete email)       │
│  • {attachment-name}_{guid}.pdf    (child document - extracted attachment)  │
│  • {attachment-name}_{guid}.docx   (child document - extracted attachment)  │
│                                                                             │
│  Metadata: Standard SPE metadata only; relationships managed in Dataverse   │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 3.2 New Entity: Email Save Rules

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        sprk_emailsaverule (NEW)                             │
│                    [Filter Rules for Auto-Processing]                       │
├─────────────────────────────────────────────────────────────────────────────┤
│  sprk_emailsaveruleid (PK)                                                  │
│  sprk_name (text) - Rule display name                                       │
│  sprk_isactive (boolean) - Enable/disable rule                              │
│  sprk_priority (int) - Evaluation order (lower = first)                     │
│                                                                             │
│  -- Action --                                                               │
│  sprk_action (OptionSet)                                                    │
│      • AutoSave - Automatically create document                             │
│      • Ignore - Do not process (explicit exclusion)                         │
│      • ReviewRequired - Create but flag for review                          │
│                                                                             │
│  -- Filter Conditions (all optional, AND logic) --                          │
│  sprk_direction (OptionSet: Inbound | Outbound | Both)                      │
│  sprk_senderdomain (text) - e.g., "client.com"                              │
│  sprk_sendercontains (text) - Partial match on sender address               │
│  sprk_recipientdomain (text) - Match on To/CC domains                       │
│  sprk_subjectcontains (text) - Partial match on subject                     │
│  sprk_subjectregex (text) - Regex pattern for subject (advanced)            │
│  sprk_hasattachments (boolean) - Must have / must not have attachments      │
│  sprk_regardingentitytype (text) - e.g., "sprk_matter", "account"           │
│  sprk_hasregardingobject (boolean) - Must be linked to a record             │
│  sprk_minimumattachmentcount (int) - Minimum number of attachments          │
│  sprk_excludesenderdomains (text) - Comma-separated domains to exclude      │
│                                                                             │
│  -- Options --                                                              │
│  sprk_createattachmentdocuments (boolean) - Create separate docs            │
│  sprk_defaultcontainer (text) - Override default container                  │
│                                                                             │
│  -- Audit --                                                                │
│  createdon, modifiedon, createdby, modifiedby, ownerid, statecode           │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 3.3 Document Relationship Pattern

```
Email Activity (activityid: AAA-111)
│
│   "Save to Document" action triggered
│
├──► sprk_document (Parent - EML File)
│    │  sprk_documentid: DOC-001
│    │  sprk_documentname: "Email - Contract Discussion.eml"
│    │  sprk_filename: "Contract_Discussion_2025-12-11_abc123.eml"
│    │  sprk_documenttype: "Email"
│    │  sprk_email: → AAA-111
│    │  sprk_parentdocument: NULL (this IS the parent)
│    │  sprk_relationshiptype: NULL or "Source"
│    │  sprk_matter: → MAT-001 (inherited from email.regardingobjectid)
│    │  sprk_graphitemid: "item-001"
│    │
│    └──► AIDocumentIntelligence processes .eml
│         sprk_aisummary: "Discussion regarding contract terms..."
│
├──► sprk_document (Child - Attachment 1)
│    │  sprk_documentid: DOC-002
│    │  sprk_documentname: "Contract_Draft_v3.docx"
│    │  sprk_filename: "Contract_Draft_v3_def456.docx"
│    │  sprk_documenttype: "Word Document"
│    │  sprk_email: → AAA-111
│    │  sprk_parentdocument: → DOC-001 (link to parent .eml)
│    │  sprk_relationshiptype: "Email Attachment"
│    │  sprk_matter: → MAT-001 (same as parent)
│    │  sprk_graphitemid: "item-002"
│    │
│    └──► AIDocumentIntelligence processes .docx
│         sprk_aisummary: "Draft contract with amendments..."
│
└──► sprk_document (Child - Attachment 2)
     │  sprk_documentid: DOC-003
     │  sprk_documentname: "Budget_Spreadsheet.xlsx"
     │  sprk_parentdocument: → DOC-001
     │  sprk_relationshiptype: "Email Attachment"
     │  ...
```

---

## 4. Component Design

### 4.1 Component Inventory

#### 4.1.1 New Components

| Component | Type | Responsibility |
|-----------|------|----------------|
| Email Save Rule Entity | Dataverse Entity | Store filter/routing rules |
| EML Generation Service | BFF Service | Convert email activity → .eml stream |
| Email Filter Engine | BFF Service | Evaluate rules, determine action |
| Email Document Orchestrator | BFF Service | Coordinate EML gen → existing pipeline |
| Email API Endpoints | Minimal API | Manual save endpoint, status queries |
| Email Webhook Endpoint | Minimal API | Receive Dataverse webhook notifications |
| Email Ribbon Command | JS Web Resource | "Save to Document" button handler |
| Email Processing Job Handler | BackgroundService | Async job handler for email processing |

#### 4.1.2 Existing Components (Reused)

| Component | How Used |
|-----------|----------|
| SpeFileStore | Upload .eml and attachment files to SPE |
| Document Creation Pipeline | Create sprk_document records |
| AIDocumentIntelligence | Process .eml and attachments |
| Job Contract (ADR-004) | Standard job envelope for async work |
| Service Bus Integration | Queue jobs for background processing |
| IDataverseService | Query email records, create document records |
| AuthorizationService | Validate user permissions for manual save |
| Redis Cache | Cache filter rules |

### 4.2 Service Descriptions

#### 4.2.1 EML Generation Service

**Purpose:** Converts a Dataverse email activity into an RFC 5322 compliant .eml file stream with embedded attachments.

**Pattern:** Stateless service, injected as scoped or singleton

**Dependencies:**
- IDataverseService (fetch email + attachments)
- MimeKit library (RFC 5322 generation)
- ILogger

**Key Operations:**

| Operation | Input | Output |
|-----------|-------|--------|
| GenerateEmlAsync | Email ID (GUID) | EmlGenerationResult |

**EmlGenerationResult Structure:**
```
EmlGenerationResult
├── EmlStream (Stream) - Complete .eml file with embedded attachments
├── FileName (string) - Sanitized filename for storage
├── Subject (string) - Email subject
├── SentOn (DateTime) - Send/receive timestamp
├── Direction (enum) - Inbound/Outbound
├── RegardingObjectId (EntityReference?) - Linked record
├── Sender (string) - Sender email address
├── Recipients (string[]) - To/CC recipients
├── AttachmentCount (int) - Number of attachments
├── TotalSize (long) - Total .eml size in bytes
├── Attachments (List<AttachmentInfo>) - Individual attachment metadata
    ├── FileName (string)
    ├── MimeType (string)
    ├── Size (long)
    ├── Stream (Stream) - Extracted attachment content
```

**RFC 5322 Compliance:**
- MIME-Version: 1.0
- Proper multipart/mixed structure for attachments
- Quoted-printable encoding for body
- Base64 encoding for binary attachments
- Proper header encoding for non-ASCII characters

**Library Recommendation:** MimeKit (NuGet package) for robust MIME handling

---

#### 4.2.2 Email Filter Engine

**Purpose:** Evaluates email save rules to determine if/how an email should be processed automatically.

**Pattern:** Stateless service with rule caching

**Dependencies:**
- IDataverseService (fetch rules, email metadata)
- IDistributedCache (Redis) for rule caching
- ILogger

**Key Operations:**

| Operation | Input | Output |
|-----------|-------|--------|
| EvaluateAsync | Email ID (GUID) | FilterResult |
| RefreshRulesCache | - | void |

**FilterResult Structure:**
```
FilterResult
├── Action (enum) - AutoSave | Ignore | ReviewRequired
├── MatchedRuleId (GUID?) - Which rule matched
├── MatchedRuleName (string?) - Rule display name
├── CreateAttachmentDocuments (bool) - From rule config
├── OverrideContainerId (string?) - If rule specifies container
├── Reason (string) - Human-readable explanation
```

**Rule Evaluation Logic:**
1. Fetch active rules from cache (or Dataverse if cache miss)
2. Sort rules by priority (ascending)
3. For each rule, evaluate all non-null conditions (AND logic)
4. First matching rule determines action
5. If no rules match, default action = Ignore

**Caching Strategy:**
- Cache key: `email-save-rules:active`
- TTL: 5 minutes (configurable)
- Invalidation: On rule create/update/delete (via webhook or manual refresh)

---

#### 4.2.3 Email Document Orchestrator

**Purpose:** Coordinates the full email-to-document conversion process, delegating to existing pipelines.

**Pattern:** Stateless orchestrator service

**Dependencies:**
- EML Generation Service
- Email Filter Engine (for auto-processing path)
- Existing Document Creation Service/Pipeline
- IDataverseService
- ILogger

**Key Operations:**

| Operation | Input | Output |
|-----------|-------|--------|
| ProcessEmailAsync | EmailProcessingRequest | EmailProcessingResult |
| ProcessEmailWithFilterAsync | Email ID | EmailProcessingResult (or null if ignored) |

**EmailProcessingRequest Structure:**
```
EmailProcessingRequest
├── EmailId (GUID) - Source email activity ID
├── CreateAttachmentDocuments (bool) - Create separate docs for attachments
├── OverrideContainerId (string?) - Override default container
├── TriggeredBy (enum) - Manual | AutoRule | Webhook
├── CorrelationId (string) - For distributed tracing
```

**EmailProcessingResult Structure:**
```
EmailProcessingResult
├── Success (bool)
├── ParentDocumentId (GUID) - The .eml document record
├── ParentDocumentName (string)
├── AttachmentDocumentIds (List<GUID>) - Child document records
├── AttachmentCount (int)
├── ProcessingState (enum) - Completed | PartialSuccess | Failed
├── FailedAttachments (List<FailureInfo>) - Any attachment failures
├── AiProcessingQueued (bool) - Whether AI jobs were queued
├── Errors (List<string>) - Any error messages
```

**Orchestration Flow:**
1. **Idempotency Check** - Query for existing sprk_document linked to this email
2. **Generate EML** - Call EML Generation Service
3. **Upload EML to SPE** - Call existing SpeFileStore
4. **Create Parent Document** - Call existing document creation pipeline
   - Set sprk_email lookup
   - Set email-specific fields (subject, sender, direction, etc.)
   - Inherit regarding object linkage (matter, project, etc.)
5. **Process Attachments** (if enabled)
   - For each attachment in EmlGenerationResult.Attachments:
     - Upload to SPE
     - Create child document with parent reference
     - Set relationship type = "Email Attachment"
6. **Queue AI Processing** - For parent and all children
7. **Return Result**

**Error Handling:**
- If EML generation fails: Return failure, no partial state
- If parent document creation fails: Return failure, attempt cleanup
- If attachment fails: Continue with others, record partial success
- All errors logged with correlation ID

---

#### 4.2.4 Email API Endpoints

**Purpose:** Expose HTTP endpoints for manual save operations and status queries.

**Pattern:** Minimal API endpoint group (per ADR-001)

**Endpoint Group:** `/api/emails`

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/{emailId}/save-as-document` | POST | User JWT | Manual save action |
| `/{emailId}/document-status` | GET | User JWT | Check if email has document |
| `/{emailId}/can-save` | GET | User JWT | Check if user can save this email |

**POST /{emailId}/save-as-document**

Request Body:
```
{
  "includeAttachments": true,
  "createSeparateAttachmentDocuments": true,
  "overrideContainerId": null
}
```

Response (200 OK):
```
{
  "documentId": "guid",
  "documentName": "Email - Subject.eml",
  "attachmentCount": 3,
  "attachmentDocumentIds": ["guid1", "guid2", "guid3"],
  "aiProcessingQueued": true
}
```

Response (409 Conflict):
```
{
  "error": "email_already_saved",
  "message": "Email already saved as document",
  "existingDocumentId": "guid"
}
```

**Authorization:**
- User must have read access to email activity
- User must have create permission on sprk_document
- Uses existing endpoint filter pattern (ADR-008)

---

#### 4.2.5 Email Webhook Endpoint

**Purpose:** Receive notifications from Dataverse when emails are created/updated for automatic processing.

**Pattern:** Lightweight webhook handler that queues jobs (ADR-001, ADR-002)

**Endpoint:** `POST /api/webhooks/email`

**Authentication:** Webhook secret validation (not user JWT)

**Logic:**
1. Validate webhook secret header
2. Parse Dataverse webhook payload (entity ID, message type, attributes)
3. Quick filter: Check email status (Sent/Received only)
4. Queue job to Service Bus with standard JobEnvelope
5. Return 202 Accepted immediately

**Important:** This endpoint does NO processing - only validation and queueing. All business logic happens in the BackgroundService handler.

---

#### 4.2.6 Email Processing Job Handler

**Purpose:** BackgroundService handler for async email-to-document processing.

**Pattern:** IJobHandler implementation per ADR-004

**Job Type:** `ProcessEmailToDocument`

**JobEnvelope Payload:**
```
{
  "emailId": "guid",
  "triggeredBy": "AutoRule",
  "filterResultAction": "AutoSave",
  "correlationId": "trace-id"
}
```

**Handler Flow:**
1. Deserialize job payload
2. Idempotency check (already processed?)
3. If triggered by webhook: Run filter engine to confirm action
4. If action = Ignore: Complete job, no further processing
5. If action = AutoSave or ReviewRequired:
   - Call Email Document Orchestrator
   - If ReviewRequired: Set flag on created document
6. Record job outcome
7. Handle failures per ADR-004 (retry, poison queue)

---

#### 4.2.7 Email Ribbon Command

**Purpose:** Power Apps ribbon button to manually save email as document.

**Type:** JavaScript web resource (minimal, button handler only)

**Ribbon Location:** Email entity main form command bar

**Display Logic:**
- Show button on email main form
- Enable only if email not already saved (query sprk_document)
- Enable only if user has create permission

**Button Action:**
1. Show progress indicator
2. Acquire access token (MSAL)
3. Call BFF API: POST /api/emails/{emailId}/save-as-document
4. Handle response:
   - Success: Show confirmation with link to document
   - Already saved: Show info with link to existing document
   - Error: Show error dialog
5. Optionally refresh form/subgrid

**Note:** Future consideration to convert to PCF control per ADR-006 if more complex UI needed.

---

## 5. Processing Flows

### 5.1 Manual Save Flow (User-Initiated)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  USER ACTION: Click "Save to Document" on Email Form                        │
└─────────────────────────────────────────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  1. RIBBON COMMAND (JavaScript)                                             │
│                                                                             │
│  • Extract email ID from form context                                       │
│  • Show progress indicator                                                  │
│  • Acquire access token via MSAL                                            │
│  • Call: POST /api/emails/{emailId}/save-as-document                        │
└─────────────────────────────────────────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  2. API ENDPOINT                                                            │
│                                                                             │
│  • Validate JWT, extract user context                                       │
│  • Check authorization (user can read email, create document)               │
│  • Check idempotency (document doesn't already exist for this email)        │
│  • Option A: Process synchronously (for immediate feedback)                 │
│  • Option B: Queue job, return 202 Accepted with job ID                     │
│                                                                             │
│  [ARCHITECT DECISION: Sync vs Async for manual save]                        │
└─────────────────────────────────────────────────────────────────────────────┘
         │
         ▼ (if synchronous)
┌─────────────────────────────────────────────────────────────────────────────┐
│  3. EMAIL DOCUMENT ORCHESTRATOR                                             │
│                                                                             │
│  3a. Generate EML                                                           │
│      └─► EML Generation Service                                             │
│          └─► Fetch email from Dataverse                                     │
│          └─► Fetch attachments from Dataverse                               │
│          └─► Build RFC 5322 .eml with MimeKit                               │
│          └─► Extract individual attachment streams                          │
│                                                                             │
│  3b. Upload .eml to SPE                                                     │
│      └─► Existing SpeFileStore.UploadFileAsync()                            │
│      └─► Returns: driveId, itemId                                           │
│                                                                             │
│  3c. Create parent document record                                          │
│      └─► Existing document creation pipeline                                │
│      └─► Set: sprk_email, email fields, regarding lookup                    │
│      └─► Set: sprk_aiprocessingstate = Pending                              │
│                                                                             │
│  3d. For each attachment:                                                   │
│      └─► Upload to SPE                                                      │
│      └─► Create child document record                                       │
│      └─► Set: sprk_parentdocument, relationship type                        │
│                                                                             │
│  3e. Queue AI processing jobs                                               │
│      └─► One job per document (parent + children)                           │
└─────────────────────────────────────────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  4. RESPONSE TO USER                                                        │
│                                                                             │
│  • Hide progress indicator                                                  │
│  • Show success message with document link                                  │
│  • Refresh related subgrids if applicable                                   │
└─────────────────────────────────────────────────────────────────────────────┘
         │
         ▼ (async)
┌─────────────────────────────────────────────────────────────────────────────┐
│  5. AI DOCUMENT INTELLIGENCE (Background)                                   │
│                                                                             │
│  • Process .eml file → summary, entity extraction                           │
│  • Process each attachment → individual summaries                           │
│  • Update sprk_document records with AI results                             │
│  • Set: sprk_aiprocessingstate = Completed                                  │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 5.2 Automatic Processing Flow (Rule-Based)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  TRIGGER: Email created via Server-Side Sync                                │
└─────────────────────────────────────────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  1. NOTIFICATION MECHANISM (Choose One)                                     │
│                                                                             │
│  Option A: Dataverse Webhook (Recommended)                                  │
│  • Dataverse sends HTTP POST to BFF API                                     │
│  • Immediate notification                                                   │
│  • Requires webhook registration                                            │
│                                                                             │
│  Option B: Polling Service                                                  │
│  • BackgroundService polls Dataverse periodically                           │
│  • Query: emails where createdon > last poll AND status = Sent/Received     │
│  • Interval: configurable (e.g., 30 seconds)                                │
│  • No external dependencies                                                 │
│                                                                             │
│  [ARCHITECT DECISION: Webhook vs Polling]                                   │
└─────────────────────────────────────────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  2. WEBHOOK ENDPOINT / POLL HANDLER                                         │
│                                                                             │
│  • Extract email ID from notification/query result                          │
│  • Quick filter: Is status = Sent (3) or Received (4)?                      │
│  • If not: Ignore (draft, pending, etc.)                                    │
│  • Create JobEnvelope with standard contract                                │
│  • Send to Service Bus queue                                                │
│  • Return/continue immediately                                              │
└─────────────────────────────────────────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  3. SERVICE BUS QUEUE                                                       │
│                                                                             │
│  Job Type: "ProcessEmailToDocument"                                         │
│  Standard retry policy per ADR-004                                          │
└─────────────────────────────────────────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  4. EMAIL PROCESSING JOB HANDLER (BackgroundService)                        │
│                                                                             │
│  4a. Idempotency check                                                      │
│      └─► Query: Does sprk_document exist for this email?                    │
│      └─► If yes: Complete job, log "already processed"                      │
│                                                                             │
│  4b. Evaluate filter rules                                                  │
│      └─► Email Filter Engine                                                │
│      └─► Returns: AutoSave | Ignore | ReviewRequired                        │
│                                                                             │
│  4c. If action = Ignore:                                                    │
│      └─► Complete job, log reason                                           │
│      └─► No document created                                                │
│                                                                             │
│  4d. If action = AutoSave or ReviewRequired:                                │
│      └─► Call Email Document Orchestrator                                   │
│      └─► If ReviewRequired: Set flag on document                            │
│                                                                             │
│  4e. Record job outcome                                                     │
│      └─► Success: Log document IDs created                                  │
│      └─► Failure: Log error, retry or poison queue per ADR-004              │
└─────────────────────────────────────────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  5. DOCUMENT CREATION (Same as Manual Flow)                                 │
│                                                                             │
│  • Generate EML                                                             │
│  • Upload to SPE                                                            │
│  • Create parent document                                                   │
│  • Create child documents for attachments                                   │
│  • Queue AI processing                                                      │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 5.3 Error and Failure Handling

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        FAILURE SCENARIOS                                    │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  SCENARIO 1: Email fetch fails (Dataverse error)                            │
│  ├─► Behavior: Job fails, enters retry cycle                                │
│  ├─► Retries: 3 attempts with exponential backoff                           │
│  └─► Final: Poison queue, alert for investigation                           │
│                                                                             │
│  SCENARIO 2: EML generation fails (corrupt data, encoding issues)           │
│  ├─► Behavior: Job fails, enters retry cycle                                │
│  ├─► Retries: 3 attempts                                                    │
│  └─► Final: Poison queue, document NOT created                              │
│                                                                             │
│  SCENARIO 3: SPE upload fails (network, throttling)                         │
│  ├─► Behavior: Job fails, enters retry cycle                                │
│  ├─► Existing resilience: SpeFileStore has Polly retry                      │
│  └─► Final: Poison queue if exhausted                                       │
│                                                                             │
│  SCENARIO 4: Parent document creation fails                                 │
│  ├─► Behavior: Job fails                                                    │
│  ├─► Cleanup: Attempt to delete uploaded SPE file                           │
│  └─► Final: No partial state; poison queue                                  │
│                                                                             │
│  SCENARIO 5: Attachment document creation fails (partial)                   │
│  ├─► Behavior: Continue processing remaining attachments                    │
│  ├─► Result: Partial success (parent + some children created)               │
│  ├─► Parent document: Marked with review flag                               │
│  └─► Log: Record which attachments failed                                   │
│                                                                             │
│  SCENARIO 6: AI processing fails                                            │
│  ├─► Behavior: Document exists, AI state = Failed                           │
│  ├─► User impact: Document visible, no summary                              │
│  └─► Recovery: Separate AI retry mechanism (existing)                       │
│                                                                             │
├─────────────────────────────────────────────────────────────────────────────┤
│                        DOCUMENT STATES                                      │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  sprk_aiprocessingstate values:                                             │
│  • Pending (100000000) - Document created, AI not started                   │
│  • Processing (100000001) - AI job running                                  │
│  • Completed (100000002) - AI completed successfully                        │
│  • Failed (100000003) - AI failed (document still visible)                  │
│  • Skipped (100000004) - AI not applicable for this file type               │
│                                                                             │
│  sprk_reviewrequired (boolean):                                             │
│  • true - Created by rule with ReviewRequired action, OR partial failure    │
│  • false - Normal processing                                                │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 6. Configuration

### 6.1 Application Settings

```
Email Processing Configuration
├── Email:DefaultContainerId (string)
│   └─► SPE container for email documents (uses main document container)
│
├── Email:ProcessInbound (bool, default: true)
│   └─► Enable processing of received emails
│
├── Email:ProcessOutbound (bool, default: true)
│   └─► Enable processing of sent emails
│
├── Email:MaxAttachmentSizeMB (int, default: 25)
│   └─► Maximum size per attachment to process
│
├── Email:MaxTotalSizeMB (int, default: 100)
│   └─► Maximum total email size (body + all attachments)
│
├── Email:BlockedAttachmentExtensions (string[], default: [".exe", ".dll", ".bat", ".ps1", ".vbs", ".js"])
│   └─► Attachment types to skip (security)
│
├── Email:FilterRuleCacheTtlMinutes (int, default: 5)
│   └─► TTL for filter rules cache
│
├── Email:DefaultAction (string, default: "Ignore")
│   └─► Action when no rules match: "Ignore" | "AutoSave" | "ReviewRequired"
│
├── Email:EnableWebhook (bool, default: true)
│   └─► Enable Dataverse webhook endpoint
│
├── Email:EnablePolling (bool, default: false)
│   └─► Enable polling as alternative/supplement to webhook
│
├── Email:PollingIntervalSeconds (int, default: 30)
│   └─► Polling frequency if enabled
│
└── Webhooks:EmailSecret (string)
    └─► Shared secret for webhook authentication
```

### 6.2 Dataverse Webhook Registration

If using webhook approach:

```
Webhook Registration
├── Name: "SDAP Email Document Processor"
├── Endpoint: https://{bff-api-host}/api/webhooks/email
├── Entity: email
├── Message: Create
├── Filtering Attributes: statuscode (optional - filter on status change)
├── Authentication: WebhookKey
└── Headers: x-webhook-key: {Webhooks:EmailSecret}
```

---

## 7. Security Considerations

### 7.1 Authorization Model

| Action | Required Permissions |
|--------|---------------------|
| Manual save | User read access to email + create on sprk_document |
| View document status | User read access to email |
| Auto-process (webhook) | Service identity (no user context) |
| Filter rule management | Admin role (separate from document permissions) |

### 7.2 Data Handling

| Concern | Mitigation |
|---------|------------|
| Email body may contain PII | Stored in SPE with existing document security |
| Attachments may be malicious | Extension blocklist; virus scanning (if available) |
| Email addresses exposed | Stored in existing email-related fields; no new exposure |
| Webhook authentication | Shared secret validation; IP allowlist (optional) |

### 7.3 Audit Trail

All email-to-document operations should be logged:
- Who triggered (user or auto-rule)
- Which rule matched (for auto-processing)
- Source email ID
- Created document IDs
- Timestamp
- Success/failure status

Use existing audit mechanisms (Dataverse audit, Application Insights).

---

## 8. Testing Strategy

### 8.1 Unit Tests

| Component | Test Focus |
|-----------|------------|
| EML Generation Service | RFC 5322 compliance, attachment encoding, header formatting |
| Email Filter Engine | Rule matching logic, priority ordering, edge cases |
| Email Document Orchestrator | Happy path, partial failures, idempotency |

### 8.2 Integration Tests

| Scenario | Description |
|----------|-------------|
| End-to-end manual save | Button → API → SPE → Dataverse → AI queue |
| End-to-end auto-process | Webhook → Filter → Job → Documents |
| Attachment handling | Email with 5 attachments of various types |
| Large email | Email near MaxTotalSizeMB limit |
| Filter rule evaluation | Multiple rules, priority ordering |

### 8.3 User Acceptance Tests

| Test Case | Steps | Expected Result |
|-----------|-------|-----------------|
| Manual save simple email | Open email → Click "Save to Document" | Document created, visible in related records |
| Manual save with attachments | Save email with 3 attachments | 4 documents created (1 parent + 3 children) |
| View saved document | Open created document record | Can download .eml, see AI summary |
| Auto-save rule | Create rule → Send matching email | Document created automatically |
| Already saved | Click "Save" on already-saved email | Show "already exists" with link |

---

## 9. Deployment Considerations

### 9.1 Database Changes

| Change | Type | Notes |
|--------|------|-------|
| sprk_emailsaverule entity | New | Filter rules storage |
| sprk_document fields | Verify | Confirm email association fields exist |
| sprk_document relationships | Verify | Confirm parent/child pattern supported |

### 9.2 Solution Components

| Component | Solution Layer |
|-----------|----------------|
| sprk_emailsaverule entity | Managed solution |
| Email ribbon customization | Managed solution |
| JS web resource for button | Managed solution |
| Email security roles | Managed solution |

### 9.3 BFF API Deployment

| Change | Notes |
|--------|-------|
| New endpoints | /api/emails/*, /api/webhooks/email |
| New services | EML Generation, Filter Engine, Orchestrator |
| New job handler | ProcessEmailToDocument |
| NuGet dependency | MimeKit library |
| Configuration | Email:* settings in appsettings |

### 9.4 Rollout Plan

**Phase 1: Manual Save Only**
- Deploy API endpoints and services
- Deploy ribbon button
- No auto-processing (rules disabled)
- User testing and feedback

**Phase 2: Auto-Processing**
- Deploy filter rule entity
- Enable webhook/polling
- Create initial rules (conservative)
- Monitor job processing

**Phase 3: Full Rollout**
- Refine rules based on usage
- Enable for all users
- Document admin procedures

---

## 10. Open Questions for Tech Architect

### 10.1 Existing Pipeline Integration

| Question | Context |
|----------|---------|
| Q1 | What is the exact method/endpoint for uploading a file to SPE? Need to call this for .eml and attachments. |
| Q2 | What is the exact method/endpoint for creating a sprk_document record? Need to understand required fields and any validation. |
| Q3 | How is AI processing triggered today? Need to queue the same job type for email documents. |
| Q4 | What job types already exist? Avoid collision with existing ProcessDocument-style handlers. |

### 10.2 Schema Verification

| Question | Context |
|----------|---------|
| Q5 | Does sprk_document have sprk_email lookup field? If not, needs to be added. |
| Q6 | Does sprk_document have sprk_parentdocument lookup? Need to confirm parent/child pattern. |
| Q7 | What is the sprk_relationshiptype option set? Need "Email Attachment" value. |
| Q8 | What is the sprk_documenttype option set? Need "Email" value. |

### 10.3 Architecture Decisions

| Question | Options |
|----------|---------|
| Q9 | Webhook vs Polling for auto-processing? Webhook is more immediate but requires registration. Polling is simpler but has latency. |
| Q10 | Sync vs Async for manual save? Sync gives immediate feedback but blocks UI longer. Async returns quickly but user must check later. |
| Q11 | Should manual save bypass filter rules entirely, or should filters be able to block even manual saves? |

### 10.4 Operational Decisions

| Question | Context |
|----------|---------|
| Q12 | Who manages filter rules? Need admin UI or just direct Dataverse access? |
| Q13 | What monitoring/alerting is needed? Integration with existing Application Insights? |
| Q14 | Retry policy for email processing - same as other jobs (3 retries) or different? |

---

## 11. Appendices

### Appendix A: RFC 5322 / MIME Reference

The .eml file format follows these standards:
- RFC 5322: Internet Message Format (headers, body structure)
- RFC 2045-2049: MIME (attachments, encoding)
- RFC 2047: MIME header encoding (non-ASCII characters)

Key headers required:
- From, To, Subject, Date (mandatory)
- Cc, Bcc (optional)
- Message-ID (should preserve from original if available)
- MIME-Version: 1.0 (for attachments)
- Content-Type: multipart/mixed (for attachments)

### Appendix B: MimeKit Library

Recommended library for .eml generation:
- NuGet: MimeKit (https://www.nuget.org/packages/MimeKit)
- License: MIT
- Features: Full MIME support, proper encoding, well-tested

Basic usage pattern:
```
Create MimeMessage
├── Set From, To, Cc, Subject, Date, MessageId
├── Create BodyBuilder
│   ├── Set HtmlBody or TextBody
│   └── Add Attachments
├── Assign Body = builder.ToMessageBody()
└── Write to Stream with message.WriteTo()
```

### Appendix C: Dataverse Email Activity Fields

Key fields on the email entity:
| Field | Type | Description |
|-------|------|-------------|
| activityid | GUID | Primary key |
| subject | String | Email subject |
| description | String | Email body (HTML) |
| sender | EntityReference | Sender (systemuser or contact) |
| from | ActivityParty[] | From addresses |
| torecipients | String | To addresses (semicolon-separated) |
| ccrecipients | String | CC addresses |
| bccrecipients | String | BCC addresses |
| directioncode | Boolean | true = Outgoing, false = Incoming |
| statuscode | OptionSet | 1=Draft, 2=Completed, 3=Sent, 4=Received, etc. |
| messageid | String | RFC 5322 Message-ID |
| regardingobjectid | EntityReference | Linked record (matter, account, etc.) |
| createdon | DateTime | Created timestamp |
| senton | DateTime | Sent timestamp |

Child entity for attachments:
| Entity | Field | Type | Description |
|--------|-------|------|-------------|
| activitymimeattachment | objectid | GUID | Parent email ID |
| activitymimeattachment | filename | String | Attachment filename |
| activitymimeattachment | mimetype | String | MIME type |
| activitymimeattachment | body | String | Base64 content |
| activitymimeattachment | filesize | Int | Size in bytes |

---

## 12. Document History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-12-11 | Spaarke Engineering | Initial draft |

---

## 13. Approval

| Role | Name | Date | Signature |
|------|------|------|-----------|
| Tech Architect | | | |
| Product Owner | | | |
| Security Review | | | |

---

*End of Design Specification*
