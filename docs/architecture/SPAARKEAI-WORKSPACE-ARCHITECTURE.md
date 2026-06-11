# SpaarkeAi Workspace Architecture

> **Purpose**: End-to-end reference for the SpaarkeAi three-pane shell as it stands after Round 13 (Calendar widget polish + all-panes-collapsed empty state + 25/50/25 pane fracs). Documents the cold-load → widget render data flow, component boundaries, auth path, BFF surface, and storage contract.
>
> **Last reviewed**: 2026-05-22 (Task 123, Round 13). Refreshed from task 113 (Round 9) to cover R10–R13 deltas. Periodic review required — this file does NOT auto-update.
>
> **R4 update (2026-05-26)**: The standalone LegalWorkspace code page (`sprk_corporateworkspace` web resource) has been **retired** per operator decision OC-R4-05. LegalWorkspace components, `LegalWorkspaceApp`, and all shared libraries remain in active use — SpaarkeAi is now the only host. See [`LEGALWORKSPACE-RETIREMENT.md`](./LEGALWORKSPACE-RETIREMENT.md) for the retirement decision, consumer audit, and migration guidance.

---

## 1. Big picture

SpaarkeAi is a single Code Page (`sprk_spaarkeai` web resource) that hosts a three-pane shell:

```
+---------------------------------------------------------------------------+
| FluentProvider (theme)                                                    |
|  AppWithAuth (probes auth; renders shell when token mintable)             |
|   ThreePaneShell                                                          |
|    PaneEventBusProvider (single bus instance)                             |
|     AiSessionProvider (session state + SSE→bus routing + auth surface)    |
|      ShellStageManager (4-stage state machine; reads bus events)          |
|       SessionRestoreManager (URL ?sessionId= restore)                     |
|        PaneCollapseContext.Provider (per-pane collapse state)             |
|         ThreePaneLayout                                                   |
|          ┌─────────────┬─────────────────────┬────────────────────┐       |
|          │ Conversation│ Workspace (tabs)    │ Context (Get       │       |
|          │ Pane        │ - tab N: Workspace  │  Started / tool)   │       |
|          │ (chat)      │   = LegalWorkspace  │                    │       |
|          │             │   App(embedded)     │                    │       |
|          └─────────────┴─────────────────────┴────────────────────┘       |
+---------------------------------------------------------------------------+
```

Every tab in the Workspace pane that represents a "workspace" (Daily Briefing, My Work, Documents, Smart To Do List, Corporate Workspace, or any user-created layout) renders **the same `WorkspaceLayoutWidget`**, which mounts **`LegalWorkspaceApp` in embedded mode** with the chosen `initialWorkspaceId`. This unification ("Option B") is the architectural foundation laid in Round 8.

---

## 2. Cold-load pipeline

The full sequence from page request to a rendered widget:

### 2.1 Bootstrap — `src/solutions/SpaarkeAi/src/main.tsx`

1. **Suppress LegalWorkspace's Daily Digest auto-popup** by writing `sessionStorage["spaarke_dailyDigestShown"]` before any React mounts (Task 105, lines 158-180).
2. **Resolve runtime config**:
   - `resolveRuntimeConfig()` from `@spaarke/auth` — reads Dataverse env vars via Xrm (in-MDA) OR localStorage cache (repeat visit).
   - Fallback: anonymous fetch of `GET /api/config/client` against `FALLBACK_BFF_BASE_URL`.
3. `setRuntimeConfig(config)` — into SpaarkeAi's singleton.
4. `setLegalWorkspaceRuntimeConfig(config)` — into LegalWorkspace's SEPARATE singleton so embedded code paths (`getBffBaseUrl()` from preview handlers, `useWorkspaceLayouts` BFF fetch, etc.) find an initialized singleton. Two distinct in-process instances hold equivalent values. See `main.tsx:222-235`.
5. `localStorage.setItem("spaarke-ai-runtime-config", JSON.stringify(config))` — warm the cache for next cold load.
6. `ensureAuthInitialized()` — warms MSAL token cache (silent + popup fallback).
7. Parse URL params (`entityType`, `entityId`, `matterId`, `sessionId`).
8. Render `<App />`.

### 2.2 App + shell — `App.tsx` + `ThreePaneShell.tsx`

`App.tsx` owns theme detection (`resolveCodePageTheme` + `setupCodePageThemeListener`). `AppWithAuth` does an UI-gating auth probe via `getAuthProvider().getAccessToken()` — **NEVER snapshotting the token into React state** (per ADR-028 §H-4; the snapshot was the root cause of the 401-after-idle bug fixed in v2).

`ThreePaneShell.tsx` assembles the provider tree (see diagram above). The three panes are rendered as siblings inside a `<ThreePaneLayout>` from `@spaarke/ui-components`.

### 2.3 Workspace pane mount — `WorkspacePane.tsx`

On mount the WorkspacePane:

1. Constructs `WorkspaceTabManager` (a plain TS class — stable ref across renders).
2. Calls `useWorkspaceLayouts({ bffBaseUrl, authenticatedFetch, isAuthenticated })` (SpaarkeAi-LOCAL hook — see §5 for the dual-hook gap).
3. Restores any persisted tabs (NFR-09 — `GET /api/ai/chat/sessions/{id}/tabs` 404 = benign).
4. Two macrotask-deferred auto-open effects:
   - **Default workspace auto-install** (Wave 2b / Task 109): dispatches `widget_load` for `activeLayout` from the BFF's 4-step cascade (per-user default → Dataverse system default → hard-coded system default → null).
   - **Pinned workspaces auto-open** (Task 101): reads `localStorage["spaarke:workspace:pinned-list"]` and dispatches `widget_load` for each pinned layout NOT already present.

Both effects use `setTimeout(fn, 0)` because `usePaneEvent('workspace', ...)` registers its subscription via a downstream `useEffect`; without the macrotask deferral the dispatch lands on a zero-subscriber channel. See `WorkspacePane.tsx:340-540` block comments for the rationale.

### 2.4 Tab dispatch + widget resolution

The `usePaneEvent('workspace', ...)` subscription in `WorkspacePane` handles `widget_load`:

```
event.type === 'widget_load' && !event.tabId
  → manager.addTab(widgetType, widgetData, displayName)  // FIFO eviction at MAX_WORKSPACE_TABS = 8
  → resolveWorkspaceWidget(widgetType).then(Component)
  → manager.resolveTabComponent(tabId, Component, displayName)
  → dispatch('workspace', { type: 'widget_load', tabId, tabCount })  // ack to ShellStageManager
  → dispatch('workspace', { type: 'tab_count_change', tabCount })    // Stage 3/4 driver
```

`resolveWorkspaceWidget(type)` is a lazy-factory registry (see §3.1). For `type === 'workspace'` it imports `WorkspaceLayoutWidget`, which renders `<LegalWorkspaceApp ... embedded initialWorkspaceId={data.layoutId} />`.

### 2.5 Embedded LegalWorkspaceApp render

`LegalWorkspaceApp` with `embedded={true}` (added in Task 087):
- Skips its internal `<PageHeader>` (which would carry a duplicate workspace dropdown).
- Skips its footer + outer `<FluentProvider>` (the SpaarkeAi shell already provides both).
- Skips cross-device theme sync side effects.
- Mounts `<FeedTodoSyncProvider>` + `<WorkspaceGrid>`.

`WorkspaceGrid` calls **LegalWorkspace's own `useWorkspaceLayouts(initialWorkspaceId)`** (note: a SEPARATE hook from SpaarkeAi's — see §5) which:
1. Fetches `GET /api/workspace/layouts` (cache-first via `sessionStorage`).
2. Selects `initialWorkspaceId` as the active layout.
3. Parses `activeLayout.sectionsJson` into `LayoutJson`.
4. Calls `buildDynamicWorkspaceConfig(layoutJson, SECTION_REGISTRY, factoryContext)` to assemble a `WorkspaceConfig`.
5. Renders `<WorkspaceShell config={config} />` from `@spaarke/ui-components`.

`WorkspaceShell` then renders sections per the layout's `rows` and `sections` arrays. Each section is materialized by its registered factory (e.g. `quickSummary.registration.ts → factory(context) → ContentSectionConfig`).

---

## 3. Component model

### 3.1 Widget registries (`@spaarke/ai-widgets`)

Two parallel registries, both lazy-factory based:

| Registry | File | Fallback | Used by |
|---|---|---|---|
| **WorkspaceWidgetRegistry** | `src/registry/WorkspaceWidgetRegistry.ts` | `GenericTextWidget` (never null) | Workspace pane tabs |
| **ContextWidgetRegistry** | `src/registry/ContextWidgetRegistry.ts` | `null` (caller handles) | Context pane (Get Started / playbook gallery / tool views) |

Registrations are side-effect imports at module load: `src/widgets/workspace/register-workspace-widgets.ts` (16 types) and `src/widgets/context/register-context-widgets.ts`. Each registration provides metadata (`displayName`, `category`, `icon`, `allowMultiple`, `defaultOrder`) and a dynamic-import factory.

The **`'workspace'` widget type** (registration #16, lines 542-566 of `register-workspace-widgets.ts`) is the unified entry point for every Dataverse-defined workspace: it ALWAYS resolves to `WorkspaceLayoutWidget`.

### 3.2 Tab management

| File | Role |
|---|---|
| `WorkspaceTabManager.ts` | Plain TS class — tab state + FIFO eviction at `MAX_WORKSPACE_TABS = 8` + persistence snapshots |
| `WorkspaceTabManagerComponent.tsx` | Renders tab strip + active widget; left/right scroll arrows; close affordance (Task 107) |
| `WorkspacePaneMenu.tsx` | PaneHeader rightSlot — "Switch Workspace" dropdown; pin toggles; "+ New Workspace" wizard launch; Manage workspaces (Task 093) |

### 3.3 PaneEventBus contract

`src/events/PaneEventBus.ts` — typed, multi-subscriber, DOM-free bus with 4 channels:

| Channel | Event types | Dispatchers | Subscribers |
|---|---|---|---|
| `workspace` | `widget_load`, `widget_update`, `widget_action`, `tab_change`, `tab_count_change`, `selection_changed`, `tabs_clear`, `wizard_step`, `entity_resolved`, `session_reset`, `active_widget_changed` | `WorkspacePaneMenu`, `WorkspacePane` (resolve ack), `AiSessionProvider` (SSE → bus), session restore | `WorkspacePane`, `ShellStageManager`, `ContextPaneController` (for `tab_change`) |
| `context` | `context_update`, `context_highlight`, `stage_change` | `AiSessionProvider`, `ContextPaneController` | `ContextPaneController`, citation-highlight viewers |
| `conversation` | `suggestion`, `playbook_change`, `playbook-selected`, `refine_request`, `first_message` | `ConversationPane`, `PlaybookGalleryWidget` | `WorkspacePane` (seed default widgets), `ShellStageManager` (Stage 1→2) |
| `safety` | `safety_annotation`, `capability_change` | `AiSessionProvider` (SSE → bus) | (consumers TBD) |

React hooks: `usePaneEvent(channel, handler)` (subscribe) + `useDispatchPaneEvent()` (returns `(channel, event) => void`).

### 3.4 4-stage shell lifecycle

Driven by `ShellStageManager.tsx` (lives inside `PaneEventBusProvider`):

```
Stage 1 'welcome'      → playbook-selected / playbook_change / first_message
Stage 2 'loading'      → workspace widget_load (with tabId) OR entity_resolved
Stage 3 'active-chat'  → tab_count_change with tabCount >= 2
Stage 4 'review'       (tabCount === 1) → Stage 3
                       (session_reset)  → Stage 1
```

Stage determination is centralized in `StageTransitionRules.determineStage(SessionState)` (from `@spaarke/ai-widgets`). The manager keeps a `SessionState` ref + recomputes after each event.

### 3.5 Section factories (LegalWorkspace-local)

LegalWorkspace defines its sections in `src/sections/*.registration.ts`. Each `SectionRegistration` (from `@spaarke/ui-components`) carries:
- Metadata: `id`, `label`, `description`, `icon`, `category`, `defaultHeight`.
- `factory(context: SectionFactoryContext): SectionConfig` — returns a discriminated union (`content`, `action-cards`, `metric-cards`).

Current 6 registrations: `get-started`, `quick-summary`, `latest-updates`, `todo`, `documents`, `daily-briefing`. The `daily-briefing` factory was hoisted to `@spaarke/ui-components/components/WorkspaceShell/sections/dailyBriefing/` in Task 069 as a `createDailyBriefingRegistration` factory; the LegalWorkspace-local file is now a re-export shim (Task 086).

---

## 4. Auth path

Spaarke Auth v2 (per ADR-028 / INV-1..INV-8):

| Surface | Pattern |
|---|---|
| Bootstrap | `initAuth(...)` then `ensureAuthInitialized()` — single MSAL `PublicClientApplication` with localStorage cache + redirect for top-frame, popup for in-MDA |
| Per-request | `authenticatedFetch(url, init)` — auto-attaches `Bearer`, retries 401 once with backoff, NEVER materializes the token in consumer state |
| URL building | `buildBffApiUrl(baseUrl, path)` — only safe way to construct BFF URLs (handles trailing slash, normalization) |
| In components | `useAuth()` returns `{ isAuthenticated, getAccessToken, authenticatedFetch, tenantId, ... }` — function-based, no token strings cross boundaries |
| In SpaarkeAi | `useAiSession()` (from `@spaarke/ai-widgets/providers/AiSessionProvider`) re-exports the same auth surface alongside session state |

**Xrm.WebApi** (Dataverse-side queries) uses Xrm's own auth — no Bearer header, no @spaarke/auth involvement. Used today for: QuickSummary card counts (`useQuickSummaryCounts.ts:53,65`), Daily Briefing notification context, document list queries inside LegalWorkspace, theme sync.

See §7 of the Componentization Audit for the Xrm.WebApi vs BFF decision criteria.

---

## 5. BFF surface (workspace-relevant endpoints)

Authentication: all routes are `RequireAuthorization()` from `WorkspaceLayoutEndpoints.MapWorkspaceLayoutEndpoints` (route group `/api/workspace`). Anonymous exception: `GET /api/config/client`.

| Method + Route | Handler | Purpose |
|---|---|---|
| `GET /api/config/client` | `ClientConfigEndpoints` | Anonymous — returns `bffBaseUrl`, `msalClientId`, `msalAuthority`, `msalScopes`, `tenantId` for direct-URL bootstrap. |
| `GET /api/workspace/layouts` | `WorkspaceLayoutService.GetLayoutsAsync` | Returns union of hard-coded system + Dataverse-system (`sprk_issystem=true`) + user-owned layouts. Hard-coded first, then Dataverse-system by `sortOrder`, then user by `sortOrder`. |
| `GET /api/workspace/layouts/default` | `GetDefaultLayoutAsync` | 4-step cascade: per-user default → Dataverse system default → hard-coded system default → `null`. Returns 200 with explicit null body when cascade exhausts. |
| `GET /api/workspace/layouts/{id}` | `GetLayoutByIdAsync` | System layouts checked first (no Dataverse round-trip); user layouts gated by `ownerid === userId`; Dataverse-system records visible to all users. |
| `POST /api/workspace/layouts` | `CreateLayoutAsync` | Max 10 user layouts/user; clears existing default if new one is default. |
| `PUT /api/workspace/layouts/{id}` | `UpdateLayoutAsync` | Rejects system layouts (hard-coded OR `IsSystem=true` Dataverse). |
| `DELETE /api/workspace/layouts/{id}` | `DeleteLayoutAsync` | Same system rejection. Hard delete (user confirmed "this cannot be undone"). |
| `GET /api/ai/chat/sessions/{id}/tabs` | `ChatEndpoints` | NFR-09 tab persistence — restore on mount. 404 = benign (no tabs yet). |
| `PATCH /api/ai/chat/sessions/{id}/tabs` | `ChatEndpoints` | NFR-09 tab persistence — debounced write-through every ~200ms. |
| `GET /api/ai/dailybriefing` | `DailyBriefingEndpoints` | LegalWorkspace's Daily Briefing data. |
| `GET /api/ai/chat/context-mappings/standalone` | `ChatEndpoints` | Recommended playbook for resolved entity context. |

The SpaarkeAi `useWorkspaceLayouts` hook (`src/solutions/SpaarkeAi/src/hooks/useWorkspaceLayouts.ts`) and LegalWorkspace's `useWorkspaceLayouts` (`src/solutions/LegalWorkspace/src/hooks/useWorkspaceLayouts.ts`) are TWO SEPARATE files that both call `GET /api/workspace/layouts` + `GET /api/workspace/layouts/default`. They differ in fallback strategy and in whether they parse `sectionsJson` into `LayoutJson`. See §1 of the Componentization Audit.

---

## 6. Dataverse schema

### 6.1 `sprk_workspacelayout` entity

| Column | Type | Purpose |
|---|---|---|
| `sprk_workspacelayoutid` | UniqueIdentifier (PK) | |
| `sprk_name` | String | Display name |
| `sprk_layouttemplateid` | String | Template key matching `@spaarke/ui-components/components/WorkspaceShell/layoutTemplates.ts` (`single-column`, `3-row-mixed`, ...) |
| `sprk_sectionsjson` | Memo | JSON: `{ schemaVersion, rows: [{ id, columns, columnsSmall, sections: [sectionId,...] }] }` |
| `sprk_isdefault` | Boolean | User's chosen default (mutually exclusive across user's layouts) |
| `sprk_sortorder` | Integer | Sort order within the user/system slice |
| `sprk_issystem` | Boolean | System record (Task 108 / Wave 2a). Visible to ALL users; not editable. |
| `ownerid` | Lookup(User/Team) | User isolation for non-system records |
| `statecode` | State | 0 = Active |

### 6.2 The 6 system layouts shipped today

| # | Layout name | Source | `sprk_layouttemplateid` | Sections (sectionsJson) | Default? |
|---|---|---|---|---|---|
| 1 | Corporate Workspace | Hard-coded (`SystemWorkspaceLayouts.cs`) — GUID `00000000-0000-0000-0000-000000000001` | `3-row-mixed` | row-1: get-started + quick-summary; row-2: latest-updates; row-3: todo + documents | No |
| 2 | Daily Briefing | Dataverse-seeded (Task 108) | `single-column` | daily-briefing | **YES (dev seed)** |
| 3 | Smart To Do List | Dataverse-seeded | `single-column` | todo | No |
| 4 | My Work | Dataverse-seeded | `single-column` | quick-summary (6 cards: My Matters, My Projects, Assign Work, Open Tasks, Communications, Invoices — task 110) | No |
| 5 | Documents | Dataverse-seeded | `single-column` | documents | No |
| 6 | Calendar | Dataverse-seeded (Task 115, Round 9) | `single-column` | calendar | No |

The 5 Dataverse-seeded layouts come from `scripts/system-layouts.json` via `Deploy-SystemWorkspaceLayouts.ps1`. Operator decision Option B (2026-05-22): every workspace — hard-coded, Dataverse-system, user-created — flows through the same `widget_load → WorkspaceLayoutWidget → LegalWorkspaceApp(embedded) → section factories` pipeline.

**Calendar is architecturally distinct from the other 5 sections** (task 115): the Calendar section is a 62-line REGISTRATION SHIM in `src/solutions/LegalWorkspace/src/sections/calendar.registration.ts` that delegates rendering entirely to `CalendarWorkspaceWidget` from the shared `@spaarke/events-components` library (task 114). The other 5 sections embed via the same `LegalWorkspaceApp(embedded)` pipeline but their factories reach into LegalWorkspace-local components + `DataverseService` + `FeedTodoSyncContext`. Calendar's "shared-lib widget + thin LW section shim" pattern is the **proven canonical model** for future widgets that should be reusable across non-LegalWorkspace hosts. See the [componentization audit §2A](./SPAARKEAI-COMPONENTIZATION-AUDIT.md) for the implications.

### 6.3 Per-BU container + index routing (multi-container, multi-index)

As of project `spaarke-multi-container-multi-index-r1`, document storage and search indexing are **record-scoped**: the SharePoint Embedded container + Azure AI Search index are chosen per record at create time, not as a tenant-wide singleton. Parent records (`sprk_matter`, `sprk_project`, `sprk_invoice`, `sprk_workassignment`, `sprk_event`) and `sprk_document` carry their own `sprk_containerid` + `sprk_searchindexname` (Document's canonical container reference is `sprk_graphdriveid`).

**Cascade at create time** (Spaarke Create Wizards — `CreateMatterWizard`, `CreateProjectWizard`, `CreateInvoiceWizard`, `CreateWorkAssignmentWizard`, `CreateEventWizard`, plus `DocumentUploadWizard`):

1. Wizard reads owning Business Unit's `sprk_containerid` + `sprk_searchindexname`.
2. For Documents, the wizard reads the parent record's values first, then falls back to the parent's BU.
3. Explicit overrides on the create form persist (e.g., a "Protected Matter" with `sprk_searchindexname = "spaarke-file-index"` keeps that value).
4. No Dataverse plugins, no Power Automate flows, no new field mappings — the wizards are the canonical cascade mechanism.

**Resolution at search time** (`IKnowledgeDeploymentService.GetSearchClientAsync` — see `src/server/api/Sprk.Bff.Api/Services/Ai/IKnowledgeDeploymentService.cs`):

1. Client (PCF `SemanticSearchControl` v1.1.74 or the `sprk_semanticsearch` Code Page) sends the record's `sprk_searchindexname` in the search request.
2. BFF validates against the static `appsettings.AiSearch.AllowedIndexes` allow-list. A miss returns `400 INDEX_NOT_ALLOWED` ProblemDetails (per ADR-019).
3. Empty / null `searchIndexName` falls back to the existing 2-tier chain: `sprk_aiknowledgedeployment` Dataverse entity → `appsettings.AiSearch.KnowledgeIndexName`.

**Coexistence is the design.** Changing a BU's `sprk_searchindexname` does NOT propagate to existing records — old records continue to point at their original container/index; only new records get the new BU default. This enables the migration scenario (old documents stay in the old index, new ones land in the new) without any sync engine. See design invariants INV-1..INV-8 in [`projects/spaarke-multi-container-multi-index-r1/design.md`](../../projects/spaarke-multi-container-multi-index-r1/design.md) §3 for the binding contracts; see [`docs/guides/MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md`](../guides/MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md) for operator procedures (assigning indexes to BUs, marking records Protected, drift handling, adding new physical indexes, backfill).

---

## 7. Storage contract (localStorage / sessionStorage)

| Key | Type | Owner | Purpose |
|---|---|---|---|
| `spaarke-ai-runtime-config` | localStorage | SpaarkeAi `main.tsx` | Cached `IRuntimeConfig` from BFF — eliminates BFF round-trip on repeat visits |
| `spaarke:workspace:pinned-list` | localStorage | `services/pinnedWorkspaces.ts` | Ordered JSON array of pinned workspaces — auto-opens on cold load (Task 092 / 101) |
| `spaarke:workspace:context-tool` | localStorage | `services/contextToolPin.ts` | User's chosen Context-pane tool (persists across sessions) |
| `spaarke:panes:collapsed` | localStorage | `hooks/usePaneCollapse.ts` | Per-pane collapse state (Task 094) |
| `spaarke-theme` | localStorage | `@spaarke/ui-components/theme` | Theme preference (light/dark) |
| `spaarke_dailyDigestShown` | sessionStorage | `main.tsx` (Task 105 suppression) + LegalWorkspace `useDailyDigestAutoPopup` | Suppress Daily Briefing modal auto-popup in embedded mode |
| `sprk_ai2_chatSessionId` | sessionStorage | `AiSessionProvider` | Active chat session ID |
| `sprk_ai2_playbookId` | sessionStorage | `AiSessionProvider` | Active playbook ID |
| `lw-layout-cache-*` | sessionStorage | LegalWorkspace `layoutCache.ts` | Cached workspace layouts list + active layout |
| `spaarke-ai-r2-shell` (key namespace) | sessionStorage | `ThreePaneLayout` | Pane split widths. Concrete keys: `spaarke-ai-r2-shell-left-width-px`, `spaarke-ai-r2-shell-right-width-px` |
| `spaarke:calendar:collapsed` | localStorage | `CalendarWorkspaceWidget` (task 116) | Persists the calendar-strip collapse toggle so the strip stays hidden across reloads when the operator collapses it. Filter row remains visible when collapsed (task 118). |

### 7.1 Pane-width precedence (task 117)

`ThreePaneLayout` now resolves initial pane widths via a three-tier precedence chain (per `useThreePaneLayout.resolveInitialWidth`):

1. **Stored pixel width** — sessionStorage `${storageKey}-left-width-px` / `${storageKey}-right-width-px`. Whatever the user last dragged. Always wins on remount.
2. **Frac × `window.innerWidth`** — applied only on cold mount when no stored value is present, when the corresponding `defaultLeftWidthFrac` / `defaultRightWidthFrac` prop is defined and finite > 0, and when `typeof window !== 'undefined'`. SpaarkeAi passes `defaultLeftWidthFrac={0.25}` + `defaultRightWidthFrac={0.25}` → 25/50/25 of the current viewport.
3. **Legacy `defaultLeftWidthPx` / `defaultRightWidthPx`** — fixed pixel fallback for SSR / non-browser environments. SpaarkeAi still passes 340 / 400 here so the chain has a final fallback.

LegalWorkspace does NOT consume `ThreePaneLayout` at all (it uses `WorkspaceGrid`-based section rendering) — confirmed in task 117. The frac props are SpaarkeAi-only today.

### 7.2 All-panes-collapsed empty state (task 119)

When the user simultaneously collapses all three panes (Assistant + Workspace + Context — three 48px strips clustered against the left edge), `ThreePaneLayout` renders an empty-state overlay as a sibling of the three collapsed strips:

- Layout: `flex: 1 1 auto`, vertical-centered column, `colorNeutralBackground2`.
- Visual: 64px `EmojiSmileSlight24Regular`, "Welcome back" text (`fontSizeBase400`, `colorNeutralForeground3`), primary `Button` labeled "Open" (autoFocused for keyboard-Enter recovery).
- ARIA: `<div role="region" aria-label="All panes are collapsed">`.
- Action: clicking "Open" calls `resetToFracDefaults()` on `useThreePaneLayout` (recomputes left/right widths from `frac × window.innerWidth`, clamped to min, AND persists the new pixel values to sessionStorage so they OVERWRITE any prior user-dragged values), then guarded `onToggleLeft/Center/Right(true)` calls to un-collapse all three panes.
- Operator semantics: "force 25/50/25 always" — discards any previously user-dragged widths intentionally.

The overlay is contained entirely inside `ThreePaneLayout`; no SpaarkeAi-side change was required (ThreePaneShell already wires the collapse states + toggle callbacks since task 094).

---

## 8. ADR + constraint pointers

- **ADR-006** — Code Pages for standalone dialogs (not PCF)
- **ADR-012** — Shared component library structure (`@spaarke/ui-components`, `@spaarke/ai-widgets`)
- **ADR-013** — AI Architecture: extend BFF, not separate service
- **ADR-021** — Fluent v9 tokens only, dark mode required, semantic tokens only
- **ADR-022** — React 19 createRoot for Code Pages
- **ADR-026** — Vite + vite-plugin-singlefile for Code Pages
- **ADR-028** — Spaarke Auth v2 (function-based; no token snapshots) — load before touching auth
- **`.claude/constraints/bff-extensions.md`** — binding pre-merge checklist for any BFF additions (CLAUDE.md §10)

---

## 9. Where to start reading code

| Question | Start here |
|---|---|
| How does cold load resolve the default tab? | `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx:340-440` |
| How is the embedded LegalWorkspace wired? | `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/WorkspaceLayoutWidget.tsx` + `src/solutions/LegalWorkspace/src/LegalWorkspaceApp.tsx:135-188` |
| How does the BFF merge system + user layouts? | `src/server/api/Sprk.Bff.Api/Services/Workspace/WorkspaceLayoutService.cs:83-114` |
| How is the default layout cascade implemented? | Same file, `GetDefaultLayoutAsync` — `WorkspaceLayoutService.cs:202-309` |
| How is auth injected without snapshots? | `src/client/shared/Spaarke.AI.Widgets/src/providers/AiSessionProvider.tsx:100-120` + `@spaarke/auth/useAuth` |
| How are widgets resolved at runtime? | `src/client/shared/Spaarke.AI.Widgets/src/registry/WorkspaceWidgetRegistry.ts` (lazy-factory + cache) |
| How does stage transition work? | `src/solutions/SpaarkeAi/src/components/shell/ThreePaneShell.tsx:245-398` (ShellStageManager) |

---

## 10. Related docs

- [`SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](./SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) — **authoritative two-wrapper model** (R4 DR-01): three surfaces, Dashboard wrapper (`LegalWorkspaceApp`) vs Direct widget wrapper (`WorkspaceWidgetRegistry`), four mount sources, dual-use pattern, LegalWorkspace-as-dashboard-engine framing. Read this before designing a new widget.
- [`SPAARKEAI-COMPONENT-MODEL.md`](./SPAARKEAI-COMPONENT-MODEL.md) — inventory of shared libs + solution-local components + PaneEventBus contract
- [`SPAARKEAI-COMPONENTIZATION-AUDIT.md`](./SPAARKEAI-COMPONENTIZATION-AUDIT.md) — honest assessment of reuse + gaps
- [`../guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) — step-by-step tutorial (worked example: Calendar widget)
- [`AI-ARCHITECTURE.md`](./AI-ARCHITECTURE.md) — broader AI pipeline (orchestration, tool catalog)
- [`AUTH-AND-BFF-URL-PATTERN.md`](./AUTH-AND-BFF-URL-PATTERN.md) — auth + URL pattern reference
- [`code-pages-architecture.md`](./code-pages-architecture.md) — Code Page packaging

---

## 11. Document changelog

- **2026-05-22 (task 113)**: initial publication (post-R9 state). Documented the cold-load pipeline, embedded LegalWorkspaceApp pattern, 4-stage shell lifecycle, BFF surface, Dataverse schema, 5 system layouts, and storage contract.
- **2026-05-22 (task 123)**: refreshed through R13. Added Calendar widget (task 115) as 6th system layout + its distinct "shared-lib widget + thin LW shim" architecture, `@spaarke/events-components` shared lib (task 114), pane-width fracs precedence chain (task 117), all-panes-collapsed overlay UX + `resetToFracDefaults()` recovery (task 119), `spaarke:calendar:collapsed` localStorage key (task 116), QuickSummary 6-card expansion (task 110). Calendar widget polish history (tasks 116/118/120/121/122) is captured in the componentization audit and the build-a-widget guide; this file references the architectural surface only.
- **2026-05-26 (R4 task 010 / W-1)**: cross-linked to new authoritative two-wrapper architecture doc `SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` (R4 DR-01). This doc remains the cold-load → render pipeline reference; the new doc establishes the mental model (surfaces / wrappers / mount sources / dual-use / LegalWorkspace-as-engine) that future widget authors apply on top of this pipeline.
- **2026-06-07 (project `spaarke-multi-container-multi-index-r1` task 061 / FR-DOC-02)**: added §6.3 "Per-BU container + index routing" describing record-scoped storage + search routing (containers + AI Search indexes selected per record at create time via the 5 parent-record create wizards + DocumentUploadWizard), BFF allow-list validation (`400 INDEX_NOT_ALLOWED`), and INV-3 coexistence model. Cross-linked to design.md §3 invariants + the new operator runbook.
