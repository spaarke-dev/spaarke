# AI Document Intelligence - Code Inventory

> **Purpose**: Track all existing code files to prevent recreation
> **Last Updated**: December 28, 2025
> **Moved from**: R1 project (verified during R1 Phase 1A)

---

## Summary

| Category | Files | Status |
|----------|-------|--------|
| BFF API Endpoints | 2 | Complete |
| BFF Services | 12 | Complete |
| BFF Models | 6 | Complete |
| BFF Filters | 1 | Complete |
| BFF Configuration | 2 | Complete |
| Unit Tests | 5 | Complete |
| PCF Controls | 2 projects | Built, not deployed |
| Infrastructure | 1+ | Partial |

---

## 1. BFF API Endpoints

### src/server/api/Sprk.Bff.Api/Api/Ai/

| File | Lines | Endpoints | Status |
|------|-------|-----------|--------|
| [AnalysisEndpoints.cs](../../src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs) | ~424 | 5 endpoints | Complete |

**Endpoints:**
- `POST /api/ai/analysis/execute` - Execute analysis with SSE streaming
- `POST /api/ai/analysis/{id}/continue` - Continue analysis via chat
- `POST /api/ai/analysis/{id}/save` - Save working document
- `POST /api/ai/analysis/{id}/export` - Export analysis
- `GET /api/ai/analysis/{id}` - Get analysis with history

**Also exists (Document Intelligence - separate feature):**
| File | Endpoints | Status |
|------|-----------|--------|
| DocumentIntelligenceEndpoints.cs | /analyze, /enqueue, /enqueue-batch | Production |
| RecordMatchEndpoints.cs | /match-records, /associate-record | Production |

---

## 2. BFF Services

### src/server/api/Sprk.Bff.Api/Services/Ai/

| File | Size | Purpose | Status |
|------|------|---------|--------|
| [AnalysisOrchestrationService.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs) | 18KB | Main orchestrator for analysis | Complete |
| [IAnalysisOrchestrationService.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/IAnalysisOrchestrationService.cs) | 4KB | Interface | Complete |
| [AnalysisContextBuilder.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisContextBuilder.cs) | 5KB | Build analysis context | Complete |
| [IAnalysisContextBuilder.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/IAnalysisContextBuilder.cs) | 2KB | Interface | Complete |
| [ScopeResolverService.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs) | 4KB | Resolve scopes | Complete |
| [IScopeResolverService.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs) | 4KB | Interface | Complete |
| [WorkingDocumentService.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/WorkingDocumentService.cs) | 4KB | Working document storage | Complete |
| [IWorkingDocumentService.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/IWorkingDocumentService.cs) | 3KB | Interface | Complete |
| [DocumentIntelligenceService.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/DocumentIntelligenceService.cs) | 28KB | Document analysis (prod feature) | Complete |
| [TextExtractorService.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/TextExtractorService.cs) | 26KB | Text extraction | Complete |
| [OpenAiClient.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/OpenAiClient.cs) | 15KB | OpenAI API wrapper | Complete |
| [DocumentTypeMapper.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/DocumentTypeMapper.cs) | 2KB | Document type mapping | Complete |

---

## 3. BFF Models

### src/server/api/Sprk.Bff.Api/Models/Ai/

| File | Purpose | Status |
|------|---------|--------|
| [AnalysisExecuteRequest.cs](../../src/server/api/Sprk.Bff.Api/Models/Ai/AnalysisExecuteRequest.cs) | Execute request model | Complete |
| [AnalysisContinueRequest.cs](../../src/server/api/Sprk.Bff.Api/Models/Ai/AnalysisContinueRequest.cs) | Continue request model | Complete |
| [AnalysisSaveRequest.cs](../../src/server/api/Sprk.Bff.Api/Models/Ai/AnalysisSaveRequest.cs) | Save request model | Complete |
| [AnalysisExportRequest.cs](../../src/server/api/Sprk.Bff.Api/Models/Ai/AnalysisExportRequest.cs) | Export request model | Complete |
| [AnalysisResult.cs](../../src/server/api/Sprk.Bff.Api/Models/Ai/AnalysisResult.cs) | Analysis result model | Complete |
| [AnalysisChunk.cs](../../src/server/api/Sprk.Bff.Api/Models/Ai/AnalysisChunk.cs) | SSE chunk model | Complete |

---

## 4. BFF Filters

### src/server/api/Sprk.Bff.Api/Api/Filters/

| File | Purpose | Status |
|------|---------|--------|
| [AnalysisAuthorizationFilter.cs](../../src/server/api/Sprk.Bff.Api/Api/Filters/AnalysisAuthorizationFilter.cs) | Authorization for analysis endpoints | Complete |

---

## 5. BFF Configuration

### src/server/api/Sprk.Bff.Api/Configuration/

| File | Purpose | Status |
|------|---------|--------|
| [AnalysisOptions.cs](../../src/server/api/Sprk.Bff.Api/Configuration/AnalysisOptions.cs) | Analysis configuration | Complete |
| DocumentIntelligenceOptions.cs | Document Intelligence config | Complete |

---

## 6. Unit Tests

### tests/unit/Sprk.Bff.Api.Tests/

| File | Tests | Status |
|------|-------|--------|
| [Api/Ai/AnalysisEndpointsTests.cs](../../tests/unit/Sprk.Bff.Api.Tests/Api/Ai/AnalysisEndpointsTests.cs) | Endpoint tests | Complete |
| [Filters/AnalysisAuthorizationFilterTests.cs](../../tests/unit/Sprk.Bff.Api.Tests/Filters/AnalysisAuthorizationFilterTests.cs) | Filter tests | Complete |
| [Services/Ai/AnalysisOrchestrationServiceTests.cs](../../tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisOrchestrationServiceTests.cs) | Service tests | Complete |
| [Services/Ai/AnalysisContextBuilderTests.cs](../../tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisContextBuilderTests.cs) | Context builder tests | Complete |
| Services/Jobs/DocumentAnalysisJobHandlerTests.cs | Job handler tests | Complete |

---

## 7. PCF Controls

### src/client/pcf/AnalysisBuilder/

| File | Purpose | Status |
|------|---------|--------|
| AnalysisBuilder.pcfproj | Project file | Complete |
| control/ControlManifest.Input.xml | PCF manifest | Complete |
| control/index.ts | Entry point | Complete |
| control/components/AnalysisBuilderApp.tsx | Main component | Complete |
| control/components/ScopeTabs.tsx | Tab navigation | Complete |
| control/components/ScopeList.tsx | Scope selection | Complete |
| control/components/PlaybookSelector.tsx | Playbook dropdown | Complete |
| control/components/FooterActions.tsx | Action buttons | Complete |
| control/components/index.ts | Component exports | Complete |
| control/types/index.ts | TypeScript types | Complete |
| control/utils/environmentVariables.ts | Env var access | Complete |
| control/utils/logger.ts | Logging utility | Complete |
| control/css/AnalysisBuilder.css | Styles | Complete |
| package.json | NPM config | Complete |
| tsconfig.json | TypeScript config | Complete |

### src/client/pcf/AnalysisWorkspace/

| File | Purpose | Status |
|------|---------|--------|
| AnalysisWorkspace.pcfproj | Project file | Complete |
| control/ControlManifest.Input.xml | PCF manifest | Complete |
| control/index.ts | Entry point | Complete |
| control/components/AnalysisWorkspaceApp.tsx | Main component | Complete |
| control/components/MonacoEditor.tsx | Code editor | Complete |
| control/components/SourceDocumentViewer.tsx | Source preview | Complete |
| control/components/RichTextEditor/RichTextEditor.tsx | Rich text | Complete |
| control/components/RichTextEditor/ToolbarPlugin.tsx | Editor toolbar | Complete |
| control/components/RichTextEditor/index.ts | Exports | Complete |
| control/hooks/useSseStream.ts | SSE streaming hook | Complete |
| control/hooks/index.ts | Hook exports | Complete |
| control/types/index.ts | TypeScript types | Complete |
| control/utils/environmentVariables.ts | Env var access | Complete |
| control/utils/logger.ts | Logging utility | Complete |
| control/css/AnalysisWorkspace.css | Styles | Complete |
| package.json | NPM config | Complete |
| tsconfig.json | TypeScript config | Complete |
| pcfconfig.json | PCF config | Complete |

---

## 8. Infrastructure

### infrastructure/bicep/

| File | Purpose | Status |
|------|---------|--------|
| [ai-foundry.bicepparam](../../infrastructure/bicep/ai-foundry.bicepparam) | AI Foundry parameters | Exists |

### infrastructure/ai-foundry/ (if exists)

| Directory | Purpose | Status |
|-----------|---------|--------|
| prompt-flows/analysis-execute/ | Analysis prompt flow | Template created |
| prompt-flows/analysis-continue/ | Chat continuation flow | Template created |
| evaluation/ | Evaluation config | Created |
| connections/ | AI service connections | Created |

---

## 9. Azure Resources (Deployed)

| Resource | Name | Status |
|----------|------|--------|
| AI Foundry Hub | sprkspaarkedev-aif-hub | Deployed |
| AI Foundry Project | sprkspaarkedev-aif-proj | Deployed |
| Azure OpenAI | spaarke-openai-dev | Deployed |
| AI Search | spaarke-search-dev | Deployed |
| Document Intelligence | spaarke-docintel-dev | Deployed |

---

## 10. Dataverse Entities (Verified in R1)

These entities were verified during R1 Phase 1A:

| Entity | Logical Name | Used By | Verified |
|--------|--------------|---------|----------|
| Analysis | sprk_analysis | AnalysisOrchestrationService | ✅ Yes |
| Analysis Action | sprk_analysisaction | ScopeResolverService | ✅ Yes |
| Analysis Skill | sprk_analysisskill | ScopeResolverService | ✅ Yes |
| Analysis Knowledge | sprk_analysisknowledge | ScopeResolverService | ✅ Yes |
| AI Knowledge Deployment | sprk_aiknowledgedeployment | ScopeResolverService | ✅ Yes |
| Analysis Tool | sprk_analysistool | ScopeResolverService | ✅ Yes |
| Analysis Playbook | sprk_analysisplaybook | AnalysisBuilder | ✅ Yes |
| Analysis Working Version | sprk_analysisworkingversion | WorkingDocumentService | ✅ Yes |
| Analysis Email Metadata | sprk_analysisemailmetadata | Export feature | ✅ Yes |
| Analysis Chat Message | sprk_analysischatmessage | AnalysisOrchestrationService | ✅ Yes |

**Note**: Entity `sprk_knowledgedeployment` documented as `sprk_aiknowledgedeployment` in actual Dataverse.

---

## Files NOT to Recreate

**DO NOT create new files for:**
- Any file listed above with "Complete" status
- Endpoints, services, or models in the AI namespace
- PCF control source files

**MAY need to create:**
- ~~Dataverse entity definitions~~ (VERIFIED - all exist)
- Integration test files
- Deployment scripts
- Solution project files

---

*This inventory should be updated as work progresses.*
*Moved from R1 to R2: December 28, 2025*
