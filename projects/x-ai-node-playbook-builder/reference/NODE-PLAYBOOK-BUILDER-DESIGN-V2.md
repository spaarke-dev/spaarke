# Node-Based Playbook Builder - Research Design V2

> **Version**: 2.0
> **Created**: January 7, 2026
> **Status**: Research Design (Pre-Specification)
> **Author**: Spaarke AI Architecture Team
> **Prerequisites**: R4 Complete (Playbook Scope System), R5 Design (RAG Pipeline)
> **Related**: [GPT 5.2 Original Concept](./Unified_Node_Playbook_and_Workflow_Design.md) (reference only)

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Objectives and Requirements](#2-objectives-and-requirements)
3. [Architecture Principles](#3-architecture-principles)
4. [Conceptual Model](#4-conceptual-model)
5. [Dataverse Schema](#5-dataverse-schema)
6. [Component Architecture](#6-component-architecture)
7. [API Specification](#7-api-specification)
8. [Execution Engine](#8-execution-engine)
9. [Frontend Architecture](#9-frontend-architecture)
10. [Migration Strategy](#10-migration-strategy)
11. [Implementation Phases](#11-implementation-phases)
12. [ADR Compliance](#12-adr-compliance)
13. [Open Questions](#13-open-questions)

---

## 1. Executive Summary

### 1.1 Purpose

Transform Spaarke's current single-action playbook model into a **multi-node orchestration platform** that enables:

- Chaining multiple AI analysis actions with data flow between them
- Mixing AI (probabilistic) and workflow (deterministic) actions in a single playbook
- Visual drag-and-drop playbook construction for business analysts
- Flexible output types beyond documents (tasks, emails, record updates)
- Per-node AI model selection for optimization

### 1.2 Design Philosophy

**Extend, don't replace.** This design adds an orchestration layer on top of the existing analysis pipeline. The core components (`IAnalysisToolHandler`, `OpenAiClient`, `ScopeResolverService`, etc.) remain unchanged and are reused by the new orchestration layer.

### 1.3 Key Architectural Decision

The **Playbook** remains the unified container for both AI analysis and deterministic workflows. We introduce a **Node** entity that wraps an Action with its scopes, enabling multiple nodes per playbook with explicit execution ordering and data flow.

**Current Model**:
```
Playbook → (1) Action → (N) Skills, Tools, Knowledge
```

**New Model**:
```
Playbook → (N) Nodes → each Node = Action + Skills + Tools + Knowledge
```

## 2. Objectives and Requirements

### 2.1 Business Objectives

| ID | Objective | Success Criteria |
|----|-----------|------------------|
| O1 | Visual playbook builder for business analysts | Non-developers can create multi-step playbooks via drag-and-drop |
| O2 | Multi-action chaining | Complex outcomes (e.g., "Summarize Contract") decomposed into sub-actions with data flow |
| O3 | Hybrid AI + workflow | Single playbook can mix AI analysis with deterministic actions (rules, tasks, emails) |
| O4 | Flexible AI model selection | Different models per node based on task requirements and cost optimization |
| O5 | Multiple output types | Playbook execution can create tasks, send emails, update records—not just documents |

### 2.2 Technical Requirements

| ID | Requirement | Constraint |
|----|-------------|------------|
| T1 | Backward compatibility | Existing playbooks continue to work unchanged |
| T2 | ADR compliance | Must comply with ADR-001, ADR-008, ADR-010, ADR-013, ADR-022 |
| T3 | React 16 compatibility | PCF controls limited to React 16 APIs (ADR-022) |
| T4 | Minimal reengineering | Reuse existing analysis pipeline, add orchestration layer |
| T5 | Incremental deployment | Each phase delivers usable functionality |
| T6 | DI budget compliance | Stay within ≤15 non-framework DI registrations (ADR-010) |

### 2.3 User Personas

| Persona | Role | Primary Needs |
|---------|------|---------------|
| **Business AI Analyst** | Creates and configures playbooks | Visual builder, no coding, template library |
| **Legal Operations** | Runs playbooks on documents | Simple execution, clear results, export options |
| **AI Configuration Specialist** | Optimizes AI behavior | Model selection, prompt tuning, execution metrics |
| **Administrator** | Manages playbooks org-wide | Sharing, permissions, usage analytics |

---

## 3. Architecture Principles

### 3.1 Orchestration Over Existing Pipeline

The new architecture adds a coordination layer that calls the existing analysis pipeline for each node:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         NEW: Orchestration Layer                             │
│                                                                              │
│  PlaybookOrchestrationService                                               │
│  ├── Load playbook + nodes                                                  │
│  ├── Build execution graph (dependency resolution)                          │
│  ├── Coordinate node execution (sequential/parallel)                        │
│  ├── Manage inter-node data flow (output variables)                         │
│  └── Execute output actions (tasks, emails, etc.)                           │
│                                                                              │
│                    Calls existing pipeline for each AI node                  │
│                                      │                                       │
└──────────────────────────────────────┼───────────────────────────────────────┘
                                       │
                                       ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                    EXISTING: Analysis Execution Layer                        │
│                           (UNCHANGED)                                        │
│                                                                              │
│  • ScopeResolverService         • AnalysisContextBuilder                    │
│  • TextExtractorService         • OpenAiClient                              │
│  • RagService                   • ToolHandlerRegistry                       │
│  • IAnalysisToolHandler implementations (8 handlers)                        │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 3.2 Node = Action + Scopes

A **Node** is the atomic unit of execution, bundling:

| Component | Purpose | Source |
|-----------|---------|--------|
| **Action** | What to do (AI analysis, rule evaluation, create task) | `sprk_analysisaction` (extended) |
| **Skills** | How to reason (prompt modifiers) | N:N from node |
| **Tools** | How to execute (handler implementations) | N:N from node |
| **Knowledge** | What to reference (RAG, documents, inline) | N:N from node |
| **Configuration** | Node-specific settings (model, timeout, conditions) | Node entity fields |

### 3.3 Playbook Modes (Backward Compatibility)

To ensure backward compatibility, playbooks operate in two modes:

| Mode | Behavior | Use Case |
|------|----------|----------|
| `Legacy` | Uses existing N:N relationships directly on playbook | Existing playbooks, no migration needed |
| `NodeBased` | Uses `sprk_playbooknode` for multi-node orchestration | New playbooks, migrated playbooks |

The orchestration service checks mode and delegates appropriately.

### 3.4 Data Flow Between Nodes

Each node declares an `outputVariable` name. Downstream nodes can reference previous outputs using template syntax:

```
Node 1 (outputVariable: "entities") → produces extracted entities
Node 2 (outputVariable: "risks") → produces risk assessment
Node 3 → references {{entities}} and {{risks}} in prompt/config
```

This enables:
- Chained AI analysis (summary uses entity extraction results)
- Conditional execution (create task only if {{risks.highCount}} > 0)
- Dynamic output actions (email subject = {{summary.title}})

---

## 4. Conceptual Model

### 4.1 Entity Relationships

```
sprk_analysisplaybook (EXTENDED)
├── sprk_playbookmode: Legacy | NodeBased
├── sprk_canvaslayoutjson: visual editor state
├── sprk_triggertype: Manual | OnDocumentUpload | OnRecordCreate | Scheduled
├── sprk_triggerconfigjson: trigger settings
├── sprk_version: version number
│
└── 1:N → sprk_playbooknode (NEW)
          ├── sprk_actionid → sprk_analysisaction (EXTENDED with actiontype)
          ├── sprk_executionorder: int
          ├── sprk_dependsonjson: [nodeId, nodeId, ...]
          ├── sprk_outputvariable: "entities", "risks", etc.
          ├── sprk_conditionjson: {"if": "{{risks.count}} > 0"}
          ├── sprk_configjson: action-type-specific config
          ├── sprk_modeldeploymentid → sprk_aimodeldeployment
          ├── sprk_timeoutseconds, sprk_retrycount
          ├── sprk_position_x, sprk_position_y: canvas coordinates
          │
          ├── N:N → sprk_analysisskill (existing, unchanged)
          ├── N:N → sprk_analysisknowledge (existing, unchanged)
          └── N:N → sprk_analysistool (existing, unchanged)

sprk_analysisaction (EXTENDED)
├── [existing fields unchanged]
├── sprk_actiontype: AiAnalysis | CreateTask | SendEmail | Condition | etc.
├── sprk_actionconfigschema: JSON schema for validation
└── sprk_modeldeploymentid: default model for this action

sprk_aimodeldeployment (NEW)
├── sprk_name: "GPT-4o Production", "GPT-4o-mini Fast"
├── sprk_provider: AzureOpenAI | OpenAI | Anthropic | Custom
├── sprk_modelid: "gpt-4o", "gpt-4o-mini"
├── sprk_endpoint: API endpoint URL
├── sprk_capability: Chat | Completion | Embedding
├── sprk_contextwindow: max tokens
├── sprk_isdefault: default for capability type
└── sprk_isactive: enabled/disabled

sprk_playbookrun (NEW - execution tracking)
├── sprk_playbookid → sprk_analysisplaybook
├── sprk_status: Pending | Running | Completed | Failed | Cancelled
├── sprk_triggeredby: user reference
├── sprk_inputcontextjson: documents, parameters
├── sprk_startedon, sprk_completedon
├── sprk_outputsjson: aggregated outputs
└── sprk_errormessage

sprk_playbooknoderun (NEW - per-node execution)
├── sprk_playbookrunid → sprk_playbookrun
├── sprk_playbooknodeid → sprk_playbooknode
├── sprk_status: Pending | Running | Completed | Failed | Skipped
├── sprk_inputjson, sprk_outputjson
├── sprk_tokensin, sprk_tokensout, sprk_durationms
└── sprk_errormessage
```

### 4.2 Action Types

The `sprk_analysisaction` entity is extended with `sprk_actiontype` to support both AI and deterministic actions:

| Value | Name | Category | Description |
|-------|------|----------|-------------|
| 0 | `AiAnalysis` | AI | Existing tool handler execution (default for backward compat) |
| 1 | `AiCompletion` | AI | Raw LLM call with prompt template |
| 2 | `AiEmbedding` | AI | Generate embeddings |
| 10 | `RuleEngine` | Deterministic | Business rules evaluation |
| 11 | `Calculation` | Deterministic | Formula/computation |
| 12 | `DataTransform` | Deterministic | JSON/XML transformation |
| 20 | `CreateTask` | Integration | Create Dataverse task |
| 21 | `SendEmail` | Integration | Send via Microsoft Graph |
| 22 | `UpdateRecord` | Integration | Update Dataverse entity |
| 23 | `CallWebhook` | Integration | External HTTP call |
| 30 | `Condition` | Control Flow | If/else branching |
| 31 | `Parallel` | Control Flow | Fork into parallel paths |
| 32 | `Wait` | Control Flow | Wait for human approval |

### 4.3 Execution Flow Example

**Playbook**: "Contract Risk Analysis with Escalation"

```
[Document Upload Trigger]
         │
         ▼
┌─────────────────────┐
│ NODE 1: Extract     │  ActionType: AiAnalysis
│ Entities            │  Tool: EntityExtractorHandler
│                     │  Model: gpt-4o-mini (fast, cheap)
│ Output: "entities"  │
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐  ┌─────────────────────┐
│ NODE 2: Analyze     │  │ NODE 3: Detect      │  (parallel - both depend on Node 1)
│ Clauses             │  │ Risks               │
│                     │  │                     │
│ Model: gpt-4o       │  │ Model: gpt-4o       │
│ Output: "clauses"   │  │ Output: "risks"     │
└─────────┬───────────┘  └─────────┬───────────┘
          │                        │
          └────────┬───────────────┘
                   ▼
┌─────────────────────┐
│ NODE 4: Generate    │  ActionType: AiAnalysis
│ Summary             │  Inputs: {{entities}}, {{clauses}}, {{risks}}
│                     │  Model: gpt-4o
│ Output: "summary"   │
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│ NODE 5: Condition   │  ActionType: Condition
│                     │  Condition: {{risks.highCount}} > 0
└─────────┬───────────┘
     ┌────┴────┐
     ▼         ▼
 [true]    [false]
     │         │
     ▼         ▼
┌─────────┐  (end)
│ NODE 6: │  ActionType: CreateTask
│ Create  │  Subject: "Review high-risk contract: {{entities.documentTitle}}"
│ Task    │  AssignTo: Legal Review Team
└─────────┘
```

---

## 5. Dataverse Schema

### 5.1 Extended Entity: `sprk_analysisaction`

| Field | Type | Description | Default |
|-------|------|-------------|---------|
| `sprk_actiontype` | Choice | Action type (see 4.2) | `AiAnalysis` (0) |
| `sprk_actionconfigschema` | Multiline Text | JSON schema for config validation | null |
| `sprk_modeldeploymentid` | Lookup | Default AI model for this action | null |

### 5.2 New Entity: `sprk_playbooknode`

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `sprk_playbooknodeid` | Uniqueidentifier | Yes | Primary key |
| `sprk_playbookid` | Lookup | Yes | FK to `sprk_analysisplaybook` |
| `sprk_actionid` | Lookup | Yes | FK to `sprk_analysisaction` |
| `sprk_name` | Text (200) | Yes | Display name |
| `sprk_executionorder` | Integer | Yes | Linear ordering (1, 2, 3...) |
| `sprk_dependsonjson` | Multiline Text | No | JSON array of node IDs to wait for |
| `sprk_outputvariable` | Text (100) | Yes | Variable name for output reference |
| `sprk_conditionjson` | Multiline Text | No | Execution condition |
| `sprk_configjson` | Multiline Text | No | Action-type-specific configuration |
| `sprk_modeldeploymentid` | Lookup | No | Override model (null = use action default) |
| `sprk_timeoutseconds` | Integer | No | Timeout (default 300) |
| `sprk_retrycount` | Integer | No | Retry attempts (default 0) |
| `sprk_position_x` | Integer | No | Canvas X coordinate |
| `sprk_position_y` | Integer | No | Canvas Y coordinate |
| `sprk_isactive` | Boolean | Yes | Enabled/disabled (default true) |

### 5.3 New N:N Relationships (on Node)

| Relationship Name | From Entity | To Entity |
|-------------------|-------------|-----------|
| `sprk_playbooknode_skill` | `sprk_playbooknode` | `sprk_analysisskill` |
| `sprk_playbooknode_knowledge` | `sprk_playbooknode` | `sprk_analysisknowledge` |
| `sprk_playbooknode_tool` | `sprk_playbooknode` | `sprk_analysistool` |

### 5.4 Extended Entity: `sprk_analysisplaybook`

| Field | Type | Description | Default |
|-------|------|-------------|---------|
| `sprk_playbookmode` | Choice | `Legacy` (0) or `NodeBased` (1) | `Legacy` (0) |
| `sprk_canvaslayoutjson` | Multiline Text | Visual editor viewport and positions | null |
| `sprk_triggertype` | Choice | When playbook runs | `Manual` (0) |
| `sprk_triggerconfigjson` | Multiline Text | Trigger-specific settings | null |
| `sprk_version` | Integer | Version number | 1 |

### 5.5 New Entity: `sprk_aimodeldeployment`

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `sprk_aimodeldeploymentid` | Uniqueidentifier | Yes | Primary key |
| `sprk_name` | Text (200) | Yes | Display name |
| `sprk_provider` | Choice | Yes | AzureOpenAI, OpenAI, Anthropic, Custom |
| `sprk_modelid` | Text (100) | Yes | Model identifier (e.g., "gpt-4o") |
| `sprk_endpoint` | Text (500) | No | API endpoint URL |
| `sprk_capability` | Choice | Yes | Chat, Completion, Embedding |
| `sprk_contextwindow` | Integer | No | Max context tokens |
| `sprk_costpertokenin` | Decimal | No | Cost tracking |
| `sprk_costpertokenout` | Decimal | No | Cost tracking |
| `sprk_isdefault` | Boolean | Yes | Default for capability type |
| `sprk_isactive` | Boolean | Yes | Enabled/disabled |

### 5.6 New Entity: `sprk_playbookrun`

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `sprk_playbookrunid` | Uniqueidentifier | Yes | Primary key |
| `sprk_playbookid` | Lookup | Yes | FK to playbook |
| `sprk_status` | Choice | Yes | Pending, Running, Completed, Failed, Cancelled |
| `sprk_triggeredby` | Lookup | Yes | User who triggered |
| `sprk_inputcontextjson` | Multiline Text | No | Documents, parameters |
| `sprk_startedon` | DateTime | No | Start time |
| `sprk_completedon` | DateTime | No | End time |
| `sprk_errormessage` | Multiline Text | No | Error details if failed |
| `sprk_outputsjson` | Multiline Text | No | Aggregated outputs |

### 5.7 New Entity: `sprk_playbooknoderun`

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `sprk_playbooknoderunid` | Uniqueidentifier | Yes | Primary key |
| `sprk_playbookrunid` | Lookup | Yes | FK to playbook run |
| `sprk_playbooknodeid` | Lookup | Yes | FK to node definition |
| `sprk_status` | Choice | Yes | Pending, Running, Completed, Failed, Skipped |
| `sprk_startedon` | DateTime | No | Start time |
| `sprk_completedon` | DateTime | No | End time |
| `sprk_inputjson` | Multiline Text | No | Input data |
| `sprk_outputjson` | Multiline Text | No | Output data |
| `sprk_tokensin` | Integer | No | Input tokens used |
| `sprk_tokensout` | Integer | No | Output tokens generated |
| `sprk_durationms` | Integer | No | Execution duration |
| `sprk_errormessage` | Multiline Text | No | Error details |

---

## 6. Component Architecture

### 6.1 Component Classification

| Category | Components | Impact |
|----------|------------|--------|
| **Unchanged** | `TextExtractorService`, `OpenAiClient`, `RagService`, `ToolHandlerRegistry`, all `*Handler` classes (8), `ExportServiceRegistry`, `DocumentContext`, `ToolResult`, `ToolExecutionContext` | Reused as-is |
| **Extended** | `IScopeResolverService`, `IAnalysisContextBuilder`, `IPlaybookService` | Add methods (details below) |
| **New (Orchestration)** | `IPlaybookOrchestrationService`, `PlaybookRunContext`, `ExecutionGraph` | Core orchestration |
| **New (Execution)** | `INodeExecutor`, `INodeExecutorRegistry`, node executor implementations | Per-type executors |
| **New (Data)** | `INodeService`, `PlaybookNode`, `NodeOutput`, `NodeExecutionContext` | Node management |
| **New (Utilities)** | `ITemplateEngine` | Variable substitution |

### 6.2 Service Layer Structure

```
Services/Ai/
├── [UNCHANGED - Existing Services]
│   ├── AnalysisOrchestrationService    # Legacy single-action execution
│   ├── ScopeResolverService
│   ├── AnalysisContextBuilder
│   ├── OpenAiClient
│   ├── RagService
│   ├── TextExtractorService
│   ├── ToolHandlerRegistry
│   └── Tools/
│       ├── EntityExtractorHandler
│       ├── ClauseAnalyzerHandler
│       ├── RiskDetectorHandler
│       ├── SummaryHandler
│       ├── DateExtractorHandler
│       ├── DocumentClassifierHandler
│       ├── ClauseComparisonHandler
│       └── FinancialCalculatorHandler
│
├── [EXTENDED - Minor Additions]
│   ├── IScopeResolverService           # +ResolveNodeScopesAsync, +GetActionExtendedAsync
│   ├── IAnalysisContextBuilder         # +BuildUserPromptWithContextAsync
│   └── IPlaybookService                # +Node-related methods, +mode handling
│
└── [NEW - Orchestration Layer]
    ├── IPlaybookOrchestrationService   # Multi-node execution coordinator
    ├── PlaybookOrchestrationService
    ├── INodeService                     # Node CRUD operations
    ├── NodeService
    ├── INodeExecutor                    # Interface for node-type executors
    ├── INodeExecutorRegistry            # Registry for executor lookup
    ├── NodeExecutorRegistry
    ├── ExecutionGraph                   # Dependency resolution, topological sort
    ├── PlaybookRunContext               # Shared state across nodes
    ├── ITemplateEngine                  # Variable substitution ({{var.prop}})
    ├── TemplateEngine
    │
    └── Nodes/                           # Node executor implementations
        ├── AiAnalysisNodeExecutor       # Bridges to existing tool handlers
        ├── AiCompletionNodeExecutor     # Raw LLM calls
        ├── CreateTaskNodeExecutor       # Dataverse task creation
        ├── SendEmailNodeExecutor        # Graph email sending
        ├── UpdateRecordNodeExecutor     # Dataverse record updates
        ├── ConditionNodeExecutor        # Conditional branching
        └── WaitNodeExecutor             # Human approval pause
```

### 6.3 Extended Interface: IScopeResolverService

**Existing methods** (unchanged):
- `ResolveScopesAsync(skillIds, knowledgeIds, toolIds)` → ResolvedScopes
- `ResolvePlaybookScopesAsync(playbookId)` → ResolvedScopes
- `GetActionAsync(actionId)` → AnalysisAction

**New methods**:

| Method | Purpose |
|--------|---------|
| `ResolveNodeScopesAsync(nodeId)` | Load scopes from node's N:N relationships |
| `GetActionExtendedAsync(actionId)` | Get action with ActionType field |

### 6.4 Extended Interface: IAnalysisContextBuilder

**Existing methods** (unchanged):
- `BuildSystemPrompt(action, skills)` → string
- `BuildUserPromptAsync(documentText, knowledgeContext)` → string

**New methods**:

| Method | Purpose |
|--------|---------|
| `BuildUserPromptWithContextAsync(documentText, knowledgeContext, previousOutputs, userContext)` | Build prompt with previous node outputs injected |

### 6.5 New Interface: IPlaybookOrchestrationService

| Method | Purpose |
|--------|---------|
| `ExecutePlaybookAsync(request, httpContext, cancellationToken)` | Execute playbook, returns IAsyncEnumerable of stream chunks |
| `ValidatePlaybookAsync(playbookId)` | Validate node graph (cycles, missing deps) |
| `GetRunStatusAsync(runId)` | Get execution status |

### 6.6 New Interface: INodeService

| Method | Purpose |
|--------|---------|
| `GetNodesForPlaybookAsync(playbookId)` | List all nodes for a playbook |
| `GetNodeAsync(nodeId)` | Get single node |
| `CreateNodeAsync(request)` | Create new node |
| `UpdateNodeAsync(nodeId, request)` | Update node |
| `DeleteNodeAsync(nodeId)` | Delete node |
| `ReorderNodesAsync(playbookId, nodeIdsInOrder)` | Reorder nodes |
| `UpdateNodeScopesAsync(nodeId, skillIds, knowledgeIds, toolIds)` | Update node's scope relationships |

### 6.7 New Interface: INodeExecutor

| Member | Purpose |
|--------|---------|
| `SupportedActionTypes` | List of ActionType values this executor handles |
| `ExecuteAsync(context, cancellationToken)` | Execute node, returns IAsyncEnumerable of chunks |
| `ValidateAsync(node, cancellationToken)` | Validate node configuration |

### 6.8 New Interface: INodeExecutorRegistry

| Method | Purpose |
|--------|---------|
| `GetExecutor(actionType)` | Get executor for action type |
| `GetAllExecutors()` | List all registered executors |

### 6.9 New Interface: ITemplateEngine

| Method | Purpose |
|--------|---------|
| `Render(template, variables)` | Substitute {{variable.property}} placeholders |

### 6.10 Key Models

**PlaybookRunContext**: Shared state across all nodes in a run
- RunId, PlaybookId
- Documents (extracted text, shared across nodes)
- Parameters (user-provided inputs)
- NodeOutputs (dictionary of outputVariable → NodeOutput)
- HttpContext (for OBO auth)

**NodeExecutionContext**: Context for single node execution
- Node definition
- Action definition (with ActionType)
- Resolved scopes (skills, knowledge, tools)
- Documents
- PreviousOutputs (read-only view of completed nodes)
- ModelDeploymentId (override or default)
- Output (mutable, set by executor)

**NodeOutput**: Result from a completed node
- TextContent (streaming text output)
- StructuredData (key-value pairs from tools)
- ToolResults (array of ToolResult from handlers)
- TokensIn, TokensOut, Duration (metrics)

**ExecutionGraph**: Dependency resolution
- Build(nodes) → Creates batches for parallel execution
- GetExecutionBatches() → Ordered list of node batches
- Validate() → Check for cycles, orphans, undefined references

### 6.11 Node Executor Implementations

| Executor | ActionTypes | Behavior |
|----------|-------------|----------|
| `AiAnalysisNodeExecutor` | AiAnalysis, AiCompletion | **Bridge to existing pipeline**: Resolves scopes, builds prompts, calls OpenAiClient, executes tool handlers |
| `CreateTaskNodeExecutor` | CreateTask | Creates Dataverse task with template substitution |
| `SendEmailNodeExecutor` | SendEmail | Sends email via Microsoft Graph |
| `UpdateRecordNodeExecutor` | UpdateRecord | Updates Dataverse entity |
| `ConditionNodeExecutor` | Condition | Evaluates condition, sets execution path |
| `WaitNodeExecutor` | Wait | Pauses execution for human approval |

**Critical**: `AiAnalysisNodeExecutor` does not reimplement AI execution—it wraps and calls the existing `AnalysisContextBuilder`, `OpenAiClient`, and `ToolHandlerRegistry` services.

---

## 7. API Specification

### 7.1 Endpoint Overview

| Method | Path | Description |
|--------|------|-------------|
| **Playbook Management** |||
| GET | `/api/ai/playbooks` | List playbooks (existing) |
| GET | `/api/ai/playbooks/{id}` | Get playbook details (existing) |
| POST | `/api/ai/playbooks` | Create playbook (existing) |
| PUT | `/api/ai/playbooks/{id}` | Update playbook (existing) |
| DELETE | `/api/ai/playbooks/{id}` | Delete playbook (existing) |
| **Node Management (NEW)** |||
| GET | `/api/ai/playbooks/{id}/nodes` | Get all nodes for playbook |
| POST | `/api/ai/playbooks/{id}/nodes` | Add node |
| PUT | `/api/ai/playbooks/{id}/nodes/{nodeId}` | Update node |
| DELETE | `/api/ai/playbooks/{id}/nodes/{nodeId}` | Delete node |
| PUT | `/api/ai/playbooks/{id}/nodes/reorder` | Reorder nodes |
| PUT | `/api/ai/playbooks/{id}/nodes/{nodeId}/scopes` | Update node scopes |
| **Canvas (NEW)** |||
| GET | `/api/ai/playbooks/{id}/canvas` | Get visual layout |
| PUT | `/api/ai/playbooks/{id}/canvas` | Save visual layout |
| **Validation & Execution (NEW)** |||
| POST | `/api/ai/playbooks/{id}/validate` | Validate playbook graph |
| POST | `/api/ai/playbooks/{id}/execute` | Start playbook execution |
| GET | `/api/ai/playbooks/runs/{runId}` | Get run status |
| GET | `/api/ai/playbooks/runs/{runId}/stream` | Stream run progress (SSE) |
| POST | `/api/ai/playbooks/runs/{runId}/cancel` | Cancel running playbook |
| **Reference Data (NEW)** |||
| GET | `/api/ai/actions` | List available actions (with ActionType) |
| GET | `/api/ai/model-deployments` | List AI model deployments |

### 7.2 Key Request/Response Shapes

**PlaybookExecuteRequest**:
- `documentIds`: array of document GUIDs
- `parameters`: optional key-value pairs
- `userContext`: optional user instructions

**PlaybookExecuteResponse** (202 Accepted):
- `runId`: GUID for tracking
- `status`: "Pending"
- `streamUrl`: SSE endpoint URL

**PlaybookStreamChunk** (SSE event):
- `runId`: GUID
- `nodeId`: GUID (if node-specific)
- `nodeName`: string (if node-specific)
- `type`: RunStarted | NodeStarted | NodeProgress | NodeCompleted | NodeSkipped | NodeFailed | RunCompleted | RunFailed
- `content`: streaming text (for NodeProgress)
- `data`: structured data (metrics, outputs, errors)
- `timestamp`: ISO datetime

**PlaybookNodeDto**:
- `id`, `playbookId`, `actionId`, `actionName`, `actionType`
- `name`, `executionOrder`, `dependsOn`, `outputVariable`
- `condition`, `config`, `modelDeploymentId`, `modelDeploymentName`
- `timeoutSeconds`, `retryCount`, `position`, `isActive`
- `scopes`: { skills: [], knowledge: [], tools: [] }

### 7.3 Streaming Protocol

Execution uses Server-Sent Events (SSE) for real-time progress:

1. Client calls `POST /api/ai/playbooks/{id}/execute` → receives `runId`
2. Client connects to `GET /api/ai/playbooks/runs/{runId}/stream`
3. Server streams chunks as events:
   - `RunStarted` (once)
   - `NodeStarted` → `NodeProgress`* → `NodeCompleted|NodeFailed|NodeSkipped` (per node)
   - `RunCompleted|RunFailed` (once)
4. Client closes connection on completion

---

## 8. Execution Engine

### 8.1 Execution Flow

1. **Load**: Fetch playbook and nodes from Dataverse
2. **Mode Check**: If Legacy mode, delegate to existing `AnalysisOrchestrationService`
3. **Build Graph**: Create `ExecutionGraph` from nodes (topological sort by dependencies)
4. **Extract Documents**: Download and extract text ONCE (shared across all nodes)
5. **Initialize Context**: Create `PlaybookRunContext` with empty `NodeOutputs`
6. **Execute Batches**: For each batch in execution order:
   - Nodes in same batch can run in parallel (no dependencies between them)
   - For each node:
     - Evaluate condition (skip if false)
     - Resolve scopes (skills, knowledge, tools)
     - Get executor for action type
     - Build `NodeExecutionContext` with previous outputs
     - Execute and stream progress
     - Store output in `NodeOutputs` dictionary
7. **Complete**: Emit `RunCompleted` with aggregated outputs

### 8.2 Dependency Resolution

The `ExecutionGraph` builds execution batches using topological sort:

**Input**: Nodes with `dependsOn` arrays
```
Node A: dependsOn []
Node B: dependsOn []
Node C: dependsOn [A]
Node D: dependsOn [A, B]
Node E: dependsOn [C, D]
```

**Output**: Batches
```
Batch 1: [A, B]      ← Can run in parallel
Batch 2: [C, D]      ← Can run in parallel (after Batch 1)
Batch 3: [E]         ← After Batch 2
```

**Validation**:
- Detect circular dependencies (error)
- Detect orphan nodes with no dependencies that aren't first (warning)
- Detect references to undefined output variables (error)

### 8.3 Condition Evaluation

Conditions use simple expression syntax with variable substitution:

```json
{"if": "{{risks.highCount}} > 0"}
{"if": "{{entities.partyCount}} >= 2"}
{"if": "{{summary.sentiment}} == 'negative'"}
```

The template engine substitutes variables, then a simple expression evaluator handles comparison operators.

### 8.4 AI Analysis Node Execution (Bridge Pattern)

The `AiAnalysisNodeExecutor` bridges to the existing analysis pipeline:

1. Get node's resolved scopes (from node's N:N relationships)
2. Process RAG knowledge sources (using existing `RagService`)
3. Build prompts (using existing `AnalysisContextBuilder`, extended for previous outputs)
4. Stream completion (using existing `OpenAiClient`, with model override)
5. Execute tool handlers (using existing `ToolHandlerRegistry`)
6. Aggregate results into `NodeOutput`

This ensures AI execution behavior is identical to existing single-action playbooks.

### 8.5 Error Handling

| Scenario | Behavior |
|----------|----------|
| Node times out | Mark node as Failed, continue or stop based on playbook config |
| Node throws exception | Mark node as Failed, log error, continue or stop |
| Condition evaluation fails | Log warning, default to true (execute node) |
| Circular dependency | Fail validation before execution |
| Missing dependency output | Fail node with clear error message |

Playbook-level setting `continueOnError` (default: false) controls whether execution stops on first failure or continues with remaining nodes.

---

## 9. Frontend Architecture

### 9.1 React 16 Constraint

Dataverse PCF controls are limited to React 16 APIs (ADR-022). React Flow, the preferred canvas library for node-based editors, requires React 18.

**Solution**: Iframe embedding pattern

```
Model-Driven App (Playbook Form)
└── PlaybookBuilderHost PCF (React 16)
    └── <iframe src="/workflow-builder">
        └── React 18 App with React Flow
            ├── Canvas with drag-and-drop nodes
            ├── Properties panel
            ├── Node palette
            └── Execution visualization
```

### 9.2 Architecture

**PlaybookBuilderHost PCF** (React 16):
- Manages Dataverse context (record ID, user info)
- Handles iframe lifecycle
- Communicates via postMessage API
- Provides save/load operations via Xrm SDK

**Workflow Builder App** (React 18, standalone):
- Full React Flow canvas functionality
- Node palette with draggable node types
- Properties panel for node configuration
- Scope selector (skills, knowledge, tools)
- Model selector dropdown
- Condition builder UI
- Real-time execution visualization
- Communicates with host via postMessage

### 9.3 Host-Builder Communication

Messages from Host → Builder:
- `INIT`: Initial playbook data, auth token
- `SAVE_SUCCESS` / `SAVE_ERROR`: Save operation result
- `AUTH_TOKEN`: Refreshed auth token

Messages from Builder → Host:
- `READY`: Builder loaded and ready
- `SAVE_REQUEST`: User clicked save, includes workflow definition
- `DIRTY_STATE`: Workflow has unsaved changes
- `EXECUTE_REQUEST`: User clicked run
- `CLOSE`: User closed builder

### 9.4 Builder App Structure

```
workflow-builder/
├── components/
│   ├── Canvas/           # React Flow wrapper, controls, minimap
│   ├── Nodes/            # Custom node components by type
│   ├── Edges/            # Custom edge components
│   ├── Palette/          # Draggable node types, templates
│   ├── Properties/       # Node configuration panel
│   ├── Toolbar/          # Actions, validation status
│   └── Execution/        # Real-time progress overlay
├── hooks/                # State management hooks
├── services/             # API client, host bridge
├── stores/               # Zustand state management
└── types/                # TypeScript interfaces
```

### 9.5 Deployment

The workflow builder app is deployed as a static web app, hosted alongside the BFF API or as a separate Azure Static Web App. The PCF control loads it via iframe with the playbook ID as a query parameter.

---

## 10. Migration Strategy

### 10.1 Backward Compatibility

Existing playbooks continue to work without modification:

1. All existing playbooks have `sprk_playbookmode = Legacy` (default)
2. Legacy mode uses existing N:N relationships on playbook entity
3. `PlaybookOrchestrationService` checks mode and delegates to existing `AnalysisOrchestrationService` for Legacy playbooks
4. No changes to existing UI forms or PCF controls required

### 10.2 Migration Path for Existing Playbooks

Optional one-click migration converts Legacy → NodeBased:

1. Read existing playbook with scopes
2. Create single `sprk_playbooknode` with:
   - ActionId from playbook's action relationship
   - ExecutionOrder = 1
   - OutputVariable = "output"
   - Copy skill/knowledge/tool relationships to node
3. Update playbook mode to NodeBased
4. Original playbook relationships remain (not deleted) for rollback

### 10.3 Schema Deployment Order

1. Add fields to `sprk_analysisaction` (actiontype, configschema, modeldeploymentid)
2. Create `sprk_aimodeldeployment` entity
3. Create `sprk_playbooknode` entity
4. Create N:N relationships on node
5. Add fields to `sprk_analysisplaybook` (mode, canvas, trigger, version)
6. Create `sprk_playbookrun` entity
7. Create `sprk_playbooknoderun` entity
8. Publish customizations

All changes are additive—no existing fields or relationships are modified or removed.

---

## 11. Implementation Phases

### Phase 1: Foundation (Estimated: 4-6 weeks)

**Deliverables**:
- Dataverse schema deployed (all new entities and fields)
- `INodeService` / `NodeService` implemented
- Extended `IScopeResolverService` with node scope resolution
- `ExecutionGraph` for dependency resolution
- `AiAnalysisNodeExecutor` bridging to existing pipeline
- `PlaybookOrchestrationService` with linear execution
- Node management API endpoints
- Basic validation

**Outcome**: Users can create playbooks with multiple AI nodes via API, execute sequentially

### Phase 2: Visual Builder (Estimated: 4-6 weeks)

**Deliverables**:
- React 18 workflow-builder app with React Flow
- Node palette, properties panel, canvas controls
- Scope selector integration
- PlaybookBuilderHost PCF with iframe embedding
- Host-builder communication
- Parallel execution support in orchestration
- Execution visualization overlay

**Outcome**: Visual drag-and-drop builder with parallel node execution

### Phase 3: Output Actions (Estimated: 3-4 weeks)

**Deliverables**:
- `CreateTaskNodeExecutor` implementation
- `SendEmailNodeExecutor` implementation
- `UpdateRecordNodeExecutor` implementation
- `ITemplateEngine` for variable substitution
- Output action configuration UI in builder
- Integration tests for output actions

**Outcome**: Workflows can create tasks, send emails, update Dataverse records

### Phase 4: Advanced Features (Estimated: 4-6 weeks)

**Deliverables**:
- `ConditionNodeExecutor` for branching
- Condition builder UI (visual expression editor)
- Per-node model selection in properties panel
- Execution history and replay
- Playbook templates library
- Version history for playbooks

**Outcome**: Conditional branching, model selection, templates

### Phase 5: Production Hardening (Estimated: 2-3 weeks)

**Deliverables**:
- Comprehensive error handling and retry logic
- Timeout management per node
- Cancellation support for running playbooks
- Audit logging for execution history
- Performance optimization (caching, batching)
- Load testing and optimization

**Outcome**: Production-ready system

---

## 12. ADR Compliance

| ADR | Requirement | Compliance Approach |
|-----|-------------|---------------------|
| **ADR-001** | Minimal API + BackgroundService | All new endpoints use Minimal API pattern |
| **ADR-008** | Endpoint filters for auth | `PlaybookAuthorizationFilter` on all playbook endpoints |
| **ADR-009** | Redis-first caching | Cache resolved scopes, execution graphs |
| **ADR-010** | DI minimalism (≤15) | ~10 new services, within budget |
| **ADR-013** | Extend BFF, not separate service | All orchestration within Sprk.Bff.Api |
| **ADR-022** | React 16 for PCF | Iframe pattern isolates React 18 builder |

---

## 13. Open Questions

| # | Question | Options | Recommendation |
|---|----------|---------|----------------|
| 1 | Should Legacy playbooks auto-migrate on first edit? | (a) Yes, transparent migration (b) No, explicit "Convert" button | (b) Explicit conversion—less surprising |
| 2 | How to handle long-running nodes (>5 min)? | (a) Background job with polling (b) Keep SSE connection open | (a) Background job for reliability |
| 3 | Should condition syntax support complex expressions? | (a) Simple comparisons only (b) Full expression language | (a) Start simple, extend later |
| 4 | How to version playbooks? | (a) Copy-on-write (b) Git-style branching | (a) Copy-on-write is simpler |
| 5 | Should nodes support custom code/scripts? | (a) Yes, sandboxed execution (b) No, predefined actions only | (b) Start with predefined, assess need |

---

## Document History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-01-07 | GPT 5.2 | Original concept (Unified_Node_Playbook_and_Workflow_Design.md) |
| 2.0 | 2026-01-07 | Claude + Human | Complete redesign aligned with existing codebase |

---

## References

- [GPT 5.2 Original Design](./Unified_Node_Playbook_and_Workflow_Design.md) - Initial concept (reference only)
- [Code Examples](./reference/code-examples.md) - Implementation reference (optional)
- ADR-001: Minimal API and Workers
- ADR-008: Endpoint Filters for Authorization
- ADR-010: DI Minimalism
- ADR-013: AI Architecture
- ADR-022: PCF Platform Libraries

---

*This document serves as the research design input for spec.md generation via the design-to-spec skill.*
