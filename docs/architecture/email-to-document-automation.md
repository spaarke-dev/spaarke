# Email-to-Document Automation Architecture

> **Last Updated**: January 2026
> **Status**: Production
> **Project**: email-to-document-automation-r2
> **Related ADRs**: ADR-001 (Minimal API + BackgroundService), ADR-008 (Endpoint Filters)

---

## Table of Contents

1. [Overview](#overview)
2. [System Architecture Diagram](#system-architecture-diagram)
3. [Process Flow](#process-flow)
4. [Component Reference](#component-reference)
5. [App-Only Authentication](#app-only-authentication)
6. [Data Models](#data-models)
7. [Configuration Reference](#configuration-reference)
8. [Error Handling](#error-handling)
9. [Telemetry and Monitoring](#telemetry-and-monitoring)
10. [Troubleshooting](#troubleshooting)

---

## Overview

The Email-to-Document Automation system automatically converts Dataverse Email activities into archived document records with the following capabilities:

| Feature | Description |
|---------|-------------|
| **Email Archival** | Converts emails to RFC 5322 compliant `.eml` files stored in SharePoint Embedded |
| **Attachment Processing** | Extracts, filters, and stores attachments as child documents |
| **Dual Intake Paths** | Webhook (real-time) and polling (backup) for email detection |
| **AI Integration** | Automatic handoff to Document Profile analysis playbook |
| **Idempotency** | Duplicate detection prevents re-processing of emails |

### High-Level Flow

```
Email Activity Created (Server-Side Sync / Outgoing)
        ↓
Webhook Trigger OR Polling Backup
        ↓
Job Queue (Azure Service Bus)
        ↓
EmailToDocumentJobHandler
        ↓
├── Convert to .eml (RFC 5322)
├── Upload to SPE Container
├── Create Dataverse Document Record
├── Process Attachments (filter, upload, link)
└── Enqueue AI Analysis Job
```

---

## System Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                          DATAVERSE ENVIRONMENT                                    │
│  ┌────────────────────────────────────────────────────────────────────────────┐ │
│  │  Email Activity (created via Server-Side Sync or Outlook)                   │ │
│  │  └─ activitymimeattachments (attachment records)                            │ │
│  └────────────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────────┘
                    │                                         │
                    │ Webhook (Create event)                  │ Polling Query
                    ↓                                         ↓
┌─────────────────────────────────────────────────────────────────────────────────┐
│                              SPRK.BFF.API                                         │
│  ┌─────────────────────────────────┐  ┌─────────────────────────────────────────┐│
│  │ EmailEndpoints.cs               │  │ EmailPollingBackupService.cs             ││
│  │ POST /api/v1/emails/webhook     │  │ BackgroundService (PeriodicTimer)        ││
│  │ ─────────────────────────────── │  │ ──────────────────────────────────────── ││
│  │ • Validate webhook signature    │  │ • Query unprocessed emails               ││
│  │ • Parse Dataverse payload       │  │ • Create jobs for missed webhooks        ││
│  │ • Submit job to Service Bus     │  │ • Run every N minutes (configurable)     ││
│  └─────────────────────────────────┘  └─────────────────────────────────────────┘│
│                    │                                    │                         │
│                    ↓                                    ↓                         │
│  ┌──────────────────────────────────────────────────────────────────────────────┐│
│  │                    JobSubmissionService.cs                                    ││
│  │  • Create JobContract (ProcessEmailToDocument)                               ││
│  │  • Set idempotency key: "Email:{emailId}:Archive"                           ││
│  │  • Send to Azure Service Bus queue                                           ││
│  └──────────────────────────────────────────────────────────────────────────────┘│
│                                       │                                           │
│                                       ↓                                           │
│  ┌──────────────────────────────────────────────────────────────────────────────┐│
│  │                    ServiceBusJobProcessor.cs                                  ││
│  │  • BackgroundService receiving from queue                                    ││
│  │  • Route to appropriate handler by JobType                                   ││
│  │  • Handle success/failure/retry logic                                        ││
│  └──────────────────────────────────────────────────────────────────────────────┘│
│                                       │                                           │
│                                       ↓                                           │
│  ┌──────────────────────────────────────────────────────────────────────────────┐│
│  │               EmailToDocumentJobHandler.cs (Core Processing)                 ││
│  │  ┌────────────────────────────────────────────────────────────────────────┐ ││
│  │  │ Step 1: Parse payload, validate emailId                                │ ││
│  │  │ Step 2: Idempotency check (skip if already processed)                  │ ││
│  │  │ Step 3: EmailToEmlConverter.ConvertToEmlAsync()                        │ ││
│  │  │ Step 4: SpeFileStore.UploadSmallAsync() → /emails/{filename}.eml       │ ││
│  │  │ Step 5: DataverseService.CreateDocumentAsync()                         │ ││
│  │  │ Step 6: DataverseService.UpdateDocumentAsync() (metadata + SPE refs)   │ ││
│  │  │ Step 7: ProcessAttachmentsAsync() → child documents                    │ ││
│  │  │ Step 8: EnqueueAiAnalysisJobAsync() → "Document Profile" playbook      │ ││
│  │  │ Step 9: Mark idempotency key as processed                              │ ││
│  │  └────────────────────────────────────────────────────────────────────────┘ ││
│  └──────────────────────────────────────────────────────────────────────────────┘│
│                    │                    │                    │                    │
│                    ↓                    ↓                    ↓                    │
│  ┌──────────────────┐  ┌──────────────────────────┐  ┌───────────────────────┐  │
│  │ EmailToEmlConverter│ │ AttachmentFilterService │  │ AiJobQueueService     │  │
│  │ ───────────────────│ │ ────────────────────────│  │ ─────────────────────│  │
│  │ • Fetch email data │ │ • Filter noise images   │  │ • Submit to AI queue │  │
│  │ • Fetch attachments│ │ • Block executables     │  │ • Playbook: Doc Prof │  │
│  │ • Build MimeMessage│ │ • Size validation       │  │ • Idempotency key    │  │
│  │ • RFC 5322 output  │ │ • Calendar file filter  │  │                      │  │
│  └──────────────────┘  └──────────────────────────┘  └───────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────────┘
                    │                                         │
                    │ App-Only Auth                           │ App-Only Auth
                    ↓                                         ↓
┌──────────────────────────────┐           ┌──────────────────────────────────────┐
│  SharePoint Embedded (SPE)   │           │  Dataverse Web API                   │
│  via Microsoft Graph API     │           │  ────────────────────────────────────│
│  ────────────────────────────│           │  • emails({id}) - fetch email data   │
│  • DriveItems (file storage) │           │  • activitymimeattachments - attachments│
│  • /emails/*.eml             │           │  • sprk_documents - document records │
│  • /emails/attachments/*     │           │  • systemusers - user lookup         │
└──────────────────────────────┘           └──────────────────────────────────────┘
```

---

## Process Flow

### 1. Email Intake

Emails enter the system through two paths:

#### Path A: Webhook (Real-time)

```
Dataverse Service Endpoint Configuration:
  Entity: email
  Message: Create
  Execution: Asynchronous
  Endpoint: POST /api/v1/emails/webhook-trigger
```

**File**: `src/server/api/Sprk.Bff.Api/Api/EmailEndpoints.cs` (lines 167-318)

```csharp
// Endpoint definition
app.MapPost("/api/v1/emails/webhook-trigger", WebhookTriggerAsync)
   .AllowAnonymous()  // Uses signature validation instead of JWT
   .WithDescription("Dataverse webhook endpoint for email create events");
```

**Webhook Validation**:
1. Check `EnableWebhook` configuration flag
2. Buffer request body for signature validation
3. Validate HMAC-SHA256 signature against `WebhookSecret`
4. Parse Dataverse webhook payload (handles `{guid}` format)
5. Validate entity type is "email"

**Webhook Payload Structure**:
```json
{
  "PrimaryEntityId": "{email-guid}",
  "MessageName": "Create",
  "BusinessUnitId": "{bu-guid}",
  "OrganizationId": "{org-guid}",
  "InitiatingUserId": "{user-guid}"
}
```

#### Path B: Polling Backup (Resilience)

**File**: `src/server/api/Sprk.Bff.Api/Services/Jobs/EmailPollingBackupService.cs`

```csharp
public class EmailPollingBackupService : BackgroundService
{
    // Runs on interval defined by PollingIntervalMinutes (default: 5)
    // Queries for emails where:
    //   - statecode = 1 (Completed)
    //   - createdon >= lookback period
    //   - sprk_documentprocessingstatus = null (not yet processed)
}
```

**OData Query**:
```
emails?$filter=statecode eq 1
  and createdon ge {lookbackDate}
  and sprk_documentprocessingstatus eq null
&$top={PollingBatchSize}
&$select=activityid,subject,createdon
```

### 2. Job Queue Processing

**File**: `src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs`

```
Azure Service Bus Configuration:
  Queue: sdap-jobs (configurable)
  Max Concurrent Calls: 5 (configurable)
  Lock Duration: 5 minutes
  Max Delivery Count: 5
```

**JobContract Structure**:
```csharp
public record JobContract
{
    public string JobId { get; init; }          // Unique job identifier
    public string JobType { get; init; }        // "ProcessEmailToDocument"
    public string SubjectId { get; init; }      // Email activity ID
    public string IdempotencyKey { get; init; } // "Email:{emailId}:Archive"
    public int Attempt { get; init; }           // Current attempt number
    public int MaxAttempts { get; init; }       // Default: 3
    public string Payload { get; init; }        // JSON payload
}
```

**Job Outcome Handling**:
| Outcome | Service Bus Action | Description |
|---------|-------------------|-------------|
| `Success` | Complete message | Remove from queue |
| `Failure` | Abandon message | Requeue for retry |
| `Poisoned` | Dead-letter | Move to DLQ, no retry |

### 3. Email-to-EML Conversion

**File**: `src/server/api/Sprk.Bff.Api/Services/Email/EmailToEmlConverter.cs`

**Interface**: `IEmailToEmlConverter`

#### Method: `ConvertToEmlAsync()`

```csharp
public async Task<EmlConversionResult> ConvertToEmlAsync(
    Guid emailActivityId,
    bool includeAttachments = true,
    CancellationToken cancellationToken = default)
{
    // 1. Authenticate with Dataverse (app-only)
    await EnsureAuthenticatedAsync(cancellationToken);

    // 2. Fetch email activity data
    var emailData = await FetchEmailActivityAsync(emailActivityId);

    // 3. Fetch attachments (with retry for race condition)
    var attachments = await FetchAttachmentsAsync(emailActivityId);

    // 4. Build RFC 5322 MimeMessage using MimeKit
    var mimeMessage = BuildMimeMessage(emailData, attachments);

    // 5. Write to MemoryStream
    var emlStream = new MemoryStream();
    mimeMessage.WriteTo(emlStream);
    emlStream.Position = 0;

    return EmlConversionResult.Succeeded(
        emlStream,
        ExtractMetadata(emailData),
        attachments,
        emlStream.Length);
}
```

#### Method: `FetchAttachmentsAsync()` (with retry logic)

```csharp
private async Task<List<EmailAttachmentInfo>> FetchAttachmentsAsync(Guid emailId)
{
    var attachments = await FetchAttachmentsInternalAsync(emailId);

    // Race condition fix: Webhook may fire before attachments are created
    if (attachments.Count == 0)
    {
        _logger.LogWarning("No attachments found, retrying after delay...");
        await Task.Delay(TimeSpan.FromSeconds(2));
        attachments = await FetchAttachmentsInternalAsync(emailId);
    }

    return attachments;
}
```

**OData Query for Attachments**:
```
activitymimeattachments?$filter=_objectid_value eq {emailId}
  &$select=activitymimeattachmentid,filename,mimetype,filesize,body
```

#### Method: `BuildMimeMessage()` (MimeKit)

```csharp
private MimeMessage BuildMimeMessage(EmailActivityData email, List<EmailAttachmentInfo> attachments)
{
    var message = new MimeMessage();

    // Headers
    message.From.Add(ParseAddress(email.Sender));
    message.To.AddRange(ParseAddresses(email.ToRecipients));
    message.Subject = email.Subject;
    message.Date = email.EmailDate ?? email.CreatedOn;
    message.MessageId = email.MessageId ?? MimeUtils.GenerateMessageId();

    // Body (HTML preferred, plain text fallback)
    var body = new TextPart(email.Description?.Contains("<") == true ? "html" : "plain")
    {
        Text = email.Description ?? "(No body)"
    };

    // Attachments
    if (attachments.Count > 0)
    {
        var multipart = new Multipart("mixed") { body };
        foreach (var att in attachments)
        {
            var attachment = new MimePart(att.MimeType)
            {
                Content = new MimeContent(att.Content),
                ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                ContentTransferEncoding = ContentEncoding.Base64,
                FileName = att.FileName
            };
            multipart.Add(attachment);
        }
        message.Body = multipart;
    }
    else
    {
        message.Body = body;
    }

    return message;
}
```

### 4. Attachment Processing

**File**: `src/server/api/Sprk.Bff.Api/Services/Email/AttachmentFilterService.cs`

#### Filtering Logic

```csharp
public IReadOnlyList<EmailAttachmentInfo> FilterAttachments(
    IEnumerable<EmailAttachmentInfo> attachments)
{
    return attachments.Where(att => !ShouldFilter(att)).ToList();
}

private bool ShouldFilter(EmailAttachmentInfo attachment)
{
    // 1. Pre-filtered by EML converter
    if (!attachment.ShouldCreateDocument) return true;

    // 2. Empty filename
    if (string.IsNullOrWhiteSpace(attachment.FileName)) return true;

    // 3. Blocked extensions (.exe, .dll, .bat, .ps1, etc.)
    var ext = Path.GetExtension(attachment.FileName)?.ToLowerInvariant();
    if (_options.BlockedAttachmentExtensions.Contains(ext)) return true;

    // 4. Calendar files (.ics, .vcs) - optional
    if (_options.FilterCalendarFiles && CalendarExtensions.Contains(ext)) return true;

    // 5. Inline attachments (embedded images) - optional
    if (_options.FilterInlineAttachments && attachment.IsInline) return true;

    // 6. Size exceeds limit
    if (attachment.SizeBytes > _options.MaxAttachmentSizeBytes) return true;

    // 7. Tracking pixel patterns
    if (IsTrackingPixel(attachment.FileName)) return true;

    // 8. Signature images (small images with signature-like names)
    if (IsSignatureImage(attachment)) return true;

    return false;
}
```

**Default Blocked Extensions**:
```
.exe, .dll, .bat, .cmd, .ps1, .vbs, .js, .msi, .scr, .com
```

**Signature Image Detection**:
- Pattern match: `image\d+`, `logo`, `signature`, `banner`
- Size threshold: < 5KB for images
- MIME type: `image/*`

#### Child Document Creation

**File**: `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/EmailToDocumentJobHandler.cs` (lines 499-584)

```csharp
private async Task ProcessSingleAttachmentAsync(
    EmailAttachmentInfo attachment,
    Guid parentDocumentId,
    string parentFileName,
    string parentGraphItemId,
    string driveId,
    string containerId,
    Guid emailId,
    CancellationToken ct)
{
    // 1. Reset stream position (may have been read during MIME building)
    if (attachment.Content?.CanSeek == true)
        attachment.Content.Position = 0;

    // 2. Upload to SPE
    var uploadPath = $"/emails/attachments/{parentDocumentId:N}/{attachment.FileName}";
    var fileHandle = await _speFileStore.UploadSmallAsync(driveId, uploadPath, attachment.Content, ct);

    // 3. Create child Document record
    var createRequest = new CreateDocumentRequest
    {
        Name = attachment.FileName,
        ContainerId = containerId,
        Description = $"Email attachment from {parentFileName}"
    };
    var childDocumentId = Guid.Parse(await _dataverseService.CreateDocumentAsync(createRequest, ct));

    // 4. Update with SPE references and parent link
    var updateRequest = new UpdateDocumentRequest
    {
        FileName = attachment.FileName,
        FileSize = attachment.SizeBytes,
        MimeType = attachment.MimeType,
        GraphItemId = fileHandle.Id,
        GraphDriveId = driveId,
        FilePath = fileHandle.WebUrl,
        HasFile = true,
        DocumentType = DocumentTypeEmailAttachment,  // 100000007

        // Parent relationship
        ParentDocumentLookup = parentDocumentId,
        ParentFileName = parentFileName,
        ParentGraphItemId = parentGraphItemId,
        RelationshipType = RelationshipTypeEmailAttachment  // 100000000

        // Note: EmailLookup is NOT set for child documents
        // (alternate key constraint - parent already uses it)
    };
    await _dataverseService.UpdateDocumentAsync(childDocumentId.ToString(), updateRequest, ct);

    // 5. Enqueue AI analysis for attachment
    await EnqueueAiAnalysisJobAsync(childDocumentId, "EmailAttachment", ct);
}
```

### 5. SPE File Upload

**Service**: `ISpeFileOperations` / `SpeFileStore` (ADR-007 facade)

```csharp
// File path conventions
Main email:   /emails/{YYYY-MM-DD_Subject}.eml
Attachments:  /emails/attachments/{parentDocumentId:N}/{filename}
```

**Upload Flow**:
```csharp
// 1. Resolve container to drive ID
var driveId = await _speFileStore.ResolveDriveIdAsync(containerId, ct);

// 2. Upload file (< 4MB uses simple upload)
var fileHandle = await _speFileStore.UploadSmallAsync(
    driveId,
    uploadPath,
    contentStream,
    ct);

// 3. fileHandle contains:
//    - Id: Graph item ID (for GraphItemId field)
//    - WebUrl: SharePoint URL (for FilePath field)
```

### 6. Dataverse Document Records

**Entity**: `sprk_document`

#### Parent Email Document Fields

| Dataverse Field | C# Property | Value |
|-----------------|-------------|-------|
| `sprk_name` | Name | Email subject |
| `sprk_filename` | FileName | `{date}_{subject}.eml` |
| `sprk_filesize` | FileSize | EML file size in bytes |
| `sprk_mimetype` | MimeType | `message/rfc822` |
| `sprk_graphitemid` | GraphItemId | SPE item ID |
| `sprk_graphdriveid` | GraphDriveId | SPE drive ID |
| `sprk_filepath` | FilePath | SharePoint URL |
| `sprk_hasfile` | HasFile | `true` |
| `sprk_documenttype` | DocumentType | `100000006` (Email) |
| `sprk_isemailarchive` | IsEmailArchive | `true` |
| `sprk_email` | EmailLookup | Email activity ID (lookup) |
| `sprk_emailsubject` | EmailSubject | Subject line |
| `sprk_emailfrom` | EmailFrom | Sender address |
| `sprk_emailto` | EmailTo | Recipients |
| `sprk_emailcc` | EmailCc | CC recipients |
| `sprk_emaildate` | EmailDate | Email timestamp |
| `sprk_emailbody` | EmailBody | Body (truncated) |
| `sprk_emailmessageid` | EmailMessageId | RFC 5322 Message-ID |
| `sprk_emaildirection` | EmailDirection | `100000000` (Received) / `100000001` (Sent) |
| `sprk_emailtrackingtoken` | EmailTrackingToken | Tracking token |
| `sprk_emailconversationindex` | EmailConversationIndex | Conversation index |

#### Child Attachment Document Fields

| Dataverse Field | C# Property | Value |
|-----------------|-------------|-------|
| `sprk_name` | Name | Attachment filename |
| `sprk_documenttype` | DocumentType | `100000007` (Email Attachment) |
| `sprk_parentdocument` | ParentDocumentLookup | Parent email document ID |
| `sprk_parentfilename` | ParentFileName | Parent .eml filename |
| `sprk_parentgraphitemid` | ParentGraphItemId | Parent SPE item ID |
| `sprk_relationshiptype` | RelationshipType | `100000000` (Email Attachment) |
| `sprk_email` | EmailLookup | **NOT SET** (alternate key constraint) |

**Important**: The `sprk_email` field has an alternate key constraint ("Email Activity Key"). Only the parent .eml document uses this lookup; child attachments relate to the email through their parent document.

### 7. AI Analysis Handoff

**File**: `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/EmailToDocumentJobHandler.cs` (lines 586-642)

```csharp
private async Task EnqueueAiAnalysisJobAsync(
    Guid documentId,
    string source,
    CancellationToken ct)
{
    if (!_options.AutoEnqueueAi)
    {
        _logger.LogDebug("AutoEnqueueAi disabled, skipping AI job for {DocumentId}", documentId);
        return;
    }

    var job = new JobContract
    {
        JobId = Guid.NewGuid().ToString(),
        JobType = "AppOnlyDocumentAnalysis",
        SubjectId = documentId.ToString(),
        IdempotencyKey = $"analysis-{documentId}-documentprofile",
        MaxAttempts = 3,
        Payload = JsonSerializer.Serialize(new
        {
            DocumentId = documentId,
            Source = source,  // "Email" or "EmailAttachment"
            EnqueuedAt = DateTime.UtcNow
        })
    };

    await _jobSubmissionService.SubmitJobAsync(job, ct);
    _telemetry.RecordAiJobEnqueued(source);
}
```

**AI Job Handler**: `AppOnlyDocumentAnalysisJobHandler`
- Job Type: `AppOnlyDocumentAnalysis`
- Default Playbook: `"Document Profile"`
- Process: Download from SPE → Extract text → Execute playbook tools → Update document

---

## App-Only Authentication

Email processing runs without user context (webhooks have no user session). All operations use **app-only authentication** via Client Credentials flow.

### Authentication Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  EmailToDocumentJobHandler (Background Job)                                  │
│  - No HttpContext available                                                 │
│  - No user token to exchange                                                │
└─────────────────────────────────────────────────────────────────────────────┘
                    │
                    ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│  ClientSecretCredential (Azure.Identity)                                    │
│  ─────────────────────────────────────────                                  │
│  Tenant ID:     {TENANT_ID}                                                 │
│  Client ID:     {API_APP_ID} (1e40baad-e065-4aea-a8d4-4b7ab273458c)        │
│  Client Secret: {API_CLIENT_SECRET}                                         │
└─────────────────────────────────────────────────────────────────────────────┘
                    │
        ┌───────────┴───────────┐
        ↓                       ↓
┌───────────────────┐   ┌───────────────────────────────────────────────────┐
│  Dataverse Scope  │   │  Graph Scope                                      │
│  ───────────────  │   │  ──────────                                       │
│  {orgUrl}/.default│   │  https://graph.microsoft.com/.default             │
│                   │   │                                                   │
│  Used for:        │   │  Used for:                                        │
│  • Email queries  │   │  • SPE file uploads                              │
│  • Attachment     │   │  • Container resolution                          │
│    fetching       │   │  • File operations                               │
│  • Document CRUD  │   │                                                   │
└───────────────────┘   └───────────────────────────────────────────────────┘
```

### Token Management Implementation

**File**: `src/server/api/Sprk.Bff.Api/Services/Email/EmailToEmlConverter.cs` (lines 492-518)

```csharp
private async Task EnsureAuthenticatedAsync(CancellationToken ct)
{
    // Check if current token is valid (with 5-minute buffer)
    if (_currentToken == null || _currentToken.Value.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
    {
        // Build scope for Dataverse
        var scope = $"{_apiUrl.Replace("/api/data/v9.2", "")}/.default";

        // Acquire new token using client credentials
        _currentToken = await _credential.GetTokenAsync(
            new TokenRequestContext([scope]),
            ct);

        // Set on HttpClient for subsequent requests
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _currentToken.Value.Token);
    }
}
```

### Configuration Requirements

**appsettings.json**:
```json
{
  "TENANT_ID": "{azure-ad-tenant-id}",
  "API_APP_ID": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
  "API_CLIENT_SECRET": "{client-secret}",
  "Dataverse": {
    "ServiceUrl": "https://spaarkedev1.crm.dynamics.com",
    "ClientSecret": "{client-secret}"
  }
}
```

**Azure AD App Registration**:
```
Application: spe-bff-api (1e40baad-e065-4aea-a8d4-4b7ab273458c)

API Permissions (Application - NOT Delegated):
  ✅ Microsoft Graph: Files.ReadWrite.All
  ✅ Microsoft Graph: Sites.ReadWrite.All
  ✅ Dynamics CRM: user_impersonation (for Dataverse access)

Dataverse Application User:
  ✅ Created in Power Platform Admin Center
  ✅ Security Role: System Administrator (or custom role with Document/Email access)
```

### App-Only vs OBO Comparison

| Aspect | App-Only (Email Processing) | OBO (User-Initiated Analysis) |
|--------|----------------------------|-------------------------------|
| **Trigger** | Webhook, background job | PCF control action |
| **User Context** | None | User token from MSAL.js |
| **Authentication** | ClientSecretCredential | UserAssertion + MSAL exchange |
| **SPE Permissions** | App registration permissions | User's SPE permissions |
| **Use Cases** | Email archival, bulk import | AI analysis, file preview |
| **HttpContext** | Not available | Required for OBO |

---

## Data Models

### EmailToDocumentPayload

```csharp
public class EmailToDocumentPayload
{
    public Guid EmailId { get; init; }
    public string TriggerSource { get; init; }  // "Webhook" or "PollingBackup"
    public string? MessageName { get; init; }    // "Create" or "Update"
    public string? WebhookCorrelationId { get; init; }
}
```

### EmlConversionResult

```csharp
public class EmlConversionResult
{
    public bool Success { get; init; }
    public Stream? EmlStream { get; init; }
    public EmailActivityMetadata? Metadata { get; init; }
    public IReadOnlyList<EmailAttachmentInfo> Attachments { get; init; }
    public string? ErrorMessage { get; init; }
    public long FileSizeBytes { get; init; }
}
```

### EmailActivityMetadata

```csharp
public class EmailActivityMetadata
{
    public Guid ActivityId { get; init; }
    public string Subject { get; init; }
    public string From { get; init; }
    public string To { get; init; }
    public string? Cc { get; init; }
    public string? Body { get; init; }
    public string? MessageId { get; init; }
    public int Direction { get; init; }  // 100000000=Received, 100000001=Sent
    public DateTime? EmailDate { get; init; }
    public string? TrackingToken { get; init; }
    public string? ConversationIndex { get; init; }
    public Guid? RegardingObjectId { get; init; }
    public string? RegardingObjectType { get; init; }
}
```

### EmailAttachmentInfo

```csharp
public class EmailAttachmentInfo
{
    public Guid AttachmentId { get; init; }
    public string FileName { get; init; }
    public string MimeType { get; init; }
    public Stream? Content { get; init; }
    public long SizeBytes { get; init; }
    public bool IsInline { get; init; }
    public string? ContentId { get; init; }
    public bool ShouldCreateDocument { get; init; }
    public string? SkipReason { get; init; }
}
```

### AttachmentProcessingResult

```csharp
public class AttachmentProcessingResult
{
    public int ExtractedCount { get; set; }   // Total attachments found
    public int FilteredCount { get; set; }    // Attachments filtered out
    public int UploadedCount { get; set; }    // Successfully created as documents
    public int FailedCount { get; set; }      // Failed to process
}
```

---

## Configuration Reference

**File**: `src/server/api/Sprk.Bff.Api/Configuration/EmailProcessingOptions.cs`

### General Settings

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `Enabled` | bool | `true` | Master switch for email processing |
| `DefaultContainerId` | string | Required | SPE container for email documents |
| `ProcessInbound` | bool | `true` | Process received emails |
| `ProcessOutbound` | bool | `true` | Process sent emails |

### Webhook Settings

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `EnableWebhook` | bool | `true` | Enable webhook endpoint |
| `WebhookSecret` | string | Required | HMAC-SHA256 secret for validation |

### Polling Settings

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `EnablePolling` | bool | `true` | Enable backup polling |
| `PollingIntervalMinutes` | int | `5` | Interval between polling runs |
| `PollingLookbackHours` | int | `24` | How far back to look for emails |
| `PollingBatchSize` | int | `100` | Max emails per polling run |

### Attachment Settings

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `MaxAttachmentSizeMB` | int | `25` | Max single attachment size |
| `MaxTotalSizeMB` | int | `100` | Max total attachments size |
| `BlockedAttachmentExtensions` | string[] | See below | Extensions to block |
| `FilterCalendarFiles` | bool | `true` | Skip .ics/.vcs files |
| `FilterInlineAttachments` | bool | `true` | Skip embedded images |
| `MinImageSizeKB` | int | `5` | Min image size (filter smaller) |

### AI Settings

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `AutoEnqueueAi` | bool | `true` | Auto-submit to AI analysis |

### Default Blocked Extensions

```csharp
BlockedAttachmentExtensions = new[]
{
    ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs",
    ".js", ".msi", ".scr", ".com", ".pif", ".application"
};
```

### Example Configuration

```json
{
  "EmailProcessing": {
    "Enabled": true,
    "DefaultContainerId": "b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50",
    "ProcessInbound": true,
    "ProcessOutbound": true,
    "EnableWebhook": true,
    "WebhookSecret": "{secret}",
    "EnablePolling": true,
    "PollingIntervalMinutes": 5,
    "PollingLookbackHours": 24,
    "PollingBatchSize": 100,
    "MaxAttachmentSizeMB": 25,
    "MaxTotalSizeMB": 100,
    "FilterCalendarFiles": true,
    "FilterInlineAttachments": true,
    "MinImageSizeKB": 5,
    "AutoEnqueueAi": true
  }
}
```

---

## Error Handling

### Job Processing Outcomes

| Scenario | Outcome | Action |
|----------|---------|--------|
| Email not found | `Poisoned` | Dead-letter, no retry |
| Attachment too large | Log warning | Skip attachment, continue |
| SPE upload failed (transient) | `Failure` | Retry up to MaxAttempts |
| SPE upload failed (permanent) | `Poisoned` | Dead-letter |
| Dataverse create failed | `Failure` | Retry |
| Idempotency duplicate | `Success` | Skip processing, complete |
| AI enqueue failed | Log error | Continue (non-fatal) |

### Retry Logic

```csharp
// In EmailToDocumentJobHandler.ProcessAsync()
catch (Exception ex)
{
    if (IsTransientError(ex))
    {
        // Retry: HttpRequestException, timeout, 429 rate limit
        return JobOutcome.Failure(ex.Message);
    }
    else
    {
        // Permanent failure: dead-letter the message
        return JobOutcome.Poisoned(ex.Message);
    }
}

private bool IsTransientError(Exception ex)
{
    return ex is HttpRequestException
        || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("429")
        || ex.Message.Contains("503");
}
```

### Attachment Processing Isolation

```csharp
// Attachment failures don't fail the main job
foreach (var attachment in filteredAttachments)
{
    try
    {
        await ProcessSingleAttachmentAsync(...);
        result.UploadedCount++;
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Failed to process attachment {FileName}", attachment.FileName);
        result.FailedCount++;
        // Continue with next attachment
    }
}
```

---

## Telemetry and Monitoring

### Telemetry Events

**File**: `src/server/api/Sprk.Bff.Api/Telemetry/EmailTelemetry.cs`

| Event | Description | Properties |
|-------|-------------|------------|
| `RecordWebhookReceived` | Webhook request received | `EmailId`, `CorrelationId` |
| `RecordWebhookRejected` | Validation failed | `Reason`, `Duration` |
| `RecordWebhookEnqueued` | Job submitted | `EmailId`, `JobId` |
| `RecordJobStart` | Job processing started | `EmailId`, `Attempt` |
| `RecordJobSuccess` | Job completed | `Duration`, `FileSize`, `AttachmentCount` |
| `RecordJobFailure` | Job failed | `Reason`, `Duration` |
| `RecordAttachmentProcessing` | Attachment stats | `Extracted`, `Filtered`, `Uploaded`, `Failed` |
| `RecordAiJobEnqueued` | AI job submitted | `DocumentType` |
| `RecordAiJobEnqueueFailure` | AI submission failed | `Reason` |

### Application Insights Queries

**Email Processing Success Rate**:
```kusto
customEvents
| where name == "EmailJobSuccess" or name == "EmailJobFailure"
| summarize
    SuccessCount = countif(name == "EmailJobSuccess"),
    FailureCount = countif(name == "EmailJobFailure")
    by bin(timestamp, 1h)
| extend SuccessRate = SuccessCount * 100.0 / (SuccessCount + FailureCount)
```

**Attachment Processing Stats**:
```kusto
customEvents
| where name == "AttachmentProcessing"
| summarize
    TotalExtracted = sum(toint(customDimensions.ExtractedCount)),
    TotalFiltered = sum(toint(customDimensions.FilteredCount)),
    TotalUploaded = sum(toint(customDimensions.UploadedCount)),
    TotalFailed = sum(toint(customDimensions.FailedCount))
    by bin(timestamp, 1d)
```

**Debug Logs (Warning Level)**:
```kusto
traces
| where message contains "[AttachmentProcessDebug]"
| where timestamp > ago(1h)
| project timestamp, message
| order by timestamp desc
```

---

## Troubleshooting

### Common Issues

#### 1. Attachments Not Processed

**Symptom**: Parent .eml created, but no child attachment documents.

**Causes**:
- Race condition: Webhook fires before attachments are created
- All attachments filtered out (signature images, tracking pixels)
- Attachment size exceeds limit

**Diagnosis**:
```kusto
traces
| where message contains "[AttachmentProcessDebug]"
| where message contains "{emailId}"
| project timestamp, message
```

**Resolution**:
- Check retry logic in `FetchAttachmentsAsync`
- Review filter rules in `AttachmentFilterService`
- Increase `MaxAttachmentSizeMB` if needed

#### 2. Child Document SPE Fields Empty

**Symptom**: Child documents created but `GraphItemId`, `GraphDriveId`, `FilePath` are null.

**Cause**: Alternate key violation on `sprk_email` field.

**Error**:
```
Entity Key Email Activity Key violated. A record with the same value for Email already exists.
```

**Resolution**: Ensure `EmailLookup` is NOT set for child documents (only parent uses it).

#### 3. AI Analysis Not Running

**Symptom**: Documents created but AI analysis status remains empty.

**Cause**: `ScopeResolverService.ResolvePlaybookScopesAsync` placeholder returns empty scopes.

**Diagnosis**:
```kusto
traces
| where message contains "Playbook has no tools configured"
| where timestamp > ago(1h)
```

**Resolution**: See [ai-analysis-integration-issue.md](../../projects/email-to-document-automation-r2/notes/ai-analysis-integration-issue.md)

#### 4. Webhook Validation Failing

**Symptom**: 401 Unauthorized on webhook endpoint.

**Causes**:
- `WebhookSecret` mismatch between Dataverse and BFF
- `EnableWebhook` set to `false`
- Signature calculation mismatch

**Diagnosis**:
```kusto
traces
| where message contains "Webhook signature validation failed"
| project timestamp, message
```

#### 5. Polling Service Overwhelming System

**Symptom**: Continuous document creation, many test records.

**Cause**: `EnablePolling = true` with short interval.

**Resolution**:
- Set `EnablePolling = false` for testing
- Increase `PollingIntervalMinutes`
- Add `sprk_documentprocessingstatus` filter to query

---

## Related Documentation

| Document | Description |
|----------|-------------|
| [sdap-auth-patterns.md](sdap-auth-patterns.md) | App-only authentication (Pattern 6) |
| [sdap-component-interactions.md](sdap-component-interactions.md) | Email-to-Document flow (Pattern 5) |
| [sdap-troubleshooting.md](sdap-troubleshooting.md) | General SDAP troubleshooting |
| [ai-analysis-integration-issue.md](../../projects/email-to-document-automation-r2/notes/ai-analysis-integration-issue.md) | Known AI analysis issue |

---

*Architecture document for email-to-document-automation-r2 project. Last updated January 2026.*
