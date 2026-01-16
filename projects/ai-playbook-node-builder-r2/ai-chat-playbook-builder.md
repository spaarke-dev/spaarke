# AI Chat Playbook Builder - Design Document

> **Project**: AI Chat Playbook Builder (formerly ENH-005)
> **Status**: Design
> **Created**: January 16, 2026
> **Last Updated**: January 16, 2026
> **Priority**: High
> **Estimated Effort**: 6-7 weeks

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Problem Statement](#problem-statement)
3. [Architecture Decision: NOT M365 Copilot](#architecture-decision-not-m365-copilot)
4. [Proposed Solution](#proposed-solution)
5. [Architecture Overview](#architecture-overview)
6. [Project-Style AI Architecture](#project-style-ai-architecture)
7. [Conversational UX](#conversational-ux)
8. [Test Execution Architecture](#test-execution-architecture)
9. [Scope Management Architecture](#scope-management-architecture)
10. [Unified AI Agent Framework](#unified-ai-agent-framework)
11. [Implementation Plan](#implementation-plan)
12. [Related Documents](#related-documents)

---

## Executive Summary

The AI Chat Playbook Builder adds **conversational AI assistance** to the PlaybookBuilderHost PCF control. Users can build playbooks through natural language while seeing results update in real-time on the visual canvas. This feature establishes a **unified AI agent framework** where the Builder itself is a playbook (PB-BUILDER) using the same scope infrastructure it helps users compose.

**Key Capabilities:**
- Natural language playbook creation ("Build a lease analysis playbook")
- Real-time canvas updates via streaming
- Intelligent scope management (Save As, Extend, Create New)
- Test execution modes (Mock, Quick, Production)
- Tiered AI model selection for cost optimization

---

## Problem Statement

Building playbooks visually is powerful but requires users to:
1. Understand the node types and their purposes
2. Know how to configure Actions, Skills, Knowledge, and Tools
3. Manually create and link scope records in Dataverse
4. Arrange nodes and connect edges appropriately

Users would benefit from **conversational AI assistance** to build playbooks through natural language while seeing results update in real-time on the visual canvas.

---

## Architecture Decision: NOT M365 Copilot

**Decision**: Build a custom AI assistant embedded in the PlaybookBuilderHost PCF, NOT an M365 Copilot plugin.

**Rationale**:

| Factor | M365 Copilot | Embedded Modal (Chosen) |
|--------|--------------|------------------------|
| Canvas integration | Indirect (API calls, page refresh) | Direct state access |
| Real-time updates | Requires polling/refresh | Immediate via Zustand |
| User experience | Context switch to Copilot | Side-by-side with canvas |
| Deployment flexibility | Power Platform only | Any React host |
| Development control | Microsoft's UX constraints | Full control |
| Authentication | AAD through Copilot | Existing PCF auth context |

**M365 Copilot Strategy**:
> M365 Copilot should be reserved for **Spaarke-wide AI capabilities** directly tied to Power Apps/Dataverse platform features (e.g., "Show my recent documents", "What matters need attention?"). Feature-specific AI like the Playbook Builder should use tightly-integrated custom implementations.

---

## Proposed Solution

**Floating AI Modal** within the PlaybookBuilderHost PCF that:
1. Accepts natural language instructions
2. Generates/modifies canvas JSON in real-time
3. Creates Dataverse scope records (Actions, Skills, Knowledge, Tools)
4. Links scopes to playbook via N:N tables
5. Shows conversational history and explanations

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        PlaybookBuilderHost PCF                               │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌────────────────────────────────────┐  ┌────────────────────────────────┐ │
│  │          CANVAS AREA               │  │      AI ASSISTANT MODAL        │ │
│  │                                    │  │      (Floating/Resizable)      │ │
│  │  ┌──────┐      ┌──────┐           │  │                                │ │
│  │  │ Node │─────▶│ Node │           │  │  ┌────────────────────────┐   │ │
│  │  └──────┘      └──────┘           │  │  │ Chat History           │   │ │
│  │      │                             │  │  │                        │   │ │
│  │      ▼                             │  │  │ User: Create a lease   │   │ │
│  │  ┌──────┐                          │  │  │       analysis playbook│   │ │
│  │  │ Node │                          │  │  │                        │   │ │
│  │  └──────┘                          │  │  │ AI: I'll create 5      │   │ │
│  │                                    │  │  │     nodes for lease... │   │ │
│  │                                    │  │  │                        │   │ │
│  │                                    │  │  └────────────────────────┘   │ │
│  │                                    │  │                                │ │
│  │                                    │  │  ┌────────────────────────┐   │ │
│  │                                    │  │  │ [Type a message...]    │   │ │
│  │                                    │  │  │                   [▶]  │   │ │
│  │                                    │  │  └────────────────────────┘   │ │
│  └────────────────────────────────────┘  └────────────────────────────────┘ │
│                                                                              │
│  ┌──────────────────────────────────────────────────────────────────────────┤
│  │                        PROPERTIES PANEL                                   │
│  │  [Selected node configuration...]                                         │
│  └──────────────────────────────────────────────────────────────────────────┘
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### AI Agent Capabilities

The AI assistant can interact with:

**1. Canvas State (via Zustand store)**
- Add/remove/modify nodes
- Create/delete edges
- Update node configurations
- Rearrange layout

**2. Dataverse Entity Tables**
- `sprk_aianalysisplaybook` (main playbook record)
- `sprk_aianalysisaction` (Action system prompts)
- `sprk_aianalysisskill` (Skill prompt fragments)
- `sprk_aianalysisknowledge` (Knowledge RAG sources)
- `sprk_aianalysistool` (Tool handlers)
- `sprk_aianalysisoutput` (Output field mappings)

**3. N:N Link Tables**
- `sprk_aianalysisplaybook_action`
- `sprk_aianalysisplaybook_skill`
- `sprk_aianalysisplaybook_knowledge`
- `sprk_aianalysisplaybook_tool`
- `sprk_aianalysisplaybook_output`

### Data Flow

```
┌────────────────────────────────────────────────────────────────────────────┐
│                          AI ASSISTANT DATA FLOW                             │
├────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  User Message: "Add a compliance analysis node that checks lease terms     │
│                 against our standard terms document"                        │
│                              │                                              │
│                              ▼                                              │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │  POST /api/ai/build-playbook-canvas (SSE Stream)                     │  │
│  │                                                                       │  │
│  │  Request:                                                             │  │
│  │  {                                                                    │  │
│  │    "playbookId": "guid",                                              │  │
│  │    "currentCanvas": { nodes: [...], edges: [...] },                   │  │
│  │    "message": "Add a compliance analysis node...",                    │  │
│  │    "conversationHistory": [...]                                       │  │
│  │  }                                                                    │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
│                              │                                              │
│                              ▼                                              │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │  BFF API - AiPlaybookBuilderService                                  │  │
│  │                                                                       │  │
│  │  1. Analyze request + current canvas state                           │  │
│  │  2. Determine required operations:                                    │  │
│  │     - Canvas changes (nodes, edges)                                   │  │
│  │     - New scope records needed                                        │  │
│  │     - N:N links to create                                             │  │
│  │  3. Generate operations via LLM                                       │  │
│  │  4. Execute Dataverse operations (create records, links)             │  │
│  │  5. Stream canvas patch + explanation                                 │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
│                              │                                              │
│                              ▼                                              │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │  SSE Response Stream:                                                 │  │
│  │                                                                       │  │
│  │  event: thinking                                                      │  │
│  │  data: {"message": "Analyzing your request..."}                      │  │
│  │                                                                       │  │
│  │  event: dataverse_operation                                           │  │
│  │  data: {"operation": "create", "entity": "sprk_aianalysisaction",    │  │
│  │         "record": {...}, "id": "new-action-guid"}                    │  │
│  │                                                                       │  │
│  │  event: canvas_patch                                                  │  │
│  │  data: {"addNodes": [...], "addEdges": [...]}                        │  │
│  │                                                                       │  │
│  │  event: message                                                       │  │
│  │  data: {"content": "I've added a Compliance Analysis node..."}       │  │
│  │                                                                       │  │
│  │  event: done                                                          │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
│                              │                                              │
│                              ▼                                              │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │  PCF Client Processing                                                │  │
│  │                                                                       │  │
│  │  1. aiAssistantStore receives stream events                          │  │
│  │  2. On canvas_patch: Apply to canvasStore (nodes/edges update)       │  │
│  │  3. On message: Append to chat history                               │  │
│  │  4. React Flow re-renders with new nodes                             │  │
│  │  5. User sees changes in real-time                                   │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
└────────────────────────────────────────────────────────────────────────────┘
```

### PCF Component Architecture

**New Stores (Zustand):**

```typescript
// aiAssistantStore.ts
interface AiAssistantState {
  isOpen: boolean;
  messages: ChatMessage[];
  isStreaming: boolean;

  // Actions
  toggleModal: () => void;
  sendMessage: (message: string) => Promise<void>;
  applyCanvasPatch: (patch: CanvasPatch) => void;
  clearHistory: () => void;
}

interface ChatMessage {
  id: string;
  role: 'user' | 'assistant' | 'system';
  content: string;
  timestamp: Date;
  canvasOperations?: CanvasOperation[];  // What changes were made
}

interface CanvasPatch {
  addNodes?: PlaybookNode[];
  removeNodeIds?: string[];
  updateNodes?: Partial<PlaybookNode>[];
  addEdges?: PlaybookEdge[];
  removeEdgeIds?: string[];
}
```

**New Components:**

```
src/client/pcf/PlaybookBuilderHost/control/
├── components/
│   ├── AiAssistant/
│   │   ├── AiAssistantModal.tsx       ← Floating modal container
│   │   ├── ChatHistory.tsx            ← Message list with scroll
│   │   ├── ChatInput.tsx              ← Text input with send button
│   │   ├── OperationFeedback.tsx      ← Shows "Creating node..." etc.
│   │   └── index.ts
│   └── ...
├── services/
│   └── AiPlaybookService.ts           ← API client for /api/ai/build-playbook-canvas
├── stores/
│   └── aiAssistantStore.ts            ← Modal state, chat history
└── ...
```

### BFF API Endpoint

```csharp
// Endpoints/AiPlaybookBuilderEndpoints.cs

app.MapPost("/api/ai/build-playbook-canvas", BuildPlaybookCanvasAsync)
   .RequireAuthorization()
   .Produces<IAsyncEnumerable<ServerSentEvent>>(StatusCodes.Status200OK);

public record BuildPlaybookCanvasRequest
{
    public Guid PlaybookId { get; init; }
    public CanvasState CurrentCanvas { get; init; }
    public string Message { get; init; }
    public ChatMessage[] ConversationHistory { get; init; }
}

public record CanvasState
{
    public PlaybookNode[] Nodes { get; init; }
    public PlaybookEdge[] Edges { get; init; }
}
```

### Example Interactions

| User Says | AI Does |
|-----------|---------|
| "Create a lease analysis playbook" | Creates 5-6 nodes: TL;DR, Key Terms, Compliance, Risk, Assemble, Deliver |
| "Add a node to extract financial terms" | Adds AI Analysis node with Financial Terms Action |
| "Connect the compliance node to the risk analysis" | Creates edge between specified nodes |
| "Use our standard compliance skill" | Searches Skills, links existing or creates new |
| "The TL;DR should output to document_summary field" | Updates Output node configuration |
| "Remove the email notification" | Deletes specified node and its edges |
| "What does this playbook do?" | Explains current canvas structure |

### Security Considerations

| Concern | Mitigation |
|---------|------------|
| Unauthorized canvas modification | Same auth as playbook edit |
| Prompt injection | Sanitize user input, validate operations |
| Runaway operations | Limit operations per request (max 10) |
| Invalid canvas state | Validate patch before applying |
| Cost control | Track token usage, rate limit |

---

## Project-Style AI Architecture

The AI assistant follows a structured approach similar to how Claude Code handles development projects. This makes the AI more predictable, recoverable, and explainable.

**Parallel to Development Projects:**

| Development Projects | Playbook Builder AI |
|---------------------|---------------------|
| `design.md` (human input) | Upload/paste requirements or chat description |
| `spec.md` | Internal playbook spec (nodes needed, purpose) |
| `plan.md` | Execution plan (order of operations) |
| `tasks/*.poml` | Discrete operations (addNode, linkScope, createEdge) |
| `.claude/skills/` | Building "skills" (how to create node types, select scopes) |
| BFF Tools | Operations (addNode, updateConfig, linkScope) |

**Key Difference**: The spec/plan/tasks are **internal AI resources** (not user-facing). Users see only the chat and real-time canvas updates.

### Internal Playbook Build Plan

When the AI receives a request, it generates an internal build plan:

```json
{
  "playbookSpec": {
    "name": "Real Estate Lease Analysis",
    "purpose": "Analyze lease agreements for compliance with company standards",
    "documentTypes": ["LEASE"],
    "matterTypes": ["REAL_ESTATE"],
    "estimatedNodes": 8
  },
  "scopeRequirements": {
    "actions": ["ACT-001", "ACT-002", "ACT-004", "ACT-005"],
    "skills": ["SKL-004", "SKL-009"],
    "knowledge": ["KNW-004", "KNW-007"],
    "tools": ["TL-001", "TL-002", "TL-004", "TL-005"]
  },
  "executionPlan": [
    { "step": 1, "op": "createNode", "type": "aiAnalysis", "label": "TL;DR Summary", "outputVar": "tldrSummary" },
    { "step": 2, "op": "createNode", "type": "aiAnalysis", "label": "Extract Parties", "outputVar": "parties" },
    { "step": 3, "op": "createEdge", "from": "step_1", "to": "step_2" },
    { "step": 4, "op": "linkScope", "nodeRef": "step_1", "scopeType": "action", "scopeId": "ACT-004" }
  ]
}
```

**Benefits:**
- Structured reasoning (AI follows a plan, not arbitrary operations)
- Validation checkpoint (can show user high-level plan before executing)
- Recoverability (if interrupted, plan shows remaining steps)
- Explainability (user can ask "why?" and AI references the plan)

### AI Building "Skills" (Internal Knowledge)

The AI has access to internal "skills" that guide how to build playbooks:

```
playbook-builder-ai/skills/
├── create-analysis-node.md    ← How to configure AI Analysis nodes
├── create-condition-node.md   ← How to add branching logic
├── select-action.md           ← Choose Action based on node purpose
├── select-skills.md           ← Choose Skills based on document type
├── attach-knowledge.md        ← When and how to link Knowledge sources
├── design-output-flow.md      ← Assemble + Deliver patterns
└── common-patterns/
    ├── lease-analysis.md      ← Reference patterns for leases
    ├── contract-review.md     ← Reference patterns for contracts
    └── risk-assessment.md     ← Risk detection node patterns
```

These are embedded in the system prompt or loaded dynamically based on context.

### AI Building "Tools" (Operations)

The AI can execute these discrete operations:

| Tool | Description | Parameters |
|------|-------------|------------|
| `addNode` | Create node on canvas | `type`, `label`, `position`, `config` |
| `removeNode` | Delete node and connected edges | `nodeId` |
| `createEdge` | Connect two nodes | `sourceId`, `targetId` |
| `updateNodeConfig` | Modify node properties | `nodeId`, `config` |
| `linkScope` | Attach existing scope to node | `nodeId`, `scopeType`, `scopeId` |
| `createScope` | Create new Action/Skill/Knowledge in Dataverse | `type`, `data` |
| `searchScopes` | Find existing scopes by name/purpose | `type`, `query` |
| `autoLayout` | Arrange nodes for visual clarity | — |

### Workflow: Understand → Plan → Confirm → Execute → Refine

```
User: "Build a lease analysis playbook"
          │
          ▼
┌─────────────────────────────────┐
│  1. UNDERSTAND                   │  ← Parse requirements
│     - Document type: Lease       │
│     - Analysis goals identified  │
│     - Load lease-analysis skill  │
└─────────────────────────────────┘
          │
          ▼
┌─────────────────────────────────┐
│  2. PLAN (internal)              │  ← Generate build plan
│     - Identify required scopes   │
│     - Sequence node operations   │
│     - Estimate 8 nodes needed    │
└─────────────────────────────────┘
          │
          ▼
┌─────────────────────────────────┐
│  3. CONFIRM (optional)           │  ← Show high-level plan
│     AI: "I'll create 8 nodes:    │
│         TL;DR, Parties, Terms,   │
│         Compliance, Risk..."     │
│     User approves or adjusts     │
└─────────────────────────────────┘
          │
          ▼
┌─────────────────────────────────┐
│  4. EXECUTE                      │  ← Stream operations
│     - Create nodes (canvas)      │
│     - Link scopes (Dataverse)    │
│     - Real-time canvas updates   │
│     - Progress feedback          │
└─────────────────────────────────┘
          │
          ▼
┌─────────────────────────────────┐
│  5. REFINE                       │  ← Conversation continues
│     User: "Add financial terms"  │
│     User: "Change output format" │
│     (Returns to step 1)          │
└─────────────────────────────────┘
```

### Design Input Mode

Users can provide requirements in multiple ways:

| Input Method | Description |
|--------------|-------------|
| **Chat** | Natural language description in the modal |
| **Paste** | Paste requirements text into chat |
| **Upload** | Upload a design document (Word, PDF, text) |
| **Reference** | "Build something like [existing playbook]" |

For uploaded documents, the AI extracts requirements before generating the plan.

---

## Conversational UX

The AI assistant accepts **natural language input** as the primary interface, with **quick action buttons** as optional suggestions. The key challenge is ensuring the AI interprets user instructions consistently and maps them to the correct resources (scopes, tools, patterns).

**Design Decision**: Hybrid input model—natural language primary, structured options as accelerators.

### Intent Classification System

The AI classifies user input into **operation intents** before executing:

| Intent Category | Example Inputs | Mapped Operation |
|----------------|----------------|------------------|
| `CREATE_PLAYBOOK` | "Build a lease analysis playbook", "Make me a playbook for contracts" | Generate full build plan |
| `ADD_NODE` | "Add a node to extract dates", "I need a compliance check node" | `addNode` tool |
| `REMOVE_NODE` | "Delete the risk node", "Remove that last node" | `removeNode` tool |
| `CONNECT_NODES` | "Connect compliance to risk", "Link these two together" | `createEdge` tool |
| `CONFIGURE_NODE` | "Change the output variable to partyNames", "Update the prompt" | `updateNodeConfig` tool |
| `LINK_SCOPE` | "Use the standard compliance skill", "Add the lease knowledge" | `linkScope` tool |
| `CREATE_SCOPE` | "Create a new action for financial terms" | `createScope` tool |
| `QUERY_STATUS` | "What does this playbook do?", "Explain the compliance node" | No tool, explain state |
| `MODIFY_LAYOUT` | "Arrange the nodes", "Clean up the layout" | `autoLayout` tool |
| `UNDO` | "Undo that", "Go back", "Revert the last change" | Reverse last operation |
| `UNCLEAR` | Ambiguous input requiring clarification | Clarification loop |

### Mapping Natural Language to Operations

The LLM uses a structured **intent extraction** prompt that:

1. **Extracts intent category** from the classification taxonomy above
2. **Identifies target entities** (node IDs, scope names, positions)
3. **Determines parameters** (node type, configuration values, etc.)
4. **Validates feasibility** against current canvas state

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                      INTENT EXTRACTION PIPELINE                              │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  User: "Connect the TL;DR summary to the compliance check"                  │
│                              │                                               │
│                              ▼                                               │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │  Step 1: CLASSIFY INTENT                                               │  │
│  │  → Category: CONNECT_NODES                                             │  │
│  │  → Confidence: 0.95                                                    │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                              │                                               │
│                              ▼                                               │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │  Step 2: EXTRACT ENTITIES                                              │  │
│  │  → Source node: "TL;DR summary" → resolve to node_001                  │  │
│  │  → Target node: "compliance check" → resolve to node_003               │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                              │                                               │
│                              ▼                                               │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │  Step 3: VALIDATE AGAINST CANVAS STATE                                 │  │
│  │  → node_001 exists? ✓                                                  │  │
│  │  → node_003 exists? ✓                                                  │  │
│  │  → Edge already exists? ✗ (proceed)                                    │  │
│  │  → Creates cycle? ✗ (safe)                                             │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                              │                                               │
│                              ▼                                               │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │  Step 4: GENERATE OPERATION                                            │  │
│  │  → Tool: createEdge                                                    │  │
│  │  → Parameters: { sourceId: "node_001", targetId: "node_003" }         │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Clarification Loops for Ambiguous Input

When intent or entities cannot be determined with high confidence, the AI asks for clarification:

| Ambiguity Type | Example | AI Response |
|----------------|---------|-------------|
| **Multiple matches** | "Connect the analysis node" (3 nodes have "analysis") | "Which analysis node? I see: [1] Compliance Analysis, [2] Risk Analysis, [3] Financial Analysis" |
| **Missing target** | "Add an edge" | "Where should I connect from and to? You can say 'from X to Y' or select nodes on the canvas." |
| **Unknown scope** | "Use the Smith contract skill" | "I couldn't find a skill named 'Smith contract'. Did you mean: [1] Standard Contract Review, [2] Create new skill?" |
| **Conflicting request** | "Connect A to B" (edge exists) | "A is already connected to B. Did you want to: [1] Remove the existing connection, [2] Add a parallel path?" |

### Scope Selection Algorithm

When the AI needs to link an Action, Skill, Knowledge, or Tool to a node, it uses a **scope selection algorithm**:

1. **Determine Required Scope Types** - Based on node type
2. **Search Existing Scopes** - Dataverse query with semantic matching
3. **Rank by Relevance** - Semantic similarity, document type compatibility, usage frequency
4. **Select or Prompt** - Auto-select if confidence > 0.85, otherwise ask user

**Scope Metadata Used for Matching:**

| Scope Type | Matching Attributes |
|------------|---------------------|
| **Action** | `name`, `description`, `tags`, `documentTypes`, `matterTypes` |
| **Skill** | `name`, `description`, `tags`, `applicableDocTypes` |
| **Knowledge** | `name`, `description`, `sourceType`, `contentTags` |
| **Tool** | `name`, `description`, `handlerType`, `inputSchema` |

### Quick Action Buttons (Suggestions, Not Requirements)

The chat UI offers **contextual quick actions** based on canvas state:

| Canvas State | Suggested Buttons |
|--------------|-------------------|
| Empty canvas | `[🏗 Build from Template]` `[📝 Describe Requirements]` |
| Single node | `[+ Add Node]` `[🔧 Configure]` `[📦 Link Scope]` |
| Multiple nodes, no edges | `[➔ Connect Nodes]` `[🔀 Auto-Layout]` |
| Node selected | `[🔧 Configure]` `[📦 Link Scope]` `[🗑 Remove]` |
| Building complete | `[✓ Validate]` `[▶ Test Run]` `[💾 Save]` |

### Ensuring Near-Deterministic Behavior

**1. Constrained System Prompt** - Enumerates all valid intents, tools, and rules

**2. Canvas State Validation** - Validates operations before execution

**3. Operation Audit Trail** - Logs user input, classified intent, and operation details

**4. Fallback to Clarification** - If thresholds fail, ask rather than guess:

| Metric | Threshold | Action if Below |
|--------|-----------|-----------------|
| Intent confidence | < 0.75 | Ask "Did you mean to [A] or [B]?" |
| Entity resolution | < 0.80 | Show matching options |
| Scope match score | < 0.70 | Ask user to select or create |
| Validation | Fails | Explain why and suggest alternatives |

**5. Conversation Context Window** - Maintains last 10 operations, canvas summary, active mode

### Error Recovery and Undo

| Scenario | AI Behavior |
|----------|-------------|
| User says "undo" | Reverse last operation, explain what was undone |
| Operation fails (Dataverse error) | Explain failure, suggest retry or alternative |
| User describes impossible operation | Explain why, suggest valid alternatives |
| Session interruption | Preserve canvas state, resume from last valid state |

---

## Test Execution Architecture

The AI Builder supports three test execution modes, allowing users to validate playbooks at different stages without polluting production data.

### Test Execution Modes

| Mode | Playbook Saved? | Document Storage | Creates Records? | Use Case |
|------|-----------------|------------------|------------------|----------|
| **Mock Test** | No | None (sample data) | No | Quick logic validation |
| **Quick Test** | No | Temp blob (24hr TTL) | No | Real document, ephemeral |
| **Production Test** | Yes | SPE file | Yes | Full end-to-end validation |

### Mode Details

**1. Mock Test (No Document)**

For rapid iteration during playbook design:

```
Canvas JSON ──▶ BFF API ──▶ Execute with sample data ──▶ Results
(in memory)                 (no Document Intelligence)
```

- Playbook canvas JSON sent in request body (not yet saved to Dataverse)
- Synthesized/sample document data based on document type
- No storage, no records created
- **Purpose**: Validate playbook logic, node flow, and condition routing quickly

**2. Quick Test (Upload, Ephemeral)**

For testing with real documents without committing:

```
Canvas JSON ─┐
(in memory)  │
             ├──▶ BFF API ──▶ Temp Blob ──▶ Doc Intel ──▶ Execute
Upload File ─┘               (24hr TTL)                    │
                                                           ▼
                                           Results (not persisted)
```

- User uploads a document file directly in the AI Builder modal
- File stored in **temp blob storage** with 24-hour TTL
- Text extracted via Azure Document Intelligence
- Playbook executed against real extracted text
- Results returned but **NOT persisted** to Dataverse
- **Purpose**: Test with real documents before committing to save

**3. Production Test (Full Flow)**

For validating the complete production pipeline:

```
Saved Playbook ─┐
(Dataverse)     │
                ├──▶ BFF API ──▶ SPE ──▶ Doc Intel ──▶ Execute
Select/Upload ──┘               File                    │
Document                                                ▼
                                        sprk_document record
                                        sprk_analysisoutput record
```

- Requires playbook to be saved first
- User selects existing SPE document OR uploads (which gets stored in SPE)
- Full flow: SPE storage → Document Intelligence → Playbook execution → Analysis Output record
- **Purpose**: Validate complete end-to-end flow matches production behavior

### API Endpoint

```
POST /api/ai/test-playbook-execution
Content-Type: multipart/form-data

{
  // Playbook source (one required)
  "playbookId": "guid",                    // If saved playbook
  "canvasJson": { nodes: [], edges: [] },  // If unsaved (in-memory)

  // Test document (required for quick/production, optional for mock)
  "testDocument": <binary>,                // Uploaded file

  // Test options
  "options": {
    "mode": "mock" | "quick" | "production",
    "persistResults": false,               // Default: false for mock/quick
    "sampleDocumentType": "LEASE"          // For mock mode: which sample to use
  }
}

Response (SSE Stream):
  event: node_start
  data: { "nodeId": "node_001", "label": "TL;DR Summary" }

  event: node_output
  data: { "nodeId": "node_001", "output": { ... }, "duration_ms": 2100 }

  event: node_complete
  data: { "nodeId": "node_001", "success": true }

  ... (repeat for each node)

  event: execution_complete
  data: {
    "success": true,
    "nodesExecuted": 11,
    "nodesSkipped": 1,
    "totalDuration_ms": 22900,
    "reportUrl": "https://..." // Temp URL for quick test, persistent for production
  }
```

### Test Mode Comparison

| Consideration | Mock Test | Quick Test | Production Test |
|---------------|-----------|------------|-----------------|
| **Speed** | Fastest (~5s) | Medium (~20-30s) | Slowest (~30-60s) |
| **Realism** | Low (sample data) | High (real extraction) | Highest (full flow) |
| **Cleanup needed** | None | None (auto-expires) | Yes (records created) |
| **Tests Doc Intelligence** | No | Yes | Yes |
| **Tests SPE integration** | No | No | Yes |
| **Requires save** | No | No | Yes |

**Recommendation**: Default to **Quick Test** for the best balance of realism and convenience.

### Temp Storage for Quick Test

| Aspect | Implementation |
|--------|----------------|
| Storage | Azure Blob Storage with 24-hour TTL |
| Container | `test-documents` (separate from production) |
| Naming | `{sessionId}/{timestamp}_{filename}` |
| Cleanup | Azure Blob lifecycle policy auto-deletes after 24 hours |
| Security | Scoped SAS tokens, per-user isolation |
| Max size | 50MB per document |

---

## Scope Management Architecture

The AI Builder includes intelligent scope management that balances **out-of-the-box value** with **customer customization flexibility**.

### Ownership Model

All scopes and playbooks have an ownership designation:

| Owner | Prefix | Editable? | Deletable? | Purpose |
|-------|--------|-----------|------------|---------|
| **System** | `SYS-` | No | No | Out-of-the-box, Spaarke-provided |
| **Customer** | `CUST-` | Yes | Yes | Customer-created or customized |

**System scopes are immutable** — this protects:
- Spaarke IP (proprietary knowledge sources, optimized prompts)
- Upgrade path (system scopes can be improved without breaking customer playbooks)
- Support consistency (known baseline for troubleshooting)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         OWNERSHIP MODEL                                      │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  SYSTEM (Spaarke-provided)              CUSTOMER (Tenant-specific)          │
│  ─────────────────────────              ─────────────────────────           │
│                                                                              │
│  ┌──────────────────────┐              ┌──────────────────────┐            │
│  │ SYS-ACT-001          │              │ CUST-ACT-001         │            │
│  │ Entity Extraction    │──"Save As"──▶│ Custom Entity Ext.   │            │
│  │ 🔒 Immutable         │              │ ✏️ Editable          │            │
│  └──────────────────────┘              └──────────────────────┘            │
│                                                                              │
│  ┌──────────────────────┐              ┌──────────────────────┐            │
│  │ SYS-SKL-004          │              │ CUST-SKL-004-A       │            │
│  │ Lease Review         │──"Extend"───▶│ Commercial RE Lease  │            │
│  │ 🔒 Immutable         │   (inherits) │ (extends SYS-SKL-004)│            │
│  └──────────────────────┘              └──────────────────────┘            │
│                                                                              │
│  ┌──────────────────────┐              ┌──────────────────────┐            │
│  │ SYS-PB-004           │              │ CUST-PB-001          │            │
│  │ Lease Review         │──"Save As"──▶│ Acme Lease Review    │            │
│  │ 🔒 Immutable         │   (copy)     │ ✏️ Editable          │            │
│  └──────────────────────┘              └──────────────────────┘            │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Scope Customization Options

**Three paths for scope customization:**

| Option | When to Use | Result |
|--------|-------------|--------|
| **Save As** | Need significant changes to existing scope | New independent scope (CUST-*) |
| **Extend** | Need to add to existing scope, keep base | New sub-scope inheriting from parent |
| **Create New** | No suitable base exists | New independent scope (CUST-*) |

**Inheritance Benefits (Extend):**
- When Spaarke improves base scope, extensions automatically get improvements
- Customer additions are preserved
- Clear lineage tracking

### Scope Type Summary

| Scope Type | AI Can Create? | Customization Options | Notes |
|------------|----------------|----------------------|-------|
| **Actions** | ✅ Yes | Save As, Extend, Create New | System prompts - text only |
| **Skills** | ✅ Yes | Save As, Extend, Create New | Prompt fragments - text only |
| **Knowledge** | ⚠️ Metadata only | Save As, Create New | Content requires separate upload |
| **Tools** | ❌ No (select/configure) | Configure parameters | Code handlers via product roadmap |
| **Outputs** | ✅ Yes | Save As, Create New | JSON field mappings |

### Tool Configuration (No Custom Code)

Tools require C# handler implementations, so the AI cannot create new tool handlers. Instead:

**1. Out-of-the-Box Tool Library**

| Tool ID | Handler | Configurable Parameters |
|---------|---------|------------------------|
| `SYS-TL-001` | `EntityExtractorHandler` | `entityTypes[]`, `customEntities[]`, `confidenceThreshold` |
| `SYS-TL-002` | `ClauseAnalyzerHandler` | `clauseCategories[]`, `customCategories[]`, `riskThreshold` |
| `SYS-TL-003` | `DocumentClassifierHandler` | `taxonomy`, `customTypes[]`, `confidenceThreshold` |
| `SYS-TL-004` | `SummaryHandler` | `length`, `format`, `focus`, `sections[]` |
| `SYS-TL-005` | `RiskDetectorHandler` | `riskCategories[]`, `customRisks[]`, `severityScale` |
| `SYS-TL-006` | `ClauseComparisonHandler` | `comparisonSource`, `customCriteria[]` |
| `SYS-TL-007` | `DateExtractorHandler` | `dateFormat`, `includeRelative`, `customDateTypes[]` |
| `SYS-TL-008` | `FinancialCalculatorHandler` | `operations[]`, `currency`, `customCalculations[]` |
| `SYS-TL-009` | `GenericAnalysisHandler` | `outputSchema`, `validationRules`, `postProcessing` |

**2. Generic Analysis Handler (Highly Configurable)**

The `GenericAnalysisHandler` enables AI-generated "virtual tools" via JSON configuration:

```json
{
  "toolId": "CUST-TL-001",
  "name": "Insurance Certificate Analyzer",
  "handlerType": "GenericAnalysisHandler",
  "configuration": {
    "outputSchema": {
      "insurer": { "type": "string", "required": true },
      "policyNumber": { "type": "string", "required": true },
      "coverageTypes": { "type": "array", "items": "string" },
      "limits": {
        "type": "object",
        "properties": {
          "generalLiability": "currency",
          "autoLiability": "currency",
          "umbrella": "currency",
          "workersComp": "string"
        }
      },
      "effectiveDate": { "type": "date" },
      "expirationDate": { "type": "date" }
    },
    "validationRules": [
      { "field": "expirationDate", "rule": "futureDate", "errorMessage": "Certificate is expired" }
    ]
  }
}
```

### Proactive Scope Gap Detection

The AI continuously analyzes the playbook being built and proactively identifies scope gaps:

1. **Continuous Analysis** - AI monitors playbook purpose, node types, document types, domain
2. **Gap Identification** - Compare required scopes vs. available scopes
3. **Proactive Suggestion** - When gap detected, offer options
4. **On-Demand Creation** - AI generates scope definition, user reviews and approves

### Dataverse Schema for Scope Ownership

```
sprk_analysisscope (extended)
├── sprk_scopeid (PK)
├── sprk_name
├── sprk_scopetype (Action, Skill, Knowledge, Tool, Output)
├── sprk_ownertype (System, Customer)          ← NEW
├── sprk_parentscope (lookup to self)           ← NEW (for extension inheritance)
├── sprk_basedon (lookup to self)               ← NEW (for "Save As" tracking)
├── sprk_isimmutable (boolean)                  ← NEW
├── sprk_promptcontent (text)
├── sprk_configuration (JSON)
└── sprk_metadata (JSON: tags, documentTypes, etc.)
```

---

## Unified AI Agent Framework

The AI Playbook Builder and AI Document Analysis share the **same underlying architecture**. This isn't a coincidence—it's the foundational insight that makes the Playbook Builder a universal composition tool for all Spaarke AI capabilities.

### The "Plan" Concept Clarified

A key question: What is the "plan" that drives execution?

| System | The "Plan" | The "Plan Runner" | Output |
|--------|-----------|-------------------|--------|
| **AI Document Analysis** | Playbook definition (nodes, edges, scopes) | `PlaybookExecutionEngine` | Analysis results, field mappings |
| **AI Playbook Builder** | Internal build spec (spec/tasks JSON) | `BuilderOrchestrator` | Playbook definition JSON |

**Key Insight**: Both are structured plans executed by an engine. The playbook definition IS the plan for analysis—nodes define what to do, edges define order, scopes provide the instructions and tools. The `PlaybookExecutionEngine` walks the graph and executes each node.

For the Builder, the "internal build plan" serves the same purpose—it's a structured representation of what the AI will construct, and the builder orchestration executes it step by step.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         THE "PLAN" ARCHITECTURE                              │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  DOCUMENT ANALYSIS FLOW                                                      │
│  ──────────────────────                                                      │
│                                                                              │
│  Input: Document          Plan: Playbook Definition       Output: Analysis   │
│  ┌─────────────┐         ┌─────────────────────────┐     ┌─────────────┐   │
│  │ Lease.pdf   │   ───▶  │ PB-001: Lease Review    │ ──▶ │ Analysis    │   │
│  └─────────────┘         │ ┌─────┐ ┌─────┐ ┌─────┐│     │ Results     │   │
│                          │ │TL;DR│→│Terms│→│Risk ││     └─────────────┘   │
│                          │ └─────┘ └─────┘ └─────┘│                        │
│                          └─────────────────────────┘                        │
│                                     ▲                                        │
│                                     │                                        │
│                          PlaybookExecutionEngine                             │
│                          (plan runner)                                       │
│                                                                              │
│  ═══════════════════════════════════════════════════════════════════════════│
│                                                                              │
│  PLAYBOOK BUILDER FLOW                                                       │
│  ─────────────────────                                                       │
│                                                                              │
│  Input: User Chat         Plan: Internal Build Spec       Output: Playbook   │
│  ┌─────────────┐         ┌─────────────────────────┐     ┌─────────────┐   │
│  │ "Build a    │   ───▶  │ BuildSpec (internal):   │ ──▶ │ PB-001:     │   │
│  │  lease      │         │ • Purpose: analyze lease│     │ Lease       │   │
│  │  analysis   │         │ • Nodes: [TL;DR, ...]   │     │ Review      │   │
│  │  playbook"  │         │ • Tasks: [step 1, ...]  │     │ (canvas)    │   │
│  └─────────────┘         └─────────────────────────┘     └─────────────┘   │
│                                     ▲                                        │
│                                     │                                        │
│                          BuilderOrchestrator                                 │
│                          (same pattern, different context)                   │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### PB-BUILDER: The Meta-Playbook

The ultimate expression of this unified architecture: **the Builder itself is a playbook**.

```
PB-BUILDER: AI Playbook Builder
├── Input: User conversation (multi-turn chat)
├── Context: Existing canvas state, conversation history
├── Scopes:
│   ├── ACT-BUILDER-*  (system prompts for building operations)
│   ├── SKL-BUILDER-*  (skills: how to create node types, select scopes)
│   ├── TL-BUILDER-*   (tools: addNode, linkScope, searchScopes, etc.)
│   ├── KNW-BUILDER-*  (knowledge: reference patterns, schema definitions)
│   └── OUT-BUILDER-*  (outputs: canvas JSON, Dataverse operations)
└── Output: Playbook definition (canvas JSON + linked scopes)
```

**This is what makes the Playbook Builder powerful**: It uses the same building blocks (actions, skills, knowledge, tools, outputs) that it helps users compose.

### Builder-Specific Scopes

**Builder Actions (ACT-BUILDER-*)**

| ID | Name | Purpose |
|----|------|---------|
| `ACT-BUILDER-001` | Intent Classification | Parse user message into operation intent |
| `ACT-BUILDER-002` | Node Configuration | Generate node config from requirements |
| `ACT-BUILDER-003` | Scope Selection | Select appropriate existing scope |
| `ACT-BUILDER-004` | Scope Creation | Generate new scope definition |
| `ACT-BUILDER-005` | Build Plan Generation | Create structured build plan from requirements |

**Builder Skills (SKL-BUILDER-*)**

| ID | Name | Purpose |
|----|------|---------|
| `SKL-BUILDER-001` | Lease Analysis Pattern | How to build lease analysis playbooks |
| `SKL-BUILDER-002` | Contract Review Pattern | How to build contract review playbooks |
| `SKL-BUILDER-003` | Risk Assessment Pattern | How to structure risk detection flows |
| `SKL-BUILDER-004` | Node Type Guide | When to use each node type |
| `SKL-BUILDER-005` | Scope Matching | How to find/create appropriate scopes |

**Builder Tools (TL-BUILDER-*)**

| ID | Name | Parameters |
|----|------|------------|
| `TL-BUILDER-001` | `addNode` | `type`, `label`, `position`, `config` |
| `TL-BUILDER-002` | `removeNode` | `nodeId` |
| `TL-BUILDER-003` | `createEdge` | `sourceId`, `targetId` |
| `TL-BUILDER-004` | `updateNodeConfig` | `nodeId`, `config` |
| `TL-BUILDER-005` | `linkScope` | `nodeId`, `scopeType`, `scopeId` |
| `TL-BUILDER-006` | `createScope` | `type`, `data` |
| `TL-BUILDER-007` | `searchScopes` | `type`, `query` |
| `TL-BUILDER-008` | `autoLayout` | — |
| `TL-BUILDER-009` | `validateCanvas` | — |

**Builder Knowledge (KNW-BUILDER-*)**

| ID | Name | Content |
|----|------|---------|
| `KNW-BUILDER-001` | Scope Catalog | All available system scopes (for search) |
| `KNW-BUILDER-002` | Reference Playbooks | Example playbook patterns |
| `KNW-BUILDER-003` | Node Schema | Valid node configurations |
| `KNW-BUILDER-004` | Best Practices | Playbook design guidelines |

### Execution Engine Modes

The `PlaybookExecutionEngine` supports two modes:

| Mode | Context Type | Interaction | Use Case |
|------|--------------|-------------|----------|
| **Batch** | Document content | Single run, streaming output | Document analysis (PB-LEASE-001, etc.) |
| **Conversational** | Chat history + canvas state | Multi-turn, incremental output | Builder (PB-BUILDER) |

```csharp
public interface IPlaybookExecutionEngine
{
    // Batch mode: single document, streaming analysis
    IAsyncEnumerable<NodeResult> ExecuteAsync(
        Playbook playbook,
        DocumentContext document,
        CancellationToken ct);

    // Conversational mode: multi-turn, incremental canvas updates
    IAsyncEnumerable<BuilderResult> ExecuteConversationalAsync(
        Playbook builderPlaybook,
        ConversationContext conversation,
        CanvasState currentCanvas,
        CancellationToken ct);
}

public record ConversationContext
{
    public ChatMessage[] History { get; init; }
    public string CurrentMessage { get; init; }
    public Dictionary<string, object> SessionState { get; init; }
}

public record CanvasState
{
    public PlaybookNode[] Nodes { get; init; }
    public PlaybookEdge[] Edges { get; init; }
    public Dictionary<string, Guid> LinkedScopes { get; init; }
}
```

### Why This Matters

1. **Single Framework**: No separate "builder AI" and "analysis AI"—same patterns, same infrastructure
2. **Composability**: Builder improvements automatically leverage analysis framework improvements
3. **Consistency**: Same scope model, same execution model, same extension mechanisms
4. **Future Potential**: Other AI capabilities (workflows, agents) can use the same playbook model

The Playbook Builder becomes **THE universal composition tool** for all Spaarke AI capabilities.

### AI Model Strategy

Different operations in the Builder benefit from different AI model characteristics:

| Operation Type | Recommended Model | Rationale |
|---------------|-------------------|-----------|
| **Intent Classification** | GPT-4o-mini | Fast, cheap, structured output sufficient |
| **Entity Resolution** | GPT-4o-mini | Quick lookups, pattern matching |
| **Build Plan Generation** | o1-mini | Complex reasoning, multi-step planning |
| **Scope Content Generation** | GPT-4o | High-quality text generation |
| **Validation & Explanation** | GPT-4o-mini | Quick checks, simple outputs |

```csharp
public class ModelSelector
{
    public string SelectModel(BuilderOperation operation) => operation.Type switch
    {
        OperationType.IntentClassification => "gpt-4o-mini",
        OperationType.EntityResolution => "gpt-4o-mini",
        OperationType.PlanGeneration => "o1-mini",
        OperationType.ScopeGeneration => "gpt-4o",
        OperationType.Validation => "gpt-4o-mini",
        OperationType.Explanation => "gpt-4o-mini",
        _ => "gpt-4o"  // default to capable model
    };
}
```

This tiered approach optimizes for both cost and quality—fast models for quick decisions, capable models for complex generation, reasoning models for planning.

---

## Implementation Plan

### Phase 1: Infrastructure (1.5 weeks)

- [ ] Create `AiPlaybookBuilderService` in BFF
- [ ] Add `/api/ai/build-playbook-canvas` endpoint
- [ ] Define canvas patch schema
- [ ] Implement Dataverse operation execution
- [ ] Extend `PlaybookExecutionEngine` with conversational mode
- [ ] Add `ConversationContext` and `CanvasState` models
- [ ] Implement incremental canvas update streaming

### Phase 2: PCF Components (1 week)

- [ ] Create `aiAssistantStore`
- [ ] Build `AiAssistantModal` component
- [ ] Build `ChatHistory` and `ChatInput` components
- [ ] Wire up SSE streaming to store
- [ ] Add toolbar button to toggle modal

### Phase 3: AI Integration + Builder Scopes (1.5 weeks)

- [ ] Design system prompt for canvas building
- [ ] Implement canvas analysis (understand current state)
- [ ] Implement operation generation
- [ ] Add scope record creation/linking
- [ ] Create builder-specific Action definitions (ACT-BUILDER-001 through ACT-BUILDER-005)
- [ ] Create builder-specific Skill definitions (SKL-BUILDER-001 through SKL-BUILDER-005)
- [ ] Create builder Tool definitions (TL-BUILDER-001 through TL-BUILDER-009)
- [ ] Create builder Knowledge content (scope catalog, reference playbooks, schemas)
- [ ] Implement `ModelSelector` for tiered model selection
- [ ] Define PB-BUILDER playbook in Dataverse
- [ ] Test with real playbook scenarios

### Phase 4: Test Execution (1 week)

- [ ] Add `/api/ai/test-playbook-execution` endpoint
- [ ] Implement mock test with sample data generation
- [ ] Implement quick test with temp blob storage
- [ ] Integrate Document Intelligence for quick test
- [ ] Build test options dialog in PCF
- [ ] Build test execution progress view
- [ ] Add test result preview/download

### Phase 5: Scope Management (1 week)

- [ ] Add scope ownership fields to Dataverse schema
- [ ] Implement "Save As" for playbooks and scopes
- [ ] Implement scope extension (inheritance) logic
- [ ] Build Scope Browser component in PCF
- [ ] Add scope creation dialogs (Action, Skill, Output)
- [ ] Add Knowledge Source configuration wizard
- [ ] Implement tool parameter configuration UI
- [ ] Add `GenericAnalysisHandler` for configurable tools
- [ ] Implement proactive scope gap detection in AI

### Phase 6: Polish (0.5-1 week)

- [ ] Error handling and retry
- [ ] Loading states and animations
- [ ] Keyboard shortcuts (Cmd/Ctrl+K to open)
- [ ] Responsive modal sizing
- [ ] Documentation

### Effort Estimate

| Component | Effort |
|-----------|--------|
| BFF endpoint + service | 3-4 days |
| Conversational execution mode | 1-2 days |
| Dataverse operations | 2-3 days |
| PCF modal + stores | 3-4 days |
| AI prompt engineering | 2-3 days |
| Builder-specific scopes (ACT/SKL/TL/KNW-BUILDER-*) | 2-3 days |
| Model selector + PB-BUILDER definition | 1 day |
| Test execution (3 modes) | 4-5 days |
| Scope management + browser | 5-6 days |
| Testing + polish | 3-4 days |
| **Total** | **6-7 weeks** |

### Timeline Impact Summary

The unified AI agent framework adds ~1 week to the original estimate:

| Addition | Effort | Rationale |
|----------|--------|-----------|
| Conversational execution mode | 1-2 days | Clean extension of existing engine |
| Builder-specific scopes | 2-3 days | Mostly prompt content, not code |
| Model selector | 0.5 days | Configuration component |
| PB-BUILDER definition | 0.5 days | Dataverse record + canvas JSON |

**Trade-off**: +1 week now establishes the foundation for ALL future AI capabilities (workflows, agents) using the same playbook model.

---

## Related Documents

- [AI Playbook Architecture](../../docs/architecture/AI-PLAYBOOK-ARCHITECTURE.md) - Node-based execution architecture
- [AI Analysis Playbook Scope Design](../../docs/architecture/AI-ANALYSIS-PLAYBOOK-SCOPE-DESIGN.md) - Scope definitions
- [Playbook Real Estate Lease Guide](../../docs/guides/PLAYBOOK-REAL-ESTATE-LEASE-ANALYSIS.md) - Example playbook
- [design.md](design.md) - Parent project design (ENH-001 through ENH-004)

---

## Revision History

| Date | Author | Changes |
|------|--------|---------|
| 2026-01-16 | AI Architecture Team | Initial document created from ENH-005 |
| 2026-01-16 | AI Architecture Team | Added project-style AI architecture (skills, tools, build plan) |
| 2026-01-16 | AI Architecture Team | Added conversational UX guidance with near-deterministic interpretation |
| 2026-01-16 | AI Architecture Team | Added test execution architecture (mock, quick, production modes) |
| 2026-01-16 | AI Architecture Team | Added scope management architecture (ownership model, Save As, Extend, tool config) |
| 2026-01-16 | AI Architecture Team | Added unified AI agent framework (PB-BUILDER meta-playbook, plan concept, builder scopes, model strategy) |
| 2026-01-16 | AI Architecture Team | Updated timeline to 6-7 weeks reflecting unified framework additions |
