# AI Node-Based Playbook Builder - Project Context

> **Project**: ai-node-playbook-builder
> **Created**: 2026-01-08
> **Status**: In Progress

---

## Project Summary

Transform Spaarke's single-action playbook model into a multi-node orchestration platform with visual builder, parallel execution, and flexible delivery outputs.

**Design Philosophy**: Extend, don't replace. Build orchestration layer on top of existing analysis pipeline.

---

## Applicable ADRs

**MUST follow these ADRs during implementation:**

| ADR | Key Constraint | Impact |
|-----|----------------|--------|
| ADR-001 | Minimal API pattern | All endpoints use Minimal API, no Azure Functions |
| ADR-008 | Endpoint filters | Use `PlaybookAuthorizationFilter`, no global middleware |
| ADR-009 | Redis-first caching | Cache resolved scopes and execution graphs |
| ADR-010 | DI minimalism | ≤15 non-framework registrations, use feature modules |
| ADR-013 | Extend BFF | All orchestration in `Sprk.Bff.Api`, no separate service |
| ADR-022 | React 16 for PCF | PCF host uses React 16; builder in iframe uses React 18 |

**Load ADRs from**: `.claude/adr/ADR-XXX-*.md`

---

## Key Technical Decisions

| Decision | Rationale |
|----------|-----------|
| One action per node | Atomic, clear purpose |
| Single tool per node | Avoids execution ambiguity |
| Multiple skills per node | Skills compose well as prompt modifiers |
| Multiple knowledge per node | Multiple context sources legitimate |
| Dataverse as SoR | POML for export/import only |
| Handlebars.NET templates | Logic-less, secure, minimal attack surface |
| Iframe for builder | Isolates React 18 from PCF React 16 |
| Playbook-level failure | Any node fails = stop and show error |

---

## Canonical Implementations

**Reference these files for patterns:**

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` | Orchestration pattern |
| `src/server/api/Sprk.Bff.Api/Api/Ai/PlaybookEndpoints.cs` | Playbook CRUD endpoints |
| `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` | SSE streaming endpoints |
| `src/server/api/Sprk.Bff.Api/Api/Filters/PlaybookAuthorizationFilter.cs` | Authorization filter |
| `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs` | Scope resolution |

---

## New Components to Create

### Backend (BFF API)

```
src/server/api/Sprk.Bff.Api/
├── Api/Ai/
│   ├── NodeEndpoints.cs              ← Node CRUD + execution
│   └── PlaybookRunEndpoints.cs       ← Run status + streaming
├── Services/Ai/
│   ├── INodeService.cs
│   ├── NodeService.cs
│   ├── IPlaybookOrchestrationService.cs
│   ├── PlaybookOrchestrationService.cs
│   ├── ITemplateEngine.cs
│   ├── TemplateEngine.cs             ← Handlebars.NET
│   ├── ExecutionGraph.cs
│   ├── PlaybookRunContext.cs
│   ├── NodeExecutionContext.cs
│   └── Nodes/
│       ├── INodeExecutor.cs
│       ├── INodeExecutorRegistry.cs
│       ├── NodeExecutorRegistry.cs
│       ├── AiAnalysisNodeExecutor.cs
│       ├── AiCompletionNodeExecutor.cs
│       ├── CreateTaskNodeExecutor.cs
│       ├── SendEmailNodeExecutor.cs
│       ├── UpdateRecordNodeExecutor.cs
│       ├── ConditionNodeExecutor.cs
│       ├── DeliverOutputNodeExecutor.cs
│       └── WaitNodeExecutor.cs
└── Models/Ai/
    ├── PlaybookNodeDto.cs
    ├── PlaybookRunDto.cs
    ├── NodeRunDto.cs
    └── NodeOutputDto.cs
```

### Frontend (Standalone Builder)

```
src/client/playbook-builder/
├── components/
│   ├── Canvas/
│   ├── Nodes/
│   ├── Edges/
│   ├── Palette/
│   ├── Properties/
│   ├── Toolbar/
│   └── Execution/
├── hooks/
├── services/
├── stores/
└── types/
```

### Frontend (PCF Host)

```
src/client/pcf/PlaybookBuilderHost/
├── index.ts
├── PlaybookBuilderHost.tsx
├── ControlManifest.Input.xml
└── css/
```

---

## Testing Requirements

- **Unit tests**: All services (`NodeService`, `PlaybookOrchestrationService`, executors)
- **Integration tests**: API endpoints with test Dataverse
- **E2E tests**: Builder-host communication, full execution flow
- **Deploy to dev**: After each phase

---

## Owner Clarifications

| Topic | Answer |
|-------|--------|
| Out of scope | No mobile-native, no third-party marketplace |
| Testing | Full testing including dev deployment |
| Versioning | Stub only - no history/rollback this release |
| Failure handling | Playbook-level - any node fails = stop + error |
| Builder deployment | Same App Service as BFF API (wwwroot) |

---

## Task Execution Protocol

**MANDATORY**: When working on tasks for this project, invoke the `task-execute` skill.

**Trigger phrases**:
- "work on task X"
- "continue" / "next task"
- "resume task X"

**What task-execute does**:
1. Loads task POML file
2. Loads knowledge files based on tags
3. Loads applicable ADRs
4. Executes with checkpointing
5. Runs quality gates (code-review + adr-check)

**DO NOT** read POML files directly and implement manually - this bypasses context loading.

---

## Current Progress

Track in [current-task.md](current-task.md) and [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md).

---

*Generated by Claude Code project-pipeline*
