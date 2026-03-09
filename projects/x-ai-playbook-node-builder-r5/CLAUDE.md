# CLAUDE.md — AI Playbook Node Builder R5

> **Project**: ai-playbook-node-builder-r5
> **Branch**: `work/ai-playbook-node-builder-r5`
> **Status**: In Progress
> **Created**: 2026-02-28

---

## Project Purpose

Rebuild the Playbook Builder from PCF (React 16, react-flow-renderer v10) to a React 19 Code Page using @xyflow/react v12+. Close the canvas-to-execution gap by building typed configuration forms for all 7 node types. Replace all mock data with real Dataverse queries.

## Execution Model

**Autonomous Claude Code** — Parallel task agents run simultaneously without human approval gates.

- Tasks in Phases 2, 3, 4, 5 run in parallel after Phase 1
- Each agent owns specific files (see plan.md file ownership table)
- Quality gates automated: code-review + adr-check at Step 9.5

## Key Architecture Decisions

| Decision | Rationale |
|----------|-----------|
| Code Page (not PCF) | Standalone workspace needs React 19 for @xyflow v12 |
| Direct Dataverse REST API | Code Page owns build-time CRUD; BFF only reads at execution time |
| @xyflow/react v12 | Typed generics, hooks API; DocumentRelationshipViewer proves compatibility |
| Zustand preserved | Framework-agnostic stores; only data sources change |

## Applicable ADRs

| ADR | Constraint |
|-----|-----------|
| ADR-006 | Code Page for standalone dialogs; `src/client/code-pages/PlaybookBuilder/` |
| ADR-021 | Fluent v9 exclusively; dark mode mandatory; `makeStyles` for styling |
| ADR-022 | Code Pages bundle React 19 (exempt from PCF React 16 constraint) |
| ADR-013 | AI calls through BFF only; no API keys in browser |
| ADR-012 | Import from `@spaarke/ui-components` before building custom |
| ADR-023 | Use ChoiceDialog for 2-4 option dialogs |
| ADR-001 | BFF endpoints follow Minimal API pattern (if any new endpoints) |
| ADR-010 | ≤15 non-framework DI registrations (BFF side) |

## Constraints

**MUST:**
- Place project in `src/client/code-pages/PlaybookBuilder/`
- Use React 19 `createRoot()` entry point
- Bundle React 19 + Fluent v9
- Use `@fluentui/react-components` exclusively
- Use `makeStyles` (Griffel) for custom styling
- Use Fluent design tokens for all colors
- Support light, dark, and high-contrast modes
- Build with Webpack 5 + `build-webresource.ps1`
- Deploy as single HTML: `out/sprk_playbookbuilder.html`
- Use `fetch()` + Bearer token for Dataverse calls

**MUST NOT:**
- Call Azure AI directly from browser
- Expose API keys in client code
- Use Fluent v8 or alternative UI libraries
- Hard-code colors in node/edge renderers
- Use mock data in any store (zero tolerance)

## Reference Implementations

| Implementation | Purpose | Location |
|---------------|---------|----------|
| AnalysisWorkspace | Auth, build pipeline, theme detection | `src/client/code-pages/AnalysisWorkspace/` |
| DocumentRelationshipViewer | @xyflow/react v12 patterns | `src/client/code-pages/DocumentRelationshipViewer/` |
| R4 PlaybookBuilderHost | Migration source (PCF) | `src/client/pcf/PlaybookBuilderHost/` |

## Key Patterns

| Pattern | File |
|---------|------|
| Code Page scaffold | `.claude/patterns/webresource/full-page-custom-page.md` |
| Streaming endpoints | `.claude/patterns/ai/streaming-endpoints.md` |
| Analysis scopes | `.claude/patterns/ai/analysis-scopes.md` |
| MSAL client auth | `.claude/patterns/auth/msal-client.md` |
| Dataverse Web API | `.claude/patterns/dataverse/web-api-client.md` |

## Constraint Files

| Domain | File |
|--------|------|
| Web Resources | `.claude/constraints/webresource.md` |
| AI | `.claude/constraints/ai.md` |
| API | `.claude/constraints/api.md` |

## Project Files

| File | Purpose |
|------|---------|
| [spec.md](spec.md) | AI implementation specification |
| [design.md](design.md) | Comprehensive technical design |
| [plan.md](plan.md) | Implementation plan with 7 phases |
| [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) | Task status overview |
| [current-task.md](current-task.md) | Active task state (context recovery) |

## Separation of Concerns

```
Playbook Builder (Code Page)  →  Owns Dataverse CRUD at build time
  - Creates/updates sprk_playbooknode records
  - Reads scope tables (skills, knowledge, tools, actions, models)
  - Writes sprk_configjson with typed node config
  - Saves canvas JSON to sprk_analysisplaybook

BFF API                        →  Only reads at execution time
  - Reads sprk_playbooknode records
  - Resolves scopes via N:N tables
  - Executes nodes via PlaybookOrchestrationService
  - NO changes to BFF API required
```

## 🚨 MANDATORY: Task Execution Protocol

**When executing project tasks, ALWAYS invoke the `task-execute` skill.** Do NOT read POML files directly and implement manually.

The task-execute skill ensures:
- Knowledge files loaded (ADRs, constraints, patterns)
- Context tracked in current-task.md
- Proactive checkpointing every 3 steps
- Quality gates run at Step 9.5
- Progress recoverable after compaction

**Trigger phrases**: "work on task X", "continue", "next task", "keep going"

---

*For Claude Code: Load this file at session start for project context.*
