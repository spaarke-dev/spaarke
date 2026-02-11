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
| Phase 3: Knowledge Scope Integration | ✅ Complete |
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

### Phase 3: Knowledge Scope Integration (COMPLETE ✅)

**Goal**: Give the LLM awareness of available scopes/tools

~~Current state: The LLM knows about node types from the prompt text, but doesn't have dynamic access to the scope catalog.~~

**Implemented**: LLM receives scope catalog injection in system prompt and has functional search_scopes and link_scope tools.

---

#### Phase 3a: Scope Catalog Injection (COMPLETE ✅)

**Inject scope catalog into system prompt for awareness.**

**Tasks**:
- [x] Load `KNW-BUILDER-001-scope-catalog` content at runtime
- [x] Format as context section in system prompt
- [x] Enable queries like "what skills are available?"
- [x] Enable queries like "what can extract entities from documents?"

**Implementation** (commit pending):
- Created `FallbackScopeCatalog.cs` with scope data from KNW-BUILDER-001
- Updated `BuilderAgentService.BuildSystemPromptAsync()` to merge Dataverse results with fallback
- When Dataverse returns empty, fallback catalog is used automatically
- Includes logging to indicate when fallback is being used

**Files Created**:
```
src/server/api/Sprk.Bff.Api/Services/Ai/FallbackScopeCatalog.cs
```

**Files Modified**:
```
src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs
```

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

#### Phase 3b: Scope Search Tool Implementation (COMPLETE ✅)

**Implement `search_scopes` tool for dynamic discovery.**

**Tasks**:
- [x] Implement `TL-BUILDER-007-searchScopes` as `search_scopes` tool
- [x] Support search by name, description, document types
- [x] Return matching scopes with relevance ranking
- [x] Enable LLM to discover scopes beyond injected catalog

**Implementation**:
- Updated `ExecuteSearchScopesAsync()` in `BuilderToolExecutor.cs`
- Searches Dataverse first, falls back to `FallbackScopeCatalog`
- Supports filtering by scope type (action, skill, knowledge, tool)
- Returns results with match scores for relevance ranking

---

#### Phase 3c: Dynamic Scope Linking (COMPLETE ✅)

**Enable LLM to wire specific scopes to nodes.**

**Tasks**:
- [x] Implement `TL-BUILDER-005-linkScope` as `link_scope` tool
- [x] Validate scope exists before linking
- [x] Update node config with scope reference
- [x] Stream update to PCF

**Implementation**:
- `ExecuteLinkScopeAsync()` in `BuilderToolExecutor.cs` (already implemented in Phase 2)
- Supports linking by scope ID or name
- Creates canvas patch operation for PCF to apply

**Key Files**:
- [KNW-BUILDER-001-scope-catalog.json](builder-scopes/KNW-BUILDER-001-scope-catalog.json) - Contains catalog of actions, skills, knowledge, tools

---

### Phase 4: Dataverse Persistence (COMPLETE ✅)

**Goal**: Store builder scopes in Dataverse for dynamic updates

Current state: Builder scopes are JSON design artifacts. FallbackPrompts.cs contains hardcoded versions.

Target state: Import JSON into Dataverse. GetBuilderScopePromptAsync loads from Dataverse with fallback.

**Tasks**:
- [x] Create Dataverse import utility (`BuilderScopeImporter.cs`)
- [x] Add admin endpoints for scope import (`BuilderScopeAdminEndpoints.cs`)
- [x] Copy 23 builder scope JSON files to API project (`builder-scopes/`)
- [x] Deploy via Kudu portal
- [x] Test import of 23 builder scope records via `/api/admin/builder-scopes/import`
- [x] Verify GetBuilderScopePromptAsync loads from Dataverse (tested via /classify-intent and /generate-plan)
- [ ] Implement cache invalidation endpoint (optional)
- [ ] Add admin UI for scope editing (future)

**Files Created**:
```
src/server/api/Sprk.Bff.Api/
├── Services/Ai/Builder/BuilderScopeImporter.cs  - Import utility (257 lines)
├── Api/Admin/BuilderScopeAdminEndpoints.cs      - Admin endpoints
├── builder-scopes/                               - 23 JSON files
└── Sprk.Bff.Api.csproj                          - Updated to include JSON files
```

**Admin Endpoints**:
- `GET /api/admin/builder-scopes/status` - Check file status (no auth)
- `POST /api/admin/builder-scopes/import` - Import all JSON files (requires auth)
- `POST /api/admin/builder-scopes/import-json` - Import single JSON (requires auth)

**Deployment Note**:
Azure CLI `az webapp deploy` may not update the running code reliably. If the status endpoint returns 404 after CLI deploy:
1. Go to Azure Portal → App Services → `spe-api-dev-67e2xz`
2. Click **Advanced Tools** → **Go** (opens Kudu)
3. Click **Tools** → **Zip Push Deploy**
4. Drag and drop `publish.zip` onto the page
5. Verify new entry appears in **Deployment Center** logs
6. Test: `curl https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/builder-scopes/status`

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

### 2026-01-20 Session - Phase 3a: Scope Catalog Injection

**Completed**:
1. Created `FallbackScopeCatalog.cs` with scope entries from KNW-BUILDER-001 design artifact
2. Implements fallback pattern: Dataverse results are preferred, fallback used when empty
3. Updated `BuilderAgentService.BuildSystemPromptAsync()` to merge with fallback
4. Added logging to track when fallback catalog is being used
5. Build verified: 0 warnings, 0 errors

**Scope Catalog Content** (from KNW-BUILDER-001):
- 5 Actions: Entity Extraction, Document Summary, Clause Analysis, Risk Detection, Financial Terms
- 4 Skills: Real Estate Domain, Contract Law Basics, Financial Analysis, Insurance Expertise
- 3 Knowledge: Standard Contract Terms, Company Policies, Regulatory Requirements
- 6 Tools: Entity Extractor, Clause Analyzer, Document Classifier, Summary, Risk Detector, Generic Analysis

**Architecture**:
```
User Query: "what skills are available?"
    │
    ▼
BuilderAgentService.ExecuteAsync()
    │
    ├─ BuildSystemPromptAsync()
    │   ├─ Load from Dataverse (may be empty)
    │   ├─ Merge with FallbackScopeCatalog
    │   └─ PlaybookBuilderSystemPrompt.Build() with catalog
    │
    ▼
LLM receives: "### Skills (Reusable Prompt Fragments)
  - **SYS-SKL-001** (Real Estate Domain): Domain expertise for real estate documents..."
```

**Completed** (Phase 3a):
- ✅ FallbackScopeCatalog.cs created
- ✅ BuilderAgentService.BuildSystemPromptAsync() updated

---

### 2026-01-20 Session (Continued) - Phase 3 Complete

**Completed Phase 3b and 3c**:
1. **Phase 3b: search_scopes tool execution**
   - Updated `ExecuteSearchScopesAsync()` in `BuilderToolExecutor.cs`
   - Searches Dataverse first, falls back to `FallbackScopeCatalog`
   - Supports query filtering, scope type filtering, and relevance scoring
   - Returns results with match scores for intelligent ranking

2. **Phase 3c: link_scope tool execution**
   - Already implemented in Phase 2 via `ExecuteLinkScopeAsync()`
   - Supports linking by scope ID or name
   - Creates canvas patch operations for PCF

**Files Modified**:
```
src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderToolExecutor.cs
```

**Build Status**: ✅ 0 errors, 0 warnings

**Phase 3 Summary**:
All Knowledge Scope Integration tasks complete:
- ✅ 3a: Scope catalog injection in system prompt
- ✅ 3b: search_scopes tool with fallback
- ✅ 3c: link_scope tool for dynamic scope linking

**Next Steps**:
1. Deploy API to Azure for testing
2. Test scope awareness queries ("what skills are available?")
3. Test search_scopes tool via agentic endpoint
4. Future: Phase 4 (Dataverse Persistence)

---

### 2026-01-20 Session - Phase 4 Implementation Started

**Completed**:
1. Created `BuilderScopeImporter.cs` (257 lines) - Utility to import builder scopes from JSON
   - `ImportFromDirectoryAsync()` - Import all JSON files from a directory
   - `ImportFromJsonAsync()` - Import a single scope from JSON string
   - Handles all 4 scope types: Action, Skill, Knowledge, Tool
   - Name prefix (SYS- vs CUST-) determines owner type in service

2. Created `BuilderScopeAdminEndpoints.cs` - Admin API endpoints
   - `GET /api/admin/builder-scopes/status` - File status check (no auth required)
   - `POST /api/admin/builder-scopes/import` - Import all JSON files
   - `POST /api/admin/builder-scopes/import-json` - Import single scope

3. Copied 23 builder scope JSON files to `src/server/api/Sprk.Bff.Api/builder-scopes/`
   - 5 Actions (ACT-BUILDER-001 through 005)
   - 5 Skills (SKL-BUILDER-001 through 005)
   - 9 Tools (TL-BUILDER-001 through 009)
   - 4 Knowledge (KNW-BUILDER-001 through 004)

4. Updated `Sprk.Bff.Api.csproj` to include JSON files in publish output

5. Registered `BuilderScopeImporter` in DI and mapped admin endpoints in `Program.cs`

**Build Status**: ✅ 0 errors, 0 warnings

**Deployment Issue**:
Azure CLI `az webapp deploy` and `az webapp deployment source config-zip` both succeed but the running app doesn't update. The `/api/admin/builder-scopes/status` endpoint returns 404.

**Phase 4 Completion**:
1. ✅ Deployed via Kudu portal (Azure CLI unreliable)
2. ✅ Status endpoint verified: 23 JSON files found
3. ✅ Fixed Knowledge import (JsonElement? for content property)
4. ✅ All 23 scopes imported to Dataverse:
   - 5 Actions, 5 Skills, 4 Knowledge, 9 Tools
5. ✅ Tested `/classify-intent` and `/generate-plan` endpoints - working

**Commits**:
- `4e7aa20` feat(ai-playbook): add Phase 4 Dataverse persistence infrastructure
- `31504c4` fix(ai-playbook): handle JSON object content in Knowledge scope imports

---

### Phase 4 Summary

All Dataverse Persistence tasks complete:
- ✅ Import utility with admin endpoints
- ✅ 23 builder scope JSON files deployed
- ✅ All scopes imported to Dataverse
- ✅ API endpoints use scopes from Dataverse

**Optional future enhancements**:
- Cache invalidation endpoint
- Admin UI for scope editing

---

*Last updated: 2026-01-20*
