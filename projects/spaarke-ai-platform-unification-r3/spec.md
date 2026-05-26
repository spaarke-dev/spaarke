# Spaarke AI Platform Unification R3 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-05-20
> **Source**: `design.md` (Welcome State Redesign — implements Moment 1: Arrival from Spaarke UI/UX strategy)
> **Predecessor**: `projects/spaarke-ai-platform-unification-r2/` (R2 shipped 86/86 tasks; this project extends R2's three-pane shell)

## Executive Summary

This project reshapes the welcome state of the SpaarkeAi three-pane code page (Assistant / Workspace / Context) to align with the Spaarke UI/UX strategy's "Moment 1: Arrival" specification. It introduces a unified pane-header primitive across all three panes, cleans up Assistant-pane chrome and activates the chat input on load, embeds the existing standalone LegalWorkspace experience as the Workspace pane's default "Home" tab (with a workspace switcher + tabs-in-dropdown menu), and replaces the Context pane's playbook gallery with seven Get-Started action cards that route wizards into the Workspace pane. The work establishes reusable foundations (`<PaneHeader>`, tabs-in-dropdown UX, Daily Briefing section) that subsequent stages (active chat, review, complete) inherit.

## Scope

### In Scope

- Unified `<PaneHeader>` shared component (icon + title + right-slot)
- Assistant pane: reduce welcome chrome to "How can I help you today?" only; activate input on load; replace Chat/History tabs with PaneHeader + right-side History overlay; add visible `+` file-attach button (multi-file up to 5); remove "Open in Word"; move prompt-menu + attach to strip above input
- Workspace pane: embed existing LegalWorkspace as default "Home" tab; add WorkspacePaneMenu (workspace switcher + tabs dropdown) in pane header right-slot; raise tab limit to `MAX_WORKSPACE_TABS = 8` (configurable); restrict layout templates to ≤2-column subset; add Daily Briefing as a new LegalWorkspace section type wired to existing `/api/ai/daily-briefing/*` endpoints
- Context pane: replace `PlaybookGalleryWidget` on welcome stage with new `GetStartedCardsWidget` (7 action cards in 2-col scrollable grid); each card dispatches `widget_load` event to route a wizard into the Workspace pane; rename stage label "Gallery" → "Get Started"; keep `PlaybookGalleryWidget` available for non-welcome stages
- Cross-cutting: error-only telemetry on new failure paths; all BFF calls via `authenticatedFetch` per ADR-028; backwards compatibility — standalone LegalWorkspace continues to function identically

### Out of Scope

- Stages 2–4 lifecycle UI (active chat, review, complete) — only welcome-stage changes here
- Full SharePoint Embedded file-upload integration (`+` button uploads to message context only, not Dataverse Document entity)
- New cross-cutting visual conventions from strategy §5.1 (AI-vs-User distinction, AI reasoning surface) — deferred to Phase A foundations work
- ADRs formalizing PaneEventBus pattern and stage lifecycle pattern — flagged as R2 lessons-learned candidates
- Strategy doc's Moments 2–4 components (TriageQueueWidget, EntityWorkspaceWidget, DocumentCanvas, etc.)
- Full App Insights happy-path telemetry — only error events are in-scope (per Owner Clarification OC-09)
- Localization / i18n — English-only, hardcoded strings (per Owner Clarification OC-10)
- Backend chat-message attachment payload extension *if* required — surfaced as risk R-1; out-of-scope unless spike confirms current endpoint already supports `attachments[]`

### Affected Areas

**Frontend (modified)**:
- `src/solutions/SpaarkeAi/src/components/shell/ThreePaneShell.tsx` — shell composition (no logic changes expected)
- `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` — replace tabs with PaneHeader + History overlay trigger
- `src/solutions/SpaarkeAi/src/components/WelcomePanel.tsx` — chrome reduction
- `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx` — embed LegalWorkspace, use PaneHeader, integrate WorkspacePaneMenu
- `src/solutions/SpaarkeAi/src/components/workspace/WorkspaceTabManager.ts` — Home-tab model, raise MAX_TABS to configurable 8
- `src/solutions/SpaarkeAi/src/components/workspace/WorkspaceLandingWidget.tsx` — delete (replaced by LegalWorkspace embed)
- `src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx` — use PaneHeader, stage label rename, welcome widget swap
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx` — activate input on load; remove ExportWord; add `+` attach button; move toolbar
- `src/client/shared/Spaarke.AI.Widgets/src/registry/ContextWidgetRegistry.ts` — register GetStartedCardsWidget
- `src/client/shared/Spaarke.AI.Widgets/src/registry/WorkspaceWidgetRegistry.ts` — register new wizard widgets
- `src/solutions/LegalWorkspace/src/sectionRegistry.ts` — register Daily Briefing section
- `src/solutions/WorkspaceLayoutWizard/src/App.tsx` (or `steps/TemplateStep.tsx`) — accept optional templateFilter prop

**Frontend (net-new)**:
- `src/client/shared/Spaarke.UI.Components/src/components/PaneHeader/{PaneHeader.tsx, index.ts}` — shared header primitive
- `src/solutions/SpaarkeAi/src/components/conversation/HistoryOverlay.tsx` — Claude-Code-style session list overlay
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useChatFileAttachment.ts` — multi-file picker + client-side text extraction
- `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePaneMenu.tsx` — combined workspace-switcher + tabs dropdown
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/context/GetStartedCardsWidget.tsx` — 7-card 2-col Get-Started widget
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/CreateProjectWizardWidget.tsx` — wrapper around CreateProjectWizardDialog
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/FindSimilarWizardWidget.tsx` — wrapper around FindSimilarWizardDialog
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/AssignWorkWizardLauncher.ts` — thin Xrm.Navigation dispatcher for `sprk_createworkassignmentwizard`
- `src/solutions/LegalWorkspace/src/sections/dailyBriefing/{DailyBriefingSection.tsx, dailyBriefing.registration.ts, useDailyBriefing.ts}` — new section + registration + BFF hook

**Backend (possibly modified — pending spike result for FR-07)**:
- `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` — `POST /api/ai/chat/sessions/{sessionId}/messages` may need `attachments[]` field (up to 5 entries)

**Backend (existing — referenced, no changes)**:
- `src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs` — `POST /api/ai/daily-briefing/narrate` consumed by useDailyBriefing
- `src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceLayoutEndpoints.cs` — consumed by useWorkspaceLayouts

## Requirements

### Functional Requirements

#### Cross-cutting

1. **FR-01**: Shared `<PaneHeader>` component published from `@spaarke/ui-components`, accepting `title: string`, `icon?: React.ReactNode`, `rightSlot?: React.ReactNode`. — Acceptance: Component exists and is consumed by ConversationPane, WorkspacePane, and ContextPaneController. Visual style matches the existing Context-pane header.

#### Assistant pane

2. **FR-02**: ConversationPane renders `<PaneHeader title="Assistant" icon={ChatRegular}>` at the top; existing `Chat | History` tab buttons removed. — Acceptance: Manual smoke shows pane header with brand-colored ChatRegular icon, "Assistant" title, no tab buttons.

3. **FR-03**: Pane header right-slot contains a `HistoryRegular` icon button. Clicking it opens a right-side overlay (`HistoryOverlay`) listing past chat sessions (up to 50 most recent via `GET /api/ai/chat/sessions?limit=50`). Selecting a session calls `setChatSessionId(sessionId)` to load that session into SprkChat; overlay closes. — Acceptance: Click icon → overlay opens; click session → conversation replaced. Overlay open <200ms, list populated <300ms p95 (per NFR-03).

4. **FR-04**: WelcomePanel chrome reduced: sparkle icon and "Welcome to Spaarke AI" heading removed; "How can I help you today?" retained as the only top-level prompt (size 400, no icon). The 4 hardcoded prompt cards (Analyze a Document / Research a Topic / Financial Analysis / Find Documents) removed. — Acceptance: Manual smoke confirms absence of those four elements.

5. **FR-05**: "Recent Conversations" section in WelcomePanel retained (existing pagination to last 5 via `/ai/chat/sessions?limit=5`). — Acceptance: Section still renders on welcome state.

6. **FR-06**: `SprkChatInput` is editable on first load before a chat session exists. Disabled-state condition changes from `disabled={isStreaming || !session || isSessionLoading}` to a form that allows typing without a session; the session is created lazily on first send (`useChatSession` already supports this). — Acceptance: User can type into input on cold load; first send creates session via BFF.

7. **FR-07**: Visible `+` (`AttachRegular`) button above the input, in a strip with the prompt menu. Clicking opens the native OS file picker (multi-select). User can attach up to **5 files per message**. Selected files have their text content extracted **client-side** (PDF.js for PDFs, `mammoth` for DOCX, raw text for `.txt`/`.md`) and attached to the next user message as context (sent in an `attachments: Array<{filename, contentType, textContent}>` field on the chat-message payload). Each attached file shows as a chip above input with an `×` remove control. Attachments do NOT create SPE Document entities. — Acceptance: User picks 2 files → both chips appear → types a question that references file content → sends → next AI reply demonstrates knowledge of both files' content.

8. **FR-08**: "Open in Word" / `SprkChatExportWord` button removed from the input toolbar. — Acceptance: Manual smoke confirms button is absent from the toolbar.

9. **FR-09**: Prompt menu and `+` attach button rendered as a horizontal strip ABOVE the conversation input box (single row, `spacingHorizontalS` between items). — Acceptance: Visual layout matches `[ Prompt menu ▾ ] [ + Attach ]` on a single row above input.

#### Workspace pane

10. **FR-10**: WorkspacePane renders `<PaneHeader title="Workspace" icon={AppsListRegular}>` with `WorkspacePaneMenu` in its right-slot. — Acceptance: Pane header parallels Assistant + Context panes; brand-colored AppsListRegular icon visible.

11. **FR-11**: On welcome stage, the Workspace pane embeds the standalone LegalWorkspace experience as the default "Home" tab. The user's default workspace layout (`GET /api/workspace/layouts/default`) renders inside the pane using the shared `WorkspaceShell` component. `WorkspaceLandingWidget` is deleted. — Acceptance: Cold load shows LegalWorkspace sections inside the pane; UI matches the standalone LegalWorkspace code page at narrower width.

12. **FR-12**: `WorkspacePaneMenu` (Fluent v9 Dropdown) in the pane header right-slot replaces the existing tab bar. Menu structure: "Open" section (newest tab at top, each with `×` close), "Home" section (pinned non-closable LegalWorkspace), "Switch Workspace" OptionGroup listing all layouts + "+ New Workspace", and "Edit current workspace" action. — Acceptance: All four sections render with dividers; close `×` only on non-Home items; selection switches active pane content; "+ New Workspace" launches the existing `WorkspaceLayoutWizard`.

13. **FR-13**: `MAX_WORKSPACE_TABS = 8` exposed as a configurable constant in `WorkspaceTabManager.ts`. Home tab is exempt from the cap. When a 9th non-Home widget arrives, FIFO eviction removes the oldest non-Home tab. — Acceptance: Open 9 widgets in succession → 9th eviction matches FIFO; Home never evicted; constant editable in one place.

14. **FR-14**: `WorkspaceLayoutWizard` accepts an optional `templateFilter?: LayoutTemplateId[]` prop. When invoked from SpaarkeAi's WorkspacePaneMenu, the filter restricts options to: `2-col-equal`, `3-row-mixed`, `hero-2x2`, `sidebar-main`, `single-column`, `single-column-5` (6 templates). Excludes: `3-col-equal`, `3-col-3-row`, `hero-grid`. — Acceptance: Wizard launched from SpaarkeAi shows 6 templates; standalone LegalWorkspace continues to show all 9 (no filter passed).

15. **FR-15**: New LegalWorkspace section type "Daily Briefing" registered in `sectionRegistry.ts`. Renders a Card with AI-curated bullets from `POST /api/ai/daily-briefing/narrate` (consumed via new `useDailyBriefing()` hook). Section is selectable in `WorkspaceLayoutWizard` Step 2 (Section Selection). Default height: `'medium'`. — Acceptance: Section appears in Layout Wizard Section Selection; including it in a layout causes content to render; available in BOTH standalone LegalWorkspace and SpaarkeAi.

16. **FR-16**: When the Daily Briefing endpoint returns an empty result (no open tasks, no recent records, no notifications), the widget renders a friendly empty state: a small icon and the text "Nothing to see right now — enjoy your day". Widget remains visible (does not hide). — Acceptance: Triggered when narrate returns empty content; matches per Owner Clarification OC-08.

#### Context pane

17. **FR-17**: ContextPaneController migrated to use shared `<PaneHeader>` (preserves existing visual style — that style is the canonical reference). — Acceptance: Pane header uses the same component as Assistant and Workspace.

18. **FR-18**: New `GetStartedCardsWidget` rendered on the welcome stage. Displays 7 cards (Create Matter, Create Project, Assign Work, Summarize Files, Find Similar, Send Email, Schedule Meeting) in a 2-column scrollable CSS grid (`gridTemplateColumns: '1fr 1fr'`, `gap: spacingHorizontalS`, vertical scroll on overflow). Each card reuses the LegalWorkspace `ActionCard` component (extracted to `@spaarke/ui-components` if needed per ADR-012). — Acceptance: All 7 cards visible; 2-col layout; keyboard-navigable; matches LegalWorkspace card styling.

19. **FR-19**: Card click on `GetStartedCardsWidget` dispatches a `widget_load` event on the `workspace` PaneEventBus channel with the appropriate widget type:

    | Card | Widget type | Existing widget |
    |---|---|---|
    | Create Matter | `create-matter-wizard` | `CreateMatterWizardWidget` ✓ |
    | Create Project | `create-project-wizard` | New wrapper around `CreateProjectWizardDialog` |
    | Document Upload | `document-upload-wizard` | `DocumentUploadWizardWidget` ✓ |
    | Find Similar | `find-similar-wizard` | New wrapper around `FindSimilarWizardDialog` |
    | Send Email | `email-compose` | Analysis Builder intent |
    | Schedule Meeting | `meeting-schedule` | Analysis Builder intent |

    — Acceptance: Each card click results in the corresponding widget opening as a new tab "on top" of Home in the Workspace pane (becomes the active tab).

20. **FR-20**: Assign Work card invokes the existing Dataverse custom page `sprk_createworkassignmentwizard` via `Xrm.Navigation.navigateTo({ pageType: 'custom', name: 'sprk_createworkassignmentwizard', data: 'bffBaseUrl=...' })`. Feature-detect `window.Xrm`; show a "Open in Spaarke" placeholder in non-host environments (Vite dev). — Acceptance: Click in Power Apps host opens the wizard dialog; click in Vite dev shows placeholder without error.

21. **FR-21**: `PlaybookGalleryWidget` remains registered in `ContextWidgetRegistry`. Available for non-welcome stages via `context_update` events (e.g., user mid-session asks to switch playbook). — Acceptance: Widget code retained; not invoked on welcome stage but still resolvable by type.

22. **FR-22**: Context-pane stage label changes from "Gallery" to "Get Started" on welcome stage. — Acceptance: `stageLabelMap.welcome === 'Get Started'` in ContextPaneController; visible in pane header right.

#### Cross-cutting (continued)

23. **FR-23**: All BFF calls (recent sessions list, daily briefing narrate, workspace layouts, chat messages with attachments) use `authenticatedFetch` from `@spaarke/auth` per ADR-028. No token snapshots stored in props or React state. — Acceptance: Code review confirms no `accessToken` prop drilling; all fetches go through `authenticatedFetch`.

24. **FR-24**: Minimal error-only telemetry: emit App Insights custom events on (a) Daily Briefing 429 responses, (b) file-extraction failures (per-file with MIME type + size), (c) HistoryOverlay session-list load failures. No happy-path events in this project. — Acceptance: Manually trigger each failure path; events appear in App Insights with the expected schema.

25. **FR-25**: Backwards compatibility — the standalone LegalWorkspace code page continues to function identically to its current behavior. All 9 layout templates remain selectable in the standalone wizard; existing user layouts via `/api/workspace/layouts` unchanged. — Acceptance: Open standalone LegalWorkspace in a separate browser tab; confirm all 9 templates in its wizard; existing user layouts still render.

### Non-Functional Requirements

| ID | Requirement | Target / Acceptance |
|---|---|---|
| **NFR-01** | Pane render performance | First paint of each pane <500 ms on cold load (excluding session-restore data fetch); shell-stage transition <100 ms |
| **NFR-02** | Session restore performance | Inherits R2 D-08 target: p95 <500 ms from session selection to restored chat history visible |
| **NFR-03** | History overlay performance | Overlay open animation <200 ms; session list populated <300 ms p95 for 50 items |
| **NFR-04** | File attachment performance & limits | Client-side text extraction <2 s per file (up to 10 MB); PDF max 200 pages or 10 MB whichever smaller; max 5 files per message; allowed MIME types: `text/plain`, `text/markdown`, `application/pdf`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document` |
| **NFR-05** | Accessibility | WCAG 2.1 AA. All interactive elements keyboard-navigable. ARIA labels on every pane header, icon-only button, and dropdown trigger. Screen-reader announces active tab, active workspace, and stage transitions |
| **NFR-06** | Dark mode | All new components use Fluent v9 tokens only (ADR-021). No hardcoded `#` colors, no `rgba(...)` literals |
| **NFR-07** | Browser support | Microsoft Edge (Chromium) current + previous major; Chrome current + previous major; no IE11 |
| **NFR-08** | Auth | All BFF calls use `authenticatedFetch` per ADR-028; no token snapshotting; 401 auto-retry with silent MSAL refresh |
| **NFR-09** | Persistence | Tabs (Home + non-Home) and active workspace layout survive page refresh via existing session-restore pipeline (Cosmos write-through, R2 D-06) |
| **NFR-10** | Backward compatibility | Standalone LegalWorkspace continues to function identically; all 9 layout templates remain available there; existing user layouts unchanged |
| **NFR-11** | Rate-limit handling | Daily Briefing widget displays a graceful degraded card (with retry CTA) on 429 from `/api/ai/daily-briefing/narrate`; does not block the rest of the workspace from rendering |
| **NFR-12** | Bundle size | Daily Briefing widget + History overlay + file-extraction libs add <250 KB gzip to the SpaarkeAi bundle (file-extraction libs lazy-loaded on first `+` click) |

## Technical Constraints

### Applicable ADRs

| ADR | Relevance |
|---|---|
| **ADR-006** (PCF over web resources) | SpaarkeAi and LegalWorkspace are full-page surfaces, not PCFs — reaffirms architectural placement |
| **ADR-008** (Endpoint filters) | If FR-07 surfaces a need for backend chat-attachment payload extension, the new/changed endpoint inherits the existing endpoint-filter pipeline (auth, rate limit, validation) |
| **ADR-010** (DI minimalism) | Any new BFF service (e.g., server-side file-extraction helper, if alternative to client-side) is registered in an existing feature module — no new modules |
| **ADR-012** (Shared components) | **Load-bearing.** `<PaneHeader>` lives in `@spaarke/ui-components`. `ActionCard` extracted to shared library if not already. All new context-agnostic components added to shared lib, not solution-local |
| **ADR-013** (AI architecture) | Daily Briefing and chat-attachment paths extend the BFF, not a new service |
| **ADR-014** (AI caching) | Daily Briefing response cached per-user with short TTL (≈5 min) to avoid hammering the LLM on tab switches |
| **ADR-016** (AI rate limits) | Daily Briefing endpoint subject to existing AI rate-limit filters; widget shows graceful degraded UI on 429 (NFR-11) |
| **ADR-021** (Fluent design system) | **Load-bearing.** All new components use Fluent v9 tokens only; no hardcoded colors, no Fluent v8; dark-mode safe by construction |
| **ADR-022** (PCF platform libraries / React 19) | React 19 used for all SpaarkeAi/LegalWorkspace code; affects bundling for PDF.js (FR-07) |
| **ADR-025** (Icon library and deployment) | Icons (`ChatRegular`, `AppsListRegular`, `DocumentRegular`, `HistoryRegular`, `AttachRegular`) come from Fluent v9 `@fluentui/react-icons`; no new SVG web resources |
| **ADR-026** (Full-page custom page standard) | SpaarkeAi and LegalWorkspace are full-page custom pages (Vite + React 19); all new shared components built per this standard |
| **ADR-028** (Spaarke auth architecture v2) | **Load-bearing.** All BFF calls go through `authenticatedFetch` from `@spaarke/auth`. Function-based auth contract per INV-1..INV-8. No token snapshots in props or state |

### MUST Rules (extracted from applicable ADRs)

- ✅ **MUST** use `<PaneHeader>` from `@spaarke/ui-components` for all three SpaarkeAi panes (ADR-012)
- ✅ **MUST** use Fluent v9 `@fluentui/react-components` and `@fluentui/react-icons` exclusively; no Fluent v8 (ADR-021)
- ✅ **MUST** use `tokens.*` for all colors and spacing; no hex literals, no `rgba()` literals (ADR-021)
- ✅ **MUST** use `authenticatedFetch` from `@spaarke/auth` for every BFF call (ADR-028)
- ✅ **MUST** call `getAccessToken()` per request — no snapshotting (ADR-028)
- ✅ **MUST** use tenant-specific Azure AD authority via `sprk_TenantId` env var (ADR-028)
- ✅ **MUST** keep the standalone LegalWorkspace code page unchanged (NFR-10, FR-25)
- ❌ **MUST NOT** create Dataverse Document entities from the Assistant `+` button (FR-07 is in-memory message context only)
- ❌ **MUST NOT** add new DI feature modules; register in existing modules (ADR-010)
- ❌ **MUST NOT** introduce React 16 fallbacks; React 19 only (ADR-022)
- ❌ **MUST NOT** add happy-path App Insights events in this project; error-only telemetry (FR-24, OC-09)

### Existing Patterns to Follow

- **Three-pane shell composition**: `src/solutions/SpaarkeAi/src/components/shell/ThreePaneShell.tsx`
- **PaneEventBus channels & multi-subscriber pattern**: `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventBusContext.tsx` (channels: `workspace`, `context`, `conversation`, `safety`)
- **AiSessionProvider auth + streaming**: `src/client/shared/Spaarke.AI.Widgets/src/providers/AiSessionProvider.tsx`
- **Lazy widget resolution**: `src/client/shared/Spaarke.AI.Widgets/src/registry/WorkspaceWidgetRegistry.ts` and `ContextWidgetRegistry.ts`
- **Workspace layout persistence**: `src/solutions/LegalWorkspace/src/hooks/useWorkspaceLayouts.ts` + BFF `/api/workspace/layouts` endpoints
- **Layout wizard composition**: `src/solutions/WorkspaceLayoutWizard/src/App.tsx` + `steps/TemplateStep.tsx`
- **Section registry pattern**: `src/solutions/LegalWorkspace/src/sectionRegistry.ts` + per-section `*.registration.ts` files
- **Action card + click handler split**: `src/solutions/LegalWorkspace/src/components/GetStarted/ActionCard.tsx` + `ActionCardHandlers.ts`
- **Workspace switcher dropdown reference**: `src/solutions/LegalWorkspace/src/components/Shell/PageHeader.tsx`
- **Context-pane header style (canonical)**: `src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx:142-171, 691-700`
- **Function-based auth contract**: `@spaarke/auth` `authenticatedFetch` — per ADR-028 INV-1..INV-8
- **SSE event types & 23-type schema**: `src/server/api/Sprk.Bff.Api/Services/Ai/.../SseEventTypes/`
- **Lessons learned (R2)**: `projects/spaarke-ai-platform-unification-r2/notes/lessons-learned.md` — PaneEventBus pattern, data-refreshed restore (D-08), tab-count event for stage transitions

## Success Criteria

1. [ ] **FR-01 Pane header parity** — Code grep confirms all 3 panes use the same `<PaneHeader>` component. Visual diff at 1280px, 1600px, and narrow viewport widths shows identical typography, spacing, and alignment.
2. [ ] **FR-02..FR-09 Assistant pane** — Manual smoke confirms: ChatRegular icon, "Assistant" title, no tab buttons, History overlay opens on click, "Welcome to Spaarke AI" + 4 prompt cards + "Open in Word" absent, input editable on cold load, multi-file attach with chip strip, prompt-menu and `+` in a strip above input.
3. [ ] **FR-07 Multi-file attach** — User attaches 5 .txt files in one message; backend receives `attachments` array of 5 items; AI reply demonstrates knowledge of all 5 file contents.
4. [ ] **FR-10..FR-16 Workspace pane** — Manual smoke: AppsListRegular icon visible; LegalWorkspace embedded as Home tab; WorkspacePaneMenu shows Open/Home/Switch Workspace/Edit sections with dividers; opening 9 widgets evicts oldest non-Home via FIFO; Layout Wizard launched from SpaarkeAi shows 6 templates (standalone shows 9); Daily Briefing renders content; empty state shows "Nothing to see right now — enjoy your day".
5. [ ] **FR-17..FR-22 Context pane** — Manual smoke: shared PaneHeader visible; 7 GetStarted cards in 2-col scrollable grid; clicking Create Matter opens `create-matter-wizard` as a new top-tab in Workspace; Assign Work invokes `sprk_createworkassignmentwizard` via Xrm in Power Apps host; stage label is "Get Started" on welcome.
6. [ ] **FR-23 Auth** — Code review confirms zero `accessToken` props or state snapshots in new components. All fetches go through `authenticatedFetch`.
7. [ ] **FR-24 Error telemetry** — Manually trigger 429 from Daily Briefing, file extraction error (corrupted PDF), and HistoryOverlay load failure. App Insights events appear with expected schema (no happy-path events).
8. [ ] **FR-25 Backwards compatibility** — Standalone LegalWorkspace opened in another browser tab still shows all 9 templates in its wizard; existing user layouts render identically; no regression from before this project.
9. [ ] **NFR-01..NFR-03 Performance** — Lighthouse + manual measurement confirm pane render <500 ms, History overlay open <200 ms + populate <300 ms p95, session restore p95 <500 ms.
10. [ ] **NFR-04 File limits** — Attempts to attach a 6th file are blocked with a UI message; >10 MB file blocked; unsupported MIME type blocked.
11. [ ] **NFR-05 Accessibility** — Manual keyboard nav through all three panes' headers + cards + dropdowns. Screen-reader announces "Assistant pane", "Workspace pane", "Context pane", active tab name, active workspace name, stage transitions.
12. [ ] **NFR-06 Dark mode** — Lint rule confirms no hex literals in new code; manual side-by-side light/dark comparison passes for all new components.
13. [ ] **NFR-09 Persistence** — Open 3 non-Home tabs, refresh page; all 3 tabs (and active selection) restored. Verify via R2 session-restore pipeline.
14. [ ] **NFR-12 Bundle size** — `npm run build:prod` of SpaarkeAi reports bundle delta <250 KB gzip vs. R2 baseline. File-extraction libs absent from initial bundle (verified via webpack-bundle-analyzer or equivalent).

## Dependencies

### Prerequisites

- R2 (`spaarke-ai-platform-unification-r2`) merged to master — confirmed (commit `b40dc3e6`)
- Auth v2 (`spaarke-auth-v2-and-hardening`) merged to master — confirmed (commit `e649f244`)
- Existing endpoints reachable: `/api/ai/chat/sessions`, `/api/ai/daily-briefing/narrate`, `/api/workspace/layouts/default`, `/api/workspace/layouts`
- `WorkspaceShell`, `useWorkspaceLayouts`, `WorkspaceLayoutWizard`, `ActionCard`, `sectionRegistry` available from LegalWorkspace + Spaarke.UI.Components
- `PaneEventBus`, `AiSessionProvider`, `WorkspaceWidgetRegistry`, `ContextWidgetRegistry` available from Spaarke.AI.Widgets

### External Dependencies

- `pdfjs-dist` (PDF text extraction client-side) — new npm dep, lazy-loaded
- `mammoth` (DOCX text extraction client-side) — new npm dep, lazy-loaded
- `@fluentui/react-icons` icons used: `ChatRegular`, `AppsListRegular`, `DocumentRegular`, `HistoryRegular`, `AttachRegular`, `AddRegular`, `DismissRegular` (existing dep, no version bump expected)
- `Xrm.Navigation.navigateTo` available only inside Power Apps host (feature-detect; placeholder in dev)
- Azure AI Content Safety, Cosmos DB, AI Search — inherited from R2, no new resources

## Owner Clarifications

*Answers captured during design-to-spec interview and from design.md §F resolutions:*

| OC# | Topic | Question | Answer | Impact |
|---|---|---|---|---|
| OC-01 | History trigger UX (design §F-1) | Where does session history open? Popover, full-pane, side overlay? | **Side overlay** — Claude-Code-style; selecting a session replaces the current conversation via `setChatSessionId` | Determines HistoryOverlay component placement and animation; affects FR-03 acceptance |
| OC-02 | File picker semantics (design §F-2) | Does `+` create SPE Document entities? | **No** — file content is extracted client-side and attached to the next chat message as context only | Excludes WorkingDocumentService / SPE pipeline; defines FR-07 boundary |
| OC-03 | Tab limit (design §F-3) | What's the new MAX_TABS? | **8 non-Home, configurable as `MAX_WORKSPACE_TABS`** | Defines FR-13; FIFO eviction at 9 |
| OC-04 | Component reuse (design §F-4) | Extract ActionCard to shared library or copy? | **Reuse whenever possible**; extract ActionCard if not coupled; same principle for WorkspaceShell, useWorkspaceLayouts, WorkspaceLayoutWizard | Constrains all net-new files to lean on existing primitives |
| OC-05 | Assign Work card (design §F-5) | What does Assign Work launch? | **Dataverse custom page `sprk_createworkassignmentwizard`** via `Xrm.Navigation.navigateTo` | Defines FR-20; AssignWorkWizardLauncher is a thin dispatcher |
| OC-06 | Pane icons (design §F-6) | Which icons per pane? | **Assistant=ChatRegular, Workspace=AppsListRegular, Context=DocumentRegular**; all `colorBrandForeground1` | Defines FR-02/FR-10/FR-17 icon choices |
| OC-07 | File attach cardinality (Step 2.5 Q1) | Single or multi-file per message? | **Multi-file, up to 5** — `attachments: Array<{filename, contentType, textContent}>` | Drives FR-07 hook API, UI (chip strip), backend payload shape, risk R-1 spike |
| OC-08 | Daily Briefing empty state (Step 2.5 Q2) | What renders when no open tasks / records / notifications? | **Friendly empty message**: small icon + "Nothing to see right now — enjoy your day" | Defines FR-16 acceptance |
| OC-09 | Telemetry scope (Step 2.5 Q3) | App Insights instrumentation in/out of scope? | **Minimal: error events only** — Daily Briefing 429s, file extraction errors, HistoryOverlay load failures | Defines FR-24 boundary; excludes happy-path events |
| OC-10 | Localization (Step 2.5 Q4) | English-only or i18n? | **English-only, hardcoded** (match R2 pattern) | No strings module; no FormatJS/react-intl wiring |

## Assumptions

*Proceeding with these assumptions (owner did not explicitly specify, but reasonable defaults apply):*

- **A-1**: Backend `POST /api/ai/chat/sessions/{sessionId}/messages` may need extension to accept `attachments: Array<{filename, contentType, textContent}>` — first task is a 30-minute spike to confirm current payload shape. If extension needed, opens a small backend sub-task (per design.md risk R-1).
- **A-2**: `Xrm.Navigation.navigateTo` is unavailable in standalone Vite dev. Feature-detect `window.Xrm` and show "Open in Spaarke" placeholder in non-host environments (per design.md risk R-3).
- **A-3**: `ActionCard` from LegalWorkspace can be made context-agnostic per ADR-012 (likely depends only on Fluent v9 + an `onClick` callback). If it pulls LegalWorkspace-specific deps, extract those into ADR-021 token mappings or refactor before lifting into `@spaarke/ui-components` (per design.md risk R-5).
- **A-4**: Session-restore (R2 D-08) currently restores widget state per individual widget; the tabs *list* across reloads is assumed to be covered. If verification (§G #13) reveals gaps, extend `SessionPersistenceService` to include the tabs array — tracked as design.md §H follow-up.
- **A-5**: New-user empty state — when a user has no saved workspaces, `GET /api/workspace/layouts/default` is expected to return a system-default layout (e.g., "Corporate Workspace") that serves as the Home tab. Assumes existing standalone LegalWorkspace behavior. Confirm during build; if false, add a new-user fallback that prompts to create a workspace.
- **A-6**: The user is the only consumer of `WorkspaceLayoutWizard.templateFilter`; standalone LegalWorkspace will continue to invoke the wizard without a filter (showing all 9 templates). Default behavior of the prop is "no filter" — preserves backwards compatibility.

## Unresolved Questions

*Still need answers before or during implementation:*

- [ ] **UQ-01** — Backend chat-message endpoint: does the current `POST /api/ai/chat/sessions/{sessionId}/messages` payload accept inline attachments? Blocks: FR-07 implementation path (frontend-only vs. requires backend sub-task). Resolution: 30-min spike in task 001.
- [ ] **UQ-02** — Daily Briefing endpoint caching: does the existing endpoint already implement per-user TTL caching per ADR-014, or does the new `useDailyBriefing()` hook need to add a frontend cache? Blocks: NFR-11 implementation depth.
- [ ] **UQ-03** — Persistence of non-Home tabs across page refresh (NFR-09): does R2's `SessionPersistenceService` already serialize the tabs list, or only individual widget state? Blocks: NFR-09 verification — may surface a backend sub-task if not covered.
- [ ] **UQ-04** — DOCX text extraction library size: confirm `mammoth` bundle size fits within NFR-12 budget (<250 KB gzip combined with PDF.js + History overlay). If too large, evaluate alternatives or accept a higher bundle budget.
- [ ] **UQ-05** — `Xrm.Navigation.navigateTo` argument shape for `sprk_createworkassignmentwizard`: confirm exact `data` query string format expected by the custom page (`bffBaseUrl=...` URL-encoded). Reference: user-provided runtime log shows the existing pattern; verify before FR-20 implementation.

---

*AI-optimized specification. Original design: `design.md` (5,189 words). Generated by `/design-to-spec` on 2026-05-20.*
