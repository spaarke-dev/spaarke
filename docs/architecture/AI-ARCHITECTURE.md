# Spaarke AI Architecture

> **Version**: 3.0
> **Last Updated**: February 21, 2026
> **Audience**: Claude Code, AI agents, engineers
> **Purpose**: Technical reference for the Spaarke AI platform component framework
> **Supersedes**: `AI-PLAYBOOK-ARCHITECTURE.md` (v2.0), `AI-ANALYSIS-PLAYBOOK-SCOPE-DESIGN.md` (v2.0)
> **Related ADRs**: ADR-001, ADR-007, ADR-008, ADR-009, ADR-010, ADR-013, ADR-014, ADR-015, ADR-016, ADR-022

---

## Four-Tier Architecture

Spaarke AI is organized into four tiers. Each tier has distinct responsibilities and can evolve independently.

```
 ┌─────────────────────────────────────────────────────────────────────┐
 │  TIER 1: SCOPE LIBRARY (Spaarke IP)                                │
 │  Reusable AI primitives stored in Dataverse                        │
 │  Actions · Skills · Knowledge · Tools · Outputs                    │
 │  Independent of any execution engine                               │
 ├─────────────────────────────────────────────────────────────────────┤
 │  TIER 2: COMPOSITION PATTERNS                                      │
 │  How scopes are assembled and invoked                              │
 │  Playbooks (visual canvas)  ·  SprkChat (conversational)           │
 │  Standalone invocation (API)  ·  Background jobs                   │
 ├─────────────────────────────────────────────────────────────────────┤
 │  TIER 3: EXECUTION RUNTIME                                         │
 │  Where AI logic actually runs                                      │
 │  In-Process (current) → Microsoft Agent Framework (future)         │
 │  PlaybookExecutionEngine · PlaybookOrchestrationService            │
 ├─────────────────────────────────────────────────────────────────────┤
 │  TIER 4: AZURE INFRASTRUCTURE                                      │
 │  Cloud services backing everything                                 │
 │  Azure OpenAI · Azure AI Search · Document Intelligence            │
 │  Redis · Service Bus · AI Foundry (future hosting option)          │
 └─────────────────────────────────────────────────────────────────────┘
```

### Key Architectural Principles

1. **Playbooks are the "frontend"** -- the Spaarke-specific composition and management UI for AI workflows. The execution backend is flexible.
2. **Scopes are independent primitives** -- consumable by playbooks, SprkChat, standalone API calls, and background jobs without requiring a playbook.
3. **AI nodes are backend-flexible** -- a node can execute in-process (current), via Microsoft Agent Framework (future), or as a published AI Foundry agent (future).
4. **Workflow nodes stay Spaarke** -- CreateTask, SendEmail, UpdateRecord, Condition, DeliverOutput nodes always run as Spaarke code.
5. **AI Foundry is infrastructure** -- it provides model hosting, Foundry IQ knowledge bases, and Agent Service runtime. It does not compete with the scope library.

---

## Component Map

### File Structure

```
src/server/api/Sprk.Bff.Api/
├── Api/Ai/                          # Minimal API endpoints
│   ├── AnalysisEndpoints.cs         # Execute, Continue, Save, Export
│   ├── PlaybookEndpoints.cs         # Playbook CRUD
│   ├── PlaybookRunEndpoints.cs      # Execute, Status, History, Cancel
│   ├── AiPlaybookBuilderEndpoints.cs  # Conversational builder
│   ├── NodeEndpoints.cs             # Node CRUD & validation
│   ├── ScopeEndpoints.cs            # Scope CRUD (Actions, Skills, Knowledge, Tools)
│   ├── HandlerEndpoints.cs          # Tool handler discovery
│   ├── SemanticSearchEndpoints.cs   # RAG search
│   ├── RagEndpoints.cs              # RAG pipeline
│   ├── ModelEndpoints.cs            # Model/deployment management
│   ├── VisualizationEndpoints.cs    # Data visualization
│   └── RecordMatchEndpoints.cs      # Record matching
├── Services/Ai/                     # Core AI services (~124 files)
│   ├── Nodes/                       # Node executor implementations
│   ├── Tools/                       # Tool handler implementations
│   ├── Builder/                     # Playbook builder agent
│   ├── SemanticSearch/              # RAG search pipeline
│   ├── Export/                      # Export format handlers
│   ├── Delivery/                    # Email/Word templates
│   ├── Handlers/                    # GenericAnalysisHandler
│   ├── Prompts/                     # System prompt definitions
│   ├── Testing/                     # Playbook test infrastructure
│   └── Visualization/              # Chart/visualization generation
├── Models/Ai/                       # DTOs and request/response models (~37 files)
│   └── SemanticSearch/              # Search-specific models
└── BackgroundWorkers/               # Background processing

src/client/pcf/
├── PlaybookBuilderHost/             # Visual playbook canvas (PCF control)
│   └── control/
│       ├── PlaybookBuilderHost.tsx   # Main React component
│       ├── index.ts                  # PCF lifecycle + auto-save
│       ├── stores/
│       │   ├── canvasStore.ts        # Zustand state (nodes/edges)
│       │   └── executionStore.ts     # Execution state
│       ├── components/
│       │   ├── Canvas/Canvas.tsx     # React Flow wrapper
│       │   ├── Palette/NodePalette.tsx  # Draggable node types
│       │   ├── Properties/PropertiesPanel.tsx  # Node config
│       │   ├── Nodes/
│       │   │   ├── BaseNode.tsx
│       │   │   ├── AiAnalysisNode.tsx
│       │   │   ├── AiCompletionNode.tsx
│       │   │   └── ConditionNode.tsx
│       │   └── AiAssistant/AiAssistantModal.tsx
│       └── services/
│           └── AiPlaybookService.ts  # BFF API client
└── UniversalQuickCreate/            # Document quick-create with AI
    └── control/components/
        ├── AiSummaryPanel.tsx
        └── AiSummaryCarousel.tsx
```

---

## Tier 1: Scope Library

Scopes are reusable AI primitives stored as Dataverse records. They are the building blocks for all AI composition patterns.

### Scope Types

| Scope | Entity | Purpose | Role in Execution |
|-------|--------|---------|-------------------|
| **Actions** | `sprk_analysisaction` | System prompt templates | Define LLM persona and behavior |
| **Skills** | `sprk_analysisskill` | Prompt fragments | Add specialized instructions to prompts |
| **Knowledge** | `sprk_analysisknowledge` | RAG context sources | Provide domain context to LLM |
| **Tools** | `sprk_analysistool` | Executable handlers | Call LLM and process responses |
| **Outputs** | Playbook field mappings | Field mappings | Map results to Dataverse fields |

### Prompt Composition

```
Final LLM Prompt = Action.SystemPrompt
                 + Skill.PromptFragment(s)
                 + Knowledge (RAG context or inline)
                 + Document text
```

### Scope Ownership Model

Every scope has `OwnerType` and `IsImmutable`:

| Prefix | OwnerType | Mutable | Description |
|--------|-----------|---------|-------------|
| `SYS-` | System | No | Spaarke-provided, immutable |
| `CUST-` | Customer | Yes | Customer-created or extended |

Scopes support inheritance via:
- `ParentScopeId` -- extends a parent scope
- `BasedOnId` -- cloned from another scope (SaveAs pattern)

### Scope Resolution

**Service**: `IScopeResolverService` / `ScopeResolverService`
**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs`

Scopes are loaded from Dataverse at execution time via N:N relationship queries:

```csharp
// IScopeResolverService key operations
Task<ResolvedScopes> ResolvePlaybookScopesAsync(Guid playbookId, CancellationToken ct);
Task<AnalysisAction?> GetActionAsync(Guid actionId, CancellationToken ct);
Task<AnalysisTool?> GetToolAsync(Guid toolId, CancellationToken ct);
Task<AnalysisSkill?> GetSkillAsync(Guid skillId, CancellationToken ct);
Task<AnalysisKnowledge?> GetKnowledgeAsync(Guid knowledgeId, CancellationToken ct);
// + CRUD, SaveAs, Extend, Search operations
```

### Core Scope Types

```csharp
// src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs

public record AnalysisAction {
    Guid Id; string Name; string SystemPrompt;
    ActionType ActionType; ScopeOwnerType OwnerType;
    bool IsImmutable; Guid? ParentScopeId; Guid? BasedOnId;
}

public record AnalysisSkill {
    Guid Id; string Name; string PromptFragment; string? Category;
    ScopeOwnerType OwnerType; bool IsImmutable;
    Guid? ParentScopeId; Guid? BasedOnId;
}

public record AnalysisKnowledge {
    Guid Id; string Name; KnowledgeType Type;
    string? Content; Guid? DocumentId; Guid? DeploymentId;
    ScopeOwnerType OwnerType; bool IsImmutable;
    Guid? ParentScopeId; Guid? BasedOnId;
}

public record AnalysisTool {
    Guid Id; string Name; ToolType Type;
    string? HandlerClass; string? Configuration;
    ScopeOwnerType OwnerType; bool IsImmutable;
    Guid? ParentScopeId; Guid? BasedOnId;
}

public enum KnowledgeType { Inline, Document, RagIndex }
public enum ToolType {
    EntityExtractor, ClauseAnalyzer, DocumentClassifier,
    Summary, RiskDetector, ClauseComparison,
    DateExtractor, FinancialCalculator, Custom
}
public enum ScopeOwnerType { System, Customer }
```

### Scope Management

**Service**: `IScopeManagementService` / `ScopeManagementService`
**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/IScopeManagementService.cs`

Write operations with ownership validation (SYS- scopes are immutable):

```csharp
// ScopeType enum: Action, Skill, Knowledge, Tool, Output
// Request types: CreateActionRequest, UpdateActionRequest, etc.
```

**Endpoints**: `ScopeEndpoints.cs` -- full CRUD for all scope types.

### Dataverse Entity Model

```
                         TYPE LOOKUP TABLES (Categorization Only)
┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐
│ sprk_analysis    │  │ sprk_aiskill     │  │ sprk_ai          │  │ sprk_ai          │
│   actiontype     │  │     type         │  │  knowledgetype   │  │    tooltype      │
│ "Extraction"     │  │ "Document        │  │ "RAG Index"      │  │ "Entity          │
│ "Classification" │  │  Analysis"       │  │ "Inline Text"    │  │  Extraction"     │
│ "Summarization"  │  │ "Compliance"     │  │ "Document Ref"   │  │ "Classification" │
└────────┬─────────┘  └────────┬─────────┘  └────────┬─────────┘  └────────┬─────────┘
         │ Lookup              │ Lookup              │ Lookup              │ Lookup
         ▼                     ▼                     ▼                     ▼
┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐
│ sprk_analysis    │  │ sprk_analysis    │  │ sprk_analysis    │  │ sprk_analysis    │
│     action       │  │     skill        │  │    knowledge     │  │      tool        │
│ MASTER LIST      │  │ MASTER LIST      │  │ MASTER LIST      │  │ MASTER LIST      │
│ ACT-001..008     │  │ SKL-001..010     │  │ KNW-001..010     │  │ TL-001..008      │
└────────┬─────────┘  └────────┬─────────┘  └────────┬─────────┘  └────────┬─────────┘
         │ N:N                 │ N:N                 │ N:N                 │ N:N
         └─────────────────────┼─────────────────────┼─────────────────────┘
                               ▼                     ▼
                     ┌──────────────────────────────────────┐
                     │       sprk_analysisplaybook          │
                     │ PB-001 "Quick Document Review"       │
                     │ PB-002 "Full Contract Analysis"      │
                     │ ...                                  │
                     │ Fields: sprk_name, sprk_description  │
                     │   sprk_canvaslayoutjson              │
                     │   sprk_outputtypeid, sprk_ispublic   │
                     └──────────────────────────────────────┘
```

**N:N Relationship Tables**:

| Table | Connects |
|-------|----------|
| `sprk_analysisplaybook_action` | Playbook <-> Action |
| `sprk_playbook_skill` | Playbook <-> Skill |
| `sprk_playbook_knowledge` | Playbook <-> Knowledge |
| `sprk_playbook_tool` | Playbook <-> Tool |

**Entity Field Details**:

| Entity | Key Fields |
|--------|------------|
| `sprk_analysisaction` | `sprk_name`, `sprk_systemprompt` (100K), `sprk_actiontypeid` (lookup) |
| `sprk_analysisskill` | `sprk_name`, `sprk_promptfragment` (100K), `sprk_skilltypeid` (lookup), `sprk_category` |
| `sprk_analysisknowledge` | `sprk_name`, `sprk_content` (100K), `sprk_type` (OptionSet: Inline/Document/RagIndex), `sprk_documentid`, `sprk_deploymentid` |
| `sprk_analysistool` | `sprk_name`, `sprk_handlerclass` (String 200), `sprk_configuration` (100K JSON), `sprk_tooltypeid` (lookup) |

### Scope Catalog

#### Actions (ACT-001 through ACT-008)

| ID | Name | ActionType | System Prompt Focus |
|----|------|------------|---------------------|
| ACT-001 | Extract Entities | Extraction | Entity extraction specialist |
| ACT-002 | Analyze Clauses | Analysis | Contract clause analyzer |
| ACT-003 | Classify Document | Classification | Document classification |
| ACT-004 | Summarize Content | Summarization | Summarization specialist |
| ACT-005 | Detect Risks | Analysis | Risk identification |
| ACT-006 | Compare Clauses | Comparison | Comparative analysis |
| ACT-007 | Extract Dates | Extraction | Date extraction |
| ACT-008 | Calculate Values | Extraction | Financial analysis |

#### Skills (SKL-001 through SKL-010)

| ID | Name | Applicable Documents |
|----|------|---------------------|
| SKL-001 | Contract Analysis | CONTRACT, AMENDMENT |
| SKL-002 | Invoice Processing | INVOICE |
| SKL-003 | NDA Review | NDA |
| SKL-004 | Lease Review | LEASE |
| SKL-005 | Employment Contract | EMPLOYMENT |
| SKL-006 | SLA Analysis | SLA |
| SKL-007 | Compliance Check | ANY |
| SKL-008 | Executive Summary | ANY |
| SKL-009 | Risk Assessment | CONTRACT, NDA, LEASE |
| SKL-010 | Clause Comparison | CONTRACT, NDA |

#### Knowledge (KNW-001 through KNW-010)

| ID | Name | Type |
|----|------|------|
| KNW-001 | Standard Contract Terms | Reference Library |
| KNW-002 | Regulatory Guidelines | Reference Library |
| KNW-003 | Best Practices | Reference Library |
| KNW-004 | Risk Categories | Taxonomy |
| KNW-005 | Document Type Definitions | Taxonomy |
| KNW-006 | Standard NDA Terms | Reference Library |
| KNW-007 | Standard Lease Terms | Reference Library |
| KNW-008 | Employment Standards | Reference Library |
| KNW-009 | SLA Benchmarks | Reference Library |
| KNW-010 | Due Diligence Checklist | Checklist |

#### Tools (TL-001 through TL-008)

| ID | Handler Class | Purpose |
|----|---------------|---------|
| TL-001 | `EntityExtractorHandler` | Extract named entities |
| TL-002 | `ClauseAnalyzerHandler` | Analyze document clauses |
| TL-003 | `DocumentClassifierHandler` | Classify document type |
| TL-004 | `SummaryHandler` | Generate summaries |
| TL-005 | `RiskDetectorHandler` | Detect risks |
| TL-006 | `ClauseComparisonHandler` | Compare to standards |
| TL-007 | `DateExtractorHandler` | Extract dates |
| TL-008 | `FinancialCalculatorHandler` | Financial calculations |

**Handler location**: `src/server/api/Sprk.Bff.Api/Services/Ai/Tools/*.cs`

---

## Tier 2: Composition Patterns

### Playbooks (Visual Canvas)

Playbooks are the primary composition pattern -- visual node-based workflows stored as Dataverse records.

**Entity**: `sprk_analysisplaybook`
**Canvas field**: `sprk_canvaslayoutjson` (serialized JSON of nodes and edges)
**Builder PCF**: `src/client/pcf/PlaybookBuilderHost/`

#### Node Types

```typescript
type PlaybookNodeType =
  | 'aiAnalysis'      // AI analysis (LLM call) — backend-flexible
  | 'aiCompletion'    // AI completion (LLM call) — backend-flexible
  | 'condition'       // Conditional branching — always Spaarke code
  | 'deliverOutput'   // Output delivery — always Spaarke code
  | 'createTask'      // Task creation — always Spaarke code
  | 'sendEmail'       // Email action — always Spaarke code
  | 'updateRecord'    // Dataverse update — always Spaarke code
  | 'wait';           // Wait/delay — always Spaarke code
```

#### Node Data Structure

```typescript
// src/client/pcf/PlaybookBuilderHost/control/stores/canvasStore.ts
interface PlaybookNodeData {
  label: string;
  type: PlaybookNodeType;
  outputVariable?: string;       // Variable name for output reference
  timeoutSeconds?: number;       // 30-3600s
  retryCount?: number;           // 0-5
  conditionJson?: string;        // Condition expression
  skillIds?: string[];           // Linked skills
  knowledgeIds?: string[];       // Linked knowledge
  toolId?: string;               // Linked tool
  modelDeploymentId?: string;    // AI model selection
}
```

#### Canvas JSON Format

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

#### Playbooks as "Frontend"

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
    ├── Condition → Branch evaluation
    └── DeliverOutput → Output assembly
```

A single playbook can mix backends per node. This is not an either/or decision.

#### Playbook Catalog (PB-001 through PB-010)

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

#### Playbook-Scope Matrix

| Playbook | Skills | Actions | Knowledge | Tools |
|----------|--------|---------|-----------|-------|
| PB-001 | SKL-008 | ACT-001,003,004 | KNW-005 | TL-001,003,004 |
| PB-002 | SKL-001,009,010 | ACT-001-006 | KNW-001,003,004 | TL-001-006 |
| PB-003 | SKL-003,009 | ACT-001,002,004,005 | KNW-001,003,006 | TL-001,002,004,005 |
| PB-004 | SKL-004,009 | ACT-001,002,004,005,007 | KNW-003,004,007 | TL-001,002,004,005,007 |
| PB-005 | SKL-005,007 | ACT-001,002,004,005 | KNW-002,008 | TL-001,002,004,005 |
| PB-006 | SKL-002 | ACT-001,003,008 | KNW-005 | TL-001,003,008 |
| PB-007 | SKL-006,007 | ACT-001,002,004 | KNW-002,009 | TL-001,002,004 |
| PB-008 | SKL-008,009 | ACT-001,003,004,005 | KNW-004,010 | TL-001,003,004,005 |
| PB-009 | SKL-007,009 | ACT-002,004,005 | KNW-002,003 | TL-002,004,005 |
| PB-010 | SKL-009 | ACT-001,005 | KNW-004 | TL-001,005 |

#### Document Type to Playbook Mapping

| Document Type | Primary Playbook | Alternatives |
|---------------|------------------|-------------|
| CONTRACT | PB-002 Full Contract | PB-001, PB-010 |
| NDA | PB-003 NDA Review | PB-001, PB-010 |
| LEASE | PB-004 Lease Review | PB-001, PB-002 |
| EMPLOYMENT | PB-005 Employment | PB-001 |
| SLA | PB-007 SLA Analysis | PB-002 |
| INVOICE | PB-006 Invoice | PB-001 |
| AMENDMENT | PB-002 Full Contract | PB-001 |
| POLICY | PB-009 Compliance | PB-001 |
| Unknown | PB-001 Quick Review | PB-010 |

### SprkChat (Conversational)

SprkChat provides scope access through conversational AI. Scopes become agent tools.

**Design document**: `projects/ai-document-analysis-enhancements/sk-analysis-chat-design.md`

The `IChatContextProvider` pattern resolves scopes to Agent Framework tools at runtime:

```csharp
// Conceptual: SprkChat scope → Agent Framework tool conversion
foreach (var tool in scopes.Tools)
{
    var handler = _toolHandlerRegistry.GetHandler(tool.HandlerName);
    if (handler != null)
    {
        var aiFunction = WrapToolHandlerAsAIFunction(handler, tool.Configuration);
        tools.Add(aiFunction);
    }
}
```

### Standalone Invocation

Scopes can be invoked directly via API without a playbook:

- `POST /api/ai/analysis` -- execute scopes on document(s)
- `POST /api/ai/search` -- semantic search using knowledge scopes
- `GET /api/ai/handlers` -- discover available tool handlers

---

## Tier 3: Execution Runtime

### PlaybookBuilderHost PCF Control

**Path**: `src/client/pcf/PlaybookBuilderHost/`
**Version**: v2.13.x (Direct Rendering)

```
PlaybookBuilderHost (PCF index.ts)
└── FluentProvider (Fluent UI v9 theme)
    └── PlaybookBuilderHostApp (React component)
        ├── Header (title, dirty indicator)
        ├── BuilderLayout
        │   ├── NodePalette (left sidebar, draggable node types)
        │   ├── Canvas (center, React Flow v10)
        │   │   ├── BaseNode
        │   │   ├── AiAnalysisNode
        │   │   ├── AiCompletionNode
        │   │   ├── ConditionNode
        │   │   └── Custom edge types
        │   └── PropertiesPanel (right sidebar)
        │       └── NodePropertiesForm
        │           ├── ScopeSelector (skills, knowledge, tools)
        │           ├── ModelSelector (AI model dropdown)
        │           └── ConditionEditor
        └── Footer (version badge)
```

**PCF Bound Properties** (`ControlManifest.Input.xml`):

| Property | Type | Bound Field | Purpose |
|----------|------|-------------|---------|
| `playbookId` | SingleLine.Text | (auto-detected) | Record ID fallback |
| `playbookName` | SingleLine.Text | `sprk_name` | Playbook name |
| `playbookDescription` | Multiple | `sprk_description` | Description |
| `canvasJson` | Multiple | `sprk_canvaslayoutjson` | Canvas state |
| `apiBaseUrl` | SingleLine.Text | -- | BFF API URL |
| `isDirty` | TwoOptions | -- | Unsaved changes flag |

**Auto-Save**: Dual-path saving (500ms debounce):
1. `notifyOutputChanged()` -- standard PCF bound property update (requires form Save)
2. `webAPI.updateRecord()` -- direct Dataverse PATCH (immediate persistence)

**ADR-022 Compliance**: React 16 APIs, react-flow-renderer v10, Fluent UI v9.

### PlaybookExecutionEngine (Dual Mode)

**Interface**: `src/server/api/Sprk.Bff.Api/Services/Ai/IPlaybookExecutionEngine.cs`
**Implementation**: `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookExecutionEngine.cs`

Supports two execution modes:

| Mode | Entry Point | Use Case |
|------|-------------|----------|
| **Batch** | `ExecuteBatchAsync()` | Document analysis via node graph |
| **Conversational** | `ExecuteConversationalAsync()` | Builder UI interactions |

```csharp
// Key types
public enum ExecutionMode { Batch, Conversational }

public class ConversationContext {
    ConversationMessage[] History;
    string CurrentMessage;
    SessionState SessionState;
    Guid? PlaybookId;
}

public class SessionState {
    string SessionId;
    CanvasState CanvasState;
    BuildPlan? ActiveBuildPlan;
    int? CurrentBuildStep;
    Dictionary<string, object?> SessionVariables;
}

public class BuilderResult {
    BuilderResultType Type;  // Thinking, Message, CanvasOperation,
                             // Clarification, PlanPreview, Complete, Error
    string? Text;
    CanvasPatch? Patch;
    BuildPlan? PlanPreview;
    SessionState? UpdatedState;
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
      1. Build ExecutionGraph (DAG from canvas JSON)
      2. Topological sort → execution batches
      3. Execute batches (max 3 parallel nodes, throttled)
      4. Stream PlaybookStreamEvents per node
      5. Store node outputs in PlaybookRunContext
      6. Rate limit handling per ADR-016
```

**Streaming events**:

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

    // Methods
    void StoreNodeOutput(NodeOutput output);
    NodeOutput? GetOutput(string variableName);
    PlaybookRunMetrics GetMetrics(int totalNodes);
    NodeExecutionContext CreateNodeContext(...);
}
```

### Node Executor Framework

**Directory**: `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/`

| File | Node Type | Backend |
|------|-----------|---------|
| `INodeExecutor.cs` | Interface | -- |
| `INodeExecutorRegistry.cs` | Registry interface | -- |
| `NodeExecutorRegistry.cs` | Registry implementation | -- |
| `AiAnalysisNodeExecutor.cs` | AI Analysis | Backend-flexible (currently in-process) |
| `ConditionNodeExecutor.cs` | Condition | Always Spaarke code |
| `CreateTaskNodeExecutor.cs` | CreateTask | Always Spaarke code |
| `UpdateRecordNodeExecutor.cs` | UpdateRecord | Always Spaarke code |
| `SendEmailNodeExecutor.cs` | SendEmail | Always Spaarke code |
| `DeliverOutputNodeExecutor.cs` | DeliverOutput | Always Spaarke code |

### Tool Handler Framework

**Interface**: `src/server/api/Sprk.Bff.Api/Services/Ai/IAnalysisToolHandler.cs`
**Registry**: `src/server/api/Sprk.Bff.Api/Services/Ai/ToolHandlerRegistry.cs`

```csharp
public interface IAnalysisToolHandler {
    string HandlerId { get; }
    ToolHandlerMetadata Metadata { get; }
    IReadOnlyList<ToolType> SupportedToolTypes { get; }

    ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool);
    Task<ToolResult> ExecuteAsync(ToolExecutionContext context, AnalysisTool tool, CancellationToken ct);
}

public record ToolResult {
    string HandlerId;
    Guid ToolId;
    string ToolName;
    bool Success;
    JsonElement? Data;      // Structured JSON output
    string? Summary;        // Human-readable text
    double? Confidence;     // 0.0-1.0
    ToolExecutionMetadata Execution;
}
```

#### Three-Tier Handler Resolution

```
Tier 1: Configuration (Dataverse)
  sprk_analysistool.sprk_handlerclass → handler name (optional)
  sprk_analysistool.sprk_configuration → JSON config
         │
         ▼
Tier 2: GenericAnalysisHandler (95% of cases)
  If handlerclass is NULL or not found → GenericAnalysisHandler
  Configuration-driven: operation, prompt_template, output_schema, temperature
  No code deployment required for new tools
         │
         ▼
Tier 3: Custom Handlers (complex scenarios)
  EntityExtractorHandler, SummaryHandler, ClauseAnalyzerHandler, etc.
  Registered in DI at startup via ToolFrameworkExtensions
  Discoverable via IToolHandlerRegistry
```

**Resolution priority**:
1. Check `sprk_handlerclass` -- if populated, look up registered handler
2. Fall back to `GenericAnalysisHandler` -- if handler not found or NULL
3. Type-based lookup -- match by `ToolType` enum

**Implemented Handlers**:

| Handler | File | Purpose |
|---------|------|---------|
| `EntityExtractorHandler` | `Tools/EntityExtractorHandler.cs` | Named entity extraction |
| `ClauseAnalyzerHandler` | `Tools/ClauseAnalyzerHandler.cs` | Contract clause analysis |
| `ClauseComparisonHandler` | `Tools/ClauseComparisonHandler.cs` | Clause comparison |
| `DocumentClassifierHandler` | `Tools/DocumentClassifierHandler.cs` | Document classification |
| `SummaryHandler` | `Tools/SummaryHandler.cs` | Document summarization |
| `RiskDetectorHandler` | `Tools/RiskDetectorHandler.cs` | Risk detection |
| `DateExtractorHandler` | `Tools/DateExtractorHandler.cs` | Date extraction |
| `FinancialCalculatorHandler` | `Tools/FinancialCalculatorHandler.cs` | Financial calculations |
| `SemanticSearchToolHandler` | `Tools/SemanticSearchToolHandler.cs` | RAG search |
| `DataverseUpdateToolHandler` | `Tools/DataverseUpdateToolHandler.cs` | Dataverse updates |
| `GenericAnalysisHandler` | `Handlers/GenericAnalysisHandler.cs` | Configuration-driven (default) |

### Execution Flow (Batch Mode)

```
POST /api/ai/playbook-runs (PlaybookRunEndpoints.cs)
  │
  ▼
PlaybookOrchestrationService.ExecutePlaybookAsync()
  │
  ├── Step 1: Load Playbook (PlaybookService.GetPlaybookAsync)
  ├── Step 2: Detect mode (Legacy vs NodeBased)
  ├── Step 3: Create PlaybookRunContext (RunId, DocumentIds, TenantId)
  ├── Step 4: Extract document text (TextExtractorService)
  │
  ├── FOR EACH execution batch (topological order, max 3 parallel):
  │   │
  │   ├── Step 5: Resolve node scopes (IScopeResolverService)
  │   ├── Step 6: Create NodeExecutionContext
  │   ├── Step 7: Route to NodeExecutor (NodeExecutorRegistry)
  │   │   │
  │   │   ├── AiAnalysisNodeExecutor:
  │   │   │   ├── Build prompt: Action.SystemPrompt + Skills + Knowledge + Document
  │   │   │   ├── Resolve tool handler (ToolHandlerRegistry)
  │   │   │   ├── Execute: handler.ExecuteAsync()
  │   │   │   │   └── IOpenAiClient.GetCompletionAsync() → Azure OpenAI
  │   │   │   └── Return NodeOutput (Data JSON + Summary text)
  │   │   │
  │   │   ├── ConditionNodeExecutor:
  │   │   │   └── Evaluate conditionJson against previous NodeOutputs
  │   │   │
  │   │   └── CreateTask/SendEmail/UpdateRecord/DeliverOutput:
  │   │       └── Execute Spaarke workflow action
  │   │
  │   ├── Step 8: Store output (PlaybookRunContext.StoreNodeOutput)
  │   └── Step 9: Stream event (PlaybookStreamEvent)
  │
  ├── Step 10: Map outputs to document fields (DocumentProfileFieldMapper)
  ├── Step 11: Update Dataverse (DataverseService.UpdateDocumentFieldsAsync)
  ├── Step 12: Store analysis output (sprk_analysisoutput.sprk_output_rtf)
  └── Step 13: Complete run (metrics, status)
```

### Dual Output Paths

| Path | Data | Storage | Display |
|------|------|---------|---------|
| **Analysis Output** | `ToolResult.Summary` (text) | `sprk_analysisoutput.sprk_output_rtf` | AiSummaryPanel |
| **Document Fields** | `ToolResult.Data` (JSON) | `sprk_document.*` fields via FieldMapper | Document form fields |

### Builder Agent (Conversational Mode)

**Directory**: `src/server/api/Sprk.Bff.Api/Services/Ai/Builder/`

| File | Purpose |
|------|---------|
| `BuilderAgentService.cs` | AI agent for conversational playbook building |
| `BuilderToolDefinitions.cs` | Tool definitions available to builder |
| `BuilderToolExecutor.cs` | Executes builder tool calls |
| `BuilderToolCall.cs` | Represents builder tool invocations |
| `BuilderScopeImporter.cs` | Imports scopes into builder context |
| `AiBuilderErrors.cs` | Error handling |

Builder endpoint: `AiPlaybookBuilderEndpoints.cs`
System prompt: `Prompts/PlaybookBuilderSystemPrompt.cs`

### RAG Pipeline

**Services**:
- `IRagService` / `RagService` -- RAG pipeline coordination
- `ISemanticSearchService` / `SemanticSearchService` -- Azure AI Search queries
- `IEmbeddingCache` / `EmbeddingCache` -- Redis-backed embedding cache (ADR-009)
- `IFileIndexingService` / `FileIndexingService` -- Document indexing

**Search flow**:

```
Query → EmbeddingCache (Redis) → Azure OpenAI (embedding)
                                        │
                                        ▼
                              Azure AI Search (hybrid: BM25 + Vector + Semantic)
                                        │
                                        ▼
                              Security filter (tenantId, permissions)
                                        │
                                        ▼
                              Semantic reranking → Results
```

**Search index**: `spaarke-knowledge-index` (1536-dim embeddings, HNSW, cosine similarity)
**Multi-tenant isolation**: OData filter on `tenantId`

### Export & Delivery

**Directory**: `src/server/api/Sprk.Bff.Api/Services/Ai/Export/`

| Service | Purpose |
|---------|---------|
| `ExportServiceRegistry` | Registry of format handlers |
| `DocxExportService` | Word export |
| `PdfExportService` | PDF export |
| `EmailExportService` | Email delivery |

---

## Tier 4: Azure Infrastructure

### Current Azure Services

| Service | Resource Name (Dev) | Purpose |
|---------|--------------------|---------|
| Azure OpenAI | `spaarke-openai-dev` | LLM completions + embeddings |
| Azure AI Search | `spaarke-search-dev` | Vector + hybrid search |
| Document Intelligence | `spaarke-docintel-dev` | PDF/image text extraction |
| Azure Redis Cache | -- | Embedding cache, search result cache |
| Azure Service Bus | -- | Background job queuing |
| Azure Key Vault | `spaarke-spekvcert` | Secrets management |
| Azure App Service | `spe-api-dev-67e2xz` | BFF API hosting |

### AI Foundry (Future Infrastructure)

AI Foundry is an optional infrastructure evolution, not a competing scope library:

| Foundry Component | Spaarke Usage | Timeline |
|-------------------|---------------|----------|
| **Foundry IQ** (knowledge bases) | Complement Knowledge scopes with multi-source agentic retrieval | Post-GA |
| **Agent Service** (managed runtime) | Optional hosting for AI nodes (alternative to in-process) | Post-GA |
| **Published Agent Applications** | Stable endpoints with Entra identity for AI nodes | Post-GA |
| **Model Router** | Dynamic model selection per task for cost optimization | When GA |

**Key principle**: If we adopt Foundry Agent Service, the Playbook System remains as the configuration/composition layer above it. Foundry provides runtime; playbooks define workflows.

### Deployment Models

| Model | AI Resources | Data Isolation | User Identity |
|-------|-------------|----------------|---------------|
| **Model 1: Spaarke-Hosted** | Spaarke Azure subscription | Logical (tenantId filters) | Guest Entra ID |
| **Model 2: Customer-Hosted** | Customer Azure subscription (BYOK) | Physical (dedicated) | Internal Entra ID |

Feature parity is identical across both models.

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
                                                            │
Future:  PlaybookOrchestrationService → Agent Framework Graph Workflow
         (thin translation layer)          │
                                           ├── IChatClient (in-process)
                                           ├── AI Foundry Agent (hosted)
                                           └── Custom endpoint (external)
```

The PlaybookOrchestrationService evolves from a full execution engine to a thin "workflow compiler" that translates playbook canvas definitions into Agent Framework graph-based workflows. The scope library stays as Spaarke IP; the execution engine becomes a translation layer.

---

## ADR Compliance

| ADR | Constraint | Implementation |
|-----|-----------|----------------|
| ADR-001 | No Azure Functions | AI endpoints via Minimal API; indexing via BackgroundService |
| ADR-007 | No Graph SDK leakage | AI services use SpeFileStore for document access |
| ADR-008 | Endpoint filters for auth | AiAuthorizationFilter per-resource checks |
| ADR-009 | Redis-first caching | EmbeddingCache with SHA256 keys, 7-day TTL |
| ADR-010 | DI minimalism (<=15) | Tool handlers registered via ToolFrameworkExtensions |
| ADR-013 | AI Tool Framework | Extensible IAnalysisToolHandler pattern |
| ADR-014 | Dual storage pattern | Analysis Output (RTF) + Document fields (JSON) |
| ADR-015 | AI observability | Application Insights logging at each execution step |
| ADR-016 | Soft failure handling | Per-node error isolation, rate limit backoff |
| ADR-022 | React 16 + Fluent v9 | PlaybookBuilderHost uses react-flow-renderer v10, Fluent UI v9 |

---

## Testing Infrastructure

**Directory**: `src/server/api/Sprk.Bff.Api/Services/Ai/Testing/`

| Component | Purpose |
|-----------|---------|
| `ProductionTestExecutor` | Full execution against Azure OpenAI |
| `QuickTestExecutor` | Basic validation (no LLM calls) |
| `MockTestExecutor` | Mock responses for development |
| `MockDataGenerator` | Generate sample data |
| `TempBlobStorageService` | Temporary storage for test artifacts |

**Endpoint**: `POST /api/ai/playbooks/{id}/test` (TestPlaybookModels)

---

## Related Documentation

| Document | Location | Audience |
|----------|----------|----------|
| AI Strategy & Roadmap | `docs/guides/SPAARKE-AI-STRATEGY-AND-ROADMAP.md` | Executive/business |
| AI Implementation Guide | `docs/guides/SPAARKE-AI-ARCHITECTURE.md` | Engineers (detailed how-to) |
| SK Chat Design | `projects/ai-document-analysis-enhancements/sk-analysis-chat-design.md` | Engineers (SprkChat design) |
| AI Deployment Guide | `docs/guides/AI-DEPLOYMENT-GUIDE.md` | DevOps |
| Azure AI Resources | `docs/architecture/auth-AI-azure-resources.md` | Infrastructure |
| ADR-013 | `.claude/adr/ADR-013.md` | Architecture constraints |

---

## Changelog

| Date | Version | Change |
|------|---------|--------|
| 2026-02-21 | 3.0 | Created from consolidation of AI-PLAYBOOK-ARCHITECTURE.md (v2.0) and AI-ANALYSIS-PLAYBOOK-SCOPE-DESIGN.md (v2.0). Added four-tier architecture, playbooks-as-frontend model, backend flexibility, nodes-as-agents evolution, complete file mapping from codebase. |
| 2026-01-16 | (predecessor) | AI-ANALYSIS-PLAYBOOK-SCOPE-DESIGN.md v2.0 |
| 2026-01-15 | (predecessor) | AI-PLAYBOOK-ARCHITECTURE.md v2.0 |
