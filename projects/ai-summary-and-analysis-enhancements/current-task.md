# Current Task State - AI Summary and Analysis Enhancements

> **Last Updated**: 2026-01-07 (Phase 2.2 complete - tasks 014-019)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Phase 2.2 complete (19/27 tasks), ready for Phase 2.3 |
| **Step** | Task 020 next (Route Document Intelligence Endpoint) |
| **Status** | in-progress (19/27 tasks - 70% complete) |
| **Next Action** | Say "work on task 020" (endpoint migration) |

### Git Status
- **Branch**: `feature/ai-summary-and-analysis-enhancements`
- **PR**: [#103](https://github.com/spaarke-dev/spaarke/pull/103) (draft)
- **Last Commit**: pending (tasks 001-006 files uncommitted)

### Files Modified This Session (Phase 2.2 Backend)
- `src/server/api/Sprk.Bff.Api/Services/Ai/DocumentProfileFieldMapper.cs` - Created field mapper (Task 014)
- `src/server/api/Sprk.Bff.Api/Models/Ai/DocumentProfileResult.cs` - Created result model (Task 015)
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` - Added dual storage with soft failure (Task 016)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/SpaarkeCore.cs` - Registered IStorageRetryPolicy (Task 016)
- `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` - Updated SSE response format (Task 018)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/DocumentProfileFieldMapperTests.cs` - 34 tests (Task 014)
- `tests/unit/Sprk.Bff.Api.Tests/Models/Ai/DocumentProfileResultTests.cs` - 11 tests (Task 017)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/DocumentProfileStorageTests.cs` - 12 tests (Task 017)
- `tests/unit/Sprk.Bff.Api.Tests/Api/Ai/AnalysisStreamChunkTests.cs` - 16 tests (Task 018)

### Files Modified This Session (Phase 2.2 Frontend)
- `src/client/pcf/UniversalQuickCreate/control/services/useAiSummary.ts` - Parse partialStorage from SSE (Task 019)
- `src/client/pcf/UniversalQuickCreate/control/components/AiSummaryPanel.tsx` - Display warning MessageBar (Task 019)
- `src/client/pcf/UniversalQuickCreate/control/components/AiSummaryCarousel.tsx` - Pass new props (Task 019)
- `tasks/TASK-INDEX.md` - Updated progress (19/27)

### Critical Context
**Document Profile is NOT a special case**—it's just another Playbook execution with:
- Different trigger (auto on upload)
- Different UI context (File Upload Tab 2)
- Additional storage (maps to sprk_document fields)

---

## Full State (Detailed)

### Project Summary

Unify AI Summary (Document Profile) and AI Analysis into single orchestration service with FullUAC authorization.

### Implementation Phases

| Phase | Tasks | Description | Status |
|-------|-------|-------------|--------|
| 2.1 | 7 | Unify Authorization (FullUAC + retry) | ✅ complete (7/7) |
| 2.2 | 10 | Document Profile Playbook Support + PCF | ✅ complete (10/10) |
| 2.3 | 5 | Migrate AI Summary Endpoint | not-started |
| 2.4 | 5 | Cleanup (immediately after deployment) | not-started |
| Wrap-up | 1 | Project closure | not-started |

### Key Decisions (Owner Clarifications 2026-01-06)

1. **Terminology**: "Document Profile" (not "Auto-Summary" or "Simple Mode")
2. **Authorization**: FullUAC mode (security requirement)
3. **Retry**: Storage operations only, 3x with exponential backoff (2s, 4s, 8s)
4. **Failure**: Soft failure - outputs preserved in sprk_analysisoutput
5. **Entities**: Use existing (sprk_analysisplaybook, sprk_aioutputtype, sprk_analysisoutput)
6. **Cleanup**: Immediately after deployment verified
7. **PCF Updates**: Display warning MessageBar on soft failure (added after review)

### Phase 2.2 Tasks 014-019 (COMPLETED)

**Task 014**: ✅ Implemented DocumentProfileFieldMapper (34 tests)
**Task 015**: ✅ Created DocumentProfileResult model (11 tests)
**Task 016**: ✅ Implemented dual storage with soft failure handling (12 tests)
**Task 017**: ✅ Created integration tests for Document Profile storage
**Task 018**: ✅ Updated SSE response format for partialStorage (16 tests, backward compatible)
**Task 019**: ✅ Updated PCF to display warning MessageBar (Fluent UI v9, dark mode compliant)

### Task 020 Details (Next)

**Task 020**: Route Document Intelligence Endpoint (Phase 2.3)
- Update Document Intelligence endpoints to route through AnalysisOrchestrationService
- Map request/response for backward compatibility
- Begin deprecation path for legacy endpoints

---

## Session History

### 2026-01-06 Session
- Investigated 403 error on AI Summary
- Applied Phase 1 scaffolding workaround (PR #102)
- Created spec.md with detailed design
- Conducted owner interview - captured 7 key decisions
- Updated spec.md with Document Profile terminology
- Ran project-pipeline to generate artifacts and tasks
- Added PCF tasks (018, 019) after user pointed out missing UI updates
- Checkpoint saved before compaction

### 2026-01-07 Session
- Resumed after compaction
- Completed task 001: Created IAiAuthorizationService interface
  - AuthorizationResult record with Success, Reason, AuthorizedDocumentIds
  - AuthorizeAsync method with ClaimsPrincipal, documentIds, CancellationToken
  - XML documentation for FullUAC mode and retry responsibilities
- Completed task 002: Implemented AiAuthorizationService with FullUAC
  - Uses IAccessDataSource.GetUserAccessAsync() (calls RetrievePrincipalAccess)
  - Checks for Read access flag
  - Diagnostic logging with [AI-AUTH] prefix
  - Handles null user, empty documentIds, partial authorization
- Completed task 003: Created StorageRetryPolicy with exponential backoff
  - 3 retries with 2s, 4s, 8s delays
  - Handles 404 (replication lag) and 503 (service unavailable)
  - Logging with [STORAGE-RETRY] prefix
  - StorageRetryableException for explicit retry signaling
- Completed tasks 004, 005, 006 (parallel):
  - Task 004: Updated AnalysisAuthorizationFilter to use IAiAuthorizationService
  - Task 005: Updated AiAuthorizationFilter to use IAiAuthorizationService
  - Task 006: Created AiAuthorizationServiceTests.cs with 20+ test cases
  - Registered IAiAuthorizationService in DI (SpaarkeCore.cs)
  - Updated existing filter tests to use new interface
- Build verified successful (6/27 tasks complete)

### Current Session (Continued - Phase 2.2)
- Completed tasks 014-019 (parallel execution):
  - Task 014: Created DocumentProfileFieldMapper with static methods
  - Task 015: Already completed (DocumentProfileResult model)
  - Task 016: Implemented dual storage with soft failure (StoreDocumentProfileOutputsAsync)
  - Task 017: Created integration tests (DocumentProfileResultTests, DocumentProfileStorageTests)
  - Task 018: Updated AnalysisStreamChunk with partialStorage and storageMessage fields
  - Task 019: Updated PCF components (useAiSummary hook, AiSummaryPanel, AiSummaryCarousel)
- All tests passing: 73 backend tests + PCF build successful
- Phase 2.2 complete (19/27 tasks - 70% complete)
