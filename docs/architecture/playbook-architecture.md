# Playbook Architecture

> **Last Updated**: April 5, 2026
> **Purpose**: Technical reference for the Spaarke Playbook system — visual AI workflows, node execution, canvas data model, builder subsystem, scheduler
> **Parent**: [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md) (Spaarke AI platform overview)

---

## Overview

Playbooks are the primary AI composition pattern in Spaarke — visual node-based workflows stored as Dataverse records. They define **what** AI operations to perform and in what order. The execution backend is pluggable (currently in-process, future: Microsoft Agent Framework).

The system has three major subsystems: the **Playbook Builder** (visual canvas for authoring), the **Execution Engine** (node graph orchestration with parallel batching), and the **Scheduler** (background service for notification-mode playbooks). All three share the same node type system and scope resolution infrastructure.

**Entity**: `sprk_analysisplaybook`
**Canvas field**: `sprk_canvaslayoutjson` (serialized JSON of nodes and edges)
**Builder**: `src/client/code-pages/PlaybookBuilder/` (React 18 Code Page)

---

## Component Structure

| Component | Path | Responsibility |
|-----------|------|---------------|
| PlaybookExecutionEngine | `Services/Ai/PlaybookExecutionEngine.cs` | Dual-mode entry point (Batch vs Conversational) |
| PlaybookOrchestrationService | `Services/Ai/PlaybookOrchestrationService.cs` | Node graph execution with parallel batching |
| PlaybookRunContext | `Services/Ai/PlaybookRunContext.cs` | Thread-safe state container for a single run |
| NodeExecutionContext | `Services/Ai/NodeExecutionContext.cs` | Per-node execution context (scopes, outputs, document) |
| NodeExecutorRegistry | `Services/Ai/Nodes/NodeExecutorRegistry.cs` | ActionType-to-INodeExecutor lookup |
| INodeExecutor | `Services/Ai/Nodes/INodeExecutor.cs` | Executor interface + NodeType/ActionType enums |
| TemplateEngine | `Services/Ai/TemplateEngine.cs` | Handlebars.NET rendering for variable substitution |
| PlaybookSchedulerService | `Services/PlaybookSchedulerService.cs` | Background scheduler for notification playbooks |
| BuilderAgentService | `Services/Ai/Builder/BuilderAgentService.cs` | AI-assisted playbook construction (agentic loop) |
| BuilderToolExecutor | `Services/Ai/Builder/BuilderToolExecutor.cs` | Executes builder tool calls into canvas operations |
| BuilderToolDefinitions | `Services/Ai/Builder/BuilderToolDefinitions.cs` | OpenAI function calling tool schemas |
| PlaybookBuilder (Code Page) | `src/client/code-pages/PlaybookBuilder/` | Visual canvas (React 18 + @xyflow/react v12) |
| PlaybookBuilderHost (PCF) | `src/client/pcf/PlaybookBuilderHost/` | Legacy field-bound control (React 16/17) |

All paths above are relative to `src/server/api/Sprk.Bff.Api/` unless noted otherwise.

---

## Three-Level Node Type System

Nodes use three type concepts at different layers:

| Level | Name | Where Stored | Purpose | Example |
|-------|------|-------------|---------|---------|
| **Canvas Type** | `PlaybookNodeType` | React Flow `node.data.type` | React component selection | `"aiAnalysis"` |
| **Dataverse NodeType** | `sprk_nodetype` | `sprk_playbooknode` OptionSet | Coarse scope resolution | `AIAnalysis (100000000)` |
| **ActionType** | `__actionType` in ConfigJson | `sprk_playbooknode.sprk_configjson` | Fine-grained executor dispatch | `AiAnalysis (0)` |

### Canvas Types (9 node types -- drag-and-drop palette items)

| Canvas Type | Dataverse NodeType | ActionType | Backend |
|------------|-------------------|------------|---------|
| `start` | -- | Start (33) | Always Spaarke code |
| `aiAnalysis` | AIAnalysis (100000000) | AiAnalysis (0) | Backend-flexible |
| `aiCompletion` | AIAnalysis (100000000) | AiCompletion (1) | Backend-flexible |
| `condition` | Control (100000002) | Condition (30) | Always Spaarke code |
| `deliverOutput` | Output (100000001) | DeliverOutput (40) | Always Spaarke code |
| `deliverToIndex` | Output (100000001) | DeliverToIndex (41) | Always Spaarke code |
| `updateRecord` | Workflow (100000003) | UpdateRecord (22) | Always Spaarke code |
| `createTask` | Workflow (100000003) | CreateTask (20) | Always Spaarke code |
| `sendEmail` | Workflow (100000003) | SendEmail (21) | Always Spaarke code |

### Dataverse NodeType (4 coarse categories for scope resolution)

| Value | Category | Scope Resolution |
|-------|----------|-----------------|
| `AIAnalysis = 100_000_000` | AI-powered | Full scopes (skills, knowledge, tools from N:N) |
| `Output = 100_000_001` | Delivery | No scopes -- assembles previous outputs |
| `Control = 100_000_002` | Flow control | No scopes -- condition/wait logic |
| `Workflow = 100_000_003` | Dataverse/email | No scopes -- Dataverse/Graph actions |

### ActionType Enum (18 values, 9 with executors)

The full enum includes reserved values for future use. Currently implemented executors cover 11 ActionTypes:

| Range | Category | Implemented | Reserved |
|-------|----------|------------|----------|
| 0-2 | AI | AiAnalysis (0), AiCompletion (1), AiEmbedding (2) | -- |
| 10-12 | Processing | -- | RuleEngine (10), Calculation (11), DataTransform (12) |
| 20-24 | Workflow | CreateTask (20), SendEmail (21), UpdateRecord (22) | CallWebhook (23), SendTeamsMessage (24) |
| 30-33 | Control | Condition (30), Start (33) | Parallel (31), Wait (32) |
| 40-41 | Output | DeliverOutput (40), DeliverToIndex (41) | -- |
| 50-51 | Data/Notify | CreateNotification (50), QueryDataverse (51) | -- |

**Key rule**: NodeType determines scope resolution strategy. ActionType determines which `INodeExecutor` runs. Both must stay in sync -- missing entries in the canvas-to-Dataverse mapping cause fallthrough to AIAnalysis, which triggers incorrect scope resolution and "requires an Action" errors.

**Mapping files** (must stay in sync):
- Client-side: `src/client/code-pages/PlaybookBuilder/src/services/playbookNodeSync.ts`
- Server-side: `src/server/api/Sprk.Bff.Api/Services/Ai/NodeService.cs`

---

## Canvas-to-Dataverse Sync

**File**: `src/client/code-pages/PlaybookBuilder/src/services/playbookNodeSync.ts`

- Auto-save: 30-second debounce after canvas changes
- Manual save: Ctrl+S
- On save: Canvas JSON written to `sprk_canvaslayoutjson`, then `syncNodesToDataverse()`:
  1. Queries existing `sprk_playbooknode` records
  2. Computes execution order via Kahn's topological sort of canvas edges
  3. Creates/updates/deletes node records with `sprk_nodetype` + `__actionType` in ConfigJson
  4. Writes `sprk_dependsonjson` with upstream node GUIDs
  5. Manages N:N relationships (skills, knowledge, tools) via associate/disassociate

---

## Playbook Builder (Code Page)

**Path**: `src/client/code-pages/PlaybookBuilder/`
**Stack**: React 18, @xyflow/react v12, Fluent UI v9, Zustand
**Deployment**: Inline HTML web resource (`sprk_playbookbuilder.html`)

The builder provides a visual canvas where users drag-and-drop node types from the NodePalette, configure properties in the NodePropertiesDialog (per-node modal with ScopeSelector, ModelSelector, ActionSelector, and type-specific forms), connect nodes with edges (including conditional true/false branches), and can use the AI Assistant Modal for conversational playbook construction.

### AI Builder Subsystem

**Directory**: `src/server/api/Sprk.Bff.Api/Services/Ai/Builder/`

The AI Assistant Modal delegates to the **BuilderAgentService**, which implements an agentic loop (max 10 tool-call rounds) using GPT-4o with function calling. The agent receives the current canvas state, available scopes, and user request, then manipulates the canvas through structured tool calls.

| Component | Responsibility |
|-----------|---------------|
| `BuilderAgentService` | Agentic conversation loop with function calling |
| `BuilderToolDefinitions` | 10 OpenAI tool schemas (canvas + scope operations) |
| `BuilderToolExecutor` | Converts tool calls into CanvasOperation patches |
| `BuilderScopeImporter` | Imports scope records for link/search/create operations |

**Builder Tools** (2 categories):
- **Canvas Operations**: `add_node`, `remove_node`, `create_edge`, `update_node_config`, `auto_layout`, `validate_canvas`, `configure_prompt_schema`
- **Scope Operations**: `link_scope`, `search_scopes`, `create_scope`

### PlaybookBuilderHost PCF Control (Legacy R4)

**Path**: `src/client/pcf/PlaybookBuilderHost/`
**Stack**: React 16/17, react-flow-renderer v10, Fluent UI v9
**Status**: Still maintained; mirrors Code Page structure. Used as field-bound control on `sprk_analysisplaybook` form.

---

## Execution Engine

### PlaybookExecutionEngine (Dual Mode)

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookExecutionEngine.cs`

Top-level entry point supporting two execution modes:

| Mode | Method | Use Case |
|------|--------|----------|
| **Batch** | `ExecuteBatchAsync()` | Document analysis via node graph (delegates to PlaybookOrchestrationService) |
| **Conversational** | `ExecuteConversationalAsync()` | Builder UI interactions (delegates to IAiPlaybookBuilderService) |

Mode detection: `hasDocuments` -> Batch; `hasCanvasState && !hasDocuments` -> Conversational; default -> Conversational.

### PlaybookOrchestrationService (Node-Based Execution)

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs`

Orchestrates node graph execution with parallel batching. Supports both interactive (OBO via HttpContext) and app-only (background) execution paths.

```
PlaybookRunRequest
  |
  v
Mode Detection: Legacy (no nodes) or NodeBased (has sprk_playbooknode records)
  |
  +-- Legacy -> delegates to IAnalysisOrchestrationService
  |
  +-- NodeBased:
      1. Build ExecutionGraph (DAG from DependsOn arrays, Kahn's algorithm)
      2. Topological sort -> execution batches (independent nodes grouped)
      3. FOR EACH batch (sequential between batches, parallel within):
         a. Resolve scopes per node based on NodeType
            - AIAnalysis -> full scopes (skills, knowledge, tools from N:N)
            - Output/Control/Workflow -> empty scopes (no LLM overhead)
         b. Determine ActionType from __actionType in ConfigJson
         c. Route to INodeExecutor via NodeExecutorRegistry[ActionType]
         d. Execute nodes in parallel (SemaphoreSlim throttle)
      4. Stream PlaybookStreamEvents per node (SSE via Channel)
      5. Store node outputs in PlaybookRunContext (ConcurrentDictionary)
      6. Template substitution: downstream nodes reference {{variable}} outputs
      7. Rate limit handling with exponential backoff (ADR-016)
```

### PlaybookRunContext (State Container)

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookRunContext.cs`

Thread-safe state container for a single playbook execution. Two constructors: one for interactive runs (with HttpContext for OBO), one for app-only runs (with tenant ID for background execution). Uses `ConcurrentDictionary<string, NodeOutput>` for node output storage since nodes may complete in parallel within a batch.

Key state: `RunId`, `PlaybookId`, `DocumentIds`, `TenantId`, `Document` (shared DocumentContext), `NodeOutputs`, `State` (Pending/Running/Completed/Failed/Cancelled).

### Parallel Execution and Performance

**Key design decision**: Nodes with no declared dependencies run in parallel within the same batch. The performance formula is `Total time ~ SUM(slowest node in each batch)`.

**Throttle**: `SemaphoreSlim(DefaultMaxParallelNodes)` -- default 3, tuned to Azure OpenAI TPM/RPM quota. Rate limit retries: max 3 with exponential backoff starting at 2 seconds.

**Key rule**: Only add dependency edges where a node actually references `{{upstream.output}}` in its prompt. Unnecessary edges force sequential execution.

| Pattern | Structure | Batches | Performance |
|---------|-----------|---------|-------------|
| Fully sequential | A -> B -> C -> D -> Output | 5 | Slowest |
| Fully parallel | A,B,C,D -> Output | 2 | Fastest |
| Partial deps | A -> B; C,D -> Output | 3 | Middle ground |

---

## Node Executor Framework

**Directory**: `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/`

Each ActionType maps to exactly one `INodeExecutor`. Executors register via DI (`IEnumerable<INodeExecutor>` constructor injection) and are indexed by `SupportedActionTypes` in `NodeExecutorRegistry`.

Every executor implements three methods: `SupportedActionTypes` (which ActionTypes it handles), `Validate()` (fail-fast before execution), and `ExecuteAsync()` (produce `NodeOutput`).

| File | ActionType(s) | Backend | Description |
|------|--------------|---------|-------------|
| `AiAnalysisNodeExecutor.cs` | AiAnalysis (0) | Backend-flexible | Bridges to IAnalysisToolHandler pipeline with L1/L2/L3 knowledge retrieval |
| `ConditionNodeExecutor.cs` | Condition (30) | Spaarke code | JSON condition expression evaluation (eq/ne/gt/lt/contains/and/or/not) |
| `CreateTaskNodeExecutor.cs` | CreateTask (20) | Spaarke code | Creates Dataverse task records via OData Web API |
| `UpdateRecordNodeExecutor.cs` | UpdateRecord (22) | Spaarke code | Writes AI-extracted values to Dataverse fields with typed coercion |
| `SendEmailNodeExecutor.cs` | SendEmail (21) | Spaarke code | Sends email via Microsoft Graph (OBO authentication) |
| `DeliverOutputNodeExecutor.cs` | DeliverOutput (40) | Spaarke code | Renders Handlebars template with all previous node outputs |
| `DeliverToIndexNodeExecutor.cs` | DeliverToIndex (41) | Spaarke code | Enqueues RAG semantic indexing job via Service Bus |
| `CreateNotificationNodeExecutor.cs` | CreateNotification (50) | Spaarke code | Creates Dataverse appnotification records with idempotency check |
| `QueryDataverseNodeExecutor.cs` | QueryDataverse (51) | Spaarke code | Executes FetchXML queries with date/user variable resolution |

### AiAnalysisNodeExecutor (ActionType 0)

Bridges node-based orchestration to the existing `IAnalysisToolHandler` pipeline. Requires document context and a configured tool with a handler class. Performs 3-tier knowledge retrieval before LLM call:
- **L1**: `ReferenceRetrievalService` -- curated domain knowledge from `spaarke-rag-references`
- **L2**: `IRagService` -- similar customer docs from `spaarke-knowledge-index-v2` (optional)
- **L3**: `IRecordSearchService` -- entity metadata from `spaarke-records-index` (optional)

Supports per-token streaming via `IStreamingAnalysisToolHandler` and resolves `$choices` lookup references for constrained decoding.

### ConditionNodeExecutor (ActionType 30)

Evaluates JSON condition expressions against previous node outputs. Supports comparison operators (`eq`, `ne`, `gt`, `lt`, `gte`, `lte`, `contains`, `startsWith`, `endsWith`, `exists`) and logical operators (`and`, `or`, `not` with nesting). Returns `ConditionResult` with `SelectedBranch`.

### UpdateRecordNodeExecutor (ActionType 22)

Writes AI-extracted values to Dataverse fields with typed coercion. Two config formats: **new** (typed `fieldMappings` with `type`/`options`) and **legacy** (flat dictionary). Types: `string`, `choice` (case-insensitive options map), `boolean`, `number`. Uses OData PATCH (not SDK) via `IFieldMappingDataverseService`.

### DeliverToIndexNodeExecutor (ActionType 41)

Enqueues RAG indexing job via `JobSubmissionService` (Service Bus). Background `RagIndexingJobHandler` processes: extract -> chunk -> embed -> index. **Important**: `sprk_searchindexed = true` means enqueued, not completed.

### CreateNotificationNodeExecutor (ActionType 50)

Creates Dataverse `appnotification` records with **idempotency check** (skips if unread duplicate exists for same user + regarding + category). Supports **iterate-items mode** for batch notifications from upstream QueryDataverse results. Priority: Informational (100M), Important (200M, default), Urgent (300M).

### QueryDataverseNodeExecutor (ActionType 51)

Executes FetchXML via OData Web API with variable resolution: `{{todayUtc}}`, `{{dueSoonWindowUtc}}` (+7d), `{{timeWindowStartUtc}}` (-24h), `{{run.userId}}`. Replaces `operator="eq-userid"` with resolved user ID.

### SendEmailNodeExecutor (ActionType 21)

Sends email via Microsoft Graph (OBO). Config: `to`, `cc`, `subject`, `body`, `isHtml` -- all template-enabled.

### CreateTaskNodeExecutor (ActionType 20)

Creates Dataverse task records via OData Web API. Config: `subject`, `description`, `regardingObjectId`, `ownerId`, `dueDate` -- all template-enabled.

---

## TemplateEngine

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/TemplateEngine.cs`

Renders Handlebars.NET templates against a context built from previous node outputs. Used by UpdateRecord, DeliverOutput, DeliverToIndex, Condition, CreateNotification, QueryDataverse, CreateTask, and SendEmail executors.

**Template syntax**:
```
{{output_aiAnalysis.text}}                   -> AI summary text
{{output_aiAnalysis.output.documentType}}    -> Structured JSON field
{{output_aiAnalysis.output.keyFindings}}     -> Array -> flattened to bullet list
{{document.id}}                              -> Document record ID
{{run.userId}}                               -> Executing user's systemuserid
```

Arrays are automatically flattened to `"- item1\n- item2"` bullet strings for Dataverse text fields.

---

## Dual Output Paths

| Path | Data | Storage | Display |
|------|------|---------|---------|
| **Analysis Output** | `ToolResult.Summary` (text) | `sprk_analysisoutput.sprk_output_rtf` | AiSummaryPanel |
| **Document Fields** | AI structured output (JSON) | `sprk_document.*` fields via UpdateRecord node | Document form fields |

Document field writes happen **during** playbook execution (UpdateRecord node), not after. There is no post-execution field mapping step.

---

## Playbook Scheduler

**File**: `src/server/api/Sprk.Bff.Api/Services/PlaybookSchedulerService.cs`

Background service (`BackgroundService` per ADR-001) that periodically executes notification-mode playbooks. Queries `sprk_analysisplaybook` where `sprk_playbooktype = Notification (2)`, reads schedule config from `sprk_configjson`, and processes users in parallel.

| Setting | Value |
|---------|-------|
| Tick interval | 1 hour (default) |
| Max parallel users per playbook | 5 (`Parallel.ForEachAsync`) |
| Error retry delay | 5 minutes |
| Last-run tracking | In-memory `ConcurrentDictionary`, seeded from Dataverse `sprk_lastrundate` |
| Execution mode | App-only (no HttpContext) via `ExecuteAppOnlyAsync()` |

The scheduler sets `UserId` on `NodeExecutionContext` so that QueryDataverse and CreateNotification nodes can resolve per-user data.

---

## Consumer Integration Pattern

```
+-------------------+     SSE stream      +------------------+     OData PATCH     +----------+
|  PCF / CodePage   | ------------------>  |  BFF API         | -----------------> | Dataverse|
|  (useAiSummary)   | <-- progress events  |  (Playbook exec) |   (UpdateRecord)   | (fields) |
+-------------------+                      +------------------+                    +----------+
```

**Rules**:
1. Component triggers playbook via SSE endpoint and displays streaming progress
2. All Dataverse field writes are server-side (UpdateRecord node via OData PATCH)
3. Component does NOT write AI output fields -- no client-side callbacks to Dataverse
4. Component owns its own UX -- file upload, metadata entry, progress display, error handling

---

## Playbooks as "Frontend"

Playbooks define **what** to do. The execution backend is pluggable:

```
Playbook (Spaarke Canvas)
+-- AI Nodes -> Backend-flexible:
|   +-- Option A: In-Process (current) -- PlaybookOrchestrationService
|   +-- Option B: Agent Framework Workflow (future) -- IChatClient/AIAgent
|   +-- Option C: AI Foundry Agent Service (future) -- Published agents
+-- Workflow Nodes -> Always Spaarke code:
|   +-- CreateTask, SendEmail, UpdateRecord
|   +-- DeliverToIndex (RAG semantic indexing via Service Bus)
|   +-- Condition (branch evaluation)
|   +-- DeliverOutput (output assembly)
+-- Data/Notify Nodes -> Always Spaarke code:
    +-- QueryDataverse (FetchXML queries)
    +-- CreateNotification (appnotification with idempotency)
```

A single playbook can mix backends per node. This is not an either/or decision.

---

## Nodes as Agents (Architectural Evolution)

Each playbook node maps conceptually to an AI agent: Node -> `AIAgent`, Action -> agent instructions, Skills -> behavioral modifiers, Knowledge -> grounding context, Tool -> `AIFunctionFactory.Create()`, edges -> graph-based workflow. The PlaybookOrchestrationService evolves toward a "workflow compiler" that translates canvas definitions into Agent Framework workflows. The scope library stays as Spaarke IP; the execution engine becomes a translation layer.

---

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Depends on | AI Tool Handlers | `IToolHandlerRegistry` / `IAnalysisToolHandler` | AiAnalysisNodeExecutor bridges to existing tool pipeline |
| Depends on | Scope Resolution | `IScopeResolverService` | Resolves skills, knowledge, tools from N:N relationships |
| Depends on | RAG Services | `ReferenceRetrievalService`, `IRagService`, `IRecordSearchService` | L1/L2/L3 knowledge retrieval |
| Depends on | Job Queue | `JobSubmissionService` (Azure Service Bus) | DeliverToIndex enqueues indexing jobs |
| Depends on | Microsoft Graph | `IGraphClientFactory` | SendEmail executor uses OBO |
| Depends on | Dataverse Web API | `IHttpClientFactory("DataverseApi")` | UpdateRecord, CreateTask, CreateNotification, QueryDataverse |
| Consumed by | PCF Controls | SSE streaming endpoints | `useAiSummary` hook triggers and displays playbook runs |
| Consumed by | Code Pages | SSE streaming endpoints | Document viewers, workspace pages |
| Consumed by | Agent Endpoints | `PlaybookInvocationService` | Agent Framework invokes playbooks |
| Consumed by | Background Services | `PlaybookSchedulerService` | Notification playbooks on schedule |

---

## Known Pitfalls

1. **Canvas-to-Dataverse mapping drift**: If a new canvas node type is added to the builder without updating both `playbookNodeSync.ts` (client) and `NodeService.cs` (server), the node falls through to `AIAnalysis` default, causing scope resolution errors and misleading "requires an Action" messages.

2. **AiAnalysisNodeExecutor is Singleton but IToolHandlerRegistry is Scoped**: The executor uses `IServiceProvider.CreateScope()` per execution to resolve the scoped registry. Forgetting this pattern when adding new executors that depend on scoped services will cause DI resolution failures.

3. **DeliverToIndex "indexed" flag is misleading**: `sprk_searchindexed = true` means the job was **enqueued**, not that indexing completed. To verify actual index presence, query Azure AI Search directly.

4. **Unnecessary dependency edges kill parallelism**: Every edge between nodes forces sequential execution. Only add edges where a node actually references `{{upstream.output}}` in its prompt or config. Audit playbooks with unexpectedly slow execution for unnecessary edges.

5. **Template variable resolution timing**: Template variables resolve at execution time using outputs from completed nodes. If a referenced variable's node failed or was skipped, the template renders the raw `{{variable}}` string. Condition nodes should guard downstream template usage.

6. **Scheduler runs all notification playbooks for all users**: The opt-out model means a new notification playbook is immediately active for every user. There is no per-user opt-in -- plan accordingly when deploying new notification playbooks.

---

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Execution engine location | In-process BackgroundService | No Azure Functions per ADR-001; keeps deployment simple | ADR-001 |
| Node executor dispatch | Registry pattern with ActionType enum | DI minimalism per ADR-010; fast lookup, extensible | ADR-010 |
| AI architecture | Tool framework extending BFF | No separate AI service per ADR-013 | ADR-013 |
| Rate limiting | Exponential backoff with SemaphoreSlim throttle | Azure OpenAI quota management | ADR-016 |
| Template engine | Handlebars.NET | Familiar syntax, supports nested properties, array flattening | -- |
| Dual execution mode | Batch + Conversational in single engine | Builder needs interactive; analysis needs streaming | -- |

---

## Constraints

- **MUST** keep client-side and server-side node type mappings in sync (`playbookNodeSync.ts` and `NodeService.cs`)
- **MUST** use OData Web API (not Dataverse SDK) for UpdateRecord, CreateTask, CreateNotification, and QueryDataverse to avoid serialization issues
- **MUST NOT** add dependency edges between nodes unless one node's output is actually referenced by the other
- **MUST** use `IServiceProvider.CreateScope()` in Singleton executors when resolving Scoped services
- **MUST** keep Workflow/Control/Output nodes free of scope resolution overhead (no LLM calls)
- **MUST NOT** exceed `DefaultMaxParallelNodes` (3) without validating against Azure OpenAI TPM/RPM quota

---

## Related

- [Pattern: analysis scopes](../../.claude/patterns/ai/analysis-scopes.md) -- scope resolution code entry points
- [Pattern: streaming endpoints](../../.claude/patterns/ai/streaming-endpoints.md) -- SSE streaming patterns
- [ADR-001](../../.claude/adr/ADR-001-minimal-api.md) -- Minimal API + BackgroundService (no Azure Functions)
- [ADR-010](../../.claude/adr/ADR-010-di-minimalism.md) -- DI minimalism
- [ADR-013](../../.claude/adr/ADR-013-ai-architecture.md) -- AI Architecture (tool framework)
- [AI Architecture](AI-ARCHITECTURE.md) -- parent document covering full AI platform

---

## Changelog

| Date | Version | Change |
|------|---------|--------|
| 2026-04-05 | 2.0 | Restored depth: execution engine dual mode, all 9 node executors (including CreateNotification/QueryDataverse), builder subsystem, scheduler, integration points, known pitfalls, constraints |
| 2026-03-13 | 1.0 | Initial version -- extracted from AI-ARCHITECTURE.md (v3.3). Added DeliverToIndex node documentation. |
