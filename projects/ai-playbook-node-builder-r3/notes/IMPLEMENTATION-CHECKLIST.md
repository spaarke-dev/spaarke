# AI Playbook Builder - Implementation Checklist

> **Created**: 2026-01-20
> **Project**: ai-playbook-node-builder-r3
> **Goal**: "Claude Code for Playbooks" - conversational AI-assisted playbook building

---

## Vision: What Makes This "Claude Code for Playbooks"

Just like Claude Code enables developers to build software through conversation, the AI Playbook Builder should enable users to build document analysis playbooks conversationally. The key capabilities:

| Claude Code Capability | AI Playbook Builder Equivalent |
|------------------------|-------------------------------|
| **Tools** - Read files, write code, run commands | **Tools** - Add nodes, create edges, search scopes, create scopes |
| **Codebase Awareness** - Knows project structure | **Scope Awareness** - Knows available Actions, Skills, Knowledge, Tools |
| **Execute Operations** - Makes real changes | **Canvas Operations** - Creates/modifies playbook structure |
| **Create Artifacts** - New files, configs | **Create Scopes** - New custom Actions/Skills when needed |
| **Multi-step Reasoning** - Agentic loops | **Agentic Execution** - Iterative playbook building |

### Current Gap (Pre-Phase 2)

The AI is currently "blind" - it:
- ❌ Can't see what scopes exist in the system
- ❌ Can't search for scopes matching user needs
- ❌ Can't create new scopes dynamically
- ❌ Returns JSON intent, not tool calls
- ❌ Single-turn execution only

### Target State (Post-Phase 2+3)

The AI has full awareness and capabilities:
- ✅ Receives tool definitions (OpenAI Function Calling)
- ✅ Can search scope catalog (`search_scopes` tool)
- ✅ Can add nodes with specific scopes (`add_node` tool)
- ✅ Can create custom scopes (`create_scope` tool)
- ✅ Agentic loop for multi-step operations

---

## Current State Summary

The AI Playbook Builder has three main components:

| Component | Location | Status |
|-----------|----------|--------|
| **PCF Control** | `src/client/pcf/PlaybookBuilderHost/` | v2.25.0+ with slash commands |
| **Backend Service** | `src/server/api/Sprk.Bff.Api/Services/Ai/` | ✅ Phase 2 complete (agentic + tools) |
| **Builder Scopes** | `projects/.../notes/builder-scopes/` | Design artifacts (23 JSON files) |

**Phase Status**:
| Phase | Status |
|-------|--------|
| Phase 1: Conversational Experience | ✅ Complete |
| Phase 2: Tool Schema Integration | ✅ Complete |
| Phase 3: Knowledge Scope Integration | ⏳ Pending |
| Phase 4: Dataverse Persistence | ⏳ Future |

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│  PCF Control (PlaybookBuilderHost)                                      │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐                   │
│  │ AiChatPanel  │  │ BuilderCanvas│  │ NodePalette  │                   │
│  │ (streaming)  │  │ (React Flow) │  │ (drag nodes) │                   │
│  └──────┬───────┘  └──────────────┘  └──────────────┘                   │
│         │ SSE events                                                     │
└─────────┼───────────────────────────────────────────────────────────────┘
          │
          ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  BFF API Endpoints                                                      │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │ POST /api/ai-playbook-builder/message                              │ │
│  │ POST /api/ai-playbook-builder/clarify                              │ │
│  │ → AiPlaybookBuilderEndpoints.cs                                    │ │
│  └────────────────────────────────────────────────────────────────────┘ │
└─────────┬───────────────────────────────────────────────────────────────┘
          │
          ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  AI Services                                                            │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │ AiPlaybookBuilderService.cs                                        │ │
│  │ ├─ GetBuilderScopePromptAsync() → loads prompts                    │ │
│  │ ├─ ClassifyIntentWithAiAsync() → calls OpenAI                      │ │
│  │ └─ ProcessMessageAsync() → orchestrates flow                       │ │
│  └────────────────────────────────────────────────────────────────────┘ │
│                                                                         │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │ Prompts:                                                           │ │
│  │ ├─ PlaybookBuilderSystemPrompt.cs (master prompts)                 │ │
│  │ └─ FallbackPrompts.cs (when Dataverse unavailable)                 │ │
│  └────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Key File References

### Backend (C#)

| File | Purpose | Notes |
|------|---------|-------|
| [AiPlaybookBuilderEndpoints.cs](../../../src/server/api/Sprk.Bff.Api/Api/Ai/AiPlaybookBuilderEndpoints.cs) | SSE streaming endpoints | Lines 238, 462: thinking event messages |
| [AiPlaybookBuilderService.cs](../../../src/server/api/Sprk.Bff.Api/Services/Ai/AiPlaybookBuilderService.cs) | AI orchestration service | Lines 134-237: scope loading with cache |
| [PlaybookBuilderSystemPrompt.cs](../../../src/server/api/Sprk.Bff.Api/Services/Ai/Prompts/PlaybookBuilderSystemPrompt.cs) | Master system prompts | Line 36: IntentClassification prompt |
| [FallbackPrompts.cs](../../../src/server/api/Sprk.Bff.Api/Services/Ai/FallbackPrompts.cs) | Fallback when Dataverse unavailable | All prompts for ACT/SKL-BUILDER-* |
| [IScopeResolverService.cs](../../../src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs) | Dataverse scope loading interface | Used by GetBuilderScopePromptAsync |

### Frontend (PCF)

| File | Purpose | Notes |
|------|---------|-------|
| [PlaybookBuilderHost.tsx](../../../src/client/pcf/PlaybookBuilderHost/control/PlaybookBuilderHost.tsx) | Main PCF control | v2.25.0: fixed header |
| [BuilderLayout.tsx](../../../src/client/pcf/PlaybookBuilderHost/control/components/BuilderLayout.tsx) | Layout component | `leftPanelOpen` default = true |
| [AiChatPanel.tsx](../../../src/client/pcf/PlaybookBuilderHost/control/components/AiChatPanel.tsx) | Chat UI component | Displays streaming responses |
| [useAiStreaming.ts](../../../src/client/pcf/PlaybookBuilderHost/control/hooks/useAiStreaming.ts) | SSE streaming hook | Handles SSE events |

### Builder Scopes (Design Artifacts)

| Category | Count | Location |
|----------|-------|----------|
| Actions | 5 | [ACT-BUILDER-*.json](builder-scopes/) |
| Skills | 5 | [SKL-BUILDER-*.json](builder-scopes/) |
| Tools | 9 | [TL-BUILDER-*.json](builder-scopes/) |
| Knowledge | 4 | [KNW-BUILDER-*.json](builder-scopes/) |

See [builder-scopes/INDEX.md](builder-scopes/INDEX.md) for full inventory.

---

## Implementation Phases

### Phase 1: Conversational Experience (COMPLETE)

**Goal**: Make the AI feel conversational, not robotic

- [x] Update `FallbackPrompts.IntentClassification` to "Claude Code" style
- [x] Update `PlaybookBuilderSystemPrompt.IntentClassification` to match
- [x] Change thinking events: "Thinking" instead of "Classifying intent"
- [x] Add `message` field to JSON response format
- [x] PCF v2.25.0: Left palette open by default
- [x] PCF v2.25.0: Fixed header with Fluent V9 styling
- [x] Commit and push (PR #142)
- [x] **Deploy API to Azure** (2026-01-20) - https://spe-api-dev-67e2xz.azurewebsites.net

**Files Modified**:
- `FallbackPrompts.cs` - IntentClassification prompt
- `PlaybookBuilderSystemPrompt.cs` - IntentClassification prompt
- `AiPlaybookBuilderEndpoints.cs` - thinking event messages
- `BuilderLayout.tsx` - leftPanelOpen default
- `PlaybookBuilderHost.tsx` - fixed header styling

---

### Phase 2: Tool Schema Integration (COMPLETE ✅)

**Goal**: Give the LLM structured tools using OpenAI Function Calling

~~Current state: The LLM receives a prompt describing operations in text. It returns JSON with intent classification. The backend then maps this to canvas operations.~~

**Implemented**: The LLM receives actual tool definitions (OpenAI function calling format). It can call tools directly, which get validated against schemas. The `/agentic` endpoint implements multi-turn tool execution.

---

#### Phase 2a: Create Tool Definitions (COMPLETE ✅)

**Convert TL-BUILDER-* JSON files to OpenAI function calling format.**

| TL-BUILDER | OpenAI Tool Name | Description | Status |
|------------|------------------|-------------|--------|
| TL-BUILDER-001 | `add_node` | Add a node to the playbook canvas | ✅ |
| TL-BUILDER-002 | `remove_node` | Remove a node from the canvas | ✅ |
| TL-BUILDER-003 | `create_edge` | Connect two nodes | ✅ |
| TL-BUILDER-004 | `update_node_config` | Modify node configuration | ✅ |
| TL-BUILDER-005 | `link_scope` | Wire a scope to a node | ✅ |
| TL-BUILDER-006 | `create_scope` | Create a new scope in Dataverse | ✅ |
| TL-BUILDER-007 | `search_scopes` | Find existing scopes by criteria | ✅ |
| TL-BUILDER-008 | `auto_layout` | Arrange canvas nodes | ✅ |
| TL-BUILDER-009 | `validate_canvas` | Validate playbook structure | ✅ |

**Tasks**:
- [x] Create `BuilderToolDefinitions.cs` with all 9 tool schemas
- [x] Create `Models/BuilderToolCall.cs` for response types
- [x] Map each TL-BUILDER inputSchema → OpenAI function parameters

**Files Created** (commit `52ef667`):
```
src/server/api/Sprk.Bff.Api/Services/Ai/Builder/
├── BuilderToolDefinitions.cs   - Tool schemas for OpenAI
├── BuilderToolExecutor.cs      - Tool execution logic (729 lines)
├── BuilderToolCall.cs          - Tool call response models
├── BuilderAgentService.cs      - Agentic loop orchestration
└── AiBuilderErrors.cs          - Error handling
```

**OpenAI Tool Format Example** (from TL-BUILDER-001):
```json
{
  "type": "function",
  "function": {
    "name": "add_node",
    "description": "Add a new node to the playbook canvas",
    "parameters": {
      "type": "object",
      "properties": {
        "nodeType": {
          "type": "string",
          "enum": ["aiAnalysis", "condition", "assemble", "deliver", "loop", "transform", "humanReview", "externalApi"],
          "description": "Type of node to create"
        },
        "label": {
          "type": "string",
          "description": "Display label for the node"
        },
        "position": {
          "type": "object",
          "properties": { "x": {"type": "number"}, "y": {"type": "number"} },
          "description": "Canvas position (optional)"
        },
        "config": {
          "type": "object",
          "description": "Node-specific configuration"
        }
      },
      "required": ["nodeType", "label"]
    }
  }
}
```

---

#### Phase 2b: Integrate Tools with AI Service (COMPLETE ✅)

**Update `ClassifyIntentWithAiAsync` to use function calling.**

**Tasks**:
- [x] Add tools array to OpenAI chat completion request
- [x] Set `tool_choice: "auto"` for flexible tool selection
- [x] Parse `tool_calls` from response (in addition to content)
- [x] Handle mixed responses (text + tool calls)

**Code Changes** (AiPlaybookBuilderService.cs):
```csharp
// Before: JSON intent in response content
var response = await _openAiClient.GetChatCompletionsAsync(options);
var json = response.Value.Choices[0].Message.Content;

// After: Tool calls + optional text
var response = await _openAiClient.GetChatCompletionsAsync(options);
var message = response.Value.Choices[0].Message;
if (message.ToolCalls?.Count > 0)
{
    foreach (var toolCall in message.ToolCalls)
    {
        // Execute tool and collect results
    }
}
```

---

#### Phase 2c: Implement Tool Execution (COMPLETE ✅)

**Map tool calls → service methods → canvas operations.**

**Tasks**:
- [x] Create `BuilderToolExecutor` class
- [x] Implement handler for each tool
- [x] Return tool results as `CanvasPatch` operations
- [x] Support streaming tool execution updates to PCF

**Execution Flow**:
```
LLM Response
    │
    ├─ tool_calls: [{name: "add_node", arguments: {...}}]
    │
    ▼
BuilderToolExecutor.ExecuteAsync(toolCall)
    │
    ├─ Validate arguments against schema
    ├─ Execute operation (create node, search scopes, etc.)
    │
    ▼
CanvasPatch (or ScopeCreationResult, SearchResults, etc.)
    │
    ▼
SSE stream to PCF
```

---

#### Phase 2d: Agentic Loop (Multi-Step Execution) (COMPLETE ✅)

**Enable multi-turn tool execution for complex operations.**

**Implemented**: `/api/ai/playbook-builder/agentic` endpoint with `BuilderAgentService`

**The Problem**: "Build a lease analysis playbook" requires multiple operations:
1. Add aiAnalysis node for entity extraction
2. Add condition node for rent threshold
3. Add assemble node for report generation
4. Connect nodes in sequence

**The Solution**: Agentic loop pattern

```
User: "Build a lease analysis playbook"
    │
    ▼
┌─────────────────────────────────────────────┐
│  AGENTIC LOOP                               │
│                                             │
│  Turn 1: LLM → add_node(aiAnalysis)         │
│          Execute → node created             │
│          Feed result back to LLM            │
│                                             │
│  Turn 2: LLM → add_node(condition)          │
│          Execute → node created             │
│          Feed result back to LLM            │
│                                             │
│  Turn 3: LLM → create_edge(node1→node2)     │
│          Execute → edge created             │
│          Feed result back to LLM            │
│                                             │
│  Turn 4: LLM → "Playbook created!" (done)   │
└─────────────────────────────────────────────┘
    │
    ▼
Final canvas state streamed to PCF
```

**Tasks**:
- [x] Implement turn loop (max 10 turns safety limit)
- [x] Accumulate tool results as context for next turn
- [x] Stream incremental updates to PCF (each tool result)
- [x] Detect completion (no more tool calls, or explicit "done")
- [x] Handle errors gracefully (continue or abort)

**Implementation**: `BuilderAgentService.ExecuteAsync()` in `src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs`

**Loop Termination Conditions**:
1. LLM returns no tool calls (just text) → Done
2. Maximum turns reached (10) → Stop with warning
3. Error in tool execution → Depending on severity, retry or abort

---

**Reference**: [TL-BUILDER-001-addNode.json](builder-scopes/TL-BUILDER-001-addNode.json) shows the schema format:
```json
{
  "configuration": {
    "handlerType": "CanvasOperationHandler",
    "operation": "addNode",
    "inputSchema": { ... },
    "outputSchema": { ... }
  }
}
```

---

### Phase 3: Knowledge Scope Integration (PENDING)

**Goal**: Give the LLM awareness of available scopes/tools

Current state: The LLM knows about node types from the prompt text, but doesn't have dynamic access to the scope catalog.

Target state: Inject KNW-BUILDER-001 (scope catalog) into context so LLM can recommend specific scopes, and implement search functionality.

---

#### Phase 3a: Scope Catalog Injection

**Inject scope catalog into system prompt for awareness.**

**Tasks**:
- [ ] Load `KNW-BUILDER-001-scope-catalog` content at runtime
- [ ] Format as context section in system prompt
- [ ] Enable queries like "what skills are available?"
- [ ] Enable queries like "what can extract entities from documents?"

**System Prompt Injection**:
```markdown
## Available Scopes

You have access to the following pre-built scopes:

### Actions (AI operations)
- **SYS-ACT-001 Entity Extraction**: Extract named entities from documents
- **SYS-ACT-002 Document Summary**: Generate TL;DR summaries
- **SYS-ACT-003 Clause Analysis**: Analyze and categorize clauses
- **SYS-ACT-004 Risk Detection**: Identify potential risks
- **SYS-ACT-005 Financial Term Extraction**: Extract monetary values

### Skills (Domain expertise)
- **SYS-SKL-001 Real Estate Domain**: Leases, deeds, easements
- **SYS-SKL-002 Contract Law Basics**: Contract principles
- **SYS-SKL-003 Financial Analysis**: Financial documents
- **SYS-SKL-004 Insurance Expertise**: Insurance, COI

(etc.)
```

---

#### Phase 3b: Scope Search Tool Implementation

**Implement `search_scopes` tool for dynamic discovery.**

**Tasks**:
- [ ] Implement `TL-BUILDER-007-searchScopes` as `search_scopes` tool
- [ ] Support search by name, description, document types
- [ ] Return matching scopes with relevance ranking
- [ ] Enable LLM to discover scopes beyond injected catalog

**Search Tool Schema**:
```json
{
  "name": "search_scopes",
  "description": "Search for scopes matching criteria",
  "parameters": {
    "query": "string - search text",
    "scopeType": "enum - Action|Skill|Knowledge|Tool",
    "documentTypes": "array - filter by applicable doc types"
  }
}
```

---

#### Phase 3c: Dynamic Scope Linking

**Enable LLM to wire specific scopes to nodes.**

**Tasks**:
- [ ] Implement `TL-BUILDER-005-linkScope` as `link_scope` tool
- [ ] Validate scope exists before linking
- [ ] Update node config with scope reference
- [ ] Stream update to PCF

**Key Files**:
- [KNW-BUILDER-001-scope-catalog.json](builder-scopes/KNW-BUILDER-001-scope-catalog.json) - Contains catalog of actions, skills, knowledge, tools

---

### Phase 4: Dataverse Persistence (FUTURE)

**Goal**: Store builder scopes in Dataverse for dynamic updates

Current state: Builder scopes are JSON design artifacts. FallbackPrompts.cs contains hardcoded versions.

Target state: Import JSON into Dataverse. GetBuilderScopePromptAsync loads from Dataverse with fallback.

**Tasks**:
- [ ] Create Dataverse import script for 23 builder scope records
- [ ] Test GetBuilderScopePromptAsync loading from Dataverse
- [ ] Implement cache invalidation endpoint
- [ ] Add admin UI for scope editing (future)

---

## API Deployment

### Deploy to Azure

```bash
# Build API
cd src/server/api/Sprk.Bff.Api
dotnet publish -c Release -o ./publish

# Deploy via Azure CLI
az webapp deploy --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --src-path ./publish.zip
```

### Verify Deployment

```bash
# Health check
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz

# Test builder endpoint (requires auth)
curl -X POST https://spe-api-dev-67e2xz.azurewebsites.net/api/ai-playbook-builder/message \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer {token}" \
  -d '{"message": "hello", "canvasState": {"nodes": [], "edges": []}}'
```

---

## Testing Checklist

### End-to-End Test Scenarios

| Scenario | Expected Behavior | Status |
|----------|-------------------|--------|
| Say "hello" | Friendly greeting, asks how to help | Pending deploy |
| Say "add a node" | Asks what kind of node or creates aiAnalysis | Pending |
| Say "build a lease playbook" | Creates multi-node playbook plan | Pending |
| Say "what does this do?" | Explains current canvas state | Pending |
| Drag node from palette | Node appears on canvas | Works |
| Connect two nodes | Edge created | Works |

### Regression Tests

- [ ] Existing playbook operations still work
- [ ] Canvas state serialization/deserialization
- [ ] SSE streaming doesn't break
- [ ] Error handling for malformed requests

---

## Related Documentation

| Document | Purpose |
|----------|---------|
| [ADR-013: AI Architecture](../../../docs/adr/ADR-013-ai-architecture.md) | AI tool framework design |
| [SPAARKE-AI-ARCHITECTURE.md](../../../docs/guides/SPAARKE-AI-ARCHITECTURE.md) | AI system overview |
| [PCF-DEPLOYMENT-GUIDE.md](../../../docs/guides/PCF-DEPLOYMENT-GUIDE.md) | PCF deployment workflow |

---

## Session Notes

### 2026-01-20 Session (Morning)

**Completed**:
1. Updated backend prompts to conversational "Claude Code" style
2. Changed thinking events from "Classifying intent" to "Thinking"
3. PCF v2.25.0 deployed with left palette open + fixed header
4. Created PR #142 with all changes
5. Documented architecture and created this checklist

---

### 2026-01-20 Session (Afternoon) - Message Passthrough Fix

**Problem Identified**:
Testing revealed AI responses were generic ("Let me help you with that question") instead of conversational. Root cause: LLM returns a `message` field with conversational text, but it was being dropped.

**Root Cause Analysis**:
- LLM prompt asks for JSON with `message` field for conversational response
- `AiIntentResult` model was missing `Message` property - field was ignored
- `IntentClassification` model was also missing `Message` property
- `ExecuteIntentAsync` used hardcoded messages instead of AI's message

**Fixes Applied**:
1. Added `Message` property to `AiIntentResult` (AiIntentClassificationSchema.cs:37)
2. Added `Message` property to `IntentClassification` (IAiPlaybookBuilderService.cs)
3. Updated `ConvertAiIntentToClassification` to pass through Message
4. Updated `ConvertToIntentClassification` to pass through Message
5. Updated `ProcessMessageAsync` to use AI's message as intro
6. Updated `ExecuteIntentAsync` to remove redundant hardcoded messages

**Files Modified**:
- `AiIntentClassificationSchema.cs` - Added Message property
- `IAiPlaybookBuilderService.cs` - Added Message property to IntentClassification
- `AiPlaybookBuilderService.cs` - Updated conversion and execution methods

**Deployed**: API redeployed to https://spe-api-dev-67e2xz.azurewebsites.net

**Next Steps**:
1. Test conversational experience in Dataverse (hard refresh browser)
2. Verify AI asks clarifying questions and provides friendly responses
3. Begin Phase 2 (tool schema integration) once validated

---

### 2026-01-20 Session (Evening) - Architecture Planning for Tool/Agentic Pattern

**Discussion Summary**:
User articulated the core challenge: "enable the playbook builder AI agent to have awareness of all aspects of building a playbook--just like Claude Code for writing software code--and how do we build that awareness."

**Key Architectural Decisions**:

1. **OpenAI Function Calling** - Use native tool support instead of JSON intent classification
   - More reliable than prompting for JSON
   - Validated against schemas
   - Supports parallel tool calls

2. **Agentic Loop Pattern** - Multi-turn execution for complex operations
   - "Build a lease playbook" requires multiple tool calls
   - Each tool result feeds back into LLM context
   - Continue until LLM stops calling tools or max turns reached

3. **Scope Catalog Injection** - Give LLM awareness of available scopes
   - KNW-BUILDER-001 injected into system prompt
   - Enables "what skills are available?" queries
   - `search_scopes` tool for dynamic discovery beyond catalog

4. **Tool Set** - 9 tools mapped from TL-BUILDER-* files:
   - Canvas ops: `add_node`, `remove_node`, `create_edge`, `update_node_config`, `auto_layout`, `validate_canvas`
   - Scope ops: `link_scope`, `search_scopes`, `create_scope`

**Files Updated**:
- This checklist - Added Vision section, detailed Phase 2 sub-phases, updated Phase 3

**Beginning Implementation**: Phase 2a - Create BuilderToolDefinitions.cs

---

### 2026-01-20 Session (Night) - Phase 2 Complete + PCF Slash Commands

**Phase 2 Implementation Complete** (commit `52ef667`):

All 4 sub-phases of Tool Schema Integration completed:
- **Phase 2a**: `BuilderToolDefinitions.cs` - 9 OpenAI function calling tool schemas
- **Phase 2b**: Tools integrated with AI service via function calling
- **Phase 2c**: `BuilderToolExecutor.cs` (729 lines) - Tool execution logic
- **Phase 2d**: `BuilderAgentService.cs` - Agentic loop with multi-turn execution

**New Backend Files**:
```
src/server/api/Sprk.Bff.Api/Services/Ai/Builder/
├── BuilderToolDefinitions.cs   - Tool schemas
├── BuilderToolExecutor.cs      - Tool execution (729 lines)
├── BuilderToolCall.cs          - Response models
├── BuilderAgentService.cs      - Agentic loop
└── AiBuilderErrors.cs          - Error handling
```

**New API Endpoints**:
- `POST /api/ai/playbook-builder/agentic` - Agentic builder with function calling
- `POST /api/ai/playbook-builder/clarification-response` - Handle clarification responses
- `POST /api/ai/playbook-builder/generate-clarification` - Generate clarification questions

**PCF Slash Command Support** (ChatInput.tsx v1.1.0):
- Added `CommandPalette.tsx` - Visual command picker
- Added `commands.ts` - Command definitions
- Typing `/` shows command palette
- Commands: `/help`, `/clear`, `/new`, etc.

**Files Modified**:
- `ChatInput.tsx` v1.1.0 - Slash command integration
- `AiPlaybookBuilderEndpoints.cs` - New endpoints + correlation IDs in errors

**Status**:
- ✅ Phase 2 (Tool Schema Integration): **COMPLETE**
- ⏳ Phase 3 (Knowledge Scope Integration): **PENDING**
- ⏳ Phase 4 (Dataverse Persistence): **FUTURE**

**Next Steps**:
1. Test agentic endpoint in Dataverse
2. Wire PCF to use `/agentic` endpoint for complex operations
3. Begin Phase 3 (scope catalog injection)

---

*Last updated: 2026-01-20*
