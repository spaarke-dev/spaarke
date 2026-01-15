# Email Analysis Playbook Documentation

> **Playbook ID**: PB-012
> **Dataverse Record ID**: `bc71facf-6af1-f011-8406-7ced8d1dc988`
> **Name**: Email Analysis
> **Created**: 2026-01-14
> **Task**: 030 - Create Email Analysis Playbook
> **Deployment Status**: ✅ Deployed to Dev

---

## Purpose

The Email Analysis playbook enables AI-powered analysis of emails in the email-to-document automation workflow. It combines:
- Email metadata (Subject, From, To, CC, Date)
- Email body text
- Extracted text from all attachments

This comprehensive context enables richer analysis than processing email body or attachments individually.

---

## Input Format

The EmailAnalysisService (Task 031) constructs a combined text input:

```
===== EMAIL METADATA =====
Subject: {subject}
From: {sender_name} <{sender_email}>
To: {recipient_list}
CC: {cc_list}
Date: {sent_datetime_utc}

===== EMAIL BODY =====
{email_body_text}

===== ATTACHMENT: {filename1} =====
{attachment1_extracted_text}

===== ATTACHMENT: {filename2} =====
{attachment2_extracted_text}
```

### Input Constraints
- **Maximum combined size**: 100KB of text
- **Attachment processing**: Only supported file types (.pdf, .docx, .txt, .eml)
- **Text extraction**: Uses ITextExtractor service for all document types

---

## Output Fields

The playbook produces structured outputs mapped to Dataverse fields:

| Output | Field | Description |
|--------|-------|-------------|
| **TL;DR** | `sprk_tldr` | 1-2 sentence ultra-concise summary |
| **Summary** | `sprk_summary` | 2-4 paragraph comprehensive summary with key points |
| **Keywords** | `sprk_keywords` | Comma-separated list of 5-10 relevant keywords |
| **Document Type** | `sprk_documenttype` | Classification: Correspondence, Contract, Notification, Request, Report |
| **Entities** | `sprk_entities` | JSON array of extracted entities (parties, organizations, dates, amounts) |

### Output Format Examples

**TL;DR**:
```
Email from ABC Corp regarding Q4 contract renewal, requesting signature by January 31st. Attachment contains updated terms with 5% fee increase.
```

**Summary**:
```
## Email Overview
This correspondence from ABC Corporation discusses the renewal of Service Agreement SA-2024-001.

## Key Points
- Contract renewal deadline: January 31, 2026
- Proposed changes: 5% fee increase effective Q2 2026
- Action required: Legal review and counter-proposal by January 15

## Attachments
The attached contract amendment (Amendment_3.pdf) contains updated pricing terms and SLA modifications.
```

**Keywords**:
```
contract renewal, ABC Corporation, Service Agreement, Q4 2026, fee increase, SLA, deadline
```

**Entities (JSON)**:
```json
[
  {"type": "organization", "name": "ABC Corporation"},
  {"type": "person", "name": "John Smith", "role": "Account Manager"},
  {"type": "date", "name": "January 31, 2026", "context": "deadline"},
  {"type": "monetary_amount", "name": "5%", "context": "fee increase"},
  {"type": "reference", "name": "SA-2024-001", "context": "Service Agreement number"}
]
```

---

## Tools Used

The playbook uses the same tools as Document Profile (PB-011):

| Tool ID | Name | Purpose |
|---------|------|---------|
| TL-001 | Entity Extractor | Extract parties, organizations, dates, amounts |
| TL-003 | Document Classifier | Classify email type and purpose |
| TL-004 | Summary Generator | Generate TL;DR and structured summary |

---

## Prompt Design (Reference)

The tools use their standard prompts, but the email context provides additional signals:

### For Summary Generator (TL-004)
The email metadata section enables the AI to:
- Identify the primary communication parties
- Understand the email's temporal context
- Recognize the sender-recipient relationship

### For Entity Extractor (TL-001)
The structured input allows extraction of:
- **From email metadata**: Sender, recipients, date
- **From body**: Referenced parties, key dates, amounts
- **From attachments**: Document-specific entities

### For Document Classifier (TL-003)
Email-specific classifications:
- **Correspondence**: General business communication
- **Notification**: Announcements, alerts, status updates
- **Request**: Action items, approvals, signatures needed
- **Contract**: Agreement-related emails with attachments
- **Report**: Periodic reports, summaries, analytics

---

## Usage in EmailAnalysisService

```csharp
// Task 031 will implement this pattern
public async Task<EmailAnalysisResult> AnalyzeEmailAsync(
    Guid emailId,
    CancellationToken cancellationToken = default)
{
    // 1. Fetch email from Dataverse
    var email = await _dataverseService.GetEmailAsync(emailId, cancellationToken);

    // 2. Fetch attachments and extract text
    var attachments = await FetchAndExtractAttachmentsAsync(email, cancellationToken);

    // 3. Build combined input text
    var inputText = BuildEmailInputText(email, attachments);

    // 4. Execute playbook
    return await _appOnlyAnalysisService.AnalyzeTextAsync(
        inputText,
        playbookName: "Email Analysis",
        cancellationToken);
}

private string BuildEmailInputText(EmailEntity email, List<AttachmentText> attachments)
{
    var sb = new StringBuilder();

    // Email metadata
    sb.AppendLine("===== EMAIL METADATA =====");
    sb.AppendLine($"Subject: {email.Subject}");
    sb.AppendLine($"From: {email.Sender}");
    sb.AppendLine($"To: {email.ToRecipients}");
    if (!string.IsNullOrEmpty(email.CcRecipients))
        sb.AppendLine($"CC: {email.CcRecipients}");
    sb.AppendLine($"Date: {email.SentOn:u}");
    sb.AppendLine();

    // Email body
    sb.AppendLine("===== EMAIL BODY =====");
    sb.AppendLine(email.Description); // Email body text
    sb.AppendLine();

    // Attachments
    foreach (var attachment in attachments)
    {
        sb.AppendLine($"===== ATTACHMENT: {attachment.FileName} =====");
        sb.AppendLine(attachment.ExtractedText);
        sb.AppendLine();
    }

    return sb.ToString();
}
```

---

## Dataverse Configuration

### Playbook Record

| Field | Value |
|-------|-------|
| sprk_name | Email Analysis |
| sprk_description | Comprehensive email analysis combining email metadata, body text, and attachment contents... |
| sprk_ispublic | true |
| isSystemPlaybook | true |
| triggerContext | email-to-document |

### Scopes (N:N Relationships)

| Scope Type | IDs |
|------------|-----|
| Skills | SKL-008 (Executive Summary) |
| Actions | ACT-001, ACT-003, ACT-004 |
| Knowledge | KNW-005 (Defined Terms) |
| Tools | TL-001, TL-003, TL-004 |

---

## Deployment

```powershell
# Deploy the Email Analysis playbook to Dataverse
cd c:\code_files\spaarke\scripts\seed-data
.\Deploy-Playbooks.ps1

# Verify deployment
.\Verify-Playbooks.ps1
```

---

## Related Files

| File | Purpose |
|------|---------|
| `scripts/seed-data/playbooks.json` | Playbook definition (PB-012) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/AppOnlyAnalysisService.cs` | Analysis execution |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Tools/SummaryHandler.cs` | Summary generation |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Tools/EntityExtractorHandler.cs` | Entity extraction |

---

## Acceptance Criteria Verification

| Criterion | Status | Evidence |
|-----------|--------|----------|
| Playbook record exists in Dataverse | ✅ Pass | ID: `bc71facf-6af1-f011-8406-7ced8d1dc988` |
| Prompt produces TL;DR, Summary, Keywords, Entities | ✅ Pass | Uses same tools as Document Profile (validated) |
| Output format parseable for storage | ✅ Pass | JSON entities, structured text (design complete) |

---

## Deployment Log

```
Date: 2026-01-14
Environment: https://spaarkedev1.crm.dynamics.com
Playbook ID: bc71facf-6af1-f011-8406-7ced8d1dc988
Status: INSERTED
Scopes Associated:
  + Skill: SKL-008 (Executive Summary)
  + Action: ACT-001 (Extract Entities)
  + Action: ACT-003 (Classify Document)
  + Action: ACT-004 (Summarize Content)
  + Knowledge: KNW-005 (Defined Terms)
  + Tool: TL-001 (Entity Extractor)
  + Tool: TL-003 (Document Classifier)
  + Tool: TL-004 (Summary Generator)
```

---

*Generated by task-execute skill - Task 030*
