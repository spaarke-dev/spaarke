# AI Document Intelligence - Code Inventory

> **Purpose**: Track all existing code files to prevent recreation
> **Last Updated**: December 29, 2025
> **Project**: AI Document Intelligence R3
> **Lineage**: R1 (created) → R2 (moved) → R3 (current)

---

## Summary

| Category | Files | Status |
|----------|-------|--------|
| BFF API Endpoints | 3 | Complete (R3: +1) |
| BFF Services | 25 | Complete (R3: +12) |
| BFF Models | 7 | Complete |
| BFF Filters | 1 | Complete |
| BFF Configuration | 3 | Complete (R3: +1) |
| Unit Tests | 9 | Complete (R3: +4) |
| Integration Tests | 2 | Complete (R3: +2) |
| E2E Test Scripts | 2 | Complete (R3: +2) |
| Documentation | 3 | Complete (R3: +3) |
| PCF Controls | 2 projects | Built, not deployed |
| Infrastructure | 1+ | Partial |

---

## 1. BFF API Endpoints

### src/server/api/Sprk.Bff.Api/Api/Ai/

| File | Lines | Endpoints | Status |
|------|-------|-----------|--------|
| [AnalysisEndpoints.cs](../../src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs) | ~424 | 5 endpoints | Complete |
| [RagEndpoints.cs](../../src/server/api/Sprk.Bff.Api/Api/Ai/RagEndpoints.cs) | ~300 | 6 endpoints | Complete (R3) |

**Analysis Endpoints:**
- `POST /api/ai/analysis/execute` - Execute analysis with SSE streaming
- `POST /api/ai/analysis/{id}/continue` - Continue analysis via chat
- `POST /api/ai/analysis/{id}/save` - Save working document
- `POST /api/ai/analysis/{id}/export` - Export analysis
- `GET /api/ai/analysis/{id}` - Get analysis with history

**RAG Endpoints (R3):**
- `POST /api/ai/rag/search` - Hybrid search (keyword + vector + semantic)
- `POST /api/ai/rag/index` - Index a document chunk
- `POST /api/ai/rag/index/batch` - Batch index multiple chunks
- `DELETE /api/ai/rag/{documentId}` - Delete a document chunk
- `DELETE /api/ai/rag/source/{sourceDocumentId}` - Delete all chunks for a source document
- `POST /api/ai/rag/embedding` - Generate embedding for text

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
| [IOpenAiClient.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/IOpenAiClient.cs) | 2KB | OpenAI client interface (completions, vision, embeddings) | Complete |
| [OpenAiClient.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/OpenAiClient.cs) | 15KB | OpenAI API wrapper with circuit breaker | Complete |
| [DocumentTypeMapper.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/DocumentTypeMapper.cs) | 2KB | Document type mapping | Complete |
| [IKnowledgeDeploymentService.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/IKnowledgeDeploymentService.cs) | 6KB | RAG deployment routing interface | Complete (R3) |
| [KnowledgeDeploymentService.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/KnowledgeDeploymentService.cs) | 12KB | RAG deployment model implementation | Complete (R3) |
| [IRagService.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/IRagService.cs) | 8KB | Hybrid RAG search interface + records | Complete (R3) |
| [RagService.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/RagService.cs) | 14KB | Hybrid search implementation (keyword + vector + semantic) | Complete (R3) |
| [IEmbeddingCache.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/IEmbeddingCache.cs) | 2KB | Embedding cache interface with content hashing | Complete (R3) |
| [EmbeddingCache.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/EmbeddingCache.cs) | 5KB | Redis-based embedding cache (SHA256 keys, 7-day TTL) | Complete (R3) |
| [IAnalysisToolHandler.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/IAnalysisToolHandler.cs) | 4KB | Tool handler interface + metadata/validation records | Complete (R3) |
| [ToolExecutionContext.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/ToolExecutionContext.cs) | 3KB | Tool execution context with document and analysis state | Complete (R3) |
| [ToolResult.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/ToolResult.cs) | 5KB | Standardized tool result with JSON data and metadata | Complete (R3) |
| [IToolHandlerRegistry.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/IToolHandlerRegistry.cs) | 2KB | Registry interface for handler discovery/resolution | Complete (R3) |
| [ToolHandlerRegistry.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/ToolHandlerRegistry.cs) | 5KB | Registry implementation with reflection-based discovery | Complete (R3) |
| [ToolFrameworkExtensions.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/ToolFrameworkExtensions.cs) | 3KB | DI extension methods for tool framework registration | Complete (R3) |

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
| [KnowledgeDocument.cs](../../src/server/api/Sprk.Bff.Api/Models/Ai/KnowledgeDocument.cs) | RAG knowledge index model | Complete (R3) |

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
| [Services/Ai/KnowledgeDeploymentServiceTests.cs](../../tests/unit/Sprk.Bff.Api.Tests/Services/Ai/KnowledgeDeploymentServiceTests.cs) | 17 tests for RAG deployment | Complete (R3) |
| [Services/Ai/RagServiceTests.cs](../../tests/unit/Sprk.Bff.Api.Tests/Services/Ai/RagServiceTests.cs) | 35 tests for hybrid RAG search + caching | Complete (R3) |
| [Services/Ai/EmbeddingCacheTests.cs](../../tests/unit/Sprk.Bff.Api.Tests/Services/Ai/EmbeddingCacheTests.cs) | 21 tests for embedding cache | Complete (R3) |
| Services/Jobs/DocumentAnalysisJobHandlerTests.cs | Job handler tests | Complete |

### tests/integration/Spe.Integration.Tests/

| File | Tests | Status |
|------|-------|--------|
| [RagSharedDeploymentTests.cs](../../tests/integration/Spe.Integration.Tests/RagSharedDeploymentTests.cs) | 12 integration tests for Shared model | Complete (R3 Task 006) |
| [RagDedicatedDeploymentTests.cs](../../tests/integration/Spe.Integration.Tests/RagDedicatedDeploymentTests.cs) | 14 integration tests for Dedicated/CustomerOwned models | Complete (R3 Task 007) |

### scripts/ (Test Scripts)

| File | Purpose | Status |
|------|---------|--------|
| [Test-RagSharedModel.ps1](../../scripts/Test-RagSharedModel.ps1) | PowerShell E2E tests for Shared model | Complete (R3 Task 006) |
| [Test-RagDedicatedModel.ps1](../../scripts/Test-RagDedicatedModel.ps1) | PowerShell E2E tests for Dedicated/CustomerOwned models | Complete (R3 Task 007) |

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

### infrastructure/ai-search/

| File | Purpose | Status |
|------|---------|--------|
| [spaarke-records-index.json](../../infrastructure/ai-search/spaarke-records-index.json) | Record matching index | Deployed |
| [spaarke-knowledge-index.json](../../infrastructure/ai-search/spaarke-knowledge-index.json) | RAG knowledge index (1536 dims) | Deployed (R3) |

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
| AI Search Index | spaarke-knowledge-index | Deployed (R3) |
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

## 11. Documentation (R3)

### docs/guides/

| File | Purpose | Status |
|------|---------|--------|
| [RAG-ARCHITECTURE.md](../../docs/guides/RAG-ARCHITECTURE.md) | Full RAG architecture documentation | Complete (R3 Task 008) |
| [RAG-CONFIGURATION.md](../../docs/guides/RAG-CONFIGURATION.md) | RAG configuration reference | Complete (R3 Task 008) |
| [RAG-TROUBLESHOOTING.md](../../docs/guides/RAG-TROUBLESHOOTING.md) | RAG troubleshooting guide | Complete (R3 Task 008) |
| AI-DEPLOYMENT-GUIDE.md | Deployment guide (updated for R3) | Complete |

### projects/ai-document-intelligence-r3/notes/

| File | Purpose | Status |
|------|---------|--------|
| task-006-test-results.md | Shared model test results | Complete |
| task-007-test-results.md | Dedicated model test results | Complete |

---

*This inventory should be updated as work progresses.*
*History: Created in R1, moved to R2 (Dec 28), moved to R3 (Dec 29), Task 005 embedding cache added (Dec 29), Task 006-008 test files and docs added (Dec 29)*
