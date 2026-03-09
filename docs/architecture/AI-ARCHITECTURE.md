# Spaarke AI Architecture

> **Version**: 3.3
> **Last Updated**: March 6, 2026
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
│   ├── AdminKnowledgeEndpoints.cs    # Admin: index/delete reference knowledge
│   ├── ModelEndpoints.cs            # Model/deployment management
│   ├── VisualizationEndpoints.cs    # Data visualization
│   └── RecordMatchEndpoints.cs      # Record matching
├── Services/Ai/                     # Core AI services (~127 files)
│   ├── Nodes/                       # Node executor implementations
│   ├── Tools/                       # Tool handler implementations
│   ├── Builder/                     # Playbook builder agent
│   ├── SemanticSearch/              # RAG search pipeline
│   ├── Export/                      # Export format handlers
│   ├── Delivery/                    # Email/Word templates
│   ├── Handlers/                    # GenericAnalysisHandler
│   ├── Models/                      # PromptSchema.cs (JPS model), DTOs
│   ├── Prompts/                     # System prompt definitions
│   ├── Testing/                     # Playbook test infrastructure
│   ├── Visualization/              # Chart/visualization generation
│   ├── ReferenceIndexingService.cs   # Golden reference knowledge indexing
│   ├── ReferenceRetrievalService.cs  # L1 reference knowledge retrieval
│   ├── PromptSchemaRenderer.cs      # JPS → prompt text + JSON Schema
│   └── LookupChoicesResolver.cs     # $choices Dataverse pre-resolution
├── Models/Ai/                       # DTOs and request/response models (~37 files)
│   ├── SemanticSearch/              # Search-specific models
│   ├── ReferenceSearchResult.cs      # Reference retrieval response models
│   └── KnowledgeRetrievalConfig.cs   # Per-action knowledge retrieval settings
└── BackgroundWorkers/               # Background processing

src/client/code-pages/
├── PlaybookBuilder/                 # Visual playbook canvas (React 18 Code Page — current)
│   ├── src/
│   │   ├── App.tsx                   # Root component
│   │   ├── index.tsx                 # React 18 createRoot entry
│   │   ├── config/msalConfig.ts      # MSAL auth config
│   │   ├── hooks/
│   │   │   ├── useAuth.ts
│   │   │   ├── useKeyboardShortcuts.ts
│   │   │   ├── usePlaybookLoader.ts
│   │   │   └── useThemeDetection.ts
│   │   ├── services/
│   │   │   ├── aiPlaybookService.ts   # BFF API client
│   │   │   ├── authService.ts         # Token management
│   │   │   ├── dataverseClient.ts     # Dataverse fetch + Bearer token
│   │   │   └── playbookNodeSync.ts    # Canvas → sprk_playbooknode sync
│   │   ├── stores/
│   │   │   ├── canvasStore.ts         # Zustand state (nodes/edges)
│   │   │   ├── executionStore.ts      # Execution state
│   │   │   ├── aiAssistantStore.ts    # AI assistant modal state
│   │   │   ├── scopeStore.ts          # Skills/Knowledge/Tools cache
│   │   │   ├── modelStore.ts          # AI model deployments
│   │   │   └── templateStore.ts       # Playbook templates
│   │   ├── types/
│   │   │   ├── canvas.ts              # PlaybookNode, PlaybookEdge, CanvasJson
│   │   │   ├── playbook.ts            # Enums, NodeType/ActionType mappings
│   │   │   ├── forms.ts               # Form field types
│   │   │   └── scopeTypes.ts          # Scope DTOs
│   │   └── components/
│   │       ├── BuilderLayout.tsx       # Main layout (toolbar, palette, canvas, properties)
│   │       ├── canvas/PlaybookCanvas.tsx  # @xyflow/react v12 canvas
│   │       ├── nodes/                 # 9 node components (Start, AiAnalysis, etc.)
│   │       ├── edges/                 # ConditionEdge (true/false branches)
│   │       ├── properties/            # PropertiesPanel, ScopeSelector, ModelSelector, etc.
│   │       ├── execution/             # ExecutionOverlay, ConfidenceBadge
│   │       └── ai-assistant/          # AiAssistantModal, ChatHistory, CommandPalette
│   ├── build-webresource.ps1          # Step 2: inline bundle → HTML
│   └── out/sprk_playbookbuilder.html  # Deployable artifact

src/client/pcf/
├── PlaybookBuilderHost/             # Visual playbook canvas (PCF — legacy R4, still maintained)
│   └── control/                     # Mirrors Code Page structure (React 16, react-flow v10)
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

Prompts are assembled by `PromptSchemaRenderer` from either flat text or **JSON Prompt Schema (JPS)** stored in `Action.SystemPrompt`:

```
Final LLM Prompt = Action.SystemPrompt (flat text OR JPS)
                 + Skill.PromptFragment(s)
                 + Knowledge (RAG context or inline)
                 + Document text
                 + $choices-resolved enum constraints (JPS only)
```

**JPS (JSON Prompt Schema)**: A structured JSON format (`$schema: "https://spaarke.com/schemas/prompt/v1"`) that enables:
- Structured instruction sections (role, task, constraints, context)
- Typed output field definitions with `structuredOutput: true` for Azure OpenAI constrained decoding
- Dynamic `$choices` enum injection from Dataverse at render time
- `$ref` scope references for knowledge and skills
- Template parameters (`{{paramName}}`) for runtime customization

**Format detection**: If `SystemPrompt` starts with `{` and contains `"$schema"`, it is parsed as JPS; otherwise it is treated as flat text.

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/PromptSchemaRenderer.cs`
**Schema**: `src/server/api/Sprk.Bff.Api/Services/Ai/Models/PromptSchema.cs`

### $choices — Dynamic Enum Resolution

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/LookupChoicesResolver.cs`

JPS output fields can declare `"$choices"` to auto-inject valid enum values at render time. This constrains the AI model (via JSON Schema `"enum"`) to return only values that exist in Dataverse, eliminating frontend fuzzy matching.

**Supported prefixes**:

| Prefix | Format | Resolution Source | Resolver |
|--------|--------|-------------------|----------|
| `lookup:` | `"lookup:{entity}.{field}"` | Active records from Dataverse reference entity | `IScopeResolverService.QueryLookupValuesAsync()` |
| `optionset:` | `"optionset:{entity}.{attribute}"` | Single-select choice/picklist metadata labels | `IScopeResolverService.QueryOptionSetLabelsAsync()` |
| `multiselect:` | `"multiselect:{entity}.{attribute}"` | Multi-select picklist metadata labels | `IScopeResolverService.QueryOptionSetLabelsAsync(isMultiSelect: true)` |
| `boolean:` | `"boolean:{entity}.{attribute}"` | Two-option boolean field labels | `IScopeResolverService.QueryBooleanLabelsAsync()` |
| `downstream:` | `"downstream:{outputVar}.{field}"` | Downstream UpdateRecord node field mapping options | `PromptSchemaRenderer` (inline) |

**Pipeline**:
```
1. LookupChoicesResolver.ResolveFromJpsAsync(rawPrompt)
   └─ Scans JPS for $choices with Dataverse prefixes
   └─ Pre-resolves via IScopeResolverService queries
   └─ Returns Dictionary<string, string[]>

2. AiAnalysisNodeExecutor stores result in ToolExecutionContext.PreResolvedLookupChoices

3. GenericAnalysisHandler passes to PromptSchemaRenderer.Render()

4. PromptSchemaRenderer.ResolveChoices() injects values as "enum" in JSON Schema
   └─ downstream: prefix resolved inline from DownstreamNodeInfo[]
   └─ All other prefixes looked up from PreResolvedLookupChoices dict
```

**Example JPS field**:
```json
{
  "name": "matterTypeName",
  "type": "string",
  "description": "The matter type that best matches this document",
  "$choices": "lookup:sprk_mattertype_ref.sprk_mattertypename"
}
```

At render time → `"enum": ["Patent", "Trademark", "Copyright", ...]` in JSON Schema → Azure OpenAI constrained decoding forces exact value selection.

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

// $choices Dataverse queries (used by LookupChoicesResolver)
Task<string[]> QueryLookupValuesAsync(string entitySetName, string fieldName, CancellationToken ct);
Task<string[]> QueryOptionSetLabelsAsync(string entityLogicalName, string attributeLogicalName, bool isMultiSelect, CancellationToken ct);
Task<string[]> QueryBooleanLabelsAsync(string entityLogicalName, string attributeLogicalName, CancellationToken ct);

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

#### Three-Level Node Type System

Nodes use three type concepts at different layers:

| Level | Name | Where Stored | Purpose | Example |
|-------|------|-------------|---------|---------|
| **Canvas Type** | `PlaybookNodeType` | React Flow `node.data.type` | React component selection | `"aiAnalysis"` |
| **Dataverse NodeType** | `sprk_nodetype` | `sprk_playbooknode` OptionSet | Coarse scope resolution | `AIAnalysis (100000000)` |
| **ActionType** | `__actionType` in ConfigJson | `sprk_playbooknode.sprk_configjson` | Fine-grained executor dispatch | `AiAnalysis (0)` |

**Canvas Types** (8 React components — drag-and-drop palette items):

```typescript
type PlaybookNodeType =
  | 'start'           // Entry point — always Spaarke code
  | 'aiAnalysis'      // AI analysis (LLM call) — backend-flexible
  | 'aiCompletion'    // AI completion (LLM call) — backend-flexible
  | 'condition'       // Conditional branching — always Spaarke code
  | 'deliverOutput'   // Output delivery — always Spaarke code
  | 'createTask'      // Task creation — always Spaarke code
  | 'sendEmail'       // Email action — always Spaarke code
  | 'wait';           // Wait/delay — always Spaarke code
```

**Dataverse NodeType** (4 coarse categories for scope resolution):

```csharp
public enum NodeType
{
    AIAnalysis = 100_000_000,  // Full scope resolution (skills, knowledge, tools)
    Output     = 100_000_001,  // No scopes — assembles previous outputs
    Control    = 100_000_002,  // No scopes — flow control
    Workflow   = 100_000_003   // No scopes — Dataverse/email actions
}
```

**ActionType** (15 fine-grained executor dispatch values):

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
    DeliverOutput = 40
}
```

**Mapping flow** (computed during canvas-to-Dataverse sync):

```
Canvas Type "sendEmail"
  → NodeType.Workflow (100000003)     ← written to sprk_nodetype
  → ActionType.SendEmail (21)         ← written to __actionType in sprk_configjson
```

At execution time: NodeType determines scope resolution strategy, ActionType determines which `INodeExecutor` runs.

#### Node Data Structure

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
  emailTo?: string[];            // Send Email: recipients
  emailSubject?: string;         // Send Email: subject
  taskSubject?: string;          // Create Task: task subject
  systemPrompt?: string;         // AI Completion: custom system prompt
  userPromptTemplate?: string;   // AI Completion: user prompt with {{variables}}
  waitType?: string;             // Wait: "duration" | "until" | "condition"
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

### Playbook Builder (Code Page — Current)

**Path**: `src/client/code-pages/PlaybookBuilder/`
**Stack**: React 18, @xyflow/react v12, Fluent UI v9, Zustand
**Deployment**: Inline HTML web resource (`sprk_playbookbuilder.html`)

```
PlaybookBuilder (Code Page)
└── FluentProvider (Fluent UI v9 theme, dark mode via useThemeDetection)
    └── App
        └── BuilderLayout
            ├── Toolbar (save, run, AI assistant toggle)
            ├── NodePalette (left sidebar, 7 draggable node types)
            ├── PlaybookCanvas (center, @xyflow/react v12)
            │   ├── StartNode
            │   ├── AiAnalysisNode
            │   ├── AiCompletionNode
            │   ├── ConditionNode
            │   ├── DeliverOutputNode
            │   ├── CreateTaskNode
            │   ├── SendEmailNode
            │   ├── WaitNode
            │   └── ConditionEdge (true/false branches)
            ├── PropertiesPanel (right sidebar, auto-opens on node select)
            │   └── NodePropertiesForm
            │       ├── ScopeSelector (skills, knowledge, tools)
            │       ├── ModelSelector (AI model dropdown)
            │       ├── ActionSelector (linked action)
            │       ├── ConditionEditor (condition nodes)
            │       ├── DeliverOutputForm / SendEmailForm / CreateTaskForm / etc.
            │       └── VariableReferencePanel (upstream output variables)
            ├── ExecutionOverlay (during playbook execution)
            └── AiAssistantModal (conversational builder, floating)
```

**Canvas-to-Dataverse Sync** (`playbookNodeSync.ts`):
- Auto-save: 30-second debounce after canvas changes
- Manual save: Ctrl+S
- On save: Canvas JSON → `sprk_canvaslayoutjson`, then `syncNodesToDataverse()`:
  1. Queries existing `sprk_playbooknode` records
  2. Computes execution order via Kahn's topological sort of canvas edges
  3. Creates/updates/deletes node records with `sprk_nodetype` + `__actionType` in ConfigJson
  4. Writes `sprk_dependsonjson` with upstream node GUIDs
  5. Manages N:N relationships (skills, knowledge, tools) via associate/disassociate
- Uses `DataverseClient` (fetch + Bearer token via MSAL)

### PlaybookBuilderHost PCF Control (Legacy R4)

**Path**: `src/client/pcf/PlaybookBuilderHost/`
**Stack**: React 16/17, react-flow-renderer v10, Fluent UI v9
**Status**: Still maintained; mirrors Code Page structure. Used as field-bound control on `sprk_analysisplaybook` form.

**ADR-022 Compliance**: React 16 APIs (platform-provided), Fluent UI v9.

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
      1. Build ExecutionGraph (DAG from DependsOn arrays, Kahn's algorithm)
      2. Topological sort → execution batches (independent nodes grouped)
      3. FOR EACH batch:
         a. Resolve scopes per node based on NodeType:
            - AIAnalysis → full scopes (skills, knowledge, tools from N:N)
            - Output/Control/Workflow → empty scopes (no LLM calls)
         b. Determine ActionType from __actionType in ConfigJson
            (falls back to NodeType-based default)
         c. Route to INodeExecutor via NodeExecutorRegistry[ActionType]
         d. Execute nodes in parallel (SemaphoreSlim throttle, configurable)
      4. Stream PlaybookStreamEvents per node (SSE)
      5. Store node outputs in PlaybookRunContext (ConcurrentDictionary)
      6. Template substitution: downstream nodes reference {{variable}} outputs
      7. Rate limit handling with exponential backoff per ADR-016
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

| File | ActionType(s) | Backend |
|------|--------------|---------|
| `INodeExecutor.cs` | Interface + NodeType/ActionType enums | -- |
| `INodeExecutorRegistry.cs` | Registry interface | -- |
| `NodeExecutorRegistry.cs` | ActionType → INodeExecutor lookup | -- |
| `AiAnalysisNodeExecutor.cs` | AiAnalysis (0), AiCompletion (1), AiEmbedding (2) | Backend-flexible (currently in-process) |
| `ConditionNodeExecutor.cs` | Condition (30) | Always Spaarke code |
| `CreateTaskNodeExecutor.cs` | CreateTask (20) | Always Spaarke code |
| `UpdateRecordNodeExecutor.cs` | UpdateRecord (22) | Always Spaarke code |
| `SendEmailNodeExecutor.cs` | SendEmail (21) | Always Spaarke code |
| `DeliverOutputNodeExecutor.cs` | DeliverOutput (40) | Always Spaarke code |

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
| `SendCommunicationToolHandler` | `Tools/SendCommunicationToolHandler.cs` | Email/Teams communication |
| `GenericAnalysisHandler` | `Handlers/GenericAnalysisHandler.cs` | Configuration-driven (default) |

### Execution Flow (Batch Mode)

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
  │   │   ├── Step 7b: ActionType resolution (from __actionType in ConfigJson):
  │   │   │   └── Falls back to NodeType default if not present
  │   │   │
  │   │   ├── Step 7c: Create NodeExecutionContext (Node, Action, Scopes, PreviousOutputs)
  │   │   │
  │   │   ├── Step 7d: Route to INodeExecutor via NodeExecutorRegistry[ActionType]:
  │   │   │   │
  │   │   │   ├── AiAnalysisNodeExecutor (ActionType 0-2):
  │   │   │   │   ├── L1 Reference Retrieval: SearchReferencesAsync (if knowledge sources linked)
  │   │   │   │   ├── L2 Document Context: RagService search (if includeDocumentContext=true)
  │   │   │   │   ├── L3 Entity Context: RecordSearchService (if includeEntityContext=true)
  │   │   │   │   ├── Pre-resolve $choices: LookupChoicesResolver.ResolveFromJpsAsync()
  │   │   │   │   │   └── Queries Dataverse for lookup/optionset/multiselect/boolean values
  │   │   │   │   ├── Build prompt: Action.SystemPrompt + Skills + L1/L2/L3 Knowledge + Document
  │   │   │   │   ├── Template substitution: {{variable}} from PreviousOutputs
  │   │   │   │   ├── PromptSchemaRenderer: JPS → prompt text + JSON Schema (with $choices → enum)
  │   │   │   │   ├── Resolve tool handler (ToolHandlerRegistry)
  │   │   │   │   ├── Execute: handler.ExecuteAsync()
  │   │   │   │   │   └── IOpenAiClient.GetCompletionAsync() → Azure OpenAI (constrained decoding)
  │   │   │   │   └── Return NodeOutput (Data JSON + Summary text + Confidence)
  │   │   │   │
  │   │   │   ├── ConditionNodeExecutor (ActionType 30):
  │   │   │   │   └── Evaluate conditionJson against previous NodeOutputs
  │   │   │   │
  │   │   │   ├── DeliverOutputNodeExecutor (ActionType 40):
  │   │   │   │   └── Render Handlebars template with all NodeOutputs
  │   │   │   │
  │   │   │   ├── CreateTask/SendEmail (ActionType 20-21):
  │   │   │   │   └── Execute Spaarke workflow action on Dataverse/Graph
  │   │   │   │
  │   │   │   └── UpdateRecordNodeExecutor (ActionType 24):
  │   │   │       ├── Render Handlebars templates: {{output_nodeLabel.output.field}}
  │   │   │       ├── Coerce values by type (Choice → option int, Boolean → true/false)
  │   │   │       └── OData Web API PATCH to Dataverse entity
  │   │   │
  │   │   ├── Step 7e: Store output (PlaybookRunContext.StoreNodeOutput)
  │   │   └── Step 7f: Stream event (PlaybookStreamEvent via SSE)
  │   │
  │   └── Check for failures — stop execution if node failed (unless continueOnError)
  │
  ├── Step 8: Store analysis output (sprk_analysisoutput.sprk_output_rtf)
  └── Step 9: Complete run (metrics, status, duration)
```

### Parallel Execution and Performance

**ExecutionGraph** (`ExecutionGraph.cs`) uses Kahn's algorithm to build a DAG from node `DependsOn` arrays:

```
GetExecutionBatches() produces:
  Batch 1: [nodes with in-degree 0]  ← no dependencies, run in parallel
  Batch 2: [nodes whose deps completed in batch 1]  ← run in parallel
  ...
  Batch N: [final nodes]
```

**Performance formula**: `Total time ≈ SUM(slowest node in each batch)`

**Parallel throttling**: `SemaphoreSlim(DefaultMaxParallelNodes)` limits concurrent execution within a batch. Default is 3, but should be tuned based on Azure OpenAI TPM/RPM quota. This prevents rate limit storms (ADR-016).

**Optimization strategies**:

| Pattern | Structure | Batches | Performance |
|---------|-----------|---------|-------------|
| Fully sequential | A → B → C → D → Output | 5 | Slowest (sum of all) |
| Fully parallel | A,B,C,D → Output | 2 | Fastest (max of A-D + Output) |
| Partial deps | A → B; C,D → Output | 3 | Middle ground |

**Key rule**: Only add dependency edges where a node actually references `{{upstream.output}}` in its prompt. Unnecessary edges force sequential execution.

### Dual Output Paths

| Path | Data | Storage | Display |
|------|------|---------|---------|
| **Analysis Output** | `ToolResult.Summary` (text) | `sprk_analysisoutput.sprk_output_rtf` | AiSummaryPanel |
| **Document Fields** | AI structured output (JSON) | `sprk_document.*` fields via UpdateRecord node (OData PATCH) | Document form fields |

Document field writes happen **during** playbook execution (UpdateRecord node in step 7d), not after. There is no post-execution field mapping step.

### TemplateEngine

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/TemplateEngine.cs`

The TemplateEngine renders Handlebars.NET templates against a context built from previous node outputs. It is used by `UpdateRecordNodeExecutor` and `DeliverOutputNodeExecutor` to resolve `{{output_nodeLabel.output.fieldName}}` references.

**Key methods**:

| Method | Purpose |
|--------|---------|
| `RenderTemplate(template, context)` | Render a Handlebars template string against a dictionary context |
| `ConvertJsonElement(JsonElement)` | Convert `System.Text.Json.JsonElement` → `Dictionary<string,object>` / `List<object>` / primitives for Handlebars traversal |
| `FlattenArrays(object)` | Convert `List<object?>` → `"- item1\n- item2"` bullet-point string for Dataverse text fields |

**Template syntax**:

```
{{output_aiAnalysis.text}}                   → AI summary text
{{output_aiAnalysis.output.documentType}}    → Structured JSON field
{{output_aiAnalysis.output.keyFindings}}     → Array → flattened to bullet list
{{document.id}}                              → Document record ID (from run context)
```

**Context building** (`BuildTemplateContext` in each executor):
1. Start with `PreviousOutputs` dictionary (keyed by `output_{nodeLabel}`)
2. Each output contains `.text` (summary), `.output` (structured JSON), `.confidence`
3. `ConvertJsonElement` recursively converts JSON so Handlebars can dot-navigate into nested objects
4. `FlattenArrays` post-processes to convert arrays into text-friendly bullet strings
5. Additional context keys (`document`, `run`) come from `PlaybookRunContext`

### Typed Field Mappings (UpdateRecord Node)

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/UpdateRecordNodeExecutor.cs`

The UpdateRecord node writes AI-extracted values to Dataverse entity fields. Each field mapping specifies a Dataverse type so the executor can coerce AI string output to the correct typed value.

**Config format** (`fieldMappings` array in node ConfigJson):

```json
{
  "entityLogicalName": "sprk_document",
  "recordId": "{{document.id}}",
  "fieldMappings": [
    {
      "field": "sprk_filesummary",
      "type": "string",
      "value": "{{output_aiAnalysis.text}}"
    },
    {
      "field": "sprk_filesummarystatus",
      "type": "choice",
      "value": "{{output_aiAnalysis.output.status}}",
      "options": {
        "pending": 100000000,
        "in progress": 100000001,
        "complete": 100000002
      }
    },
    {
      "field": "sprk_isconfidential",
      "type": "boolean",
      "value": "{{output_aiAnalysis.output.isConfidential}}"
    }
  ]
}
```

**Supported types and coercion** (`CoerceFieldValue`):

| Type | AI Output | Coerced Value | Notes |
|------|-----------|---------------|-------|
| `string` | `"Contract for services..."` | `"Contract for services..."` | Passthrough |
| `choice` | `"Complete"` | `100000002` | Case-insensitive lookup in `options` map; falls back to raw int parse |
| `boolean` | `"yes"` / `"true"` / `"1"` | `true` | Truthy/falsy string parsing |
| `number` | `"42"` | `42` | `int.TryParse` then `decimal.TryParse` |

**OData Web API PATCH** (not SDK): `PATCH /api/data/v9.2/{entitySetName}({recordId})` with JSON body of coerced field values. This bypasses the Dataverse SDK to avoid `JsonElement` serialization issues with typed attributes.

**Backward compatibility**: The old `fields` dictionary format (`"fields": { "fieldName": "value" }`) still works. If `fieldMappings` is present it takes precedence; otherwise the legacy `fields` path executes unchanged.

### Consumer Integration Pattern

Components (PCF controls, Code Pages) that invoke AI analysis follow this pattern:

```
┌─────────────────┐     SSE stream      ┌──────────────────┐     OData PATCH     ┌──────────┐
│  PCF / CodePage │ ──────────────────→  │  BFF API         │ ──────────────────→ │ Dataverse│
│  (useAiSummary) │ ← progress events   │  (Playbook exec) │   (UpdateRecord)    │ (fields) │
└─────────────────┘                      └──────────────────┘                     └──────────┘
```

**Rules**:

1. **Component triggers playbook** via SSE endpoint (`/api/ai/analysis/execute`) and displays streaming progress
2. **All Dataverse field writes are server-side** — the playbook's UpdateRecord node writes structured fields via OData PATCH during execution
3. **Component does NOT write AI output fields** — no `onSummaryComplete` callbacks, no `updateSummary()` calls to Dataverse after playbook finishes
4. **Component owns its own UX** — file upload, metadata entry, progress display, error handling

**Anti-pattern** (removed in v3.15.0 of UniversalQuickCreate): Client-side `onSummaryComplete` callback accumulated ALL SSE chunks (including diagnostic messages like `"[Node-based execution: 3 nodes detected]"`) into a string, then overwrote the correct UpdateRecord values by calling `documentRecordService.updateSummary()`.

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
- `ReferenceIndexingService` -- Index knowledge sources into spaarke-rag-references
- `ReferenceRetrievalService` -- Query golden reference index for L1 knowledge

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

**Search indexes**:
- `spaarke-knowledge-index-v2` -- Customer documents (3072-dim, HNSW, cosine)
- `spaarke-rag-references` -- Golden reference knowledge (3072-dim, HNSW, cosine)
- `discovery-index` -- Discovery chunks (3072-dim)

**Multi-tenant isolation**: OData filter on `tenantId`

### Knowledge-Augmented Execution (R3)

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
  │   └── Excludes current document, scoped by parentEntity
  │
  ├── L3: IRecordSearchService (optional, includeEntityContext=true)
  │   └── Queries spaarke-records-index (business entity metadata)
  │
  └── Merge: L1 + L2 + L3 → KnowledgeContext → Prompt assembly
```

**Configuration** via `KnowledgeRetrievalConfig` in action's ConfigJson:

| Setting | Default | Description |
|---------|---------|-------------|
| `mode` | `auto` | `auto` (retrieve if sources linked), `always`, `never` |
| `topK` | 5 | Max reference chunks to retrieve (1-20) |
| `includeDocumentContext` | false | Enable L2 similar document retrieval |
| `includeEntityContext` | false | Enable L3 business entity context |

**Result caching**: Redis-based, key `sdap:rag-ref:{tenantId}:{queryHash}:{sourceIdsHash}:{topK}`, 10-minute TTL.

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
| Azure OpenAI | `spaarke-openai-dev` | LLM completions + embeddings (`gpt-4o-mini` deployed, `gpt-4o` planned) |
| Azure AI Search | `spaarke-search-dev` | Vector + hybrid search |
| Document Intelligence | `spaarke-docintel-dev` | PDF/image text extraction |
| Azure Redis Cache | -- | Embedding cache, search result cache |
| Azure Service Bus | -- | Background job queuing |
| Azure Key Vault | `spaarke-spekvcert` | Secrets management |
| Azure App Service | `spe-api-dev-67e2xz` | BFF API hosting |

### Model Selection (R3)

Model names are configured via `ModelSelectorOptions`, not hardcoded:

| Property | Default | Usage |
|----------|---------|-------|
| `DefaultModel` | `gpt-4o` | GenericAnalysisHandler (playbook AI nodes) |
| `ToolHandlerModel` | `gpt-4o-mini` | Built-in tool handlers (classification, extraction) |

**Resolution chain**: Node ConfigJson `ModelDeploymentId` → `ModelSelector.SelectModel(operationType)` → `ModelSelectorOptions.DefaultModel`

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
| ADR-022 | React 16 + Fluent v9 (PCF); React 18 (Code Pages) | PlaybookBuilderHost PCF: React 16 + react-flow v10; PlaybookBuilder Code Page: React 18 + @xyflow/react v12; both use Fluent UI v9 |

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
| How to Create Playbooks | `docs/guides/HOW-TO-CREATE-AI-PLAYBOOK-SCOPES.md` | Admins, Power Users |
| SK Chat Design | `projects/ai-document-analysis-enhancements/sk-analysis-chat-design.md` | Engineers (SprkChat design) |
| AI Deployment Guide | `docs/guides/AI-DEPLOYMENT-GUIDE.md` | DevOps |
| Azure AI Resources | `docs/architecture/auth-AI-azure-resources.md` | Infrastructure |
| ADR-013 | `.claude/adr/ADR-013.md` | Architecture constraints |

---

## Changelog

| Date | Version | Change |
|------|---------|--------|
| 2026-03-06 | 3.3 | Added JSON Prompt Schema (JPS) documentation: prompt composition with JPS format detection, $choices dynamic enum resolution with 5 Dataverse prefix types (lookup, optionset, multiselect, boolean, downstream), LookupChoicesResolver pre-resolution pipeline, IScopeResolverService query methods, updated execution flow with $choices pre-resolution step, updated file structure. |
| 2026-03-03 | 3.2 | Updated for typed field mappings: TemplateEngine (ConvertJsonElement, FlattenArrays), UpdateRecord OData PATCH with typed coercion (Choice/Boolean/Number), consumer integration pattern (PCF triggers playbook, no client-side field writes), removed obsolete DocumentProfileFieldMapper references from execution flow. |
| 2026-03-01 | 3.1 | Updated for Playbook Builder R5: three-level node type system (Canvas Type → NodeType → ActionType), Code Page builder as primary (PCF legacy), canvas-to-Dataverse sync with sprk_nodetype + __actionType, parallel execution batches and performance optimization, updated file structure, all 6 node executors, all 12 tool handlers. |
| 2026-02-21 | 3.0 | Created from consolidation of AI-PLAYBOOK-ARCHITECTURE.md (v2.0) and AI-ANALYSIS-PLAYBOOK-SCOPE-DESIGN.md (v2.0). Added four-tier architecture, playbooks-as-frontend model, backend flexibility, nodes-as-agents evolution, complete file mapping from codebase. |
| 2026-01-16 | (predecessor) | AI-ANALYSIS-PLAYBOOK-SCOPE-DESIGN.md v2.0 |
| 2026-01-15 | (predecessor) | AI-PLAYBOOK-ARCHITECTURE.md v2.0 |
