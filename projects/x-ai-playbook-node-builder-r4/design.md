# AI Playbook Builder R2 — Design Document

> **Project**: ai-playbook-builder-r2
> **Date**: February 27, 2026
> **Status**: Draft — Pending Review
> **Branch**: `work/ai-playbook-builder-r2`
> **Worktree**: `spaarke-wt-ai-spaarke-platform-enhancements-r2`
> **Consolidated From**: ai-playbook-builder-r2 + ai-scope-resolution-enhancements

---

## 1. Executive Summary

Complete the node-based Playbook execution pipeline end-to-end, from visual canvas design through AI orchestration to formatted output in the Analysis Workspace. The infrastructure is ~80% built but has critical gaps that prevent activation. Today, playbook execution falls through to a legacy sequential path that streams raw JSON to the user, and scope resolution uses hardcoded stub dictionaries with fake GUIDs instead of querying Dataverse.

This project consolidates two previously separate efforts:
- **ai-scope-resolution-enhancements** — Fix scope resolution across all types (Tools, Skills, Knowledge, Actions) and eliminate stub dictionary anti-patterns
- **ai-playbook-builder-r2** — Wire canvas-to-node sync, node-based execution, output formatting, and workspace UX

These are tightly coupled: node-based execution depends on scope resolution working correctly. Merging them prevents misalignment and captures implementation efficiencies (e.g., `ScopeResolverService` changes serve both `ResolveNodeScopesAsync` and individual scope getters).

### Three Focus Areas

| Focus Area | Scope | Outcome |
|-----------|-------|---------|
| **1. Scope Resolution Foundation** | Server-side: replace stub dictionaries with Dataverse queries for all scope types (Tools, Skills, Knowledge, Actions), fix job handler registration, handler discovery API | All scopes loaded dynamically from Dataverse; zero stub code remains; handler metadata API for frontend validation |
| **2. Playbook Node AI Orchestration** | Server-side: canvas-to-node sync, ResolveNodeScopesAsync, document loading, execution wiring, streaming, output persistence | Playbooks with nodes execute via `PlaybookOrchestrationService` with parallel batching, formatted markdown output via Deliver Output node |
| **3. Analysis Workspace Application** | Client-side: statuscode-based auto-execute, completion toast, Run Analysis button, source pane toggle, SprkChat auto-loading side pane | Complete workspace UX with context-aware SprkChat integration |

### Architecture Vision: Three-Tier Scope Resolution

```
┌────────────────────────────────────────────────────────────────┐
│  Tier 1: Configuration (Dataverse - Source of Truth)           │
│  - sprk_analysistool, sprk_promptfragment, sprk_systemprompt,  │
│    sprk_content                                                 │
│  - Must work without code deployment (new records auto-work)   │
│  - HandlerClass NULL → Defaults to generic handler              │
└────────────────────────────────────────────────────────────────┘
                              ↓
┌────────────────────────────────────────────────────────────────┐
│  Tier 2: Generic Execution (Handles 95% of Cases)              │
│  - GenericAnalysisHandler (Tools)                              │
│  - Reads configuration JSON, executes via AI prompts            │
│  - No arbitrary code execution (security safe)                  │
└────────────────────────────────────────────────────────────────┘
                              ↓
┌────────────────────────────────────────────────────────────────┐
│  Tier 3: Custom Handlers (Complex Scenarios Only)              │
│  - EntityExtractorHandler, SummaryHandler, etc.                │
│  - Registered in DI at startup                                  │
│  - Discoverable via IToolHandlerRegistry                        │
│  - Optional - specified in HandlerClass field                   │
└────────────────────────────────────────────────────────────────┘
```

---

## 2. Requirements & Objectives

### Functional Requirements

#### Focus Area 1: Scope Resolution Foundation

| ID | Requirement | Priority |
|----|------------|----------|
| FR-01 | Fix `AppOnlyDocumentAnalysis` job handler registration — no more "NoHandler" dead-letter errors | P0 |
| FR-02 | `GetToolAsync` queries Dataverse `sprk_analysistools` with `$expand=sprk_ToolTypeId` (80% done, needs deploy + test) | P0 |
| FR-03 | `GetSkillAsync` queries Dataverse `sprk_promptfragments` with `$expand=sprk_SkillTypeId` — replace stub dictionary | P0 |
| FR-04 | `GetKnowledgeAsync` queries Dataverse `sprk_contents` with `$expand=sprk_KnowledgeTypeId` — replace stub dictionary | P0 |
| FR-05 | `GetActionAsync` queries Dataverse `sprk_systemprompts` with `$expand=sprk_ActionTypeId` — replace stub dictionary | P0 |
| FR-06 | All stub dictionaries (lines 25-129 of ScopeResolverService.cs) removed after Dataverse queries proven | P0 |
| FR-07 | Handler resolution: check `sprk_handlerclass` first → fall back to `GenericAnalysisHandler` → type-based lookup | P0 |
| FR-08 | `GET /api/ai/handlers` endpoint returns metadata for all registered handlers with ConfigurationSchema | P1 |
| FR-09 | All 9 tool handlers include JSON Schema Draft 07 configuration schema in metadata | P1 |

#### Focus Area 2: Playbook Node AI Orchestration

| ID | Requirement | Priority |
|----|------------|----------|
| FR-10 | Visual Playbook Builder is the single source of truth for node design — saving the playbook form syncs canvas nodes to executable Dataverse records | P0 |
| FR-11 | Node-based playbooks execute via `PlaybookOrchestrationService` with parallel batching (max 3 concurrent), not the legacy sequential path | P0 |
| FR-12 | Deliver Output node renders formatted markdown (or html/json/text) via Handlebars templates, replacing raw JSON streaming | P0 |
| FR-13 | Per-node scope resolution via `ResolveNodeScopesAsync` loads skills, knowledge, and tool from N:N relationship tables | P0 |
| FR-14 | Document text is loaded into `PlaybookRunContext.Document` before node execution | P0 |
| FR-15 | Per-token SSE streaming from AI nodes to Analysis Workspace client | P1 |
| FR-16 | Analysis statuscode transitions: Draft(1) → In Progress → Completed(2); auto-execute only fires for Draft with empty content | P0 |

#### Focus Area 3: Analysis Workspace Application

| ID | Requirement | Priority |
|----|------------|----------|
| FR-17 | Analysis Workspace shows completion toast notification | P1 |
| FR-18 | "Run Analysis" toolbar button for manual re-execution | P1 |
| FR-19 | Source pane toggle button (show/hide document viewer) | P2 |
| FR-20 | SprkChat side pane auto-loads with Analysis Workspace via `Xrm.App.sidePanes.createPane()` | P1 |

### Non-Functional Requirements

| ID | Requirement |
|----|------------|
| NFR-01 | Scope resolution latency < 200ms (p95) |
| NFR-02 | `GET /api/ai/handlers` response < 100ms (cached) |
| NFR-03 | Document Profile playbook (4 nodes) executes in < 30 seconds |
| NFR-04 | Per-token streaming latency < 200ms from OpenAI to client render |
| NFR-05 | Canvas-to-node sync completes in < 2 seconds for playbooks with ≤ 20 nodes |
| NFR-06 | All existing legacy (non-node) playbook execution continues to work unchanged |
| NFR-07 | Analysis success rate > 98% |
| NFR-08 | Dead-letter queue errors < 1/day (down from ~5-10/hour) |
| NFR-09 | Zero code deployment required for new scope configurations in Dataverse |
| NFR-10 | Helpful error messages when handler not found (lists available handlers) |

---

## 3. Current State — What Happens Today

### Problem: Stub Dictionary Anti-Pattern

**Root Cause:**
- `ScopeResolverService.cs` (lines 25-129) contains hardcoded stub dictionaries with fake GUIDs
- `PlaybookService.cs` loads **real** GUIDs from Dataverse N:N relationships
- **Mismatch** → ScopeResolverService returns null → Tools/Skills/Knowledge/Actions not found → Analysis fails

**Evidence:**
```
Dead-letter queue error:
"Playbook 'Document Profile' has no tools configured"

Root cause trace:
1. PlaybookService.LoadToolIdsAsync() returns Guid("abc-123-real-guid-from-dataverse")
2. ScopeResolverService.GetToolAsync(Guid("abc-123-real-guid-from-dataverse"))
3. Checks _stubTools dictionary with fake GUIDs → Not found → Returns null
4. Playbook has empty Tools collection → Analysis fails
```

### User Journey (Legacy Path)

```
1. User clicks "+New Analysis" on a document form
   → sprk_analysis_commands.js opens Analysis Builder dialog

2. Analysis Builder shows playbook cards (Document Profile, etc.)
   → User selects playbook, reviews scope tabs, clicks Execute

3. Analysis Builder creates sprk_analysis record in Dataverse
   → statuscode=1 (Draft), empty sprk_workingdocument
   → Associates N:N scopes (skills, knowledge, tools)
   → Navigates to sprk_analysis entity record form

4. Analysis form loads with embedded AnalysisWorkspace web resource
   → useAnalysisExecution checks auto-execute conditions
   → Draft + empty + has playbookId + created within 60 seconds → auto-execute

5. Client sends POST /api/ai/analysis/execute to BFF
   → BFF calls ExecutePlaybookAsync()
   → GetNodesAsync(playbookId) returns EMPTY ARRAY (no node records exist)
   → Falls through to legacy sequential tool execution

6. Legacy path: for each tool in playbook N:N:
   → Resolves handler via ToolHandlerRegistry
   → Handler calls Azure OpenAI with JSON prompt instructions
   → AI returns structured JSON (e.g., {"summary": "...", "confidence": 0.85})
   → Raw JSON tokens streamed via SSE to client

7. Client renders raw JSON tokens in Lexical editor
   → User sees: { "summary": { "Executive Summary": "..." } }
   → NOT formatted markdown — this is the core problem
```

### Why It Breaks

1. **No `sprk_playbooknode` records exist** — Canvas JSON stores node designs visually but never creates Dataverse records. `GetNodesAsync()` always returns `[]`, forcing the legacy path.

2. **Stub dictionaries use fake GUIDs** — `ScopeResolverService` compares real Dataverse GUIDs against hardcoded fake GUIDs. Nothing matches. Tools/Skills/Knowledge resolve to null.

3. **Legacy path streams raw tool output** — Each tool handler instructs AI to "Return ONLY valid JSON". The JSON tokens are streamed directly to the client, which expects markdown.

4. **No Deliver Output step** — The legacy path has no template rendering. Raw tool results go straight to `sprk_workingdocument`.

5. **Auto-execute uses 60-second age check** — The `createdOn` age heuristic is unreliable. Should use `statuscode` instead.

6. **Job handler not registered** — `AppOnlyDocumentAnalysis` job type goes to dead-letter queue because the handler isn't in DI.

### Affected Code Sections

| Service | Method | Issue |
|---------|--------|-------|
| ScopeResolverService | GetToolAsync | 80% done — queries Dataverse but needs deploy + test |
| ScopeResolverService | GetSkillAsync | Uses stub dictionary |
| ScopeResolverService | GetKnowledgeAsync | Uses stub dictionary |
| ScopeResolverService | GetActionAsync | Uses stub dictionary |
| ScopeResolverService | ResolveNodeScopesAsync | Stub — returns empty ResolvedScopes |
| Program.cs | DI registration | AiAnalysisNodeExecutor NOT registered |
| Program.cs | DI registration | AppOnlyDocumentAnalysis job handler NOT registered |
| AnalysisOrchestrationService | ExecutePlaybookAsync | Doesn't delegate to PlaybookOrchestrationService when nodes exist |
| AiAnalysisNodeExecutor | ExecuteAsync | Uses blocking call, not streaming |
| PlaybookRunContext | Document property | Never populated |

### Already Completed (Prior Work)

- Enhanced handler resolution with fallback in `AppOnlyAnalysisService`, `AnalysisOrchestrationService`, `AiAnalysisNodeExecutor`
- HandlerClass → GenericAnalysisHandler fallback chain implemented
- Available handlers listed in error messages
- `GetToolAsync` Dataverse query code written (needs deploy)

---

## 4. Future State — How It Should Work

### User Journey (Node-Based Path)

```
1-3. Same as today (Analysis Builder creates record, navigates to form)

4. Analysis form loads with embedded AnalysisWorkspace web resource
   → useAnalysisExecution checks: statusCode === 1 (Draft) AND content empty
   → Auto-executes (no age check needed)

5. Client sends POST /api/ai/analysis/execute to BFF
   → BFF calls ExecutePlaybookAsync()
   → GetNodesAsync(playbookId) returns 4 NODE RECORDS ← synced from canvas
   → Delegates to PlaybookOrchestrationService.ExecuteAsync()

6. PlaybookOrchestrationService:
   a. Loads document text via SpeFileStore → sets PlaybookRunContext.Document
   b. Sets analysis statuscode to "In Progress"
   c. Builds ExecutionGraph from nodes → topological sort → parallel batches:
      - Batch 1: [Document Classifier, Entity Extractor, Summary Generator] (parallel)
      - Batch 2: [Deliver Output] (depends on all three)
   d. Executes Batch 1 (parallel via SemaphoreSlim(3)):
      - For each AI node:
        - ScopeResolverService.ResolveNodeScopesAsync(nodeId) → tool, skills, knowledge
        - Each scope loaded from Dataverse (not stubs) via GetToolAsync, GetSkillAsync, etc.
        - AiAnalysisNodeExecutor → tool handler → Azure OpenAI
        - Handler resolution: HandlerClass → GenericAnalysisHandler fallback
        - Handler returns ToolResult { Data (JsonElement), Summary (text) }
        - Per-token streaming via NodeProgress → SSE to client
        - Result stored in PlaybookRunContext.NodeOutputs[outputVariable]
   e. Executes Batch 2 (Deliver Output):
      - DeliverOutputNodeExecutor → BuildTemplateContext()
      - Renders Handlebars template:
        ## Document Classification
        **Type:** {{classifier.output.category}}
        **Confidence:** {{classifier.output.confidence}}
        ## Executive Summary
        {{summarizer.text}}
        ## Key Entities
        {{#each extractor.output.entities}}
        - **{{this.value}}** ({{this.type}})
        {{/each}}
      - Returns clean formatted markdown
   f. Final markdown written to sprk_workingdocument
   g. Sets analysis statuscode to Completed(2)

7. Client receives formatted markdown via SSE
   → markdownToHtml() → Lexical editor renders clean document
   → Completion toast notification
   → Reopening same record: statusCode !== 1, so no auto-execute
```

### Key Architecture Changes

| Aspect | Today (Legacy) | Future (Node-Based) |
|--------|---------------|---------------------|
| Scope resolution | Stub dictionaries with fake GUIDs | Dataverse Web API queries (real data) |
| Node source | None (empty array) | Canvas JSON → auto-synced to sprk_playbooknode records |
| Execution engine | Sequential tool loop in AnalysisOrchestrationService | Parallel batches via PlaybookOrchestrationService + ExecutionGraph |
| Scope resolution level | ResolvePlaybookScopesAsync (playbook-level) | ResolveNodeScopesAsync (per-node N:N tables) |
| Handler resolution | Type-based lookup only | HandlerClass → GenericAnalysisHandler → type-based (3-tier) |
| Output formatting | Raw JSON tokens streamed directly | Deliver Output node with Handlebars template |
| Auto-execute trigger | createdOn within 60 seconds | statusCode === 1 (Draft) |
| Working document content | Raw JSON strings | Clean formatted markdown |
| Status tracking | No transitions | Draft → In Progress → Completed |
| SprkChat | Not launched | Auto-loads as persistent side pane |
| Job processing | Dead-letter errors | Registered handler, successful processing |

---

## 5. Existing Components Inventory

### 5.1 Server-Side Components

#### Orchestration Services

| Component | File | Lines | Status | Changes Needed |
|-----------|------|-------|--------|---------------|
| `PlaybookOrchestrationService` | `Services/Ai/PlaybookOrchestrationService.cs` | 841 | **Complete** — mode detection, batch execution, SemaphoreSlim(3), SSE streaming, retry logic | Wire document loading; add output persistence after Deliver Output |
| `IPlaybookOrchestrationService` | `Services/Ai/IPlaybookOrchestrationService.cs` | 392 | **Complete** — interface + PlaybookStreamEvent, PlaybookRunMetrics, PlaybookRunState | None |
| `AnalysisOrchestrationService` | `Services/Ai/AnalysisOrchestrationService.cs` | 2,025 | **Complete** — legacy tool execution, playbook detection | Add delegation to PlaybookOrchestrationService when nodes exist; add statuscode transitions |
| `IAnalysisOrchestrationService` | `Services/Ai/IAnalysisOrchestrationService.cs` | 151 | **Complete** — interface + PlaybookExecuteRequest | None |

#### Node Execution

| Component | File | Lines | Status | Changes Needed |
|-----------|------|-------|--------|---------------|
| `ExecutionGraph` | `Services/Ai/ExecutionGraph.cs` | 267 | **Complete** — Kahn's algorithm topological sort, batch generation, cycle detection | None |
| `AiAnalysisNodeExecutor` | `Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` | 302 | **Complete** — bridges NodeExecutionContext → ToolExecutionContext | Add streaming support via IStreamingAnalysisToolHandler; **register in DI** |
| `DeliverOutputNodeExecutor` | `Services/Ai/Nodes/DeliverOutputNodeExecutor.cs` | 317 | **Complete** — Handlebars template rendering, json/text/html/markdown delivery types, BuildTemplateContext | None |
| `ConditionNodeExecutor` | `Services/Ai/Nodes/ConditionNodeExecutor.cs` | — | **Complete** — condition evaluation | None |
| `CreateTaskNodeExecutor` | `Services/Ai/Nodes/CreateTaskNodeExecutor.cs` | — | **Complete** — Dataverse task creation | None |
| `SendEmailNodeExecutor` | `Services/Ai/Nodes/SendEmailNodeExecutor.cs` | — | **Complete** — email via Graph | None |
| `UpdateRecordNodeExecutor` | `Services/Ai/Nodes/UpdateRecordNodeExecutor.cs` | — | **Complete** — Dataverse entity update | None |
| `NodeExecutorRegistry` | `Services/Ai/Nodes/NodeExecutorRegistry.cs` | 113 | **Complete** — DI discovery by ActionType, ConcurrentDictionary | None |
| `INodeExecutor` | `Services/Ai/Nodes/INodeExecutor.cs` | 98 | **Complete** — interface + ActionType enum (15 types) | None |

#### Context & Models

| Component | File | Lines | Status | Changes Needed |
|-----------|------|-------|--------|---------------|
| `PlaybookRunContext` | `Services/Ai/PlaybookRunContext.cs` | 295 | **Complete** — run state, ConcurrentDictionary for node outputs, CreateNodeContext factory | Populate `Document` property before execution |
| `NodeExecutionContext` | `Services/Ai/NodeExecutionContext.cs` | 138 | **Complete** — all node context fields, MaxTokens=4096, Temperature=0.3 | None |
| `NodeOutput` | `Services/Ai/NodeOutput.cs` | 287 | **Complete** — Ok/Error factories, NodeExecutionMetrics, NodeErrorCodes | None |
| `PlaybookNodeDto` | `Models/Ai/PlaybookNodeDto.cs` | 327 | **Complete** — CreateNodeRequest, UpdateNodeRequest, NodeScopesRequest, NodeValidationResult | None |

#### Node & Scope Services

| Component | File | Lines | Status | Changes Needed |
|-----------|------|-------|--------|---------------|
| `NodeService` | `Services/Ai/NodeService.cs` | 200+ | **In Progress** — GetNodesAsync, CreateNodeAsync working; N:N relationships mapped | **Add SyncCanvasToNodesAsync()** — convert canvas JSON to Dataverse records |
| `INodeService` | `Services/Ai/INodeService.cs` | 99 | **Complete** — CRUD interface + UpdateNodeScopesAsync, ReorderNodesAsync | Add SyncCanvasToNodesAsync to interface |
| `ScopeResolverService` | `Services/Ai/ScopeResolverService.cs` | 200+ | **Stub-heavy** — GetToolAsync 80% done; GetSkillAsync/GetKnowledgeAsync/GetActionAsync all stubs; ResolveNodeScopesAsync returns empty | **Replace all stubs with Dataverse queries; implement ResolveNodeScopesAsync** |
| `IScopeResolverService` | `Services/Ai/IScopeResolverService.cs` | — | **Complete** | None |
| `PlaybookService` | `Services/Ai/PlaybookService.cs` | 200+ | **In Progress** — CRUD, canvas save/retrieve, N:N relationships | None (canvas sync handled by NodeService) |

#### Working Document & Templates

| Component | File | Lines | Status | Changes Needed |
|-----------|------|-------|--------|---------------|
| `WorkingDocumentService` | `Services/Ai/WorkingDocumentService.cs` | 133 | **Partial** — UpdateWorkingDocumentAsync and FinalizeAnalysisAsync work; SaveToSpeAsync is stub | None for this project |
| `TemplateEngine` | (Handlebars.NET wrapper) | — | **Complete** — Render, HasVariables, GetVariableNames | None |
| `IStreamingAnalysisToolHandler` | `Services/Ai/IStreamingAnalysisToolHandler.cs` | — | **Complete** — opt-in streaming interface with ToolStreamEvent.Token and .Completed | None |
| `GenericAnalysisHandler` | `Services/Ai/Handlers/GenericAnalysisHandler.cs` | — | **Complete** — implements IStreamingAnalysisToolHandler; BuildExecutionPrompt adds JSON instructions | None (JSON output is correct for tool results; Deliver Output handles formatting) |

#### Tool Handlers (All Need ConfigurationSchema)

| Handler | File | Status | ConfigurationSchema Needed |
|---------|------|--------|---------------------------|
| `GenericAnalysisHandler` | `Services/Ai/Handlers/GenericAnalysisHandler.cs` | **Complete** | Schema for operations (extract, classify, validate, generate, transform, analyze) |
| `EntityExtractorHandler` | `Services/Ai/Tools/EntityExtractorHandler.cs` | **Complete** | Schema for entityTypes, confidenceThreshold |
| `SummaryHandler` | `Services/Ai/Tools/SummaryHandler.cs` | **Complete** | Schema for maxLength, format |
| `ClauseAnalyzerHandler` | `Services/Ai/Tools/ClauseAnalyzerHandler.cs` | **Complete** | Schema for clauseTypes, comparisonMode |
| `DocumentClassifierHandler` | `Services/Ai/Tools/DocumentClassifierHandler.cs` | **Complete** | Schema for categories, threshold |
| `RiskDetectorHandler` | `Services/Ai/Tools/RiskDetectorHandler.cs` | **Complete** | Schema for riskCategories, severity |
| `ClauseComparisonHandler` | `Services/Ai/Tools/ClauseComparisonHandler.cs` | **Complete** | Schema for baselineDocumentId |
| `DateExtractorHandler` | `Services/Ai/Tools/DateExtractorHandler.cs` | **Complete** | Schema for dateFormats, timezone |
| `FinancialCalculatorHandler` | `Services/Ai/Tools/FinancialCalculatorHandler.cs` | **Complete** | Schema for calculationType, precision |

#### Endpoints

| Component | File | Lines | Status | Changes Needed |
|-----------|------|-------|--------|---------------|
| `AnalysisEndpoints` | `Api/Ai/AnalysisEndpoints.cs` | — | **Complete** — POST /api/ai/analysis/execute with SSE streaming | None (delegates to AnalysisOrchestrationService) |
| `PlaybookEndpoints` | `Api/Ai/PlaybookEndpoints.cs` | 699 | **Complete** — full CRUD, canvas save, sharing | **Add sync-nodes endpoint** or hook into existing save |
| `HandlerEndpoints` | `Api/Ai/HandlerEndpoints.cs` | **Exists** | **Add GET /api/ai/handlers** for handler discovery (may already have scaffolding) |

#### DI Registration (Program.cs)

**Currently Registered:**
- `ScopeResolverService` (HttpClient) → `IScopeResolverService`
- `WorkingDocumentService` (Scoped) → `IWorkingDocumentService`
- `AnalysisOrchestrationService` (Scoped) → `IAnalysisOrchestrationService`
- `PlaybookService` (HttpClient) → `IPlaybookService`
- `NodeService` (HttpClient) → `INodeService`
- `NodeExecutorRegistry` (Singleton) → `INodeExecutorRegistry`
- `PlaybookOrchestrationService` (Scoped) → `IPlaybookOrchestrationService`
- `TemplateEngine` (Singleton) → `ITemplateEngine`
- `DeliverOutputNodeExecutor` (Singleton) → `INodeExecutor`
- `ConditionNodeExecutor` (Singleton) → `INodeExecutor`
- `CreateTaskNodeExecutor` (Singleton) → `INodeExecutor`
- `SendEmailNodeExecutor` (Singleton) → `INodeExecutor`
- `UpdateRecordNodeExecutor` (Singleton) → `INodeExecutor`
- Tool handlers via `AddToolFramework()` assembly scanning

**NOT Registered (Gaps):**
- `AiAnalysisNodeExecutor` — **MISSING from DI**. NodeExecutorRegistry cannot discover it, so ActionType.AiAnalysis has no executor.
- `AppOnlyDocumentAnalysisJobHandler` — **MISSING from DI**. Service Bus processor cannot find handler, messages go to dead-letter queue.

### 5.2 Client-Side Components

#### Analysis Workspace Code Page

| Component | File | Status | Changes Needed |
|-----------|------|--------|---------------|
| Entry point | `code-pages/AnalysisWorkspace/src/index.tsx` (211 lines) | **Complete** — multi-source param resolution, theme detection | None |
| App root | `code-pages/AnalysisWorkspace/src/App.tsx` (586 lines) | **Complete** — 2-panel layout, all hook wiring | Add completion toast, Run Analysis button, source toggle, SprkChat auto-load |
| Types | `code-pages/AnalysisWorkspace/src/types/index.ts` (212 lines) | **Complete** | None |
| useAnalysisLoader | `code-pages/AnalysisWorkspace/src/hooks/useAnalysisLoader.ts` (223 lines) | **Complete** | None |
| useAnalysisExecution | `code-pages/AnalysisWorkspace/src/hooks/useAnalysisExecution.ts` (237 lines) | **Complete** | **Replace 60-second age check with statuscode-based auto-execute**; expose `triggerExecute()` for manual button |
| useAutoSave | `code-pages/AnalysisWorkspace/src/hooks/useAutoSave.ts` (202 lines) | **Complete** | None |
| useThemeDetection | `code-pages/AnalysisWorkspace/src/hooks/useThemeDetection.ts` | **Complete** | None |
| analysisApi | `code-pages/AnalysisWorkspace/src/services/analysisApi.ts` (400+ lines) | **Complete** | None (outputType hardcoded to 0 is acceptable; format comes from Deliver Output node) |
| markdownToHtml | `code-pages/AnalysisWorkspace/src/utils/markdownToHtml.ts` (117 lines) | **Complete** | None |
| EditorPanel | `code-pages/AnalysisWorkspace/src/components/EditorPanel.tsx` | **Complete** | None |
| SourceViewerPanel | `code-pages/AnalysisWorkspace/src/components/SourceViewerPanel.tsx` | **Complete** | None |
| DocumentStreamBridge | `code-pages/AnalysisWorkspace/src/components/DocumentStreamBridge.tsx` | **Complete** | None |
| DiffReviewPanel | `code-pages/AnalysisWorkspace/src/components/DiffReviewPanel.tsx` | **Complete** | None |
| Launcher | `code-pages/AnalysisWorkspace/launcher/sprk_AnalysisWorkspaceLauncher.js` (606 lines) | **Complete** | None |
| webpack.config | `code-pages/AnalysisWorkspace/webpack.config.js` (99 lines) | **Complete** | None |

#### Playbook Builder PCF

| Component | File | Status | Changes Needed |
|-----------|------|--------|---------------|
| PlaybookBuilderHost | `pcf/PlaybookBuilderHost/control/PlaybookBuilderHost.tsx` | **Complete** — auto-sync canvas to bound field (500ms debounce) | None (canvas-to-node sync happens server-side on form save) |
| canvasStore | `pcf/PlaybookBuilderHost/control/stores/canvasStore.ts` | **Complete** — PlaybookNodeData with skillIds[], knowledgeIds[], toolId | None |
| aiAssistantStore | `pcf/PlaybookBuilderHost/control/stores/aiAssistantStore.ts` | **Complete** | None |
| NodePropertiesForm | `pcf/PlaybookBuilderHost/control/components/Properties/NodePropertiesForm.tsx` | **Complete** — edits label, outputVariable, modelDeploymentId, skillIds, knowledgeIds, toolId, timeout, retry, condition | None |
| All node type components | `pcf/PlaybookBuilderHost/control/components/Nodes/` (7 types) | **Complete** | None |
| AiPlaybookService | `pcf/PlaybookBuilderHost/control/services/AiPlaybookService.ts` | **Complete** | None |

#### Analysis Builder PCF

| Component | File | Status | Changes Needed |
|-----------|------|--------|---------------|
| AnalysisBuilderApp | `pcf/AnalysisBuilder/control/components/AnalysisBuilderApp.tsx` | **Complete** — creates sprk_analysis, associates N:N, navigates to form | None |

#### SprkChat Side Pane

| Component | File | Status | Changes Needed |
|-----------|------|--------|---------------|
| SprkChatPane Code Page | `code-pages/SprkChatPane/` | **Complete** | None |
| Launcher | `sprk_openSprkChatPane.js` | **Complete** | None |
| SprkChatBridge | `@spaarke/ui-components` (BroadcastChannel) | **Complete** | None |
| useSelectionBroadcast | `code-pages/AnalysisWorkspace/src/hooks/useSelectionBroadcast.ts` | **Complete** | None |
| useDiffReview | `code-pages/AnalysisWorkspace/src/hooks/useDiffReview.ts` | **Complete** | None |

#### Ribbon Commands

| Component | File | Status | Changes Needed |
|-----------|------|--------|---------------|
| sprk_analysis_commands.js | `webresources/js/sprk_analysis_commands.js` (535 lines) | **Complete** | None |

### 5.3 Components to Remove

None. All existing components are preserved. The legacy execution path remains as a fallback when `GetNodesAsync()` returns an empty array.

---

## 6. New Components & Changes Required

### Focus Area 1: Scope Resolution Foundation

#### 6.1 Fix Job Handler Registration (CRITICAL)

**File**: `Program.cs`

**Problem**: `AppOnlyDocumentAnalysis` job type messages go to dead-letter queue because the handler isn't registered in DI. Error: `"No handler registered for job type 'AppOnlyDocumentAnalysis'"`.

**Change**: Register the job handler:
```csharp
services.AddSingleton<IJobHandler, AppOnlyDocumentAnalysisJobHandler>();
```

**Verification**: Email processing completes end-to-end; no dead-letter errors.

#### 6.2 Complete Tool Resolution (Deploy + Test)

**File**: `ScopeResolverService.cs`

**Status**: `GetToolAsync` Dataverse query code is 80% written. Needs deployment to dev and end-to-end testing.

**Implementation** (already coded):
```csharp
public async Task<AnalysisTool?> GetToolAsync(Guid toolId, CancellationToken cancellationToken)
{
    await EnsureAuthenticatedAsync(cancellationToken);

    var url = $"sprk_analysistools({toolId})?$expand=sprk_ToolTypeId($select=sprk_name)";
    var response = await _httpClient.GetAsync(url, cancellationToken);

    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        return null;

    response.EnsureSuccessStatusCode();
    var entity = await response.Content.ReadFromJsonAsync<ToolEntity>(cancellationToken);
    // Map to AnalysisTool domain model...
}
```

**Acceptance**: Logs show `"Loaded tool from Dataverse: {ToolName}"`. GenericAnalysisHandler fallback works.

#### 6.3 Implement Skill Resolution from Dataverse

**File**: `ScopeResolverService.cs`

**Current**: Stub dictionary with fake GUIDs.

**New Implementation**:
```csharp
public async Task<AnalysisSkill?> GetSkillAsync(Guid skillId, CancellationToken cancellationToken)
{
    _logger.LogInformation("[GET SKILL] Loading skill {SkillId} from Dataverse", skillId);

    await EnsureAuthenticatedAsync(cancellationToken);

    var url = $"sprk_promptfragments({skillId})?$expand=sprk_SkillTypeId($select=sprk_name)";
    var response = await _httpClient.GetAsync(url, cancellationToken);

    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        _logger.LogWarning("[GET SKILL] Skill {SkillId} not found in Dataverse", skillId);
        return null;
    }

    response.EnsureSuccessStatusCode();

    var entity = await response.Content.ReadFromJsonAsync<SkillEntity>(cancellationToken);
    if (entity == null) return null;

    return new AnalysisSkill
    {
        Id = entity.Id,
        Name = entity.Name ?? "Unnamed Skill",
        Description = entity.Description,
        PromptFragment = entity.PromptFragment,
        Category = entity.SkillTypeId?.Name ?? "General",
        OwnerType = ScopeOwnerType.System,
        IsImmutable = false
    };
}
```

**DTO Classes**:
```csharp
private class SkillEntity
{
    [JsonPropertyName("sprk_promptfragmentid")]
    public Guid Id { get; set; }

    [JsonPropertyName("sprk_name")]
    public string? Name { get; set; }

    [JsonPropertyName("sprk_description")]
    public string? Description { get; set; }

    [JsonPropertyName("sprk_promptfragment")]
    public string? PromptFragment { get; set; }

    [JsonPropertyName("sprk_SkillTypeId")]
    public SkillTypeReference? SkillTypeId { get; set; }
}

private class SkillTypeReference
{
    [JsonPropertyName("sprk_name")]
    public string? Name { get; set; }
}
```

#### 6.4 Implement Knowledge Resolution from Dataverse

**File**: `ScopeResolverService.cs`

**Current**: Stub dictionary with fake GUIDs.

**New Implementation**:
```csharp
public async Task<AnalysisKnowledge?> GetKnowledgeAsync(Guid knowledgeId, CancellationToken cancellationToken)
{
    _logger.LogInformation("[GET KNOWLEDGE] Loading knowledge {KnowledgeId} from Dataverse", knowledgeId);

    await EnsureAuthenticatedAsync(cancellationToken);

    var url = $"sprk_contents({knowledgeId})?$expand=sprk_KnowledgeTypeId($select=sprk_name)";
    var response = await _httpClient.GetAsync(url, cancellationToken);

    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        _logger.LogWarning("[GET KNOWLEDGE] Knowledge {KnowledgeId} not found in Dataverse", knowledgeId);
        return null;
    }

    response.EnsureSuccessStatusCode();

    var entity = await response.Content.ReadFromJsonAsync<KnowledgeEntity>(cancellationToken);
    if (entity == null) return null;

    var knowledgeType = MapKnowledgeTypeName(entity.KnowledgeTypeId?.Name ?? "");

    return new AnalysisKnowledge
    {
        Id = entity.Id,
        Name = entity.Name ?? "Unnamed Knowledge",
        Description = entity.Description,
        Type = knowledgeType,
        Content = entity.Content,
        DeploymentId = entity.DeploymentId,
        OwnerType = ScopeOwnerType.System,
        IsImmutable = false
    };
}

private static KnowledgeType MapKnowledgeTypeName(string typeName)
{
    return typeName switch
    {
        string s when s.Contains("Standards", StringComparison.OrdinalIgnoreCase) => KnowledgeType.Inline,
        string s when s.Contains("Regulations", StringComparison.OrdinalIgnoreCase) => KnowledgeType.RagIndex,
        _ => KnowledgeType.Inline
    };
}
```

**DTO Classes**:
```csharp
private class KnowledgeEntity
{
    [JsonPropertyName("sprk_contentid")]
    public Guid Id { get; set; }

    [JsonPropertyName("sprk_name")]
    public string? Name { get; set; }

    [JsonPropertyName("sprk_description")]
    public string? Description { get; set; }

    [JsonPropertyName("sprk_content")]
    public string? Content { get; set; }

    [JsonPropertyName("sprk_deploymentid")]
    public Guid? DeploymentId { get; set; }

    [JsonPropertyName("sprk_KnowledgeTypeId")]
    public KnowledgeTypeReference? KnowledgeTypeId { get; set; }
}

private class KnowledgeTypeReference
{
    [JsonPropertyName("sprk_name")]
    public string? Name { get; set; }
}
```

#### 6.5 Implement Action Resolution from Dataverse

**File**: `ScopeResolverService.cs`

**Current**: Stub dictionary with fake GUIDs.

**New Implementation**:
```csharp
public async Task<AnalysisAction?> GetActionAsync(Guid actionId, CancellationToken cancellationToken)
{
    _logger.LogInformation("[GET ACTION] Loading action {ActionId} from Dataverse", actionId);

    await EnsureAuthenticatedAsync(cancellationToken);

    var url = $"sprk_systemprompts({actionId})?$expand=sprk_ActionTypeId($select=sprk_name)";
    var response = await _httpClient.GetAsync(url, cancellationToken);

    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        _logger.LogWarning("[GET ACTION] Action {ActionId} not found in Dataverse", actionId);
        return null;
    }

    response.EnsureSuccessStatusCode();

    var entity = await response.Content.ReadFromJsonAsync<ActionEntity>(cancellationToken);
    if (entity == null) return null;

    int sortOrder = 0;
    if (!string.IsNullOrEmpty(entity.ActionTypeId?.Name))
    {
        var match = System.Text.RegularExpressions.Regex.Match(entity.ActionTypeId.Name, @"^(\d+)\s*-");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var order))
            sortOrder = order;
    }

    return new AnalysisAction
    {
        Id = entity.Id,
        Name = entity.Name ?? "Unnamed Action",
        Description = entity.Description,
        SystemPrompt = entity.SystemPrompt,
        SortOrder = sortOrder,
        OwnerType = ScopeOwnerType.System,
        IsImmutable = false
    };
}
```

**DTO Classes**:
```csharp
private class ActionEntity
{
    [JsonPropertyName("sprk_systempromptid")]
    public Guid Id { get; set; }

    [JsonPropertyName("sprk_name")]
    public string? Name { get; set; }

    [JsonPropertyName("sprk_description")]
    public string? Description { get; set; }

    [JsonPropertyName("sprk_systemprompt")]
    public string? SystemPrompt { get; set; }

    [JsonPropertyName("sprk_ActionTypeId")]
    public ActionTypeReference? ActionTypeId { get; set; }
}

private class ActionTypeReference
{
    [JsonPropertyName("sprk_name")]
    public string? Name { get; set; }
}
```

#### 6.6 Remove All Stub Dictionaries

**File**: `ScopeResolverService.cs`

**Actions** (after Dataverse queries proven):
1. Delete `_stubActions` dictionary (lines 25-45)
2. Delete `_stubSkills` dictionary (lines 47-73)
3. Delete `_stubKnowledge` dictionary (lines 75-93)
4. Delete `_stubTools` dictionary (lines 95-129)
5. Remove all references to stub data in comments

#### 6.7 Handler Discovery API

**File**: `Api/Ai/HandlerEndpoints.cs` (already exists — extend or add handler metadata endpoint)

**Implementation**:
```csharp
app.MapGet("/api/ai/handlers", async (
    IToolHandlerRegistry registry,
    IMemoryCache cache,
    CancellationToken cancellationToken) =>
{
    var cacheKey = "ai:handlers:metadata";
    if (cache.TryGetValue(cacheKey, out object? cachedValue))
        return Results.Ok(cachedValue);

    var handlers = registry.GetAllHandlerInfo();
    var response = new
    {
        handlers = handlers.Select(h => new
        {
            handlerId = h.HandlerId,
            name = h.Metadata.Name,
            description = h.Metadata.Description,
            version = h.Metadata.Version,
            supportedToolTypes = h.SupportedToolTypes.Select(t => t.ToString()).ToArray(),
            supportedInputTypes = h.Metadata.SupportedInputTypes,
            parameters = h.Metadata.Parameters.Select(p => new
            {
                name = p.Name,
                description = p.Description,
                type = p.Type.ToString(),
                required = p.Required,
                defaultValue = p.DefaultValue
            }).ToArray(),
            configurationSchema = h.Metadata.ConfigurationSchema,
            isEnabled = h.IsEnabled
        }).ToArray()
    };

    cache.Set(cacheKey, response, TimeSpan.FromMinutes(5));
    return Results.Ok(response);
})
.WithName("GetToolHandlers")
.WithTags("AI")
.RequireAuthorization();
```

#### 6.8 Add ConfigurationSchema to Handler Metadata

**File**: `ToolHandlerMetadata.cs` + all 9 handler files

**Change to ToolHandlerMetadata**:
```csharp
public record ToolHandlerMetadata(
    string Name,
    string Description,
    string Version,
    IReadOnlyList<string> SupportedInputTypes,
    IReadOnlyList<ToolParameterDefinition> Parameters,
    object? ConfigurationSchema = null // NEW: JSON Schema Draft 07
);
```

Each handler adds its ConfigurationSchema to the Metadata property. All schemas follow JSON Schema Draft 07 specification with type, properties, required fields, and validation rules.

### Focus Area 2: Playbook Node AI Orchestration

#### 6.9 Canvas-to-Node Auto-Sync (NEW)

**Purpose**: When the playbook form is saved, sync canvas JSON nodes to executable `sprk_playbooknode` Dataverse records with N:N scope relationships.

**Location**: `NodeService.cs` — add `SyncCanvasToNodesAsync(Guid playbookId, string canvasJson)`

**Logic**:
1. Parse canvas JSON → extract nodes[] and edges[]
2. Load existing `sprk_playbooknode` records for this playbook
3. Diff canvas nodes vs existing records (match by canvas node ID stored in `sprk_configjson` or a dedicated field)
4. For each canvas node:
   - If new → `CreateNodeAsync()` with mapped fields
   - If exists → `UpdateNodeAsync()` with changed fields
   - Map N:N scopes: `node.data.skillIds[]` → `sprk_playbooknode_skill`, `node.data.knowledgeIds[]` → `sprk_playbooknode_knowledge`
   - Map lookup: `node.data.toolId` → `sprk_toolid`
   - Map lookup: `node.data.actionId` → `sprk_actionid`
   - Map `node.data.outputVariable` → `sprk_outputvariable`
   - Map `node.data.timeoutSeconds` → `sprk_timeoutseconds`
   - Map `node.data.retryCount` → `sprk_retrycount`
   - Map `node.data.conditionJson` → `sprk_conditionjson`
   - Map `node.data.config` → `sprk_configjson`
   - Map `node.position.x/y` → `sprk_position_x/y`
5. Compute execution order from topological sort of edges
6. Compute `sprk_dependsonjson` from incoming edges per node
7. Delete orphaned records (exist in Dataverse but not in canvas)
8. Set `sprk_isactive = true` for all synced nodes

**Trigger**: Call from `PlaybookEndpoints` — either:
- A new `POST /api/ai/playbooks/{id}/sync-nodes` endpoint called by PCF on form save, OR
- Hook into existing `PUT /api/ai/playbooks/{id}/canvas` (SaveCanvasLayout) to also sync nodes

**Files**: `NodeService.cs`, `INodeService.cs`, `PlaybookEndpoints.cs`

#### 6.10 Register AiAnalysisNodeExecutor in DI

**File**: `Program.cs`

**Change**: Add registration so `NodeExecutorRegistry` discovers it:
```csharp
services.AddSingleton<INodeExecutor, AiAnalysisNodeExecutor>();
```

This is a one-line fix. Without it, `NodeExecutorRegistry.GetExecutor(ActionType.AiAnalysis)` returns null and all AI nodes fail.

#### 6.11 Implement ResolveNodeScopesAsync

**File**: `ScopeResolverService.cs`

**Current**: Stub returning empty `ResolvedScopes`

**Implementation**: Query per-node N:N relationship tables:
```
sprk_playbooknode_skill → load AnalysisSkill[] for node (via GetSkillAsync for each)
sprk_playbooknode_knowledge → load AnalysisKnowledge[] for node (via GetKnowledgeAsync for each)
sprk_toolid lookup → load AnalysisTool for node (via GetToolAsync)
```

**Pattern**: Follow existing `ResolvePlaybookScopesAsync()` which queries playbook-level N:N tables using the same OData approach.

**Depends on**: 6.3 (GetSkillAsync), 6.4 (GetKnowledgeAsync), 6.2 (GetToolAsync) — must work before this method can produce real results.

#### 6.12 Load Document Text into PlaybookRunContext

**File**: `AnalysisOrchestrationService.cs` (in `ExecutePlaybookAsync` method)

**Current**: `PlaybookRunContext.Document` is never set.

**Implementation**: Before delegating to `PlaybookOrchestrationService.ExecuteAsync()`:
1. Download document via `SpeFileStore.DownloadFileAsUserAsync()` (OBO auth)
2. Extract text via `ITextExtractor` (PDF, DOCX, TXT, images)
3. Set `context.Document = new DocumentContext(text, metadata)`

**Reuse**: This exact pattern exists in the legacy action-based path in `AnalysisOrchestrationService`. Extract to a shared method.

#### 6.13 Wire ExecutePlaybookAsync to PlaybookOrchestrationService

**File**: `AnalysisOrchestrationService.cs`

**Current**: `ExecutePlaybookAsync()` checks for nodes via `GetNodesAsync()`. When nodes exist, it should delegate to `PlaybookOrchestrationService` but currently falls through to legacy.

**Implementation**:
```csharp
var nodes = await _nodeService.GetNodesAsync(playbookId, cancellationToken);
if (nodes.Length > 0)
{
    // Set statuscode to In Progress
    await UpdateAnalysisStatusAsync(analysisId, StatusCode.InProgress);

    // Delegate to node-based orchestration
    var request = new PlaybookRunRequest(playbookId, documentIds, analysisId);
    await foreach (var evt in _playbookOrchestrationService.ExecuteAsync(request))
    {
        // Bridge PlaybookStreamEvent → AnalysisStreamChunk for SSE
        yield return MapToStreamChunk(evt);
    }

    // Set statuscode to Completed
    await UpdateAnalysisStatusAsync(analysisId, StatusCode.Completed);
}
else
{
    // Legacy path (unchanged)
    await foreach (var chunk in ExecuteLegacyToolsAsync(...))
        yield return chunk;
}
```

**Bridge mapping**: `PlaybookStreamEvent.NodeProgress` → `AnalysisStreamChunk.TextChunk`

#### 6.14 Enable Per-Token Streaming in AiAnalysisNodeExecutor

**File**: `AiAnalysisNodeExecutor.cs`

**Current**: Uses `handler.ExecuteAsync()` (blocking, returns complete result).

**Implementation**: Check if handler implements `IStreamingAnalysisToolHandler`:
```csharp
if (handler is IStreamingAnalysisToolHandler streamingHandler)
{
    await foreach (var evt in streamingHandler.StreamExecuteAsync(toolContext, tool, ct))
    {
        if (evt is ToolStreamEvent.Token token)
            onTokenReceived?.Invoke(token.Text); // → NodeProgress SSE
        else if (evt is ToolStreamEvent.Completed completed)
            toolResult = completed.Result;
    }
}
else
{
    toolResult = await handler.ExecuteAsync(toolContext, tool, ct);
}
```

The `onTokenReceived` callback maps to `PlaybookStreamEvent.NodeProgress` which the orchestrator writes to the SSE channel.

#### 6.15 Persist Deliver Output Result to Working Document

**File**: `AnalysisOrchestrationService.cs` (in the bridge between PlaybookOrchestrationService output and final persistence)

**Implementation**: After node-based execution completes:
1. Find the Deliver Output node's result in `PlaybookRunContext.NodeOutputs`
2. Extract the rendered text content
3. Call `WorkingDocumentService.UpdateWorkingDocumentAsync(analysisId, content)`
4. Call `WorkingDocumentService.FinalizeAnalysisAsync(analysisId, tokensIn, tokensOut)` — sets statuscode to Completed(2)

#### 6.16 Document Profile Playbook Node Configuration

Create 4 nodes in the Document Profile playbook via the visual Playbook Builder:

| Node | Type | OutputVariable | DependsOn | Skills/Knowledge | Tool |
|------|------|---------------|-----------|-----------------|------|
| Document Classifier | aiAnalysis | `classifier` | (none) | As configured | TL-001 DocumentClassifierHandler |
| Entity Extractor | aiAnalysis | `extractor` | (none) | As configured | TL-003 EntityExtractorHandler |
| Summary Generator | aiAnalysis | `summarizer` | (none) | As configured | TL-002 SummaryGeneratorHandler |
| Deliver Output | deliverOutput | `output` | classifier, extractor, summarizer | (none) | (none) |

**Deliver Output ConfigJson**:
```json
{
  "deliveryType": "markdown",
  "template": "## Document Classification\n**Type:** {{classifier.output.category}}\n**Confidence:** {{classifier.output.confidence}}\n\n## Executive Summary\n{{summarizer.text}}\n\n## Key Entities\n{{#each extractor.output.entities}}\n- **{{this.value}}** ({{this.type}})\n{{/each}}"
}
```

After designing in the canvas and saving the form, the auto-sync (6.9) creates the Dataverse records.

### Focus Area 3: Analysis Workspace Application

#### 6.17 Replace Age-Based Auto-Execute with Statuscode

**File**: `useAnalysisExecution.ts`

**Remove**:
- `NEW_RECORD_AGE_MS` constant
- `createdOn` age check in `shouldAutoExecute()`

**New `shouldAutoExecute()` logic**:
```typescript
const isDraft = analysis.statusCode === 1;
const isEmpty = !analysis.content || analysis.content.trim().length === 0;
const hasAction = !!analysis.actionId || !!analysis.playbookId;
return isDraft && isEmpty && hasAction && !!token && !isExecuting;
```

**Server-side complement**: BFF sets statuscode to In Progress at execution start (6.13), so reopening the same record skips auto-execute.

**New export**: `triggerExecute()` function for the Run Analysis button (manual invocation that bypasses shouldAutoExecute checks).

#### 6.18 Add Completion Notification

**File**: `App.tsx`

**Implementation**: After execution completes and `reloadAnalysis()` succeeds:
```typescript
import { useToastController, Toast, ToastTitle } from "@fluentui/react-components";

// In the onComplete callback:
dispatchToast(
    <Toast><ToastTitle>Analysis complete</ToastTitle></Toast>,
    { intent: "success", timeout: 5000 }
);
```

Wrap the app root in `<Toaster>` provider.

#### 6.19 Add "Run Analysis" Toolbar Button

**File**: `App.tsx` (or AnalysisToolbar component)

**Implementation**: Fluent UI v9 `<Button>` in the toolbar:
- Label: "Run Analysis" with play icon
- Disabled when `isExecuting` is true or `!analysis.playbookId && !analysis.actionId`
- onClick calls `triggerExecute()` from useAnalysisExecution (6.17)
- Shows spinner during execution

#### 6.20 Add Source Pane Toggle Button

**File**: `App.tsx`

**Implementation**: Toggle button in toolbar:
- Controls `showSourcePane` state (default: true)
- When hidden, EditorPanel takes full width
- When shown, PanelSplitter + SourceViewerPanel render
- Button shows document icon with tooltip "Show/Hide Source Document"

#### 6.21 Auto-Load SprkChat Side Pane

**File**: `App.tsx` or `index.tsx`

**Implementation**: On workspace load (after auth and analysis load):
```typescript
useEffect(() => {
    if (!analysisId || !Xrm?.App?.sidePanes) return;

    Xrm.App.sidePanes.createPane({
        title: "SprkChat",
        paneId: "sprkchat-analysis",
        canClose: true,
        imageSrc: "sprk_/images/sprkchat_icon.svg",
        width: 400
    }).then(pane => {
        pane.navigate({
            pageType: "webresource",
            webresourceName: "sprk_SprkChatPane",
            data: `analysisId=${analysisId}&documentId=${documentId}`
        });
    }).catch(err => {
        console.warn("[AnalysisWorkspace] SprkChat pane unavailable:", err);
    });
}, [analysisId, documentId]);
```

**Behavior**:
- Opens automatically when workspace loads
- User can close it — button remains in Dataverse side pane rail
- Clicking the rail button reopens SprkChat with same context
- This is the Dataverse-native pattern — persistent everywhere

---

## 7. Data Architecture

### 7.1 Scope Entities (Source of Truth)

```
sprk_analysistool (Tools):
  sprk_analysistoolid (Guid) — PK
  sprk_name (String 200)
  sprk_description (Memo 2000)
  sprk_tooltypeid (Lookup → sprk_aitooltype)
  sprk_handlerclass (String 200) — maps to IAnalysisToolHandler class name
  sprk_configuration (Memo 100K) — JSON config for handler

sprk_promptfragment (Skills):
  sprk_promptfragmentid (Guid) — PK
  sprk_name (String 200)
  sprk_description (Memo 2000)
  sprk_skilltypeid (Lookup → sprk_aiskilltype)
  sprk_promptfragment (Memo 100K) — the actual prompt fragment text

sprk_systemprompt (Actions):
  sprk_systempromptid (Guid) — PK
  sprk_name (String 200)
  sprk_description (Memo 2000)
  sprk_actiontypeid (Lookup → sprk_analysisactiontype)
  sprk_systemprompt (Memo 100K) — the system prompt text

sprk_content (Knowledge):
  sprk_contentid (Guid) — PK
  sprk_name (String 200)
  sprk_description (Memo 2000)
  sprk_knowledgetypeid (Lookup → sprk_aiknowledgetype)
  sprk_content (Memo 100K) — inline content or RAG index config
  sprk_deploymentid (Guid, nullable) — for RAG type
```

### 7.2 Canvas JSON vs Dataverse Records

```
Playbook Record (sprk_analysisplaybook)
├── sprk_canvaslayoutjson (text field) ← VISUAL ONLY
│   └── { nodes: PlaybookNode[], edges: Edge[], version: 1 }
│       Each node has: id, type, position, data: {
│           label, type, actionId, outputVariable,
│           skillIds[], knowledgeIds[], toolId,
│           modelDeploymentId, config, timeoutSeconds, retryCount
│       }
│
└── sprk_playbooknode Records (separate table) ← EXECUTABLE
    ├── sprk_name (text)
    ├── _sprk_playbookid_value (lookup → playbook)
    ├── _sprk_actionid_value (lookup → action)
    ├── _sprk_toolid_value (lookup → tool)
    ├── _sprk_modeldeploymentid_value (lookup)
    ├── sprk_executionorder (integer)
    ├── sprk_dependsonjson (JSON array of node GUIDs)
    ├── sprk_outputvariable (text)
    ├── sprk_conditionjson (JSON)
    ├── sprk_configjson (JSON — includes Deliver Output template)
    ├── sprk_timeoutseconds (integer)
    ├── sprk_retrycount (integer)
    ├── sprk_position_x/y (integer — from canvas)
    ├── sprk_isactive (boolean)
    └── N:N Relationships (per node)
        ├── sprk_playbooknode_skill → sprk_promptfragment (skills)
        └── sprk_playbooknode_knowledge → sprk_content (knowledge)
```

### 7.3 Sync Flow

```
User edits canvas → Zustand canvasStore → isDirty=true
→ 500ms debounce → getCanvasJson() → onSave(json)
→ PCF bound field → sprk_canvaslayoutjson saved on form save
→ Form save event → POST /api/ai/playbooks/{id}/sync-nodes
→ NodeService.SyncCanvasToNodesAsync():
    Parse JSON → diff vs existing → create/update/delete records
    Map skillIds/knowledgeIds/toolId → N:N relationships
    Compute executionOrder from topological sort
    Compute dependsOnJson from edge connections
```

### 7.4 Scope Hierarchy

```
Playbook-Level Scopes (N:N on playbook):
  sprk_analysisplaybook_action → available actions
  sprk_playbook_skill → available skills
  sprk_playbook_knowledge → available knowledge
  sprk_playbook_tool → available tools

Node-Level Scopes (N:N on node):
  sprk_playbooknode_skill → skills assigned to this node
  sprk_playbooknode_knowledge → knowledge assigned to this node
  sprk_toolid (lookup) → single tool for this node
  sprk_actionid (lookup) → action providing system prompt

Scope Resolution at Execution:
  ResolveNodeScopesAsync(nodeId) queries node-level N:N tables
  Each scope getter (GetSkillAsync, GetToolAsync, etc.) queries Dataverse
  Returns: ResolvedScopes { Skills[], Knowledge[], Tools[] }
```

### 7.5 Handler Resolution Pattern

```
Unified Resolution (All Scope Types):
1. Load from Dataverse (no stubs) using HttpClient + Web API
2. Expand lookups ($expand for type relationships)
3. Map to domain model (AnalysisTool, AnalysisSkill, etc.)
4. Resolve handler (if applicable):
   a. Check sprk_handlerclass field first
   b. If not found → fall back to GenericAnalysisHandler
   c. If no GenericHandler → fall back to type-based lookup
5. Return null only if entity doesn't exist in Dataverse
```

### 7.6 Analysis Record Status Transitions

```
sprk_analysis.statuscode values:
  1          = Draft (initial, auto-execute eligible)
  100000001  = In Progress (set by BFF at execution start)
  2          = Completed (set by BFF after Deliver Output persisted)
  100000002  = Error (set by BFF on execution failure)

Transition rules:
  Create record      → statuscode = 1 (Draft)
  Execute starts     → statuscode = In Progress
  Execute succeeds   → statuscode = 2 (Completed)
  Execute fails      → statuscode = Error
  Re-execute (manual)→ statuscode = In Progress (via Run Analysis button)
```

---

## 8. Architecture Decisions

### AD-01: Visual Builder as Source of Truth

The Playbook Builder canvas is the primary design surface. Node records in Dataverse are derived (synced) from canvas JSON on form save. This means:
- Users always work in the visual builder
- No need to manually create Dataverse records
- Canvas JSON is the design artifact; Dataverse records are the execution artifact
- The AI Assistant can also generate canvas patches that sync to records

### AD-02: Statuscode-Based Auto-Execute

Replace the `createdOn` age check (60-second window) with a statuscode check. A Draft(1) analysis with empty content auto-executes exactly once because the BFF immediately transitions it to In Progress. This is deterministic and doesn't depend on timing.

### AD-03: Deliver Output for All Formatting

Individual tool results return structured JSON — this is correct and intentional. The Deliver Output node is the single point of output formatting. It uses Handlebars templates to compose tool results into clean markdown (or html/json/text). This separation means:
- Tool handlers focus on extraction/analysis, not presentation
- Output format is configurable per playbook (via Deliver Output template)
- The same tool results can be formatted differently for different use cases

### AD-04: Per-Token Streaming from AI Nodes

AI analysis nodes stream tokens via `IStreamingAnalysisToolHandler.StreamExecuteAsync()`. Each token emits a `NodeProgress` SSE event. The Analysis Workspace renders these progressively via `markdownToHtml()`. However, since individual tools return JSON, the streaming content during AI execution may show JSON fragments. The final formatted output comes from the Deliver Output node.

**Decision**: Stream tokens from AI nodes for responsiveness, but the primary rendered content is the Deliver Output result. Consider showing a "Processing..." indicator during AI node execution and rendering the final Deliver Output result.

### AD-05: SprkChat as Persistent Side Pane

SprkChat uses `Xrm.App.sidePanes.createPane()` — the Dataverse-native pattern. It auto-loads with the Analysis Workspace and persists in the side rail. This is NOT embedded as a third panel in the workspace layout. This pattern extends to all Spaarke contexts (document forms, matter forms, etc.).

### AD-06: Three-Tier Scope Resolution

All scopes loaded from Dataverse (Tier 1 - Configuration). Most tools execute via GenericAnalysisHandler (Tier 2 - Generic Execution) reading configuration JSON. Complex scenarios use custom handlers (Tier 3) registered in DI and discoverable via IToolHandlerRegistry. This enables:
- Zero-deployment scope additions (create in Dataverse, it works)
- Secure execution (no arbitrary code in GenericAnalysisHandler)
- Custom behavior when needed (specified via HandlerClass field)

### AD-07: Consolidated Project (Scope Resolution + Playbook Builder)

Scope resolution and playbook node execution are tightly coupled. `ResolveNodeScopesAsync` depends on `GetSkillAsync`, `GetKnowledgeAsync`, `GetToolAsync`, `GetActionAsync` all working correctly. Keeping them in one project ensures:
- Consistent implementation patterns across all scope types
- No GUID mismatch between scope getters and N:N relationship loaders
- Shared testing and deployment
- Clear dependency ordering (scope getters before node execution)

---

## 9. Technical Constraints

### Applicable ADRs

| ADR | Constraint |
|-----|-----------|
| ADR-001 | Minimal API + BackgroundService. No Azure Functions. |
| ADR-004 | Job Contract — idempotent handlers, propagate CorrelationId, emit JobOutcome events |
| ADR-006 | Anti-legacy-JS: PCF for form controls, Code Page for standalone dialogs |
| ADR-007 | SpeFileStore facade — no Graph SDK types leak above facade |
| ADR-008 | Endpoint filters for auth — no global auth middleware |
| ADR-010 | DI minimalism — ≤15 non-framework registrations, use feature modules |
| ADR-013 | AI Architecture — extend BFF, not separate service; use SpeFileStore for files |
| ADR-014 | AI Caching — Redis for expensive AI results; IMemoryCache for short-lived metadata |
| ADR-021 | Fluent UI v9 — all UI must use Fluent v9; dark mode required |
| ADR-022 | PCF Platform Libraries — React 16 APIs in PCF; React 18 bundled in Code Pages |

### Existing Patterns to Follow

**Dataverse Web API Query Pattern** (from existing GetToolAsync):
```csharp
var url = $"sprk_analysistools({toolId})?$expand=sprk_ToolTypeId($select=sprk_name)";
var response = await _httpClient.GetAsync(url, cancellationToken);
if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
    return null;
response.EnsureSuccessStatusCode();
var entity = await response.Content.ReadFromJsonAsync<ToolEntity>(cancellationToken);
```

**Handler Resolution with Fallback** (from existing AppOnlyAnalysisService):
```csharp
// 1. Check sprk_handlerclass field first
if (!string.IsNullOrWhiteSpace(tool.HandlerClass))
    handler = registry.GetHandler(tool.HandlerClass);

// 2. Fall back to GenericAnalysisHandler
if (handler == null)
{
    _logger.LogWarning("Handler '{HandlerClass}' not found. Available: [{Available}]. Falling back...",
        tool.HandlerClass, string.Join(", ", registry.GetRegisteredHandlerIds()));
    handler = registry.GetHandler("GenericAnalysisHandler");
}
```

---

## 10. Reference Documents

| Document | Location | Content |
|----------|----------|---------|
| AI Architecture Guide | `docs/guides/SPAARKE-AI-ARCHITECTURE.md` | Four-tier architecture, scope catalog, playbook catalog, canvas JSON format |
| Prior Playbook Builder Design | `projects/x-ai-playbook-node-builder-r3/design.md` | AI Assistant design, existing component inventory |
| AI Chat Builder Design | `projects/ai-playbook-node-builder-r2/ai-chat-playbook-builder.md` | 1200+ line detailed design for AI-assisted playbook construction |
| Scope Resolution Update Plan | _(consolidated into this document)_ | Original: `ai-scope-resolution-enhancements/scope-resolution-update-plan.md` — project removed after merge |
| Scope Resolution Spec | _(consolidated into this document)_ | Original: `ai-scope-resolution-enhancements/spec.md` — project removed after merge |
| ADR-001 | `.claude/adr/ADR-001.md` | Minimal API + BackgroundService (no Azure Functions) |
| ADR-004 | `.claude/adr/ADR-004.md` | Job Contract — idempotent handlers, CorrelationId |
| ADR-006 | `.claude/adr/ADR-006.md` | Anti-legacy-JS: PCF for form controls, Code Page for standalone dialogs |
| ADR-013 | `.claude/adr/ADR-013.md` | AI Architecture: Tool Framework, extend BFF not separate service |
| ADR-021 | `.claude/adr/ADR-021.md` | Fluent UI v9 Design System |

---

## 11. Implementation Phases & Dependencies

### Phase Overview

```
Phase 0: Job Handler Registration Fix ─────────────────────────────┐
                                                                    │
Phase 1: Complete Tool Resolution (deploy + test) ─────────────────┤
                                                                    │
     ┌─── Phase 2: Skill Resolution ──────────────────┐            │
     │                                                  │            │
     ├─── Phase 3: Knowledge Resolution ──────────────┤─── Phase 5: Stub Removal ──┐
     │                                                  │                             │
     └─── Phase 4: Action Resolution ────────────────┘                             │
                                                                                     │
Phase 6: Handler Discovery API + ConfigurationSchema ──────────────────────────────┤
                                                                                     │
Phase 7: Canvas-to-Node Sync + DI Registration ───────────────────────────────────┤
                                                                                     │
Phase 8: ResolveNodeScopesAsync + Execution Wiring ───────────────────────────────┤
                                                                                     │
Phase 9: Document Loading + Streaming + Output Persistence ────────────────────────┤
                                                                                     │
Phase 10: Analysis Workspace UX (statuscode, toast, button, SprkChat) ─────────────┤
                                                                                     │
Phase 11: End-to-End Testing & Verification ──────────────────────────────────────┤
                                                                                     │
Phase 12: Deployment & Monitoring ────────────────────────────────────────────────┘
```

### Sequential Dependencies

| Phase | Depends On | Rationale |
|-------|-----------|-----------|
| 0 | None | Critical fix, unblocks everything |
| 1 | 0 | Tool resolution needs working job processing to test |
| 2, 3, 4 | 1 | Follow same pattern as proven tool resolution |
| 5 | 2, 3, 4 | Must complete all Dataverse queries before removing stubs |
| 6 | 5 | Discovery API should return real data, not stub-backed |
| 7 | 1 | Canvas sync creates node records that need tools/scopes |
| 8 | 5, 7 | ResolveNodeScopesAsync uses all scope getters + needs node records |
| 9 | 8 | Execution wiring must work before adding streaming/persistence |
| 10 | 9 | Workspace UX depends on execution pipeline working |
| 11 | All above | Integration testing validates everything |
| 12 | 11 | Deploy after tests pass |

### Parallel Execution Opportunities

- **Phases 2, 3, 4** can run in parallel (skill, knowledge, action resolution are independent)
- **Phase 6** can overlap with Phases 7-9 (handler discovery is independent of node execution)
- **Phase 10** client-side work can overlap with Phase 9 server-side work (different codebases)

---

## 12. Risk Assessment

### High Risk

| Risk | Impact | Mitigation |
|------|--------|------------|
| Dataverse query performance | Analysis latency increases | Add caching (Redis), index optimization |
| Schema mismatch (field names) | Deserialization failures | Thorough testing with real Dataverse data |
| Handler not found (production) | Tool execution failures | Fallback to GenericAnalysisHandler (already implemented) |

### Medium Risk

| Risk | Impact | Mitigation |
|------|--------|------------|
| Canvas-to-node sync data loss | Orphaned or missing node records | Diff-based sync with logging; preserve Dataverse records on sync failure |
| Migration timing (stub removal) | Breaking changes | Deploy Dataverse queries first, remove stubs later (phased) |
| N:N relationship mapping errors | Wrong scopes assigned to nodes | Verify with Dataverse query explorer before code |

### Low Risk

| Risk | Impact | Mitigation |
|------|--------|------------|
| SprkChat side pane unavailable | No chat integration | Graceful catch — workspace works without SprkChat |
| Increased API calls to Dataverse | Cost/throttling | Caching strategy reduces calls by 95% |

---

## 13. Verification Plan

### Focus Area 1: Scope Resolution

| Step | Test | Expected Result |
|------|------|----------------|
| 1 | Deploy with job handler fix | No "NoHandler" dead-letter errors |
| 2 | Trigger email processing | sprk_analysis records created successfully |
| 3 | Check logs for tool resolution | `"Loaded tool from Dataverse: {ToolName}"` |
| 4 | Check logs for skill resolution | `"Loaded skill from Dataverse: {SkillName}"` |
| 5 | Check logs for knowledge resolution | `"Loaded knowledge from Dataverse: {KnowledgeName}"` |
| 6 | Check logs for action resolution | `"Loaded action from Dataverse: {ActionName}"` |
| 7 | Test missing handler fallback | GenericAnalysisHandler executes; log lists available handlers |
| 8 | Verify stub dictionaries removed | No references to `_stubTools`, `_stubSkills`, etc. in codebase |
| 9 | Call GET /api/ai/handlers | Returns all handlers with ConfigurationSchema |
| 10 | Call GET /api/ai/handlers twice | Second call returns cached response (< 10ms) |

### Focus Area 2: Node-Based Execution

| Step | Test | Expected Result |
|------|------|----------------|
| 11 | Open Document Profile playbook in Playbook Builder | Canvas shows 4 nodes (3 AI + 1 Deliver Output) |
| 12 | Save the playbook form | Auto-sync creates 4 `sprk_playbooknode` records with N:N scopes |
| 13 | Verify Dataverse records | `GetNodesAsync(playbookId)` returns 4 nodes with correct tool/skill/knowledge associations |
| 14 | Create new analysis via Analysis Builder → select Document Profile | Analysis record created with statuscode=1, empty content |
| 15 | Analysis Workspace loads | Auto-execute triggers (Draft + empty) |
| 16 | Watch server logs | `PlaybookOrchestrationService.ExecuteAsync()` called (not legacy path) |
| 17 | Watch SSE stream | NodeStarted/NodeProgress/NodeCompleted events for each of 3 AI nodes, then Deliver Output |
| 18 | Verify parallel execution | 3 AI nodes run concurrently (check timestamps in logs) |
| 19 | Verify final output | `sprk_workingdocument` contains formatted markdown (not raw JSON) |
| 20 | Verify statuscode | Draft(1) → In Progress → Completed(2) |
| 21 | Reopen same analysis | Formatted content displayed; no auto-execute (statuscode !== 1) |

### Focus Area 3: Workspace UX

| Step | Test | Expected Result |
|------|------|----------------|
| 22 | Analysis completes | Toast notification: "Analysis complete" |
| 23 | Click "Run Analysis" button | Execution triggers, button shows spinner, disabled during execution |
| 24 | Toggle source pane | Document viewer hides/shows; editor takes full width when hidden |
| 25 | Workspace loads | SprkChat side pane opens automatically |
| 26 | Close SprkChat | Button remains in Dataverse side pane rail; clicking reopens with same context |
| 27 | Select text in editor | SprkChat receives selection event via BroadcastChannel |

### Regression: Legacy Path

| Step | Test | Expected Result |
|------|------|----------------|
| 28 | Execute a playbook that has NO nodes in canvas | Legacy sequential path runs unchanged |
| 29 | Execute an action-based analysis (no playbook) | Action path runs unchanged |
| 30 | Create new tool in Dataverse (no HandlerClass) | GenericAnalysisHandler picks it up automatically |

### Monitoring Thresholds (Post-Deploy)

| Metric | Threshold | Action if Exceeded |
|--------|-----------|-------------------|
| Dead-letter queue messages | > 5/hour | Investigate, rollback if critical |
| Scope resolution failures | > 2% | Review logs, fix data issues |
| Handler not found warnings | > 10/hour | Check handler registration |
| API response time (GET /api/ai/handlers) | > 500ms | Verify cache working |
| Analysis success rate | < 95% | Investigate playbook configs |
| Scope resolution latency | > 200ms p95 | Add Redis caching |

---

## Appendix A: Handler Registration Verification

**Verify GenericAnalysisHandler registered:**
```csharp
var registry = serviceProvider.GetRequiredService<IToolHandlerRegistry>();
var handlerIds = registry.GetRegisteredHandlerIds();

// Should include:
// - EntityExtractorHandler
// - GenericAnalysisHandler
// - SummaryHandler
// - ClauseAnalyzerHandler
// - DocumentClassifierHandler
// - RiskDetectorHandler
// - ClauseComparisonHandler
// - DateExtractorHandler
// - FinancialCalculatorHandler
```

## Appendix B: Example Log Output

**Successful resolution:**
```
[11:23:45 INF] [GET TOOL] Loading tool abc-123-real-guid from Dataverse
[11:23:45 INF] [GET TOOL] Loaded tool from Dataverse: Entity Extractor (Type: EntityExtractor, HandlerClass: EntityExtractorHandler)
[11:23:46 DBG] Executing tool 'Entity Extractor' (Type=EntityExtractor, HandlerClass=EntityExtractorHandler)
[11:23:50 INF] Tool execution complete: EntityExtractor in 4234ms
```

**Fallback resolution:**
```
[11:23:45 INF] [GET TOOL] Loading tool abc-123-real-guid from Dataverse
[11:23:45 INF] [GET TOOL] Loaded tool from Dataverse: Custom Risk Tool (Type: Custom, HandlerClass: CustomRiskHandler)
[11:23:46 WRN] Custom handler 'CustomRiskHandler' not found. Available: [EntityExtractorHandler, GenericAnalysisHandler, SummaryHandler, ...]. Falling back to GenericAnalysisHandler.
[11:23:50 INF] Generic tool execution complete: extract in 4123ms
```

---

*Consolidated from ai-playbook-builder-r2 and ai-scope-resolution-enhancements (removed after merge).*
