# SpaarkeAi Shipped State — Ground Truth for R5 User Testing

> **Captured**: 2026-06-03 (immediately post-R4 master merge)
> **Basis**: Code survey at master commit `18b9323f` (R4 merged; PR #331)
> **Method**: Explore agent survey of `src/solutions/SpaarkeAi/`, `src/client/shared/Spaarke.AI.Widgets/`, `src/solutions/LegalWorkspace/sectionRegistry.ts`
> **Why this exists**: User testing must be grounded in **what's actually shipped**, not what's documented in architecture aspirations. This doc reports code state, not intent.

---

## 1. Entry points + boot sequence

### Main entry (`main.tsx`)

**File**: `src/solutions/SpaarkeAi/src/main.tsx:1–374`

Bootstrap sequence (lines 182–357):

1. **Suppress Daily Digest auto-popup** (pre-bootstrap, line 188) — sets `sessionStorage.spaarke_dailyDigestShown` before any React tree mounts so embedded LegalWorkspaceApp doesn't launch the Daily Briefing modal.
2. **Register LegalWorkspaceApp as default workspace renderer** (line 198) — `setDefaultWorkspaceRenderer(LegalWorkspaceRenderer)`. This is the R4 C-4 abstraction point; SpaarkeAi binds here, not hardcoded inside `WorkspaceLayoutWidget`.
3. **Resolve runtime config** (lines 201–233):
   - XRM path (in-MDA): `resolveRuntimeConfig()` reads Xrm frame env vars or localStorage cache
   - Direct URL path (top-frame): fetch from BFF `GET /api/config/client` (anonymous endpoint)
   - Fallback BFF URL: `VITE_BFF_BASE_URL` env var (baked at build, default `https://spaarke-bff-dev.azurewebsites.net`)
4. **Initialize dual runtime-config singletons** (lines 235–253):
   - SpaarkeAi singleton via `setRuntimeConfig(config)`
   - LegalWorkspace singleton via `setLegalWorkspaceRuntimeConfig(config)` (R4 Fix 4.1) — same config to both, so embedded LW paths find initialized BFF URL
5. **Cache to localStorage** (lines 255–261) — persist config so repeat visits resolve instantly
6. **Auth initialization** (lines 274–293):
   - `ensureAuthInitialized()` from `@spaarke/auth`
   - Per ADR-028: MSAL silent + popup chain (never redirect in iframe)
   - Patch tenant ID from MSAL account if initially empty
7. **Parse URL parameters** (lines 305–332):
   - Entity context: `?entityType=` + `?entityId=` + optional `?matterId=`
   - Session restore: `?sessionId=` for resume flow
   - Decodes Dataverse `?data=` parameter (URL-encoded key=value pairs)
8. **Render App** (lines 343–353) → `<App entityLogicalName entityId matterId sessionId />` with React 19 `createRoot`

**Auth bootstrap invariants** (lines 23–28 docblock):
- MUST NOT use redirect flow inside an iframe
- MUST use redirect in top-frame (popup unreliable for bookmarks)
- MUST handle MSAL redirect promise on every page load
- MUST store MSAL cache in localStorage (survives tab/browser close)

### Root component (`App.tsx`)

**File**: `src/solutions/SpaarkeAi/src/App.tsx:1–168`

Provider tree (lines 153–168):
```
<FluentProvider theme={resolveCodePageTheme()}>
  <AppWithAuth>
    <ThreePaneShell bffBaseUrl + entityLogicalName + entityId + matterId + sessionId />
  </AppWithAuth>
</FluentProvider>
```

**Theme resolution** (lines 153–161) — priority: localStorage `spaarke-theme` > URL `?flags=themeOption:dark|light` > navbar DOM background detection > default light. Must NOT use OS `prefers-color-scheme` per `theme-consistency.md`.

**Auth gating** (lines 79–112 `AppWithAuth`) — probes `getAuthProvider().getAccessToken()` once at mount → sets `isAuthenticated` UI flag. Does NOT snapshot the token (fixed ADR-028 §H-5 bug where token staled after ~80min idle). Downstream BFF calls get fresh token via `authenticatedFetch()` per-request.

---

## 2. Widget catalog — actual registrations

### Workspace widgets (Direct widgets)

**Source of truth**: `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts:1–590`

**16 registered workspace widgets** as of R4 baseline:

| # | Type ID | Display Name | Category | Allow Multiple | Default Order | Archetype | Source |
|---|---------|--------------|----------|---|---|---|---|
| 1 | `BudgetDashboard` | Budget Dashboard | financial | NO | 10 | AI output (R1 migrated) | `@spaarke/ai-outputs` |
| 2 | `SearchResults` | Search Results | search | YES | 20 | AI output (R1) | `@spaarke/ai-outputs` |
| 3 | `redline-viewer` | Document Comparison | document | YES | 25 | Direct widget (R2 new, task 042) | AI router output |
| 4 | `AnalysisEditor` | Analysis Editor | analysis | YES | 30 | AI output (R1) | `@spaarke/ai-outputs` |
| 5 | `ContractComparison` | Contract Comparison | document | YES | 40 | AI output (R1) | `@spaarke/ai-outputs` |
| 6 | `StatusSummary` | Status Summary | status | NO | 50 | AI output (R1) | `@spaarke/ai-outputs` |
| 7 | `Recommendation` | Recommendations | recommendation | NO | 60 | AI output (R1) | `@spaarke/ai-outputs` |
| 8 | `ActionPlan` | Action Plan | planning | NO | 70 | AI output (R1) | `@spaarke/ai-outputs` |
| 9 | `create-matter-wizard` | Create Matter Wizard | wizard | NO | 80 | Wizard launcher (embedded) | Task AIPU2-104 |
| 10 | `document-upload-wizard` | Upload Documents | wizard | YES | 85 | Wizard launcher (embedded) | Task AIPU2-104 |
| 11 | `search-select-wizard` | Search & Select | wizard | YES | 90 | Wizard launcher (embedded) | Task AIPU2-104 |
| 12 | `email-compose` | Send Email | ai | YES | 100 | Analysis Builder dispatcher (Get Started card) | Task 044 FR-19 |
| 13 | `meeting-schedule` | Schedule Meeting | ai | YES | 110 | Analysis Builder dispatcher (Get Started card) | Task 044 FR-19 |
| 14 | `create-project-wizard` | Create New Project | wizard | YES | 120 | Code Page launcher (Xrm.Navigation.navigateTo) | Task 043 FR-19 |
| 15 | `find-similar-wizard` | Find Similar Documents | wizard | YES | 130 | Code Page launcher (Xrm.Navigation.navigateTo) | Task 043 FR-19 |
| 16 | `workspace` | Workspace | workspace | YES | 140 | Dashboard wrapper (LegalWorkspaceApp embedded) | Task 052 C-4 |

### DocumentViewerWidget (R4 W-4 demo)

**File**: `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-document-viewer-widget.ts:1–66`

- Type: `'document-viewer'`
- Display name: Document Viewer
- Category: document
- Allow Multiple: YES
- Default Order: 150
- Mount source: Assistant pane → Workspace (first end-to-end demo)
- Archetype: Direct widget, AI-output style
- **Status**: Registered (R4 task 042 / W-4); dispatcher side not yet wired

### Dashboard sections (LegalWorkspace SECTION_REGISTRY)

**File**: `src/solutions/LegalWorkspace/src/sectionRegistry.ts:1–98`

**7 registered dashboard sections** (R4 W-3 made `SECTION_METADATA_CATALOG` in `@spaarke/ui-components` the authoritative wizard-picker source):

| # | ID | Display Name | Dual-use | Source |
|---|---------|---|---|---|
| 1 | `get-started` | Get Started | NO | LW-internal |
| 2 | `quick-summary` | Quick Summary | NO | LW-internal |
| 3 | `latest-updates` | Latest Updates | NO | LW-internal |
| 4 | `todo` | Smart To Do | NO | LW-internal |
| 5 | `documents` | Documents | NO | LW-internal |
| 6 | `daily-briefing` | Daily Briefing | YES (R3 task 069/086 — hoisted to `@spaarke/ui-components`) | Shared lib |
| 7 | `calendar` | Calendar | YES (R3 task 114/115 — canonical Pattern D, `@spaarke/events-components`) | Shared lib |

Guard logic (lines 45–81) — development-only checks for duplicate IDs and drift between SECTION_REGISTRY and SECTION_METADATA_CATALOG.

---

## 3. Mount sources — what dispatches `widget_load`

Per `SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md §3`, there are **four mount sources** plus two R4 additions (W-4, W-5) that are partial.

### Source 1: Workspace dropdown (user picker) ✅ shipped

**File**: `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePaneMenu.tsx`

- Trigger: user clicks "Switch Workspace" dropdown → selects a layout
- Dispatch: `dispatch('workspace', { type: 'widget_load', widgetType: 'workspace', widgetData: { layoutId, layoutName } })`
- Wrapper: Dashboard wrapper (every entry is a `sprk_workspacelayout` record)
- Status: No R4 changes; ships and works

### Source 2: Auto-install default workspace (cold load) ✅ shipped R4

**File**: `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx:377–440` (task 109 Wave 2b)

- Trigger: App cold-load; `useWorkspaceLayouts()` resolves BFF's default layout (per-user → system → global → null cascade)
- Dispatch (lines 418–432): `widget_load { widgetType: 'workspace', widgetData: { layoutId, layoutName } }`
- Guards (lines 383–440): run-once via `autoInstalledDefaultRef`; skip if already open; skip if in pinned list (avoid double-dispatch); defer to macrotask via `setTimeout(..., 0)` so `usePaneEvent` subscription is live
- Wrapper: Dashboard wrapper (typically Daily Briefing in dev per Wave 2a seed)
- Status: Shipped task 109; replaces hardcoded Home tab

### Source 3: Auto-open pinned workspaces ✅ shipped R4

**File**: `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx:481–500` (task 101)

- Trigger: App cold-load; reads `localStorage['spaarke:workspace:pinned-list']` via `getPinnedWorkspaces()`
- Dispatch: per-pinned `widget_load { widgetType: 'workspace', widgetData: { layoutId } }`
- Guards (lines 481–500): run-once via `autoOpenedPinsRef`; skip already-open layouts; defer to macrotask
- Wrapper: Dashboard wrapper, persisted pin order
- Status: Shipped (task 092/101)

### Source 4: Assistant → Workspace (R4 W-4) ⏳ partial

**File**: `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` (imports `DOCUMENT_VIEWER_WIDGET_TYPE` symbolic constant lines 86–94) + AiSessionProvider streaming.onPaneEvent

- Trigger (demo scenario): user uploads PDF in chat → AI extracts text → suggests "Open as workspace tab" button
- Dispatch flow: Assistant pane / AI streaming handler dispatches `widget_load { widgetType: 'document-viewer', widgetData }` → WorkspacePane resolves from registry → mounts `DocumentViewerWidget`
- Wrapper: Direct widget wrapper
- **Status**: **Infrastructure shipped, dispatcher side NOT wired in ConversationPane.** Widget registered + type constant exported; no actual button or dispatcher call site exists yet. **Operator-chosen demo scenario at task time; implementation incomplete.**

### Source 5: Context → Workspace (R4 W-5) ⏳ partial

**File**: `src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx` + per-wizard final-step handlers

- Trigger (recommended demo: Create Project wizard final step): user clicks "Add result to Workspace" → result widget dispatches
- Dispatch flow: wizard final-step `useDispatchPaneEvent` dispatches `widget_load { widgetType, widgetData }` to workspace channel
- Wrapper: usually Direct widget; could be Dashboard if wizard produces a layout
- **Status**: **Infrastructure exists (ContextPaneController imports `useDispatchPaneEvent`), per-wizard final-step handlers NOT implemented.** No "Add to Workspace" button visible to users yet.

---

## 4. Pane wiring + observable state

### Assistant pane (left)

**File**: `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx`

- What it is: chat via `SprkChat` from `@spaarke/ui-components`; Welcome state (`WelcomePanel`) when no session/entity/playbook; active chat when session selected
- Dispatches to workspace: `widget_load` mount source 4 — infrastructure only; demo dispatcher not yet wired
- Receives from workspace: `active_widget_changed` (R4 Fix 4) — broadcast so conversation pane can adapt if it wants entity scope (no consumers wired yet per Fix 4 docblock)
- Header (shared `PaneHeader`): title "Assistant", icon `ChatRegular`, right slot HistoryMenu side-overlay trigger (task 022)
- Stage lifecycle (lines 13–35 docblock):
  - Welcome → Loading: first message sent OR welcome prompt click OR `playbook-selected` event (AIPU2-102)
  - Loading → Active-chat: streaming starts OR entity resolved
  - Session reset: `session_reset` event clears state

### Workspace pane (center)

**File**: `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx`

- What it is: tab manager (`WorkspaceTabManager` class, plain TS, single instance per mount); tab bar + active widget renderer (`WorkspaceTabManagerComponent`); lazy widget resolution via `resolveWorkspaceWidget()`
- Tab model: FIFO queue with MAX_WORKSPACE_TABS cap (~8 tabs); each tab `{ tabId, widgetType, widgetData, displayName }`
- Persistence: serialize to BFF `PATCH /ai/chat/sessions/{sessionId}/tabs` on mutation (debounced ~200ms, best-effort); restore on mount via fetch on cold load
- Workspace dropdown (WorkspacePaneMenu): "Switch Workspace" header button; lists available layouts from `useWorkspaceLayouts()` BFF fetch; click → dispatch `widget_load { type: 'workspace', widgetType: 'workspace', layoutId }`
- Dispatches: `tab_count_change { tabCount }` (triggers Stage 3 ↔ Stage 4 transition); `active_widget_changed { widgetType, widgetData, tabId, displayName }` (signals Context pane which workspace tab is active)
- Receives: `widget_load` (Assistant/Context/auto-install); `widget_update { widgetType, widgetData }`; `widget_action { action, payload }` (forwards to active tab's widget via ref if implements handler); `playbook-selected` (from Context gallery — clears tabs if exclusive mode, seeds default widgets per AIPU2-102)
- Header (shared `PaneHeader`): title "Workspace", icon `AppsListRegular`, right slot reserved
- Observable state: `tabState.tabs` array, `tabState.activeTabIndex`; loading spinner while widget factory promise resolves
- Stage lifecycle (ThreePaneShell): Welcome (empty placeholder lines 82–88) → Loading (empty or spinner if restoring) → Active-chat (single tab) → Review (tab bar ≥2 tabs)

### Context pane (right)

**File**: `src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx`

- What it is: stage-aware context widget resolver from `ContextWidgetRegistry`; default widgets by stage
- Stage-to-widget mapping (lines 112–119): welcome → `playbook-gallery`; loading → `entity-info`; active-chat → `sources-citations`; review → `related-items`
- Receives: `context_update { widgetType, data }` (server-driven widget + payload); `context_highlight { citationId, selectionRef }` (forwards to active widget's `onHighlight()` ref method); `stage_change` (clears active widget so stage-default UI renders)
- Dispatches to workspace: (indirectly via wizard final-step handlers) `widget_load` when wizard completes — **wizard implementations pending**
- Header (shared `PaneHeader`): title "Context", icon `DocumentRegular`, right slot ContextPaneMenu (Get Started cards, playbook gallery launcher)
- Get Started cards (`GetStartedCardsWidget`, AIPU2-042): renders at welcome stage when no playbook; each card `{ id, label, icon, description }`; click → dispatches `widget_load` to workspace via card's registered handler (e.g., email-compose → Analysis Builder dispatcher)
- Playbook gallery (`PlaybookGalleryWidget`, task 102): shows available playbooks; click → dispatches `playbook-selected` on `conversation` channel; ConversationPane advances Stage 1 → Stage 2

---

## 5. Known limitations / not-shipped from R4 lessons-learned

**Source**: `projects/spaarke-ai-platform-unification-r4/notes/lessons-learned.md`

### Deferred to R5 (explicit)

- **Kiota CVE chain-lock** — Microsoft.Graph 5.x → 6.x major upgrade required; ~1–2 weeks; deferred to `spaarke-graph-sdk-kiota-upgrade-r1`. Two Moderate CVEs (OpenMcdf, OpenTelemetry.Api) patched instead.
- **BFF test-infrastructure cleanup** — ~283 pre-existing test failures (NSubstitute IChatClient streaming mock issues, WebApplicationFactory DI, individual test logic drift) — operator decision 2026-05-27: separate dedicated test-suite project, not in R4.
- **Iframe-wizards pattern project** — `spaarke-iframe-wizard-pattern-enhancement` design.md drafted with binding constraint "NO Power Automate, NO Dataverse plugins" per operator direction; implementation deferred.
- **Residual lint warnings (low priority)**:
  - 20 `react-hooks/exhaustive-deps` (all intentional: refs, run-once, immutable deps)
  - 2 `no-explicit-any` in `DatasetGrid/GridView.tsx` (Fluent v9 callback type non-trivial fix)
- **CommandBar callbacks** — task 081 fixed TypeError by defaulting `commands = []`; may be masking caller-side bugs where consumers should pass empty array instead of undefined.

### Not-shipped UX from architect docs (still in backlog)

Per `SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` and `BUILD-A-NEW-WORKSPACE-WIDGET.md`:

- **Assistant → Workspace mount (W-4)** — DocumentViewerWidget registered + `DOCUMENT_VIEWER_WIDGET_TYPE` exported; **dispatcher call site NOT wired** in Assistant pane.
- **Context → Workspace mount (W-5)** — ContextPaneController can dispatch; **per-wizard final-step handlers NOT implemented**. Create Project wizard recommended demo; unimplemented.
- **Calendar as standalone Direct widget** — Calendar currently ships ONLY as Dashboard section in LegalWorkspace; Direct registration in WorkspaceWidgetRegistry would enable Assistant/Context to dispatch it — not yet done.
- **Stages 2–4 header treatment** — per A-1 in R3/R4 spec, deferred to Moment 2; current header strip minimal (no stage-specific chrome).

---

## 6. FR-to-surface map (R4 spec)

**Source**: `projects/spaarke-ai-platform-unification-r4/spec.md`

### Shipped FRs (all ✅ at code-level)

| FR | Item | Task | UX visibility | Implementation status |
|---|---|---|---|---|
| **FR-01** | W-3 WorkspaceLayoutWizard catalog drift | 040 | Medium — wizard shows all 7 sections in picker (Calendar + Daily Briefing included); custom dashboards composable | ✅ Shipped |
| **FR-02** | W-4 Assistant → Workspace mount source | 042 | **NOT YET** — DocumentViewerWidget registered; dispatcher (convo pane button) incomplete | ⏳ **Partial** |
| **FR-03** | W-5 Context → Workspace mount source | 043 | **NOT YET** — wizard final-step handlers not implemented; no "Add to Workspace" button visible | ⏳ **Partial** |
| **FR-04** | A-4 25 MB chat attachment cap | 050 | High — users upload up to 25 MB in chat (boundary enforced client + server) | ✅ Shipped |
| **FR-05** | A-5 tab persistence verify+fix | 030+031 | Medium — tabs persist across browser refresh + close/reopen via BFF `PATCH /tabs` | ✅ Shipped |
| **FR-06** | B-3 telemetry rename | 062 | None (backend telemetry constant) | ✅ Shipped |
| **FR-07** | B-4 WorkspaceLayoutDto.modifiedOn | 053 | Medium — Manage Workspace pane shows per-layout modified date | ✅ Shipped |
| **FR-08** | B-5 BFF PUT + If-Match weak ETag | 054 | Low — server-side concurrency safety (not user-visible unless simultaneous multi-user edits) | ✅ Shipped |
| **FR-09** | B-6 CalendarSidePane promotion | 055 | None (internal unification; record-form Calendar unchanged) | ✅ Shipped |
| **FR-10** | B-7 useEventsBulkActions hook | 063 | None (internal dedup) | ✅ Shipped |
| **FR-11** | B-8 CalendarDrawer.eventDates API | 064 | Medium — Calendar Drawer badges show event counts + overdue indicators | ✅ Shipped |
| **FR-12** | B-11 type-drift cast cleanup | 067 | None (build cleanliness) | ✅ Shipped |
| **FR-13** | C-3 consolidate useWorkspaceLayouts | 051 | None (internal consolidation) | ✅ Shipped |
| **FR-14** | C-4 WorkspaceRenderer interface | 052 | None (interface abstraction; LegalWorkspaceApp mounts identically) | ✅ Shipped |

**UX-visible count**: 7 of 14 FRs have direct operator-facing changes (FR-01, 04, 05, 07, 08, 11, and indirectly 09 via parity). Remaining 7 are build/governance/internal.

---

## 7. Suggested initial test focus

**High readiness (test meaningfully today)**:

1. **Workspace dropdown + auto-open** — Switch Workspace dropdown lists available layouts; clicking opens as a tab; pinned workspaces auto-open on cold load; tab persistence survives browser refresh and close/reopen. Surfaces complete and shipping.
2. **Attachment upload (25 MB cap)** — Test boundaries: 1 MB / 10 MB / 24 MB succeed; 26 MB rejects with clear error. Chat message + file association end-to-end.
3. **Section visibility in workspace builder** — Open WorkspaceLayoutWizard (+ New Workspace button) → picker shows all 7 sections including Calendar + Daily Briefing; compose a custom layout combining any sections; save and verify it opens correctly.
4. **Pane collapse** (task 094) — click any pane header to collapse to icon-only strip; click strip to re-expand; state persists across refresh. All three panes support independent collapse.

**Medium readiness (partial implementation — caveats apply)**:

5. **Assistant → Workspace integration** — dispatcher infrastructure exists (widget registered, type constant exported); "open as workspace tab" button NOT yet wired in Assistant pane. Code review valuable; UX testing must wait for dispatcher implementation.
6. **Context wizard → Workspace integration** — infrastructure-ready but per-wizard handlers NOT implemented. Create Project wizard recommended; not callable from final step yet.
7. **Tab state serialization** — tabs serialize to BFF on mutation (debounced); restore on cold load from `PATCH /tabs` endpoint. Test close + reopen; edge cases possible in session lifecycle.

**Low readiness (governance / internal — no operator-visible UX)**:

- Build artifacts tracking, TypeScript cleanliness, linting, bundle-size measurement, ADR/doc governance — shipping and correct, but not user-facing.

**Test recommendation**: Start with workspace switching + attachment uploads + section picker. Flag W-4 and W-5 as "design incomplete" pending operator choice of demo scenarios and per-wizard final-step implementation. Tab persistence is testable end-to-end but may surface edge cases in multi-tab close/restore sequences — extended-session testing recommended there.

---

*This file is durable reference for R5 user-testing planning. Update if R5 reshapes the shipped surface (e.g., W-4/W-5 dispatchers land, Calendar gets Direct registration, etc.).*
