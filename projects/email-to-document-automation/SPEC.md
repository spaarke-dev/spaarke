# Email-to-Document Automation
## Design Specification

> **Version**: 1.0  
> **Date**: December 11, 2025  
> **Status**: Draft for Review  
> **Owner**: Spaarke Product / Document Management  
> **Related Projects**: [AI Document Intelligence](../ai-document-intelligence-r1/SPEC.md)

---

## Executive Summary

This specification defines the **Email-to-Document Automation** feature that converts Power Platform Email activities (received via Server-Side Sync) into SDAP Document records with RFC 5322 compliant `.eml` files stored in SharePoint Embedded (SPE). The feature bridges the gap between email activity records (which have no physical file representation) and the SDAP document management pipeline.

**Key Capabilities:**
- **Automatic Email Ingestion**: Background service monitors incoming emails and creates Documents
- **RFC 5322 Compliance**: Emails stored as standards-compliant .eml files for legal discovery
- **Intelligent Association**: Automatic linking to Matters, Accounts, or Contacts via email metadata and tracking tokens
- **Attachment Handling**: Both embedded (complete archive) and separated (searchable documents) storage
- **AI Processing**: Emails enter existing AI Document Intelligence pipeline for summarization and entity extraction
- **Manual Fallback**: "Save to Document" ribbon button for user-initiated conversion
- **Smart Filtering**: Rules engine prevents storage of unnecessary emails (signatures, logos, spam)

**Architecture Alignment:**
- **ADR-001**: BackgroundService worker + Service Bus; **no Azure Functions/Durable Functions** (exceptions require an ADR addendum)
- **ADR-002**: No heavy plugins; orchestration in BFF API
- **ADR-004**: Uniform async job contract for email processing
- **ADR-007**: All file operations via SpeFileStore facade
- **ADR-013**: AI processing via existing Document Intelligence pipeline

**Engineering Quality (Microsoft Senior Dev Practices):**
- **Idempotency by Design**: repeated triggers must produce at most one primary email Document
- **Durable Progress Tracking**: no reliance on "last poll window" timestamps alone
- **Bounded Concurrency + Backpressure**: parallelism is configured and SPE/Dataverse throttling is respected
- **Secure-by-Default**: per-record authorization checks for manual conversion APIs
- **Production Observability**: correlation IDs, metrics, and failure diagnostics without leaking PII

---

## 1. Architecture Context

### 1.1 Existing Foundation

This feature leverages proven SDAP components:

| Component | Status | Description |
|-----------|--------|-------------|
| **Sprk.Bff.Api** | ✅ Production | ASP.NET Core 8 Minimal API, orchestrates all backend services |
| **SpeFileStore** | ✅ Production | SharePoint Embedded file access facade |
| **IDataverseService** | ✅ Production | Dataverse entity CRUD operations |
| **BackgroundService Workers** | ✅ Production | Service Bus message processing |
| **AI Document Intelligence** | ✅ Production | Text extraction, summarization, entity extraction |
| **TextExtractorService** | ✅ Production | Multi-format text extraction (will support .eml) |
| **Document Creation Pipeline** | ✅ Production | Unified document creation workflow |

### 1.2 Server-Side Sync Architecture

Power Platform Server-Side Sync creates Email activity records from Exchange:

```
┌─────────────────────────────────────────────────────────────────┐
│                    Exchange / Microsoft 365                     │
│  User mailboxes (john@company.com, jane@company.com)           │
└────────────────────────┬────────────────────────────────────────┘
                         │ SMTP/Graph API
                         │ (Server-Side Sync)
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│                       Dataverse                                 │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │  Email Activity Records (email entity)                     │ │
│  │  • Subject                                                 │ │
│  │  • From (Party List)                                       │ │
│  │  • To (Party List)                                         │ │
│  │  • Body (HTML/Plain text)                                  │ │
│  │  • Sent Date                                               │ │
│  │  • TrackingToken (optional)                                │ │
│  │  • Regarding (Matter/Account/Contact)                      │ │
│  └────────────────────────────────────────────────────────────┘ │
│                                                                 │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │  ActivityMimeAttachment Records                            │ │
│  │  • FileName                                                │ │
│  │  • Body (base64 encoded file content)                      │ │
│  │  • MimeType                                                │ │
│  │  • FileSize                                                │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

**Key Characteristics:**
- No physical .eml or .msg files exist initially
- Email content stored as structured activity data
- Attachments stored in `activitymimeattachment` records (base64 encoded)
- No automatic file storage or document management

### 1.3 Target Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    Dataverse Email Activity                     │
│  Created by Server-Side Sync                                    │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         │ 1. Email created event
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│              EmailToDocumentBackgroundService                   │
│  (ADR-001 BackgroundService + Service Bus)                      │
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │ 1. Discover: Query new emails via IDataverseService      │  │
│  │ 2. Filter: Apply exclusion rules (spam, logos, etc.)     │  │
│  │ 3. Enqueue: Create ADR-004 job(s) on Service Bus         │  │
│  │    - ProcessEmailToDocumentJob(emailId, options, ...)    │  │
│  │    - ProcessEmailAttachmentsJob(emailId, parentDocId)    │  │
│  │ 4. Process: Worker handles jobs with retries/poisoning   │  │
│  │ 5. Convert: Generate RFC 5322 .eml file                  │  │
│  │ 6. Upload: Store .eml to SPE via SpeFileStore            │  │
│  │ 7. Associate: Match to Matter/Account/Contact            │  │
│  │ 8. Create: Document record with email metadata           │  │
│  │ 9. Enqueue: Trigger AI processing                        │  │
│  └──────────────────────────────────────────────────────────┘  │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│                     Sprk.Bff.Api                                │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │ /api/emails/convert-to-document (NEW)                    │  │
│  │ • POST - Manual conversion via ribbon button             │  │
│  │                                                           │  │
│  │ /api/emails/batch-process (NEW)                          │  │
│  │ • POST - Admin bulk processing                           │  │
│  │                                                           │  │
│  │ /api/emails/association-preview (NEW)                    │  │
│  │ • GET - Preview automatic associations                   │  │
│  └──────────────────────────────────────────────────────────┘  │
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │ Services (NEW)                                           │  │
│  │ • IEmailToEmlConverter - RFC 5322 generation             │  │
│  │ • IEmailAssociationService - Smart Matter linking        │  │
│  │ • IEmailFilterService - Exclusion rules engine           │  │
│  │ • IEmailAttachmentProcessor - Detach and store           │  │
│  └──────────────────────────────────────────────────────────┘  │
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │ Existing Services (REUSE)                                │  │
│  │ • SpeFileStore - File upload/storage                     │  │
│  │ • IDataverseService - Entity operations                  │  │
│  │ • ITextExtractor - .eml text extraction                  │  │
│  │ • AI Document Intelligence - Summarization               │  │
│  └──────────────────────────────────────────────────────────┘  │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│                     Result: sprk_document Records               │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │ Primary Document (Complete Email)                        │  │
│  │ • .eml file in SPE (with embedded attachments)           │  │
│  │ • Email metadata fields populated                        │  │
│  │ • Linked to Matter/Account/Contact                       │  │
│  │ • AI summary and entities extracted                      │  │
│  └──────────────────────────────────────────────────────────┘  │
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │ Attachment Documents (Individual Files)                  │  │
│  │ • Separate documents for each attachment                 │  │
│  │ • Files stored in SPE                                    │  │
│  │ • Linked to primary email document                       │  │
│  │ • AI processing for supported formats                    │  │
│  └──────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

---

## 2. Data Model

### 2.1 Existing Dataverse Entities

#### **Email Activity (Standard Entity)**

Power Platform standard entity for email activities:

| Field | Type | Description |
|-------|------|-------------|
| `activityid` | GUID | Primary key |
| `subject` | Text(200) | Email subject line |
| `description` | Memo | Email body (HTML or plain text) |
| `from` | Party List | Sender party list |
| `to` | Party List | Primary recipients |
| `cc` | Party List | CC recipients |
| `bcc` | Party List | BCC recipients |
| `sentedon` | DateTime | When email was sent |
| `receivedon` | DateTime | When email was received |
| `regardingobjectid` | Lookup | Related record (Matter/Account/Contact) |
| `trackingtoken` | Text(50) | Email tracking token |
| `directioncode` | Boolean | Incoming (true) / Outgoing (false) |
| `statecode` | Choice | Open, Completed, Cancelled |

#### **ActivityMimeAttachment (Standard Entity)**

Stores email attachments:

| Field | Type | Description |
|-------|------|-------------|
| `activitymimeattachmentid` | GUID | Primary key |
| `activityid` | Lookup | Parent email activity |
| `filename` | Text(255) | Attachment filename |
| `mimetype` | Text(256) | MIME type |
| `filesize` | Integer | File size in bytes |
| `body` | Memo | Base64 encoded file content |
| `subject` | Text(2000) | Attachment subject/description |

### 2.2 Extended sprk_document Entity

Extend existing `sprk_document` entity with email-specific fields:

| Display Name | Schema Name | Type | Required | Description |
|--------------|-------------|------|----------|-------------|
| **Email Subject** | `sprk_emailsubject` | Text(500) | ❌ | Email subject line (existing) |
| **Email From** | `sprk_emailfrom` | Text(1000) | ❌ | Sender email addresses (existing) |
| **Email To** | `sprk_emailto` | Text(2000) | ❌ | Recipient email addresses (existing) |
| **Email CC** | `sprk_emailcc` | Text(2000) | ❌ | CC email addresses (NEW) |
| **Email Date** | `sprk_emaildate` | DateTime | ❌ | Email sent/received date (existing) |
| **Email Body** | `sprk_emailbody` | Memo | ❌ | Email body text (truncated, existing) |
| **Email Direction** | `sprk_emaildirection` | Choice | ❌ | Incoming, Outgoing (NEW) |
| **Email Activity** | `sprk_emailactivityid` | Lookup(email) | ❌ | Source email activity record (NEW) |
| **Tracking Token** | `sprk_emailtrackingtoken` | Text(50) | ❌ | Email tracking token (NEW) |
| **Conversation Index** | `sprk_emailconversationindex` | Text(500) | ❌ | Email threading identifier (NEW) |
| **Is Email Archive** | `sprk_isemailarchive` | Two Options | ❌ | Indicates complete .eml archive (NEW) |
| **Parent Email Document** | `sprk_parentemaildocumentid` | Lookup(sprk_document) | ❌ | For attachments, link to parent email doc (NEW) |

**Idempotency / Uniqueness (Recommended):**
- Add an alternate key (unique constraint) on `sprk_document` for `(sprk_emailactivityid, sprk_isemailarchive)` so that the primary archive document cannot be created twice.
- For attachment documents, add an alternate key for `(sprk_parentemaildocumentid, <attachment identifier>)` where the identifier is stable (e.g., `activitymimeattachmentid`).

### 2.3 New Configuration Entity

#### **sprk_emailprocessingrule** (Email Processing Rules)

Defines rules for filtering and processing emails:

| Display Name | Schema Name | Type | Required | Description |
|--------------|-------------|------|----------|-------------|
| **Name** | `sprk_name` | Text(200) | ✅ | Rule name |
| **Rule Type** | `sprk_ruletype` | Choice | ✅ | Exclusion, Inclusion, Association |
| **Condition Type** | `sprk_conditiontype` | Choice | ✅ | Sender, Subject, Body, Attachment, Size |
| **Operator** | `sprk_operator` | Choice | ✅ | Contains, StartsWith, EndsWith, Regex, Equals |
| **Value** | `sprk_value` | Text(1000) | ✅ | Match value or regex pattern |
| **Action** | `sprk_action` | Choice | ✅ | Skip, Process, AssociateToMatter |
| **Priority** | `sprk_priority` | Whole Number | ✅ | Execution order (lower = higher priority) |
| **Is Active** | `statecode` | State | ✅ | Active/Inactive |
| **Description** | `sprk_description` | Memo | ❌ | Rule description |

**Default Rules:**
1. **Skip signature logos**: Attachment filename matches `signature.png|logo.gif|icon.jpg` → Skip
2. **Skip automated notifications**: Sender contains `noreply@|donotreply@|notifications@` → Skip
3. **Skip large emails**: Email size > 25MB → Skip (log warning)
4. **Process legal emails**: Sender domain matches `@lawfirm.com` → Process + Associate to Matter

### 2.3.1 New Operational State (Recommended)

Because polling windows are not durable across restarts, store processing state explicitly.

#### **Email Activity extensions** (Recommended)

- `sprk_documentprocessingstatus` (Choice): Pending, InProgress, Succeeded, Skipped, Failed
- `sprk_documentprocessingattempts` (Whole Number)
- `sprk_documentprocessingcorrelationid` (Text(50) or GUID)
- `sprk_documentprocessinglasterror` (Memo)
- `sprk_documentprocessedon` (DateTime)

#### **sprk_emailprocessingcheckpoint** (Optional)

Stores durable high-watermark per environment/worker instance (e.g., last processed `createdon` or change-tracking token) to avoid missing or duplicating work after restarts.

### 2.4 Entity Relationships

```
┌──────────────────────┐
│  Email Activity      │
│  (Standard)          │
└──────────┬───────────┘
           │ 1:N
           │
           ▼
┌──────────────────────┐         ┌──────────────────────┐
│ ActivityMimeAttachment│        │  sprk_document       │
│  (Standard)          │         │  (Extended)          │
└──────────────────────┘         │  • Email metadata    │
                                 │  • .eml file in SPE  │
                                 │  • AI summary        │
                                 └──────────┬───────────┘
                                            │ 1:N
                                            │ (attachments)
                                            ▼
                                 ┌──────────────────────┐
                                 │  sprk_document       │
                                 │  (Attachment)        │
                                 │  • File in SPE       │
                                 │  • Parent link       │
                                 └──────────────────────┘

┌──────────────────────┐
│sprk_emailprocessingrule│  (Consulted during processing)
│  (Configuration)     │
└──────────────────────┘
```

---

## 3. Service Layer

### 3.1 New Services

#### **IEmailToEmlConverter**

Converts Email activity to RFC 5322 compliant .eml file.

```csharp
/// <summary>
/// Converts Dataverse Email activities to RFC 5322 .eml files.
/// </summary>
public interface IEmailToEmlConverter
{
    /// <summary>
    /// Generate .eml file content from Email activity.
    /// Includes headers, body, and embedded attachments (MIME multipart).
    /// </summary>
    /// <param name="emailId">Email activity ID</param>
    /// <param name="includeAttachments">Embed attachments in .eml (default: true)</param>
    /// <returns>RFC 5322 compliant .eml content as stream</returns>
    Task<Stream> ConvertToEmlAsync(
        Guid emailId,
        bool includeAttachments = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate .eml filename from email metadata.
    /// Format: "{date}_{sanitizedSubject}_{from}.eml"
    /// </summary>
    /// <param name="emailId">Email activity ID</param>
    /// <returns>Safe filename for storage</returns>
    Task<string> GenerateEmlFileNameAsync(
        Guid emailId,
        CancellationToken cancellationToken = default);
}
```

**Implementation Notes:**
- Use `MimeKit` library (already in use for email parsing)
- Generate proper MIME multipart structure for attachments
- Preserve email headers (Message-ID, References, In-Reply-To for threading)
- Handle HTML and plain text bodies (multipart/alternative)
- Sanitize filename for SPE compatibility

**Compliance/Interoperability Notes (Recommended):**
- Ensure line endings are CRLF in the serialized `.eml` output and that required headers are present.
- If Dataverse does not provide a source `Message-Id`, generate a stable `Message-Id` (scoped to tenant) for threading; also persist it on the created document for later correlation.

#### **IEmailAssociationService**

Determines automatic Matter/Account/Contact associations.

```csharp
/// <summary>
/// Smart association of emails to SDAP entities (Matter, Account, Contact).
/// Uses sender, subject, body, tracking tokens, and threading.
/// </summary>
public interface IEmailAssociationService
{
    /// <summary>
    /// Determine best association for email based on multiple signals.
    /// Returns association recommendation with confidence score.
    /// </summary>
    /// <param name="emailId">Email activity ID</param>
    /// <returns>Association recommendation</returns>
    Task<EmailAssociationResult> DetermineAssociationAsync(
        Guid emailId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Preview association without applying.
    /// Used by UI to show user what would be linked.
    /// </summary>
    /// <param name="emailId">Email activity ID</param>
    /// <returns>Preview with explanation</returns>
    Task<EmailAssociationPreview> PreviewAssociationAsync(
        Guid emailId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply association to document record.
    /// </summary>
    /// <param name="documentId">Document ID</param>
    /// <param name="association">Association to apply</param>
    Task ApplyAssociationAsync(
        Guid documentId,
        EmailAssociationResult association,
        CancellationToken cancellationToken = default);
}

public record EmailAssociationResult(
    Guid? MatterId,
    Guid? AccountId,
    Guid? ContactId,
    decimal ConfidenceScore, // 0.0 - 1.0
    AssociationMethod Method, // TrackingToken, ThreadAnalysis, SenderMatch, etc.
    string Explanation); // Human-readable reason

public record EmailAssociationPreview(
    EmailAssociationResult PrimaryAssociation,
    EmailAssociationResult[] AlternativeAssociations,
    string[] Signals); // Matched signals (e.g., "Tracking token found", "Sender in contact")

public enum AssociationMethod
{
    None,
    TrackingToken,
    ThreadParent,
    SenderContact,
    SubjectMatterNumber,
    BodyMatterReference,
    RecentCommunication
}
```

**Association Logic Priority:**
1. **Tracking Token** (Confidence: 0.95) - Email has `trackingtoken` field populated
2. **Thread Parent** (Confidence: 0.90) - Email is reply to existing email with Matter
3. **Sender Contact Lookup** (Confidence: 0.80) - Sender email matches Contact → linked Account/Matter
4. **Subject Matter Number** (Confidence: 0.75) - Subject contains "Matter #12345"
5. **Body Matter Reference** (Confidence: 0.60) - Body contains matter number or client name
6. **Recent Communication** (Confidence: 0.50) - Sender communicated on Matter in last 30 days

#### **IEmailFilterService**

Applies exclusion and inclusion rules.

```csharp
/// <summary>
/// Evaluates processing rules to determine if email should be converted.
/// </summary>
public interface IEmailFilterService
{
    /// <summary>
    /// Check if email should be processed based on active rules.
    /// </summary>
    /// <param name="emailId">Email activity ID</param>
    /// <returns>Filter result with matched rules</returns>
    Task<EmailFilterResult> ShouldProcessEmailAsync(
        Guid emailId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluate specific rule against email.
    /// Used for testing and preview.
    /// </summary>
    /// <param name="emailId">Email activity ID</param>
    /// <param name="ruleId">Processing rule ID</param>
    /// <returns>True if rule matches</returns>
    Task<bool> EvaluateRuleAsync(
        Guid emailId,
        Guid ruleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all applicable rules for email (for audit/preview).
    /// </summary>
    /// <param name="emailId">Email activity ID</param>
    /// <returns>Matched rules in priority order</returns>
    Task<EmailProcessingRule[]> GetApplicableRulesAsync(
        Guid emailId,
        CancellationToken cancellationToken = default);
}

public record EmailFilterResult(
    bool ShouldProcess,
    string Reason,
    EmailProcessingRule[] MatchedRules);

public record EmailProcessingRule(
    Guid RuleId,
    string Name,
    RuleType Type,
    RuleAction Action,
    int Priority);
```

**Rule Evaluation:**
- Load active rules from `sprk_emailprocessingrule` (cached in Redis)
- Sort by priority (ascending)
- First matching rule with `Action = Skip` → Skip email
- If no skip rules match, check inclusion rules
- Default: Process if no explicit skip

#### **IEmailAttachmentProcessor**

Extracts and stores email attachments as separate documents.

```csharp
/// <summary>
/// Processes email attachments - both embedded and separated storage.
/// </summary>
public interface IEmailAttachmentProcessor
{
    /// <summary>
    /// Extract attachments from Email activity and create separate Documents.
    /// Filters out excluded attachments (signatures, logos).
    /// </summary>
    /// <param name="emailId">Email activity ID</param>
    /// <param name="parentDocumentId">Parent email document ID</param>
    /// <returns>Created attachment document IDs</returns>
    Task<Guid[]> ProcessAttachmentsAsync(
        Guid emailId,
        Guid parentDocumentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if attachment should be stored separately.
    /// Excludes small images, signatures, logos.
    /// </summary>
    /// <param name="attachmentId">ActivityMimeAttachment ID</param>
    /// <returns>True if should create document</returns>
    Task<bool> ShouldProcessAttachmentAsync(
        Guid attachmentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create single document from attachment.
    /// </summary>
    /// <param name="attachmentId">ActivityMimeAttachment ID</param>
    /// <param name="parentDocumentId">Parent email document ID</param>
    /// <returns>Created document ID</returns>
    Task<Guid> CreateDocumentFromAttachmentAsync(
        Guid attachmentId,
        Guid parentDocumentId,
        CancellationToken cancellationToken = default);
}
```

**Attachment Filtering Logic:**
- Skip if filename matches exclusion patterns (regex from rules)
- Skip if size < 5KB and MIME type is image/* (likely signature)
- Skip if filename is generic: `image001.png`, `spacer.gif`, etc.
- Process all documents, PDFs, Office files, and meaningful images

### 3.2 Background Service Worker

#### **EmailToDocumentBackgroundService**

Monitors new emails and processes them automatically.

```csharp
/// <summary>
/// Background service that monitors incoming emails and converts them to Documents.
/// Implements ADR-001 BackgroundService pattern.
/// </summary>
public class EmailToDocumentBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailToDocumentBackgroundService> _logger;
    private readonly EmailProcessingOptions _options;

    // Recommended: use PeriodicTimer rather than Timer + Task.Delay
    // and avoid keeping timers as fields unless they are actually used.

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EmailToDocumentBackgroundService started");

        var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessPendingEmailsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing emails");
            }
        }
    }

    private async Task ProcessPendingEmailsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dataverse = scope.ServiceProvider.GetRequiredService<IDataverseService>();
        var converter = scope.ServiceProvider.GetRequiredService<IEmailToEmlConverter>();
        var filter = scope.ServiceProvider.GetRequiredService<IEmailFilterService>();
        var association = scope.ServiceProvider.GetRequiredService<IEmailAssociationService>();
        var attachments = scope.ServiceProvider.GetRequiredService<IEmailAttachmentProcessor>();
        var speStore = scope.ServiceProvider.GetRequiredService<SpeFileStore>();

        // Recommended:
        // 1) Discover candidates using a durable checkpoint or Dataverse change tracking.
        // 2) Enqueue ADR-004 jobs to Service Bus.
        // 3) Process jobs with bounded parallelism and backpressure.
        var newEmails = await GetUnprocessedEmailsAsync(dataverse, ct);
        foreach (var emailId in newEmails.Take(_options.BatchSize))
        {
            await EnqueueEmailProcessingJobAsync(emailId, ct);
        }
    }

    private async Task ProcessSingleEmailAsync(
        Guid emailId,
        IDataverseService dataverse,
        IEmailToEmlConverter converter,
        IEmailFilterService filter,
        IEmailAssociationService association,
        IEmailAttachmentProcessor attachments,
        SpeFileStore speStore,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        _logger.LogInformation(
            "Processing email {EmailId} with correlation {CorrelationId}",
            emailId, correlationId);

        try
        {
            // 1. Check filter rules
            var filterResult = await filter.ShouldProcessEmailAsync(emailId, ct);
            if (!filterResult.ShouldProcess)
            {
                _logger.LogInformation(
                    "Email {EmailId} skipped: {Reason}",
                    emailId, filterResult.Reason);
                await MarkEmailAsProcessedAsync(emailId, skipped: true, ct);
                return;
            }

            // 2. Convert to .eml
            var emlStream = await converter.ConvertToEmlAsync(emailId, includeAttachments: true, ct);
            var emlFileName = await converter.GenerateEmlFileNameAsync(emailId, ct);

            // 3. Upload to SPE
            var containerId = await GetOrCreateEmailContainerAsync(dataverse, ct);
            var emlPath = $"emails/{DateTime.UtcNow:yyyy/MM}/{emlFileName}";
            var driveItem = await speStore.UploadSmallAsync(containerId, emlPath, emlStream, ct);

            // 4. Determine association
            var associationResult = await association.DetermineAssociationAsync(emailId, ct);

            // 5. Create Document record
            var documentId = await CreateEmailDocumentAsync(
                emailId, driveItem, associationResult, dataverse, ct);

            // 6. Process attachments (separate documents)
            var attachmentDocIds = await attachments.ProcessAttachmentsAsync(
                emailId, documentId, ct);

            // 7. Enqueue AI processing
            await EnqueueAiProcessingAsync(documentId, ct);

            // 8. Mark email as processed
            await MarkEmailAsProcessedAsync(emailId, skipped: false, ct);

            _logger.LogInformation(
                "Email {EmailId} processed successfully. Document: {DocumentId}, Attachments: {Count}",
                emailId, documentId, attachmentDocIds.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Failed to process email {EmailId} with correlation {CorrelationId}",
                emailId, correlationId);
            
            // Mark for retry or manual intervention
            await MarkEmailAsFailedAsync(emailId, ex.Message, ct);
        }
    }

    private async Task<Guid[]> GetUnprocessedEmailsAsync(
        IDataverseService dataverse, 
        CancellationToken ct)
    {
        // Query emails that are eligible for processing and not already succeeded.
        // Avoid relying solely on "now - poll interval" windows; prefer:
        // - Dataverse change tracking tokens; OR
        // - a durable high-watermark persisted in Dataverse/config storage.
        //
        // Also filter out emails that are not fully available yet:
        // - statecode = Completed (email fully received)
        // - (optional) attachments fully hydrated
        // FetchXML:
        // - directioncode = incoming (true)
        // - createdon > checkpoint
        // - NOT EXISTS (sprk_document where sprk_emailactivityid = email.activityid)
        // - sprk_documentprocessingstatus != Succeeded
        
        // Return array of email activity IDs
        return await dataverse.QueryAsync<Guid>(/* FetchXML */, ct);
    }

    private async Task MarkEmailAsProcessedAsync(
        Guid emailId, 
        bool skipped, 
        CancellationToken ct)
    {
        // Update email processing fields:
        // - sprk_documentprocessingstatus = Skipped/Succeeded
        // - sprk_documentprocessedon = utcNow
        // - sprk_documentprocessingattempts++
    }
}
```

**Configuration:**

```json
{
  "EmailProcessing": {
    "Enabled": true,
    "PollIntervalSeconds": 60,
    "BatchSize": 10,
    "ProcessAttachments": true,
    "AutoEnqueueAi": true,
    "MinimumConfidenceScore": 0.5,
    "DefaultContainerId": "b!..." // SPE container for emails
  }
}
```

### 3.3 Async Job Contract (ADR-004) & Processing Semantics

The system must assume **at-least-once** execution semantics for background work. “Exactly-once” delivery is not guaranteed, so correctness must be achieved via **idempotency** and **durable state**.

#### 3.3.1 Job Types

- `ProcessEmailToDocumentJob`
    - Creates the primary `.eml` archive document from a Dataverse Email activity.
- `ProcessEmailAttachmentsJob`
    - Creates attachment documents (optional, depending on configuration/request).
- `EnqueueAiProcessingJob`
    - Triggers the existing AI Document Intelligence pipeline for the created document(s).
- `BatchProcessEmailsJob` (Admin)
    - Enumerates a historical range and enqueues `ProcessEmailToDocumentJob` per email.

#### 3.3.2 Job Contract Schema (ADR-004)

All Service Bus messages MUST conform to the shared ADR-004 `JobContract` schema used across Spaarke.

```json
{
    "JobId": "guid",
    "JobType": "email-to-document",
    "SubjectId": "email-guid",
    "CorrelationId": "request-guid",
    "IdempotencyKey": "Email:{emailId}:Archive",
    "Attempt": 1,
    "MaxAttempts": 3,
    "Payload": {
        "trigger": "Auto|Manual|Batch",
        "requestedByUserId": "guid-or-null",
        "dataverseOrgUrl": "https://...",
        "traceparent": "00-...",
        "emailId": "guid",
        "includeAttachmentsInEml": true,
        "createSeparateAttachmentDocuments": true,
        "associationOverride": {
            "matterId": "guid-or-null",
            "accountId": "guid-or-null",
            "contactId": "guid-or-null"
        }
    }
}
```

**Rules:**
- `Payload` MUST NOT include email bodies or attachment bytes (avoid message bloat and PII leakage).
- `traceparent` SHOULD be passed via `Payload` or Service Bus application properties for end-to-end correlation.

#### 3.3.3 Payloads

**ProcessEmailToDocumentJob payload**

```json
{
    "emailId": "guid",
    "includeAttachmentsInEml": true,
    "createSeparateAttachmentDocuments": true,
    "associationOverride": {
        "matterId": "guid-or-null",
        "accountId": "guid-or-null",
        "contactId": "guid-or-null"
    }
}
```

**ProcessEmailAttachmentsJob payload**

```json
{
    "emailId": "guid",
    "parentEmailDocumentId": "guid"
}
```

**EnqueueAiProcessingJob payload**

```json
{
    "documentId": "guid"
}
```

#### 3.3.4 Idempotency & Concurrency Control

Recommended layered approach:

1. **Storage-level uniqueness**: enforce alternate keys (unique constraints) as described in §2.2.
2. **Record-level processing state**: use `sprk_documentprocessingstatus` and correlation fields (§2.3.1) to prevent duplicate work and aid support.
3. **Optimistic concurrency**: updates to the Email activity processing state should use conditional updates (ETag/version) to prevent two workers claiming the same email.

**Idempotency keys:**
- Archive doc: `Email:{emailId}:Archive`
- Attachment doc: `Email:{emailId}:Attachment:{activitymimeattachmentid}`

#### 3.3.5 Retry, Backoff, and Poison Handling

**Retry classification (Recommended):**
- **Transient** (retry): Dataverse throttling, Service Bus transient errors, SPE throttling/429/503, timeouts.
- **Permanent** (no retry): email deleted, missing permissions, invalid configuration, unsupported attachment encoding.

**Policy (Recommended defaults):**
- Max attempts: `3` (configurable)
- Backoff: exponential + jitter; respect `Retry-After` when provided
- Poison: after max attempts, send to DLQ and set `sprk_documentprocessingstatus = Failed` with a safe error code (no PII)

**DLQ operations (Recommended):**
- Admin-only endpoint/tooling to re-drive DLQ messages after remediation
- Audit trail for re-drive actions (who/when/why)

#### 3.3.6 Throttling, Bounded Concurrency, Backpressure

The worker must be configured with bounded concurrency to protect Dataverse and SPE.

Recommended controls:
- `EmailProcessing:MaxConcurrentEmails` (e.g., 4-10)
- `EmailProcessing:MaxConcurrentUploads` (e.g., 2-4)
- `EmailProcessing:PrefetchCount` (Service Bus)

When throttled (HTTP 429/503):
- honor `Retry-After`
- reduce concurrency temporarily (adaptive backoff)
- emit a telemetry signal (see §3.3.7)

#### 3.3.7 Observability (Application Insights)

**Correlation/Tracing:**
- Propagate `correlationId` and W3C `traceparent` from API → Service Bus → worker → downstream dependencies.
- Use structured logs with stable identifiers; avoid logging email addresses, subject, or body.

**Custom Events (Recommended):**
- `EmailToDocument.JobStarted`
- `EmailToDocument.JobSucceeded`
- `EmailToDocument.JobSkipped`
- `EmailToDocument.JobFailed`

**Common properties (Recommended):**
- `correlationId`, `jobId`, `jobType`, `trigger`, `emailId` (or hashed `emailIdHash`), `durationMs`
- `attachmentCount`, `emlSizeBytes`, `succeededAttachmentCount`, `skippedAttachmentCount`
- `failureCategory` (Authz, Dataverse, SPE, Conversion, Validation), `httpStatus` (if applicable)

**Metrics (Recommended):**
- Success/failure counts by `trigger`
- Processing latency (email created → document created)
- DLQ depth and age
- Throttle rate (count of 429/503) and average retry delay

**Alerts (Recommended):**
- DLQ depth > 0 for > 15 minutes
- Failure rate > X% over 15 minutes
- Processing lag > target (e.g., P95 > 2 minutes)

### 3.4 Integration with Existing Services

#### **Extend TextExtractorService for .eml**

```csharp
// Add to TextExtractorService
public async Task<string> ExtractTextFromEmlAsync(
    Stream emlStream,
    CancellationToken cancellationToken = default)
{
    using var message = await MimeMessage.LoadAsync(emlStream, cancellationToken);
    
    var textParts = message.BodyParts
        .OfType<TextPart>()
        .Where(p => p.IsPlain || p.IsHtml);
    
    var builder = new StringBuilder();
    foreach (var part in textParts)
    {
        var text = part.Text;
        if (part.IsHtml)
        {
            text = StripHtmlTags(text); // Existing method
        }
        builder.AppendLine(text);
    }
    
    return builder.ToString();
}
```

Add to `SupportedFileTypes` configuration:
```json
{
  "DocumentIntelligence": {
    "SupportedFileTypes": {
      ".eml": { "Enabled": true, "Method": "Native" }
    }
  }
}
```

---

## 4. API Endpoints

### 4.1 Manual Conversion Endpoint

#### **POST /api/emails/convert-to-document**

User-initiated conversion via "Save to Document" ribbon button.

**Authentication:** Bearer token (Azure AD)

**Authorization (Recommended):**
- Validate the caller has Dataverse read access to the Email activity record (and attachments, if included).
- Validate the caller has rights to create `sprk_document` in the target context (Matter/Account/etc.).
- When `associationOverride` is provided, validate the caller has read access to the referenced entity and permission to associate.

**Rate Limit:** 20 requests/minute per user

**Request Body:**
```json
{
  "emailId": "00000000-0000-0000-0000-000000000001",
  "includeAttachments": true,
  "associationOverride": {
    "matterId": "guid",      // Optional: Force specific association
    "accountId": "guid",
    "contactId": "guid"
  }
}
```

**Response:** `200 OK`
```json
{
  "documentId": "guid",
  "emailDocumentId": "guid",
  "attachmentDocumentIds": ["guid", "guid"],
  "association": {
    "matterId": "guid",
    "confidenceScore": 0.85,
    "method": "SenderContact",
    "explanation": "Sender john@client.com matches Contact linked to Matter #12345"
  },
  "driveId": "b!...",
  "itemId": "01ABC...",
  "webUrl": "https://..."
}
```

**Error Codes:**
- `400` - Invalid request (email not found, already processed)
- `404` - Email activity not found
- `429` - Rate limit exceeded
- `500` - Conversion failed

**Error Shape (Recommended):**
- Return RFC 7807 `ProblemDetails` including a `correlationId`/`traceId` so support can find logs.
- Avoid including email subject/body/addresses in error responses.

**Implementation:**

```csharp
app.MapPost("/api/emails/convert-to-document", async (
    ConvertEmailRequest request,
    IEmailToEmlConverter converter,
    IEmailAssociationService association,
    IEmailAttachmentProcessor attachments,
    SpeFileStore speStore,
    IDataverseService dataverse,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    var correlationId = Guid.NewGuid();
    logger.LogInformation("Manual email conversion: {EmailId}, Correlation: {CorrelationId}",
        request.EmailId, correlationId);

    try
    {
        // Recommended: enforce per-record authorization before proceeding.
        // e.g., await dataverse.AssertCanReadEmailAsync(request.EmailId, ct);

        // 1. Convert to .eml
        var emlStream = await converter.ConvertToEmlAsync(
            request.EmailId, 
            includeAttachments: request.IncludeAttachments, 
            ct);
        
        var emlFileName = await converter.GenerateEmlFileNameAsync(request.EmailId, ct);

        // 2. Upload to SPE
        var containerId = await GetContainerForEmailAsync(dataverse, ct);
        var emlPath = $"emails/manual/{DateTime.UtcNow:yyyy/MM}/{emlFileName}";
        var driveItem = await speStore.UploadSmallAsync(containerId, emlPath, emlStream, ct);

        // 3. Determine or apply association
        var associationResult = request.AssociationOverride != null
            ? CreateAssociationFromOverride(request.AssociationOverride)
            : await association.DetermineAssociationAsync(request.EmailId, ct);

        // 4. Create Document
        var documentId = await CreateEmailDocumentAsync(
            request.EmailId, driveItem, associationResult, dataverse, ct);

        // 5. Process attachments if requested
        var attachmentIds = request.IncludeAttachments
            ? await attachments.ProcessAttachmentsAsync(request.EmailId, documentId, ct)
            : Array.Empty<Guid>();

        // 6. Enqueue AI processing
        await EnqueueAiProcessingAsync(documentId, ct);

        return Results.Ok(new ConvertEmailResponse
        {
            DocumentId = documentId,
            EmailDocumentId = documentId,
            AttachmentDocumentIds = attachmentIds,
            Association = associationResult,
            DriveId = driveItem.ParentReference.DriveId,
            ItemId = driveItem.Id,
            WebUrl = driveItem.WebUrl
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to convert email {EmailId}", request.EmailId);
        return Results.Problem(
            title: "Email conversion failed",
            statusCode: 500,
            extensions: new Dictionary<string, object?>
            {
                ["correlationId"] = correlationId
            });
    }
})
.RequireAuthorization()
.RequireRateLimiting("email-convert");
```

### 4.2 Batch Processing Endpoint

#### **POST /api/emails/batch-process**

Admin endpoint for bulk processing of historical emails.

**Authentication:** Bearer token (Admin role required)

**Rate Limit:** 5 requests/minute per user

**Behavior (Required):**
- This endpoint **enqueues** a background batch job (ADR-004) and returns immediately.
- The API MUST NOT synchronously convert emails in the request thread.
- The batch job enumerates eligible emails and enqueues `email-to-document` jobs (one per email).

**Request Body:**
```json
{
  "startDate": "2025-01-01T00:00:00Z",
  "endDate": "2025-12-11T23:59:59Z",
  "senderFilter": "@lawfirm.com",     // Optional
  "regardingFilter": {                // Optional
    "entityType": "sprk_matter",
    "entityId": "guid"
  },
  "dryRun": false
}
```

**Response:** `202 Accepted`
```json
{
  "jobId": "guid",
  "estimatedCount": 1250,
  "status": "queued"
}
```

#### 4.2.1 Job Status Contract (Recommended)

The status endpoint returns a **stable contract** backed by persisted job state (e.g., a `JobOutcome` store) so that admins can monitor progress and re-drive failures.

**Job Status Fields:**
- `jobId` (GUID), `jobType` (string), `status` (`queued|processing|succeeded|failed|cancelled`)
- `requestedByUserId` (GUID?), `correlationId` (GUID)
- `startedAt`, `completedAt`, `lastUpdatedAt` (UTC)
- Progress counters: `processed`, `total`, `succeeded`, `skipped`, `failed`
- Failure diagnostics: `failureCategory` + `errorCode` (no PII)

**Progress semantics:**
- For batch jobs, counters reflect **emails enumerated and processed** (not individual attachments).
- For per-email jobs, counters MAY be omitted or represent a single unit of work.

**Data source:**
- Persist job lifecycle updates (`JobStarted`, `JobSucceeded`, `JobFailed`, etc.) and progress increments as part of ADR-004 processing.
- The status endpoint reads from that store; it MUST NOT derive state by scanning Dataverse on demand.

**Status Endpoint:** `GET /api/emails/batch-process/{jobId}/status`

```json
{
  "jobId": "guid",
  "status": "processing",
  "processed": 450,
  "total": 1250,
  "succeeded": 420,
  "skipped": 25,
  "failed": 5,
  "startedAt": "2025-12-11T10:00:00Z",
    "estimatedCompletion": "2025-12-11T10:45:00Z",
    "lastUpdatedAt": "2025-12-11T10:12:34Z"
}
```

### 4.3 Association Preview Endpoint

#### **GET /api/emails/association-preview**

Preview automatic association before processing.

**Query Parameters:**
- `emailId` (required): Email activity ID

**Response:** `200 OK`
```json
{
  "emailId": "guid",
  "primaryAssociation": {
    "matterId": "guid",
    "matterNumber": "M-12345",
    "matterName": "Smith v. Jones Litigation",
    "confidenceScore": 0.85,
    "method": "TrackingToken",
    "explanation": "Email tracking token matched active Matter M-12345"
  },
  "alternativeAssociations": [
    {
      "accountId": "guid",
      "accountName": "Smith LLC",
      "confidenceScore": 0.60,
      "method": "SenderContact",
      "explanation": "Sender john@smith.com is primary contact for Smith LLC"
    }
  ],
  "signals": [
    "Tracking token: TK-12345-XYZ",
    "Sender email: john@smith.com matches Contact record",
    "Subject contains matter number: M-12345",
    "Email is reply to thread associated with Matter"
  ]
}
```

---

## 5. UI Components

### 5.1 Email Form Ribbon Button

Add "Save to Document" button to Email activity form ribbon.

**Button Definition:**

```xml
<Button
  Id="sprk.Email.SaveToDocument"
  Command="sprk.Email.SaveToDocumentCommand"
  Sequence="50"
  LabelText="Save to Document"
  ToolTipTitle="Convert Email to Document"
  ToolTipDescription="Save this email as a .eml document with attachments stored in SharePoint Embedded"
  Image16by16="/_imgs/ribbon/document_16.png"
  Image32by32="/_imgs/ribbon/document_32.png"
  TemplateAlias="o1" />

<CommandDefinition Id="sprk.Email.SaveToDocumentCommand">
  <EnableRules>
    <EnableRule Id="sprk.Email.IsCompleted" />
    <EnableRule Id="sprk.Email.NotAlreadyConverted" />
  </EnableRules>
  <DisplayRules>
    <DisplayRule Id="sprk.Email.AlwaysVisible" />
  </DisplayRules>
  <Actions>
    <JavaScriptFunction
      Library="$webresource:sprk_emailactions.js"
      FunctionName="Sprk.Email.convertToDocument" />
  </Actions>
</CommandDefinition>
```

**JavaScript Handler:**

```typescript
// sprk_emailactions.ts
namespace Sprk.Email {
    export async function convertToDocument(
        primaryControl: Xrm.FormContext
    ): Promise<void> {
        const emailId = primaryControl.data.entity.getId();
        
        // Show confirmation dialog
        const confirmResult = await Xrm.Navigation.openConfirmDialog({
            title: "Convert Email to Document",
            text: "This will create a Document record with a .eml file and process all attachments. Continue?",
            confirmButtonLabel: "Convert",
            cancelButtonLabel: "Cancel"
        });

        if (!confirmResult.confirmed) {
            return;
        }

        // Show progress indicator
        Xrm.Utility.showProgressIndicator("Converting email to document...");

        try {
            const apiClient = new SdapApiClient();
            const response = await apiClient.post<ConvertEmailResponse>(
                "/api/emails/convert-to-document",
                {
                    emailId: emailId,
                    includeAttachments: true
                }
            );

            Xrm.Utility.closeProgressIndicator();

            // Show success notification
            await Xrm.Navigation.openAlertDialog({
                title: "Email Converted",
                text: `Document created successfully.\n\nDocument ID: ${response.documentId}\nAttachments: ${response.attachmentDocumentIds.length}`,
                confirmButtonLabel: "Open Document"
            });

            // Navigate to document record
            Xrm.Navigation.navigateTo({
                pageType: "entityrecord",
                entityName: "sprk_document",
                entityId: response.documentId
            });

        } catch (error) {
            Xrm.Utility.closeProgressIndicator();
            
            await Xrm.Navigation.openErrorDialog({
                message: `Failed to convert email: ${error.message}`
            });
        }
    }
}
```

### 5.2 Email Processing Dashboard (Admin)

Custom page for monitoring email-to-document processing.

**Features:**
- View processing statistics (processed, skipped, failed)
- Filter by date range, sender, matter
- Manual retry for failed emails
- Bulk processing interface
- Rule configuration UI

**Implementation:** Power Apps Custom Page with TypeScript PCF control.

---

## 6. Implementation Phases

### Phase 1: Core Conversion Infrastructure (Week 1-2)

**Goal:** Establish basic email-to-document conversion

**Tasks:**
1. **Data Model Extensions**
   - Add email-specific fields to `sprk_document`
   - Create `sprk_emailprocessingrule` entity
   - Add custom fields to Email activity (sprk_documentprocessed)
   - Configure security roles
   
2. **Service Implementation**
   - Implement `IEmailToEmlConverter` using MimeKit
   - RFC 5322 compliance validation
   - Embedded attachment handling
   - Filename sanitization
   
3. **BFF API Endpoint**
   - Implement `POST /api/emails/convert-to-document`
   - Integration with SpeFileStore
   - Document creation workflow
   - Error handling and logging
   
4. **Testing**
   - Unit tests for EML generation
   - Integration tests for API endpoint
   - Test various email formats (HTML, plain text, mixed)
   - Test attachment embedding

**Acceptance Criteria:**
- ✅ Email activity can be converted to .eml file
- ✅ .eml file stored in SPE successfully
- ✅ sprk_document created with email metadata
- ✅ Embedded attachments preserved in .eml
- ✅ RFC 5322 validation passes

### Phase 2: Background Service & Filtering (Week 3-4)

**Goal:** Automatic processing of incoming emails

**Tasks:**
1. **Background Service**
   - Implement `EmailToDocumentBackgroundService`
   - ADR-004 compliant job contract
   - Polling mechanism for new emails
   - Idempotency handling
   
2. **Filter Service**
   - Implement `IEmailFilterService`
   - Rule evaluation engine
   - Default exclusion rules (logos, signatures)
   - Redis caching for rules
   
3. **Configuration**
   - EmailProcessingOptions configuration
   - Default rule seeding
   - Admin UI for rule management
   
4. **Monitoring**
   - Application Insights custom events
   - Processing metrics dashboard
   - Failure alerting

**Acceptance Criteria:**
- ✅ Background service polls for new emails
- ✅ Exclusion rules filter unnecessary emails
- ✅ Automatic document creation works end-to-end
- ✅ Failed emails logged and retryable
- ✅ Monitoring dashboard operational

### Phase 3: Association & Attachments (Week 5-6)

**Goal:** Smart linking and attachment processing

**Tasks:**
1. **Association Service**
   - Implement `IEmailAssociationService`
   - Tracking token matching
   - Thread analysis
   - Sender/contact matching
   - Matter number extraction from subject/body
   
2. **Attachment Processor**
   - Implement `IEmailAttachmentProcessor`
   - Separate document creation for attachments
   - Parent-child linking
   - Attachment filtering (exclude signatures)
   
3. **Preview API**
   - Implement `GET /api/emails/association-preview`
   - Explain association logic to users
   - Alternative association suggestions
   
4. **Testing**
   - Test all association methods
   - Test attachment filtering rules
   - Test parent-child relationships

**Acceptance Criteria:**
- ✅ Emails automatically associated to Matters
- ✅ Tracking tokens recognized and matched
- ✅ Attachments stored as separate documents
- ✅ Signature images filtered out
- ✅ Association preview API functional

### Phase 4: UI Integration & AI Processing (Week 7-8)

**Goal:** User interface and AI pipeline integration

**Tasks:**
1. **Email Form Ribbon**
   - Add "Save to Document" button
   - JavaScript handler implementation
   - Confirmation dialogs
   - Success notifications
   
2. **AI Processing Integration**
   - Extend TextExtractorService for .eml
   - Add .eml to SupportedFileTypes
   - Enqueue AI processing for email documents
   - Test AI summarization of emails
   
3. **Admin Dashboard**
   - Custom page for monitoring
   - Bulk processing interface
   - Rule management UI
   - Statistics and reporting
   
4. **Documentation**
   - User guide for "Save to Document"
   - Admin guide for rule configuration
   - Troubleshooting guide

**Acceptance Criteria:**
- ✅ Users can manually convert emails via ribbon
- ✅ Email documents processed by AI pipeline
- ✅ AI summaries and entities extracted
- ✅ Admin dashboard functional
- ✅ Documentation complete

### Phase 5: Batch Processing & Production Readiness (Week 9-10)

**Goal:** Historical email processing and production deployment

**Tasks:**
1. **Batch Processing**
   - Implement `POST /api/emails/batch-process`
   - Job status tracking
   - Historical email query
   - Dry-run mode
   
2. **Performance Optimization**
   - Parallel processing for batch jobs
   - Redis caching for rules and associations
   - Connection pooling optimization
   - Rate limiting tuning
   
3. **Production Deployment**
   - Deploy background service to production
   - Configure monitoring and alerts
   - Create runbook for incidents
   - Smoke tests in production
   
4. **Training & Rollout**
   - Training materials for users
   - Admin training for rule configuration
   - Phased rollout plan

**Acceptance Criteria:**
- ✅ Batch processing handles thousands of emails
- ✅ Performance targets met (< 30s per email)
- ✅ Production deployment successful
- ✅ Monitoring and alerts operational
- ✅ Training completed

---

## 7. Non-Functional Requirements

### 7.1 Performance

| Metric | Target | Measurement |
|--------|--------|-------------|
| Email conversion | < 30 seconds | End-to-end (EML + upload + document) |
| Attachment processing | < 10 seconds per file | Extraction + upload |
| Background service poll | 60 seconds | Configurable interval |
| Batch processing | 100 emails/minute | With parallel processing |
| Association lookup | < 500ms | 95th percentile |

### 7.2 Scalability

| Dimension | Limit | Notes |
|-----------|-------|-------|
| Email size | 25MB max | Configurable, larger emails skipped |
| Attachments per email | 50 max | Typical enterprise limit |
| Batch processing | 10,000 emails | Per job |
| Concurrent processing | 10 emails | BackgroundService limit |
| Rule evaluation | < 100 active rules | Cached in Redis |

### 7.3 Reliability

| Requirement | Implementation |
|-------------|----------------|
| Idempotency | Alternate keys on `sprk_document` + processing status fields + idempotency keys (§2.2, §2.3.1, §3.3.4) |
| Retry logic | Service Bus retries + exponential backoff w/ jitter; honor `Retry-After` for Dataverse/SPE throttling (§3.3.5) |
| Poison queue | DLQ after max attempts; admin re-drive tooling + audit trail (§3.3.5) |
| Monitoring | Application Insights custom events |
| Audit trail | All operations logged with correlation ID |

### 7.4 Security

| Requirement | Implementation |
|-------------|----------------|
| Authentication | Entra ID JWT tokens (existing) |
| Authorization | Email record access validated via Dataverse security |
| Data isolation | Multi-tenant via Dataverse security roles |
| PII handling | Email content stored only in Dataverse and SPE |
| Encryption | At-rest (SPE) and in-transit (TLS) |

---

## 8. Testing Strategy

### 8.1 Unit Tests

**Services:**
- `EmailToEmlConverter` - RFC 5322 compliance, attachment embedding
- `EmailAssociationService` - All association methods, confidence scoring
- `EmailFilterService` - Rule evaluation, priority ordering
- `EmailAttachmentProcessor` - Filtering logic, document creation

**Target:** 80% code coverage

### 8.2 Integration Tests

**API Endpoints:**
- `/convert-to-document` - End-to-end with test email
- `/batch-process` - Job queueing and status tracking
- `/association-preview` - All association scenarios

**Background Service:**
- Poll and process workflow
- Idempotency checks
- Error handling and retry

**Target:** All happy path + error scenarios covered

### 8.3 E2E Tests

**User Scenarios:**
1. Receive email via Server-Side Sync
2. Automatic processing creates Document
3. Email associated to Matter correctly
4. Attachments stored separately
5. AI processing completes successfully
6. Manual conversion via ribbon button

**Target:** All critical user journeys automated

### 8.4 Performance Tests

**Load Tests:**
- 1,000 emails processed via background service
- 100 concurrent manual conversions
- Batch processing of 10,000 historical emails

**Stress Tests:**
- Large emails (25MB with attachments)
- High attachment count (50 files)
- Complex thread analysis (100+ related emails)

---

## 9. Risk Assessment

### 9.1 Technical Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| **RFC 5322 Compliance** | Medium | Use proven MimeKit library, extensive testing |
| **Association Accuracy** | High | Multiple fallback methods, manual override option |
| **Performance Degradation** | Medium | Parallel processing, caching, monitoring |
| **Email Content Variations** | High | Test diverse email clients, extensive error handling |

### 9.2 User Experience Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| **False Positives (Wrong Associations)** | High | Confidence scoring, preview API, manual correction |
| **Missed Emails** | Medium | Comprehensive filter rules, monitoring, manual fallback |
| **Slow Processing** | Low | Background processing, progress indicators |

### 9.3 Business Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| **Compliance Issues** | High | RFC 5322 compliance, audit trail, legal review |
| **Storage Costs** | Medium | Filter rules, attachment size limits, archival policies |
| **Data Integrity** | High | Idempotency, validation, extensive testing |

---

## 10. Open Questions

1. **Historical Email Cutoff**: How far back should automatic processing go?
   - **Recommendation**: Last 90 days for automatic, batch processing for historical

2. **Large Email Handling**: What to do with emails > 25MB?
   - **Recommendation**: Skip automatic, provide manual tool for admins

3. **Thread Complexity**: How deep should thread analysis go?
   - **Recommendation**: Parent + grandparent (2 levels) for performance

4. **Duplicate Detection**: What if email already manually created as Document?
   - **Recommendation**: Check for existing document by email ID before processing

5. **Retention Policy**: How long to keep email documents?
   - **Recommendation**: Follow same retention as Matters (7 years default)

---

## 11. Success Criteria

### 11.1 Technical Success

- [ ] Background service processes 95% of emails within 2 minutes
- [ ] Association accuracy > 80% (measured against manual review sample)
- [ ] Zero data loss or corruption
- [ ] API response times < 2s (P95)
- [ ] RFC 5322 validation passes for all generated .eml files

### 11.2 User Success

- [ ] Users can manually convert emails in < 10 seconds
- [ ] Association preview helps users understand linking
- [ ] 90% of emails correctly associated automatically
- [ ] Attachment filtering excludes 95% of unnecessary files
- [ ] User satisfaction score > 4/5

### 11.3 Business Success

- [ ] 100% of incoming legal emails archived as Documents
- [ ] Compliance audit passes for email retention
- [ ] AI processing provides value (summaries used by 80% of users)
- [ ] Storage costs within budget (< $100/month per 10,000 emails)
- [ ] No critical security incidents

---

## 12. Appendices

### Appendix A: RFC 5322 Email Format

```
From: john@client.com
To: lawyer@lawfirm.com
Subject: Re: Matter M-12345 - Document Review
Date: Wed, 11 Dec 2025 10:30:00 -0800
Message-ID: <abc123@client.com>
In-Reply-To: <xyz789@lawfirm.com>
References: <xyz789@lawfirm.com>
MIME-Version: 1.0
Content-Type: multipart/mixed; boundary="----BOUNDARY"

------BOUNDARY
Content-Type: text/plain; charset="utf-8"

Email body content here...

------BOUNDARY
Content-Type: application/pdf; name="contract.pdf"
Content-Transfer-Encoding: base64
Content-Disposition: attachment; filename="contract.pdf"

[Base64 encoded file content]
------BOUNDARY--
```

### Appendix B: Association Algorithm Pseudocode

```
function DetermineAssociation(emailId):
    email = LoadEmail(emailId)
    
    // Priority 1: Tracking Token
    if email.TrackingToken:
        matter = FindMatterByTrackingToken(email.TrackingToken)
        if matter:
            return {matter, confidence: 0.95, method: "TrackingToken"}
    
    // Priority 2: Thread Parent
    if email.InReplyTo:
        parentEmail = FindEmail(email.InReplyTo)
        if parentEmail.Document and parentEmail.Document.Matter:
            return {parentEmail.Document.Matter, confidence: 0.90, method: "ThreadParent"}
    
    // Priority 3: Sender Contact
    sender = ParseEmailAddress(email.From)
    contact = FindContactByEmail(sender)
    if contact and contact.Account:
        matters = GetActiveMattersByAccount(contact.Account)
        if matters.Count == 1:
            return {matters[0], confidence: 0.80, method: "SenderContact"}
    
    // Priority 4: Subject Matter Number
    matterNumber = ExtractMatterNumber(email.Subject)
    if matterNumber:
        matter = FindMatterByNumber(matterNumber)
        if matter:
            return {matter, confidence: 0.75, method: "SubjectMatterNumber"}
    
    // Priority 5: Body Analysis
    bodyMatters = AnalyzeBodyForMatterReferences(email.Body)
    if bodyMatters.Count == 1:
        return {bodyMatters[0], confidence: 0.60, method: "BodyMatterReference"}
    
    // Priority 6: Recent Communication
    if contact:
        recentMatters = GetMattersWithRecentCommunication(contact, days: 30)
        if recentMatters.Count == 1:
            return {recentMatters[0], confidence: 0.50, method: "RecentCommunication"}
    
    return {null, confidence: 0.0, method: "None"}
```

### Appendix C: Related ADRs

| ADR | Title | Relevance |
|-----|-------|-----------|
| ADR-001 | Minimal APIs + BackgroundService | Background service pattern |
| ADR-002 | No Heavy Plugins | No plugins or Power Automate |
| ADR-004 | Async Job Contract | Background processing pattern |
| ADR-007 | SpeFileStore Facade | File operations |
| ADR-013 | AI Architecture | AI processing integration |

### Appendix D: Glossary

| Term | Definition |
|------|------------|
| **Server-Side Sync** | Power Platform feature that synchronizes emails from Exchange to Dataverse |
| **RFC 5322** | Internet Message Format standard for email |
| **ActivityMimeAttachment** | Dataverse entity storing email attachments |
| **Tracking Token** | Unique identifier embedded in outgoing emails for thread tracking |
| **EML File** | Email file format following RFC 5322 standard |
| **Party List** | Dataverse field type for email recipients (To, From, CC, BCC) |

---

**Document Status:** Draft for Review  
**Next Steps:**  
1. Review with engineering team  
2. Validate association algorithm accuracy  
3. Security review for email content handling  
4. Legal review for compliance requirements  

**Target Review Date:** December 13, 2025
