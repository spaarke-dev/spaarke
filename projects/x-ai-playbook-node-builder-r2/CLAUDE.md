# AI Chat Playbook Builder - Claude Context

> **Project**: AI Chat Playbook Builder
> **Created**: 2026-01-16
> **Last Updated**: 2026-01-16

---

## Project Summary

Adding conversational AI assistance to PlaybookBuilderHost PCF control. Users build playbooks via natural language with real-time canvas updates.

---

## Key Decisions

| Decision | Rationale |
|----------|-----------|
| Custom modal, NOT M365 Copilot | Direct canvas state access, immediate updates, full UX control |
| Session-only conversation | No backend storage needed, Zustand client state |
| Auto-prefix CUST- for customer scopes | Clear ownership, suffix (1) for duplicates |
| Tiered AI models | Cost optimization: mini for classification, o1 for planning |
| Three test modes | Mock (fast), Quick (real doc), Production (full flow) |

---

## Applicable ADRs

| ADR | Key Constraint |
|-----|----------------|
| **ADR-001** | Minimal API patterns for endpoints |
| **ADR-006** | PCF for all new UI |
| **ADR-008** | Endpoint filters for authorization |
| **ADR-013** | Extend BFF, no separate AI microservice |
| **ADR-016** | Rate limiting on AI endpoints (~200/hr) |
| **ADR-021** | Fluent UI v9, tokens for colors, dark mode required |
| **ADR-022** | React 16 APIs only (`ReactDOM.render`) |

---

## File Locations

### PCF Components (to create)
```
src/client/pcf/PlaybookBuilderHost/control/
├── components/
│   └── AiAssistant/
│       ├── AiAssistantModal.tsx
│       ├── ChatHistory.tsx
│       ├── ChatInput.tsx
│       └── OperationFeedback.tsx
├── services/
│   └── AiPlaybookService.ts
└── stores/
    └── aiAssistantStore.ts
```

### BFF API (to create)
```
src/server/api/Sprk.Bff.Api/
├── Api/Ai/
│   └── AiPlaybookBuilderEndpoints.cs
└── Services/Ai/
    └── AiPlaybookBuilderService.cs
```

### Reference Existing Code
- `src/client/pcf/PlaybookBuilderHost/control/stores/canvasStore.ts` - Canvas state
- `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` - SSE pattern

---

## Technical Notes

### SSE Streaming Events
```
event: thinking
event: dataverse_operation
event: canvas_patch
event: message
event: done
```

### Canvas Patch Schema
```typescript
interface CanvasPatch {
  addNodes?: PlaybookNode[];
  removeNodeIds?: string[];
  updateNodes?: Partial<PlaybookNode>[];
  addEdges?: PlaybookEdge[];
  removeEdgeIds?: string[];
}
```

### Intent Categories (11)
1. CREATE_PLAYBOOK
2. ADD_NODE
3. REMOVE_NODE
4. CONNECT_NODES
5. CONFIGURE_NODE
6. LINK_SCOPE
7. CREATE_SCOPE
8. QUERY_STATUS
9. MODIFY_LAYOUT
10. UNDO
11. UNCLEAR

### Confidence Thresholds
- Intent: <75% → clarify
- Entity: <80% → show options
- Scope: <70% → ask user

---

## Builder-Specific Scopes

| Type | IDs | Purpose |
|------|-----|---------|
| Actions | ACT-BUILDER-001 to 005 | Intent, config, selection, creation, planning |
| Skills | SKL-BUILDER-001 to 005 | Lease, contract, risk patterns, node guide, matching |
| Tools | TL-BUILDER-001 to 009 | addNode, removeNode, createEdge, etc. |
| Knowledge | KNW-BUILDER-001 to 004 | Scope catalog, playbooks, schema, best practices |

---

## Test Modes

| Mode | Playbook Saved? | Document | Creates Records? |
|------|-----------------|----------|------------------|
| Mock | No | Sample data | No |
| Quick | No | Temp blob (24hr) | No |
| Production | Yes | SPE file | Yes |

---

## Current Phase

**Phase 1: Infrastructure** - BFF endpoint, Dataverse ops, conversational mode

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: When working on project tasks, Claude MUST invoke the `task-execute` skill.

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Invoke task-execute with task X |
| "continue" | Check TASK-INDEX.md, invoke task-execute |
| "next task" | Check TASK-INDEX.md, invoke task-execute |

**DO NOT** read POML files directly and implement manually - this bypasses knowledge loading, checkpointing, and quality gates.

---

## Context Recovery

If resuming after compaction or new session:
1. Read `current-task.md` for active task state
2. Read `tasks/TASK-INDEX.md` for overall progress
3. Say "continue" to resume via task-execute

---

*Project-specific context for Claude Code*
