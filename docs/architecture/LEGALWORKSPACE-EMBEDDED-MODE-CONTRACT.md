# LegalWorkspace Embedded-Mode Host Contract

> **Purpose**: Authoritative, testable contract for any host page embedding `LegalWorkspaceApp` in embedded mode (`<LegalWorkspaceApp embedded ... />`). Lists every host obligation as a MUST with a verification step and a reference-implementation pointer into SpaarkeAi.
>
> **Status**: New architectural reference doc introduced in R4 (DR-07 / C-2).
> **Last reviewed**: 2026-05-26 (R4 task 015 / C-2 — initial publication).
> **Audience**: Authors of future hosts that want to embed the LegalWorkspace dashboard engine; SpaarkeAi maintainers when modifying the existing embed path.
> **Predecessor framing**: This doc supersedes any ad-hoc / tribal understanding of "how SpaarkeAi embeds LegalWorkspace." If you are extending the embed, this contract is now binding.
>
> **Required reading before this doc**:
> - [`SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](./SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) — establishes the two-wrapper model and the LegalWorkspace-as-dashboard-engine framing this contract sits inside. Read §2.1 (Dashboard wrapper) and §5 (LegalWorkspace as the dashboard engine) before reading §1 here.
> - [`SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](./SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — cold-load → widget render pipeline that the embedded LegalWorkspaceApp slots into.
> - [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — function-based auth (`authenticatedFetch`, no token snapshots). The `webApi` shim and BFF call paths in §4 and §5 below assume ADR-028 compliance.

---

## 0. LW retirement context — read first

Per scoping decision **OC-R4-05** (2026-05-25), the standalone LegalWorkspace Code Page (`sprk_corporateworkspace`) is **retired in R4** — see [`SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](./SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) §5 and (when published) `LEGALWORKSPACE-RETIREMENT.md`. The components remain as a library, but the standalone deploy is gone. **SpaarkeAi is the only host of `LegalWorkspaceApp` today.**

So why write a host contract for one host?

1. **The contract is implicit and unwritten today.** Every requirement enumerated below was discovered organically during R3 / R4 implementation. If a future host (Outlook side pane, Teams app, a second Code Page) wants to embed the dashboard engine, it needs this doc as its checklist.
2. **The single existing host is itself test #1.** The reference-implementation pointers below let a maintainer verify SpaarkeAi still satisfies every clause when modifying its bootstrap, theme, or mount path. A regression in SpaarkeAi's compliance is detectable against this contract.
3. **R3 FR-25 / NFR-10 ("standalone LegalWorkspace continues to function identically") is superseded** — so requirements about "preserve standalone bundle byte-for-byte" are NOT host requirements; they were a constraint on *what could change inside `LegalWorkspaceApp`*. That constraint is gone. This contract is purely forward-going.

Read every MUST below as "any host that mounts `<LegalWorkspaceApp embedded ... />` MUST..."; SpaarkeAi is the worked example.

---

## 1. Contract overview

`LegalWorkspaceApp` is the dashboard rendering engine of the Spaarke platform (see [`SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](./SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) §5.2). In embedded mode it expects the host to own:

| Concern | Host owns | LegalWorkspaceApp owns |
|---|---|---|
| Config init | YES — both SpaarkeAi singleton AND LegalWorkspace singleton | NO |
| Theme + `FluentProvider` | YES — exactly one outer `FluentProvider` | NO (skips its own when `embedded=true`) |
| Cross-device theme sync side effects | YES | NO (skips when `embedded=true`) |
| `Xrm.WebApi` access (the `webApi` prop) | YES — host resolves and injects | NO |
| `userId` (Dataverse user GUID) | YES — host resolves and injects | NO |
| Mount/unmount lifecycle | YES — host decides when to mount/unmount | NO |
| SessionStorage sentinel suppression (where needed) | YES — before any React tree mounts | NO |
| `PageHeader` chrome / workspace dropdown / footer | NO — embedded mode skips these | NO (deliberately, when `embedded=true`) |
| Layout fetch + section rendering | NO | YES — once `initialWorkspaceId` arrives |
| `FeedTodoSyncProvider` context | NO | YES — internal to LegalWorkspaceApp |

If a host gets ANY column above wrong, the embedded app fails — often loudly (config-init throw), sometimes silently (double `FluentProvider`, drifting theme, doubled side effects).

The six contract sections that follow (§2–§7) state each MUST in testable form with the SpaarkeAi reference impl.

---

## 2. Config initialization (DUAL singleton)

LegalWorkspace ships its OWN `runtimeConfig` singleton (`src/solutions/LegalWorkspace/src/config/runtimeConfig.ts`). This singleton is distinct from any host singleton (e.g. SpaarkeAi's `src/solutions/SpaarkeAi/src/config/runtimeConfig.ts`). Both must be initialized before the embedded tree renders, or LegalWorkspace internals throw at first access of `getBffBaseUrl()`.

### 2.1 MUST: Initialize the LegalWorkspace singleton BEFORE mounting `<LegalWorkspaceApp embedded ... />`

**Rationale**: `LegalWorkspaceApp`'s internal code paths (`useWorkspaceLayouts` BFF fetch, navigateTo handlers, document preview) call `getBffBaseUrl()`, which throws `[LegalWorkspace] Runtime config not initialized` if the singleton is uninitialized. Initializing only the host singleton is insufficient.

**Verification**: With the host running but without calling `setLegalWorkspaceRuntimeConfig(...)`, click any action inside an embedded `<LegalWorkspaceApp />` that issues a BFF call (e.g. open a workspace, open a document). The console MUST NOT show `[LegalWorkspace] Runtime config not initialized`. If it does, the host is non-compliant.

**Reference implementation** (SpaarkeAi):

- `src/solutions/SpaarkeAi/src/main.tsx` lines 50–60 — imports `setLegalWorkspaceRuntimeConfig` from `@spaarke/legal-workspace`.
- `src/solutions/SpaarkeAi/src/main.tsx` lines 217–235 — after `setRuntimeConfig(config)` for SpaarkeAi's own singleton, calls `setLegalWorkspaceRuntimeConfig(config)` with the SAME `IRuntimeConfig`. Both singletons hold equivalent values; they remain distinct in-process instances by design.
- `src/solutions/LegalWorkspace/src/index.ts` lines 23–47 — barrel export of `setRuntimeConfig as setLegalWorkspaceRuntimeConfig`.
- `src/solutions/LegalWorkspace/src/config/runtimeConfig.ts` lines 25–54 — singleton implementation + `[LegalWorkspace] Runtime config not initialized` error.

### 2.2 MUST: Pass an identical `IRuntimeConfig` to both singletons

**Rationale**: If the host and embedded singletons disagree on `bffBaseUrl`, `bffOAuthScope`, `msalClientId`, or `tenantId`, calls from the host tree hit one BFF / scope and calls from inside the embedded tree hit another. The auth handshake (ADR-028) then fails because the access token's audience / scope doesn't match the URL being called.

**Verification**: After bootstrap, evaluate both `<HostNamespace>.getRuntimeConfig()` and `<LegalWorkspace>.getBffBaseUrl()` in DevTools. Both MUST return identical `bffBaseUrl`. Both `bffOAuthScope` values MUST be identical. Both `msalClientId` and `tenantId` MUST be identical.

**Reference implementation**: SpaarkeAi passes the same `config: IRuntimeConfig` object to `setRuntimeConfig()` (host) and `setLegalWorkspaceRuntimeConfig()` (embedded) — `src/solutions/SpaarkeAi/src/main.tsx` line 217 (host) immediately followed by line 226 (embedded) inside the same `bootstrap()` function.

### 2.3 MUST: Resolve config via `resolveRuntimeConfig()` from `@spaarke/auth` (not a hand-rolled fetch)

**Rationale**: `@spaarke/auth`'s `resolveRuntimeConfig()` handles the multi-path resolution (Xrm env vars → localStorage cache → BFF `/api/config/client` anonymous endpoint) and sets the `window.__SPAARKE_BFF_BASE_URL__` / `window.__SPAARKE_MSAL_CLIENT_ID__` globals that MSAL consults. A hand-rolled fetch path will miss the localStorage caching contract and the window-global side effect.

**Verification**: In DevTools after bootstrap, `window.__SPAARKE_BFF_BASE_URL__` MUST be set to the resolved BFF base URL. Reload the page — bootstrap must succeed without re-fetching `/api/config/client` (localStorage cache hit). Clear localStorage, reload — bootstrap must successfully fetch `/api/config/client`.

**Reference implementation**: `src/solutions/SpaarkeAi/src/main.tsx` lines 174–215 — full multi-path resolution with try/catch fall-through to the BFF fetch. Caches resolved config to `localStorage.setItem("spaarke-ai-runtime-config", ...)` for subsequent visits (lines 239–243).

---

## 3. Theme ownership (single `FluentProvider`)

`LegalWorkspaceApp` deliberately skips its own outer `FluentProvider` when `embedded={true}` — see `src/solutions/LegalWorkspace/src/LegalWorkspaceApp.tsx` lines 180–187. This assumes the host already wraps its tree in exactly one `FluentProvider` with a resolved theme.

### 3.1 MUST: The host MUST provide exactly one outer `FluentProvider` ancestor

**Rationale**: Double-wrapping in `FluentProvider` is known to subtly disrupt token propagation through nested portals (popovers, dialogs, drawers). Embedded mode was added precisely to avoid this double-wrap (see `LegalWorkspaceApp.tsx` lines 180–185 — explicit comment about double-provider risk).

**Verification**:
1. Open DevTools, locate the rendered embedded LegalWorkspaceApp tree.
2. Walk the React tree upward from inside the embedded mount: there MUST be exactly ONE `FluentProvider` ancestor between the embedded `LegalWorkspaceApp` and the root.
3. Any popover or dialog opened from inside the embedded tree (e.g. Quick Summary expand dialog, document preview) MUST render with correct Fluent v9 tokens and respond to theme toggle without visual artifacts.

**Reference implementation** (SpaarkeAi):

- `src/solutions/SpaarkeAi/src/App.tsx` line 164 — sole `<FluentProvider theme={theme}>` wraps the entire SpaarkeAi React tree, including all three panes.
- `src/solutions/SpaarkeAi/src/App.tsx` lines 5–7 — code comment confirms theme ownership lives in App.tsx; ThreePaneShell receives it via props.
- `src/solutions/SpaarkeAi/src/components/shell/ThreePaneShell.tsx` lines 551–564 — explicit comment "FluentProvider is owned by App.tsx".

### 3.2 MUST: The host MUST NOT mount LegalWorkspaceApp with `embedded={false}` inside another `FluentProvider`

**Rationale**: With `embedded={false}` (default), `LegalWorkspaceApp` adds its OWN outer `FluentProvider` (line 187). If the host has its own provider too, the rendered tree has two — the failure mode §3.1 was designed to prevent.

**Verification**: Search every `<LegalWorkspaceApp ` instantiation in the host. Every one MUST pass `embedded` (truthy) when there is a host `FluentProvider` ancestor. The only legal "no `embedded` prop" use is the standalone LegalWorkspace Code Page, which is **retired in R4** (no longer applies).

**Reference implementation**: `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/WorkspaceLayoutWidget.tsx` lines 173–185 — `<LegalWorkspaceApp ... embedded />` (the boolean shorthand for `embedded={true}`). The only mount path in SpaarkeAi today.

### 3.3 MUST: The host MUST own cross-device theme sync (Dataverse round-trip)

**Rationale**: `LegalWorkspaceApp` skips its `syncThemeFromDataverse` / `persistThemeToDataverse` / `THEME_CHANGE_EVENT` listener when `embedded={true}` (lines 103–120). These side effects are intentionally suppressed because the host already runs equivalent logic; running them twice causes doubled Dataverse writes and double-fire of theme-change listeners.

**Verification**: With an embedded LegalWorkspaceApp mounted, change theme in the host. The Dataverse user-pref record MUST be updated exactly ONCE per change (no doubled writes). Reload the page in a fresh browser session — the theme persists. Open the same user account in a second browser — theme syncs across devices.

**Reference implementation**: SpaarkeAi's `App.tsx` uses `@spaarke/ui-components`'s `useTheme()` hook + the global `THEME_CHANGE_EVENT` listener, which fires `persistThemeToDataverse` once per change at the App.tsx layer (the same layer where `FluentProvider` lives). The embedded `LegalWorkspaceApp` correctly defers.

---

## 4. SessionStorage sentinels

`LegalWorkspaceApp` and its descendant components rely on `sessionStorage` keys for cache + suppression behavior. Some keys must be SET by the host before mount (suppression), some are owned by LegalWorkspace and merely READ (cache), and some can be invalidated by the host on certain events.

### 4.1 Known sessionStorage keys

| Key | Owner | Purpose | Host obligation |
|---|---|---|---|
| `spaarke_dailyDigestShown` | LegalWorkspace (`useDailyDigestAutoPopup` hook) | Once-per-session sentinel; when truthy, suppresses the Daily Digest modal auto-popup | SET before any React tree mounts, in hosts that do NOT want the auto-popup |
| `sprk:workspace:activeLayout` | LegalWorkspace (`layoutCache.ts`) | Cached active workspace layout for instant rerender | None to set; OPTIONALLY invalidate via `invalidateLayoutCache()` after wizard saves |
| `sprk:workspace:layoutsList` | LegalWorkspace (`layoutCache.ts`) | Cached full layouts list | Same as above |
| `BANNER_DISMISSED_KEY` (workspace error banner) | LegalWorkspace (`WorkspaceLoadingStates.tsx`) | Once-per-session "user dismissed the warning banner" | None |

### 4.2 MUST: Hosts that suppress the LegalWorkspace Daily Digest auto-popup MUST set `spaarke_dailyDigestShown` before any React tree mounts

**Rationale**: `useDailyDigestAutoPopup` (in `src/solutions/LegalWorkspace/src/hooks/useDailyDigestAutoPopup.ts`) unconditionally fires inside `WorkspaceGrid` on mount. Its guard is `if (sessionStorage.getItem(SESSION_KEY)) return;` (line 66) where `SESSION_KEY = "spaarke_dailyDigestShown"` (line 33). If the host wants to prevent the modal from auto-launching every cold-load (because the host renders Daily Briefing inline, or the host's UX otherwise replaces the popup), the only safe suppression is to set the sentinel BEFORE the React tree mounts.

**Verification**: Cold-load the host (clear sessionStorage, then visit). Wait for the embedded LegalWorkspaceApp to render. The Daily Digest modal MUST NOT appear automatically. The inline Daily Briefing section (if the host renders one) MUST appear normally.

**Reference implementation** (SpaarkeAi):

- `src/solutions/SpaarkeAi/src/main.tsx` lines 159–171 — defines `DAILY_DIGEST_SESSION_KEY` and the `suppressLegalWorkspaceDailyDigestAutoPopup()` function.
- `src/solutions/SpaarkeAi/src/main.tsx` line 180 — calls the suppression function as the first statement inside `bootstrap()`, before `resolveRuntimeConfig()` and certainly before any `createRoot().render(...)` call.
- The flag value `"suppressed-by-spaarkeai"` is informational; the hook only checks truthiness. Existing operator opt-outs (`"opted-out"`, `"shown"`) are preserved by the `if (!sessionStorage.getItem(...))` check in the suppression function.

### 4.3 MUST: Hosts that perform workspace-layout mutations outside the embedded `WorkspaceLayoutWizard` MUST invalidate the layout cache

**Rationale**: `sprk:workspace:activeLayout` and `sprk:workspace:layoutsList` are populated by `useWorkspaceLayouts` on a successful BFF fetch. If the host has its own UI for renaming, deleting, or reordering layouts (e.g. a host-level Manage Workspaces pane), failing to call `invalidateLayoutCache()` will result in stale data being rendered on the next embedded mount.

**Verification**: Mutate a layout via the host's own UI (rename, delete, reorder), then navigate to or remount the embedded `LegalWorkspaceApp`. The change MUST be reflected immediately; no stale layout data may appear.

**Reference implementation**: `src/solutions/LegalWorkspace/src/workspace/layoutCache.ts` lines 79–86 — `invalidateLayoutCache()` function clears both keys. SpaarkeAi's `WorkspacePane` / `WorkspaceLayoutWizard` chains both call this after a successful save.

### 4.4 MUST: Hosts MUST NOT write to LegalWorkspace-owned sessionStorage keys for any purpose other than the suppression / invalidation patterns above

**Rationale**: The cache keys are shape-versioned by the `WorkspaceLayoutDto` interface. Writing arbitrary data to `sprk:workspace:activeLayout` will cause the `JSON.parse` inside `layoutCache.ts` to either throw (caught and returns null) or produce a malformed layout that crashes `WorkspaceShell`. Future cache-shape changes are owned by LegalWorkspace, not the host.

**Verification**: Search the host codebase for `sprk:workspace:` and `spaarke_dailyDigestShown` literals. Every write site MUST either be the §4.2 suppression call or a `invalidateLayoutCache()` invocation. No raw writes.

**Reference implementation**: SpaarkeAi has zero raw writes to LegalWorkspace-owned keys outside `main.tsx`'s single suppression call. Grep `src/solutions/SpaarkeAi/` for `sprk:workspace:` returns no results.

---

## 5. `webApi` shim (Xrm.WebApi binding)

`LegalWorkspaceApp` takes `webApi: IWebApi` as a required prop (`src/solutions/LegalWorkspace/src/LegalWorkspaceApp.tsx` lines 23, 85). The interface is defined in `src/solutions/LegalWorkspace/src/types/xrm.ts` and is a strict subset of both `ComponentFramework.WebApi` (PCF) and `Xrm.WebApi` (Code Pages / model-driven apps): `retrieveMultipleRecords`, `retrieveRecord`, `createRecord`, `updateRecord`, `deleteRecord`. The host MUST supply a concrete implementation.

### 5.1 MUST: The host MUST resolve `Xrm.WebApi` (or an equivalent `IWebApi` implementation) and pass it as the `webApi` prop

**Rationale**: LegalWorkspace's internal `DataverseService`, section factories (Quick Summary, Documents, Latest Updates, Smart To Do, etc.), and `FeedTodoSyncContext` all consume `webApi` for Dataverse reads/writes. Without it, every section either renders an empty state, throws on render, or crashes during data fetch. Note that BFF calls (per ADR-028) use `authenticatedFetch` from `@spaarke/auth` and are NOT routed through `webApi` — `webApi` is exclusively for direct Dataverse access via the Web API.

**Verification**: Open DevTools after the embedded LegalWorkspaceApp mounts. Trigger any section that lists Dataverse records (e.g. Documents tab, Latest Updates). Network tab MUST show requests against the Dataverse Web API (e.g. `https://<org>.crm.dynamics.com/api/data/v9.x/sprk_documents?...`). No `TypeError: Cannot read properties of null (reading 'retrieveMultipleRecords')` or similar.

**Reference implementation** (SpaarkeAi via `WorkspaceLayoutWidget`):

- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/WorkspaceLayoutWidget.tsx` lines 91–135 — inline `locateXrm()` / `getWebApiSafe()` / `getUserIdSafe()` frame-walk (window → window.parent → window.top). This duplicates LegalWorkspace's own xrmProvider intentionally (lines 87–89 explain) so the widget bundle doesn't drag the LW internal helper into the shared lib closure.
- Lines 156–157 — `webApi` and `userId` resolved once with `useMemo`.
- Lines 161–171 — dev fallback: when Xrm is unavailable (e.g. Vite `npm run dev`), render an empty-state message instead of crashing inside LegalWorkspaceApp.
- Lines 173–185 — actual `<LegalWorkspaceApp ... webApi={webApi} userId={userId} ... />` mount.

### 5.2 MUST: The host MUST also resolve and pass `userId` (Dataverse user GUID, brace-stripped)

**Rationale**: `LegalWorkspaceApp` line 93 sanitizes braces (`userId?.replace(/[{}]/g, '') ?? ''`) but cannot recover from an empty / undefined `userId`. Many sections (Smart To Do, MyPortfolio, ActivityFeed) filter by current user and will show wrong results — or no results — if `userId` is missing.

**Verification**: In DevTools, log `props.userId` from inside the host's `LegalWorkspaceApp` mount call (or set a temporary debug-prop in the host). The value MUST be a Dataverse user GUID (e.g. `12345678-1234-1234-1234-123456789012`), brace-stripped, lowercased per Dataverse convention.

**Reference implementation**: `WorkspaceLayoutWidget.tsx` lines 124–135 — `getUserIdSafe()` walks `Xrm.Utility.getGlobalContext().getUserId()` → `ctx.userSettings.userId` → `Xrm.userSettings.userId`, stripping braces in all paths. Returns `""` if no Xrm context, which the dev-fallback empty state at line 161 catches.

### 5.3 MUST: The host MUST NOT inject a mock or shimmed `IWebApi` in production

**Rationale**: `IWebApi` defines a Dataverse data contract. A mock implementation will either return synthetic data (misleading sections) or throw (broken sections). Mocks are appropriate ONLY for dev-loop testing (Vite dev server, Storybook). Hosts MUST detect the no-Xrm condition and render a clear empty state (per §5.1's dev-fallback pattern) instead of papering it over with a fake `webApi`.

**Verification**: Search the host for any constant or function with a name like `mockWebApi`, `fakeWebApi`, `stubWebApi`. None of these may flow into the `webApi` prop on a production code path.

**Reference implementation**: `WorkspaceLayoutWidget.tsx` lines 161–171 — explicit "if `!webApi` render empty state" guard. No mock injection.

### 5.4 MUST: BFF calls inside the embedded LegalWorkspaceApp MUST go through `@spaarke/auth`'s `authenticatedFetch` (per ADR-028)

**Rationale**: ADR-028 forbids token snapshots and raw `fetch(...)` with hand-built `Authorization` headers. The embedded LegalWorkspaceApp inherits this constraint — its `useWorkspaceLayouts` BFF fetch, document preview calls, and any future BFF-backed sections route through `authenticatedFetch`. The host does NOT need to inject auth credentials; LegalWorkspace's calls implicitly use whichever `@spaarke/auth` MSAL provider is configured at the singleton level (initialized per §2).

**Verification**: Inspect the Network tab during embedded operation. Every BFF call (anything against `<bffBaseUrl>/api/...`) MUST carry a `Authorization: Bearer <jwt>` header. Tokens MUST refresh on 401 (ADR-028 retry semantics). No `window.__SPAARKE_BFF_TOKEN__` global, no `accessToken` props.

**Reference implementation**: The `setLegalWorkspaceRuntimeConfig(config)` call in `main.tsx` (§2.1) wires the embedded LegalWorkspace's auth client to the same MSAL provider SpaarkeAi uses. Internal LegalWorkspace fetches (e.g. `useWorkspaceLayouts.ts` — calls `authenticatedFetch` from `@spaarke/auth` directly) inherit auth via the shared singleton state.

---

## 6. Mount semantics

The host decides when `<LegalWorkspaceApp embedded ... />` mounts, unmounts, and remounts. These decisions must respect a small number of invariants.

### 6.1 MUST: The host MUST mount `LegalWorkspaceApp` ONLY after config init (§2) is complete

**Rationale**: Mounting before `setLegalWorkspaceRuntimeConfig(...)` runs means LegalWorkspace's first internal `getBffBaseUrl()` call throws synchronously, blowing up the tab before `useWorkspaceLayouts` can fire. Even with `try/catch` around the throw, the section factories receive an unresolved layout and render error states.

**Verification**: Sequence test — set a `console.log` immediately before mount and another inside `setLegalWorkspaceRuntimeConfig`. The init log MUST precede the mount log on every cold load and every browser-refresh load.

**Reference implementation**: `src/solutions/SpaarkeAi/src/main.tsx` — `bootstrap()` is async; `await ensureAuthInitialized()` (line 257) gates `root.render()` (line 326). The mount of `WorkspaceLayoutWidget` (and therefore `<LegalWorkspaceApp />`) happens lazily inside React rendering, well after bootstrap completes.

### 6.2 MUST: The host MUST treat the `data.layoutId` prop as the identity of an embedded instance

**Rationale**: `LegalWorkspaceApp(embedded)` resolves the active layout from `initialWorkspaceId` (which the host passes as `data.layoutId`). Two simultaneous mounts of LegalWorkspaceApp with DIFFERENT `layoutId` values are legal — they render different layouts inside different tabs. Two mounts with the SAME `layoutId` are redundant — the host should reuse the existing tab rather than mount a new one.

**Verification**: From the host UI, attempt to mount the same workspace layout twice via the same dispatch path. The host MUST either (a) bring the existing tab forward (preferred), or (b) refuse with a clear indication that the layout is already open. Mounting two identical instances side-by-side is a host bug.

**Reference implementation**: `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx` lines 545–588 — guards against duplicate `widget_load` dispatches by checking `event.tabId` (already-acked) before dispatching. The WorkspaceTabManager's FIFO + de-dupe logic at the `workspace` channel layer enforces single-instance-per-layoutId.

### 6.3 MUST: The host MUST unmount `LegalWorkspaceApp` when its tab closes

**Rationale**: Embedded LegalWorkspaceApp owns inner React subscriptions (`FeedTodoSyncContext`, `useDailyDigestAutoPopup`, theme-event listeners gated by `embedded`). Leaving an unmounted-but-not-unmounted LegalWorkspaceApp in the React tree leaks subscriptions and may cause memory growth.

**Verification**: Open the embedded LegalWorkspaceApp tab, then close it via the host's tab-close affordance. In DevTools React Profiler, the LegalWorkspaceApp subtree MUST disappear from the rendered tree. Re-open the same layout — a fresh LegalWorkspaceApp mount fires (not a hidden-but-present one).

**Reference implementation**: SpaarkeAi's `WorkspaceTabManager` unmounts a tab's component when the tab is closed (the React tree structure is `WorkspacePane > TabContent[activeTabId] > WorkspaceLayoutWidget > LegalWorkspaceApp`; closing the tab removes the `TabContent` and its descendants).

### 6.4 MUST: The host MUST NOT recreate `webApi` / `userId` on every render

**Rationale**: `LegalWorkspaceApp`'s `useEffect` dependencies include `webApi` and `userId` (line 108). If the host passes new identities on every render (e.g. recomputing inside JSX without `useMemo`), the effects re-fire infinitely. The same applies to `initialWorkspaceId`.

**Verification**: Wrap `LegalWorkspaceApp` in `React.Profiler` (or use DevTools React Profiler). The number of renders per mounted tab MUST be small (single-digit) per user interaction. If `LegalWorkspaceApp` re-renders constantly without user interaction, the host is passing fresh prop identities.

**Reference implementation**: `WorkspaceLayoutWidget.tsx` lines 156–157 — `webApi` and `userId` both wrapped in `React.useMemo(() => ..., [])` (empty deps; they're stable for the lifetime of the widget). `data.layoutId` comes from the `WorkspaceWidgetComponent` framework with stable identity per tab.

### 6.5 MUST: The host MUST NOT render UI chrome that duplicates LegalWorkspaceApp's standalone chrome

**Rationale**: `LegalWorkspaceApp(embedded)` already suppresses its `PageHeader` (workspace dropdown), its footer (version stamp), and its outer `FluentProvider` (§3). If the host renders its own workspace dropdown ABOVE the embedded mount, the user sees two workspace switchers and can desync them (host switcher and the embedded `useWorkspaceLayouts` state). The host owns the workspace dropdown OR delegates it to LegalWorkspace; never both.

**Verification**: Visually inspect the embedded LegalWorkspaceApp tab. There MUST be at most one "workspace switcher" affordance in the surrounding chrome. The footer (version stamp) MUST appear at most once.

**Reference implementation**: SpaarkeAi's `WorkspacePaneMenu` (the "Switch Workspace" dropdown) lives in the Workspace pane header, OUTSIDE the embedded mount. `LegalWorkspaceApp.tsx` lines 146–156 conditionally render `<PageHeader>` only when `!embedded`. The combination keeps exactly one switcher visible.

---

## 7. Lifecycle hooks + side-effect contracts

Beyond mount/unmount, the host must respect the side-effect boundaries `LegalWorkspaceApp` has internally chosen to defer.

### 7.1 MUST: The host MUST own theme-change side effects (Dataverse persist) — §3.3 already enforced

This is restated here for completeness because it's a lifecycle concern as well as a theme concern. See §3.3.

### 7.2 MUST: The host SHOULD invalidate caches at appropriate host-driven events

The §4 cache keys are sessionStorage-bound, so they die naturally on session end. But hosts that perform mutations via their own UI (renames, deletes, reorder) SHOULD call `invalidateLayoutCache()` immediately after the mutation succeeds — even before the embedded mount remounts — so any subsequent embedded reads see fresh data.

**Reference implementation**: SpaarkeAi calls `invalidateLayoutCache()` in its `WorkspaceLayoutWizard` save flow (per existing R3 implementation).

### 7.3 MUST: The host MUST NOT add a second auto-popup on top of LegalWorkspace's hooks

**Rationale**: LegalWorkspace's `useDailyDigestAutoPopup` is one example; future LegalWorkspace components may add similar once-per-session hooks. The host must not duplicate them (e.g. running its own "show Daily Briefing on first load" logic AND letting the LegalWorkspace hook fire). Suppression (§4.2) is the canonical replacement pattern.

**Verification**: Cold-load with a clean sessionStorage. Confirm exactly one Daily Digest modal appears (host-driven OR LW-driven, NOT both). If neither host nor LW shows the modal, that's also a valid configuration — the host has chosen to suppress it.

**Reference implementation**: SpaarkeAi suppresses LW's auto-popup (per §4.2) and does NOT render its own modal-Daily-Digest substitute. The Daily Briefing inline section in the Home tab (rendered via `createDailyBriefingRegistration` + `WorkspaceShell`) is unaffected — different code path.

---

## 8. Contract verification checklist (testable summary)

A host author can use this as a smoke test of compliance. Each row links back to its MUST section.

| # | Test | Section |
|---|---|---|
| 1 | After bootstrap, both `getRuntimeConfig()` (host) and `getBffBaseUrl()` (LegalWorkspace) return identical `bffBaseUrl` | §2.2 |
| 2 | DevTools shows `window.__SPAARKE_BFF_BASE_URL__` set; reload uses localStorage cache | §2.3 |
| 3 | Open any embedded BFF action — no `[LegalWorkspace] Runtime config not initialized` in console | §2.1 |
| 4 | Walk React tree above embedded LegalWorkspaceApp — exactly one `FluentProvider` ancestor | §3.1 |
| 5 | Change theme — Dataverse user-pref record updates exactly ONCE; persists across sessions | §3.3 |
| 6 | Cold-load with empty sessionStorage — Daily Digest modal does NOT auto-popup (if host suppresses) | §4.2 |
| 7 | Grep host for `sprk:workspace:` — zero raw writes outside `invalidateLayoutCache()` | §4.4 |
| 8 | Network tab during embedded operation — Dataverse Web API calls succeed; no `webApi` null-deref crashes | §5.1 |
| 9 | `userId` prop on `LegalWorkspaceApp` is a brace-stripped Dataverse user GUID | §5.2 |
| 10 | Every BFF call inside the embedded tree carries `Authorization: Bearer <jwt>`; no token snapshot props | §5.4 |
| 11 | Mount the same `layoutId` twice — host reuses existing tab (or refuses) | §6.2 |
| 12 | Close the embedded tab — React Profiler confirms full unmount; reopen is a fresh mount | §6.3 |
| 13 | `LegalWorkspaceApp` re-render count per user interaction is small (single-digit) | §6.4 |
| 14 | Embedded chrome has at most one workspace switcher and at most one footer | §6.5 |
| 15 | Exactly one Daily Digest auto-popup (host-driven OR LW-driven, never both) | §7.3 |

If any row above fails, the host is non-compliant with this contract. Re-read the linked section and the SpaarkeAi reference impl.

---

## 9. Cross-references

### 9.1 Architecture docs

- [`SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](./SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) — the parent architectural framing. §2.1 (Dashboard wrapper) and §5 (LegalWorkspace as the dashboard engine) are the conceptual home of this contract.
- [`SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](./SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — the cold-load → widget render pipeline this contract sits inside.
- [`SPAARKEAI-COMPONENT-MODEL.md`](./SPAARKEAI-COMPONENT-MODEL.md) — inventory of the `@spaarke/*` libraries this contract leans on (`@spaarke/auth`, `@spaarke/legal-workspace`, `@spaarke/ai-widgets`, `@spaarke/ui-components`).
- `LEGALWORKSPACE-RETIREMENT.md` (forward — published by R4 W-6) — formal record of the standalone Code Page retirement that makes this contract forward-looking rather than backward-compatible.

### 9.2 ADRs

- [`ADR-012`](../adr/ADR-012-shared-component-libraries.md) — `@spaarke/*` lib placement. Underpins the "config init crosses singletons" pattern in §2.
- [`ADR-021`](../adr/ADR-021-fluent-design-system.md) — Fluent v9 tokens. Theme ownership in §3 inherits.
- [`ADR-022`](../adr/ADR-022-react-version-for-code-pages.md) — React 19 for Code Pages. Both host and embedded tree are React 19.
- [`ADR-028`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — Function-based auth. §5.4 BFF auth requirements + §2.3 auth-config resolution flow inherit ADR-028 directly. **Load this ADR before reading §2 or §5.4.**

### 9.3 Reference implementation (SpaarkeAi)

- `src/solutions/SpaarkeAi/src/main.tsx` — Config dual-init (§2.1, §2.2, §2.3), Daily Digest suppression (§4.2), bootstrap sequencing (§6.1).
- `src/solutions/SpaarkeAi/src/App.tsx` — Single `FluentProvider` ownership (§3.1).
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/WorkspaceLayoutWidget.tsx` — Xrm frame-walk + `webApi`/`userId` resolution (§5.1, §5.2), dev fallback (§5.3), memoized props (§6.4), mount call (§3.2).
- `src/solutions/LegalWorkspace/src/LegalWorkspaceApp.tsx` — Source of truth for the embedded-vs-standalone branching (lines 86–120, 145–185).
- `src/solutions/LegalWorkspace/src/config/runtimeConfig.ts` — LegalWorkspace's runtime-config singleton (the second one initialized per §2.1).
- `src/solutions/LegalWorkspace/src/index.ts` — Barrel export with `setLegalWorkspaceRuntimeConfig` (§2.1).
- `src/solutions/LegalWorkspace/src/workspace/layoutCache.ts` — Cache key constants + `invalidateLayoutCache()` (§4.3, §7.2).
- `src/solutions/LegalWorkspace/src/hooks/useDailyDigestAutoPopup.ts` — Sessionstorage suppression target (§4.2).
- `src/solutions/LegalWorkspace/src/types/xrm.ts` — `IWebApi` interface (§5.1).

### 9.4 Project artifacts (R4)

- [`../../projects/spaarke-ai-platform-unification-r4/spec.md`](../../projects/spaarke-ai-platform-unification-r4/spec.md) — DR-07 (this doc), OC-R4-05 (LW retirement context).
- [`../../projects/spaarke-ai-platform-unification-r4/plan.original.md`](../../projects/spaarke-ai-platform-unification-r4/plan.original.md) — Phase 1 C-2 task definition.

---

## 10. Glossary

| Term | Definition |
|---|---|
| **Embedded mode** | The state of `LegalWorkspaceApp` when its `embedded` prop is truthy. Skips `PageHeader`, footer, outer `FluentProvider`, cross-device theme sync side effects. §1, §3, §7. |
| **Host** | Any code that mounts `<LegalWorkspaceApp embedded ... />`. SpaarkeAi is the only host today. §0. |
| **Dashboard engine** | What `LegalWorkspaceApp(embedded)` is — the library code that parses `sectionsJson` and renders sections. See [`SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](./SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) §5. |
| **Dual singleton** | The pattern of initializing BOTH host-side runtime config AND LegalWorkspace-side runtime config with identical values. §2. |
| **`IWebApi`** | The minimal Dataverse Web API contract `LegalWorkspaceApp` consumes via its `webApi` prop. Defined in `src/solutions/LegalWorkspace/src/types/xrm.ts`. §5. |
| **OC-R4-05** | 2026-05-25 scoping decision: standalone LegalWorkspace Code Page retired; components retained as library. The premise of this contract. §0. |
| **SessionStorage sentinel** | A boolean (truthy/falsy) key in `sessionStorage` used by LegalWorkspace components for once-per-session behavior. Hosts may need to SET sentinels before mount (to suppress) or invalidate caches after mutations. §4. |

---

## 11. Document changelog

- **2026-05-26 (R4 task 015 / C-2)**: Initial publication. Established the six host-requirement categories (config init, theme ownership, sessionStorage sentinels, `webApi` shim, mount semantics, lifecycle hooks) as testable MUSTs with SpaarkeAi reference-impl pointers. Authored against the R4 LW-retirement context (OC-R4-05) so the contract is forward-looking for any future host. Cross-linked from W-1's [`SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](./SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) §7.1.
