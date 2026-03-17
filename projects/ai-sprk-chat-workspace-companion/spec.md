# SprkChat Analysis Workspace Companion - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-16
> **Source**: design-analysis-workspace-companion.md
> **Prerequisite**: ai-sprk-chat-context-awareness-r1 (Phase 1 — complete, merged)

---

## Executive Summary

SprkChat currently auto-registers on every page as a generic chat, competing with M365 Copilot and launching in the Analysis Workspace without awareness of the analysis context. This project repositions SprkChat as a **contextual AI companion** by: (1) removing global auto-launch from pages that don't need it, (2) enriching the Analysis Workspace launch with full analysis context (type, matter, practice area, source document), and (3) adding an inline AI toolbar for editor text selection plus enhanced SprkChat pane interactions (quick-action chips, slash command menu, plan preview, rich response rendering).

All phases (2A through 2F) are in scope for this release — full feature build, no stubs.

---

## Scope

### In Scope

**Phase 2A — Contextual Launch**
- Remove SidePaneManager injection from `EventsPage/index.html` and `SpeAdminApp/index.html`
- Extend `openSprkChatPane.ts` with `SprkChatLaunchContext` interface (analysisType, matterType, practiceArea, analysisId, sourceFileId, sourceContainerId, mode)
- Update `AnalysisWorkspace/App.tsx` to launch SprkChat with enriched context after record loads
- Update `SprkChatPane` to consume new URL context parameters

**Phase 2B — Inline AI Toolbar**
- New `InlineAiToolbar` + `InlineAiActions` components in `@spaarke/ui-components`
- Floating toolbar positioned above text selection in Lexical editor
- 5 default actions: summarize (chat), simplify (diff), expand (diff), fact-check (chat), ask (chat)
- Wire into `EditorPanel.tsx` via new `useInlineAiToolbar` hook
- SprkChat subscribes to `inline_action` BroadcastChannel events
- Selection-lost protection via `mousedown` (not `click`) on toolbar buttons

**Phase 2C — Context-Driven Actions**
- New BFF endpoint: `GET /api/ai/chat/context-mappings/analysis/{analysisId}`
- Resolves: analysisType, matterType, practiceArea → defaultPlaybook + availablePlaybooks + inlineActions + knowledgeSources
- Inline actions sourced from `sprk_playbookcapabilities` field on `sprk_analysisplaybook` (existing field — map capabilities to `InlineAiAction` definitions)
- Default 5 actions always present; playbook-specific actions appended
- Knowledge source scoping based on resolved analysis context
- Seed initial playbook-to-inline-action mappings in Dataverse (patent claims example)

**Phase 2D — Insert-to-Editor**
- "Insert" button on SprkChat message responses
- New BroadcastChannel event: `document_insert` (content + cursor position)
- Editor inserts at cursor or replaces selection with undo support
- Supports plain text and formatted HTML

**Phase 2E — Enhanced SprkChat Pane**
- `SlashCommandMenu` component (`@spaarke/ui-components`): Fluent Popover, keyboard-navigable, `[/]` button in input bar + input interception on `/` keystroke
- Dynamic command registry: system commands (static) + playbook capabilities (from `sprk_playbookcapabilities`) + available playbooks for switching
- `QuickActionChips` component: up to 4 chips above input bar, populated from context mapping endpoint, hidden when pane < 350px
- `SprkChatMessageRenderer`: structured response card renderer (markdown default + citations, diffs, entity cards, action confirmations)
- `PlanPreviewCard`: plan preview message type with Proceed/Edit Plan/Cancel controls and per-step progress indicators

**Phase 2F — BFF Plan Preview + Write-Back**
- New SSE event type: `plan_preview` (emitted before executing multi-tool chains or write-back actions)
- Plan approval endpoint: `POST /api/ai/chat/sessions/{sessionId}/plan/approve`
- Write-back to `sprk_analysisoutput.sprk_workingdocument` (Dataverse field — NOT SPE source file)
- Mandatory plan preview for: any 2+ tool chain, any Dataverse field write, any external action (email)
- Conversational plan editing support ("skip step 2", "also include the deadline")
- BFF detects compound intent → emits plan_preview SSE → client renders → user approves → BFF executes

### Out of Scope

- Modifying SPE source documents (write-back targets `sprk_analysisoutput` only)
- Changes to `sprk_analysisplaybook` entity schema (using existing `sprk_playbookcapabilities`)
- Changes to `sprk_aichatcontextmap` schema (Phase 1 entity — not modified here)
- Multi-tenant mapping isolation
- Mobile/tablet inline toolbar (Analysis Workspace is desktop-primary)
- Separate AI service (all AI extends existing BFF per ADR-013)
- Separate `ai-sprk-chat-extensibility-r1` project features not listed above

### Affected Areas

| Area | Path | Change Type |
|------|------|-------------|
| BFF API — Context Mapping | `src/server/api/Sprk.Bff.Api/Api/Ai/` | New endpoint + service extension |
| BFF API — Chat Sessions | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/` | Plan preview SSE + approval endpoint |
| Shared UI Components | `src/client/shared/Spaarke.UI.Components/src/components/` | 11 new files |
| Analysis Workspace | `src/client/code-pages/AnalysisWorkspace/src/` | App.tsx, EditorPanel, new hook |
| SprkChat Pane | `src/client/code-pages/SprkChatPane/` | Context consumption, inline action handler, slash menu, chips |
| EventsPage | `src/solutions/EventsPage/index.html` | Remove injection |
| SpeAdminApp | `src/solutions/SpeAdminApp/index.html` | Remove injection |

---

## Requirements

### Functional Requirements

1. **FR-01**: SprkChat MUST NOT auto-register on EventsPage or SpeAdminApp — Acceptance: pages load without creating a SprkChat side pane
2. **FR-02**: Analysis Workspace MUST launch SprkChat with analysisType, matterType, practiceArea, analysisId, sourceFileId, sourceContainerId — Acceptance: SprkChat URL params contain all fields after record load
3. **FR-03**: Inline AI toolbar MUST appear above text selection within the Lexical editor — Acceptance: toolbar visible within 200ms of stable selection; absent when selection collapsed
4. **FR-04**: All inline actions MUST execute through the existing SprkChat session (not a separate API call) — Acceptance: actions appear in chat history pane
5. **FR-05**: Diff-mode actions (simplify, expand) MUST open existing `DiffReviewPanel` — Acceptance: DiffReviewPanel opens with proposed revision
6. **FR-06**: Context-specific inline actions MUST be sourced from `sprk_playbookcapabilities` mapped to `InlineAiAction` definitions — Acceptance: patent-claims playbook shows extract-claims and prior-art-search actions
7. **FR-07**: New BFF endpoint `GET /api/ai/chat/context-mappings/analysis/{analysisId}` MUST resolve full context from analysis record + related matter + source document — Acceptance: returns defaultPlaybook, availablePlaybooks, inlineActions, knowledgeSources, analysisContext
8. **FR-08**: Quick-action chips MUST update when playbook changes — Acceptance: chip set changes within one render cycle of playbook switch
9. **FR-09**: Slash command menu MUST open on `/` keystroke (first character) or `[/]` button click — Acceptance: menu appears with keyboard navigation; type-ahead filters; Esc dismisses
10. **FR-10**: Slash commands for playbook capabilities MUST be dynamically derived from `sprk_playbookcapabilities` — Acceptance: switching playbooks updates command list
11. **FR-11**: Plan preview MUST appear before executing compound actions (2+ tools) or any Dataverse write-back — Acceptance: no write-back executes without user Proceed confirmation
12. **FR-12**: Write-back targets `sprk_analysisoutput.sprk_workingdocument` ONLY — Acceptance: SPE source document is never modified by chat actions
13. **FR-13**: All AI-generated content MUST stream (SSE) — Acceptance: no REST-returned AI content; all inline action results stream

### Non-Functional Requirements

- **NFR-01**: Inline toolbar selection debounce ≤ 200ms to avoid flicker during drag-select
- **NFR-02**: All new Fluent UI components support dark mode (ADR-021)
- **NFR-03**: `InlineAiToolbar` and `SlashCommandMenu` in shared library MUST NOT depend on `Xrm` (no PCF/Dataverse SDK imports)
- **NFR-04**: Chips hidden when SprkChat pane width < 350px (natural language + slash menu remain available)
- **NFR-05**: Plan preview execution shows per-step progress with partial results visible as steps complete
- **NFR-06**: Context mapping endpoint uses existing Redis caching pattern (30-min TTL per ADR-009)

---

## Technical Constraints

### Applicable ADRs

- **ADR-001**: New BFF endpoints use Minimal API pattern (no MVC controllers)
- **ADR-006**: AnalysisWorkspace is a Code Page (React 19, bundled) — not a PCF control
- **ADR-008**: New BFF endpoint MUST use endpoint filter for authorization (not global middleware)
- **ADR-009**: Context mapping extension MUST use Redis-first caching; no hybrid L1 cache unless profiling proves need
- **ADR-012**: All new UI components go in `@spaarke/ui-components` shared library
- **ADR-013**: AI features extend the BFF `AiToolService` pipeline — no separate AI service
- **ADR-021**: All UI uses Fluent UI v9 tokens exclusively; no hard-coded colors; dark mode required
- **ADR-022**: AnalysisWorkspace uses React 19 (bundled via Code Page); `createRoot` is correct; no platform-provided React

### MUST Rules

- ✅ MUST use Fluent UI v9 `Popover` + `MenuList` for `SlashCommandMenu`
- ✅ MUST use `mousedown` (not `click`) on inline toolbar to prevent selection loss
- ✅ MUST reuse existing `DiffReviewPanel` for diff-type inline actions (not a new component)
- ✅ MUST reuse existing `BroadcastChannel` bridge for all editor↔SprkChat communication
- ✅ MUST route all inline actions through existing SprkChat session (appears in chat history)
- ✅ MUST use endpoint filter for new context-mappings/analysis endpoint authorization
- ❌ MUST NOT modify SPE source files from chat write-back
- ❌ MUST NOT add `Xrm` dependency to `@spaarke/ui-components` shared library
- ❌ MUST NOT use React 16/17 APIs in AnalysisWorkspace (React 19 Code Page)
- ❌ MUST NOT create a separate AI service (extend BFF per ADR-013)
- ❌ MUST NOT stub plan preview — BFF must emit `plan_preview` SSE event type in Phase 2F

### Existing Patterns to Follow

- See `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatContextMappingService.cs` — extend for analysis-specific resolution
- See `src/server/api/Sprk.Bff.Api/Api/Ai/AiToolEndpoints.cs` — SSE streaming pattern for plan_preview events
- See `src/client/code-pages/AnalysisWorkspace/src/hooks/useDocumentStreaming.ts` — existing bridge event handling
- See `src/client/code-pages/AnalysisWorkspace/src/hooks/useDiffReview.ts` — reuse for diff-type inline actions
- See `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx` — entry point for inline_action subscription and new component mounting

---

## Success Criteria

1. [ ] SprkChat does NOT auto-register on EventsPage or SpeAdminApp — Verify: load each page, confirm no side pane created
2. [ ] SprkChat launches with full analysis context in Analysis Workspace — Verify: inspect URL params passed to SprkChatPane
3. [ ] Inline AI toolbar appears on text selection in editor — Verify: select text in EditorPanel, confirm toolbar renders above selection
4. [ ] Inline actions execute through SprkChat session and appear in chat history — Verify: click toolbar action, confirm message appears in pane
5. [ ] Diff-mode actions open existing DiffReviewPanel — Verify: click Simplify, confirm diff panel opens
6. [ ] Context-specific actions appear based on playbook — Verify: patent-claims playbook shows extract-claims, prior-art-search
7. [ ] "Ask SprkChat" sends selected text to chat pane — Verify: selected text appears as quoted message in chat
8. [ ] All inline actions stream results — Verify: no AI content returned synchronously; all via SSE
9. [ ] SprkChat pane ID (`sprkchat-analysis`) prevents duplicate panes — Verify: open Analysis Workspace twice, only one pane exists
10. [ ] Quick-action chips appear above input bar and update with context/playbook — Verify: switch playbook, chips update
11. [ ] Slash command menu opens on `/` keystroke with dynamic playbook commands — Verify: type `/` in chat input, menu shows playbook capabilities
12. [ ] Compound actions and write-back show plan preview before executing — Verify: request a write-back, plan preview card appears, no execution until Proceed clicked
13. [ ] Rich responses render structured cards (citations, diffs, entity cards, confirmations) — Verify: trigger each response type, confirm card renders not raw JSON
14. [ ] Write-back modifies `sprk_analysisoutput.sprk_workingdocument` only — Verify: SPE source file unchanged after write-back operation

---

## Dependencies

### Prerequisites

- Phase 1 complete: `ChatContextMappingService`, `sprk_aichatcontextmap` entity, `GET /api/ai/chat/context-mappings` endpoint — all confirmed merged ✅
- `sprk_playbookcapabilities` field exists on `sprk_analysisplaybook` entity — confirmed existing ✅
- `DiffReviewPanel`, `useDocumentStreaming`, `useDiffReview` hooks exist in AnalysisWorkspace — confirmed existing ✅
- BroadcastChannel bridge (`DocumentStreamBridge.tsx`) exists — confirmed existing ✅

### External Dependencies

- `sprk_analysisplaybook` records in Dataverse with populated `sprk_playbookcapabilities` for patent-claims context (seed data task)
- `sprk_playbookcapabilities` field format must be investigated before implementing capability mapping

---

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Phase 2F scope | Is Phase 2F in or out of this release? | In — full build, all phases included | All BFF plan-preview + write-back tasks are in scope; no stubs |
| `inlineActions` field | Is `inlineActions` a new field on `sprk_analysisplaybook`? | No — use existing `sprk_playbookcapabilities` field | Implementation maps capability values to `InlineAiAction` definitions; no schema change needed |
| Context mapping endpoint | New endpoint or extend existing? | Create what is required | New endpoint `GET /api/ai/chat/context-mappings/analysis/{analysisId}` confirmed needed (does not exist) |
| Plan preview BFF timing | BFF changes needed for Phase 2E or only 2F? | Prefer no stubs — include BFF changes | Full BFF plan_preview SSE event type in Phase 2F; client-side rendering in Phase 2E aligns with BFF delivery |
| Rich response types | All 5 types required or subset? | Include all if not significant; else defer | All 5 types included; deferred only if implementation proves excessively complex |
| Write-back target | Write-back to analysis output or SPE file? | `sprk_analysisoutput.sprk_workingdocument` ONLY — SPE source is read-only | Write-back routes through Dataverse update, not SPE/Graph |

## Assumptions

- **`sprk_playbookcapabilities` format**: Multi-select option set on `sprk_analysisplaybook`. Known values: `search` (100000000), `analyze` (100000001), `write_back` (100000002), `reanalyze` (100000003), `selection_revise` (100000004), `web_search` (100000005), `summarize` (100000006). Read as a collection of integers server-side.
- **Capability-to-InlineAction mapping**: Option set integer values mapped server-side to `InlineAiAction` definitions (label, icon, actionType) and to slash command definitions via a static dictionary in the BFF; not stored in Dataverse. `write_back` maps to plan-preview-gated write action; `selection_revise` maps to diff-type inline action.
- **Entity card navigation**: `Xrm.Navigation.navigateTo` called from `AnalysisWorkspace` layer, not from within the shared library component.
- **Plan approval endpoint**: Session state tracks pending plan between `plan_preview` emission and user approval; investigate existing session model in `AiToolService` before Phase 2F tasks.
- **Rich response JSON**: Structured response metadata attached as a `metadata` field on SSE events; client reads `metadata.responseType` to select renderer.

## Unresolved Questions

- [x] **`sprk_playbookcapabilities` field format** — Resolved: multi-select option set with 7 known values (see Assumptions). No further investigation needed.
- [ ] **Plan preview session state** — How does BFF maintain "pending plan" state between `plan_preview` emission and user approval? Investigate `AiToolService` session model before Phase 2F. Blocks: plan approval endpoint design.

---

## File Inventory

### New Files (12)

| File | Purpose | Phase |
|------|---------|-------|
| `src/client/shared/Spaarke.UI.Components/src/components/InlineAiToolbar/InlineAiToolbar.tsx` | Floating toolbar component | 2B |
| `src/client/shared/Spaarke.UI.Components/src/components/InlineAiToolbar/InlineAiActions.tsx` | Action button list | 2B |
| `src/client/shared/Spaarke.UI.Components/src/components/InlineAiToolbar/useInlineAiToolbar.ts` | Position + visibility hook | 2B |
| `src/client/shared/Spaarke.UI.Components/src/components/InlineAiToolbar/useInlineAiActions.ts` | Action execution via BroadcastChannel | 2B |
| `src/client/shared/Spaarke.UI.Components/src/components/InlineAiToolbar/inlineAiToolbar.types.ts` | Types + default action definitions | 2B |
| `src/client/shared/Spaarke.UI.Components/src/components/SlashCommandMenu/SlashCommandMenu.tsx` | Fluent Popover command menu | 2E |
| `src/client/shared/Spaarke.UI.Components/src/components/SlashCommandMenu/useSlashCommands.ts` | Registry + filtering logic | 2E |
| `src/client/shared/Spaarke.UI.Components/src/components/SlashCommandMenu/slashCommand.types.ts` | Types | 2E |
| `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/QuickActionChips.tsx` | Chip bar above input | 2E |
| `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChatMessageRenderer.tsx` | Rich response card renderer | 2E |
| `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/PlanPreviewCard.tsx` | Plan preview with approve/edit/cancel | 2E/2F |
| `src/client/code-pages/AnalysisWorkspace/src/hooks/useInlineAiToolbar.ts` | Wires toolbar to EditorPanel + bridge | 2B |

### Modified Files (7)

| File | Change | Phase |
|------|--------|-------|
| `src/client/code-pages/SprkChatPane/launcher/openSprkChatPane.ts` | Expand `SprkChatLaunchContext` interface | 2A |
| `src/client/code-pages/AnalysisWorkspace/src/App.tsx` | Pass enriched context on SprkChat launch | 2A |
| `src/client/code-pages/AnalysisWorkspace/src/components/EditorPanel.tsx` | Mount `InlineAiToolbar` | 2B |
| `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx` | `inline_action` handler, mount SlashCommandMenu + QuickActionChips | 2B/2E |
| `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChatInput.tsx` | `[/]` button, input interception | 2E |
| `src/solutions/EventsPage/index.html` | Remove SidePaneManager injection snippet | 2A |
| `src/solutions/SpeAdminApp/index.html` | Remove SidePaneManager injection snippet | 2A |

### New BFF Files

| File | Purpose | Phase |
|------|---------|-------|
| New endpoint in `ChatContextMappingEndpoints.cs` (or dedicated file) | Add `GET /analysis/{analysisId}` route | 2C |
| `AnalysisChatContextResolver.cs` (new service) | Resolves analysis record → full context | 2C |
| Plan approval endpoint | `POST /api/ai/chat/sessions/{sessionId}/plan/approve` | 2F |

---

*AI-optimized specification. Original design: design-analysis-workspace-companion.md*
