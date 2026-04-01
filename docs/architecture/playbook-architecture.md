# Playbook Architecture

> **Version**: 1.0
> **Last Updated**: March 13, 2026
> **Audience**: Claude Code, AI agents, engineers
> **Purpose**: Technical reference for the Spaarke Playbook system — visual AI workflows, node execution, canvas data model
> **Parent**: [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md) (Spaarke AI platform overview)

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

### Canvas Types (9 node types — drag-and-drop palette items)

| Canvas Type | Dataverse NodeType | ActionType | Backend |
|------------|-------------------|------------|---------|
| `start` | — | — | Always Spaarke code |
| `aiAnalysis` | AIAnalysis (100000000) | AiAnalysis (0) | Backend-flexible |
| `aiCompletion` | AIAnalysis (100000000) | AiCompletion (1) | Backend-flexible |
| `condition` | Control (100000002) | Condition (30) | Always Spaarke code |
| `deliverOutput` | Output (100000001) | DeliverOutput (40) | Always Spaarke code |
| `deliverToIndex` | Output (100000001) | DeliverToIndex (41) | Always Spaarke code |
| `updateRecord` | Workflow (100000003) | UpdateRecord (22) | Always Spaarke code |
| `createTask` | Workflow (100000003) | CreateTask (20) | Always Spaarke code |
| `sendEmail` | Workflow (100000003) | SendEmail (21) | Always Spaarke code |

### Dataverse NodeType (4 coarse categories for scope resolution)

```
AIAnalysis  = 100_000_000  → Full scope resolution (skills, knowledge, tools from N:N)
Output      = 100_000_001  → No scopes — assembles previous outputs
Control     = 100_000_002  → No scopes — flow control
Workflow    = 100_000_003  → No scopes — Dataverse/email actions
```

**Key rule**: NodeType determines scope resolution strategy. ActionType determines which `INodeExecutor` runs. Both must stay in sync — missing entries in the canvas-to-Dataverse mapping cause fallthrough to AIAnalysis, which triggers incorrect scope resolution and "requires an Action" errors.

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

The builder provides a visual canvas where users drag-and-drop node types, configure properties in the PropertiesPanel, connect nodes with edges (including conditional true/false branches), and can use the AI Assistant Modal for conversational playbook construction.

### PlaybookBuilderHost PCF Control (Legacy R4)

**Path**: `src/client/pcf/PlaybookBuilderHost/`
**Stack**: React 16/17, react-flow-renderer v10, Fluent UI v9
**Status**: Still maintained; mirrors Code Page structure. Used as field-bound control on `sprk_analysisplaybook` form.

---

## Execution Engine

### PlaybookOrchestrationService (Node-Based Execution)

Orchestrates node graph execution with parallel batching:

```
PlaybookRunRequest
  │
  ▼
Mode Detection: Legacy (no nodes) or NodeBased (has sprk_playbooknode records)
  │
  ├── Legacy → delegates to IAnalysisOrchestrationService
  │
  └── NodeBased:
      1. Build ExecutionGraph (DAG from DependsOn arrays, Kahn's algorithm)
      2. Topological sort → execution batches (independent nodes grouped)
      3. FOR EACH batch (sequential between batches, parallel within):
         a. Resolve scopes per node based on NodeType
         b. Determine ActionType from __actionType in ConfigJson
         c. Route to INodeExecutor via NodeExecutorRegistry[ActionType]
         d. Execute nodes in parallel (SemaphoreSlim throttle)
      4. Stream PlaybookStreamEvents per node (SSE)
      5. Store node outputs in PlaybookRunContext (ConcurrentDictionary)
      6. Template substitution: downstream nodes reference {{variable}} outputs
      7. Rate limit handling with exponential backoff (ADR-016)
```

### Parallel Execution and Performance

**Key design decision**: Nodes with no declared dependencies run in parallel within the same batch. The performance formula is `Total time ≈ SUM(slowest node in each batch)`.

**Throttle**: `SemaphoreSlim(DefaultMaxParallelNodes)` — default 3, tuned to Azure OpenAI TPM/RPM quota.

**Key rule**: Only add dependency edges where a node actually references `{{upstream.output}}` in its prompt. Unnecessary edges force sequential execution.

| Pattern | Structure | Batches | Performance |
|---------|-----------|---------|-------------|
| Fully sequential | A → B → C → D → Output | 5 | Slowest |
| Fully parallel | A,B,C,D → Output | 2 | Fastest |
| Partial deps | A → B; C,D → Output | 3 | Middle ground |

---

## Node Executor Framework

Each ActionType maps to exactly one `INodeExecutor`:

| ActionType | Executor | Backend |
|------------|---------|---------|
| AiAnalysis (0), AiCompletion (1), AiEmbedding (2) | `AiAnalysisNodeExecutor` | Backend-flexible |
| Condition (30) | `ConditionNodeExecutor` | Always Spaarke code |
| CreateTask (20) | `CreateTaskNodeExecutor` | Always Spaarke code |
| UpdateRecord (22) | `UpdateRecordNodeExecutor` | Always Spaarke code |
| SendEmail (21) | `SendEmailNodeExecutor` | Always Spaarke code |
| DeliverOutput (40) | `DeliverOutputNodeExecutor` | Always Spaarke code |
| DeliverToIndex (41) | `DeliverToIndexNodeExecutor` | Always Spaarke code |

### DeliverToIndex Node (ActionType 41)

Enqueues a RAG semantic indexing job via Azure Service Bus after playbook analysis. Enables the Document Profile playbook to automatically index uploaded documents for semantic search.

**Important**: The `sprk_searchindexed` flag on the document record indicates the indexing job was **enqueued**, not that indexing completed successfully.

### UpdateRecord Node (ActionType 22)

Writes AI-extracted values to Dataverse entity fields using typed coercion.

**Supported types**: `string` (passthrough), `choice` (case-insensitive options map), `boolean` (truthy/falsy), `number` (int/decimal).

**OData Web API PATCH** (not SDK): Bypasses the Dataverse SDK to avoid `JsonElement` serialization issues with typed attributes.

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

---

## TemplateEngine

Renders Handlebars.NET templates against a context built from previous node outputs. Used by UpdateRecord, DeliverOutput, and DeliverToIndex executors.

**Template syntax**:
```
{{output_aiAnalysis.text}}                   → AI summary text
{{output_aiAnalysis.output.documentType}}    → Structured JSON field
{{output_aiAnalysis.output.keyFindings}}     → Array → flattened to bullet list
{{document.id}}                              → Document record ID
```

Arrays are automatically flattened to `"- item1\n- item2"` bullet strings for Dataverse text fields.

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
    ├── CreateTask, SendEmail, UpdateRecord
    ├── DeliverToIndex (RAG semantic indexing via Service Bus)
    ├── Condition (branch evaluation)
    └── DeliverOutput (output assembly)
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

The PlaybookOrchestrationService evolves from a full execution engine to a thin "workflow compiler" that translates playbook canvas definitions into Agent Framework graph-based workflows. The scope library stays as Spaarke IP; the execution engine becomes a translation layer.

---

## Changelog

| Date | Version | Change |
|------|---------|--------|
| 2026-03-13 | 1.0 | Initial version — extracted from AI-ARCHITECTURE.md (v3.3). Added DeliverToIndex node documentation. |
