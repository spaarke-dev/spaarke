# CLAUDE.md — SprkChat Analysis Workspace Command Center

> **Project**: ai-sprk-chat-extensibility-r1
> **Last Updated**: 2026-03-25

---

## Project Context

Transform SprkChat from text-only chat into a contextual command center for Analysis Workspace with slash commands, quick-action chips, compound actions, and context-enriched routing.

**Key Files**:
- [spec.md](spec.md) — AI-optimized specification (17 FRs, 7 NFRs)
- [plan.md](plan.md) — Implementation plan (5 phases)
- [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) — Task status

---

## Applicable ADRs

| ADR | Key Constraint |
|-----|---------------|
| ADR-001 | Minimal API; no Azure Functions |
| ADR-006 | SprkChat = Code Page (React 18+); NOT PCF |
| ADR-008 | Endpoint filters for auth; no global middleware |
| ADR-012 | Shared components in `@spaarke/ui-components`; callback props; zero service deps |
| ADR-013 | AI extends BFF; flow ChatHostContext; no separate AI service |
| ADR-021 | Fluent v9 only; semantic tokens; dark mode required |

---

## Key Patterns

| Pattern | Location | Usage |
|---------|----------|-------|
| SSE streaming | `.claude/patterns/ai/streaming-endpoints.md` | Chat streaming |
| Minimal API endpoints | `.claude/patterns/api/endpoint-definition.md` | New BFF endpoints |
| Email integration | `.claude/patterns/api/send-email-integration.md` | Phase 3 email |
| Code Page patterns | `.claude/patterns/webresource/` | SprkChatPane |

---

## Existing Components (Reuse)

### Shared UI (`@spaarke/ui-components`)
- `SprkChatInput.tsx` — Input with action menu (intercept `/` here)
- `QuickActionChips.tsx` — Enhance with playbook capabilities
- `PlanPreviewCard.tsx` — Enhance with edit/cancel for compound actions
- `ActionConfirmationDialog.tsx` — Reuse for write-back confirmation
- `SprkChatSuggestions.tsx` — Chip-style pattern reference

### Hooks
- `useDynamicSlashCommands.ts` — Command registry from capabilities
- `useChatPlaybooks.ts` — Playbook switching
- `useChatSession.ts` — Session lifecycle
- `useSelectionListener.ts` — Editor selection tracking
- `useActionHandlers.ts` — Action execution

### SprkChatPane Code Page
- `contextService.ts` — Context polling (2s interval) + pane lifecycle
- `openSprkChatPane.ts` — Launch orchestrator + context enrichment
- `App.tsx` — Session persistence via sessionStorage

### BFF Services
- `PlaybookChatContextProvider.cs` — System prompt + tool registration
- `DynamicCommandResolver.cs` — Populate commands from playbooks
- `CompoundIntentDetector.cs` — Multi-step action detection
- `SprkChatAgent.cs` — Azure OpenAI agent with tools
- `PendingPlanManager.cs` — Plan approval state
- `PlaybookDispatcher.cs` — Route to appropriate playbook

---

## MUST Rules

- MUST use Fluent v9 for SlashCommandMenu (Popover, MenuList)
- MUST place new shared components in `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/`
- MUST use callback-based props (no Xrm/service dependencies in shared components)
- MUST flow context through `ChatHostContext` pipeline in BFF
- MUST NOT create legacy JavaScript webresources
- MUST NOT call AI services from client
- MUST NOT use Fluent v8 or hard-coded colors
- MUST NOT create global auth middleware

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: When executing project tasks, invoke the `task-execute` skill. DO NOT read POML files directly and implement manually.

The task-execute skill ensures:
- Knowledge files are loaded (ADRs, constraints, patterns)
- Context is tracked in current-task.md
- Proactive checkpointing every 3 steps
- Quality gates run at Step 9.5
- Progress is recoverable after compaction

**Trigger phrases**: "work on task X", "continue", "next task", "keep going", "resume task X"

---

*For Claude Code: Load this file when working on any task in this project.*
