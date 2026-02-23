# Project Follow-up Items

> **Project**: email-to-document-automation-r2
> **Last Updated**: 2026-01-15

---

## Open Items

### P2 - AI Analysis Not Running on Automated Documents (DEFERRED)

**Discovered**: 2026-01-15 (Phase 4 deployment verification)
**Status**: DEFERRED to `ai-document-intelligence-r5` project
**Documentation**: [ai-analysis-integration-issue.md](ai-analysis-integration-issue.md)

**Summary**: Automated AI analysis (triggered by email-to-document jobs) fails because `ScopeResolverService.ResolvePlaybookScopesAsync` is a placeholder returning empty scopes. Manual analysis works because UI passes `ToolIds[]` directly.

**Impact**: Documents created by automated email processing will NOT have AI analysis, even with `AutoEnqueueAi = true`.

**Workaround**: Users can manually trigger AI analysis from the Analysis Builder UI.

**Recommended Resolution**: Option C (Unified Path) - Refactor `AppOnlyAnalysisService` to pass playbook IDs directly like the UI does, eliminating the need for a separate orchestration path.

**See full documentation**: [ai-analysis-integration-issue.md](ai-analysis-integration-issue.md)

---

## Resolved Items

### ~~P2 - Attachment Child Documents Not Created (RACE CONDITION)~~ ✅ RESOLVED

**Discovered**: Phase 4 deployment verification (Task 039)
**Resolved**: 2026-01-15
**Resolution**: Implemented retry logic + architectural refactoring

**Root Cause**: Race condition between webhook and attachment creation. The Dataverse webhook fires on email **Create** event, but attachments are added via separate `activitymimeattachments` API calls. The webhook job could process before attachments existed.

**Fix Applied (Two Parts)**:

1. **Retry Logic** in `FetchAttachmentsAsync` (`EmailToEmlConverter.cs`):
   - If 0 attachments found on first query, wait 2 seconds and retry once
   - Handles race condition where webhook fires before attachments are created
   - Location: `src/server/api/Sprk.Bff.Api/Services/Email/EmailToEmlConverter.cs`

2. **Architectural Refactoring** - Eliminated redundant re-extraction:
   - **Before**: Fetched attachments → Built .eml → Re-extracted from .eml → Processed
   - **After**: Fetched attachments → Built .eml → Pass attachments directly → Processed
   - Changed `ProcessAttachmentsAsync` to accept `IReadOnlyList<EmailAttachmentInfo>` directly
   - Removed redundant `ExtractAttachments(emlStream)` call from job handler
   - Benefits: Faster processing, reduced memory allocation, cleaner architecture

**Files Modified**:
- `src/server/api/Sprk.Bff.Api/Services/Email/EmailToEmlConverter.cs` - Added retry logic
- `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/EmailToDocumentJobHandler.cs` - Refactored to pass attachments directly

**Verification Status**: ✅ VERIFIED
- API deployed at 2026-01-15 05:34 UTC (retry logic)
- API redeployed at 2026-01-15 ~07:00 UTC (EmailLookup fix for child documents)
- Test email verified: Child documents now have SPE fields populated (GraphDriveId, GraphItemId, FilePath)
- Screenshot confirmation from user showing SPE fields populated on attachment documents

---

### ~~P1 - Child Document SPE Fields Not Set (ALTERNATE KEY VIOLATION)~~ ✅ RESOLVED

**Discovered**: 2026-01-15 (Phase 4 deployment verification - Task 039)
**Resolved**: 2026-01-15
**Resolution**: Removed `EmailLookup` from child document UpdateDocumentRequest

**Root Cause**: The `sprk_document` entity has an alternate key on the `sprk_email` field (Email Activity Key). When creating child documents for attachments, the code was setting `EmailLookup = emailId`, which violated the alternate key because the parent .eml document already used that email lookup.

**Error Message**: `Entity Key Email Activity Key violated. A record with the same value for Email already exists.`

**Fix Applied**: Removed `EmailLookup = emailId` from child document updates in `EmailToDocumentJobHandler.cs`. Added comment explaining that child documents relate to the email through their `ParentDocumentLookup` instead.

**Files Modified**:
- `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/EmailToDocumentJobHandler.cs`

**Verification**:
- Test email processed successfully
- Child documents (attachments) now have SPE fields populated:
  - `GraphDriveId` ✅
  - `GraphItemId` ✅
  - `FilePath` ✅
- No alternate key violations in logs

---

### ~~P1 - Email Lookup Field Not Set~~ ✅ RESOLVED

**Discovered**: Phase 4 deployment verification (Task 039)
**Resolved**: 2026-01-14
**Resolution**: Added missing email fields to `DataverseServiceClientImpl.UpdateDocumentAsync`

**Root Cause**: `DataverseServiceClientImpl.UpdateDocumentAsync` was missing the following field mappings that `DataverseWebApiService` had:
- `EmailLookup` → `sprk_email` (EntityReference to `email` table)
- `IsEmailArchive` → `sprk_isemailarchive`
- `EmailCc` → `sprk_emailcc`
- `EmailMessageId` → `sprk_emailmessageid`
- `EmailDirection` → `sprk_emaildirection`
- `EmailTrackingToken` → `sprk_emailtrackingtoken`
- `EmailConversationIndex` → `sprk_emailconversationindex`
- `RelationshipType` → `sprk_relationshiptype`

**Fix Applied**: Added all missing fields to `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` (lines 306-326)

**Verification**:
- Test Email #20 (ID: `7f237b6b-7ef1-f011-8406-7c1e520aa4df`)
- Document created: `9a237b6b-7ef1-f011-8406-7c1e520aa4df`
- Document has `_sprk_email_value` = `7f237b6b-7ef1-f011-8406-7c1e520aa4df` ✅
- Document has `sprk_isemailarchive` = `true` ✅
- Manual Email Analysis Playbook execution: ✅ Working correctly

---

*Track follow-up items discovered during project execution*
