# Current Task — code-quality-and-assurance-r2

## Active Task
- **Task ID**: 013
- **Task File**: tasks/013-migrate-dataverse-consumers.poml
- **Title**: Migrate IDataverseService Consumers to Narrow Interfaces
- **Phase**: 2: Backend Structural Decomposition
- **Status**: completed
- **Started**: 2026-03-15
- **Completed**: 2026-03-15
- **Rigor Level**: FULL (bff-api tag, .cs modification, 9 steps, 3 dependencies)

## Quick Recovery
If resuming after compaction or new session:
1. Read this file
2. Read TASK-INDEX.md for overall progress
3. Say "continue" to pick up next pending task

| Field | Value |
|-------|-------|
| **Task** | 013 - Migrate IDataverseService Consumers to Narrow Interfaces (COMPLETED) |
| **Step** | 9 of 9: All steps complete |
| **Status** | completed |
| **Next Action** | Task 014 (Build verification + integration test pass) is now unblocked |

## Progress
- Task 001: Fix 3 Unbounded Static Dictionaries — COMPLETED 2026-03-14
- Task 002: Replace new HttpClient() with IHttpClientFactory — COMPLETED (prior session)
- Task 003: Fix No-Op Arch Tests + Add Plugin Assembly Coverage — COMPLETED 2026-03-14
- Task 004: Delete Dead MsalAuthProvider.ts + Create Shared Logger — COMPLETED 2026-03-15
- Task 010: Decompose OfficeService.cs → 4 Focused Services — COMPLETED 2026-03-15
- Task 011: Decompose AnalysisOrchestrationService → 3 Services — COMPLETED 2026-03-15
- Task 012: Segregate IDataverseService into 9 Focused Interfaces — COMPLETED 2026-03-15
- Task 013: Migrate IDataverseService Consumers to Narrow Interfaces — COMPLETED 2026-03-15
- Task 020: Extract useAuth + useDocumentResolution Hooks — COMPLETED 2026-03-15
- Task 021: Extract useAnalysisData + useAnalysisExecution Hooks — COMPLETED 2026-03-15
- Task 022: Extract useWorkingDocumentSave + useChatState Hooks — COMPLETED 2026-03-15 (parallel session)
- Task 023: Extract usePanelResize + Finalize Component Decomposition — COMPLETED 2026-03-15
- Task 030: Fix ADR-022 violations — React 18→16 in 3 PCF controls — COMPLETED (prior session)
- Task 031: Document BaseProxyPlugin ADR-002 Violations — COMPLETED 2026-03-15

## Files Modified (Task 013)

### Services Migrated to Narrow Interfaces
- `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/RagIndexingJobHandler.cs` — IDataverseService → IDocumentDataverseService
- `src/server/api/Sprk.Bff.Api/Services/Finance/InvoiceReviewService.cs` — IDataverseService → IDocumentDataverseService + IFieldMappingDataverseService
- `src/server/api/Sprk.Bff.Api/Services/Finance/SignalEvaluationService.cs` — IDataverseService → IFieldMappingDataverseService
- `src/server/api/Sprk.Bff.Api/Services/Ai/AppOnlyAnalysisService.cs` — IDataverseService → IDocumentDataverseService + IAnalysisDataverseService
- `src/server/api/Sprk.Bff.Api/Services/Office/OfficeDocumentPersistence.cs` — verified already narrow
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisDocumentLoader.cs` — verified already narrow
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisResultPersistence.cs` — verified already narrow
- `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs` — migrated
- `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationAccountService.cs` — migrated
- `src/server/api/Sprk.Bff.Api/Services/Communication/IncomingCommunicationProcessor.cs` — migrated
- `src/server/api/Sprk.Bff.Api/Services/Communication/IncomingAssociationResolver.cs` — migrated
- `src/server/api/Sprk.Bff.Api/Services/Communication/MailboxVerificationService.cs` — migrated
- `src/server/api/Sprk.Bff.Api/Services/ScorecardCalculatorService.cs` — migrated

### Endpoints Migrated
- `src/server/api/Sprk.Bff.Api/DataverseDocumentsEndpoints.cs` — IDocumentDataverseService
- `src/server/api/Sprk.Bff.Api/FileAccessEndpoints.cs` — IDocumentDataverseService
- `src/server/api/Sprk.Bff.Api/EmailEndpoints.cs` — IDocumentDataverseService
- `src/server/api/Sprk.Bff.Api/CommunicationEndpoints.cs` — IGenericEntityService
- `src/server/api/Sprk.Bff.Api/Events/EventEndpoints.cs` — IEventDataverseService
- `src/server/api/Sprk.Bff.Api/FieldMappings/FieldMappingEndpoints.cs` — IFieldMappingDataverseService
- `src/server/api/Sprk.Bff.Api/NavMapEndpoints.cs` — IGenericEntityService
- `src/server/api/Sprk.Bff.Api/Ai/RagEndpoints.cs` — IDocumentDataverseService
- `src/server/api/Sprk.Bff.Api/Ai/RecordMatchEndpoints.cs` — IDocumentDataverseService
- `src/server/api/Sprk.Bff.Api/Ai/VisualizationEndpoints.cs` — IDocumentDataverseService
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs` — IDocumentDataverseService + IDataverseHealthService
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/DebugEndpointExtensions.cs` — IDocumentDataverseService

### DI Registration
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/GraphModule.cs` — 9 forwarding registrations added

### Services Keeping IDataverseService (with justifying comments)
- `src/server/api/Sprk.Bff.Api/Services/Finance/FinanceRollupService.cs` — casts to ServiceClient for FetchXML
- `src/server/api/Sprk.Bff.Api/Services/Finance/FinanceSummaryService.cs` — casts to DataverseServiceClientImpl for FetchXML
- `src/server/api/Sprk.Bff.Api/Services/Finance/SpendSnapshotService.cs` — casts to DataverseServiceClientImpl for FetchXML
- `src/server/api/Sprk.Bff.Api/Services/Finance/Tools/FinancialCalculationToolHandler.cs` — casts to ServiceClient for FetchXML
- `src/server/api/Sprk.Bff.Api/Services/Workspace/TodoGenerationService.cs` — casts to DataverseServiceClientImpl + multiple domains
- `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeManagementService.cs` — scaffolding, no method calls yet

### Test Files Fixed (21 files)
- 4 ScorecardCalculator test files — added second mock param
- EmailAnalysisIntegrationTests.cs — added IAnalysisDataverseService param
- AnalysisOrchestrationServiceTests.cs — updated AnalysisDocumentLoader + AnalysisResultPersistence constructors
- 14 Communication test files — updated constructor mock params
- MailboxVerificationTests.cs — added second mock param

## Notes (Task 013)
- All consumers migrated to narrowest applicable interface(s)
- 6 services justified to keep IDataverseService (FetchXML casts, scaffolding)
- 9 DI forwarding registrations in GraphModule.cs (ADR-010 compliant — forwarding delegates don't count)
- dotnet build: 0 errors, 0 warnings
- dotnet test build: 3 pre-existing UploadSessionManager errors only (verified in baseline)
- Mock pattern: Mock<IDataverseService>.Object satisfies narrow interface params via inheritance
