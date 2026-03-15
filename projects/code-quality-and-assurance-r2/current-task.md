# Current Task — code-quality-and-assurance-r2

## Active Task
- **Task ID**: 014
- **Task File**: tasks/014-backend-build-verification.poml
- **Title**: Build Verification and Integration Test Pass After Phase 2
- **Phase**: 2: Backend Structural Decomposition
- **Status**: completed
- **Started**: 2026-03-15
- **Completed**: 2026-03-15
- **Rigor Level**: STANDARD (testing tag, verification task)

## Quick Recovery
If resuming after compaction or new session:
1. Read this file
2. Read TASK-INDEX.md for overall progress
3. Say "continue" to pick up next pending task

| Field | Value |
|-------|-------|
| **Task** | 014 - Build Verification and Integration Test Pass (COMPLETED) |
| **Step** | 10 of 10: All steps complete |
| **Status** | completed |
| **Next Action** | Task 032 (Final quality scorecard + lessons learned) is now unblocked |

## Phase 2 Verification Summary

### Build Status
- **dotnet build**: PASS — 0 errors, 0 warnings
- **Fixed during verification**: 3 UploadSessionManager constructor errors in test files (missing `IHttpClientFactory` parameter added in task 002)
- **Fixed during verification**: 4 AnalysisOrchestrationServiceTests failures (empty byte[] cache mock causing JsonException instead of KeyNotFoundException — added `bytes.Length == 0` guard in `AnalysisDocumentLoader.GetCachedAnalysisAsync`)

### Test Results
- **Unit tests (Sprk.Bff.Api.Tests)**: 4,176 passed, 0 failed, 105 skipped
- **Integration tests (Spe.Integration.Tests)**: 53 passed, 138 failed (pre-existing — require `SpeAdmin:KeyVaultUri` live Azure config), 108 skipped
- **Architecture fitness tests**: Not yet created — will be added in Phase 3

### Structural Metrics

| Metric | Target | Actual | Status |
|--------|--------|--------|--------|
| OfficeService.cs line count | < 500 | 1,951 | INCOMPLETE — 4 services extracted but coordinating class still large |
| AnalysisOrchestrationService constructor params | <= 10 | 10 | PASS |
| IDataverseService interface count | 9 | 9 | PASS |
| IDataverseService inline methods | 0 | 0 | PASS |
| Static unbounded dictionaries | 0 | 0 | PASS |
| new HttpClient() in src/server/ | 0 | 0 (1 in docs/ markdown only) | PASS |

### OfficeService.cs Gap Analysis
Task 010 extracted 4 focused services (`OfficeDocumentPersistence`, `OfficeEmailEnricher`, `OfficeJobQueue`, `OfficeStorageUploader`) but OfficeService.cs retained substantial business logic:
- Entity search methods (SearchEntitiesAsync, SearchDocumentsAsync)
- Share link creation (CreateShareLinksAsync, ProcessShareInvitationsAsync)
- Quick-create operations (QuickCreateAsync)
- Recent documents (GetRecentDocumentsAsync)
- Attachment packaging (GetAttachmentsAsync, PackageAttachmentAsync)
- SSE job status streaming (StreamJobStatusAsync, ProduceJobStatusEventsAsync)
- Stub data generators for development

This is a known incomplete extraction from task 010. The remaining methods represent distinct responsibility groups that could be further decomposed in a follow-up project.

## Progress
- Task 001: Fix 3 Unbounded Static Dictionaries — COMPLETED 2026-03-14
- Task 002: Replace new HttpClient() with IHttpClientFactory — COMPLETED (prior session)
- Task 003: Fix No-Op Arch Tests + Add Plugin Assembly Coverage — COMPLETED 2026-03-14
- Task 004: Delete Dead MsalAuthProvider.ts + Create Shared Logger — COMPLETED 2026-03-15
- Task 010: Decompose OfficeService.cs → 4 Focused Services — COMPLETED 2026-03-15
- Task 011: Decompose AnalysisOrchestrationService → 3 Services — COMPLETED 2026-03-15
- Task 012: Segregate IDataverseService into 9 Focused Interfaces — COMPLETED 2026-03-15
- Task 013: Migrate IDataverseService Consumers to Narrow Interfaces — COMPLETED 2026-03-15
- Task 014: Build Verification + Integration Test Pass — COMPLETED 2026-03-15
- Task 020: Extract useAuth + useDocumentResolution Hooks — COMPLETED 2026-03-15
- Task 021: Extract useAnalysisData + useAnalysisExecution Hooks — COMPLETED 2026-03-15
- Task 022: Extract useWorkingDocumentSave + useChatState Hooks — COMPLETED 2026-03-15 (parallel session)
- Task 023: Extract usePanelResize + Finalize Component Decomposition — COMPLETED 2026-03-15
- Task 024: PCF Build Verification — COMPLETED 2026-03-15
- Task 030: Fix ADR-022 violations — React 18→16 in 3 PCF controls — COMPLETED (prior session)
- Task 031: Document BaseProxyPlugin ADR-002 Violations — COMPLETED 2026-03-15

## Files Modified (Task 014)
- `tests/unit/Sprk.Bff.Api.Tests/SpeFileStoreTests.cs` — added IHttpClientFactory parameter to UploadSessionManager constructor
- `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/AssociationMappingTests.cs` — added IHttpClientFactory parameter
- `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/DataverseRecordCreationTests.cs` — added IHttpClientFactory parameter
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisDocumentLoader.cs` — added empty byte[] guard in GetCachedAnalysisAsync

## Notes (Task 014)
- Integration tests (138 failures) are all infrastructure-dependent (require Azure Key Vault URI) — pre-existing, not caused by Phase 2 changes
- OfficeService.cs line count (1,951) does not meet the < 500 target from task 010. This is a known gap carried forward. The 4 extracted services are correct and functional, but the original coordinating class retained too much business logic.
- All other Phase 2 metrics pass their acceptance criteria
