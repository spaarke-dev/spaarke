# Plan — SpaarkeAi Welcome State Redesign

> **Target**: First-load / welcome state of the SpaarkeAi three-pane code page (Assistant + Workspace + Context).
> **Project home**: `projects/spaarke-ai-platform-unification-r3/`
> **Source**: User feedback session 2026-05-20 reviewing the deployed dev welcome state ([screenshot reference](#)).
> **Approach**: Reuse existing primitives from `LegalWorkspace`, `@spaarke/ui-components`, and `WorkspaceLayoutWizard` wherever possible — minimize net-new code.
> **Date**: 2026-05-20 (revised 2026-05-20 — §F interpretations resolved)

---

## Strategic context

This project implements **Moment 1: Arrival** from the Spaarke UI/UX strategy ([`projects/spaarke-UI-UX-strategy-plan/patterns-to-build-final.md`](../spaarke-UI-UX-strategy-plan/patterns-to-build-final.md), §4.1). The strategy defines four surfaces — Operational, Assistant, Workspace, Context — and Arrival is the welcome state inside the three Spaarke AI surfaces (Assistant + Workspace + Context). Direct quote from §4.1:

> "User opens Spaarke. They haven't been in the app since Friday. They want to know what they're walking into… The Workspace surface is filled with the user's chosen initial content — by default, the Daily Briefing widget. Assistant shows a welcome state with suggested prompts. Context surfaces ambient information appropriate to the user's recent activity, possibly including a Playbook gallery."

The strategy lists four components required for this moment (strategy §4.1 table):

| Strategy component | Surface | R2 status | This project |
|---|---|---|---|
| **DailyBriefingWidget** | Workspace (default initial content) | Missing widget; backend endpoints exist (`/api/ai/daily-briefing/*`) | Build as new LegalWorkspace section + register in shared section registry (§C-4) |
| **AssistantWelcomeState** | Assistant | Present as `WelcomePanel` with 4 hardcoded prompt cards | Reduce chrome to just "How can I help you today?"; activate input on load (§B) |
| **PlaybookGallery** | Context | Present as `PlaybookGalleryWidget` (Choose a Playbook card) | Replace welcome rendering with `GetStartedCardsWidget` (7 action cards); keep `PlaybookGalleryWidget` available for non-welcome stages (§D) |
| **SessionRestore** | Cross-cutting | Present (D-08 data-refreshed restore <500ms p95) | No change — leverage existing |

The strategy also articulates the **three-pane triangle** relationship (strategy §1.4): *"any pane can drive the others… Assistant orchestrates AI interactions, Workspace holds the active work, Context holds what's known and referenceable about the active work."* The Workspace-pane menu (§C-2) and the Context-pane card → Workspace-tab routing (§D-2) make that triangle concrete for the welcome state.

One open decision in strategy §7.2 is directly relevant: *"The orchestrator-stance commitment — Assistant is the front door, not the destination. Structured answers route to Workspace; chat does not become the place answers live."* The Workspace-pane "tabs in dropdown" UX (§C-2) and the routing of all Context-card clicks to Workspace tabs (§D-2) operationalize this commitment.

## Context (what's wrong today)

SpaarkeAi (the three-pane code page) shipped in R2 with a welcome-state layout that doesn't match the strategy:

- Each pane has a different header pattern (tab buttons on Assistant, no header on Workspace, icon+title+stage-label on Context) — no parallel structure.
- The Assistant pane shows a "Welcome to Spaarke AI" panel with 4 hardcoded prompt cards, while the same panel renders the playbook gallery in the Context pane — splitting attention.
- The Workspace pane shows a static "What would you like to work on?" landing widget rather than the personalized, widget-driven workspace the user already experiences in the standalone `LegalWorkspace` code page.
- The Context pane shows a single "Choose a Playbook" card, when the user wants actionable Get-Started cards that launch wizards directly.

This project reshapes the welcome state of all three panes to (a) present a parallel/aligned pane-header convention, (b) move primary actions into the Context pane as Get-Started cards, (c) embed the existing LegalWorkspace experience into the Workspace pane (with its layout wizard, sections, and workspace switcher), and (d) clean up Assistant-pane chrome while activating the conversation input immediately on load.

The intended outcome: a welcome state where the three panes work as a coherent triple — Assistant is the conversational front door, Workspace is the user's personalized work surface (just like the standalone LegalWorkspace), and Context is the launch surface for guided actions.

## Personas and business value

**Primary personas** (per strategy §3.1 persona-aware defaults):
- **Legal Operations Manager** — manages matters, projects, work assignments at portfolio level. Wants the welcome state to surface what changed since last login (Daily Briefing) and what guided flows to launch (Get-Started cards).
- **In-house counsel** — focused work on individual matters/documents. Wants to type a question immediately on arrival, see recent work to resume, and launch playbooks quickly.

**Business value**:
- **First-30-seconds product experience** (strategy §3.2: *"the home page setting determines the user's first 30 seconds in the product every day"*). The welcome state is the entry point most users see every day; aligning it to the strategy compounds.
- **Reusable foundation** for stages 2-4 (active chat, review, complete) — the unified `<PaneHeader>` primitive and tabs-in-dropdown UX become base patterns the rest of the lifecycle inherits.
- **Reduced duplication** — the standalone LegalWorkspace and SpaarkeAi's Workspace pane share section registry, layout wizard, and persistence endpoint. Daily Briefing built here is available in both surfaces.

---

## Scope

**In scope** — welcome state (first load, no session, no entity context):
- Unified pane-header convention across all three panes
- Assistant pane: chrome cleanup, input activation, file-add affordance, toolbar move
- Workspace pane: embed LegalWorkspace, add workspace-switcher + tabs dropdown, add Daily Briefing section
- Context pane: replace `PlaybookGalleryWidget` with `GetStartedCardsWidget` (7 cards, 2-col scrollable, launch wizards)

**Out of scope** (deferred):
- Full SharePoint Embedded file-upload integration from the Assistant `+` button (button + picker only)
- Stages 2-4 lifecycle UI (active chat, review, complete) — only welcome-stage changes here
- New cross-cutting conventions from the strategy doc (AI-vs-User distinction, AI reasoning surface, etc.) — separate Phase A work
- ADRs for PaneEventBus and stage lifecycle patterns

---

## Pane-by-pane changes

### A. Cross-cutting — Unified pane header

**Today** — three different header patterns:
- Assistant: `"Chat" | "History"` tab buttons (no title block) — [`ConversationPane.tsx:736-778`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx)
- Workspace: no header (renders `WorkspaceLandingWidget` directly on welcome)
- Context: `<icon> "Context" <stageLabel>` — [`ContextPaneController.tsx:691-700`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx)

**Target** — extract Context's header pattern into a shared `<PaneHeader>` primitive used by all three panes.

| Pane | Icon | Title | Right-corner action |
|---|---|---|---|
| Assistant | `ChatRegular` | "Assistant" | History trigger — `HistoryRegular` icon button opening a right-side overlay (see B-2) |
| Workspace | `AppsListRegular` | "Workspace" | Workspace switcher + tabs dropdown (see §C) |
| Context | `DocumentRegular` (existing) | "Context" | Existing stage label retained ("Get Started" on welcome — see D-3) |

**Implementation**:
- **New shared component**: `PaneHeader` in `src/client/shared/Spaarke.UI.Components/src/components/PaneHeader/PaneHeader.tsx` — props: `title`, `icon?`, `rightSlot?` (any React node for the right-corner area).
- Migrate `ContextPaneController` to use `<PaneHeader>` (preserves existing visual style — that style becomes the canonical one).
- Apply `<PaneHeader>` to `ConversationPane` and `WorkspacePane`. Remove the existing tab buttons in `ConversationPane`.
- Title typography: `Text size={300}` semibold, left-aligned — matches the Context pane today.
- Icon styling: `colorBrandForeground1` (matches `headerIcon` in ContextPaneController today).
- Pane background, borders, padding: lift the existing Context-pane header styles ([`ContextPaneController.tsx:142-171`](../../../src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx)) into the shared component.

### B. Assistant pane changes

**Files**:
- [`src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx)
- [`src/solutions/SpaarkeAi/src/components/WelcomePanel.tsx`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/SpaarkeAi/src/components/WelcomePanel.tsx)
- [`src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx)
- [`src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChatExportWord.tsx`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChatExportWord.tsx)

| # | Change | Where |
|---|---|---|
| B-1 | Add `<PaneHeader title="Assistant">` at top; remove the existing `"Chat" \| "History"` tab buttons | `ConversationPane.tsx` |
| B-2 | Wire History into the pane header's right-slot — `HistoryRegular` icon button opens a **right-side overlay** listing past conversations (Claude-Code-style session list). Selecting an item **replaces the current conversation** by calling `setChatSessionId(sessionId)` to load that session's history into SprkChat. Overlay closes on selection. Reuse the existing recent-sessions data API (`GET /api/ai/chat/sessions?limit=N`) with a higher `limit` (e.g., 50) and infinite scroll or pagination. | `ConversationPane.tsx` + `PaneHeader` rightSlot + new `HistoryOverlay.tsx` |
| B-3 | Remove the sparkle icon + "Welcome to Spaarke AI" heading. Keep ONLY "How can I help you today?" (move to top of `WelcomePanel`, smaller — `size={400}` instead of `500`, no icon) | `WelcomePanel.tsx:476-492` |
| B-4 | Remove the 4 hardcoded prompt cards (`Analyze a Document`, `Research a Topic`, `Financial Analysis`, `Find Documents`) — these are conceptually replaced by the Get-Started cards in the Context pane | `WelcomePanel.tsx:496-506` |
| B-5 | Keep "Recent Conversations" section in `WelcomePanel` (already paginated to last 5 via `/ai/chat/sessions?limit=5`) | `WelcomePanel.tsx:510-515` |
| B-6 | Activate `SprkChatInput` on load — change `disabled={isStreaming \|\| !session \|\| isSessionLoading}` so that absence of a session no longer disables the input. Input still needs a session before send, so handle session-creation lazily on first submit. | `SprkChat.tsx:2089-2094` |
| B-7 | Add a visible file-attach `+` button (icon `AttachRegular`) above the input (next to the prompt menu, see B-9). On click: opens native OS file picker; selected file's text content is **extracted client-side** (PDF.js for PDFs, raw text for `.txt`/`.md`, `.docx` via `mammoth`) and **attached to the next user message as context** so the Assistant can read/review it. **This is NOT an SPE document upload** — no Document entity is created in Dataverse. The file content is passed through the chat session-message body (or an in-memory attachment slot on the SprkChat session state). Existing `WorkingDocumentService` / SPE upload path is unchanged. | `SprkChat.tsx` + new `useChatFileAttachment` hook |
| B-8 | Remove the "Open in Word" / `SprkChatExportWord` button from the input toolbar | `SprkChat.tsx:2076-2086` |
| B-9 | Move the prompt menu (currently inline near input) and the new `+` button to a strip ABOVE the conversation input box. Layout: `[ Prompt menu ▾ ]   [ + Attach ]` in a single horizontal row, with Fluent v9 tokens (`spacingHorizontalS` between items). | `SprkChat.tsx` |

**Note on B-6**: activating the input on load means the user can type immediately, but the BFF session is not created until first send. `useChatSession` in SprkChat already supports this pattern via the function-based auth contract — the first send creates a session, then subsequent sends reuse it.

### C. Workspace pane changes

**Files**:
- [`src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx)
- [`src/solutions/SpaarkeAi/src/components/workspace/WorkspaceLandingWidget.tsx`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/SpaarkeAi/src/components/workspace/WorkspaceLandingWidget.tsx)
- [`src/solutions/SpaarkeAi/src/components/workspace/WorkspaceTabManager.ts`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/SpaarkeAi/src/components/workspace/WorkspaceTabManager.ts)
- [`src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/)
- [`src/solutions/LegalWorkspace/src/sectionRegistry.ts`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/LegalWorkspace/src/sectionRegistry.ts)

#### C-1. Embed the LegalWorkspace experience as the default ("Home") tab

- Delete `WorkspaceLandingWidget` (and its usage in `WorkspacePane.tsx:48,327-329`).
- Reuse the `WorkspaceShell` shared component to render LegalWorkspace's sections inside the Workspace pane. Use the same `useWorkspaceLayouts` hook ([`LegalWorkspace/src/hooks/useWorkspaceLayouts.ts`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/LegalWorkspace/src/hooks/useWorkspaceLayouts.ts)) — the BFF endpoint `/api/workspace/layouts/default` already serves the user's default layout.
- The active workspace layout becomes the "Home" tab — the LegalWorkspace renders inside it.

#### C-2. Tabs-in-dropdown UX (per user direction)

**Replace the existing tab bar** ([`WorkspaceTabManagerComponent`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/SpaarkeAi/src/components/workspace/WorkspaceTabManagerComponent.tsx)) with a **dropdown menu** in the pane header.

**Behaviour**:
- The dropdown is in the right corner of the Workspace pane header (Fluent v9 `Dropdown` or `Menu`).
- On load: dropdown shows one entry — the active LegalWorkspace layout name (e.g., "Corporate Workspace"), pinned as the bottom item.
- When a new widget arrives (Assistant route, Context-card wizard, etc.), it becomes the new top item in the dropdown and is auto-activated.
- A third widget pushes the second down. Each non-Home item has a close `×`.
- The Home entry (LegalWorkspace) cannot be closed.
- The dropdown's selected value = the currently active tab; switching the dropdown selection switches the active pane content.

**Combined "workspace switcher + tabs" menu** — structure:

```
Workspace pane header right-corner dropdown:
  ┌─────────────────────────────────────┐
  │ Open                                │   ← section label
  │   📄 Document.pdf            ×      │   ← top = newest
  │   ⊞ Budget Dashboard         ×      │
  │ ──────────────                      │
  │ Home                                │
  │   ⌂ Corporate Workspace      ✓      │   ← current
  │ ──────────────                      │
  │ Switch Workspace                    │   ← OptionGroup
  │   Corporate Workspace               │
  │   Trial Prep Workspace              │
  │   + New Workspace                   │
  │ ──────────────                      │
  │   ⚙ Edit current workspace          │
  └─────────────────────────────────────┘
```

Selecting an item in "Switch Workspace" changes the Home tab's underlying layout (calls `useWorkspaceLayouts.setActive`). Selecting an "Open" item activates that tab. "+ New Workspace" launches the existing `WorkspaceLayoutWizard`.

**Implementation**:
- Extend `WorkspaceTabManager` to model a `home` entry that's always present and non-closable. The existing 3-tab `MAX_TABS` constant (in `WorkspaceTabManager.ts`) needs to be raised — recommend `8` for non-home tabs (so 9 total visible items including Home). Make this configurable.
- New component: `WorkspacePaneMenu.tsx` rendering the combined dropdown. Uses Fluent `Dropdown` + `OptionGroup` per the structure above. Lifts the workspace-list logic from [`LegalWorkspace/src/components/Shell/PageHeader.tsx`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/LegalWorkspace/src/components/Shell/PageHeader.tsx).
- `WorkspacePane.tsx` renders `<PaneHeader title="Workspace" rightSlot={<WorkspacePaneMenu .../>}>` followed by either the active tab's content or the embedded LegalWorkspace.

#### C-3. Layout templates restricted to ≤2 columns

The Layout Wizard ([`src/solutions/WorkspaceLayoutWizard/src/steps/TemplateStep.tsx`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/WorkspaceLayoutWizard/src/steps/TemplateStep.tsx)) currently shows all 9 templates from [`layoutTemplates.ts`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/layoutTemplates.ts).

When launched from inside SpaarkeAi's Workspace pane, filter to the ≤2-col templates:
- `2-col-equal` ✓
- `3-row-mixed` ✓ (2-col rows with a 1-col middle)
- `hero-2x2` ✓
- `sidebar-main` ✓ (1:2 ratio = 2 columns)
- `single-column` ✓
- `single-column-5` ✓

Exclude: `3-col-equal`, `3-col-3-row`, `hero-grid` (3-col bottom).

**Implementation**: add a `templateFilter?: LayoutTemplateId[]` prop to the wizard's `App.tsx` (or `TemplateStep`) and pass an allow-list from SpaarkeAi. The standalone LegalWorkspace continues to see all 9.

#### C-4. Daily Briefing as a new section type

The backend already has `POST /api/ai/daily-briefing/summarize` and `POST /api/ai/daily-briefing/narrate` ([`src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs)).

**Implementation**:
- Add new section: [`src/solutions/LegalWorkspace/src/sections/dailyBriefing/`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/LegalWorkspace/src/sections/) — `DailyBriefingSection.tsx` + `dailyBriefing.registration.ts`.
- Section renders a `Card` listing AI-curated bullets: open tasks, recent records, documents, notifications — by channel.
- Hook: new `useDailyBriefing()` calling `POST /api/ai/daily-briefing/narrate` via `authenticatedFetch` from `@spaarke/auth`.
- Register the section in [`sectionRegistry.ts`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/LegalWorkspace/src/sectionRegistry.ts) so it's selectable in the Layout Wizard's Step 2 (Section Selection).
- Default height: similar to `QuickSummary` (`defaultHeight: 'medium'`).

This section becomes available in BOTH the standalone LegalWorkspace and SpaarkeAi's Workspace pane (since they share the same registry).

### D. Context pane changes

**Files**:
- [`src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx)
- [`src/client/shared/Spaarke.AI.Widgets/src/registry/ContextWidgetRegistry.ts`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/client/shared/Spaarke.AI.Widgets/src/registry/ContextWidgetRegistry.ts)
- [`src/solutions/LegalWorkspace/src/components/GetStarted/`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/LegalWorkspace/src/components/GetStarted/)

#### D-1. Replace `PlaybookGalleryWidget` welcome content with Get-Started cards

- Create new context widget: `GetStartedCardsWidget` in `src/client/shared/Spaarke.AI.Widgets/src/widgets/context/GetStartedCardsWidget.tsx`.
- Render all 7 LegalWorkspace action cards (Create Matter, Create Project, Assign Work, Summarize Files, Find Similar, Send Email, Schedule Meeting) — config from [`getStartedConfig.ts`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/LegalWorkspace/src/components/GetStarted/getStartedConfig.ts).
- Display: **2-column scrollable grid** (Context pane is narrow). Use `gridTemplateColumns: '1fr 1fr'` with `gap: tokens.spacingHorizontalS`. Vertically scrollable when overflowing.
- Reuse `ActionCard` from [`src/solutions/LegalWorkspace/src/components/GetStarted/ActionCard.tsx`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/LegalWorkspace/src/components/GetStarted/ActionCard.tsx). May need to extract to shared library OR copy the component into AI.Widgets — depends on whether `ActionCard` can be made context-agnostic per ADR-012 (likely yes; only depends on Fluent + an `onClick`).

#### D-2. Card click launches wizard in Workspace pane

- On card click, reuse the existing `OpenWizard` pattern from [`ActionCardHandlers.ts`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/LegalWorkspace/src/components/GetStarted/ActionCardHandlers.ts).
- Dispatch a `widget_load` event on the `workspace` PaneEventBus channel with the appropriate widget type:

| Card | Widget type dispatched | Existing widget |
|---|---|---|
| Create Matter | `create-matter-wizard` | `CreateMatterWizardWidget` (already in `@spaarke/ai-widgets`) ✓ |
| Create Project | `create-project-wizard` | Need new wrapper around existing `CreateProjectWizardDialog` |
| Document Upload | `document-upload-wizard` | `DocumentUploadWizardWidget` ✓ |
| Find Similar | `find-similar-wizard` | Need new wrapper around `FindSimilarWizardDialog` |
| Send Email | `email-compose` | Analysis Builder intent — open in tab |
| Schedule Meeting | `meeting-schedule` | Analysis Builder intent — open in tab |
| Assign Work | `assign-work-wizard` | Launches Dataverse custom page `sprk_createworkassignmentwizard` (existing — confirmed by user via runtime log showing `[Spaarke.RuntimeConfig] Resolved: bffBaseUrl=... sprk_createworkassignmentwizard?data=...`). Open via `Xrm.Navigation.navigateTo({ pageType: 'custom', name: 'sprk_createworkassignmentwizard', data: 'bffBaseUrl=...' })`, OR — if used inside the pane rather than as a dialog — wrap it as a `WorkspaceWidget` that renders the custom page in an iframe-like host. **Prefer the dialog navigate** for parity with how LegalWorkspace launches it. |

For wizards that exist as Dialogs in LegalWorkspace but not as Workspace widgets, wrap them in WorkspaceWidget contracts (new files in `@spaarke/ai-widgets/src/widgets/workspace/`) and register in `WorkspaceWidgetRegistry.ts`.

#### D-3. Context pane header

- Continue using the existing pattern (now via shared `<PaneHeader>`).
- Stage label "Gallery" → rename to "Get Started" on welcome stage (string change in [`ContextPaneController.tsx`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx) `stageLabelMap`).
- Stage-to-widget mapping: `welcome → get-started-cards` (replaces `welcome → playbook-gallery`).

#### D-4. PlaybookGalleryWidget stays in registry

- Don't delete `PlaybookGalleryWidget` — it's still useful when a session is active and the user wants to switch playbooks. Keep it registered as a non-welcome-stage widget for invocation via `context_update` events.

---

## E. Reusable primitives — net-new files

| File | Purpose | Size estimate |
|---|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/PaneHeader/PaneHeader.tsx` | Shared 3-pane header convention (title + icon + rightSlot) | ~80 lines |
| `src/client/shared/Spaarke.UI.Components/src/components/PaneHeader/index.ts` | Barrel export | 5 lines |
| `src/solutions/SpaarkeAi/src/components/conversation/HistoryOverlay.tsx` | Right-side overlay listing past conversations; selection replaces active session (F-1) | ~150 lines |
| `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useChatFileAttachment.ts` | Client-side file picker + text-extraction hook for `+` button; pushes extracted content into next message context (F-2) | ~120 lines |
| `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePaneMenu.tsx` | Right-corner workspace switcher + tabs dropdown | ~200 lines |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/context/GetStartedCardsWidget.tsx` | 7-card 2-col Get-Started widget for Context pane | ~150 lines |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/CreateProjectWizardWidget.tsx` | Workspace-widget wrapper around `CreateProjectWizardDialog` | ~80 lines |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/FindSimilarWizardWidget.tsx` | Same for FindSimilar | ~80 lines |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/AssignWorkWizardLauncher.ts` | Thin dispatcher that calls `Xrm.Navigation.navigateTo({ pageType: 'custom', name: 'sprk_createworkassignmentwizard' })` — F-5 | ~40 lines |
| `src/solutions/LegalWorkspace/src/sections/dailyBriefing/DailyBriefingSection.tsx` | New LegalWorkspace section | ~150 lines |
| `src/solutions/LegalWorkspace/src/sections/dailyBriefing/dailyBriefing.registration.ts` | Section registration | ~30 lines |
| `src/solutions/LegalWorkspace/src/sections/dailyBriefing/useDailyBriefing.ts` | Hook calling BFF | ~80 lines |

Modified files: ~10 (per the per-pane tables above).

**Possibly modified (depends on F-2 backend support)**: `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` — `POST /api/ai/chat/sessions/{sessionId}/messages` may need to accept an optional `attachments: [{ filename, contentType, textContent }]` array if the existing payload shape doesn't already support inline file context. Confirm during the F-2 implementation pass; if backend changes are needed, they become a sub-task.

---

## F. Resolved interpretations (2026-05-20)

| # | Question | Resolution |
|---|---|---|
| F-1 | History trigger UX (B-2) | **Side overlay** that lists past conversations (Claude-Code-style); selecting an item **replaces the current conversation** via `setChatSessionId(sessionId)`. Reuse `/api/ai/chat/sessions` API. Closes on selection. |
| F-2 | `+` file picker (B-7) | File picker uploads files for the Assistant to **read/review** — extracted text content is attached to the next chat message as context. Does **NOT** create an SPE Document entity. Must feed into the sprk-ai process (i.e. the AI agent receives the file content as part of the next turn's input). |
| F-3 | `MAX_TABS` (C-2) | **8 non-home tabs (configurable)** → 9 visible slots including Home. Constant exposed as `MAX_WORKSPACE_TABS` in `WorkspaceTabManager.ts`. |
| F-4 | Component reuse | **Reuse whenever possible.** `ActionCard` extracted to `@spaarke/ui-components` if it has LegalWorkspace-only deps; otherwise import directly. Same principle for `useWorkspaceLayouts`, `WorkspaceShell`, `WorkspaceLayoutWizard`. |
| F-5 | "Assign Work" card mapping (D-2) | Exists as Dataverse custom page **`sprk_createworkassignmentwizard`** (confirmed via runtime console log). Launch via `Xrm.Navigation.navigateTo` as a dialog. |
| F-6 | Pane-header icons (§A) | **Add per pane**: Assistant = `ChatRegular`, Workspace = `AppsListRegular`, Context = `DocumentRegular` (unchanged). All use `colorBrandForeground1`. |

All F items are folded into §A–§D above; this table is the authoritative record of the decisions.

---

## G. Verification

End-to-end smoke after deploy to dev:

1. **Pane headers parallel** — open `https://orgname.crm.dynamics.com/.../spaarkeai.html` (or local Vite dev), confirm all three panes show: icon (left), left-aligned title, right-corner action, identical typography/spacing.
2. **Pane icons** — Assistant shows `ChatRegular`, Workspace shows `AppsListRegular`, Context shows `DocumentRegular` (all `colorBrandForeground1`).
3. **Assistant input active on load** — type into the conversation box before clicking anything; first send creates a session via BFF.
4. **Assistant chrome** — confirm absence of "Welcome to Spaarke AI" heading, 4 prompt cards, and "Open in Word" button; confirm presence of `+` and prompt-menu strip above input.
5. **History overlay** — click the `HistoryRegular` icon in the Assistant pane header → right-side overlay opens listing past sessions; click any session → overlay closes and the conversation replaces the current one (history loads into SprkChat).
6. **File attach `+`** — click `+`, pick a text file (e.g. `.md` or `.txt`); type a question that references the file; send. The Assistant's reply should reflect knowledge of the file content (i.e. file content was attached to the message context, NOT uploaded to SPE).
7. **Workspace = LegalWorkspace embed** — confirm the user's default workspace layout (`GET /api/workspace/layouts/default`) renders inside the Workspace pane on load.
8. **Workspace dropdown menu** — right-corner shows current workspace name; click reveals tabs section (just "Home" initially), workspace-switch section (all layouts), "+ New Workspace" launches the existing wizard with the 6 filtered templates.
9. **Daily Briefing section** — in the Layout Wizard, Step 2 (Section Selection) now includes Daily Briefing as an option; selecting it and saving causes it to render in the pane.
10. **Context pane Get-Started cards** — confirm 7 cards in 2-col scrollable grid, "Get Started" stage label, click on Create Matter opens `create-matter-wizard` in the Workspace pane (which becomes a new tab "on top" of Home in the dropdown).
11. **Assign Work card** — click → `sprk_createworkassignmentwizard` opens via `Xrm.Navigation.navigateTo` (dialog).
12. **MAX_TABS** — open 9 widgets in succession; 9th eviction matches FIFO from oldest non-Home tab; Home is never evicted.
13. **Tab close** — close a non-Home tab; Home (LegalWorkspace) becomes active again; tab gone from dropdown.
14. **Backwards compatibility** — open the standalone LegalWorkspace code page in another browser tab; confirm it still shows all 9 layout templates in its own wizard and still works identically.

Unit tests:
- `PaneHeader.test.tsx` — basic render + rightSlot
- `WorkspacePaneMenu.test.tsx` — tab insertion at top, close behaviour, Home pinning
- `GetStartedCardsWidget.test.tsx` — 7 cards render, click dispatches correct widget_load event
- `useDailyBriefing.test.ts` — fetch mock + error handling

Manual a11y:
- Tab through Workspace pane: header dropdown reachable, all cards/buttons keyboard-navigable
- Screen-reader: `PaneHeader` title announces "Assistant / Workspace / Context"; tabs dropdown announces selected
- Dark-mode: confirm all new components use Fluent v9 tokens only (ADR-021)

---

## H. Out-of-scope follow-ups (track separately)

- Backend chat-message attachment payload shape (F-2) — if `POST /api/ai/chat/sessions/{sessionId}/messages` doesn't already accept inline attachments, that's a sub-task surfaced during build.
- Stages 2–4 (active chat / review / complete) header treatment
- ADR-025 PaneEventBus pattern + ADR-026 stage lifecycle pattern (lessons-learned candidates from R2)
- Cross-cutting AI-vs-User visual convention + AIReasoningSurface convention (Phase A foundations from strategy doc)
- Persistence of which non-Home tabs are open across session restore (D-08 already restores widget state; confirm coverage)
- File-attachment size cap + content-type allow-list policy (F-2) — needs a policy doc; defaulting to <10 MB and text/PDF/DOCX for v1.

---

## I. Critical files referenced

Frontend:
- [`src/solutions/SpaarkeAi/src/components/shell/ThreePaneShell.tsx`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/SpaarkeAi/src/components/shell/ThreePaneShell.tsx)
- [`src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx)
- [`src/solutions/SpaarkeAi/src/components/WelcomePanel.tsx`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/SpaarkeAi/src/components/WelcomePanel.tsx)
- [`src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx)
- [`src/solutions/SpaarkeAi/src/components/workspace/WorkspaceTabManager.ts`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/SpaarkeAi/src/components/workspace/WorkspaceTabManager.ts)
- [`src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx)
- [`src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx)
- [`src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/)
- [`src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/layoutTemplates.ts`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/layoutTemplates.ts)
- [`src/client/shared/Spaarke.AI.Widgets/src/registry/ContextWidgetRegistry.ts`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/client/shared/Spaarke.AI.Widgets/src/registry/ContextWidgetRegistry.ts)
- [`src/client/shared/Spaarke.AI.Widgets/src/registry/WorkspaceWidgetRegistry.ts`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/client/shared/Spaarke.AI.Widgets/src/registry/WorkspaceWidgetRegistry.ts)
- [`src/solutions/LegalWorkspace/src/components/Shell/PageHeader.tsx`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/LegalWorkspace/src/components/Shell/PageHeader.tsx)
- [`src/solutions/LegalWorkspace/src/components/GetStarted/`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/LegalWorkspace/src/components/GetStarted/)
- [`src/solutions/LegalWorkspace/src/sectionRegistry.ts`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/LegalWorkspace/src/sectionRegistry.ts)
- [`src/solutions/LegalWorkspace/src/hooks/useWorkspaceLayouts.ts`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/LegalWorkspace/src/hooks/useWorkspaceLayouts.ts)
- [`src/solutions/WorkspaceLayoutWizard/src/`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/solutions/WorkspaceLayoutWizard/src/)

Backend (existing — no changes required):
- [`src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs)
- [`src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceLayoutEndpoints.cs`](../../code_files/spaarke-wt-spaarke-ai-platform-unification-r2/src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceLayoutEndpoints.cs)

---

## J. Applicable ADRs

| ADR | Relevance to this project |
|---|---|
| [**ADR-006**](../../.claude/adr/ADR-006-pcf-over-webresources.md) | SpaarkeAi and LegalWorkspace are full-page surfaces, not PCFs. Reaffirms the architectural choice that all new code in this project goes into the existing code-page solutions. |
| [**ADR-008**](../../.claude/adr/ADR-008-endpoint-filters.md) | If §F-2 surfaces a need for a backend chat-attachment payload extension, the new/changed endpoint inherits the existing endpoint-filter pipeline (auth, rate limit, validation). |
| [**ADR-010**](../../.claude/adr/ADR-010-di-minimalism.md) | Any new BFF service (e.g. file-extraction helper if implemented server-side instead of client-side) is registered in an existing feature module — no new modules. |
| [**ADR-012**](../../.claude/adr/ADR-012-shared-components.md) | **Load-bearing.** `<PaneHeader>` lives in `@spaarke/ui-components`. `ActionCard` extracted to shared library if not already. All new context-agnostic components added to shared lib, not solution-local. |
| [**ADR-013**](../../.claude/adr/ADR-013-ai-architecture.md) | Daily Briefing and chat-attachment paths are AI features extending the BFF, not a new service. Aligns with the "single BFF" pattern. |
| [**ADR-014**](../../.claude/adr/ADR-014-ai-caching.md) | Daily Briefing response should be cached per-user with short TTL (e.g. 5min) to avoid hammering the LLM on tab switches. Hook respects the existing AI-caching layer. |
| [**ADR-016**](../../.claude/adr/ADR-016-ai-rate-limits.md) | Daily Briefing endpoint usage from welcome state is high-frequency. Must respect existing AI rate-limit filters; widget shows graceful degraded UI on 429. |
| [**ADR-021**](../../.claude/adr/ADR-021-fluent-design-system.md) | **Load-bearing.** All new components use Fluent v9 tokens only — no hardcoded colors, no Fluent v8. Dark-mode safe by construction. Verified in §G a11y checks. |
| [**ADR-022**](../../.claude/adr/ADR-022-pcf-platform-libraries.md) | React 19 used for all SpaarkeAi/LegalWorkspace code — no React 16 fallback. Affects bundling for PDF.js (F-2). |
| [**ADR-025**](../../.claude/adr/ADR-025-icon-library-and-deployment.md) | Icons used (`ChatRegular`, `AppsListRegular`, `DocumentRegular`, `HistoryRegular`, `AttachRegular`) come from Fluent v9 `@fluentui/react-icons`. No new SVG web resources required. |
| [**ADR-026**](../../.claude/adr/ADR-026-full-page-custom-page-standard.md) | SpaarkeAi and LegalWorkspace are full-page custom pages (Vite + React 19). All new shared components built per this standard. |
| [**ADR-028**](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) | **Load-bearing.** All BFF calls (recent sessions list, daily briefing, layouts, attachment-bearing chat messages) go through `authenticatedFetch` from `@spaarke/auth`. Function-based auth contract per INV-1..INV-8. No token snapshots in props or state. |

---

## K. Non-functional requirements

| ID | Requirement | Target / Acceptance |
|---|---|---|
| **NFR-01** | Pane render performance | First paint of each pane <500ms on cold load (excluding session-restore data fetch); shell-stage transition <100ms. |
| **NFR-02** | Session restore performance | Inherits R2 D-08 target: p95 <500ms from session selection to restored chat history visible (per [`AIPU2-106`](../spaarke-ai-platform-unification-r2/tasks/106-session-restore-e2e.poml)). |
| **NFR-03** | History overlay performance | Overlay open <200ms (animation only); session list populated <300ms p95 for 50 items. |
| **NFR-04** | File attachment (F-2) | Client-side text extraction <2s for files up to 10 MB; PDF max 200 pages or 10 MB whichever smaller; allowed MIME types: `text/plain`, `text/markdown`, `application/pdf`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document`. |
| **NFR-05** | Accessibility | WCAG 2.1 AA. All interactive elements keyboard-navigable. ARIA labels on every pane header, icon-only button, and dropdown trigger. Screen-reader announces active tab, active workspace, and stage transitions. |
| **NFR-06** | Dark mode | All new components use Fluent v9 tokens only (ADR-021). No hardcoded `#` colors, no `rgba(...)` literals. Verified by lint rule + manual side-by-side comparison. |
| **NFR-07** | Browser support | Microsoft Edge (Chromium) current + previous major; Chrome current + previous major. No IE11 work. |
| **NFR-08** | Auth | All BFF calls use `authenticatedFetch` per ADR-028; no token snapshotting; 401 auto-retry with silent MSAL refresh. |
| **NFR-09** | Persistence | Tabs (Home + non-Home) and active workspace layout survive page refresh via existing session-restore pipeline. New tabs persisted as part of session state (Cosmos write-through, R2 D-06). |
| **NFR-10** | Backward compatibility | The standalone LegalWorkspace code page continues to function identically — all 9 layout templates remain available there; existing user layouts (`/api/workspace/layouts`) unchanged. |
| **NFR-11** | Rate limit handling | Daily Briefing widget displays a graceful degraded card (with retry CTA) on 429 from `/api/ai/daily-briefing/narrate`. Does not block the rest of the workspace from rendering. |
| **NFR-12** | Bundle size | Daily Briefing widget + History overlay + file-extraction libs add <250 KB gzip to the SpaarkeAi bundle (file-extraction libs lazy-loaded on first `+` click — not in initial bundle). |

---

## L. Quantitative success metrics

| Metric | Target | Verification |
|---|---|---|
| **Pane header parity** | 3/3 panes use shared `<PaneHeader>` component | Code grep + visual diff at three viewport widths |
| **Welcome chrome reduction** | "Welcome to Spaarke AI" heading + 4 prompt cards + "Open in Word" button absent | Manual smoke (§G #4) |
| **Input activation** | Chat input editable on load (no session required) | Manual smoke (§G #3) |
| **Workspace embed** | Default workspace layout renders inside Workspace pane on load | `/api/workspace/layouts/default` returns 200 + UI matches standalone LegalWorkspace |
| **Tab capacity** | `MAX_WORKSPACE_TABS = 8` (non-Home), configurable | Unit test + manual smoke (§G #12) |
| **Get-Started cards** | All 7 cards present, 2-col scrollable, dispatch correct widget_load | Unit test + manual smoke (§G #10) |
| **Daily Briefing section** | Available in Layout Wizard Section Selection; renders content from `/api/ai/daily-briefing/narrate` | Manual smoke (§G #9) |
| **History overlay flow** | Click → overlay → select → conversation replaced | Manual smoke (§G #5) |
| **File attach flow** | Click `+` → pick file → text extracted → next AI reply references file content | Manual smoke (§G #6) |
| **Assign Work card** | Click → `sprk_createworkassignmentwizard` opens via Xrm | Manual smoke (§G #11) |

---

## M. Risks and mitigations

| # | Risk | Likelihood × Impact | Mitigation |
|---|---|---|---|
| R-1 | Backend `POST /api/ai/chat/sessions/{sessionId}/messages` does not accept inline attachments. F-2 needs a backend change. | Medium × Medium | First task in build is a 30-min spike to confirm the current payload shape. If extension needed, surface as a sub-task before the F-2 implementation starts. Backend change is small (add optional `attachments[]` field). |
| R-2 | PDF.js bundle size impacts first-load TTI even when lazy-loaded (chunk fetch on first `+` click). | Low × Low | Lazy-load via dynamic `import()`. Show file-attach button placeholder during chunk fetch. Measured against NFR-12. |
| R-3 | `Xrm.Navigation.navigateTo` is only available inside the Power Apps host. `sprk_createworkassignmentwizard` invocation fails in standalone Vite dev. | High × Low | Feature-detect `window.Xrm` and show a "Open in Spaarke" placeholder in non-host environments. Tested in both Vite dev and Power Apps host. |
| R-4 | The `templateFilter` prop added to `WorkspaceLayoutWizard` regresses the standalone LegalWorkspace if not defaulted to "all templates". | Medium × High | Default behaviour preserves existing standalone usage (no filter = show all 9). Regression test included in §G #14. |
| R-5 | `ActionCard` extraction from LegalWorkspace to `@spaarke/ui-components` accidentally couples LegalWorkspace-specific styles into the shared lib. | Medium × Medium | Inspect deps before extraction; if `ActionCard` references LegalWorkspace-only tokens, lift those into ADR-021 token mappings or refactor before extracting. Reviewed during code-review gate. |
| R-6 | Daily Briefing endpoint returns 429 (rate-limited) on welcome state for many concurrent users. | Medium × Medium | Cache per-user with 5min TTL (ADR-014). Show degraded card on 429 (NFR-11). Telemetry to Azure Monitor (R2 task 066). |
| R-7 | `MAX_WORKSPACE_TABS = 8` may still be too low for power users who keep many widgets open across a workday. | Low × Low | Configurable constant. Telemetry on tab-count distribution after rollout. Adjust in v1.x if data warrants. |
| R-8 | Combining workspace-switcher and tabs in one dropdown UX is unconventional — users may not discover the workspace-switch section. | Medium × Medium | Visible section dividers + labels ("Open", "Home", "Switch Workspace"). User testing with 2-3 personas before broad rollout. Fallback: split into two dropdowns if testing surfaces confusion. |
| R-9 | Session-restore (R2 D-08) currently restores widget state but may not include the tabs-list across reloads. | Medium × High | Confirm coverage in §G #9-13; if missing, extend `SessionPersistenceService` to include tabs array. Tracked as §H follow-up. |

---

## N. Out-of-scope follow-ups (cont. from §H)

The strategy doc enumerates components for moments beyond Arrival; the following are explicitly **not** in this project's scope but will inherit the foundations laid here:

- **Moment 2 (Triage)** — TriageQueueWidget, AIPrioritySignal, PortfolioBreakdownPanel, BulkOperationConfirmation (strategy §4.2)
- **Moment 3 (Deep work on an entity)** — EntityWorkspaceWidget (full editor with InlineFieldEditor, RelatedRecordsSection, ActivityTimeline, AIEntitySummary, AIFieldSuggestion), EntityIntelligencePanel (strategy §4.3)
- **Moment 4 (Document review)** — DocumentCanvas, SelectionDrivenCrossPane, InlineCitationMark, AIRedlineProposal, CompareMode, PlaybookReferencePanel, AIConcernFlag (strategy §4.4)
- **Cross-cutting conventions** (strategy §5.1) — AI-vs-User visual distinction (#1), AI working indicator (#3), AI reasoning surface (#5/#23) — partial implementations exist; full convention specs are Phase A foundations work
- **§7 open decisions** (strategy) — left-nav entries, orchestrator-stance commitment as ADR, personalization scope at launch, surface names, AI intelligence layer scope

---

*End of design.*
