# Email-to-Document Automation R2 - Design Specification

> **Version**: 1.1
> **Date**: January 13, 2026
> **Project**: Email-to-Document Automation - Round 2 (R2)
> **Status**: Ready for Implementation
> **Prerequisite**: Email-to-Document R1 (Complete - PR #104)
>
> **v1.1 Updates**:
> - Added attachment filtering (signature logos, tracking pixels, calendar files)
> - Added Email entity fields for AI output (mirrors Document Profile fields)
> - Clarified Extract-Combine-Analyze approach for Email Analysis Playbook

---

## Executive Summary

This document specifies the Round 2 enhancements for the Email-to-Document Automation feature. R1 established the core pipeline (email → .eml → SPE upload → Document record). R2 addresses a critical access gap and extends the feature with attachment processing and AI analysis capabilities.

### R2 Scope

| Phase | Priority | Description |
|-------|----------|-------------|
| **Phase 1** | 🔴 MVP | API-proxied download endpoint (fixes user access to app-uploaded files) |
| **Phase 2** | High | Attachment extraction and child Document creation |
| **Phase 3** | Medium | AppOnlyAnalysisService for background AI analysis |
| **Phase 4** | Medium | Email Analysis Playbook (combines email + attachments) |
| **Phase 5** | Medium | UI/PCF enhancements (ribbon toolbar for processing existing/sent emails) |

### Out of Scope

- ❌ New monitoring dashboards
- ❌ Explicit R1 rework (unless refinement needed for R2)

### Dependencies

- **Playbook Module**: Email Analysis Playbook creation requires coordination with Playbook project/module

---

## Problem Statement

### Critical Issue: Users Cannot Access App-Uploaded Files

**Discovery**: End-to-end testing (January 13, 2026) revealed that while email processing works correctly, users cannot open the resulting .eml files directly.

**Root Cause**:
- Files uploaded via **PCF (OBO auth)** → User has SPE container permissions automatically
- Files uploaded via **app-only auth** (email processing) → Only the app has SPE permissions, not users

**Symptoms**:
- Graph Preview API works (returns pre-authenticated ephemeral URLs)
- Direct WebUrl access fails: "user@domain.com does not have permission to access this resource"

**Impact**: Users can preview .eml files in the embedded viewer but cannot download or open them in external applications.

### Secondary Gaps

| Gap | Impact |
|-----|--------|
| **No attachment extraction** | Email attachments are embedded in .eml but not individually searchable or analyzable |
| **No AI analysis for app-uploaded files** | `AnalysisOrchestrationService` requires OBO auth, unavailable in job handlers |
| **No combined email analysis** | Email + attachment content not analyzed as a unit |

---

## Phase 1: API-Proxied Download Endpoint (MVP)

### Overview

Create a BFF endpoint that proxies file downloads, using app-only auth to fetch from SPE while validating user authorization via Dataverse permissions.

### Why This Approach (Option B)

| Option | Description | Verdict |
|--------|-------------|---------|
| **A** | Field security on `sprk_filepath` | ❌ Too simple, no audit trail |
| **B** | API-proxied download | ✅ Audit trail, single source of truth |
| **C** | Grant container permissions | ❌ Duplicates permission management |

**Option B Benefits**:
1. **Audit trail** - Log all downloads for compliance (legal emails)
2. **Single source of truth** - Dataverse controls all authorization
3. **Future-proof** - Works for any non-user uploads (bulk imports, integrations)
4. **Granular control** - Can differentiate read vs download permissions

### Endpoint Specification

```
GET /api/v1/documents/{documentId}/download
```

**Request**:
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `documentId` | GUID | Yes | Dataverse `sprk_document` ID |

**Response**:
- **200 OK**: File stream with appropriate `Content-Type` and `Content-Disposition` headers
- **403 Forbidden**: User lacks Dataverse read permission on document
- **404 Not Found**: Document doesn't exist or has no file

**Headers**:
```http
Content-Type: {sprk_mimetype}
Content-Disposition: attachment; filename="{sprk_filename}"
Content-Length: {sprk_filesize}
X-Download-AuditId: {correlation-id}
```

### Implementation Design

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  GET /api/documents/{documentId}/download                                    │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  1. DocumentDownloadAuthorizationFilter                                      │
│     ├─ Extract documentId from route                                         │
│     ├─ Query Dataverse: user has read access to sprk_document?               │
│     └─ Return 403 if unauthorized                                            │
│                                                                              │
│  2. DocumentDownloadEndpoint                                                 │
│     ├─ Get document metadata from Dataverse (sprk_graphitemid, sprk_graphdriveid) │
│     ├─ Log download audit event                                              │
│     ├─ Call SpeFileStore.DownloadFileAsync (app-only)                        │
│     └─ Stream file to response                                               │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Authorization Model

```csharp
// DocumentDownloadAuthorizationFilter.cs
public class DocumentDownloadAuthorizationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var documentId = context.HttpContext.GetRouteValue("documentId") as string;

        // Use existing IDataverseService to check user's read access
        // This respects all Dataverse security: teams, BUs, sharing, field-level
        var canRead = await _dataverseService.UserCanReadAsync(
            context.HttpContext,
            "sprk_document",
            Guid.Parse(documentId));

        if (!canRead)
            return Results.Forbid();

        return await next(context);
    }
}
```

### Audit Logging

```csharp
// Telemetry event for compliance
_telemetry.LogDocumentDownload(new DocumentDownloadEvent
{
    DocumentId = documentId,
    UserId = httpContext.User.GetObjectId(),
    FileName = document.FileName,
    FileSize = document.FileSize,
    MimeType = document.MimeType,
    DownloadTimestamp = DateTimeOffset.UtcNow,
    CorrelationId = Activity.Current?.Id
});
```

### Files to Create/Modify

| File | Action | Description |
|------|--------|-------------|
| `Api/DocumentEndpoints.cs` | Modify | Add download endpoint |
| `Filters/DocumentDownloadAuthorizationFilter.cs` | Create | Authorization filter |
| `Telemetry/DocumentTelemetry.cs` | Modify | Add download audit events |

### Acceptance Criteria

- [ ] Users can download .eml files uploaded by email processing
- [ ] Download respects Dataverse read permissions
- [ ] All downloads logged with correlation ID
- [ ] 403 returned for unauthorized access attempts
- [ ] Streaming response (no buffering full file in memory)

---

## Phase 2: Attachment Processing

### Overview

Extend `EmailToDocumentJobHandler` to extract email attachments, upload each as a separate SPE file, and create child Document records linked to the parent .eml Document.

### Data Model

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  sprk_document (Parent - .eml)                                               │
│  ├─ sprk_documentid: {guid}                                                  │
│  ├─ sprk_isemailarchive: true                                                │
│  ├─ sprk_documenttype: 100000006 (Email)                                     │
│  ├─ sprk_email: {email activity lookup}                                      │
│  └─ sprk_filename: "Subject.eml"                                             │
│                                                                              │
│      ▲ sprk_ParentDocumentLookup                                             │
│      │                                                                       │
│  ┌───┴─────────────────────────────────────────────────────────────────────┐ │
│  │  sprk_document (Child - Attachment 1)                                   │ │
│  │  ├─ sprk_ParentDocumentLookup: {parent guid}                            │ │
│  │  ├─ sprk_isemailarchive: false                                          │ │
│  │  ├─ sprk_documenttype: 100000001 (General) or appropriate type          │ │
│  │  └─ sprk_filename: "Contract.pdf"                                       │ │
│  └─────────────────────────────────────────────────────────────────────────┘ │
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────────┐ │
│  │  sprk_document (Child - Attachment 2)                                   │ │
│  │  ├─ sprk_ParentDocumentLookup: {parent guid}                            │ │
│  │  └─ sprk_filename: "Invoice.xlsx"                                       │ │
│  └─────────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Processing Flow

```
EmailToDocumentJobHandler (Enhanced)
        │
        ▼
┌───────────────────────────────────────────────────────────────────────────┐
│  1. Convert email → .eml file (existing)                                   │
│  2. Upload .eml to SPE → Create parent Document record (existing)          │
│  3. NEW: For each attachment:                                              │
│     a. Extract attachment bytes from MimeMessage                           │
│     b. Upload to SPE via SpeFileStore                                      │
│     c. Create child Document record                                        │
│        - Set sprk_ParentDocumentLookup = parent Document ID                │
│        - Set sprk_email = same email activity lookup                       │
│     d. Queue Document Profile analysis job (Phase 3)                       │
│  4. Queue Document Profile analysis for parent .eml (Phase 3)              │
│  5. Queue Email Analysis job (Phase 4)                                     │
└───────────────────────────────────────────────────────────────────────────┘
```

### Attachment Filtering

**Not all email attachments should create Document records.** The following must be filtered out to avoid noise:

#### Exclusion Criteria

| Category | Detection Method | Examples |
|----------|------------------|----------|
| **Signature logos** | Inline images with `Content-ID` header, typically referenced in HTML body | Company logos, social icons |
| **Tracking pixels** | Images ≤ 10x10 pixels OR file size < 1KB | Email open trackers, analytics pixels |
| **Inline images** | `Content-Disposition: inline` (vs `attachment`) | Embedded screenshots in body |
| **Calendar files** | MIME type `text/calendar` or `.ics` extension | Meeting invites (already in Dataverse) |
| **Signature cards** | `.vcf` files < 5KB | vCard contact files in signatures |

#### Filter Implementation

```csharp
public class AttachmentFilter
{
    public bool ShouldProcess(MimeEntity attachment)
    {
        // Skip inline content (signature images, embedded graphics)
        if (attachment.ContentDisposition?.Disposition == "inline")
            return false;

        // Skip if referenced by Content-ID in HTML body (signature logo)
        if (!string.IsNullOrEmpty(attachment.ContentId))
            return false;

        // Skip tracking pixels (tiny images)
        if (IsTrackingPixel(attachment))
            return false;

        // Skip calendar invites (already synced as activities)
        if (attachment.ContentType.MimeType == "text/calendar")
            return false;

        // Skip tiny vCard files (signature contact cards)
        if (IsSignatureVCard(attachment))
            return false;

        return true;
    }

    private bool IsTrackingPixel(MimeEntity attachment)
    {
        if (!attachment.ContentType.MimeType.StartsWith("image/"))
            return false;

        // Check file size (tracking pixels are typically < 1KB)
        if (attachment is MimePart part && part.Content.Stream.Length < 1024)
            return true;

        return false;
    }

    private bool IsSignatureVCard(MimeEntity attachment)
    {
        return attachment.ContentType.MimeType == "text/vcard"
            && attachment is MimePart part
            && part.Content.Stream.Length < 5120; // < 5KB
    }
}
```

#### Configurable Rules

Filter rules should be configurable via `EmailProcessingOptions`:

```json
{
  "EmailProcessing": {
    "AttachmentFiltering": {
      "SkipInlineContent": true,
      "SkipContentIdReferences": true,
      "SkipCalendarFiles": true,
      "MinFileSizeBytes": 1024,
      "MaxTrackingPixelBytes": 1024,
      "SkipVCardUnderBytes": 5120,
      "MaxAttachmentSizeBytes": 262144000
    }
  }
}
```

**Note**: Max attachment size follows SPE upload limits (250MB per Graph API).

### Interface Extensions

```csharp
// IEmailToEmlConverter.cs - Extended
public interface IEmailToEmlConverter
{
    // Existing
    EmlConversionResult ConvertToEml(EmailActivityDto email);

    // New - returns only processable attachments (filtered)
    IReadOnlyList<EmailAttachment> ExtractAttachments(byte[] emlBytes);
}

public record EmailAttachment(
    string FileName,
    string ContentType,
    byte[] Content,
    long Size,
    bool WasFiltered = false,  // For metrics/logging
    string? FilterReason = null
);
```

### Files to Create/Modify

| File | Action | Description |
|------|--------|-------------|
| `Services/Email/IEmailToEmlConverter.cs` | Modify | Add attachment extraction |
| `Services/Email/EmailToEmlConverter.cs` | Modify | Implement extraction using MimeKit |
| `Services/Jobs/Handlers/EmailToDocumentJobHandler.cs` | Modify | Process attachments |

### Acceptance Criteria

- [ ] Attachments extracted from .eml and uploaded to SPE individually
- [ ] Child Document records created with parent relationship
- [ ] All Documents linked to source email activity
- [ ] Attachment processing failures don't block parent .eml creation
- [ ] Metrics track attachment count and sizes

---

## Phase 3: AppOnlyAnalysisService

### Overview

Create a parallel analysis service that operates with app-only authentication, enabling AI analysis of documents uploaded by background processes (email processing, bulk imports).

### Why a Separate Service?

| Service | Auth Mode | Use Case |
|---------|-----------|----------|
| `AnalysisOrchestrationService` | OBO | PCF-triggered analysis (user context required) |
| `AppOnlyAnalysisService` | App-only | Background job analysis (no user context) |

**Key Constraint**: `AnalysisOrchestrationService` requires `HttpContext` for OBO token acquisition. Job handlers have no `HttpContext`.

### Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  User-Initiated (OBO)               App-Only (Background)                    │
│  ──────────────────────             ─────────────────────                    │
│  AnalysisOrchestrationService       AppOnlyAnalysisService                   │
│  - PCF triggers                     - Email processing                       │
│  - User context (OBO)               - Bulk uploads                           │
│  - HttpContext required             - No user context                        │
│                                                                              │
│              ↘                    ↙                                         │
│                  Shared Components                                           │
│                  - OpenAiClient                                              │
│                  - TextExtractorService                                      │
│                  - SpeFileStore (app-only mode)                              │
│                  - IDataverseService                                         │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Interface Design

```csharp
public interface IAppOnlyAnalysisService
{
    /// <summary>
    /// Analyze a document using app-only authentication.
    /// For documents uploaded by background processes.
    /// </summary>
    Task<DocumentProfileResult> AnalyzeDocumentAsync(
        Guid documentId,
        Guid? playbookId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyze an email with all its attachments.
    /// Output goes to the Email activity record.
    /// </summary>
    Task<EmailAnalysisResult> AnalyzeEmailAsync(
        Guid emailActivityId,
        Guid playbookId,
        CancellationToken cancellationToken = default);
}
```

### Job Handler Integration

```csharp
// New job type for app-only analysis
public class AppOnlyAnalysisJobHandler : IJobHandler
{
    public string JobType => "AppOnlyDocumentAnalysis";

    public async Task HandleAsync(JobContract job, CancellationToken ct)
    {
        var payload = job.GetPayload<AppOnlyAnalysisPayload>();

        await _appOnlyAnalysisService.AnalyzeDocumentAsync(
            payload.DocumentId,
            payload.PlaybookId,
            ct);
    }
}
```

### Email Entity Fields for AI Output

The `email` activity entity requires new fields to store AI analysis results. These fields **mirror the Document Profile fields** on `sprk_document` for consistency:

#### Field Mapping (Document → Email)

| Document Profile Field | Email Entity Field | Type | Description |
|------------------------|-------------------|------|-------------|
| `sprk_aisummary` | `sprk_emailaisummary` | Multiline Text | AI-generated summary |
| `sprk_aitldr` | `sprk_emailaitldr` | Text (500) | One-line TL;DR |
| `sprk_aidocumenttype` | `sprk_emailaidocumenttype` | Text (100) | Classified document type |
| `sprk_aikeywords` | `sprk_emailaikeywords` | Multiline Text | Extracted keywords (comma-separated) |
| `sprk_aientities` | `sprk_emailaientities` | Multiline Text | Extracted entities (JSON) |
| `sprk_aiconfidence` | `sprk_emailaiconfidence` | Decimal | Analysis confidence score (0.0-1.0) |
| `sprk_aianalyzedon` | `sprk_emailaianalyzedon` | DateTime | When analysis was performed |
| `sprk_aiplaybookid` | `sprk_emailaiplaybookid` | Lookup | Which playbook was used |

#### Entity Definition (Dataverse)

```xml
<!-- email entity customizations -->
<attribute>
  <LogicalName>sprk_emailaisummary</LogicalName>
  <DisplayName>AI Summary</DisplayName>
  <AttributeType>Memo</AttributeType>
  <MaxLength>100000</MaxLength>
</attribute>
<attribute>
  <LogicalName>sprk_emailaitldr</LogicalName>
  <DisplayName>AI TL;DR</DisplayName>
  <AttributeType>String</AttributeType>
  <MaxLength>500</MaxLength>
</attribute>
<attribute>
  <LogicalName>sprk_emailaidocumenttype</LogicalName>
  <DisplayName>AI Document Type</DisplayName>
  <AttributeType>String</AttributeType>
  <MaxLength>100</MaxLength>
</attribute>
<attribute>
  <LogicalName>sprk_emailaikeywords</LogicalName>
  <DisplayName>AI Keywords</DisplayName>
  <AttributeType>Memo</AttributeType>
  <MaxLength>10000</MaxLength>
</attribute>
<attribute>
  <LogicalName>sprk_emailaientities</LogicalName>
  <DisplayName>AI Entities</DisplayName>
  <AttributeType>Memo</AttributeType>
  <MaxLength>100000</MaxLength>
  <Description>JSON array of extracted entities</Description>
</attribute>
<attribute>
  <LogicalName>sprk_emailaiconfidence</LogicalName>
  <DisplayName>AI Confidence</DisplayName>
  <AttributeType>Decimal</AttributeType>
  <MinValue>0</MinValue>
  <MaxValue>1</MaxValue>
  <Precision>2</Precision>
</attribute>
<attribute>
  <LogicalName>sprk_emailaianalyzedon</LogicalName>
  <DisplayName>AI Analyzed On</DisplayName>
  <AttributeType>DateTime</AttributeType>
</attribute>
```

#### Why Mirror Document Fields?

1. **Consistency** - Users see same field names across Documents and Emails
2. **Reporting** - Unified queries across both entity types
3. **Playbook reuse** - Same output schema works for both
4. **Future extensibility** - Easy to add fields to both simultaneously

**Note**: Fields are currently populated by hardcoded extraction logic. Future enhancement: Playbook-defined output schema would make these configurable.

### Files to Create

| File | Action | Description |
|------|--------|-------------|
| `Services/Analysis/IAppOnlyAnalysisService.cs` | Create | Interface definition |
| `Services/Analysis/AppOnlyAnalysisService.cs` | Create | Implementation |
| `Services/Jobs/Handlers/AppOnlyAnalysisJobHandler.cs` | Create | Job handler |
| `Models/Analysis/AppOnlyAnalysisPayload.cs` | Create | Job payload model |
| Dataverse solution | Modify | Add `sprk_email*` fields to email entity |

### Acceptance Criteria

- [ ] App-only analysis works for documents uploaded by email processing
- [ ] Document Profile created and linked to Document record
- [ ] Email entity fields populated with AI analysis results
- [ ] Field values match Document Profile output format
- [ ] Shared components (OpenAI, TextExtractor) reused from existing services
- [ ] Metrics track app-only analysis separate from OBO analysis

---

## Phase 4: Email Analysis Playbook

### Overview

Create a specialized AI playbook that analyzes an email holistically - combining email metadata, body text, and all attachment contents into a unified analysis stored on the Email activity record.

### Why Separate from Document Profile?

| Analysis Type | Entity | Content Analyzed | Output |
|---------------|--------|------------------|--------|
| Document Profile | `sprk_document` | Single file (.eml or attachment) | Document-level metadata |
| Email Analysis | `email` (activity) | Email metadata + body + ALL attachments | Email-level insights |

**Azure Document Intelligence Limitation**: Does NOT process .eml files natively. For .eml, TextExtractorService sees MIME structure but cannot meaningfully decode base64-encoded attachments.

### Processing Approach: Extract-Combine-Analyze

**Key Decision**: Content is extracted and combined **BEFORE** sending to AI for analysis (not analyzed individually then combined).

#### Why Extract-Combine-Analyze?

| Approach | Pros | Cons | Verdict |
|----------|------|------|---------|
| **Extract → Combine → Analyze (single call)** | Holistic understanding, relationships preserved, one API call | Large context window needed | ✅ Recommended |
| **Analyze each → Combine results** | Smaller individual calls | Loses cross-document relationships, more API calls, higher cost | ❌ Not recommended |

**Rationale**:
1. **Holistic context** - AI can see relationships between email body and attachments (e.g., "see attached contract" → contract content)
2. **Single API call** - More cost-effective than multiple calls
3. **Better summarization** - AI understands the full picture, not fragments
4. **Action item extraction** - Can correlate "please review Section 3" with actual Section 3 content from attachment

### Processing Flow

```
Email Analysis Playbook Execution
        │
        ▼
┌───────────────────────────────────────────────────────────────────────────┐
│  PHASE A: Content Extraction (before AI)                                   │
│  ────────────────────────────────────────                                  │
│  1. Load Email Activity record from Dataverse                              │
│  2. Load parent Document (.eml) and extract body text                      │
│  3. Load all child Documents (attachments)                                 │
│  4. For each attachment: extract text via TextExtractorService             │
│     - PDF, DOCX, XLSX → Document Intelligence                              │
│     - Images → OCR                                                         │
│     - Text files → Direct read                                             │
│                                                                            │
│  PHASE B: Context Assembly (before AI)                                     │
│  ─────────────────────────────────────                                     │
│  5. Combine into unified context object:                                   │
│     - Email metadata (from, to, cc, subject, date)                         │
│     - Email body text (plain text, not HTML)                               │
│     - Array of attachment objects with extracted text                      │
│                                                                            │
│  PHASE C: AI Analysis (single call)                                        │
│  ──────────────────────────────────                                        │
│  6. Send combined context to OpenAI with Email Analysis Playbook prompt    │
│  7. AI processes entire email+attachments as single unit                   │
│  8. AI returns structured output (TL;DR, Summary, Keywords, Entities)      │
│                                                                            │
│  PHASE D: Result Storage                                                   │
│  ───────────────────────                                                   │
│  9. Map AI output to Email entity fields (sprk_emailai*)                   │
│  10. Update Email activity record in Dataverse                             │
└───────────────────────────────────────────────────────────────────────────┘
```

### Context Size Management

Large emails with many attachments may exceed token limits. Handle gracefully:

```csharp
public class EmailContextBuilder
{
    private const int MaxContextTokens = 100_000; // GPT-4 Turbo limit
    private const int ReservedForPrompt = 5_000;
    private const int ReservedForResponse = 10_000;
    private const int AvailableForContent = MaxContextTokens - ReservedForPrompt - ReservedForResponse;

    public EmailAnalysisContext Build(EmailActivityDto email, List<AttachmentContent> attachments)
    {
        var context = new EmailAnalysisContext
        {
            Email = BuildEmailSection(email),
            Attachments = new List<AttachmentContext>()
        };

        int usedTokens = EstimateTokens(context.Email);

        // Add attachments in order of importance (by size, assuming larger = more important)
        foreach (var attachment in attachments.OrderByDescending(a => a.ExtractedText.Length))
        {
            int attachmentTokens = EstimateTokens(attachment.ExtractedText);

            if (usedTokens + attachmentTokens <= AvailableForContent)
            {
                context.Attachments.Add(new AttachmentContext
                {
                    FileName = attachment.FileName,
                    MimeType = attachment.MimeType,
                    ExtractedText = attachment.ExtractedText
                });
                usedTokens += attachmentTokens;
            }
            else
            {
                // Truncate or skip - log for metrics
                _logger.LogWarning("Attachment {FileName} truncated/skipped due to context limits",
                    attachment.FileName);
            }
        }

        return context;
    }
}
```

### Playbook Design

**Playbook Name**: "Email Analysis"

**Input Context** (combined before AI call):
```json
{
  "email": {
    "subject": "RE: Contract Review - Smith Matter",
    "from": "attorney@lawfirm.com",
    "to": ["client@company.com"],
    "cc": ["paralegal@lawfirm.com"],
    "sentDate": "2026-01-13T10:30:00Z",
    "bodyText": "Please find the revised contract attached. Pay special attention to Section 3..."
  },
  "attachments": [
    {
      "fileName": "Contract_v2.pdf",
      "mimeType": "application/pdf",
      "extractedText": "PROFESSIONAL SERVICES AGREEMENT\n\nSection 3: Payment Terms\n..."
    },
    {
      "fileName": "Redline.docx",
      "mimeType": "application/vnd.openxmlformats...",
      "extractedText": "Changes highlighted in red: Section 3.1 modified to..."
    }
  ]
}
```

**Output Schema** (matches Email entity fields):
```json
{
  "tldr": "Contract revision with updated payment terms in Section 3",
  "documentType": "Legal Correspondence",
  "summary": "Attorney sends revised contract for client review, highlighting changes to payment terms in Section 3. Two attachments: the updated contract and a redline showing changes.",
  "keywords": ["contract", "payment terms", "Section 3", "professional services", "revision"],
  "entities": [
    { "type": "Person", "value": "attorney@lawfirm.com", "role": "Sender" },
    { "type": "Organization", "value": "lawfirm.com", "role": "Representing" },
    { "type": "Document", "value": "Contract_v2.pdf", "role": "Primary Attachment" },
    { "type": "Reference", "value": "Section 3", "role": "Key Section" }
  ],
  "confidence": 0.92
}

### Files to Create

| File | Action | Description |
|------|--------|-------------|
| `Services/Analysis/EmailAnalysisService.cs` | Create | Email analysis orchestration |
| `Models/Analysis/EmailAnalysisContext.cs` | Create | Combined context model |
| `Models/Analysis/EmailAnalysisResult.cs` | Create | Result model |

### Acceptance Criteria

- [ ] Email Analysis Playbook created in Dataverse
- [ ] Analysis combines email metadata + body + all attachments
- [ ] Results stored on Email activity record
- [ ] Analysis triggered after attachment processing completes

---

## Technical Constraints

### ADR Compliance

| ADR | Requirement | R2 Compliance |
|-----|-------------|---------------|
| ADR-001 | Minimal API + BackgroundService | New endpoints via Minimal API; analysis via existing job pattern |
| ADR-004 | Standard job contract | New job types follow existing schema |
| ADR-007 | SpeFileStore facade | Download endpoint uses `SpeFileStore.DownloadFileAsync` |
| ADR-008 | Endpoint filters | `DocumentDownloadAuthorizationFilter` for download endpoint |
| ADR-010 | DI minimalism (≤15) | +2 services: `AppOnlyAnalysisService`, `EmailAnalysisService` |

### Existing Patterns to Follow

| Pattern | Reference | Usage in R2 |
|---------|-----------|-------------|
| Job Handler | `EmailToDocumentJobHandler` | `AppOnlyAnalysisJobHandler` follows same pattern |
| Endpoint Filter | `DocumentAuthorizationFilter` | `DocumentDownloadAuthorizationFilter` extends pattern |
| Service Bus Job | `JobContract` schema | New job types use same schema |
| Telemetry | `EmailTelemetry` | New metrics follow same naming conventions |

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Large attachments exceed memory limits | Service instability | Stream attachments, don't buffer full content |
| AI analysis costs for high email volume | Budget overrun | Rate limiting, queue prioritization |
| Circular dependencies OBO/app-only | Architecture complexity | Clear service boundaries, shared components only |
| Attachment extraction failures | Data loss | Graceful degradation - parent .eml still created |

---

## Success Metrics

| Metric | Target | Measurement |
|--------|--------|-------------|
| Download endpoint latency (P95) | < 2s | Application Insights |
| Attachment extraction success rate | > 99% | `email.attachment.extracted` counter |
| App-only analysis success rate | > 95% | `analysis.apponly.succeeded` counter |
| Email analysis completion time | < 5 min | Job duration histogram |

---

## Implementation Order

```
Phase 1: API-Proxied Download (MVP)     ← BLOCKS USER ACCESS
    │
    ▼
Phase 2: Attachment Processing          ← BLOCKS AI ANALYSIS
    │
    ▼
Phase 3: AppOnlyAnalysisService         ← ENABLES BACKGROUND AI
    │
    ▼
Phase 4: Email Analysis Playbook        ← COMPLETES FEATURE
```

**Recommendation**: Phase 1 is blocking and should be implemented immediately. Phases 2-4 can be parallelized after Phase 1 ships.

---

## Related Documentation

| Document | Purpose |
|----------|---------|
| [EMAIL-TO-DOCUMENT-ARCHITECTURE.md](../../docs/guides/EMAIL-TO-DOCUMENT-ARCHITECTURE.md) | R1 architecture reference |
| [SPAARKE-AI-ARCHITECTURE.md](../../docs/guides/SPAARKE-AI-ARCHITECTURE.md) | AI Tool Framework patterns |
| [sdap-auth-patterns.md](../../docs/architecture/sdap-auth-patterns.md) | OBO vs app-only patterns |
| [lessons-learned.md](./lessons-learned.md) | R1 retrospective |

---

*Created: January 13, 2026*
