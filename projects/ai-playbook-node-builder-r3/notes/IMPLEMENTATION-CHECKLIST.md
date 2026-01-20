# AI Playbook Builder - Implementation Checklist

> **Created**: 2026-01-20
> **Project**: ai-playbook-node-builder-r3
> **Goal**: "Claude Code for Playbooks" - conversational AI-assisted playbook building

---

## Current State Summary

The AI Playbook Builder has three main components:

| Component | Location | Status |
|-----------|----------|--------|
| **PCF Control** | `src/client/pcf/PlaybookBuilderHost/` | v2.25.0 deployed |
| **Backend Service** | `src/server/api/Sprk.Bff.Api/Services/Ai/` | Updated, needs deploy |
| **Builder Scopes** | `projects/.../notes/builder-scopes/` | Design artifacts (23 JSON files) |

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
- [ ] **Deploy API to Azure** for end-to-end testing

**Files Modified**:
- `FallbackPrompts.cs` - IntentClassification prompt
- `PlaybookBuilderSystemPrompt.cs` - IntentClassification prompt
- `AiPlaybookBuilderEndpoints.cs` - thinking event messages
- `BuilderLayout.tsx` - leftPanelOpen default
- `PlaybookBuilderHost.tsx` - fixed header styling

---

### Phase 2: Tool Schema Integration (PENDING)

**Goal**: Give the LLM structured tools instead of just natural language

Current state: The LLM receives a prompt describing operations in text. It returns JSON with intent classification. The backend then maps this to canvas operations.

Target state: The LLM receives actual tool definitions (OpenAI function calling format). It can call tools directly, which get validated against schemas.

**Tasks**:
- [ ] Create `BuilderToolDefinitions.cs` with OpenAI function schemas
- [ ] Map TL-BUILDER-* JSON files to OpenAI tool format
- [ ] Update `ClassifyIntentWithAiAsync` to include tools in request
- [ ] Add tool call response parsing (parallel to JSON intent parsing)
- [ ] Map tool calls → canvas patch operations
- [ ] Update PCF to handle tool-based responses

**Key Files to Create/Modify**:
```
src/server/api/Sprk.Bff.Api/Services/Ai/
├── BuilderToolDefinitions.cs (NEW) - Tool schemas for OpenAI
├── AiPlaybookBuilderService.cs - Add tool call handling
└── Models/BuilderToolCall.cs (NEW) - Tool call response models
```

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

Target state: Inject KNW-BUILDER-001 (scope catalog) into context so LLM can recommend specific scopes.

**Tasks**:
- [ ] Load `KNW-BUILDER-001-scope-catalog` content at runtime
- [ ] Inject as context section in system prompt
- [ ] Enable "what skills are available?" queries
- [ ] Add scope search functionality (TL-BUILDER-007)

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

### 2026-01-20 Session

**Completed**:
1. Updated backend prompts to conversational "Claude Code" style
2. Changed thinking events from "Classifying intent" to "Thinking"
3. PCF v2.25.0 deployed with left palette open + fixed header
4. Created PR #142 with all changes
5. Documented architecture and created this checklist

**Next Session**:
1. Deploy API to Azure
2. End-to-end test conversational experience
3. Begin Phase 2 (tool schema integration) if Phase 1 validated

---

*Last updated: 2026-01-20*
