# Claude Code Task Prompt: Build Visual Node-Based Playbook Builder Prototype (Fluent UI v9)

## Objective

Build a **working UI prototype** of Spaarke's **Node-Based Playbook Builder** that allows a Business AI Analyst to visually design a playbook as a graph of **Nodes**, where each node wraps:

- **Action** (with ActionType: AI analysis, create task, send email, condition, etc.)
- **Scopes** (Skills, Tools, Knowledge)
- **AI Model Deployment** (optional override per node)
- **Dependencies** (which nodes must complete first)
- **Output Variable** (name for downstream reference)
- **Condition** (optional execution condition)

This prototype is for **visualization + authoring**. It does not need to execute real AI or workflows; it must support realistic editing, validation, and versionable export/import of the playbook graph definition.

## Non-Goals

- No real LLM calls or workflow execution.
- No production-grade security, auth, Dataverse integration, or persistence (mock data only).
- No attempt to build a generic "agent framework."
- No custom design system: **must use Microsoft Fluent UI v9**.

---

## Hard Requirements

1. **Design system**
   - Use **Microsoft Fluent UI React v9** components and styling conventions.
   - Do not use Fluent UI v8.
   - Ensure consistent spacing, typography, and layout consistent with a model-driven / Microsoft 365 experience.

2. **Graph editor**
   - Provide a node-based canvas with:
     - Add node (drag from palette or click)
     - Connect nodes (edges represent dependencies)
     - Select node
     - Move nodes
     - Delete node/edge
     - Zoom, pan, fit-to-view

3. **Node configuration**
   - Selecting a node opens a **right-side configuration panel** (Fluent Panel/Drawer pattern) with sections:
     - **General**: Name, Action selection, ActionType badge
     - **Scopes**: Skills, Tools, Knowledge multi-selects
     - **Execution**: Dependencies, Output Variable, Condition
     - **AI Settings**: Model Deployment override (for AI action types)
     - **Validation**: Status and issues

4. **Playbook-level configuration**
   - A top-level Playbook header section for:
     - Playbook name
     - Description
     - Version
     - Tags
     - Playbook Mode: `Legacy` (single action) | `NodeBased` (multi-node)

5. **Export / Import**
   - Export playbook graph to JSON.
   - Import JSON and render the graph.

6. **Fluent UI compliance**
   - Use Fluent layout primitives and components:
     - `Toolbar`, `Button`, `Menu`, `TabList`, `Tab`, `Input`, `Textarea`, `Dropdown`, `Option`, `Checkbox`, `Tag`, `Badge`, `Divider`, `Card`, `Field`, `Table`, `Toast` (as needed).
   - Use Fluent icons from `@fluentui/react-icons`.

---

## Recommended Technical Approach

### Framework
- React + TypeScript.
- Fluent UI v9: `@fluentui/react-components` and `@fluentui/react-icons`.

### Graph library
- Use a graph/canvas library that supports custom node rendering. Recommended:
  - **reactflow** (preferred for speed of prototyping).
- If you use reactflow, ensure node visuals look Fluent:
  - Use Fluent `Card` for node body.
  - Use Fluent typography, tokens, and spacing.
  - Avoid "web app builder" aesthetics.

### Project location
Create as a new prototype project folder:

- `/projects/node-playbook-builder-prototype/`

Include:
- `README.md` (how to run)
- `src/` with clean component structure

---

## Information Architecture

### Primary Screen: "Playbook Builder"

Layout (desktop-first):

- **Top command bar**: playbook actions (New, Open/Import, Save (mock), Export, Validate, Run (disabled), Help)
- **Left panel**: Node palette + Playbook metadata summary
- **Center**: Node canvas (graph)
- **Right panel**: Node configuration (opens on selection)

---

## UI Mockup (ASCII)

### Overall Page

```
+-----------------------------------------------------------------------------------+
| Playbook Builder: [Contract Risk Analysis]   v1.0     [Validate] [Export] [Help]  |
+-----------------------------------------------------------------------------------+
| Left: Palette / Metadata   |                 Canvas / Graph                       |
|----------------------------|------------------------------------------------------|
| Playbook                   |  (Zoom: 80%)  [Fit] [Center]                         |
| Name:  _________           |                                                      |
| Mode:  NodeBased  v        |   [Extract Entities] ---> [Analyze Clauses]          |
| Version: 1.0               |          |                    |                      |
| Tags: [Contract] [Risk]    |          v                    v                      |
|                            |   [Detect Risks] --------> [Generate Summary]        |
| Add Nodes                  |                               |                      |
| ----------------------     |                               v                      |
| [+] AI Analysis            |         [Condition: High Risk?]                      |
| [+] AI Completion          |              /           \                           |
| [+] Create Task            |            yes            no                         |
| [+] Send Email             |             |              |                         |
| [+] Update Record          |     [Create Task]      (end)                         |
| [+] Condition              |                                                      |
+----------------------------+------------------------------------------------------+
| Right: Node Config (Drawer/Panel)                                                 |
|-----------------------------------------------------------------------------------|
| Node: Detect Risks                         [Delete Node]                          |
| ActionType: [AI Analysis]                                                         |
|-----------------------------------------------------------------------------------|
| General:                                                                          |
| - Name: Detect Risks                                                              |
| - Action: Risk Detection (dropdown)                                               |
| - Output Variable: risks                                                          |
|-----------------------------------------------------------------------------------|
| Scopes:                                                                           |
| - Skills: [Risk Heuristics] [Severity Rubric] [+ Add]                             |
| - Tools:  [RiskDetectorHandler] [+ Add]                                           |
| - Knowledge: [Risk Taxonomy] [Standard Positions] [+ Add]                         |
|-----------------------------------------------------------------------------------|
| Execution:                                                                        |
| - Depends On: [Extract Entities] (dropdown/multi)                                 |
| - Condition: (none)                                                               |
|-----------------------------------------------------------------------------------|
| AI Settings:                                                                      |
| - Model: GPT-4o Production (dropdown)                                             |
| - Timeout: 300s   Retries: 1                                                      |
|-----------------------------------------------------------------------------------|
| Validation:                                                                       |
| - Required fields complete?  OK                                                   |
| - Dependencies valid?        OK                                                   |
| - Scopes configured?         OK                                                   |
+-----------------------------------------------------------------------------------+
```

### Condition Node Configuration

```
+-----------------------------------------------------------------------------------+
| Node: High Risk Check                      [Delete Node]                          |
| ActionType: [Condition]                                                           |
|-----------------------------------------------------------------------------------|
| General:                                                                          |
| - Name: High Risk Check                                                           |
| - Output Variable: riskDecision                                                   |
|-----------------------------------------------------------------------------------|
| Condition:                                                                        |
| - Expression: {{risks.highCount}} > 0                                             |
| - Available Variables:                                                            |
|   - {{entities.*}} from "Extract Entities"                                        |
|   - {{clauses.*}} from "Analyze Clauses"                                          |
|   - {{risks.*}} from "Detect Risks"                                               |
|   - {{summary.*}} from "Generate Summary"                                         |
|-----------------------------------------------------------------------------------|
| Branches:                                                                         |
| - True Path: [Create Task]                                                        |
| - False Path: (end)                                                               |
+-----------------------------------------------------------------------------------+
```

### Output Action Node (Create Task)

```
+-----------------------------------------------------------------------------------+
| Node: Escalate to Legal                    [Delete Node]                          |
| ActionType: [Create Task]                                                         |
|-----------------------------------------------------------------------------------|
| General:                                                                          |
| - Name: Escalate to Legal                                                         |
| - Output Variable: escalationTask                                                 |
|-----------------------------------------------------------------------------------|
| Task Configuration:                                                               |
| - Subject: Review high-risk contract: {{entities.documentTitle}}                  |
| - Description: {{summary.text}}                                                   |
| - Due Date Offset: 3 days                                                         |
| - Assign To: Legal Review Team                                                    |
| - Priority: High                                                                  |
|-----------------------------------------------------------------------------------|
| Execution:                                                                        |
| - Depends On: [High Risk Check]                                                   |
| - Condition: (inherits from branch)                                               |
+-----------------------------------------------------------------------------------+
```

---

## Visual Design Requirements (Fluent UI v9)

### General
- Use Fluent tokens and defaults; do not hardcode colors.
- Use Cards for nodes and panels; keep rounded corners subtle (Fluent).
- Typography:
  - Node titles: semibold, ~14-16px
  - Metadata labels: 12-13px
- Spacing:
  - Use consistent vertical rhythm (8px grid).
- Interaction:
  - Hover states on nodes and edges
  - Selection states clearly visible
  - Keyboard accessible controls where reasonable

### Node Appearance by ActionType

Each node must display at-a-glance:
- Node title (Name)
- ActionType badge with color coding
- Status indicator (Valid/Invalid)
- Scope counts (for AI nodes): Skills / Tools / Knowledge attached
- Output variable name

**ActionType Badge Colors** (using Fluent semantic colors):

| ActionType | Badge Color | Icon |
|------------|-------------|------|
| AI Analysis | Blue (brand) | BrainCircuit |
| AI Completion | Blue (brand) | Sparkle |
| Create Task | Green (success) | TaskListAdd |
| Send Email | Purple | Mail |
| Update Record | Orange (warning) | DatabaseArrowUp |
| Condition | Gray | BranchFork |
| Wait | Yellow | Clock |

**Example AI Node**:
```
+-------------------------+
| Detect Risks            |
| [AI Analysis]           |  <- Blue badge
| Output: risks           |
+-------------------------+
| Skills 2 | Tools 1 | K 2|
| Model: GPT-4o           |
+-------------------------+
| [Valid]                 |  <- Green indicator
+-------------------------+
```

**Example Condition Node**:
```
+-------------------------+
| High Risk Check         |
| [Condition]             |  <- Gray badge
| {{risks.highCount}} > 0 |
+-------------------------+
|    /yes\     /no\       |  <- Branch indicators
+-------------------------+
```

**Example Output Action Node**:
```
+-------------------------+
| Escalate to Legal       |
| [Create Task]           |  <- Green badge
| Output: escalationTask  |
+-------------------------+
| Assign: Legal Team      |
| Due: +3 days            |
+-------------------------+
```

---

## Data Model (Prototype)

Implement a minimal in-memory model with types aligned to V2 design:

### Playbook
- id: string
- name: string
- description: string
- version: number
- mode: `Legacy | NodeBased`
- tags: string[]
- nodes: PlaybookNode[]
- edges: Edge[]
- canvasLayout: { viewport: { x, y, zoom } }

### PlaybookNode
- id: string
- name: string (display name)
- actionId: string
- actionType: ActionType
- executionOrder: number
- dependsOn: string[] (node IDs)
- outputVariable: string
- condition: string | null (e.g., "{{risks.highCount}} > 0")
- config: Record<string, any> (action-type specific)
- modelDeploymentId: string | null (AI nodes only)
- timeoutSeconds: number
- retryCount: number
- scopes: {
    skills: string[]
    tools: ToolBinding[]
    knowledge: string[]
  }
- position: { x: number, y: number }
- isActive: boolean

### ToolBinding
- toolId: string
- parameters: Record<string, any>

### Edge
- id: string
- sourceNodeId: string
- targetNodeId: string
- sourceHandle?: string (for condition branches: "true" | "false")

### ActionType (enum)
```typescript
enum ActionType {
  AiAnalysis = 0,
  AiCompletion = 1,
  AiEmbedding = 2,
  RuleEngine = 10,
  Calculation = 11,
  DataTransform = 12,
  CreateTask = 20,
  SendEmail = 21,
  UpdateRecord = 22,
  CallWebhook = 23,
  Condition = 30,
  Parallel = 31,
  Wait = 32
}
```

---

## Sample Catalogs (Mock Data)

Provide hardcoded catalogs to drive dropdowns:

### Actions Catalog (by ActionType)

**AI Actions:**
- Extract Entities (AiAnalysis) - Extract key entities from document
- Analyze Clauses (AiAnalysis) - Identify and analyze contract clauses
- Detect Risks (AiAnalysis) - Identify risks and issues
- Generate Summary (AiAnalysis) - Create document summary
- Compare to Standards (AiAnalysis) - Compare against reference positions
- Custom AI Prompt (AiCompletion) - Raw LLM call with template

**Output Actions:**
- Create Task (CreateTask) - Create Dataverse task record
- Send Email (SendEmail) - Send notification email
- Update Record (UpdateRecord) - Update Dataverse entity
- Call Webhook (CallWebhook) - External HTTP call

**Control Flow:**
- Condition (Condition) - If/else branching
- Wait for Approval (Wait) - Human approval gate

### AI Model Deployments
- GPT-4o Production (default, high quality)
- GPT-4o-mini Fast (fast, cost-effective)
- GPT-4o-mini Batch (async, lowest cost)
- O1 Preview (reasoning tasks)

### Skills
- Risk Detection Heuristics
- Risk Severity Rubric
- Non-Inference / Evidence Rule
- Contract Structure Recognition
- Regulatory Compliance Rules
- Software License Analysis

### Tools
- EntityExtractorHandler
- ClauseAnalyzerHandler
- RiskDetectorHandler
- ClauseComparisonHandler
- SummaryHandler
- DateExtractorHandler
- DocumentClassifierHandler

### Knowledge
- Risk Taxonomy
- Standard Software License Positions
- Security Requirements Checklist
- Open Source Policy
- Regulatory Requirements Database

---

## Core User Stories (Prototype)

1. Create a new playbook and name it.
2. Add nodes from the palette by ActionType.
3. Connect nodes to establish dependencies (edges).
4. Select a node and configure:
   - Action selection
   - Scopes (Skills / Tools / Knowledge) for AI nodes
   - Output variable name
   - Dependencies
   - Condition (for conditional execution)
   - Model deployment override (for AI nodes)
5. Configure output action nodes:
   - Task subject/description with {{variable}} templates
   - Email recipients and body with templates
6. Use condition nodes to create branches:
   - Set condition expression
   - Connect true/false paths to different nodes
7. Validate the playbook:
   - Show summary of issues (missing fields, cycles, invalid references)
8. Export JSON.
9. Import JSON and render the same graph.

---

## Validation Rules (Prototype)

Implement basic validation to demonstrate governance:

### Required Fields
- All nodes: name, actionId, outputVariable (must be unique)
- AI nodes: at least one scope (skill OR tool OR knowledge)
- Condition nodes: condition expression
- CreateTask nodes: subject template
- SendEmail nodes: recipients, subject

### Graph Rules
- At least one node
- No circular dependencies (cycles)
- All dependsOn references must be valid node IDs
- Condition branches must have valid targets
- Output variable references ({{var}}) must reference prior nodes

### Variable Reference Validation
- Template expressions like {{risks.highCount}} must reference:
  - A node that executes BEFORE the current node
  - A valid output variable name from that node

### Display validation results
- Node-level badges (Valid/Invalid/Warning)
- Global validation panel showing all issues
- Highlight invalid nodes on canvas

---

## Component Structure (Suggested)

```
src/
├── App.tsx
├── pages/
│   └── PlaybookBuilderPage.tsx
├── components/
│   ├── TopToolbar.tsx
│   ├── LeftPalette/
│   │   ├── LeftPalette.tsx
│   │   ├── PlaybookMetadata.tsx
│   │   └── NodePalette.tsx
│   ├── Canvas/
│   │   ├── GraphCanvas.tsx (ReactFlow wrapper)
│   │   ├── CanvasControls.tsx
│   │   └── MiniMap.tsx
│   ├── Nodes/
│   │   ├── BaseNode.tsx
│   │   ├── AiAnalysisNode.tsx
│   │   ├── OutputActionNode.tsx
│   │   ├── ConditionNode.tsx
│   │   └── nodeStyles.ts
│   ├── Edges/
│   │   ├── DefaultEdge.tsx
│   │   └── ConditionalEdge.tsx
│   └── RightPanel/
│       ├── NodeConfigPanel.tsx
│       ├── sections/
│       │   ├── GeneralSection.tsx
│       │   ├── ScopesSection.tsx
│       │   ├── ExecutionSection.tsx
│       │   ├── AiSettingsSection.tsx
│       │   ├── TaskConfigSection.tsx
│       │   ├── EmailConfigSection.tsx
│       │   ├── ConditionSection.tsx
│       │   └── ValidationSection.tsx
│       └── ScopeSelector.tsx
├── state/
│   └── playbookStore.ts (Zustand)
├── models/
│   ├── types.ts
│   └── actionTypes.ts
├── data/
│   ├── catalogs.ts
│   └── mockPlaybooks.ts
├── utils/
│   ├── validation.ts
│   ├── exportImport.ts
│   ├── graphUtils.ts
│   └── templateUtils.ts
└── hooks/
    ├── usePlaybook.ts
    ├── useValidation.ts
    └── useTemplateVariables.ts
```

---

## Fluent UI v9 Notes (Implementation)

- Use `FluentProvider` at app root.
- Prefer `makeStyles` / tokens and Fluent patterns.
- Use `Drawer` for right-side panel (Fluent v9 has Drawer components).
- Use `MessageBar` for validation messages.
- Use `Badge` for ActionType indicators.
- Use `Tag` for scope items (skills, tools, knowledge).

---

## Template Variable Support

### Syntax
- Variables use `{{nodeName.property}}` syntax
- Example: `{{entities.documentTitle}}`, `{{risks.highCount}}`

### UI Support
- Show available variables based on upstream nodes
- Autocomplete in text fields
- Variable picker dropdown
- Preview resolved values (mock)

### Validation
- Validate variable references point to upstream nodes
- Warn on undefined properties (can't fully validate until runtime)

---

## Deliverables

1. Working prototype UI that runs locally.
2. Playbook canvas with node add/connect/select/edit.
3. Node configuration right panel with action-type-specific sections.
4. Multiple ActionType support (AI, Output Actions, Conditions).
5. Template variable support in output action configuration.
6. Dependency visualization (edges showing execution flow).
7. Validation UX with node-level and playbook-level feedback.
8. Export/Import JSON.
9. README with run instructions and mapping to Spaarke concepts.

---

## Acceptance Criteria Checklist

- [ ] Fluent UI v9 used across all UI controls and node visuals
- [ ] Graph interactions: add, connect, move, select, delete
- [ ] ActionType-specific node rendering (AI, Output, Condition)
- [ ] Right-side config panel with action-type-specific sections
- [ ] Scopes (Skills / Tools / Knowledge) selection for AI nodes
- [ ] Output variable names editable and validated for uniqueness
- [ ] Dependencies (dependsOn) configurable via dropdown
- [ ] Condition expressions with template variable support
- [ ] Model deployment selection for AI nodes
- [ ] Template variables ({{var}}) in output action configs
- [ ] Available variables shown based on upstream nodes
- [ ] Validation works and displays issues (node + playbook level)
- [ ] Export JSON matches V2 design data model
- [ ] Import JSON works
- [ ] Code is organized, readable, and aligned to prototype conventions

---

## Implementation Guidance and Guardrails

- Keep the prototype opinionated and clean; avoid over-engineering.
- Do not introduce additional UI libraries that conflict with Fluent.
- Treat this as a **visual authoring experience** for business AI analysts.
- Ensure the graph clearly communicates:
  - Execution sequence (via edges/dependencies)
  - Node types (via ActionType badges)
  - Data flow (via output variables)
  - Conditional branches (via edge labels)

---

## Stretch Goals (Optional)

- Node templates (preconfigured common patterns)
- Collapsible left and right panels
- Mini-map (ReactFlow)
- "Preview" mode showing execution sequence
- Undo/Redo support
- Keyboard shortcuts for common actions
- Dark mode toggle

---

## Output

Produce the complete working prototype with the structure above in:

- `/projects/node-playbook-builder-prototype/`

Include a short write-up in README describing:
- What this prototype demonstrates
- How it maps to Spaarke Playbook + Node model
- Key data model concepts (ActionType, Output Variables, Dependencies, Conditions)
- What remains for productionization (Dataverse persistence, security, execution engine)

---

## Alignment with V2 Design

This prototype UI aligns with `NODE-PLAYBOOK-BUILDER-DESIGN-V2.md`:

| V2 Concept | Prototype Implementation |
|------------|--------------------------|
| `sprk_playbooknode` | PlaybookNode type |
| `sprk_actiontype` enum | ActionType enum with same values |
| `sprk_outputvariable` | outputVariable field |
| `sprk_dependsonjson` | dependsOn array |
| `sprk_conditionjson` | condition string field |
| `sprk_modeldeploymentid` | modelDeploymentId with dropdown |
| Node N:N scopes | scopes object with skills/tools/knowledge |
| Template syntax `{{var}}` | Template variables in configs |
| Playbook mode | mode: Legacy/NodeBased |
| Execution batches | Visual dependency edges |

