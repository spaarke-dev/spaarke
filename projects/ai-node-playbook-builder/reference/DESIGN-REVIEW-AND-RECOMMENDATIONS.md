# AI Node-Based Playbook Builder - Design Review & Recommendations

> **Reviewer**: Claude (Opus 4.5)
> **Date**: January 8, 2026
> **Document Under Review**: [NODE-PLAYBOOK-BUILDER-DESIGN-V2.md](NODE-PLAYBOOK-BUILDER-DESIGN-V2.md)
> **Prototype Reference**: [prototype-wire-frame-01.jpg](reference/prototype-wire-frame-01.jpg)

---

## Executive Summary

The NODE-PLAYBOOK-BUILDER-DESIGN-V2.md provides a solid foundation for transforming Spaarke's playbook system into a node-based orchestration platform. The "extend, don't replace" philosophy is correct, and the architecture reuses existing components appropriately.

**Key Recommendations:**

| Topic | Current Design | Recommendation | Rationale |
|-------|---------------|----------------|-----------|
| **Skills per Node** | N:N (multiple) | **KEEP - Multiple skills** | Skills are prompt modifiers; combining them is valid and valuable |
| **Tools per Node** | N:N (multiple) | **CHANGE - Single tool** | Each node should do ONE thing; multiple tools creates ambiguity |
| **Knowledge per Node** | N:N (multiple) | **KEEP - Multiple knowledge** | Multiple context sources are legitimate and common |
| **Output Variables** | Single string | **ENHANCE - Structured output** | Support typed output schemas for better downstream consumption |
| **Action per Node** | Single (1:N) | **KEEP - One action** | Atomic nodes are correct; this is the right constraint |

---

## 1. Node Scope Structure Analysis

### 1.1 Actions: One Per Node (Correct)

**Current Design**: Each node references exactly one `sprk_analysisaction`.

**Assessment**: **CORRECT** - This is the right constraint.

**Rationale**:
- A node should be an atomic unit of work with a single, clear purpose
- Multiple actions per node would create unclear execution semantics
- The prototype correctly shows each node card with a single "Action" dropdown
- This enables clean data flow: Node A outputs → Node B inputs

**No change recommended.**

---

### 1.2 Skills: Multiple Per Node (Correct)

**Current Design**: N:N relationship `sprk_playbooknode_skill` allows multiple skills per node.

**Assessment**: **CORRECT** - Multiple skills should be supported.

**Rationale**:
- Skills are **behavioral modifiers** that adjust how the AI reasons
- Skills compose well: "Be concise" + "Use legal terminology" + "Focus on risks"
- The existing Spaarke system already supports multiple skills per playbook
- The prototype shows "Skills" as a multi-select dropdown (correct)

**Example valid combinations**:
```
Node: "Contract Risk Analysis"
├── Skill: "Legal Document Heuristics" (how to parse contracts)
├── Skill: "Risk Scoring Rubric" (how to categorize risks)
└── Skill: "Executive Summary Style" (output format preference)
```

**Enhancement Recommendation**: Add skill conflict detection. Some skills may contradict each other (e.g., "Be verbose" + "Be concise"). Consider:
- Adding a `conflictsWith` field to `sprk_analysisskill`
- UI warning when conflicting skills are selected

---

### 1.3 Tools: Single Per Node (Change Recommended)

**Current Design**: N:N relationship `sprk_playbooknode_tool` allows multiple tools per node.

**Assessment**: **CHANGE TO SINGLE TOOL** - One tool per node is cleaner.

**Rationale**:

1. **Execution ambiguity**: If a node has multiple tools (e.g., `EntityExtractor` + `RiskDetector`), which executes? Both? In what order? What if one fails?

2. **Node purpose confusion**: A node named "Extract Entities" shouldn't also run risk detection. That's a separate concern.

3. **Data flow clarity**: Each tool produces different output shapes. Multiple tools create merged, unclear output.

4. **Alignment with prototype**: The wireframe shows "Tools" as singular selection (dropdown, not multi-select).

5. **Existing pattern**: Current playbooks effectively use one primary tool per analysis action.

**Recommended Change**:
```diff
- N:N → sprk_playbooknode_tool
+ 1:N → sprk_toolid (FK on sprk_playbooknode)
```

**Schema Change**:
```
sprk_playbooknode
├── sprk_toolid → sprk_analysistool (nullable FK, single tool)
└── [remove N:N relationship sprk_playbooknode_tool]
```

**Edge Case - Tool-less Nodes**: Some action types don't need tools:
- `AiCompletion` (raw LLM call)
- `CreateTask`, `SendEmail` (integration actions)
- `Condition` (control flow)

The FK should be nullable for these cases.

**Alternative (if multiple tools are truly needed)**: If there's a valid use case for multiple tools, document it explicitly and define:
- Execution order (parallel or sequential)
- Output merging strategy
- Failure handling per tool

---

### 1.4 Knowledge: Multiple Per Node (Correct)

**Current Design**: N:N relationship `sprk_playbooknode_knowledge` allows multiple knowledge sources.

**Assessment**: **CORRECT** - Multiple knowledge sources should be supported.

**Rationale**:
- Knowledge provides context for AI reasoning
- Multiple sources are common: "Company style guide" + "Legal terminology glossary" + "Industry standards"
- RAG retrieval may pull from multiple indexes
- The prototype correctly shows "Knowledge" as multi-select

**Example valid combinations**:
```
Node: "Clause Compliance Check"
├── Knowledge: "Company Contract Standards" (inline reference)
├── Knowledge: "Industry Regulations RAG Index" (semantic search)
└── Knowledge: "Approved Clause Templates" (document reference)
```

**Enhancement Recommendation**: Add knowledge relevance scoring. When multiple knowledge sources are provided:
- Show which sources were actually used in the response
- Track token usage per knowledge source
- Enable future optimization (drop low-value sources)

---

### 1.5 Output Variables: Enhance for Typed Outputs

**Current Design**: `sprk_outputvariable` is a string name (e.g., "entities", "risks").

**Assessment**: **CORRECT CONCEPT, NEEDS ENHANCEMENT** - Add output schema typing.

**Current Approach**:
```
Node 1: outputVariable = "entities"
Node 2: references {{entities.partyCount}} in condition
```

**Problem**: No validation that `entities` actually has a `partyCount` property. Template errors only discovered at runtime.

**Recommended Enhancement**: Add output schema definition per action type.

```
sprk_analysisaction (extended)
├── sprk_outputschemajson: JSON schema defining output structure
```

**Example**:
```json
// EntityExtractor action output schema
{
  "type": "object",
  "properties": {
    "parties": { "type": "array", "items": { "$ref": "#/definitions/Party" } },
    "partyCount": { "type": "integer" },
    "dates": { "type": "array", "items": { "$ref": "#/definitions/DateRef" } },
    "amounts": { "type": "array", "items": { "$ref": "#/definitions/Amount" } }
  }
}
```

**Benefits**:
- Build-time validation of template references
- IntelliSense in the visual builder for `{{variable.}}` completion
- Clearer documentation of what each node produces
- Type-safe condition expressions

---

### 1.6 Multiple Outputs Per Node

**Question from User**: Should nodes support multiple outputs?

**Assessment**: **NO - Single output variable per node** (current design is correct).

**Rationale**:
1. A node does ONE thing and produces ONE output
2. If you need multiple outputs, use multiple nodes
3. The single `outputVariable` can be a complex object with multiple properties
4. Template syntax `{{output.property}}` already allows accessing nested data

**Example**:
```
// WRONG: Node with multiple output variables
Node: "Full Analysis"
├── output: "summary"
├── output: "entities"  // Multiple outputs = unclear purpose
└── output: "risks"

// CORRECT: Single structured output
Node: "Full Analysis"
└── outputVariable: "analysis"
    └── Contains: { summary, entities, risks }
    └── Access: {{analysis.summary}}, {{analysis.entities}}, {{analysis.risks}}

// BETTER: Separate nodes (recommended)
Node 1: "Extract Entities" → output: "entities"
Node 2: "Detect Risks" → output: "risks"
Node 3: "Generate Summary" → output: "summary" (depends on entities, risks)
```

The design should encourage **decomposition into focused nodes** rather than multi-output monoliths.

---

## 2. Workflow Extension Assessment

**User Concern**: This design should extend to support workflows in a future project.

**Assessment**: **WELL-POSITIONED** - The design already includes workflow primitives.

### 2.1 Action Types Already Support Workflows

The `sprk_actiontype` enum includes:

| Value | Category | Purpose |
|-------|----------|---------|
| 0-2 | AI | Analysis, completion, embedding |
| 10-12 | Deterministic | Rules, calculations, transforms |
| 20-23 | **Integration** | Tasks, emails, records, webhooks |
| 30-32 | **Control Flow** | Conditions, parallel, wait |

These integration and control flow actions ARE workflow primitives.

### 2.2 Recommendations for Workflow Readiness

**A. Condition Node Enhancement**:

Current design uses simple comparisons:
```json
{"if": "{{risks.highCount}} > 0"}
```

For workflows, consider:
```json
{
  "if": "{{risks.highCount}} > 0",
  "then": "branchA",  // Node ID or path name
  "else": "branchB"
}
```

**B. Add Loop/Iterator Action Type**:

For workflow use cases like "process each entity":
```
Value 33: ForEach - Iterate over array, execute sub-nodes per item
```

**C. Add Human-in-the-Loop States**:

The `Wait` action (value 32) is a good start. Enhance with:
- `sprk_waitconfigjson`: Who can approve, timeout, escalation
- Integration with Dataverse task/activity for approval tracking

**D. Future-Proof the Execution Graph**:

Current design uses `sprk_dependsonjson` for dependencies. For workflows:
- Consider adding `sprk_edgeconditionjson` on edges (not just nodes)
- Support multiple outbound edges from condition nodes

### 2.3 Separation Recommendation

**Current project**: Focus on AI playbook orchestration (multi-node analysis)
**Future project**: Add workflow-specific features (loops, approvals, complex branching)

The design correctly positions for this separation. The foundation (nodes, edges, execution graph) is shared.

---

## 3. Component Reuse Assessment

**User Concern**: Reuse existing components and minimize reengineering.

**Assessment**: **EXCELLENT** - The design correctly identifies reusable components.

### 3.1 Components to Reuse (No Changes)

| Component | Status | Rationale |
|-----------|--------|-----------|
| `IAnalysisToolHandler` + 8 implementations | **Reuse as-is** | Core analysis logic unchanged |
| `ToolHandlerRegistry` | **Reuse as-is** | Handler discovery works |
| `OpenAiClient` | **Reuse as-is** | Azure OpenAI integration unchanged |
| `TextExtractorService` | **Reuse as-is** | Document extraction unchanged |
| `RagService` | **Reuse as-is** | Knowledge retrieval unchanged |
| `SpeFileStore` | **Reuse as-is** | File access facade unchanged |
| Authorization patterns | **Reuse as-is** | Endpoint filters per ADR-008 |

### 3.2 Components to Extend (Minor Changes)

| Component | Extension Needed |
|-----------|------------------|
| `IScopeResolverService` | Add `ResolveNodeScopesAsync(nodeId)` |
| `IAnalysisContextBuilder` | Add template variable substitution |
| `IPlaybookService` | Add node CRUD methods |
| Dataverse entities | Add new entities, extend existing |

### 3.3 New Components (Must Build)

| Component | Purpose | Complexity |
|-----------|---------|------------|
| `IPlaybookOrchestrationService` | Multi-node execution | High |
| `INodeService` | Node CRUD | Medium |
| `ExecutionGraph` | Dependency resolution | Medium |
| `ITemplateEngine` | Variable substitution | Low |
| Node executors (5-6) | Per-action-type execution | Medium each |
| Workflow Builder UI | React Flow canvas | High |

### 3.4 Reengineering Risks

**Risk 1: Execution Context Proliferation**

Current: `ToolExecutionContext`, `DocumentContext`
New: `PlaybookRunContext`, `NodeExecutionContext`

**Mitigation**: Clear ownership - new contexts wrap existing ones, don't duplicate.

```csharp
public class NodeExecutionContext
{
    public DocumentContext Document { get; }  // Existing, reused
    public ToolExecutionContext ToolContext { get; }  // Existing, reused
    public IReadOnlyDictionary<string, NodeOutput> PreviousOutputs { get; }  // New
}
```

**Risk 2: Dataverse Query Explosion**

Multiple nodes = multiple scope resolutions = many Dataverse queries.

**Mitigation**: Batch loading. Load all nodes + scopes for a playbook in one query (or few queries), then resolve in memory.

---

## 4. Prototype Alignment Analysis

Reviewing [prototype-wire-frame-01.jpg](reference/prototype-wire-frame-01.jpg):

### 4.1 What the Prototype Shows

- **Header**: "Untitled Playbook", version, stats (5 Nodes, 4 Edges, 5 Outputs)
- **Canvas**: Dark background, node cards with connection lines
- **Node Cards**: Name, Action dropdown, Output Variable, Model selector, Scopes section
- **Right Sidebar**: Node Types palette (AI Analysis, AI Completion, Create Task, Condition, Send Email, Update Record) + Output Types (Document, Email, Event, Queue, Record)

### 4.2 Alignment with Design Document

| Prototype Element | Design Document | Alignment |
|-------------------|-----------------|-----------|
| Node types in palette | `sprk_actiontype` enum | **Aligned** |
| Scopes section (Skills, Tools, Knowledge) | N:N relationships on node | **Aligned** |
| Model dropdown | `sprk_modeldeploymentid` | **Aligned** |
| Output Variable field | `sprk_outputvariable` | **Aligned** |
| Canvas positioning | `sprk_position_x`, `sprk_position_y` | **Aligned** |
| Output Types palette | Not in design | **GAP** |

### 4.3 Gap: Output Types Palette

The prototype shows an "Output Types" section with: Document, Email, Event, Queue, Record.

**Question**: What are these? They appear separate from Node Types.

**Possible Interpretations**:
1. **Terminal nodes**: Final output destinations for the playbook
2. **Output format options**: How to render/deliver results
3. **Separate concept**: Output handlers vs. action nodes

**Recommendation**: Clarify intent. If these are output destinations, consider:
- Adding `sprk_outputtype` enum to the schema
- Making "Output Type" a property of the playbook (where results go)
- Or modeling as special "sink" nodes that terminate the graph

---

## 5. Critical Design Questions

### 5.1 Template Variable Security

The `{{variable.property}}` syntax enables powerful data flow but creates injection risk.

**Question**: Can user-controlled data appear in templates?

**Risk Scenario**:
```
Document text contains: "{{risks.highCount}}"
If not escaped, this could be interpreted as a template
```

**Recommendation**:
- Use a sandboxed template engine (not raw string replacement)
- Escape document content before template substitution
- Validate template syntax at save time, not execution time

### 5.2 Parallel Execution Limits

The design supports parallel node execution (nodes in same batch run concurrently).

**Question**: What's the max parallelism? Azure OpenAI has rate limits.

**Recommendation**:
- Add `maxParallelNodes` setting on playbook (default: 3)
- Implement queuing/throttling in `PlaybookOrchestrationService`
- Track rate limit headers and backoff

### 5.3 Long-Running Node Timeout

Design mentions `sprk_timeoutseconds` per node (default 300 = 5 minutes).

**Question**: What happens when a node times out mid-stream?

**Recommendation**:
- Cancel the OpenAI request
- Mark node as `TimedOut` (distinct from `Failed`)
- Capture partial output if available
- Allow playbook setting: `continueOnTimeout: true/false`

### 5.4 React Flow + React 16 Constraint

Design proposes iframe embedding to bypass React 16 limitation (ADR-022).

**Assessment**: This is a valid workaround but has tradeoffs.

| Approach | Pros | Cons |
|----------|------|------|
| **Iframe (proposed)** | Full React 18 + React Flow | Harder integration, postMessage complexity |
| **Custom canvas (alternative)** | Native PCF, no iframe | Significant dev effort, less polished UX |
| **Standalone web app** | Cleanest separation | Requires separate deployment, auth flow |

**Recommendation**: Iframe approach is reasonable. Ensure:
- Clear postMessage protocol documentation
- Auth token refresh mechanism
- Graceful degradation if iframe fails to load

---

## 6. Recommended Schema Changes

Based on the analysis above, here are specific schema modifications:

### 6.1 Change: Single Tool Per Node

```diff
sprk_playbooknode
- [N:N relationship sprk_playbooknode_tool]
+ sprk_toolid: Lookup → sprk_analysistool (nullable)
```

### 6.2 Add: Output Schema on Actions

```
sprk_analysisaction (add field)
+ sprk_outputschemajson: Multiline Text (JSON schema)
```

### 6.3 Add: Playbook Output Configuration

```
sprk_analysisplaybook (add fields)
+ sprk_outputtype: Choice (Document | Email | Task | Record | None)
+ sprk_outputconfigjson: Multiline Text
```

### 6.4 Add: Execution Limits

```
sprk_analysisplaybook (add fields)
+ sprk_maxparallelnodes: Integer (default 3)
+ sprk_continueontimeout: Boolean (default false)
+ sprk_continueonerror: Boolean (default false)
```

---

## 7. Phasing Recommendations

The design proposes 5 phases. Adjustments recommended:

### Phase 1: Foundation (Keep as-is)
- Dataverse schema
- Node CRUD
- Single-tool constraint
- Sequential execution only

### Phase 2: Visual Builder (Adjust)
- Move parallel execution to Phase 3 (simpler builder first)
- Focus on canvas UX and host-builder communication
- Add output schema validation in builder

### Phase 3: Parallel Execution + Output Actions (Combined)
- Parallel execution with throttling
- CreateTask, SendEmail, UpdateRecord handlers
- Template engine with security

### Phase 4: Advanced Features (Keep as-is)
- Conditions
- Model selection UI
- Templates library

### Phase 5: Production Hardening (Keep as-is)
- Error handling
- Monitoring
- Performance

---

## 8. Summary of Recommendations

### Must Change
1. **Single tool per node** - Change N:N to FK
2. **Add output schema** - On `sprk_analysisaction` for type safety
3. **Template security** - Sandboxed engine, escape user content

### Should Add
4. **Skill conflict detection** - Warn on conflicting skills
5. **Knowledge relevance tracking** - Which sources were used
6. **Execution throttling** - `maxParallelNodes` setting
7. **Output type configuration** - Where does final output go

### Validate
8. **Output Types palette** - Clarify intent from prototype
9. **Timeout handling** - Define partial output behavior
10. **Iframe auth flow** - Document token refresh mechanism

---

## 9. Decision Log

| # | Decision | Rationale | Status |
|---|----------|-----------|--------|
| D1 | One action per node | Atomic, clear purpose | **Confirmed** |
| D2 | Multiple skills per node | Skills compose well | **Confirmed** |
| D3 | Single tool per node | Avoid execution ambiguity | **Changed from N:N** |
| D4 | Multiple knowledge per node | Multiple contexts valid | **Confirmed** |
| D5 | Single output variable per node | Output is structured object | **Confirmed** |
| D6 | Extend existing handlers | Reuse > rebuild | **Confirmed** |
| D7 | Iframe for React Flow | ADR-022 workaround | **Confirmed** |

---

## Next Steps

1. **Review this document** with stakeholders
2. **Clarify** Output Types palette purpose
3. **Update** NODE-PLAYBOOK-BUILDER-DESIGN-V2.md with accepted changes
4. **Run** `/design-to-spec` to generate final spec.md
5. **Run** `/project-pipeline` to generate tasks

---

## 10. Output Concepts: AI Output vs Delivery Output

**Clarification from Stakeholder Review**: There are two distinct output concepts that require separate terminology and handling.

### 10.1 Terminology Definitions

| Term | Definition | Example |
|------|------------|---------|
| **AI Output** (Node Output) | The structured data result from an AI node's execution | JSON with entities, risks, summary text |
| **Delivery Output** (User Output) | The final rendered artifact delivered to users | Word document, Email, Teams message, Dataverse record |

### 10.2 AI Output (Node Output)

**Purpose**: Data produced by a node for consumption by downstream nodes or final rendering.

**Characteristics**:
- Structured data (JSON schema-defined)
- Internal to playbook execution
- Referenced via template variables `{{nodeOutput.property}}`
- Stored in `sprk_playbooknoderun.sprk_outputjson`

**Schema Requirements**:
```
sprk_analysisaction (extended)
├── sprk_outputschemajson: JSON Schema defining output structure
├── sprk_outputformat: Choice (JSON | Markdown | PlainText)
└── sprk_outputexample: Sample output for documentation
```

**Example AI Output Schema** (EntityExtractor):
```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "type": "object",
  "required": ["parties", "dates"],
  "properties": {
    "parties": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "name": { "type": "string" },
          "role": { "enum": ["buyer", "seller", "guarantor", "other"] },
          "address": { "type": "string" }
        }
      }
    },
    "partyCount": { "type": "integer" },
    "dates": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "label": { "type": "string" },
          "value": { "type": "string", "format": "date" },
          "context": { "type": "string" }
        }
      }
    },
    "amounts": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "value": { "type": "number" },
          "currency": { "type": "string" },
          "label": { "type": "string" }
        }
      }
    }
  }
}
```

### 10.3 Delivery Output (User Output)

**Purpose**: The final artifact delivered to the end user after playbook execution completes.

**Delivery Types**:

| Type | Description | Integration Point |
|------|-------------|-------------------|
| **Document** | Word/PDF document with formatted content | Power Apps Word Template or custom renderer |
| **Email** | Formatted email message | Power Apps Email Template or Microsoft Graph |
| **Teams Message** | Teams channel or chat notification | Microsoft Graph Teams API |
| **Record Update** | Populate fields on existing Dataverse record | Dataverse SDK |
| **Record Create** | Create new Dataverse record (Task, Activity, etc.) | Dataverse SDK |
| **Queue** | Add message to processing queue | Azure Service Bus or Dataverse queue |

### 10.4 Delivery Configuration Schema

```
sprk_analysisplaybook (extended)
├── sprk_deliverytype: Choice (Document | Email | TeamsMessage | RecordUpdate | RecordCreate | Queue | None)
├── sprk_deliveryconfigjson: Delivery-specific configuration
└── sprk_deliverytemplateid: Lookup → template record (Word/Email template)

sprk_deliverytemplate (NEW entity)
├── sprk_deliverytemplateid: Uniqueidentifier (PK)
├── sprk_name: Text (200)
├── sprk_type: Choice (WordDocument | Email | TeamsAdaptiveCard)
├── sprk_templatecontent: Multiline Text (template markup)
├── sprk_templatefileid: Text (for Word templates stored in SPE)
├── sprk_placeholders: Multiline Text (JSON array of expected placeholders)
└── sprk_isactive: Boolean
```

### 10.5 Template Integration Options

**Option A: Power Apps Template Builder (Recommended for Phase 1)**

- **Word Templates**: Use Power Apps document generation with Word templates
- **Email Templates**: Use Power Apps email templates
- **Pros**: Already exists, users familiar, no build effort
- **Cons**: Limited customization, separate configuration UI

**Integration Approach**:
```
Playbook Execution Complete
    ↓
Aggregate AI Outputs → JSON payload
    ↓
Call Power Automate flow with payload
    ↓
Power Automate renders template → Delivery
```

**Option B: Custom Template Engine (Future Enhancement)**

- Build Spaarke-native template editor
- Supports placeholders: `{{entities[0].name}}`, `{{risks | where: severity == 'high' | count}}`
- Rich preview with sample data
- **Pros**: Full control, unified UX
- **Cons**: Significant build effort

**Recommendation**: Start with Power Apps templates (Option A), evaluate custom engine based on user feedback.

### 10.6 Delivery Node Type

Add a new action type for explicit delivery:

```
ActionType Value 40: DeliverOutput
├── Delivery Type (Document | Email | Teams | Record)
├── Template selection
├── Recipient configuration (for Email/Teams)
├── Field mapping (for Record)
```

This allows playbooks to have **multiple delivery outputs**:
```
Node 1: Extract Entities → entities
Node 2: Analyze Risks → risks
Node 3: Generate Summary → summary
Node 4: Deliver Document (entities + risks + summary → Word doc)
Node 5: Deliver Email (if risks.highCount > 0 → send alert email)
```

---

## 11. AI Output Schema - Detailed Requirements

### 11.1 Schema Definition Approach

**Per-Tool Output Schema**: Each tool handler defines its output schema.

```csharp
public interface IAnalysisToolHandler
{
    // Existing
    string HandlerId { get; }
    ToolHandlerMetadata Metadata { get; }

    // NEW: Output schema for this handler
    JsonSchema OutputSchema { get; }
}
```

**Example Implementation**:
```csharp
public class EntityExtractorHandler : IAnalysisToolHandler
{
    public JsonSchema OutputSchema => new JsonSchema
    {
        Type = JsonSchemaType.Object,
        Properties = new Dictionary<string, JsonSchema>
        {
            ["parties"] = new JsonSchema { Type = JsonSchemaType.Array, ... },
            ["partyCount"] = new JsonSchema { Type = JsonSchemaType.Integer },
            ["dates"] = new JsonSchema { Type = JsonSchemaType.Array, ... },
            ["amounts"] = new JsonSchema { Type = JsonSchemaType.Array, ... }
        },
        Required = new[] { "parties", "dates" }
    };
}
```

### 11.2 Schema Storage in Dataverse

```
sprk_analysistool (extended)
├── sprk_outputschemajson: Multiline Text
└── sprk_outputexamplejson: Multiline Text (sample output for documentation)
```

Populated during solution deployment or via admin configuration.

### 11.3 Schema Validation Points

| Point | Validation | Action |
|-------|------------|--------|
| **Design-time** | Validate template references against schema | Show error in builder UI |
| **Save-time** | Validate all node connections have compatible types | Block save with errors |
| **Run-time** | Validate actual output matches schema | Log warning, continue execution |

### 11.4 Template IntelliSense Support

The visual builder should provide autocomplete for template variables:

```
User types: {{entities.
                      ↓
Autocomplete dropdown:
  - parties (array)
  - partyCount (integer)
  - dates (array)
  - amounts (array)
```

**Implementation**: Load output schema from upstream node's tool, parse JSON Schema, generate property list.

---

## 12. Template Security - Detailed Requirements

### 12.1 Threat Model

| Threat | Vector | Impact |
|--------|--------|--------|
| **Template Injection** | Document text contains `{{...}}` | Unintended variable substitution |
| **Data Exfiltration** | Malicious template accesses sensitive variables | Data leak |
| **Denial of Service** | Recursive template, infinite loops | System hang |
| **Code Execution** | Template engine allows code execution | Full compromise |

### 12.2 Mitigation Requirements

**M1: Sandboxed Template Engine**

- Use a logic-less or restricted template engine
- **Recommended**: Handlebars.NET or Scriban with restricted mode
- **Prohibited**: Engines with code execution (Razor, Liquid with filters)

```csharp
// GOOD: Scriban in safe mode
var template = Template.Parse(templateString);
template.Render(context, member => member.Name != "System"); // Block System namespace

// BAD: Full Razor execution
@{ var x = System.IO.File.ReadAllText("secrets.txt"); }
```

**M2: Input Escaping**

All document content and user input MUST be escaped before template context injection:

```csharp
public class TemplateContext
{
    // AI outputs are safe (controlled by us)
    public Dictionary<string, NodeOutput> NodeOutputs { get; }

    // Document content MUST be escaped
    public string DocumentText => HttpUtility.HtmlEncode(_rawDocumentText);

    // User parameters MUST be escaped
    public Dictionary<string, string> Parameters =>
        _rawParameters.ToDictionary(k => k.Key, v => HttpUtility.HtmlEncode(v.Value));
}
```

**M3: Template Validation at Save Time**

```csharp
public class TemplateValidator
{
    public ValidationResult Validate(string template, IReadOnlyList<string> allowedVariables)
    {
        var errors = new List<string>();

        // Parse template
        var references = ExtractVariableReferences(template);

        foreach (var reference in references)
        {
            // Check variable exists
            if (!allowedVariables.Contains(reference.RootVariable))
                errors.Add($"Unknown variable: {reference.RootVariable}");

            // Check for dangerous patterns
            if (reference.Contains("System.") || reference.Contains("Environment."))
                errors.Add($"Prohibited reference: {reference}");

            // Check recursion depth
            if (reference.Depth > 5)
                errors.Add($"Reference too deep: {reference}");
        }

        return new ValidationResult(errors);
    }
}
```

**M4: Execution Limits**

```csharp
public class TemplateEngineOptions
{
    public int MaxIterations { get; set; } = 1000;      // Prevent infinite loops
    public int MaxOutputLength { get; set; } = 100_000; // 100KB max output
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}
```

**M5: Variable Allowlist**

Only expose explicitly allowed variables to templates:

```csharp
public class SecureTemplateContext
{
    private readonly HashSet<string> _allowedRoots = new()
    {
        "entities", "risks", "summary", "clauses", // Node outputs
        "document", "playbook", "user"              // System context
    };

    public object Resolve(string path)
    {
        var root = path.Split('.')[0];
        if (!_allowedRoots.Contains(root))
            throw new TemplateSecurityException($"Access denied: {root}");

        return ResolveInternal(path);
    }
}
```

---

## 13. Scope Filtering by Action Type

### 13.1 Requirement

Not all scopes (skills, tools, knowledge) make sense for every action type. The UI should filter available options based on the selected action.

### 13.2 Compatibility Matrix

| Action Type | Skills | Tools | Knowledge | Delivery |
|-------------|--------|-------|-----------|----------|
| **AiAnalysis** | ✅ Multiple | ✅ Single | ✅ Multiple | ❌ N/A |
| **AiCompletion** | ✅ Multiple | ❌ None | ✅ Multiple | ❌ N/A |
| **CreateTask** | ❌ N/A | ❌ N/A | ❌ N/A | ✅ Record config |
| **SendEmail** | ❌ N/A | ❌ N/A | ❌ N/A | ✅ Email template |
| **UpdateRecord** | ❌ N/A | ❌ N/A | ❌ N/A | ✅ Field mapping |
| **Condition** | ❌ N/A | ❌ N/A | ❌ N/A | ❌ N/A |
| **DeliverOutput** | ❌ N/A | ❌ N/A | ❌ N/A | ✅ Template |

### 13.3 Schema Support

```
sprk_analysisaction (extended)
├── sprk_allowsskills: Boolean (default true)
├── sprk_allowstools: Boolean (default true)
├── sprk_allowsknowledge: Boolean (default true)
├── sprk_allowsdelivery: Boolean (default false)
├── sprk_compatibletoolsjson: JSON array of compatible tool IDs (null = all)
├── sprk_compatibleskillsjson: JSON array of compatible skill IDs (null = all)
```

### 13.4 UI Behavior

When user selects an Action for a node:
1. Query action's compatibility settings
2. Show/hide scope sections accordingly
3. Filter dropdown options to compatible items only

```typescript
// PlaybookBuilderHost - on action selection
function onActionSelected(actionId: string) {
    const action = await api.getAction(actionId);

    setShowSkills(action.allowsSkills);
    setShowTools(action.allowsTools);
    setShowKnowledge(action.allowsKnowledge);
    setShowDelivery(action.allowsDelivery);

    if (action.compatibleToolIds) {
        setToolOptions(tools.filter(t => action.compatibleToolIds.includes(t.id)));
    }
}
```

---

## 14. Playbook Types

### 14.1 Requirement

Playbooks can have different purposes that affect available actions and configurations. A type classification simplifies the UI and enforces appropriate constraints.

### 14.2 Playbook Type Definitions

| Type | Description | Available Action Types | Use Case |
|------|-------------|----------------------|----------|
| **AI Analysis** | Document analysis with AI | AiAnalysis, AiCompletion, Condition, DeliverOutput | Contract analysis, risk detection |
| **Workflow** | Deterministic business process | CreateTask, SendEmail, UpdateRecord, Condition, Wait | Approval flows, notifications |
| **Hybrid** | Mix of AI and workflow | All action types | AI analysis triggering tasks |

### 14.3 Schema Support

```
sprk_analysisplaybook (extended)
├── sprk_playbooktype: Choice (AiAnalysis=0 | Workflow=1 | Hybrid=2)
```

### 14.4 Type-Based Filtering

```typescript
const actionTypesByPlaybookType = {
    'AiAnalysis': ['AiAnalysis', 'AiCompletion', 'Condition', 'DeliverOutput'],
    'Workflow': ['CreateTask', 'SendEmail', 'UpdateRecord', 'Condition', 'Wait', 'CallWebhook'],
    'Hybrid': ['*'] // All types
};

function getAvailableActionTypes(playbookType: string): string[] {
    return actionTypesByPlaybookType[playbookType] || [];
}
```

### 14.5 Future Consideration

For the **Workflow** project, consider:
- **AI Summary** type: Read-only analysis, no record modifications
- **Workflow** type: No AI, deterministic process
- **Agentic** type: AI with tool use, human-in-the-loop approvals

---

## 15. POML Consideration

### 15.1 Current State

Spaarke uses POML (Playbook Object Markup Language) tags in task files but does not leverage POML as a full markup language for playbook definitions.

### 15.2 POML for Playbook Definitions - Analysis

**Potential Use**: Define playbooks as POML documents instead of Dataverse records.

**Example POML Playbook**:
```xml
<playbook name="Contract Risk Analysis" type="AiAnalysis" version="1.0">
  <description>Analyze contracts for risks and entities</description>

  <nodes>
    <node id="extract" action="EntityExtractor" output="entities">
      <skills>
        <skill ref="legal-document-parsing" />
      </skills>
      <knowledge>
        <knowledge ref="company-entity-standards" />
      </knowledge>
    </node>

    <node id="risks" action="RiskDetector" output="risks" depends="extract">
      <skills>
        <skill ref="risk-scoring-rubric" />
      </skills>
      <condition>{{entities.partyCount}} >= 2</condition>
    </node>

    <node id="summary" action="Summary" output="summary" depends="extract,risks">
      <config>
        <model>gpt-4o</model>
        <maxTokens>2000</maxTokens>
      </config>
    </node>

    <node id="deliver" action="DeliverOutput" depends="summary">
      <delivery type="Document" template="contract-analysis-report" />
    </node>
  </nodes>

  <edges>
    <edge from="extract" to="risks" />
    <edge from="extract" to="summary" />
    <edge from="risks" to="summary" />
    <edge from="summary" to="deliver" />
  </edges>
</playbook>
```

### 15.3 Pros and Cons

| Aspect | Pro | Con |
|--------|-----|-----|
| **Version Control** | POML files can be git-versioned | Dataverse records harder to version |
| **Portability** | Export/import playbooks as files | Requires parsing/serialization layer |
| **Developer Experience** | Edit in IDE with schema validation | Non-technical users prefer visual editor |
| **Deployment** | Include in solution packages | Extra deployment step |
| **Querying** | Dataverse queries are powerful | POML requires indexing for search |

### 15.4 Recommendation

**Do NOT adopt POML as primary storage** for this project. Reasons:

1. **Dataverse is the system of record** - Spaarke's architecture centers on Dataverse
2. **Visual builder outputs JSON** - Converting to/from POML adds complexity
3. **Query requirements** - Finding playbooks by type, owner, action requires Dataverse queries
4. **Security model** - Dataverse row-level security is well-understood

**Consider POML for**:
- **Export/Import** format for playbook backup and migration
- **Template library** distribution (ship playbook templates as POML files)
- **Developer authoring** alternative (power users can write POML, import to Dataverse)

### 15.5 If POML Is Adopted

Create a separate design document covering:
- POML schema definition (XSD or JSON Schema)
- Parser implementation
- Round-trip fidelity (POML → Dataverse → POML)
- Visual builder serialization to POML
- Template library structure
- Deployment integration

**Recommendation**: Defer POML deep-dive to a future enhancement. Focus this project on Dataverse-native storage with JSON canvas layout.

---

## 16. Updated Decision Log

| # | Decision | Rationale | Status |
|---|----------|-----------|--------|
| D1 | One action per node | Atomic, clear purpose | **Confirmed** |
| D2 | Multiple skills per node | Skills compose well | **Confirmed** |
| D3 | Single tool per node | Avoid execution ambiguity | **Confirmed** |
| D4 | Multiple knowledge per node | Multiple contexts valid | **Confirmed** |
| D5 | Single AI output variable per node | Output is structured object | **Confirmed** |
| D6 | Extend existing handlers | Reuse > rebuild | **Confirmed** |
| D7 | Iframe for React Flow | ADR-022 workaround | **Confirmed** |
| D8 | AI Output vs Delivery Output | Distinct concepts, different handling | **NEW** |
| D9 | Power Apps templates for delivery (Phase 1) | Leverage existing capability | **NEW** |
| D10 | Scope filtering by action type | UI simplification, prevent invalid configs | **NEW** |
| D11 | Playbook types (AI/Workflow/Hybrid) | Constrain available actions | **NEW** |
| D12 | POML for export/import only | Dataverse remains system of record | **NEW** |

---

## 17. Next Steps (Updated)

1. ~~Review this document with stakeholders~~ **DONE**
2. ~~Clarify Output Types palette purpose~~ **DONE** (Delivery Outputs)
3. **Create canonical design document** (NODE-PLAYBOOK-BUILDER-SPEC.md)
4. Review canonical design for completeness
5. Run `/design-to-spec` if needed
6. Run `/project-pipeline` to generate tasks

---

*This review was conducted against NODE-PLAYBOOK-BUILDER-DESIGN-V2.md (Version 2.0, January 7, 2026) and the prototype wireframe. Recommendations are based on existing Spaarke architecture patterns and ADR constraints.*

*Updated January 8, 2026 with stakeholder feedback on output concepts, scope filtering, playbook types, and POML consideration.*
