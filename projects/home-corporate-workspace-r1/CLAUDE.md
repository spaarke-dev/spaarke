# Legal Operations Workspace (Home Corporate) R1 — AI Context

> **Purpose**: This file provides context for Claude Code when working on home-corporate-workspace-r1.
> **Always load this file first** when working on any task in this project.

---

## Project Status

| Field | Value |
|-------|-------|
| **Status** | In Progress |
| **Last Updated** | 2026-02-18 |
| **Current Task** | Deployment and Testing |
| **Next Action** | Deploy solution to Dataverse, verify in browser |

---

## Quick Reference

### Key Files

| File | Purpose |
|------|---------|
| [spec.md](spec.md) | AI implementation specification (source of truth) |
| [design.md](design.md) | Original UI/UX design document (v2.0) |
| [plan.md](plan.md) | Implementation plan with WBS |
| [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) | Task status overview |
| [current-task.md](current-task.md) | Active task state (for context recovery) |
| [screenshots/](screenshots/) | 15 prototype mockup screenshots |

### Project Metadata

| Field | Value |
|-------|-------|
| **Branch** | `work/home-corporate-workspace-r1` |
| **Project Path** | `projects/home-corporate-workspace-r1/` |
| **PCF Path** | `src/client/pcf/LegalWorkspace/` |
| **API Path** | `src/server/api/Sprk.Bff.Api/` |
| **Solutions Path** | `src/solutions/` |
| **Type** | PCF (Custom Page) + BFF API |
| **Complexity** | High |

---

## Context Loading Rules

| When Working On | Load These First |
|----------------|-----------------|
| **Any task** | This file + `current-task.md` + task POML file |
| **Page shell / layout** | `.claude/constraints/pcf.md` + `.claude/patterns/pcf/control-initialization.md` |
| **Theme / dark mode** | `.claude/patterns/pcf/theme-management.md` + ADR-021 |
| **BFF endpoints** | `.claude/constraints/api.md` + `.claude/patterns/api/endpoint-definition.md` |
| **Scoring engine** | `.claude/constraints/api.md` + `.claude/patterns/api/service-registration.md` |
| **AI integration** | `.claude/constraints/ai.md` + `docs/guides/SPAARKE-AI-ARCHITECTURE.md` |
| **Caching** | `.claude/patterns/caching/distributed-cache.md` + ADR-009 |
| **File uploads** | ADR-007 + SpeFileStore patterns |
| **Dataverse queries** | `.claude/patterns/dataverse/entity-operations.md` |

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: When executing tasks, Claude Code MUST invoke the `task-execute` skill. DO NOT read POML files directly and implement manually.

**Trigger phrases** → Invoke `task-execute`:
- "work on task X", "continue", "next task", "keep going", "resume task X"

**Why**: task-execute ensures knowledge files are loaded, checkpointing occurs, quality gates run, and progress is recoverable.

### Parallel Task Execution

When tasks can run in parallel (no dependencies), each task MUST still use task-execute:
- Send one message with multiple Skill tool invocations
- Each invocation calls task-execute with a different task file
- Example: Tasks 020, 021, 022 in parallel → Three separate task-execute calls in one message

### Multi-File Work Decomposition

For tasks modifying 4+ files:
1. Decompose into dependency graph (group by module/component)
2. Delegate to subagents in parallel where safe (one per module)
3. Parallelize when files are in different modules with no shared interfaces
4. Serialize when files have tight coupling or sequential dependencies

---

## Key Technical Constraints

### Custom Page (React 18)

- **ADR Exception**: This is a Power Apps Custom Page, NOT a standard PCF control
- CAN use React 18 APIs (`createRoot`, concurrent features, hooks)
- Runs in its own iframe — does NOT share MDA React runtime
- Still MUST use Fluent UI v9 and follow all styling constraints

### Fluent UI v9 (ADR-021)

- MUST use `@fluentui/react-components` exclusively (no v8, no third-party)
- MUST use `makeStyles` (Griffel) for all custom styling with Fluent `tokens`
- MUST use semantic color tokens — NEVER hardcode hex/rgb/hsl
- MUST support light, dark, and high-contrast modes
- MUST wrap all UI in `FluentProvider` with theme
- MUST use `@fluentui/react-icons` for all icons

### Data Access: Hybrid Pattern

| Query Type | Source | Why |
|-----------|--------|-----|
| Matter/Project/Document list | `Xrm.WebApi` (client) | Simple entity query |
| Event feed, To-do CRUD | `Xrm.WebApi` (client) | Direct entity updates |
| Portfolio Health aggregation | **BFF endpoint** | Complex multi-entity aggregation |
| Quick Summary metrics | **BFF endpoint** | Multi-entity aggregation |
| AI Summary/Pre-fill | **BFF endpoint** | AI Playbook invocation (server-side) |
| Priority/Effort scoring | **BFF endpoint** | Server-side with multi-entity context |

### BFF API (ADR-001, ADR-008, ADR-010)

- Minimal API pattern for all new endpoints
- Endpoint filters for authorization (NOT global middleware)
- ProblemDetails for all API errors
- DI registrations ≤15 non-framework lines
- IDistributedCache (Redis) for aggregation caching
- Rate limiting on AI endpoints

### Action Cards Architecture

- Only "Create New Matter" is a custom dialog (Block 6)
- Other 6 cards launch existing AI Playbook Analysis Builder (`AiToolAgent` PCF)
- Each card passes pre-configured context/intent to Analysis Builder
- NO new standalone dialog UX for these 6 cards

### Scoring System

- Priority: deterministic rule-based, 0-100, with factor tables
- Effort: base by event type + complexity multipliers, capped at 100
- Both produce transparent reason strings
- Server-side calculation via BFF endpoint

---

## Canonical Implementations to Follow

| What | File | Notes |
|------|------|-------|
| Workspace PCF | `src/client/pcf/AnalysisWorkspace/` | Multi-panel layout, theme handling, MSAL auth |
| Clean React PCF | `src/client/pcf/DueDatesWidget/` | Cleanest React 16 template structure |
| Aggregation endpoint | `Api/Finance/FinanceEndpoints.cs` | BFF aggregation pattern |
| Scoring endpoint | `Api/Scorecard/ScorecardCalculatorEndpoints.cs` | Scoring calculation pattern |
| AI streaming | `Api/Ai/AnalysisEndpoints.cs` | AI Playbook streaming |
| DI module | `Infrastructure/DI/FinanceModule.cs` | Module extension method pattern |
| Auth filter | `Api/Filters/DocumentAuthorizationFilter.cs` | Endpoint authorization |
| Redis cache | `Services/Ai/EmbeddingCache.cs` | IDistributedCache pattern |
| ProblemDetails | `Api/Infrastructure/ProblemDetailsHelper.cs` | Error response pattern |

---

## Parallel Execution Strategy (Agent Teams)

### Parallel Groups

| Group | Tasks | File Ownership | Prerequisite |
|-------|-------|----------------|-------------|
| **pg-foundation** | Shell, shared types, theme | `LegalWorkspace/` (shell only) | None — must complete first |
| **pg-phase1-ui** | Block 2, Block 5, Block 7 | Each block's component directory | pg-foundation |
| **pg-phase1-bff** | Portfolio + health endpoints | `src/server/api/` (separate files) | pg-foundation interfaces |
| **pg-phase2-feed** | Block 3 (Updates Feed) | `components/ActivityFeed/` | pg-phase1-ui |
| **pg-phase2-todo** | Block 4 (Smart To Do) | `components/SmartToDo/` | Block 3D flag interface |
| **pg-phase3-actions** | Block 1 + action cards | `components/GetStarted/` | pg-phase2 |
| **pg-phase3-dialog** | Block 6 (Create Matter) | `components/CreateMatter/` | pg-foundation |
| **pg-scoring** | Priority + effort engines | `src/server/api/` (scoring services) | pg-foundation interfaces |
| **pg-ai-integration** | AI Summary, briefing, pre-fill | AI service layer | Interfaces defined |

### Recommended Team Configuration

```
Team Lead: Orchestrator (Opus, high effort)
├── Teammate 1: PCF UI components (Sonnet, medium effort)
├── Teammate 2: BFF API endpoints (Sonnet, medium effort)
├── Teammate 3: Scoring + AI integration (Sonnet, medium effort)
└── (Optional) Teammate 4: Tests + documentation (Sonnet, low effort)
```

---

## Visual References

15 mockup screenshots at `screenshots/`. Key references:

| Screenshot | Shows |
|-----------|-------|
| `workspace-main-page.jpg` | Full page layout — primary composition reference |
| `to-do-list_1.jpg` | Smart To Do with scoring badges |
| `to-do-list-ai-summary_2.jpg` | Priority×Effort scoring grid |
| `updates-list-filtered-by-alert_1.jpg` | Feed with filter pills |
| `create-new-matter-dialog-wizard.jpg` through `_5.jpg` | 5-step wizard flow |
| `my-portfolio-matter-list_1.jpg` | Matter items with grade pills |

See spec.md Visual References table for complete mapping.

---

## Dataverse Entities (Existing Only)

| Entity | Key Fields |
|--------|------------|
| `sprk_event` | todoflag, todostatus, todosource, priority, priorityscore, effort, effortscore, estimatedminutes, priorityreason, effortreason |
| `sprk_matter` | totalbudget, totalspend, utilizationpercent, budgetcontrols_grade, guidelinescompliance_grade, outcomessuccess_grade, overdueeventcount |
| `sprk_project` | name, type, practicearea, owner, status, budgetused |
| `sprk_document` | name, type, description, matter lookup, modifiedon |
| `sprk_organization` | name |
| `sprk_contact` | name |

---

## Decisions Made

| Decision | Choice | Date | Rationale |
|----------|--------|------|-----------|
| Hosting model | Power Apps Custom Page (React 18) | 2026-02-17 | ADR exception — full React app needed |
| Data access | Hybrid (Xrm.WebApi + BFF) | 2026-02-17 | Simple queries client, aggregations server |
| Action cards | Reuse Analysis Builder for 6 of 7 | 2026-02-17 | Major scope reduction, existing infrastructure |
| Prototype code | None — implement from design + screenshots | 2026-02-17 | Fresh implementation |

---

## Applicable ADRs

ADR-001, ADR-006, ADR-007, ADR-008, ADR-009, ADR-010, ADR-012, ADR-013, ADR-021, ADR-022

### Pattern Files

- `.claude/patterns/api/endpoint-definition.md`
- `.claude/patterns/api/error-handling.md`
- `.claude/patterns/api/service-registration.md`
- `.claude/patterns/pcf/control-initialization.md`
- `.claude/patterns/pcf/theme-management.md`
- `.claude/patterns/caching/distributed-cache.md`
- `.claude/patterns/dataverse/entity-operations.md`

### Constraint Files

- `.claude/constraints/api.md`
- `.claude/constraints/pcf.md`
- `.claude/constraints/ai.md`
- `.claude/constraints/data.md`

---

*For Claude Code: Load this file first when working on any task in this project.*
