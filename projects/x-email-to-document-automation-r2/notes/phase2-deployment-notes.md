# Phase 2 Deployment Notes - Attachment Processing

> **Deployed**: 2026-01-14
> **Environment**: Dev (spe-api-dev-67e2xz)
> **Phase**: 2 - Attachment Processing

---

## Deployment Summary

### Build and Deploy
- **Build**: `dotnet publish` Release configuration
- **Deploy**: `az webapp deploy --type zip`
- **Deployment ID**: b4d918f6b3a843c9b441c0d4318ccfab
- **Status**: Succeeded

### Health Verification
- **Health endpoint**: `GET /healthz` → 200 OK, "Healthy"
- **Ping endpoint**: `GET /ping` → 200 OK, "pong"

---

## Phase 2 Features Deployed

### 1. Attachment Extraction (Task 010)
- `ExtractAttachments` method in `EmailToEmlConverter`
- Parses .eml files using MimeKit
- Extracts all MIME attachments with metadata
- Identifies inline vs regular attachments via ContentId

### 2. Attachment Filtering (Task 011)
- `AttachmentFilterService` filters noise attachments
- Blocked extensions: .exe, .dll, .bat, .ps1, .vbs, .js, .cmd
- Signature patterns: image001.png, spacer.gif, logo*.png
- Tracking pixels: pixel.gif, beacon.gif, 1x1.gif
- Calendar files: .ics, .vcs (when FilterCalendarFiles=true)
- Small images: < 5KB threshold (MinImageSizeKB)
- Inline attachments: filtered when FilterInlineAttachments=true

### 3. Job Handler Integration (Task 012)
- `ProcessAttachmentsAsync` in `EmailToDocumentJobHandler`
- Uploads attachments to SPE at `/emails/attachments/{parentDocumentId:N}/{filename}`
- Creates child Document records with:
  - `sprk_ParentDocumentLookup` = parent Document ID
  - `sprk_DocumentType` = 100000007 (Email Attachment)
  - `sprk_RelationshipType` = 100000000 (Email Attachment)
- Graceful error handling - attachment failures don't fail main job

### 4. Unit Tests (Task 013)
- 64 new unit tests added
- `EmailAttachmentExtractionTests.cs` - 18 tests
- `AttachmentFilterServiceTests.cs` - 46 tests
- All tests pass (272/273 email tests, 1 pre-existing CC failure)

---

## Verification Checklist

### Automated Verification
- [x] Local tests pass (272/273)
- [x] Build succeeds (Release configuration)
- [x] Deploy succeeds to Azure App Service
- [x] Health endpoint returns 200
- [x] Ping endpoint returns 200

### Manual Verification Required
The following require manual testing with real emails in Dataverse:

- [ ] Trigger email processing for email with PDF attachment
- [ ] Verify parent .eml Document created in Dataverse
- [ ] Query child Documents by `sprk_ParentDocumentLookup`
- [ ] Verify PDF child Document exists with correct relationship
- [ ] Verify small signature image was filtered (not created)
- [ ] Download child Document via `/api/documents/{id}/download`

### Verification Query (FetchXML)
```xml
<fetch>
  <entity name="sprk_document">
    <attribute name="sprk_documentid" />
    <attribute name="sprk_name" />
    <attribute name="sprk_documenttype" />
    <attribute name="sprk_parentdocumentlookup" />
    <filter>
      <condition attribute="sprk_parentdocumentlookup" operator="eq" value="{parentDocumentId}" />
    </filter>
  </entity>
</fetch>
```

---

## Configuration

### EmailProcessingOptions (appsettings.json)
```json
{
  "EmailProcessing": {
    "MaxAttachmentSizeMB": 25,
    "MinImageSizeKB": 5,
    "FilterCalendarFiles": true,
    "FilterInlineAttachments": true,
    "BlockedAttachmentExtensions": [".exe", ".dll", ".bat", ".ps1", ".vbs", ".js", ".cmd"],
    "SignatureImagePatterns": [
      "^image\\d{3}\\.(png|gif|jpg|jpeg)$",
      "^spacer\\.(gif|png)$",
      "^logo.*\\.(png|gif|jpg|jpeg)$"
    ],
    "TrackingPixelPatterns": [
      "pixel\\.(gif|png)",
      "beacon\\.(gif|png)",
      "^1x1\\.(gif|png)$"
    ]
  }
}
```

---

## Known Issues

1. **CC Recipients Test Failure**: Pre-existing test failure for CC recipients - Dataverse email entity doesn't return ccrecipients field directly (requires activityparty query). Not a Phase 2 issue.

2. **Attachment Processing is Sequential**: Attachments are processed one at a time to avoid overwhelming SPE. For emails with many attachments, this may take longer but is more reliable.

---

## Telemetry

Phase 2 adds attachment processing telemetry:
- `email.attachment.operation=extracted` - Count of attachments found
- `email.attachment.operation=filtered` - Count filtered as noise
- `email.attachment.operation=uploaded` - Count successfully uploaded
- `email.attachment.operation=failed` - Count of failures

Monitor via Application Insights:
```kusto
customMetrics
| where name == "email.attachments.processed"
| summarize sum(value) by tostring(customDimensions["email.attachment.operation"])
```

---

## Next Steps

Phase 2 is complete pending manual verification. Next phase:
- **Phase 3**: AppOnlyAnalysisService (Tasks 020-029)
  - Create app-only AI analysis service
  - Integrate with email handler for automatic AI enqueue

---

*Deployment completed by Claude Code*
