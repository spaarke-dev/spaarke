# Current Task State - Email-to-Document Automation

> **Last Updated**: 2026-01-13 16:45 (by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Email-to-Document Automation - Phase 2 |
| **Step** | Documentation complete, ready for user testing |
| **Status** | waiting-on-user |
| **Next Action** | User: Test email to verify `sprk_filepath` is populated. Then start Phase A (attachment processing). |

### Documentation Update ✅ COMPLETE

Updated the following docs to document the email processing approach and bug fixes:
1. ✅ `docs/architecture/sdap-auth-patterns.md` - Added Pattern 6: App-Only Auth for Background Email Processing
2. ✅ `docs/architecture/sdap-bff-api-patterns.md` - Added Email Webhook + Job Handler Pattern section
3. ✅ `docs/architecture/auth-azure-resources.md` - Added Email Processing Configuration section
4. ✅ `docs/guides/EMAIL-TO-DOCUMENT-ARCHITECTURE.md` - Added Bug Fixes, Auth Pattern, and Future AI Architecture sections

**Documented:**
- App-only auth for SPE file uploads (vs OBO for PCF) - Pattern 6
- Dataverse field mappings: `sprk_filepath`, `sprk_mimetype`, `sprk_filesize` (int not long)
- WCF date format handling (`/Date(123)/`) for webhook payloads
- DefaultContainerId is Drive ID format (`b!xxx`), not raw GUID
- Planned AppOnlyAnalysisService architecture (separate from OBO AnalysisOrchestrationService)

### Immediate Next Steps

1. **User Testing**: Test email to verify `sprk_filepath` is populated correctly
2. **Phase A**: Implement attachment processing (extract, upload, create child Documents)
3. **Phase B**: Create AppOnlyAnalysisService for background AI analysis
4. **Phase C**: Create Email Analysis Playbook

### Files Modified This Session
**Code Changes (Earlier):**
- `src/server/shared/Spaarke.Dataverse/Models.cs` - Added `FilePath` property to `UpdateDocumentRequest`
- `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` - Added `sprk_filepath` mapping + earlier fixes
- `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/EmailToDocumentJobHandler.cs` - Set `FilePath = fileHandle.WebUrl`
- `src/server/api/Sprk.Bff.Api/Models/Email/DataverseWebhookPayload.cs` - Added NullableWcfDateTimeConverter
- `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs` - Fixed sprk_filetype → sprk_mimetype
- `scripts/Set-ContainerId.ps1` - Created to fix Azure escaping issue

**Documentation Updates (This Session):**
- `docs/architecture/sdap-auth-patterns.md` - Added Pattern 6: App-Only Auth for Background Email Processing
- `docs/architecture/sdap-bff-api-patterns.md` - Added Email Webhook + Job Handler Pattern
- `docs/architecture/auth-azure-resources.md` - Added Email Processing Configuration section
- `docs/guides/EMAIL-TO-DOCUMENT-ARCHITECTURE.md` - Added Bug Fixes, Auth Pattern, and Future AI Architecture sections

### Critical Context
**End-to-end flow is WORKING!** Email → .eml conversion → SPE upload → Document record creation all succeed.

---

## Issue #1: FilePath URL Not Saved ✅ CODE COMPLETE

**Problem:** PCF sets `sprk_filepath: file.webUrl` but job handler wasn't setting this field.

**Fix Applied (3 changes):**
1. ✅ Added `FilePath` property to `UpdateDocumentRequest` in `Models.cs:29-30`
2. ✅ Added mapping `sprk_filepath = request.FilePath` in `DataverseServiceClientImpl.cs:240-241`
3. ✅ Set `FilePath = fileHandle.WebUrl` in `EmailToDocumentJobHandler.cs:212`

**Status:** Code complete, build passes. Ready to deploy and test.

---

## Issue #2 + #3: Attachment Processing + AI Analysis (NEXT PROJECT PHASE)

### Three Distinct Requirements

| # | Entity | What's Needed | Current State |
|---|--------|---------------|---------------|
| **1** | **Document** (.eml) | SPE file + AI Document Profile | ✅ File uploaded, ❌ AI analysis |
| **2** | **Document** (per attachment) | SPE file + AI Document Profile (child of .eml Document) | ❌ Not created |
| **3** | **Email** (activity record) | AI analysis combining email metadata + all attachment content | ❌ Not implemented |

### Why Three Separate Analyses?

**Azure Document Intelligence does NOT process .eml files natively.** It supports: PDF, JPEG, PNG, BMP, TIFF, HEIF, DOCX, XLSX, PPTX, HTML.

For .eml files, `TextExtractorService` falls back to raw text extraction which sees MIME structure but does NOT decode base64-encoded attachments meaningfully.

**Therefore:**
- Document (.eml) → Document Profile on email body text only
- Document (attachment) → Document Profile on actual file (PDF, DOCX, etc.) - these CAN be processed by Azure AI
- Email (activity) → **Email Analysis Playbook** combining email metadata + all attachment contents

### Architectural Decisions Made

1. **Attachment Documents are children** of .eml Document via `sprk_ParentDocumentLookup`

2. **Email Analysis output goes to Email record only** - Document record (for .eml) gets its own separate Document Profile analysis

3. **Create separate AppOnlyAnalysisService** - Don't complicate `AnalysisOrchestrationService` with both OBO and app-only paths

### Proposed Service Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  User-Initiated (OBO)              App-Only (Background)    │
│  ─────────────────────             ────────────────────     │
│  AnalysisOrchestrationService      AppOnlyAnalysisService   │
│  - PCF triggers                    - Email processing       │
│  - User context (OBO)              - Bulk uploads           │
│  - HttpContext required            - No user context        │
│                                                             │
│              ↘                    ↙                        │
│                 Shared Components                           │
│                 - OpenAiClient                              │
│                 - TextExtractorService                      │
│                 - SpeFileStore (app-only mode)              │
│                 - IDataverseService                         │
└─────────────────────────────────────────────────────────────┘
```

### Processing Flow (To Be Implemented)

```
Email Webhook Triggers
        ↓
┌───────────────────────────────────────────────────────────┐
│ EmailToDocumentJobHandler (Enhanced)                       │
│                                                            │
│  1. Convert email → .eml file                              │
│  2. Upload .eml to SPE → Document record (parent)          │
│  3. For each attachment:                                   │
│     a. Extract from email                                  │
│     b. Upload to SPE                                       │
│     c. Create child Document record                        │
│        (sprk_ParentDocumentLookup → parent .eml Document)  │
│  4. Queue Document Profile analysis for .eml Document      │
│  5. Queue Document Profile analysis for each attachment    │
│  6. Queue Email Analysis job                               │
│     - Input: Email ID + all Document IDs                   │
│     - Playbook: "Email Analysis" (combines everything)     │
│     - Output: Updates Email record with AI analysis        │
└───────────────────────────────────────────────────────────┘
```

### Implementation Tasks (For Next Phase)

**Phase A: Attachment Processing**
1. Enhance `EmailToEmlConverter` to expose attachment extraction
2. Modify `EmailToDocumentJobHandler` to process attachments as separate uploads
3. Create child Document records with `sprk_ParentDocumentLookup` relationship

**Phase B: App-Only Analysis Service**
1. Create `IAppOnlyAnalysisService` interface
2. Implement `AppOnlyAnalysisService` using shared components
3. Add endpoint or job handler for triggering app-only analysis

**Phase C: Email Analysis Playbook**
1. Create "Email Analysis" playbook in Dataverse
2. Design prompt that combines email metadata + attachment contents
3. Implement playbook execution in `AppOnlyAnalysisService`

---

## Bug Fixes Completed Earlier This Session

| Bug | Fix | File(s) |
|-----|-----|---------|
| DateTime parse error on OperationCreatedOn | Added `[JsonConverter(typeof(NullableWcfDateTimeConverter))]` | DataverseWebhookPayload.cs |
| `sprk_filetype` field not found | Changed to `sprk_mimetype` | DataverseWebApiService.cs, DataverseServiceClientImpl.cs |
| Int64 type error on FileSize | Cast to `(int)` for Dataverse Whole Number field | DataverseServiceClientImpl.cs |
| DefaultContainerId Azure escaping | Used PowerShell script to avoid `!` escaping | Set-ContainerId.ps1 |

---

## Project Status

| Aspect | Status |
|--------|--------|
| Code Implementation | ✅ 33/33 original tasks complete |
| Azure Deployment | ✅ Deployed with FilePath fix |
| End-to-End Flow | ✅ Working |
| FilePath URL (Issue #1) | ✅ Deployed, needs user testing |
| Documentation Update | ✅ Complete - all docs updated |
| Attachment Processing (Issue #2) | ❌ Design complete, needs implementation |
| AI Analysis (Issue #3) | ❌ Architecture defined, needs implementation |

---

## Azure Configuration (Verified)

| Setting | Value |
|---------|-------|
| `EmailProcessing__DefaultContainerId` | `b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50` |

---

## Git State

- **Branch**: `work/email-to-document-automation`
- **Uncommitted**: Issue #1 fix + earlier bug fixes (commit after successful test)
