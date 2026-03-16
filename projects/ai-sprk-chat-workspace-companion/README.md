# SprkChat Analysis Workspace Companion

> **Last Updated**: 2026-03-16
>
> **Status**: In Progress

## Overview

Repositions SprkChat from a generic global chat widget into a contextual AI companion for the Analysis Workspace — removing unwanted auto-registration from non-AI pages, enriching the launch with full analysis context, and adding an inline AI toolbar, slash command menu, quick-action chips, rich response rendering, and plan preview with write-back capabilities.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Implementation plan with 6-phase WBS |
| [Design Spec](./spec.md) | Original design specification |
| [Task Index](./tasks/TASK-INDEX.md) | Task breakdown and status |
| [Current Task](./current-task.md) | Active task state (context recovery) |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Planning → Ready for Development |
| **Progress** | 0% |
| **Owner** | Spaarke Dev Team |

## Problem Statement

SprkChat currently auto-registers as a side pane on every Dataverse page, including EventsPage and SpeAdminApp where it is irrelevant and competes with M365 Copilot. When it does launch in the Analysis Workspace, it has no awareness of the analysis context (type, matter, practice area, source document), so the AI can only offer generic assistance. Users must manually copy text from the editor into the chat, and there is no way to direct AI output back into the working document.

## Solution Summary

Phase 2A removes SprkChat's global auto-injection from non-AI pages and extends the launcher to carry a rich `SprkChatLaunchContext` so the Analysis Workspace can pass analysis identity on open. Phase 2B adds a floating `InlineAiToolbar` that appears over text selections in the Lexical editor with 5 default actions routing through the existing chat session. Phase 2C adds a new BFF endpoint that resolves the analysis record + matter to a full context mapping (playbooks, inline actions, knowledge sources). Phases 2D–2F add insert-to-editor, slash commands, quick-action chips, structured response rendering, and a plan preview gate before any write-back or compound tool chain.

## Graduation Criteria

The project is **complete** when:

- [ ] SprkChat does NOT auto-register on EventsPage or SpeAdminApp
- [ ] Analysis Workspace launches SprkChat with all 7 context parameters (analysisType, matterType, practiceArea, analysisId, sourceFileId, sourceContainerId, mode)
- [ ] Inline AI toolbar appears within 200ms of stable text selection in editor and routes actions through chat session
- [ ] Diff-mode inline actions (Simplify, Expand) open existing `DiffReviewPanel`
- [ ] `GET /api/ai/chat/context-mappings/analysis/{analysisId}` endpoint resolves and returns defaultPlaybook + inlineActions + knowledgeSources
- [ ] Quick-action chips appear above chat input and update when playbook changes
- [ ] Slash command menu opens on `/` keystroke with type-ahead and keyboard navigation
- [ ] Plan preview card renders before any 2+ tool chain or Dataverse write-back
- [ ] Write-back targets `sprk_analysisoutput.sprk_workingdocument` ONLY — SPE source file unchanged
- [ ] All AI responses stream via SSE — no synchronously returned AI content
- [ ] All new UI components support dark mode (ADR-021 tokens only)
- [ ] Context mapping endpoint uses Redis caching with 30-min TTL (ADR-009)

## Scope

### In Scope

**Phase 2A — Contextual Launch**
- Remove SidePaneManager injection from `EventsPage/index.html` and `SpeAdminApp/index.html`
- Extend `openSprkChatPane.ts` with `SprkChatLaunchContext` interface
- Update `AnalysisWorkspace/App.tsx` to launch SprkChat with enriched context
- Update `SprkChatPane` to consume new URL context parameters

**Phase 2B — Inline AI Toolbar**
- 5 new files in `Spaarke.UI.Components/src/components/InlineAiToolbar/`
- Floating toolbar above text selection in Lexical editor
- 1 new hook `useInlineAiToolbar.ts` in AnalysisWorkspace
- Wire into `EditorPanel.tsx`

**Phase 2C — Context-Driven Actions**
- New BFF endpoint `GET /api/ai/chat/context-mappings/analysis/{analysisId}`
- `AnalysisChatContextResolver.cs` service
- Capability-to-InlineAction mapping from `sprk_playbookcapabilities` option set
- Seed patent-claims playbook example data

**Phase 2D — Insert-to-Editor**
- Insert button on SprkChat message responses
- `document_insert` BroadcastChannel event
- Editor inserts at cursor or replaces selection (with undo)

**Phase 2E — Enhanced SprkChat Pane**
- `SlashCommandMenu` component + `useSlashCommands` hook + types (3 files)
- `QuickActionChips`, `SprkChatMessageRenderer`, `PlanPreviewCard` components (3 files)
- Wire into `SprkChat.tsx` and `SprkChatInput.tsx`

**Phase 2F — BFF Plan Preview + Write-Back**
- `plan_preview` SSE event type from BFF
- Plan approval endpoint `POST /api/ai/chat/sessions/{sessionId}/plan/approve`
- Mandatory plan preview for compound tool chains and Dataverse writes
- Write-back to `sprk_analysisoutput.sprk_workingdocument`

### Out of Scope

- Modifying SPE source documents
- Changes to `sprk_analysisplaybook` entity schema
- Changes to `sprk_aichatcontextmap` schema (Phase 1 entity)
- Multi-tenant mapping isolation
- Mobile/tablet inline toolbar
- Separate AI service

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Extend BFF, no new service | Unified runtime, avoid microservice complexity | [ADR-013](.claude/adr/ADR-013-ai-architecture.md) |
| InlineAiToolbar in shared library | Reusable across surfaces, no Xrm dependency | [ADR-012](.claude/adr/ADR-012-shared-components.md) |
| `mousedown` not `click` on toolbar | Prevents selection loss before action triggers | spec FR-04 |
| Redis 30-min TTL for context mapping | Avoids repeated Dataverse+SPE lookups | [ADR-009](.claude/adr/ADR-009-redis-caching.md) |
| Write-back to `sprk_analysisoutput` only | SPE source files are read-only by design | spec FR-12 |
| Endpoint filter for new BFF route | Resource auth requires route values | [ADR-008](.claude/adr/ADR-008-endpoint-filters.md) |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Plan approval session state design | High | Medium | Investigate `ChatSessionManager` model before Phase 2F tasks; decision logged in CLAUDE.md |
| `sprk_playbookcapabilities` capability mapping complexity | Med | Low | Field format confirmed (multi-select option set, 7 values); static dictionary in BFF |
| Lexical editor selection events timing | Med | Medium | 200ms debounce; `mousedown` guard; test with drag-select edge cases |
| SSE streaming for all 5 response card types | Med | Low | Reuse existing `useSseStream` hook; extend `metadata.responseType` field |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| Phase 1: `ChatContextMappingService` + `sprk_aichatcontextmap` | Internal | ✅ Merged | Base for Phase 2C extension |
| `DiffReviewPanel` + `useDiffReview` hook | Internal | ✅ Exists | Reused for diff-type inline actions |
| `BroadcastChannel` bridge (`DocumentStreamBridge.tsx`) | Internal | ✅ Exists | Extended for `inline_action` and `document_insert` events |
| `sprk_playbookcapabilities` field on `sprk_analysisplaybook` | Dataverse | ✅ Exists | 7 known capability values |
| Patent-claims playbook with seed data | Dataverse | 🔲 Task | Seed data task required |

## Changelog

| Date | Version | Change |
|------|---------|--------|
| 2026-03-16 | 1.0 | Initial project setup |
