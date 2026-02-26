# CLAUDE.md — SprkChat Interactive Collaboration (R2)

> **Project**: ai-spaarke-platform-enhancents-r2
> **Last Updated**: 2026-02-26
> **Status**: Complete

---

## Project Status

| Field | Value |
|-------|-------|
| **Phase** | Complete |
| **Active Task** | none |
| **Branch** | `work/ai-spaarke-platform-enhancents-r2` |
| **Progress** | 89/89 tasks complete |
| **Completed** | 2026-02-26 |

---

## Quick Reference

| Resource | Path |
|----------|------|
| Spec | `projects/ai-spaarke-platform-enhancents-r2/spec.md` |
| Design | `projects/ai-spaarke-platform-enhancents-r2/design.md` |
| Plan | `projects/ai-spaarke-platform-enhancents-r2/plan.md` |
| Tasks | `projects/ai-spaarke-platform-enhancents-r2/tasks/` |
| Current Task State | `projects/ai-spaarke-platform-enhancents-r2/current-task.md` |

---

## Context Loading Rules

When working on this project, load in this order:

1. **Always load first**: This file (`CLAUDE.md`)
2. **Task context**: `current-task.md` → active task `.poml` file
3. **Constraints**: `.claude/constraints/api.md`, `.claude/constraints/ai.md`, `.claude/constraints/pcf.md`
4. **ADRs**: Based on task tags (see Applicable ADRs below)
5. **Patterns**: Based on task type (see Patterns below)
6. **On demand**: `plan.md`, `spec.md`, `design.md` (only when architectural questions arise)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: When executing project tasks, Claude Code MUST invoke the `task-execute` skill.

**DO NOT** read POML files directly and implement manually. The task-execute skill ensures:
- Knowledge files loaded (ADRs, constraints, patterns)
- Context tracked in `current-task.md`
- Proactive checkpointing every 3 steps
- Quality gates (code-review + adr-check) at Step 9.5
- PCF version bumping protocol
- Deployment via `dataverse-deploy` / `code-page-deploy` skills

**Trigger phrases**: "work on task X", "continue", "next task", "keep going", "resume task X"

---

## Placeholder Code Protocol

**EVERY task that produces placeholder/stub code MUST:**

1. Declare a `<placeholders>` section in the task POML
2. Log placeholders in `current-task.md` under "Decisions Made"
3. Include a `// PLACEHOLDER: <description> — Completed by task NNN` comment in code
4. Verify build compiles with placeholder (document if it doesn't)

**At project wrap-up**: Task 090 runs a full placeholder audit — grep for `// PLACEHOLDER:` across all modified files and verify each has been resolved.

**Types of acceptable placeholders:**
- `hardcoded-return` — Returns mock data; compiles and tests pass
- `todo-comment` — `// TODO:` with explicit task reference
- `mock-data` — Static data standing in for API response
- `no-op` — Empty function body; compiles but does nothing

---

## Parallel Execution Model

This project uses **3-track agent team parallelism** per sprint:

### Sprint 1 (Phase 1 — Foundation)
| Track | Package | Agent Ownership | Key Files |
|-------|---------|----------------|-----------|
| 1 | A: Side Pane | Agent 1 | `code-pages/SprkChatPane/`, `SprkChatBridge.ts` |
| 2 | B: Streaming Engine | Agent 2 | `RichTextEditor/plugins/`, `WorkingDocumentTools.cs` |
| 3 | D: Action Menu | Agent 3 | `SprkChatActionMenu.tsx`, `/actions` endpoint |

### Sprint 2 (Phase 2 — Integration)
| Track | Package | Agent Ownership | Key Files |
|-------|---------|----------------|-----------|
| 1 | C: AW Migration | Agent 1 | `code-pages/AnalysisWorkspace/`, legacy PCF removal |
| 2 | E: Re-Analysis | Agent 2 | `AnalysisExecutionTools.cs`, re-analysis flow |
| 3 | I: Web Search | Agent 3 | `WebSearchTools.cs`, knowledge scope |

### Sprint 3 (Phase 3 — Polish)
| Track | Package | Agent Ownership | Key Files |
|-------|---------|----------------|-----------|
| 1 | F: Diff View | Agent 1 | `DiffCompareView.tsx` |
| 2 | G: Selection Revision | Agent 2 | Selection API, cross-pane flow |
| 3 | H: Suggestions/Citations | Agent 3 | Suggestions + citation components |

**Rules for parallel execution:**
- Each agent MUST use `task-execute` skill (never bypass)
- Agents MUST NOT modify files owned by another track
- Shared files (`SprkChat.tsx`, `ChatEndpoints.cs`) are modified sequentially (one task at a time)
- Use `--dangerously-skip-permissions` for autonomous execution in isolated environments

---

## Key Technical Constraints

### Code Pages (Packages A, C)
- React 19 with `createRoot()` — NOT platform-provided React 16
- Bundle via webpack → inline HTML via `build-webresource.ps1`
- Deploy single self-contained HTML web resource
- `<FluentProvider theme={...}>` wrapping mandatory
- Independent auth via `Xrm.Utility.getGlobalContext()`
- Open via `Xrm.Navigation.navigateTo()` or `Xrm.App.sidePanes.createPane()`

### BFF API (Packages B, D, E, I)
- Minimal API endpoints only (ADR-001)
- Endpoint filters for AI authorization (ADR-008)
- Rate limiting on all AI endpoints (ADR-016)
- ProblemDetails for all errors (ADR-019)
- 0 additional DI registrations — factory-instantiated tools (ADR-010)
- Tool classes follow `AIFunctionFactory.Create` pattern (ADR-013)

### Shared Components (all packages)
- Fluent UI v9 exclusively — no v8, no hard-coded colors (ADR-021)
- Components in `@spaarke/ui-components` (ADR-012)
- Must work in both React 16 (PCF) and React 19 (Code Page) where applicable
- Dark mode + high-contrast support required

### Cross-Pane Communication
- `BroadcastChannel` API with `window.postMessage` fallback
- Channel name pattern: `sprk-workspace-{context}`
- Auth tokens NEVER transmitted via BroadcastChannel (independent auth per pane)
- Events: document_stream_*, selection_changed, context_changed

### Testing
- 80%+ line coverage for new code
- xUnit + NSubstitute (C#), Jest (TypeScript)
- `WebApplicationFactory<Program>` for API integration tests
- Test naming: `{Method}_{Scenario}_{ExpectedResult}`

---

## Applicable ADRs

| ADR | Applies To | Key Rule |
|-----|-----------|----------|
| ADR-001 | API endpoints | Minimal API; no Azure Functions |
| ADR-006 | Code Pages, PCF | Code Pages for standalone; PCF for field-bound only |
| ADR-007 | File access | SpeFileStore facade only |
| ADR-008 | Auth | Endpoint filters; no global middleware |
| ADR-010 | DI | ≤15 registrations; factory-instantiate new tools |
| ADR-012 | UI components | Shared library; Fluent v9; React 18-compatible |
| ADR-013 | AI tools | AIFunctionFactory; ChatHostContext flow; rate limiting |
| ADR-014 | Caching | Redis; no streaming token cache |
| ADR-015 | Data governance | No document content in logs; tenant-scoped |
| ADR-016 | Rate limits | Bounded concurrency; ProblemDetails for 429/503 |
| ADR-019 | Errors | ProblemDetails; stable errorCode; terminal SSE errors |
| ADR-021 | UI | Fluent v9 only; makeStyles + design tokens; dark mode |
| ADR-022 | React versions | Code Pages: React 19 bundled; PCF: React 16 platform |

---

## Decisions Made

*(Updated during task execution)*

| Date | Decision | Reason | Task |
|------|----------|--------|------|
| 2026-02-25 | Big-bang AW migration (no incremental iframe) | Simpler, eliminates React 16 constraint | Design |
| 2026-02-25 | Independent auth per Code Page pane | Security — no auth via BroadcastChannel | Design |
| 2026-02-25 | Factory-instantiated tools (0 DI) | ADR-010 budget at 12/15 | Design |
| 2026-02-25 | Playbook capabilities on Dataverse entity | Admin-configurable per playbook | Design |

---

## Implementation Notes

*(Updated during task execution)*

- Existing DI budget: 12 of 15 registrations used — **no room for new registrations**
- `SprkChatAgentFactory.ResolveTools()` is the integration point for new tool classes
- Existing SSE chunk types: `token`, `done`, `error` — new events must be additive
- `RichTextEditor` ref API: `focus()`, `getHtml()`, `setHtml()`, `clear()` — extend for streaming
- PlaybookBuilder `CommandPalette.tsx` and `SuggestionBar.tsx` are reference implementations only — do not modify them
- Code Page deployment requires TWO build steps: `npm run build` + `build-webresource.ps1` (inlines HTML)

---

## Resources

### Canonical Implementations (Follow These Patterns)

| Pattern | File |
|---------|------|
| SSE streaming endpoint | `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` |
| AI tool class | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/TextRefinementTools.cs` |
| Tool registration | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` |
| Code Page entry point | `src/client/code-pages/SemanticSearch/index.tsx` |
| SprkChat component | `src/client/shared/.../SprkChat/SprkChat.tsx` |
| RichTextEditor | `src/client/shared/.../RichTextEditor/RichTextEditor.tsx` |
| Command palette reference | `src/client/pcf/PlaybookBuilderHost/.../CommandPalette.tsx` |
| Suggestions reference | `src/client/pcf/PlaybookBuilderHost/.../SuggestionBar.tsx` |
| Theme management | `.claude/patterns/pcf/theme-management.md` |

### Constraints Files
- `.claude/constraints/api.md`
- `.claude/constraints/ai.md`
- `.claude/constraints/pcf.md`
- `.claude/constraints/testing.md`

---

## Project Completed

| Field | Value |
|-------|-------|
| **Completion Date** | 2026-02-26 |
| **Branch** | `work/ai-spaarke-platform-enhancents-r2` |
| **Branch Status** | Ready for merge to master |
| **Tasks Completed** | 89/89 |
| **Packages Delivered** | A (Side Pane), B (Streaming Write), C (AW Migration), D (Action Menu), E (Re-Analysis), F (Diff View), G (Selection Revision), H (Suggestions/Citations), I (Web Search) |

All R2 implementation, testing, deployment, and validation work is complete. The branch contains the full SprkChat Interactive Collaboration feature set. See `notes/lessons-learned.md` for retrospective documentation.

---

*For Claude Code: This project is complete. No further task execution is needed.*
