# Task 042 — Standalone Email code page (`src/solutions/EmailPage/**`)

> For task 051 (deploy). Documents the code-page build output, the navigation
> registration plan, and the `EmailWorkspaceProps` this mount supplies —
> mirrors the equivalent section in `task-041-*` notes (widget mount) for
> dual-mount-parity traceability.

## What was built

`src/solutions/EmailPage/` — a new Vite React 19 solution, structurally
identical to `src/solutions/DailyBriefing/` (package.json, vite.config.ts,
index.html, tsconfig.json/tsconfig.node.json, `src/vite-env.d.ts`,
`src/config/runtimeConfig.ts`, `src/services/authInit.ts`, `src/main.tsx`).

- `src/main.tsx` — `createRoot` + host `FluentProvider` (theme via
  `resolveCodePageTheme`/`setupCodePageThemeListener`) + `AppErrorBoundary
  surfaceName="Email"` + async non-blocking `bootstrapAuth()`
  (`resolveRuntimeConfig` → `setRuntimeConfig` → `ensureAuthInitialized`).
- Renders the shared `EmailWorkspace` from `@spaarke/communication-components`
  (task 040) UNCHANGED — no fork, no per-mount branch inside the shared
  component (NFR-06).
- `EmailWorkspace` itself is gated on `bootstrapAuth()` completing: while
  config/auth resolve, the pane shows a `Spinner` loading state; if bootstrap
  rejects (no resolvable auth context), the pane shows a fail-closed
  error/retry state instead of mounting `EmailWorkspace` (NFR-07 — no email
  content is ever rendered without a resolved auth context). `Xrm.WebApi`-backed
  reads (view list, per-selection record) work within the existing
  authenticated MDA session regardless of BFF auth state; only the `.eml`
  render + composer-send calls route through `authenticatedFetch`
  (ADR-028) and fail with a 401 → error state if unauthenticated.

## `EmailWorkspaceProps` supplied by this mount

| Prop | Concrete implementation |
|---|---|
| `dataverseClient` | `new XrmDataverseClient()` (`@spaarke/ui-components`) |
| `dataService` | `createXrmDataService()` (`@spaarke/ui-components`) |
| `navigationService` | `createXrmNavigationService()` (`@spaarke/ui-components`) |
| `webApi` | Thin `EmailWorkspaceWebApi` bridge built here, lazily resolving `getXrm()!.WebApi` per call (`retrieveMultipleRecords`, `retrieveRecord`, `updateRecord`) — mirrors the lazy-resolve pattern used by `XrmDataverseClient`/`createXrmDataService` |
| `authenticatedFetch` | `authenticatedFetch` from `./services/authInit.ts` (thin consumer of `@spaarke/auth`'s `createCodePageAuthInitializer`) |
| `bffBaseUrl` | `getBffBaseUrl()` from `./config/runtimeConfig.ts`, only read AFTER `bootstrapAuth()` resolves (see gating note above) |
| `accessPermissionOptions` | omitted — `EmailWorkspace` defaults to `DEFAULT_ACCESS_PERMISSION_OPTIONS` |
| `onSearchRecipients` / `linkAnotherCatalog` / `initialSelectedId` | omitted (optional; not needed for the standalone-mount MVP) |

Dual-mount parity (NFR-06): task 041 (SpaarkeAi `email` workspace widget)
supplies the SAME prop contract via its own host-specific adapters (BFF- or
widget-context-backed rather than direct `Xrm.WebApi`, per that task's own
notes) — both mounts render `EmailWorkspace` unchanged.

## Build output

- Build command: `npm run build` (Vite code page — NOT a PCF `build:prod`).
- Output: `src/solutions/EmailPage/dist/sprk_emailpage.html` (single-file
  inlined bundle via `vite-plugin-singlefile`, ~2.19 MB / ~614 KB gzip).
- `npm install --legacy-peer-deps --no-audit --no-fund` required first (stale
  lockfile convention for Vite solutions per root CLAUDE.md §12).
- `tsc-surface-gate`: 0 new errors introduced by this surface (66
  pre-existing errors in shared libs, deferred to Phase B — unrelated to this
  task).

## Navigation registration plan (for task 051)

No `SiteMap.xml` is checked into this repo — Spaarke code pages are
registered as reachable-from-navigation via the **Dataverse maker portal**
at deploy time (same pattern documented in
`src/solutions/EventsPage/DEPLOYMENT-GUIDE.md` "Step 5: Update Sitemap
Navigation" and used for `sprk_dailyupdate`/DailyBriefing). Task 051 should:

1. Create/verify a `sprk_emailpage` web resource (type Webpage/HTML) in the
   target Dataverse solution — mirrors `Deploy-DailyBriefing.ps1`'s pattern
   (`webresourceset` find-by-name → PATCH `content` with the built HTML's
   base64 → `PublishXml`). A new deploy script (or an extension of an
   existing generic web-resource push script) should target
   `src/solutions/EmailPage/dist/sprk_emailpage.html`.
2. In App Designer (or the SiteMap editor) for the target Model-Driven App,
   add an "Email" navigation entry pointing at the `sprk_emailpage` web
   resource (Page type: "Web Resource" / Custom Page per the app's existing
   convention — follow the same steps EventsPage's guide documents for
   `sprk_eventspage`).
3. Verify: click "Email" in the left navigation → the standalone code page
   loads, authenticates, and renders `EmailWorkspace` identically to the
   `email` workspace widget mount (task 041) — the dual-mount-parity
   acceptance criterion.
4. Dark-mode + fail-closed (unauthenticated) UI-test cases documented in the
   task 042 POML `<ui-tests>` section should be exercised against the
   deployed web resource as part of task 050's verification sweep.

## Escalation check (none fired)

Both escalation conditions in the task POML were evaluated and did NOT fire:
- The standalone code-page host CAN supply the exact `EmailWorkspaceWebApi` /
  `IDataverseClient` / `IDataService` / `INavigationService` contract
  `EmailWorkspace` expects, using only existing `@spaarke/ui-components`
  Xrm adapters + a thin lazy `Xrm.WebApi` bridge — no fork was needed.
- Fail-closed authorization IS guaranteed through `@spaarke/auth` alone (the
  `createCodePageAuthInitializer` factory's `authenticatedFetch` — no new
  auth surface was added).
