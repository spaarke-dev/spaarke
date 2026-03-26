# CLAUDE.md — Analysis Workspace + SprkChat Integration

> **Project**: ai-analysis-workspace-sprkchat-integration
> **Branch**: `work/ai-analysis-workspace-sprkchat-integration-r1`
> **Created**: 2026-03-26

---

## Project Context

Merge AnalysisWorkspace and SprkChatPane into a single unified Code Page. SprkChat becomes an embedded right panel within the Analysis Workspace via shared React context + callback props, replacing BroadcastChannel cross-pane communication. This is a prerequisite for the SprkChat extensibility project.

## Applicable ADRs

| ADR | Constraint | Applies To |
|-----|-----------|-----------|
| ADR-006 | Code Page for standalone dialogs; default UI surface | AnalysisWorkspace is a Code Page |
| ADR-012 | Shared component library — callback-based props, `@spaarke/ui-components` | SprkChat components stay in shared library |
| ADR-021 | Fluent UI v9 exclusively; `makeStyles`; design tokens; dark mode required | All UI in this project |
| ADR-022 | React 19 for Code Pages (bundled, not platform-provided) | AnalysisWorkspace uses React 19 |
| ADR-026 | Vite + `vite-plugin-singlefile`; CSS reset; Xrm frame-walk | Code Page build standard |

## Key Constraints (MUST Rules)

- MUST keep SprkChat components in `@spaarke/ui-components` (ADR-012)
- MUST use callback-based props on SprkChat — no direct service dependencies (ADR-012)
- MUST use Fluent UI v9 exclusively; `makeStyles` for styling (ADR-021)
- MUST use React 19 `createRoot` entry point (ADR-022, ADR-026)
- MUST wrap all UI in `FluentProvider` with theme (ADR-021)
- MUST use Fluent design tokens for colors/spacing — no hard-coded hex/rgb (ADR-021)
- MUST support dark mode and high-contrast (ADR-021)
- MUST use Vite + `vite-plugin-singlefile` for build (ADR-026)
- MUST use Xrm frame-walk pattern for Dataverse API access (ADR-026)
- MUST NOT move SprkChat components into AnalysisWorkspace Code Page
- MUST NOT add AnalysisWorkspace-specific dependencies to shared library
- MUST NOT use BroadcastChannel for editor ↔ chat communication in unified page
- MUST NOT use Fluent v8 or hard-coded colors

## Canonical Code References

| Pattern | File | Key Detail |
|---------|------|-----------|
| Current App layout | `src/client/code-pages/AnalysisWorkspace/src/App.tsx` | 2-panel → restructure to 3-panel |
| PanelSplitter | `src/client/code-pages/AnalysisWorkspace/src/components/PanelSplitter.tsx` | Extend for three-panel |
| usePanelResize | `src/client/code-pages/AnalysisWorkspace/src/hooks/usePanelResize.ts` | Replace with usePanelLayout |
| EditorPanel | `src/client/code-pages/AnalysisWorkspace/src/components/EditorPanel.tsx` | Remove BroadcastChannel; expose ref |
| DocumentStreamBridge | `src/client/code-pages/AnalysisWorkspace/src/components/DocumentStreamBridge.tsx` | Remove entirely |
| useSelectionBroadcast | `src/client/code-pages/AnalysisWorkspace/src/hooks/useSelectionBroadcast.ts` | Remove entirely |
| MSAL config | `src/client/code-pages/AnalysisWorkspace/src/config/msalConfig.ts` | Single auth point |
| useAnalysisLoader | `src/client/code-pages/AnalysisWorkspace/src/hooks/useAnalysisLoader.ts` | Unified context |
| SprkChatPane App | `src/client/code-pages/SprkChatPane/src/App.tsx` | Remove entirely |
| contextService | `src/client/code-pages/SprkChatPane/src/services/contextService.ts` | Remove entirely |
| SprkChat types | `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/types.ts` | SprkChatProps interface |
| SprkChatBridge | `src/client/shared/Spaarke.UI.Components/src/services/SprkChatBridge.ts` | Deprecate (not delete) |
| InlineAiToolbar | `src/client/shared/Spaarke.UI.Components/src/components/InlineAiToolbar/` | Rewire from bridge to context |

## Resource Files

### Constraints
- `.claude/adr/ADR-006-pcf-over-webresources.md`
- `.claude/adr/ADR-012-shared-components.md`
- `.claude/adr/ADR-021-fluent-design-system.md`
- `.claude/adr/ADR-022-pcf-platform-libraries.md`
- `.claude/adr/ADR-026-full-page-custom-page-standard.md`

### Skills
- `.claude/skills/code-page-deploy/SKILL.md`
- `.claude/skills/code-review/SKILL.md`
- `.claude/skills/adr-check/SKILL.md`

## Parallel Execution Notes

This project is structured for concurrent task agent execution:
- **Phase 1**: Groups A, B, C run in parallel (new components + editor hooks + toolbar)
- **Phase 2**: Groups D, E, F, G run in parallel (cleanup across different directories)
- **Phase 3**: Group H runs in parallel (auth + context consolidation)
- **Phase 4**: Groups I, J, K run in parallel (layout, UX, visual polish)
- **Phase 5**: Group L runs in parallel (deployment + test cleanup)
- See `plan.md` Parallel Execution Groups for task groupings

---

## Project Status

- **Phase**: Planning
- **Last Updated**: 2026-03-26
- **Current Task**: Not started
- **Next Action**: Execute task 001

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - Original design specification (permanent reference)
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Implementation plan and WBS
- [`current-task.md`](current-task.md) - Active task state (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker

### Project Metadata
- **Project Name**: ai-analysis-workspace-sprkchat-integration
- **Type**: Code Page / Frontend Refactoring
- **Complexity**: Medium-High

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

### Parallel Task Execution
When tasks can run in parallel (see plan.md groups), each task MUST still use task-execute:
- Single message with multiple Skill tool invocations
- Each invocation calls task-execute with a different task file
- Monitor via TaskOutput tool

### 🚨 MUST: Multi-File Work Decomposition

For tasks modifying 4+ files, Claude Code MUST:
1. Decompose into dependency graph (group by module/component)
2. Delegate to subagents in parallel where safe
3. Parallelize when files are in different modules
4. Serialize when files have tight coupling

---

## Decisions Made

*No decisions recorded yet*

## Implementation Notes

*No notes yet*

---

*Generated by Claude Code project-pipeline*
