# BFF Matter Endpoints — Task Index

> **Branch**: `work/bff-matter-endpoints` | **PR**: #188

---

## Phase 1: BFF — Refactor Pre-Fill to Playbook System

### Task 1: Refactor MatterPreFillService to use IPlaybookOrchestrationService
**Status**: ✅ Complete
**Files**:
- `src/server/api/Sprk.Bff.Api/Services/Workspace/MatterPreFillService.cs` (modify)

**Details**:
- Replaced direct `IOpenAiClient.GetCompletionAsync()` with `IPlaybookOrchestrationService.ExecuteAsync()`
- Removed hardcoded extraction prompt and updated internal `AiPreFillResult` to match new field names
- Playbook ID configurable via `Workspace:PreFillPlaybookId` appsettings key
- Extracted text passed as `UserContext` in `PlaybookRunRequest` (files aren't sprk_document records yet)
- Consumes `IAsyncEnumerable<PlaybookStreamEvent>` for streaming playbook results
- Graceful degradation: empty response on AI timeout (45s) or playbook failure

### Task 2: Implement WorkspaceAiService with real playbook execution
**Status**: ✅ Complete
**Files**:
- `src/server/api/Sprk.Bff.Api/Services/Workspace/WorkspaceAiService.cs` (modify)

**Details**:
- Added `IPlaybookOrchestrationService` and `IConfiguration` constructor dependencies
- Added `HttpContext` parameter to `GenerateAiSummaryAsync` for OBO token flow
- New `ExecutePlaybookAnalysisAsync()` consumes playbook stream events
- Extracts analysis text and suggestedActions from `NodeOutput.StructuredData`
- Falls back to `TextContent` when structured data parsing fails
- Configurable playbook ID via `Workspace:AiSummaryPlaybookId`
- `BuildFallbackResponse()` returns template response when playbook unavailable

### Task 3: Update PreFillResponse for new form shape
**Status**: ✅ Complete
**Files**:
- `src/server/api/Sprk.Bff.Api/Api/Workspace/Models/PreFillResponse.cs` (modify)

**Details**:
- Removed obsolete fields: `Organization`, `EstimatedBudget`, `KeyParties`
- Fields: `MatterTypeName`, `PracticeAreaName`, `MatterName`, `Summary`, `Confidence`, `PreFilledFields`
- `PreFillResponse.Empty()` factory method for timeout/failure cases

---

## Phase 2: BFF — Endpoint Updates

### Task 4: Update WorkspaceMatterEndpoints for refactored pre-fill
**Status**: ✅ Complete (no changes needed)
**Files**:
- `src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceMatterEndpoints.cs` (verified)

**Details**:
- Endpoint already correctly delegates to `MatterPreFillService.AnalyzeFilesAsync()`
- `Produces<PreFillResponse>` matches updated response shape
- WorkspaceAuthorizationFilter + `ai-stream` rate limit unchanged
- Updated `WorkspaceAiEndpoints.cs` to pass `httpContext` to `GenerateAiSummaryAsync()`

### Task 5: Update DI registrations in WorkspaceModule
**Status**: ✅ Complete
**Files**:
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/WorkspaceModule.cs` (modify)

**Details**:
- Updated comments: MatterPreFillService depends on `IPlaybookOrchestrationService (scoped)`
- Updated comments: WorkspaceAiService depends on `IPlaybookOrchestrationService (scoped)`
- Registration count unchanged (8) — same services, different dependencies
- DI graph valid: both services are scoped, matching IPlaybookOrchestrationService scope

---

## Phase 3: Front-End — File Upload + Auth Infrastructure

### Task 6: Implement file upload via BFF OBO endpoint
**Status**: ✅ Complete
**Files**:
- `src/solutions/LegalWorkspace/src/components/CreateMatter/matterService.ts` (modify)
- `src/solutions/LegalWorkspace/src/services/EntityCreationService.ts` (new)

**Details**:
- Created `EntityCreationService.uploadFilesToSpe()` — uploads via `PUT /api/obo/drives/{driveId}/upload`
- `MatterService._uploadFiles()` removed — replaced with inline `EntityCreationService` calls
- After upload, creates `sprk_document` records linking uploaded files to matter via `@odata.bind`
- Progress callback support via `onUploadProgress` parameter
- `MatterService` constructor now accepts `containerId` for SPE container

### Task 7: Add authentication for BFF API calls
**Status**: ✅ Complete
**Files**:
- `src/solutions/LegalWorkspace/src/services/bffAuthProvider.ts` (new)

**Details**:
- Lightweight `IBffAuthProvider` interface with `getAccessToken()`, `clearCache()`, `isAuthenticated()`
- Token acquisition via PCF bridge pattern (window-level global `__SPAARKE_BFF_TOKEN__`)
- `authenticatedFetch()` helper — wraps fetch with Bearer header + 401 retry
- In-memory token cache with 5-minute expiration buffer
- Compatible with future MSAL migration (same interface, swap internals)
- Scope: `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation`

### Task 8: Add BFF base URL discovery
**Status**: ✅ Complete
**Files**:
- `src/solutions/LegalWorkspace/src/config/bffConfig.ts` (new)

**Details**:
- `getBffBaseUrl()` resolves URL from: window global → parent frame → fallback dev URL
- Fallback: `https://spe-api-dev-67e2xz.azurewebsites.net/api`
- URL normalization: ensures https:// prefix, removes trailing slash
- `fetchAiDraftSummary()` updated to use `getBffBaseUrl()` + `authenticatedFetch()`

---

## Phase 4: Front-End — Generic EntityCreationService

### Task 9: Create generic EntityCreationService
**Status**: ✅ Complete
**Files**:
- `src/solutions/LegalWorkspace/src/services/EntityCreationService.ts` (new)

**Details**:
- Entity-agnostic service for creation workflows (Matter, Project, future types)
- Methods:
  - `uploadFilesToSpe(containerId, files, onProgress)` — BFF OBO upload with progress
  - `createEntityRecord(entityName, entityData)` — wraps Xrm.WebApi.createRecord
  - `createDocumentRecords(parentEntityName, parentEntityId, navigationProperty, files)` — creates sprk_document records
  - `requestAiPreFill(files, entityType)` — BFF AI pre-fill endpoint
- Types: `IFileUploadResult`, `ISpeFileMetadata`, `IDocumentLinkResult`, `IAiPreFillResponse`, `IUploadProgress`
- Exported via services barrel (`services/index.ts`)

### Task 10: Integrate EntityCreationService into MatterService
**Status**: ✅ Complete
**Files**:
- `src/solutions/LegalWorkspace/src/components/CreateMatter/matterService.ts` (modify)

**Details**:
- `MatterService` creates `EntityCreationService` in constructor
- File upload delegates to `_entityService.uploadFilesToSpe()` + `_entityService.createDocumentRecords()`
- Entity params: `parentEntityName='sprk_matters'`, `navigationProperty='sprk_matter'`
- Graceful degradation when no `containerId` configured (warnings, not errors)
- Matter-specific logic preserved: `buildMatterEntity()`, follow-on actions, search helpers

---

## Phase 5: Playbook Configuration

### Task 11: Create Entity Pre-Fill extraction Skill in Dataverse
**Status**: Pending (Dataverse configuration — no code files)

**Details**:
- Create `sprk_analysisskill` record: "Matter Entity Extraction"
- Prompt fragment template:
  ```
  Extract the following fields from the provided document text:
  - matter_type: The type/category of legal matter (e.g., "Litigation", "Licensing", "Corporate")
  - practice_area: The legal practice area (e.g., "Intellectual Property", "Employment")
  - matter_name: A descriptive name for the matter
  - summary: A 2-3 sentence summary of the matter context

  Return as JSON: { "matterTypeName": "...", "practiceAreaName": "...", "matterName": "...", "summary": "..." }
  ```
- Template supports `{entity_type}` variable substitution for reuse with projects
- Output format: deterministic JSON matching `PreFillResponse` shape

### Task 12: Configure pre-fill node in Document Profile playbook
**Status**: Pending (Dataverse configuration — no code files)

**Details**:
- Playbook ID: `18cf3cc8-02ec-f011-8406-7c1e520aa4df`
- Add `sprk_playbooknode` for entity pre-fill extraction:
  - ActionId → AI Analysis action
  - ToolId → GenericAnalysisHandler (configuration-driven, no custom handler needed)
  - SkillIds → [Matter Entity Extraction skill from Task 11]
  - OutputVariable: `"prefill_data"`
  - DependsOn: [summary node, classification node] (if they exist)
- Node data flow: upstream "summary" + "class" outputs → pre-fill node prompt context
- Test: `POST /api/ai/playbooks/18cf3cc8-02ec-f011-8406-7c1e520aa4df/execute`

---

## Phase 6: Build Verification

### Task 13: Build and verify BFF API compiles
**Status**: ✅ Complete
**Files**: None (verification only)

**Details**:
- `dotnet build src/server/api/Sprk.Bff.Api/` — 0 errors, 0 warnings
- DI registration count: 8 (unchanged, ≤15 per ADR-010)

### Task 14: Build and verify front-end compiles
**Status**: ✅ Complete
**Files**: None (verification only)

**Details**:
- `npm run build` in `src/solutions/LegalWorkspace/` — 0 errors
- 2152 modules, 815 KB output (single-file HTML)
- All imports resolve correctly (EntityCreationService, bffConfig, bffAuthProvider)

---

## Summary

| Phase | Tasks | Description | Status |
|-------|-------|-------------|--------|
| 1 | Tasks 1-3 | BFF: Refactor pre-fill from hardcoded AI → playbook system | ✅ Complete |
| 2 | Tasks 4-5 | BFF: Endpoint + DI updates | ✅ Complete |
| 3 | Tasks 6-8 | Front-end: File upload + auth + BFF URL | ✅ Complete |
| 4 | Tasks 9-10 | Front-end: Generic EntityCreationService | ✅ Complete |
| 5 | Tasks 11-12 | Playbook: Dataverse configuration (Skills, Nodes) | ⏳ Pending |
| 6 | Tasks 13-14 | Build verification | ✅ Complete |

**Completion**: 12 of 14 tasks complete. Tasks 11-12 require Dataverse environment access (no code files).

**What was REMOVED from original scope**:
- ~~`POST /api/workspace/matters/files`~~ — Use existing SPE upload endpoint
- ~~`POST /api/workspace/matters/draft-summary`~~ — Front-end handles via Xrm.WebApi
- ~~`MatterFileService.cs`~~ — Not needed
- ~~`MatterSummaryService.cs`~~ — Not needed
- ~~Request/Response DTOs for removed endpoints~~ — Not needed

---

## Files Created/Modified

### New Files
| File | Purpose |
|------|---------|
| `src/solutions/LegalWorkspace/src/config/bffConfig.ts` | BFF base URL discovery |
| `src/solutions/LegalWorkspace/src/services/bffAuthProvider.ts` | Bearer token auth for BFF calls |
| `src/solutions/LegalWorkspace/src/services/EntityCreationService.ts` | Generic entity creation service |

### Modified Files
| File | Change |
|------|--------|
| `src/server/api/Sprk.Bff.Api/Services/Workspace/MatterPreFillService.cs` | Playbook orchestration refactor |
| `src/server/api/Sprk.Bff.Api/Services/Workspace/WorkspaceAiService.cs` | Real playbook execution |
| `src/server/api/Sprk.Bff.Api/Api/Workspace/Models/PreFillResponse.cs` | Field alignment |
| `src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceAiEndpoints.cs` | Pass HttpContext |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/WorkspaceModule.cs` | DI comment updates |
| `src/solutions/LegalWorkspace/src/components/CreateMatter/matterService.ts` | EntityCreationService integration |
| `src/solutions/LegalWorkspace/src/services/index.ts` | Barrel export updates |
