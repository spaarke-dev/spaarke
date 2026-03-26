# SprkChat Analysis Workspace Command Center — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-25
> **Source**: `projects/ai-sprk-chat-extensibility-r1/design.md` (5,022 words, March 25, 2026)
> **Scope**: Analysis Workspace only (Corporate Workspace queries moved to `projects/ai-m365-copilot-integration`)

---

## Executive Summary

Transform SprkChat from a text-only chat interface into a contextual command center for the Analysis Workspace. The system provides three interaction tiers — natural language (primary), quick-action chips (discoverable), and slash commands (power users) — all backed by a smart routing layer that enriches user messages with structured context signals before the BFF AI model selects the appropriate playbook tools. Phase 0 enforces scope by removing SprkChat from non-analysis pages and implementing side pane lifecycle management.

---

## Scope

### In Scope

- **Phase 0**: Remove SprkChat from Corporate Workspace (SidePaneManager injection + global ribbon button); implement side pane close on navigation away from analysis; auto-reopen with session persistence on return
- **Phase 1**: Smart routing layer with client-side context enrichment; dynamic command registry from playbook capabilities; `SlashCommandMenu` Fluent v9 component; system commands (`/clear`, `/new`, `/help`, `/export`); playbook switching via slash menu
- **Phase 2**: Quick-action chips above input area; chips populated from playbook capabilities + analysis context; analysis-type-aware chip sets; responsive behavior
- **Phase 3**: Compound actions with plan preview and approval gates; email drafting via `sprk_communication` module (Graph API); progress indicators; write-back with confirmation
- **Phase 4**: Parameterized prompt templates stored in Dataverse; playbook library browser within chat
- **Phase 5**: Playbook library discoverability; admin-curated featured playbooks; usage analytics (future)

### Out of Scope

- Corporate Workspace AI interactions (moved to M365 Copilot integration project)
- Matter-level Q&A outside analysis context (moved to M365 Copilot)
- General navigation queries ("open the Acme project") (moved to M365 Copilot)
- Rich response cards with custom entity rendering (deferred — not required at this stage)
- Admin-defined context actions via new Dataverse table `sprk_aichatcontextaction` (dropped — playbook capabilities multi-select provides sufficient dynamic actions)
- M365 Copilot integration, Adaptive Cards, Custom Engine Agent (separate project)

### Affected Areas

- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/` — New components (SlashCommandMenu, QuickActionChips enhancements, PlanPreviewCard enhancements, CompoundActionProgress)
- `src/client/code-pages/SprkChatPane/src/` — Context enrichment, slash command interception, session lifecycle
- `src/client/code-pages/SprkChatPane/launcher/openSprkChatPane.ts` — Launch context updates
- `src/client/code-pages/AnalysisWorkspace/src/App.tsx` — Side pane cleanup on unmount
- `src/client/side-pane-manager/SidePaneManager.ts` — Removal or scope restriction
- `src/solutions/LegalWorkspace/index.html` — Remove SidePaneManager script injection
- `src/client/webresources/ribbon/sprk_application_ribbon_sprkchat.xml` — Remove global ribbon button
- `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` — Enhanced context in system prompt, tool registration from playbook capabilities
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/` — PlaybookChatContextProvider enhancements for dynamic tool registration

---

## Requirements

### Functional Requirements

#### Phase 0: Scope Enforcement + Side Pane Lifecycle

1. **FR-01**: Remove SidePaneManager injection from Corporate Workspace — `src/solutions/LegalWorkspace/index.html` MUST NOT inject `sprk_SidePaneManager` script into parent Dataverse shell. Acceptance: SprkChat icon does not appear in Corporate Workspace side pane rail.

2. **FR-02**: Remove global ribbon button for SprkChat — `sprk_application_ribbon_sprkchat.xml` MUST be removed from the deployed solution. Acceptance: No "SprkChat" button in global command bar on any page.

3. **FR-03**: Close side pane on navigation away — When user navigates away from an Analysis record (to Corporate Workspace, matter form, etc.), the `sprkchat-analysis` side pane MUST be explicitly closed. Two mechanisms:
   - Primary: `useEffect` cleanup in `AnalysisWorkspace/App.tsx` calls `Xrm.App.sidePanes.getPane('sprkchat-analysis')?.close()` on unmount
   - Fallback: `contextService.ts` context poll (2-second interval) detects entityType change away from `sprk_analysisoutput` → triggers pane close
   - Acceptance: Navigating from Analysis Workspace to Corporate Workspace leaves no orphaned SprkChat pane.

4. **FR-04**: Auto-reopen with session on return — When user navigates back to an Analysis record, SprkChat MUST auto-reopen and restore the previous session from sessionStorage (keyed by pane ID). Acceptance: Return to same analysis record restores chat history and context. Existing infrastructure: `sessionStorage` persistence in `SprkChatPane/App.tsx` (lines 262-271).

#### Phase 1: Smart Chat Foundation

5. **FR-05**: Client-side context enrichment — `SprkChatInput` MUST enrich outgoing messages with structured context signals before sending to BFF:
   - `source`: `"natural_language"` | `"slash_command"` | `"chip_click"`
   - `editor_selection`: Current Lexical editor selection text (if any, via `useSelectionBroadcast`)
   - `document_type`: From Document Profile classification output
   - `available_actions`: List of playbook capability identifiers for current context
   - `conversation_phase`: `"initial"` | `"refinement"` | `"follow_up"` (derived from message history)
   - Acceptance: BFF receives enriched payload; AI model routing accuracy improves with context signals.

6. **FR-06**: Dynamic command registry — Available slash commands MUST be populated from three sources: system commands (static), active playbook capabilities (dynamic), and playbook switching options (dynamic). Commands MUST update when playbook changes. Acceptance: Switching playbook via `/switch` changes available commands within 1 second.

7. **FR-07**: SlashCommandMenu component — A Fluent v9 Popover MUST open when `/` is typed as the first character in empty input (or `[/]` button clicked). Features:
   - Keyboard navigation (Arrow Up/Down, Enter to select, Esc to dismiss)
   - Type-ahead filtering (`/se` → shows `/search`)
   - Categories: active playbook actions (labeled with playbook name), "Switch Assistant", "System"
   - Width matches input width; max height ~300px with scroll
   - Acceptance: Full keyboard-accessible navigation; closes on Esc, click-away, or Backspace past `/`.

8. **FR-08**: System commands — Built-in commands always available:
   - `/clear` — Delete session, start fresh
   - `/new` — New session with current context
   - `/export` — Download conversation as markdown
   - `/help` — Display available commands
   - Acceptance: Each command executes its action without sending to BFF AI.

9. **FR-09**: Playbook switching from menu — User can switch active playbook from the slash command menu under "Switch Assistant" category. Switching updates system prompt, registered tools, available commands, and quick-action chips. Uses existing `useChatPlaybooks` hook + `switchContext`. Acceptance: Switching playbook changes the entire capability set within the same chat session.

10. **FR-10**: Natural language routing — For messages without slash commands, the BFF AI model selects appropriate tools based on: enriched context signals (FR-05), playbook-scoped tool registrations, and entity metadata. Single-step actions execute immediately; multi-step detected for plan preview (Phase 3). Acceptance: "Summarize this document" routes to SummaryGenerator tool without user specifying a command.

#### Phase 2: Quick-Action Chips

11. **FR-11**: Quick-action chip bar — Contextual chips MUST display above the input area, populated from:
    1. Active playbook capabilities (top 2-3 from `sprk_playbookcapabilities`)
    2. Analysis-type-aware actions (e.g., NDA analysis → "Refine Analysis", "Compare to Standard")
    - Maximum 4 chips to avoid visual clutter
    - Hidden when pane width < 350px
    - Chips update dynamically on playbook switch or context change
    - Tapping a chip sends a structured message with `source: "chip_click"`
    - Acceptance: Chips change appropriately across different analysis types.

#### Phase 3: Compound Actions + Plan Preview

12. **FR-12**: Plan preview for compound actions — Any action requiring 2+ tool calls MUST show a numbered step plan before executing. User actions: [Proceed], [Edit plan], [Cancel]. "Edit plan" allows conversational modification ("skip step 2", "also include the contract deadline"). Any data-modifying or email-sending action MUST require plan preview regardless of step count. Acceptance: User can review, modify, and approve multi-step plans before execution.

13. **FR-13**: Email drafting and sending — Email compound action flow:
    1. AI generates email draft (recipient, subject, body) using matter/analysis context
    2. Draft shown as email preview in chat
    3. User can refine conversationally ("make it more formal", "add the deadline")
    4. User approves → launches email Code Page modal (existing `sprk_communication` module pattern) OR sends via BFF → Graph API through `sprk_communication` services
    - Acceptance: User can draft, refine, and send email entirely within SprkChat + email modal flow.

14. **FR-14**: Write-back with confirmation — Chat can update Dataverse records (e.g., correct an analysis finding) via `write_back` playbook capability. All write-backs MUST show before/after preview and require explicit user confirmation. Acceptance: "The indemnification cap is $5M not $2M" → shows diff → user confirms → record updated.

15. **FR-15**: Progress indicators — Multi-step compound actions MUST show progress status for each step (pending, in-progress, complete, failed). Acceptance: User sees real-time step progress during execution.

#### Phase 4: Prompt Templates + Playbook Library

16. **FR-16**: Parameterized prompt templates — Pre-built prompt templates stored in Dataverse (extending playbook configuration) with fill-in-the-blank parameters: e.g., "Draft a {type} to {recipient} about {topic}". User selects template from slash menu or chip → fills parameters → executes. Acceptance: Admin can create new prompt templates in Dataverse; they appear in SprkChat within 5 minutes (context mapping cache TTL).

17. **FR-17**: Playbook library browser in chat — Browsable playbook catalog accessible via `/playbooks` command. Shows playbook name, description, capabilities, and recommended use cases. "Try this playbook" activates the playbook in current session. Acceptance: User can discover and activate any playbook they have access to without leaving chat.

### Non-Functional Requirements

- **NFR-01**: Slash menu MUST open within 100ms of `/` keypress.
- **NFR-02**: Playbook switch MUST complete (new tools registered, chips updated) within 1 second.
- **NFR-03**: Quick-action chips MUST update within 500ms of context change.
- **NFR-04**: Side pane close on navigation MUST execute within the 2-second poll interval.
- **NFR-05**: All new components MUST support full keyboard navigation and screen reader labels (WCAG 2.1 AA).
- **NFR-06**: All new components MUST support dark mode via Fluent v9 semantic tokens.
- **NFR-07**: Context enrichment payload MUST add < 1KB to each message.

---

## Technical Constraints

### Applicable ADRs

| ADR | Relevance |
|-----|-----------|
| **ADR-001** | BFF API endpoints use Minimal API pattern; no separate microservices |
| **ADR-006** | SprkChat is a React Code Page (React 18+, bundled); NOT a PCF control |
| **ADR-008** | Endpoint filters for AI endpoint authorization; no global middleware |
| **ADR-012** | New SprkChat components go in `@spaarke/ui-components`; callback-based props; zero service dependencies in shared components |
| **ADR-013** | AI features extend BFF (not separate service); flow ChatHostContext through pipeline; use RagSearchOptions for knowledge scoping |
| **ADR-021** | Fluent v9 exclusively; semantic tokens for colors; dark mode required; FluentProvider wraps all UI |

### MUST Rules

- MUST use Fluent v9 components for SlashCommandMenu (Popover, MenuList, Input)
- MUST place new shared components in `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/`
- MUST use callback-based props in shared components (no direct Xrm or BFF service dependencies)
- MUST flow enriched context through existing `ChatHostContext` pipeline in BFF
- MUST NOT create legacy JavaScript webresources for slash command handling
- MUST NOT call Azure AI services directly from the client (all AI goes through BFF)
- MUST NOT use Fluent v8, hard-coded colors, or alternative UI libraries
- MUST NOT create global auth middleware for new endpoints

### Existing Patterns to Follow

- See `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChatSuggestions.tsx` for chip-style component pattern
- See `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChatInput.tsx` for input interception pattern
- See `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/PlanPreviewCard.tsx` for plan preview pattern
- See `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/QuickActionChips.tsx` for existing chip component
- See `src/client/code-pages/SprkChatPane/src/services/contextService.ts` for context detection + polling pattern
- See `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookChatContextProvider.cs` for system prompt + tool registration pattern

---

## Success Criteria

1. [ ] **Scope enforced**: SprkChat does NOT appear on Corporate Workspace or any non-analysis page — Verify: navigate to Corporate Workspace, confirm no SprkChat icon in side pane rail
2. [ ] **Side pane lifecycle**: Navigating away from Analysis Workspace closes SprkChat pane; returning reopens with previous session — Verify: navigate away and back, confirm session restored
3. [ ] **Natural language works**: "Summarize this document" routes to SummaryGenerator tool without slash command — Verify: type natural language request, confirm correct tool invocation
4. [ ] **Slash menu is dynamic**: Available commands change when playbook switches — Verify: switch playbook via `/switch`, confirm command list updates
5. [ ] **Context enrichment improves routing**: Editor selection, document type, and conversation phase included in BFF payload — Verify: inspect BFF request payload with browser DevTools
6. [ ] **Compound actions show plan**: "Summarize and email counsel" shows plan preview before executing — Verify: request multi-step action, confirm plan card appears with Proceed/Cancel
7. [ ] **Email works end-to-end**: Draft → refine → send via `sprk_communication` module or email Code Page modal — Verify: complete email flow from chat

---

## Dependencies

### Prerequisites

- **Project #1 (Context Awareness)**: COMPLETE — context mappings, page type detection, entity metadata enrichment
- **SprkChat Workspace Companion**: COMPLETE — analysis workspace integration, inline toolbar, side pane launch
- **SprkChat Platform Enhancement R2**: COMPLETE — markdown rendering, SSE streaming, playbook dispatch, web search, document upload

### Existing Infrastructure (Reused)

| Component | Location | Used For |
|---|---|---|
| `SprkChatInput` | `@spaarke/ui-components` | Input interception for `/` trigger |
| `QuickActionChips` | `@spaarke/ui-components` | Existing chip component to enhance |
| `PlanPreviewCard` | `@spaarke/ui-components` | Existing plan preview to enhance |
| `ActionConfirmationDialog` | `@spaarke/ui-components` | Write-back confirmation |
| `useChatPlaybooks` hook | `SprkChat/hooks/` | Playbook switching |
| `contextService.ts` | `SprkChatPane/src/services/` | Context detection + polling (2s interval) |
| `SprkChatBridge` | `@spaarke/ui-components/services/` | Cross-pane communication |
| `PlaybookChatContextProvider` | `Sprk.Bff.Api/Services/Ai/Chat/` | System prompt + tool registration |
| `PlaybookDispatcher` | `Sprk.Bff.Api/Services/Ai/Chat/` | Semantic matching for natural language → playbook |
| `sprk_communication` module | Dataverse + BFF services | Email composition and sending via Graph API |
| `sessionStorage` persistence | `SprkChatPane/App.tsx` | Session restore on pane reopen |

### External Dependencies

- None new — all capabilities build on existing BFF API, Azure OpenAI, playbook engine, and Dataverse infrastructure

---

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Smart routing | Server-side only vs client-side augmentation? | **Hybrid**: Client enriches messages with structured context signals (editor selection, document type, conversation phase, available actions); BFF AI model still makes routing decision with richer input | Client sends enriched payload; BFF system prompt incorporates signals; no separate classifier model needed |
| Ribbon button | Remove entirely or scope to analysis forms? | **Remove entirely** — SprkChat launches only from AnalysisWorkspace Code Page | Remove `sprk_application_ribbon_sprkchat.xml` from solution; no enable rule changes needed |
| Email sending | Dataverse email activity or BFF Graph API? | **BFF → Graph API via `sprk_communication` module**; can also launch the email Code Page HTML modal (same pattern as Create Email wizard) | Reuse existing `sprk_communication` services; add email draft tool to playbook capabilities |
| Context actions table | New `sprk_aichatcontextaction` Dataverse table? | **Dropped** — playbook capabilities multi-select provides sufficient dynamic actions without new infrastructure | No new Dataverse table; dynamic actions driven by existing `sprk_playbookcapabilities` |
| Prompt templates | Hardcoded or Dataverse-stored? | **Dataverse-stored** — extending playbook configuration | New fields on existing playbook entity or related table for template definitions |
| Rich response cards | Custom React or JSON schema? | **Not required at this stage** — deferred from scope | No rich card rendering work; existing markdown + citation rendering sufficient |
| Auto-reopen on return | Auto-reopen with previous session? | **Yes** — existing `sessionStorage` persistence supports this | Verify existing code handles the close→navigate→return→reopen cycle |

---

## Assumptions

- **Conversation phase detection**: Assuming simple heuristic (first message = "initial", message after AI response = "follow_up", message referencing prior output = "refinement") rather than ML-based classification. Will affect context enrichment accuracy.
- **Playbook capabilities as chip source**: Assuming the existing `sprk_playbookcapabilities` multi-select field provides sufficient granularity for quick-action chip population. If more specific chip labels/icons are needed, may require additional metadata on playbook entity.
- **Email Code Page modal**: Assuming the existing email composition Code Page (`sprk_communication` pattern) can be launched from within SprkChat's side pane context. May need URL param adjustments for side-pane-to-dialog launch.
- **Context poll interval**: Assuming the existing 2-second poll interval in `contextService.ts` is acceptable for navigation detection. Faster detection would require a different approach (MutationObserver on Xrm shell, or `beforeunload` events).

---

## Unresolved Questions

- [ ] **Slash command namespace collisions**: If two playbooks define overlapping capabilities (e.g., both have `search`), how are slash commands differentiated? Options: prefix with playbook name, or merge into single command. — Blocks: FR-06 dynamic command registry design
- [ ] **Compound action rollback**: If step 3 of a 4-step compound action fails, should completed steps be rolled back? Or preserved with partial results shown? — Blocks: FR-12 plan preview error handling
- [ ] **Email recipient resolution**: How does SprkChat resolve "outside counsel" to a specific contact? From matter party/role relationships in Dataverse? — Blocks: FR-13 email drafting
- [ ] **Template parameter types**: For parameterized prompts (FR-16), what parameter types are needed? Free text only, or also lookups (e.g., recipient from contact list)? — Blocks: Phase 4 template design

---

*AI-optimized specification. Original design: `projects/ai-sprk-chat-extensibility-r1/design.md`*
