# AI Node-Based Playbook Builder - Design Document

> **Version**: 1.0
> **Created**: January 8, 2026
> **Status**: Ready for Specification
> **Prerequisites**: R4 Complete (Playbook Scope System), R5 Design (RAG Pipeline)

---

## 1. Executive Summary

### 1.1 Purpose

Transform Spaarke's current single-action playbook model into a **multi-node orchestration platform** that enables:

- Chaining multiple AI analysis actions with data flow between them
- Mixing AI (probabilistic) and deterministic actions in a single playbook
- Visual drag-and-drop playbook construction for business analysts
- Flexible delivery outputs (documents, emails, records, Teams messages)
- Per-node AI model selection for cost/quality optimization

### 1.2 Design Philosophy

**Extend, don't replace.** This design adds an orchestration layer on top of the existing analysis pipeline. Core components (`IAnalysisToolHandler`, `OpenAiClient`, `ScopeResolverService`, etc.) remain unchanged and are reused by the new orchestration layer.

### 1.3 Core Architectural Decisions

| Decision | Rationale |
|----------|-----------|
| One action per node | Atomic, clear purpose |
| Multiple skills per node | Skills are prompt modifiers that compose well |
| **Single tool per node** | Avoids execution ambiguity |
| Multiple knowledge per node | Multiple context sources are legitimate |
| Single AI output per node | Output is structured object with properties |
| AI Output vs Delivery Output | Distinct concepts requiring separate handling |
| Dataverse as system of record | POML for export/import only |

### 1.4 Key Transformations

**Current Model**:
```
Playbook → (1) Action → (N) Skills, Tools, Knowledge
```

**New Model**:
```
Playbook → (N) Nodes → each Node = Action + Skills + (1) Tool + Knowledge
                    → Delivery Nodes for output rendering
```

---

## 2. Requirements

### 2.1 Business Requirements

| ID | Requirement | Priority |
|----|-------------|----------|
| BR-01 | Visual playbook builder for non-developers | P1 |
| BR-02 | Multi-node AI analysis with data flow | P1 |
| BR-03 | Multiple delivery output types (Document, Email, Record, Teams) | P1 |
| BR-04 | Per-node AI model selection | P2 |
| BR-05 | Playbook templates library | P2 |
| BR-06 | Playbook versioning | P3 |
| BR-07 | Execution history and metrics | P2 |

### 2.2 Technical Requirements

| ID | Requirement | Constraint |
|----|-------------|------------|
| TR-01 | Backward compatibility | Existing playbooks continue to work unchanged |
| TR-02 | ADR compliance | ADR-001, ADR-008, ADR-010, ADR-013, ADR-022 |
| TR-03 | React 16 compatibility | PCF controls limited to React 16 APIs |
| TR-04 | Minimal reengineering | Reuse existing analysis pipeline |
| TR-05 | DI budget compliance | ≤15 non-framework DI registrations |

### 2.3 User Personas

| Persona | Role | Primary Needs |
|---------|------|---------------|
| **Business AI Analyst** | Creates and configures playbooks | Visual builder, no coding, template library |
| **Legal Operations** | Runs playbooks on documents | Simple execution, clear results, export options |
| **AI Configuration Specialist** | Optimizes AI behavior | Model selection, prompt tuning, execution metrics |

---

## 3. Conceptual Model

### 3.1 Node Structure

A **Node** is the atomic unit of execution, bundling:

| Component | Cardinality | Purpose |
|-----------|-------------|---------|
| **Action** | 1 per node | What to do (AI analysis, create task, deliver output) |
| **Skills** | 0..N per node | How to reason (prompt modifiers) |
| **Tool** | 0..1 per node | How to execute (handler implementation) |
| **Knowledge** | 0..N per node | What to reference (RAG, documents, inline) |
| **Configuration** | 1 per node | Node-specific settings (model, timeout, conditions) |

### 3.2 Output Concepts

**Two distinct output types**:

| Term | Definition | Example |
|------|------------|---------|
| **AI Output** (Node Output) | Structured data from AI node execution | JSON: `{ parties: [...], risks: [...] }` |
| **Delivery Output** | Final artifact delivered to users | Word document, Email, Teams message, Dataverse record |

### 3.3 Playbook Types

| Type | Description | Available Actions |
|------|-------------|-------------------|
| **AI Analysis** | Document analysis with AI | AiAnalysis, AiCompletion, Condition, DeliverOutput |
| **Workflow** | Deterministic business process | CreateTask, SendEmail, UpdateRecord, Condition, Wait |
| **Hybrid** | Mix of AI and workflow | All action types |

### 3.4 Playbook Modes (Backward Compatibility)

| Mode | Behavior | Use Case |
|------|----------|----------|
| `Legacy` | Uses existing N:N relationships on playbook entity | Existing playbooks, no migration needed |
| `NodeBased` | Uses `sprk_playbooknode` for multi-node orchestration | New playbooks, migrated playbooks |

### 3.5 Action Types

| Value | Name | Category | Description |
|-------|------|----------|-------------|
| 0 | `AiAnalysis` | AI | Tool handler execution (existing) |
| 1 | `AiCompletion` | AI | Raw LLM call with prompt template |
| 2 | `AiEmbedding` | AI | Generate embeddings |
| 10 | `RuleEngine` | Deterministic | Business rules evaluation |
| 11 | `Calculation` | Deterministic | Formula/computation |
| 12 | `DataTransform` | Deterministic | JSON/XML transformation |
| 20 | `CreateTask` | Integration | Create Dataverse task |
| 21 | `SendEmail` | Integration | Send via Microsoft Graph |
| 22 | `UpdateRecord` | Integration | Update Dataverse entity |
| 23 | `CallWebhook` | Integration | External HTTP call |
| 24 | `SendTeamsMessage` | Integration | Teams notification |
| 30 | `Condition` | Control Flow | If/else branching |
| 31 | `Parallel` | Control Flow | Fork into parallel paths |
| 32 | `Wait` | Control Flow | Wait for human approval |
| 40 | `DeliverOutput` | Delivery | Render and deliver final output |

### 3.6 Data Flow Between Nodes

Each node declares an `outputVariable` name. Downstream nodes reference outputs using template syntax:

```
Node 1 (outputVariable: "entities") → produces extracted entities
Node 2 (outputVariable: "risks") → produces risk assessment
Node 3 → references {{entities}} and {{risks}} in prompt/config
Node 4 (DeliverOutput) → renders Word doc using {{entities}}, {{risks}}
```

---

## 4. Dataverse Schema

### 4.1 Extended Entity: `sprk_analysisplaybook`

| Field | Type | Description | Default |
|-------|------|-------------|---------|
| `sprk_playbookmode` | Choice | `Legacy` (0) or `NodeBased` (1) | `Legacy` |
| `sprk_playbooktype` | Choice | `AiAnalysis` (0), `Workflow` (1), `Hybrid` (2) | `AiAnalysis` |
| `sprk_canvaslayoutjson` | Multiline Text | Visual editor viewport and positions | null |
| `sprk_triggertype` | Choice | When playbook runs | `Manual` |
| `sprk_triggerconfigjson` | Multiline Text | Trigger-specific settings | null |
| `sprk_version` | Integer | Version number | 1 |
| `sprk_maxparallelnodes` | Integer | Max concurrent node execution | 3 |
| `sprk_continueonerror` | Boolean | Continue on node failure | false |

### 4.2 New Entity: `sprk_playbooknode`

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `sprk_playbooknodeid` | Uniqueidentifier | Yes | Primary key |
| `sprk_playbookid` | Lookup | Yes | FK to `sprk_analysisplaybook` |
| `sprk_actionid` | Lookup | Yes | FK to `sprk_analysisaction` |
| `sprk_toolid` | Lookup | No | FK to `sprk_analysistool` (single tool) |
| `sprk_name` | Text (200) | Yes | Display name |
| `sprk_executionorder` | Integer | Yes | Linear ordering |
| `sprk_dependsonjson` | Multiline Text | No | JSON array of dependent node IDs |
| `sprk_outputvariable` | Text (100) | Yes | Variable name for output reference |
| `sprk_conditionjson` | Multiline Text | No | Execution condition |
| `sprk_configjson` | Multiline Text | No | Action-type-specific configuration |
| `sprk_modeldeploymentid` | Lookup | No | Override AI model |
| `sprk_timeoutseconds` | Integer | No | Timeout (default 300) |
| `sprk_retrycount` | Integer | No | Retry attempts (default 0) |
| `sprk_position_x` | Integer | No | Canvas X coordinate |
| `sprk_position_y` | Integer | No | Canvas Y coordinate |
| `sprk_isactive` | Boolean | Yes | Enabled/disabled |

**N:N Relationships on Node**:
- `sprk_playbooknode_skill` → `sprk_analysisskill` (multiple skills)
- `sprk_playbooknode_knowledge` → `sprk_analysisknowledge` (multiple knowledge)

### 4.3 Extended Entity: `sprk_analysisaction`

| Field | Type | Description | Default |
|-------|------|-------------|---------|
| `sprk_actiontype` | Choice | Action type (see 3.5) | `AiAnalysis` |
| `sprk_outputschemajson` | Multiline Text | JSON Schema for output | null |
| `sprk_outputformat` | Choice | JSON, Markdown, PlainText | JSON |
| `sprk_modeldeploymentid` | Lookup | Default AI model | null |
| `sprk_allowsskills` | Boolean | Can have skills | true |
| `sprk_allowstools` | Boolean | Can have tool | true |
| `sprk_allowsknowledge` | Boolean | Can have knowledge | true |
| `sprk_allowsdelivery` | Boolean | Can configure delivery | false |

### 4.4 Extended Entity: `sprk_analysistool`

| Field | Type | Description |
|-------|------|-------------|
| `sprk_outputschemajson` | Multiline Text | JSON Schema defining tool output structure |
| `sprk_outputexamplejson` | Multiline Text | Sample output for documentation |

### 4.5 New Entity: `sprk_aimodeldeployment`

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `sprk_aimodeldeploymentid` | Uniqueidentifier | Yes | Primary key |
| `sprk_name` | Text (200) | Yes | Display name |
| `sprk_provider` | Choice | Yes | AzureOpenAI, OpenAI, Anthropic |
| `sprk_modelid` | Text (100) | Yes | Model identifier (e.g., "gpt-4o") |
| `sprk_endpoint` | Text (500) | No | API endpoint URL |
| `sprk_capability` | Choice | Yes | Chat, Completion, Embedding |
| `sprk_contextwindow` | Integer | No | Max context tokens |
| `sprk_isdefault` | Boolean | Yes | Default for capability |
| `sprk_isactive` | Boolean | Yes | Enabled/disabled |

### 4.6 New Entity: `sprk_deliverytemplate`

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `sprk_deliverytemplateid` | Uniqueidentifier | Yes | Primary key |
| `sprk_name` | Text (200) | Yes | Display name |
| `sprk_type` | Choice | Yes | WordDocument, Email, TeamsAdaptiveCard |
| `sprk_templatecontent` | Multiline Text | No | Template markup |
| `sprk_templatefileid` | Text (500) | No | SPE file ID for Word templates |
| `sprk_placeholdersjson` | Multiline Text | No | Expected placeholders |
| `sprk_isactive` | Boolean | Yes | Enabled/disabled |

### 4.7 New Entity: `sprk_playbookrun`

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `sprk_playbookrunid` | Uniqueidentifier | Yes | Primary key |
| `sprk_playbookid` | Lookup | Yes | FK to playbook |
| `sprk_status` | Choice | Yes | Pending, Running, Completed, Failed, Cancelled |
| `sprk_triggeredby` | Lookup | Yes | User who triggered |
| `sprk_inputcontextjson` | Multiline Text | No | Documents, parameters |
| `sprk_startedon` | DateTime | No | Start time |
| `sprk_completedon` | DateTime | No | End time |
| `sprk_outputsjson` | Multiline Text | No | Aggregated outputs |
| `sprk_errormessage` | Multiline Text | No | Error details |

### 4.8 New Entity: `sprk_playbooknoderun`

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `sprk_playbooknoderunid` | Uniqueidentifier | Yes | Primary key |
| `sprk_playbookrunid` | Lookup | Yes | FK to playbook run |
| `sprk_playbooknodeid` | Lookup | Yes | FK to node definition |
| `sprk_status` | Choice | Yes | Pending, Running, Completed, Failed, Skipped |
| `sprk_inputjson` | Multiline Text | No | Input data |
| `sprk_outputjson` | Multiline Text | No | Output data |
| `sprk_tokensin` | Integer | No | Input tokens used |
| `sprk_tokensout` | Integer | No | Output tokens generated |
| `sprk_durationms` | Integer | No | Execution duration |
| `sprk_errormessage` | Multiline Text | No | Error details |
| `sprk_validationwarnings` | Multiline Text | No | Any output validation warnings |

---

## 5. Component Architecture

### 5.1 Component Classification

| Category | Components | Impact |
|----------|------------|--------|
| **Unchanged** | `TextExtractorService`, `OpenAiClient`, `RagService`, `ToolHandlerRegistry`, all 8 `*Handler` classes, `ExportServiceRegistry`, `DocumentContext`, `ToolResult` | Reused as-is |
| **Extended** | `IScopeResolverService`, `IAnalysisContextBuilder`, `IPlaybookService`, `IAnalysisToolHandler` | Add methods |
| **New (Orchestration)** | `IPlaybookOrchestrationService`, `PlaybookRunContext`, `ExecutionGraph` | Core orchestration |
| **New (Execution)** | `INodeExecutor`, `INodeExecutorRegistry`, node executor implementations | Per-type executors |
| **New (Data)** | `INodeService`, `PlaybookNode`, `NodeOutput`, `NodeExecutionContext` | Node management |
| **New (Utilities)** | `ITemplateEngine` | Variable substitution |

### 5.2 Service Layer Structure

```
Services/Ai/
├── [UNCHANGED]
│   ├── AnalysisOrchestrationService    # Legacy single-action
│   ├── ScopeResolverService
│   ├── AnalysisContextBuilder
│   ├── OpenAiClient
│   ├── RagService
│   ├── TextExtractorService
│   ├── ToolHandlerRegistry
│   └── Tools/ (8 handlers)
│
├── [EXTENDED]
│   ├── IScopeResolverService           # +ResolveNodeScopesAsync
│   ├── IAnalysisContextBuilder         # +BuildUserPromptWithContextAsync
│   ├── IPlaybookService                # +Node methods, +mode handling
│   └── IAnalysisToolHandler            # +OutputSchema property
│
└── [NEW - Orchestration Layer]
    ├── IPlaybookOrchestrationService
    ├── PlaybookOrchestrationService
    ├── INodeService
    ├── NodeService
    ├── INodeExecutor
    ├── INodeExecutorRegistry
    ├── NodeExecutorRegistry
    ├── ExecutionGraph
    ├── PlaybookRunContext
    ├── ITemplateEngine
    ├── TemplateEngine (Handlebars.NET)
    │
    └── Nodes/
        ├── AiAnalysisNodeExecutor
        ├── AiCompletionNodeExecutor
        ├── CreateTaskNodeExecutor
        ├── SendEmailNodeExecutor
        ├── UpdateRecordNodeExecutor
        ├── ConditionNodeExecutor
        ├── DeliverOutputNodeExecutor
        └── WaitNodeExecutor
```

### 5.3 Extended Interface: IAnalysisToolHandler

```csharp
public interface IAnalysisToolHandler
{
    // Existing
    string HandlerId { get; }
    ToolHandlerMetadata Metadata { get; }
    IReadOnlyList<ToolType> SupportedToolTypes { get; }
    ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool);
    Task<ToolResult> ExecuteAsync(ToolExecutionContext context, AnalysisTool tool, CancellationToken ct);

    // NEW: Output schema for this handler
    JsonSchema OutputSchema { get; }
}
```

### 5.4 New Interface: ITemplateEngine

```csharp
public interface ITemplateEngine
{
    string Render(string template, IReadOnlyDictionary<string, object> variables);
}
```

### 5.5 Key Models

**PlaybookRunContext**: Shared state across all nodes
- RunId, PlaybookId
- Documents (extracted text, shared across nodes)
- Parameters (user-provided inputs)
- NodeOutputs (dictionary of outputVariable → NodeOutput)
- HttpContext (for OBO auth)

**NodeExecutionContext**: Context for single node execution
- Node definition
- Action definition (with ActionType)
- Resolved scopes (skills, knowledge, tool)
- Documents
- PreviousOutputs (read-only view of completed nodes)
- ModelDeploymentId (override or default)

**NodeOutput**: Result from a completed node
- TextContent (streaming text output)
- StructuredData (JSON object matching schema)
- ToolResults (array of ToolResult from handlers)
- TokensIn, TokensOut, Duration (metrics)
- Confidence (optional, from AI)

---

## 6. API Specification

### 6.1 Endpoint Overview

| Method | Path | Description |
|--------|------|-------------|
| **Playbook Management** |||
| GET | `/api/ai/playbooks` | List playbooks |
| GET | `/api/ai/playbooks/{id}` | Get playbook with nodes |
| POST | `/api/ai/playbooks` | Create playbook |
| PUT | `/api/ai/playbooks/{id}` | Update playbook |
| DELETE | `/api/ai/playbooks/{id}` | Delete playbook |
| **Node Management** |||
| GET | `/api/ai/playbooks/{id}/nodes` | Get all nodes |
| POST | `/api/ai/playbooks/{id}/nodes` | Add node |
| PUT | `/api/ai/playbooks/{id}/nodes/{nodeId}` | Update node |
| DELETE | `/api/ai/playbooks/{id}/nodes/{nodeId}` | Delete node |
| PUT | `/api/ai/playbooks/{id}/nodes/reorder` | Reorder nodes |
| PUT | `/api/ai/playbooks/{id}/nodes/{nodeId}/scopes` | Update node scopes |
| **Canvas** |||
| GET | `/api/ai/playbooks/{id}/canvas` | Get visual layout |
| PUT | `/api/ai/playbooks/{id}/canvas` | Save visual layout |
| **Validation & Execution** |||
| POST | `/api/ai/playbooks/{id}/validate` | Validate playbook graph |
| POST | `/api/ai/playbooks/{id}/execute` | Start execution |
| GET | `/api/ai/playbooks/runs/{runId}` | Get run status |
| GET | `/api/ai/playbooks/runs/{runId}/stream` | Stream progress (SSE) |
| POST | `/api/ai/playbooks/runs/{runId}/cancel` | Cancel execution |
| **Reference Data** |||
| GET | `/api/ai/actions` | List actions (with ActionType, compatibility) |
| GET | `/api/ai/model-deployments` | List AI model deployments |
| GET | `/api/ai/delivery-templates` | List delivery templates |

### 6.2 Streaming Protocol (SSE)

Event types:
- `RunStarted` - Playbook execution began
- `NodeStarted` - Node execution began
- `NodeProgress` - Streaming text content
- `NodeCompleted` - Node finished successfully
- `NodeSkipped` - Node skipped (condition false)
- `NodeFailed` - Node failed with error
- `RunCompleted` - Playbook finished successfully
- `RunFailed` - Playbook failed

---

## 7. Frontend Architecture

### 7.1 React 16 Constraint Solution

Dataverse PCF controls are limited to React 16 APIs (ADR-022). React Flow requires React 18.

**Solution**: Iframe embedding pattern

```
Model-Driven App (Playbook Form)
└── PlaybookBuilderHost PCF (React 16)
    └── <iframe src="/playbook-builder">
        └── React 18 App with React Flow
            ├── Canvas with drag-and-drop nodes
            ├── Properties panel
            ├── Node palette
            └── Execution visualization
```

### 7.2 Host-Builder Communication

**Messages Host → Builder**:
- `INIT`: Playbook data, auth token, action list
- `SAVE_SUCCESS` / `SAVE_ERROR`: Save operation result
- `AUTH_TOKEN`: Refreshed auth token

**Messages Builder → Host**:
- `READY`: Builder loaded and ready
- `SAVE_REQUEST`: Save playbook definition
- `DIRTY_STATE`: Unsaved changes indicator
- `EXECUTE_REQUEST`: Start playbook execution
- `CLOSE`: User closed builder

### 7.3 Builder App Structure

```
playbook-builder/
├── components/
│   ├── Canvas/           # React Flow wrapper
│   ├── Nodes/            # Custom node components by type
│   ├── Edges/            # Custom edge components
│   ├── Palette/          # Draggable node types
│   ├── Properties/       # Node configuration panel
│   ├── Toolbar/          # Actions, validation
│   └── Execution/        # Real-time progress overlay
├── hooks/
├── services/             # API client, host bridge
├── stores/               # Zustand state management
└── types/
```

### 7.4 Scope Filtering UI

When user selects an Action for a node:
1. Query action's compatibility settings
2. Show/hide scope sections (Skills, Tool, Knowledge, Delivery)
3. Filter dropdown options to compatible items

---

## 8. Template Engine

**Decision**: Use **Handlebars.NET** for all template rendering.

**Rationale**:
- Logic-less engine with no code execution capability
- Playbook creators are trusted administrators
- Document content is processed by AI, not by templates
- Simple, well-understood, minimal attack surface

**No custom security layer required.**

---

## 9. AI Output Accuracy

### 9.1 The Core Risk

AI outputs are probabilistic and may contain errors:
- **Hallucinations**: AI reports data that doesn't exist in the document
- **Omissions**: AI misses items that are present
- **Misinterpretations**: AI extracts incorrect values
- **False confidence**: Output looks definitive but AI was guessing

### 9.2 Mitigation Strategy

**Philosophy**: Surface uncertainty to users rather than hiding it. Let humans verify when stakes are high.

| Mitigation | Description | Complexity |
|------------|-------------|------------|
| **Confidence scores** | Tool handlers output confidence values; UI displays as color badges | Low |
| **Source citations** | AI includes source text references for verification | Low |
| **Human review gate** | Optional Wait node before delivery for high-stakes playbooks | Already in design |
| **Basic validation** | Sanity checks on outputs (valid JSON, required fields, reasonable bounds) | Low (~100 LoC) |
| **Audit trail** | All node inputs/outputs logged in `sprk_playbooknoderun` | Already in design |

### 9.3 Confidence Scores

Update tool handler prompts to request confidence alongside data:

```json
{
  "parties": [
    { "name": "Acme Corp", "role": "buyer", "confidence": 0.95 },
    { "name": "Smith LLC", "role": "seller", "confidence": 0.87 }
  ],
  "_meta": {
    "overallConfidence": 0.91,
    "processingNotes": "Party roles inferred from context"
  }
}
```

**UI Treatment**: Display confidence badges (green ≥0.9, yellow 0.7-0.9, red <0.7).

### 9.4 Source Citations

AI includes where data was found:

```json
{
  "parties": [
    {
      "name": "Acme Corp",
      "role": "buyer",
      "source": "Page 1, paragraph 2: 'Acme Corp (the \"Buyer\")...'"
    }
  ]
}
```

**UI Treatment**: Click to highlight source in document viewer.

### 9.5 Basic Output Validation

Simple sanity checks, not complex schema validation:

```csharp
public ValidationResult ValidateOutput(NodeOutput output, string actionType)
{
    // Did AI return valid JSON?
    if (!IsValidJson(output.RawText))
        return Warning("AI returned invalid JSON");

    // Are required fields present?
    if (actionType == "EntityExtractor" && !output.HasField("parties"))
        return Warning("No parties extracted - verify document");

    // Sanity bounds
    if (output.GetInt("partyCount") > 50)
        return Warning("Unusually high party count - verify");

    return Ok();
}
```

Warnings are logged to `sprk_playbooknoderun.sprk_validationwarnings` but do not block execution.

---

## 10. Delivery Output Integration

### 10.1 Delivery Types

| Type | Integration | Phase |
|------|-------------|-------|
| **Document** | Power Apps Word Template | P1 |
| **Email** | Power Apps Email Template or Microsoft Graph | P1 |
| **Teams Message** | Microsoft Graph Teams API | P2 |
| **Record Update** | Dataverse SDK | P1 |
| **Record Create** | Dataverse SDK | P1 |

### 10.2 Phase 1 Approach

Use **Power Apps templates** for Document and Email:

```
Playbook Execution Complete
    ↓
Aggregate AI Outputs → JSON payload
    ↓
Call Power Automate flow with payload
    ↓
Power Automate renders template → Delivery
```

### 10.3 Future Enhancement

Build Spaarke-native template editor with:
- Placeholder support: `{{entities[0].name}}`
- Filters: `{{risks | where: severity == 'high' | count}}`
- Rich preview with sample data

---

## 11. Execution Engine

### 11.1 Execution Flow

1. **Load**: Fetch playbook and nodes from Dataverse
2. **Mode Check**: If Legacy mode, delegate to existing `AnalysisOrchestrationService`
3. **Build Graph**: Create `ExecutionGraph` (topological sort by dependencies)
4. **Extract Documents**: Download and extract text ONCE (shared)
5. **Initialize Context**: Create `PlaybookRunContext`
6. **Execute Batches**: For each batch:
   - Nodes in same batch run in parallel (respecting `maxParallelNodes`)
   - Evaluate conditions, resolve scopes, execute
   - Validate outputs (warnings only)
   - Store outputs in context
7. **Deliver**: Execute delivery nodes
8. **Complete**: Emit `RunCompleted`

### 11.2 Execution Throttling

- `maxParallelNodes` setting on playbook (default 3)
- Queue nodes when at limit
- Track Azure OpenAI rate limit headers
- Implement exponential backoff

---

## 12. Migration Strategy

### 12.1 Backward Compatibility

- All existing playbooks have `sprk_playbookmode = Legacy`
- Legacy mode uses existing N:N relationships on playbook entity
- `PlaybookOrchestrationService` checks mode and delegates
- No changes to existing UI forms required

### 12.2 Migration Path

Optional migration: Legacy → NodeBased
1. Create single `sprk_playbooknode` with existing action
2. Copy skill/knowledge relationships to node
3. Set tool on node
4. Update playbook mode to NodeBased

---

## 13. Implementation Phases

### Phase 1: Foundation

**Deliverables**:
- Dataverse schema (all new entities and fields)
- `INodeService` / `NodeService`
- Extended `IScopeResolverService` with node scope resolution
- `ExecutionGraph` for dependency resolution
- `AiAnalysisNodeExecutor` bridging to existing pipeline
- `PlaybookOrchestrationService` with sequential execution
- Node management API endpoints
- Basic validation

**Outcome**: Multi-node playbooks via API, sequential execution

### Phase 2: Visual Builder

**Deliverables**:
- React 18 playbook-builder app with React Flow
- Node palette, properties panel, canvas controls
- Scope selector with filtering
- PlaybookBuilderHost PCF with iframe
- Host-builder communication
- Output schema validation in builder

**Outcome**: Visual drag-and-drop builder

### Phase 3: Parallel Execution + Delivery

**Deliverables**:
- Parallel node execution with throttling
- `CreateTaskNodeExecutor`, `SendEmailNodeExecutor`
- `DeliverOutputNodeExecutor`
- Power Apps template integration
- `ITemplateEngine` (Handlebars.NET)
- Execution visualization overlay

**Outcome**: Full orchestration with delivery outputs

### Phase 4: Advanced Features

**Deliverables**:
- `ConditionNodeExecutor` for branching
- Per-node model selection UI
- Confidence score display in UI
- Playbook templates library
- Execution history and metrics

**Outcome**: Conditional branching, confidence visibility, templates

### Phase 5: Production Hardening

**Deliverables**:
- Comprehensive error handling and retry
- Timeout management
- Cancellation support
- Audit logging
- Performance optimization

**Outcome**: Production-ready system

---

## 14. ADR Compliance

| ADR | Requirement | Compliance |
|-----|-------------|------------|
| ADR-001 | Minimal API + BackgroundService | All new endpoints use Minimal API |
| ADR-008 | Endpoint filters for auth | `PlaybookAuthorizationFilter` on all endpoints |
| ADR-009 | Redis-first caching | Cache resolved scopes, execution graphs |
| ADR-010 | DI minimalism (≤15) | ~10 new services, within budget |
| ADR-013 | Extend BFF, not separate service | All orchestration within Sprk.Bff.Api |
| ADR-022 | React 16 for PCF | Iframe pattern isolates React 18 builder |

---

## 15. Success Criteria

| Metric | Target |
|--------|--------|
| Playbook creation time | <10 minutes for 5-node playbook |
| Execution latency (5 nodes) | <60 seconds |
| UI responsiveness | <100ms for drag/drop operations |
| Backward compatibility | 100% existing playbooks work |

---

## 16. Open Questions (Resolved)

| # | Question | Resolution |
|---|----------|------------|
| 1 | Multiple skills per node? | Yes - skills compose well |
| 2 | Multiple tools per node? | No - single tool per node |
| 3 | Multiple knowledge per node? | Yes - multiple contexts valid |
| 4 | Output terminology? | AI Output vs Delivery Output |
| 5 | POML for playbooks? | Export/import only, Dataverse is SoR |
| 6 | Template engine? | Handlebars.NET (logic-less, safe) |
| 7 | Delivery templates? | Power Apps Phase 1, custom later |
| 8 | Template security complexity? | Minimal - use safe engine, done |
| 9 | AI accuracy risks? | Confidence scores, citations, human review gate |

---

## 17. References

- [NODE-PLAYBOOK-BUILDER-DESIGN-V2.md](NODE-PLAYBOOK-BUILDER-DESIGN-V2.md) - Initial design
- [DESIGN-REVIEW-AND-RECOMMENDATIONS.md](DESIGN-REVIEW-AND-RECOMMENDATIONS.md) - Design review
- [prototype-wire-frame-01.jpg](reference/prototype-wire-frame-01.jpg) - Visual prototype
- ADR-001: Minimal API and Workers
- ADR-008: Endpoint Filters for Authorization
- ADR-010: DI Minimalism
- ADR-013: AI Architecture
- ADR-022: PCF Platform Libraries

---

*This design document consolidates the design review and stakeholder feedback. Ready for `/design-to-spec` transformation.*
