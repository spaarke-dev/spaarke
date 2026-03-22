# UI Dialog & Shell Standardization - AI Context

> **Purpose**: This file provides context for Claude Code when working on ui-dialog-shell-standardization.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Completed
- **Last Updated**: 2026-03-19
- **Current Task**: All tasks completed
- **Next Action**: Project complete. Archived as `x-ui-dialog-shell-standardization`.

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - Original design specification (permanent reference)
- [`design.md`](design.md) - Original design document from discussion
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Implementation plan and WBS
- [`current-task.md`](current-task.md) - **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker

### Project Metadata
- **Project Name**: ui-dialog-shell-standardization
- **Type**: Frontend Architecture / Component Extraction / Code Page Creation
- **Complexity**: High (multi-phase, 50+ files moved, 8+ new Code Pages)

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for design decisions, requirements, and acceptance criteria
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply ADRs** relevant to the technologies used (loaded automatically via adr-aware)

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via task-execute |
| "continue" | Execute next pending task (check TASK-INDEX.md for next 🔲) |
| "continue with task X" | Execute task X via task-execute |
| "next task" | Execute next pending task via task-execute |
| "keep going" | Execute next pending task via task-execute |
| "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

### Why This Matters

The task-execute skill ensures:
- Knowledge files are loaded (ADRs, constraints, patterns)
- Context is properly tracked in current-task.md
- Proactive checkpointing occurs every 3 steps
- Quality gates run (code-review + adr-check) at Step 9.5
- Progress is recoverable after compaction

### 🚨 MUST: Multi-File Work Decomposition

For tasks modifying 4+ files:
1. Decompose into dependency graph (group by module/component)
2. Delegate to subagents in parallel where safe (independent modules)
3. Serialize when files have tight coupling

---

## Key Technical Constraints

- **IDataService abstraction required** — All shared library services must accept `IDataService` interface, never call `Xrm.WebApi` directly (ADR-012)
- **Vite + vite-plugin-singlefile for all Code Pages** — Single HTML output, no webpack (ADR-026)
- **React 19 for Code Pages** — `createRoot` entry point, bundled (ADR-021)
- **React 16/17 for PCF** — Platform-provided, shared library peerDeps >=16.14.0 (ADR-022)
- **Fluent v9 tokens only** — No hard-coded colors, dark mode required (ADR-021)
- **Code Pages are default UI surface** — PCF only for form-bound controls (ADR-006)
- **`embedded={true}` on WizardShell** — When rendering inside Dataverse modal chrome
- **Clean webresource display names** — "Create New Matter" not "sprk_creatematterwizard"
- **navigateTo Promise for post-dialog refresh** — Corporate Workspace refetches after dialog close

---

## Architecture: Three-Layer Model

```
Layer 1: @spaarke/ui-components (shared library — SINGLE SOURCE OF TRUTH)
├── types/          → IDataService, IUploadService, INavigationService
├── utils/          → detectTheme(), parseDataParams()
├── components/
│   ├── Wizard/     → WizardShell (existing, no changes)
│   ├── CreateRecordWizard/ → Generic boilerplate (existing, no changes)
│   ├── PlaybookLibraryShell/ → NEW (from AnalysisBuilder)
│   ├── CreateMatterWizard/   → MOVED from LegalWorkspace
│   ├── CreateProjectWizard/  → MOVED
│   ├── CreateEventWizard/    → MOVED
│   ├── CreateTodoWizard/     → MOVED
│   ├── CreateWorkAssignmentWizard/ → MOVED
│   ├── DocumentUploadWizard/ → REFACTORED
│   ├── SummarizeFilesWizard/ → MOVED
│   └── FindSimilarDialog/    → MOVED

Layer 2: Code Page wrappers (~30-50 LOC each)
├── src/solutions/CreateMatterWizard/     → sprk_creatematterwizard
├── src/solutions/CreateProjectWizard/    → sprk_createprojectwizard
├── src/solutions/CreateEventWizard/      → sprk_createeventwizard
├── src/solutions/CreateTodoWizard/       → sprk_createtodowizard
├── src/solutions/CreateWorkAssignmentWizard/ → sprk_createworkassignmentwizard
├── src/solutions/SummarizeFilesWizard/   → sprk_summarizefileswizard
├── src/solutions/FindSimilarDialog/      → sprk_findsimilar
└── src/solutions/PlaybookLibrary/        → sprk_playbooklibrary

Layer 3: Consumers
├── Corporate Workspace (navigateTo calls)
├── Entity main forms (ribbon → navigateTo)
└── Power Pages SPA (direct import, own Dialog)
```

---

## Decisions Made

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-03-19 | No separate QuickStartShell | Avoid over-engineering; use PlaybookLibraryShell + WizardShell with intent pre-selection |
| 2026-03-19 | IDataService in Phase 1 | ADR-012 prerequisite; cleaner to establish pattern first |
| 2026-03-19 | PlaybookLibraryShell from AnalysisBuilder | More efficient than building from scratch; ~508 LOC to extract |
| 2026-03-19 | Get Started cards stay inline | Cards in workspace page open targets via navigateTo; no modal for browsing |

---

## Resources

### Applicable ADRs
- **ADR-006**: UI Surface Architecture — Code Pages default, PCF for form binding only
- **ADR-012**: Shared Component Library — IDataService abstraction, service portability tiers
- **ADR-021**: Fluent UI v9 Design System — React 19, Fluent v9 tokens, dark mode
- **ADR-022**: PCF Platform Libraries — React 16/17 for PCF, peerDeps >=16.14.0
- **ADR-026**: Code Page Build Standard — Vite + vite-plugin-singlefile, single HTML

### Patterns
- `.claude/patterns/webresource/full-page-custom-page.md` — Code Page template
- `.claude/patterns/webresource/custom-dialogs-in-dataverse.md` — Dialog patterns
- `.claude/patterns/pcf/dialog-patterns.md` — navigateTo pattern
- `.claude/patterns/pcf/theme-management.md` — Theme detection

### Reference Implementations
- `src/solutions/EventsPage/` — Canonical Vite Code Page
- `src/solutions/AnalysisBuilder/src/App.tsx` — PlaybookLibraryShell source (~508 LOC)
- `src/solutions/DocumentUploadWizard/` — Existing wizard Code Page (webpack, to migrate)

### Deploy Scripts
- `scripts/Deploy-EventsPage.ps1` — Web resource deployment pattern
- `scripts/Deploy-LegalWorkspaceCustomPage.ps1` — Corporate Workspace deploy

---

*This file should be kept updated throughout project lifecycle*
