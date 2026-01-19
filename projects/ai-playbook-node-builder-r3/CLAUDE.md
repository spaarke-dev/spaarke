# AI Playbook Assistant Completion - Claude Context

> **Project**: ai-playbook-node-builder-r3
> **Type**: Completion Project
> **Status**: In Progress

---

## Project Summary

Complete the AI Assistant in Playbook Builder by extending existing services (`AiPlaybookBuilderService`, `ScopeResolverService`) for full scope CRUD, AI intent classification, and test execution.

**Critical Principle**: This is a COMPLETION project. EXTEND existing code, do NOT create duplicate services.

---

## Quick Reference

### Key Files to Extend

| File | What to Add |
|------|-------------|
| `Services/Ai/IScopeResolverService.cs` | CRUD interface methods |
| `Services/Ai/ScopeResolverService.cs` | Dataverse CRUD implementation |
| `Services/Ai/AiPlaybookBuilderService.cs` | AI intent classification |
| `Endpoints/AiPlaybookBuilderEndpoints.cs` | Test execution endpoint |
| `stores/aiAssistantStore.ts` | Model selection support |

### Key Files to Create

| File | Purpose |
|------|---------|
| `components/ScopeBrowser/` | Scope selection UI |
| `components/SaveAsDialog/` | Save As workflow |
| `components/TestModeSelector/` | Test mode options |

### Do NOT Create

- ❌ New builder service (extend `AiPlaybookBuilderService`)
- ❌ New scope service (extend `ScopeResolverService`)
- ❌ New DI registrations (extend existing)
- ❌ Managed Dataverse solutions (unmanaged only)

---

## Applicable ADRs

| ADR | Key Constraint |
|-----|----------------|
| **ADR-001** | Minimal API pattern for endpoints |
| **ADR-006** | PCF over webresources |
| **ADR-008** | Endpoint filters for authorization |
| **ADR-010** | ≤15 DI registrations, extend existing |
| **ADR-013** | AI Tool Framework, use `IOpenAiClient` |
| **ADR-014** | AI caching strategies |
| **ADR-021** | Fluent UI v9, dark mode required |
| **ADR-022** | React 16 APIs, unmanaged solutions |

---

## Scope Ownership Model

### Prefixes

| Prefix | Owner Type | Immutable | Can Edit |
|--------|------------|-----------|----------|
| `SYS-` | System | Yes | No |
| `CUST-` | Customer | No | Yes |

### Dataverse Fields

```
sprk_ownertype    - OptionSet: System=1, Customer=2
sprk_isimmutable  - Boolean
sprk_parentscope  - Lookup (self-reference for Extend)
sprk_basedon      - Lookup (self-reference for Save As)
```

---

## Builder Scopes (23 Total)

### Actions (ACT-BUILDER-*)
- 001: Intent Classification
- 002: Node Configuration
- 003: Scope Selection
- 004: Scope Creation
- 005: Build Plan Generation

### Skills (SKL-BUILDER-*)
- 001: Lease Analysis Pattern
- 002: Contract Review Pattern
- 003: Risk Assessment Pattern
- 004: Node Type Guide
- 005: Scope Matching

### Tools (TL-BUILDER-*)
- 001-009: Canvas operations (addNode, removeNode, createEdge, etc.)

### Knowledge (KNW-BUILDER-*)
- 001-004: Scope catalog, reference playbooks, node schema, best practices

---

## Test Execution Modes

| Mode | Storage | External Calls | Cleanup |
|------|---------|----------------|---------|
| **Mock** | None | No | N/A |
| **Quick** | `playbook-test-documents` | Yes | 24 hours |
| **Production** | Production | Yes | No |

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: When executing project tasks, Claude Code MUST invoke the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Why This Matters

The task-execute skill ensures:
- ✅ Knowledge files are loaded (ADRs, constraints, patterns)
- ✅ Context is properly tracked in current-task.md
- ✅ Proactive checkpointing occurs every 3 steps
- ✅ Quality gates run (code-review + adr-check) at Step 9.5
- ✅ Progress is recoverable after compaction

### Auto-Detection Rules

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Invoke task-execute with task X |
| "continue" | Check TASK-INDEX.md, invoke task-execute |
| "next task" | Check TASK-INDEX.md, invoke task-execute |

---

## Patterns to Follow

### Scope CRUD (Phase 1)

```csharp
// Pattern: Ownership validation
public async Task<AnalysisAction> UpdateActionAsync(Guid id, UpdateActionRequest request, CancellationToken ct)
{
    var existing = await GetActionAsync(id, ct);
    if (existing.IsImmutable)
        throw new InvalidOperationException("Cannot update system scope");

    // Proceed with update...
}
```

### AI Intent (Phase 2)

```csharp
// Pattern: Structured output
var schema = new { operation = "string", parameters = "object", confidence = "number" };
var result = await _openAiClient.GetStructuredOutputAsync<IntentResult>(message, schema, ct);

if (result.Confidence < 0.8)
    return new ClarificationNeeded(GenerateQuestions(result));
```

### PCF Components (Phase 5)

```typescript
// Pattern: Fluent UI v9 dialog
import { Dialog, DialogTrigger, DialogSurface, DialogTitle, DialogBody } from "@fluentui/react-components";

export const SaveAsDialog: React.FC<Props> = ({ scope, onSave }) => {
    // Follow dialog-patterns.md
};
```

---

## Context Recovery

If resuming work:
1. Read `current-task.md` for active task state
2. Check `TASK-INDEX.md` for overall progress
3. Continue from last completed step

---

## References

| Document | Purpose |
|----------|---------|
| [spec.md](spec.md) | Full specification |
| [plan.md](plan.md) | Implementation plan |
| [ai-chat-playbook-builder.md](../ai-playbook-node-builder-r2/ai-chat-playbook-builder.md) | Comprehensive design |

---

*Context file created: 2026-01-19*
