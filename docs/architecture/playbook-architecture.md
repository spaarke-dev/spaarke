# Playbook Architecture

> **Version**: 1.0
> **Last Updated**: March 13, 2026
> **Audience**: Claude Code, AI agents, engineers
> **Purpose**: Technical reference for the Spaarke Playbook system — visual AI workflows, node execution, canvas data model
> **Parent**: [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md) (Spaarke AI platform overview)
> **Implementation companion**: [ai-implementation-reference.md](ai-implementation-reference.md) (working code examples, configuration)

---

## Overview

Playbooks are the primary AI composition pattern in Spaarke — visual node-based workflows stored as Dataverse records. They define **what** AI operations to perform and in what order. The execution backend is pluggable (currently in-process, future: Microsoft Agent Framework).

**Entity**: `sprk_analysisplaybook`
**Canvas field**: `sprk_canvaslayoutjson` (serialized JSON of nodes and edges)
**Builder**: `src/client/code-pages/PlaybookBuilder/` (React 18 Code Page)

---

## Three-Level Node Type System

Nodes use three type concepts at different layers:

| Level | Name | Where Stored | Purpose | Example |
|-------|------|-------------|---------|---------|
| **Canvas Type** | `PlaybookNodeType` | React Flow `node.data.type` | React component selection | `"aiAnalysis"` |
| **Dataverse NodeType** | `sprk_nodetype` | `sprk_playbooknode` OptionSet | Coarse scope resolution | `AIAnalysis (100000000)` |
| **ActionType** | `__actionType` in ConfigJson | `sprk_playbooknode.sprk_configjson` | Fine-grained executor dispatch | `AiAnalysis (0)` |

### Canvas Types (9 React components — drag-and-drop palette items)

```typescript
type PlaybookNodeType =
  | 'start'           // Entry point — always Spaarke code
  | 'aiAnalysis'      // AI analysis (LLM call) — backend-flexible
  | 'aiCompletion'    // AI completion (LLM call) — backend-flexible
  | 'condition'       // Conditional branching — always Spaarke code
  | 'deliverOutput'   // Output delivery — always Spaarke code
  | 'deliverToIndex'  // RAG semantic indexing — always Spaarke code
  | 'updateRecord'    // Dataverse field writes — always Spaarke code
  | 'createTask'      // Task creation — always Spaarke code
  | 'sendEmail'       // Email action — always Spaarke code
  | 'wait';           // Wait/delay — always Spaarke code
```

### Dataverse NodeType (4 coarse categories for scope resolution)

```csharp
public enum NodeType
{
    AIAnalysis = 100_000_000,  // Full scope resolution (skills, knowledge, tools)
    Output     = 100_000_001,  // No scopes — assembles previous outputs
    Control    = 100_000_002,  // No scopes — flow control
    Workflow   = 100_000_003   // No scopes — Dataverse/email actions
}
```

### ActionType (15 fine-grained executor dispatch values)

```csharp
public enum ActionType
{
    // AI nodes (backend-flexible)
    AiAnalysis = 0, AiCompletion = 1, AiEmbedding = 2,
    // Processing nodes
    RuleEngine = 10, Calculation = 11, DataTransform = 12,
    // Workflow nodes (always Spaarke code)
    CreateTask = 20, SendEmail = 21, UpdateRecord = 22,
    CallWebhook = 23, SendTeamsMessage = 24,
    // Control nodes (always Spaarke code)
    Condition = 30, Parallel = 31, Wait = 32,
    // Output nodes
    DeliverOutput = 40,
    DeliverToIndex = 41     // Queue document for RAG semantic indexing
}
```

### Mapping Flow (computed during canvas-to-Dataverse sync)

```
Canvas Type "sendEmail"
  → NodeType.Workflow (100000003)     ← written to sprk_nodetype
  → ActionType.SendEmail (21)         ← written to __actionType in sprk_configjson

Canvas Type "deliverToIndex"
  → NodeType.Output (100000001)       ← written to sprk_nodetype
  → ActionType.DeliverToIndex (41)    ← written to __actionType in sprk_configjson
```

At execution time: NodeType determines scope resolution strategy, ActionType determines which `INodeExecutor` runs.

**Mapping files**:
- Client-side: `src/client/code-pages/PlaybookBuilder/src/services/playbookNodeSync.ts` (`mapCanvasTypeToNodeType`, `mapCanvasTypeToActionType`)
- Server-side: `src/server/api/Sprk.Bff.Api/Services/Ai/NodeService.cs` (`MapCanvasTypeToNodeType`, `MapCanvasTypeToActionType`)

Both must stay in sync — missing entries cause fallthrough to default `AIAnalysis`, which triggers incorrect scope resolution and "requires an Action" errors.

---

## Node Data Structure

```typescript
// src/client/code-pages/PlaybookBuilder/src/types/playbook.ts
interface PlaybookNodeData {
  label: string;
  type: PlaybookNodeType;
  actionId?: string;             // Linked action (system prompt)
  outputVariable?: string;       // Variable name for output reference
  isActive?: boolean;            // Active/disabled toggle
  skillIds?: string[];           // Linked skills (N:N)
  knowledgeIds?: string[];       // Linked knowledge (N:N)
  toolIds?: string[];            // Linked tools (N:N)
  modelDeploymentId?: string;    // AI model selection
  timeoutSeconds?: number;       // 30-3600s
  retryCount?: number;           // 0-5
  conditionJson?: string;        // Condition expression (condition nodes)
  // Type-specific config (stored in sprk_configjson):
  deliveryType?: string;         // Deliver Output: "markdown" | "html" | "text" | "json"
  template?: string;             // Deliver Output: Handlebars template
  indexName?: string;            // Deliver to Index: target search index (default: "knowledge")
  indexSource?: string;          // Deliver to Index: "document" | "field"
  emailTo?: string[];            // Send Email: recipients
  emailSubject?: string;         // Send Email: subject
  taskSubject?: string;          // Create Task: task subject
  systemPrompt?: string;         // AI Completion: custom system prompt
  userPromptTemplate?: string;   // AI Completion: user prompt with {{variables}}
  waitType?: string;             // Wait: "duration" | "until" | "condition"
}
```

---

## Canvas JSON Format

Stored in `sprk_analysisplaybook.sprk_canvaslayoutjson`:

```json
{
  "nodes": [
    {
      "id": "node_1736956789_abc",
      "type": "aiAnalysis",
      "position": { "x": 250, "y": 100 },
      "data": {
        "label": "Extract Entities",
        "type": "aiAnalysis",
        "actionId": "ACT-001",
        "skillIds": ["SKL-001", "SKL-008"],
        "knowledgeIds": ["KNW-001"],
        "toolId": "TL-001",
        "outputVariable": "entities",
        "modelDeploymentId": "gpt-4o-mini"
      }
    }
  ],
  "edges": [
    {
      "id": "reactflow__edge-node_1-node_2",
      "source": "node_1",
      "target": "node_2",
      "type": "smoothstep",
      "animated": true
    }
  ],
  "version": 1
}
```

---

## Canvas-to-Dataverse Sync

**File**: `src/client/code-pages/PlaybookBuilder/src/services/playbookNodeSync.ts`

- Auto-save: 30-second debounce after canvas changes
- Manual save: Ctrl+S
- On save: Canvas JSON → `sprk_canvaslayoutjson`, then `syncNodesToDataverse()`:
  1. Queries existing `sprk_playbooknode` records
  2. Computes execution order via Kahn's topological sort of canvas edges
  3. Creates/updates/deletes node records with `sprk_nodetype` + `__actionType` in ConfigJson
  4. Writes `sprk_dependsonjson` with upstream node GUIDs
  5. Manages N:N relationships (skills, knowledge, tools) via associate/disassociate
- Uses `DataverseClient` (fetch + Bearer token via MSAL)

---

## Playbook Builder (Code Page)

**Path**: `src/client/code-pages/PlaybookBuilder/`
**Stack**: React 18, @xyflow/react v12, Fluent UI v9, Zustand
**Deployment**: Inline HTML web resource (`sprk_playbookbuilder.html`)

```
PlaybookBuilder (Code Page)
└── FluentProvider (Fluent UI v9 theme, dark mode via useThemeDetection)
    └── App
        └── BuilderLayout
            ├── Toolbar (save, run, AI assistant toggle, fullscreen)
            ├── NodePalette (left sidebar, draggable node types)
            ├── PlaybookCanvas (center, @xyflow/react v12)
            │   ├── StartNode
            │   ├── AiAnalysisNode / AiCompletionNode
            │   ├── ConditionNode
            │   ├── DeliverOutputNode / DeliverToIndexNode
            │   ├── UpdateRecordNode
            │   ├── CreateTaskNode / SendEmailNode / WaitNode
            │   └── ConditionEdge (true/false branches)
            ├── NodePropertiesDialog (modal, opens on node select)
            │   └── NodePropertiesForm
            │       ├── ScopeSelector (skills, knowledge, tools)
            │       ├── ModelSelector (AI model dropdown)
            │       ├── ActionSelector (linked action)
            │       ├── Type-specific forms (DeliverToIndexForm, SendEmailForm, etc.)
            │       └── VariableReferencePanel (upstream output variables)
            ├── ExecutionOverlay (during playbook execution)
            └── AiAssistantModal (conversational builder, floating)
```

### PlaybookBuilderHost PCF Control (Legacy R4)

**Path**: `src/client/pcf/PlaybookBuilderHost/`
**Stack**: React 16/17, react-flow-renderer v10, Fluent UI v9
**Status**: Still maintained; mirrors Code Page structure. Used as field-bound control on `sprk_analysisplaybook` form.

---

## Execution Engine

### PlaybookExecutionEngine (Dual Mode)

**Interface**: `src/server/api/Sprk.Bff.Api/Services/Ai/IPlaybookExecutionEngine.cs`
**Implementation**: `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookExecutionEngine.cs`

| Mode | Entry Point | Use Case |
|------|-------------|----------|
| **Batch** | `ExecuteBatchAsync()` | Document analysis via node graph |
| **Conversational** | `ExecuteConversationalAsync()` | Builder UI interactions |

```csharp
public enum ExecutionMode { Batch, Conversational }

public class ConversationContext {
    ConversationMessage[] History;
    string CurrentMessage;
    SessionState SessionState;
    Guid? PlaybookId;
}
```

### PlaybookOrchestrationService (Node-Based Execution)

**Interface**: `src/server/api/Sprk.Bff.Api/Services/Ai/IPlaybookOrchestrationService.cs`
**Implementation**: `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs`

Orchestrates node graph execution with parallel batching:

```
PlaybookRunRequest
  │
  ▼
Mode Detection: Legacy (no nodes) or NodeBased (has nodes)
  │
  ├── Legacy → delegates to IAnalysisOrchestrationService
  │
  └── NodeBased:
      1. Build ExecutionGraph (DAG from DependsOn arrays, Kahn's algorithm)
      2. Topological sort → execution batches (independent nodes grouped)
      3. FOR EACH batch:
         a. Resolve scopes per node based on NodeType:
            - AIAnalysis → full scopes (skills, knowledge, tools from N:N)
            - Output/Control/Workflow → empty scopes (no LLM calls)
         b. Determine ActionType from __actionType in ConfigJson
         c. Route to INodeExecutor via NodeExecutorRegistry[ActionType]
         d. Execute nodes in parallel (SemaphoreSlim throttle, configurable)
      4. Stream PlaybookStreamEvents per node (SSE)
      5. Store node outputs in PlaybookRunContext (ConcurrentDictionary)
      6. Template substitution: downstream nodes reference {{variable}} outputs
      7. Rate limit handling with exponential backoff per ADR-016
```

### PlaybookRunContext (State Container)

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookRunContext.cs`

Thread-safe state container for a single playbook execution:

```csharp
public class PlaybookRunContext {
    Guid RunId;
    Guid PlaybookId;
    Guid[] DocumentIds;
    string TenantId;
    PlaybookRunState State;  // Pending, Running, Completed, Failed, Cancelled
    DocumentContext? Document;
    CancellationToken CancellationToken;

    // Thread-safe node output storage (ConcurrentDictionary)
    IReadOnlyDictionary<string, NodeOutput> NodeOutputs;

    void StoreNodeOutput(NodeOutput output);
    NodeOutput? GetOutput(string variableName);
    PlaybookRunMetrics GetMetrics(int totalNodes);
}
```

### Streaming Events

```csharp
public enum PlaybookEventType {
    RunStarted, NodeStarted, NodeProgress,
    NodeCompleted, NodeSkipped, NodeFailed,
    RunCompleted, RunFailed, RunCancelled
}

public record PlaybookRunMetrics {
    int TotalNodes; int CompletedNodes;
    int FailedNodes; int SkippedNodes;
    int TotalTokensIn; int TotalTokensOut;
    TimeSpan Duration;
}
```

---

## Node Executor Framework

**Directory**: `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/`

| File | ActionType(s) | Backend |
|------|--------------|---------|
| `INodeExecutor.cs` | Interface + NodeType/ActionType enums | -- |
| `NodeExecutorRegistry.cs` | ActionType → INodeExecutor lookup | -- |
| `AiAnalysisNodeExecutor.cs` | AiAnalysis (0), AiCompletion (1), AiEmbedding (2) | Backend-flexible |
| `ConditionNodeExecutor.cs` | Condition (30) | Always Spaarke code |
| `CreateTaskNodeExecutor.cs` | CreateTask (20) | Always Spaarke code |
| `UpdateRecordNodeExecutor.cs` | UpdateRecord (22) | Always Spaarke code |
| `SendEmailNodeExecutor.cs` | SendEmail (21) | Always Spaarke code |
| `DeliverOutputNodeExecutor.cs` | DeliverOutput (40) | Always Spaarke code |
| `DeliverToIndexNodeExecutor.cs` | DeliverToIndex (41) | Always Spaarke code |

### DeliverToIndex Node (ActionType 41)

Enqueues a RAG semantic indexing job via Azure Service Bus. This enables playbooks to automatically index documents for semantic search after analysis.

**Use case**: Document Profile playbook includes a DeliverToIndex node so that uploaded documents are automatically indexed and become searchable via semantic search.

**Config properties** (in `sprk_configjson`):

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| `indexName` | string | Yes | `"knowledge"` | Target Azure AI Search index name |
| `indexSource` | string | No | `"document"` | What to index: `"document"` (full document) or `"field"` (specific field) |
| `source` | string | No | `"document"` | Alias for indexSource |

**Execution flow**:
1. Reads `indexName` and `indexSource` from node ConfigJson
2. Gets `DocumentContext` from `PlaybookRunContext` (includes `GraphDriveId`, `GraphItemId`)
3. Enqueues `RagIndexingJob` message to Azure Service Bus queue
4. Background worker (`RagIndexingJobHandler`) processes: extract text → chunk → embed → index to Azure AI Search
5. Sets `sprk_document.sprk_searchindexed = true` on the Dataverse record

**Important**: The `sprk_searchindexed` flag indicates the indexing job was **enqueued**, not that indexing completed successfully. To verify actual index presence, query Azure AI Search directly:

```bash
# Verify document chunks exist in index
curl -s "https://spaarke-search-dev.search.windows.net/indexes/spaarke-knowledge-index-v2/docs?api-version=2024-07-01&search=*&$filter=documentId eq '{document-guid}'&$count=true" \
  -H "api-key: {admin-key}" | jq '.["@odata.count"]'
```

**Client-side defaults** (set in `canvasStore.ts` `onDrop` handler):
```typescript
if (nodeType === "deliverToIndex") {
    baseData.indexName = "knowledge";
    baseData.indexSource = "document";
}
```

### DeliverOutput Node (ActionType 40)

Renders a Handlebars template with all previous node outputs. Used as the final "assembly" step in playbooks.

### UpdateRecord Node (ActionType 22)

Writes AI-extracted values to Dataverse entity fields with type coercion.

**Config format** (`fieldMappings` array in ConfigJson):

```json
{
  "entityLogicalName": "sprk_document",
  "recordId": "{{document.id}}",
  "fieldMappings": [
    { "field": "sprk_filesummary", "type": "string", "value": "{{output_aiAnalysis.text}}" },
    { "field": "sprk_filesummarystatus", "type": "choice", "value": "{{output_aiAnalysis.output.status}}", "options": { "pending": 100000000, "complete": 100000002 } },
    { "field": "sprk_isconfidential", "type": "boolean", "value": "{{output_aiAnalysis.output.isConfidential}}" }
  ]
}
```

**Supported types**: `string` (passthrough), `choice` (case-insensitive lookup in options map), `boolean` (truthy/falsy parsing), `number` (int/decimal parse).

**OData Web API PATCH** (not SDK): `PATCH /api/data/v9.2/{entitySetName}({recordId})` with coerced field values.

---

## Execution Flow (Batch Mode)

```
POST /api/ai/playbooks/{id}/execute (PlaybookRunEndpoints.cs)
  │
  ▼
PlaybookOrchestrationService.ExecutePlaybookAsync()
  │
  ├── Step 1: Load Playbook + Nodes (PlaybookService / NodeService)
  ├── Step 2: Detect mode (Legacy vs NodeBased — has sprk_playbooknode records?)
  ├── Step 3: Create PlaybookRunContext (RunId, DocumentIds, TenantId)
  ├── Step 4: Extract document text (TextExtractorService)
  │
  ├── Step 5: Build ExecutionGraph (Kahn's algorithm on DependsOn arrays)
  ├── Step 6: GetExecutionBatches() — group independent nodes into parallel batches
  │
  ├── FOR EACH batch (sequential between batches, parallel within):
  │   │
  │   ├── Step 7: FOR EACH node in batch (parallel, SemaphoreSlim throttle):
  │   │   │
  │   │   ├── Step 7a: Scope resolution (based on sprk_nodetype):
  │   │   │   ├── AIAnalysis → ResolveNodeScopesAsync (skills, knowledge, tools from N:N)
  │   │   │   └── Output/Control/Workflow → empty scopes (no LLM overhead)
  │   │   │
  │   │   ├── Step 7b: ActionType resolution (from __actionType in ConfigJson)
  │   │   │
  │   │   ├── Step 7c: Route to INodeExecutor via NodeExecutorRegistry[ActionType]:
  │   │   │   ├── AiAnalysisNodeExecutor (0-2): L1/L2/L3 knowledge → prompt → LLM → output
  │   │   │   ├── ConditionNodeExecutor (30): evaluate conditionJson
  │   │   │   ├── DeliverOutputNodeExecutor (40): Handlebars template render
  │   │   │   ├── DeliverToIndexNodeExecutor (41): enqueue Service Bus job
  │   │   │   ├── UpdateRecordNodeExecutor (22): OData PATCH with type coercion
  │   │   │   ├── CreateTask/SendEmail (20-21): Dataverse/Graph actions
  │   │   │   └── Wait (32): delay execution
  │   │   │
  │   │   ├── Step 7d: Store output (PlaybookRunContext.StoreNodeOutput)
  │   │   └── Step 7e: Stream event (PlaybookStreamEvent via SSE)
  │   │
  │   └── Check for failures — stop execution if node failed (unless continueOnError)
  │
  ├── Step 8: Store analysis output (sprk_analysisoutput.sprk_output_rtf)
  └── Step 9: Complete run (metrics, status, duration)
```

---

## Parallel Execution and Performance

**ExecutionGraph** (`ExecutionGraph.cs`) uses Kahn's algorithm to build a DAG from node `DependsOn` arrays:

```
GetExecutionBatches() produces:
  Batch 1: [nodes with in-degree 0]  ← no dependencies, run in parallel
  Batch 2: [nodes whose deps completed in batch 1]  ← run in parallel
  ...
  Batch N: [final nodes]
```

**Performance formula**: `Total time ≈ SUM(slowest node in each batch)`

**Parallel throttling**: `SemaphoreSlim(DefaultMaxParallelNodes)` limits concurrent execution within a batch. Default is 3, tuned based on Azure OpenAI TPM/RPM quota (ADR-016).

| Pattern | Structure | Batches | Performance |
|---------|-----------|---------|-------------|
| Fully sequential | A → B → C → D → Output | 5 | Slowest (sum of all) |
| Fully parallel | A,B,C,D → Output | 2 | Fastest (max of A-D + Output) |
| Partial deps | A → B; C,D → Output | 3 | Middle ground |

**Key rule**: Only add dependency edges where a node actually references `{{upstream.output}}` in its prompt. Unnecessary edges force sequential execution.

---

## TemplateEngine

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/TemplateEngine.cs`

Renders Handlebars.NET templates against a context built from previous node outputs. Used by UpdateRecord, DeliverOutput, and DeliverToIndex executors.

**Template syntax**:

```
{{output_aiAnalysis.text}}                   → AI summary text
{{output_aiAnalysis.output.documentType}}    → Structured JSON field
{{output_aiAnalysis.output.keyFindings}}     → Array → flattened to bullet list
{{document.id}}                              → Document record ID
```

**Context building**:
1. Start with `PreviousOutputs` dictionary (keyed by `output_{nodeLabel}`)
2. Each output contains `.text` (summary), `.output` (structured JSON), `.confidence`
3. `ConvertJsonElement` recursively converts JSON for Handlebars dot-navigation
4. `FlattenArrays` converts arrays to `"- item1\n- item2"` bullet strings

---

## Dual Output Paths

| Path | Data | Storage | Display |
|------|------|---------|---------|
| **Analysis Output** | `ToolResult.Summary` (text) | `sprk_analysisoutput.sprk_output_rtf` | AiSummaryPanel |
| **Document Fields** | AI structured output (JSON) | `sprk_document.*` fields via UpdateRecord node | Document form fields |

Document field writes happen **during** playbook execution (UpdateRecord node), not after. There is no post-execution field mapping step.

---

## Consumer Integration Pattern

```
┌─────────────────┐     SSE stream      ┌──────────────────┐     OData PATCH     ┌──────────┐
│  PCF / CodePage │ ──────────────────→  │  BFF API         │ ──────────────────→ │ Dataverse│
│  (useAiSummary) │ ← progress events   │  (Playbook exec) │   (UpdateRecord)    │ (fields) │
└─────────────────┘                      └──────────────────┘                     └──────────┘
```

**Rules**:
1. Component triggers playbook via SSE endpoint and displays streaming progress
2. All Dataverse field writes are server-side (UpdateRecord node via OData PATCH)
3. Component does NOT write AI output fields — no client-side callbacks to Dataverse
4. Component owns its own UX — file upload, metadata entry, progress display, error handling

---

## Knowledge-Augmented Execution (R3)

The execution pipeline retrieves tiered knowledge before calling the LLM:

```
AiAnalysisNodeExecutor.ExecuteAsync()
  │
  ├── L1: ReferenceRetrievalService.SearchReferencesAsync()
  │   └── Queries spaarke-rag-references (curated domain knowledge)
  │   └── Filtered by playbook's N:N knowledge source IDs
  │
  ├── L2: IRagService.SearchAsync() (optional, includeDocumentContext=true)
  │   └── Queries spaarke-knowledge-index-v2 (similar customer docs)
  │
  ├── L3: IRecordSearchService (optional, includeEntityContext=true)
  │   └── Queries spaarke-records-index (business entity metadata)
  │
  └── Merge: L1 + L2 + L3 → KnowledgeContext → Prompt assembly
```

**Configuration** via `KnowledgeRetrievalConfig` in action ConfigJson:

| Setting | Default | Description |
|---------|---------|-------------|
| `mode` | `auto` | `auto` (retrieve if sources linked), `always`, `never` |
| `topK` | 5 | Max reference chunks to retrieve (1-20) |
| `includeDocumentContext` | false | Enable L2 similar document retrieval |
| `includeEntityContext` | false | Enable L3 business entity context |

---

## Builder Agent (Conversational Mode)

**Directory**: `src/server/api/Sprk.Bff.Api/Services/Ai/Builder/`

| File | Purpose |
|------|---------|
| `BuilderAgentService.cs` | AI agent for conversational playbook building |
| `BuilderToolDefinitions.cs` | Tool definitions available to builder |
| `BuilderToolExecutor.cs` | Executes builder tool calls |
| `BuilderScopeImporter.cs` | Imports scopes into builder context |

Builder endpoint: `AiPlaybookBuilderEndpoints.cs`
System prompt: `Prompts/PlaybookBuilderSystemPrompt.cs`

---

## Playbooks as "Frontend"

Playbooks define **what** to do. The execution backend is pluggable:

```
Playbook (Spaarke Canvas)
├── AI Nodes → Backend-flexible:
│   ├── Option A: In-Process (current) — PlaybookOrchestrationService
│   ├── Option B: Agent Framework Workflow (future) — IChatClient/AIAgent
│   └── Option C: AI Foundry Agent Service (future) — Published agents
└── Workflow Nodes → Always Spaarke code:
    ├── CreateTask → Dataverse task creation
    ├── SendEmail → Email delivery
    ├── UpdateRecord → Dataverse PATCH
    ├── DeliverToIndex → RAG semantic indexing (Service Bus)
    ├── Condition → Branch evaluation
    └── DeliverOutput → Output assembly
```

A single playbook can mix backends per node. This is not an either/or decision.

---

## Nodes as Agents (Architectural Evolution)

Each playbook node conceptually maps to an AI agent:

| Playbook Concept | Agent Framework Equivalent |
|------------------|---------------------------|
| Node | `AIAgent` (via `IChatClient.AsAIAgent()`) |
| Action (system prompt) | Agent instructions |
| Skills (prompt fragments) | Agent behavioral modifiers |
| Knowledge (RAG context) | Agent context / grounding |
| Tool (handler) | `AIFunctionFactory.Create()` tool |
| Node execution order (edges) | Graph-based workflow |
| PlaybookRunContext | Shared conversation state |
| PlaybookOrchestrationService | Multi-agent orchestrator |

### Evolution Path

```
Current: PlaybookOrchestrationService → NodeExecutors → IOpenAiClient

Future:  PlaybookOrchestrationService → Agent Framework Graph Workflow
         (thin translation layer)          │
                                           ├── IChatClient (in-process)
                                           ├── AI Foundry Agent (hosted)
                                           └── Custom endpoint (external)
```

---

## Playbook Catalog (PB-001 through PB-010)

| ID | Name | Document Types | Complexity | Nodes |
|----|------|----------------|-----------|-------|
| PB-001 | Quick Document Review | ANY | Low | Classify → Extract → Summarize → Deliver |
| PB-002 | Full Contract Analysis | CONTRACT, AMENDMENT | High | Extract → (Clauses ‖ Risks) → Compare → Deliver |
| PB-003 | NDA Review | NDA | Medium | Extract → Scope → Risks → Deliver |
| PB-004 | Lease Review | LEASE | High | Extract → (Clauses ‖ Dates) → Risks → Deliver |
| PB-005 | Employment Contract | EMPLOYMENT | Medium | Summarize → Extract → Clauses → Risks |
| PB-006 | Invoice Validation | INVOICE | Low | Extract → Classify |
| PB-007 | SLA Analysis | SLA, CONTRACT | Medium | Summarize → Extract → Clauses |
| PB-008 | Due Diligence Review | ANY | Medium | Summarize → Classify → Extract → Risks |
| PB-009 | Compliance Review | POLICY, CONTRACT | Medium | Summarize → Extract → Clauses → Risks |
| PB-010 | Risk-Focused Scan | CONTRACT, NDA, LEASE | Low | Extract → Risks |

---

## Changelog

| Date | Version | Change |
|------|---------|--------|
| 2026-03-13 | 1.0 | Initial version — extracted from AI-ARCHITECTURE.md (v3.3). Added comprehensive DeliverToIndex documentation including config properties, execution flow, index verification, client-side defaults, and common pitfalls. Updated builder component tree to reflect Code Page UX changes (NodePropertiesDialog, fullscreen toggle). |
