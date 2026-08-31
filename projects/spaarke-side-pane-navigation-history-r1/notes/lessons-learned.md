# Lessons Learned — spaarke-side-pane-navigation-history-r1

> Written at project close (2026-08-15). Audience: whoever builds the next side-pane / code-page-on-MDA-shell feature.

## What worked

- **Path B (global always-available docked pane + continuous capture) was the right call.** The spike gate (task 001) confirmed `Xrm.App.sidePanes.createPane({canClose:false, alwaysRender:true})` persists across in-app navigation and that a 1.5s `getPageContext()` poll captures every visit with no misses — zero form handlers, zero plugins, exactly as designed. The recovered `contextService.ts` poll loop ported almost verbatim.
- **Reuse-first held.** Views tab reused `ViewService.ts`; `sprk_navitem` CRUD mirrored the Notepad memo repository; the capture loop re-adopted retired code. No net-new abstraction was invented where an existing one fit.
- **Read-time security trimming (task 080) generalized into a project-wide principle.** The same "let the platform decide access" idea later solved the Monitored lens (see below) — a sign the trimming abstraction was at the right altitude.

## What surprised us (the load-bearing gotchas)

1. **A single-contributor code page must NOT mount the multi-contributor `SprkSidePaneHost`.** The deployed pane rendered blank in the live app while rendering fine headlessly. Root cause: mounting the full host (lifecycle orchestrator + async lazy contributor resolution + rail) inside a webresource that only ever has ONE contributor. **Fix: mirror `CalendarSidePane` — a plain root `FluentProvider` wrapping the pane body directly.** Lesson: reach for `SprkSidePaneHost` only when a pane genuinely hosts multiple independent contributors. The framework is still proven (task 085 stub contributor), but the Navigator itself doesn't use it.

2. **Modern UCI has NO supported global page-load JS hook.** (Researcher-confirmed: form/grid/control events only; global ribbon enable-rule regressed; `AppModule` onload undocumented/unsupported.) Auto-load was solved with a **two-insertion model**: (a) an **entity command-bar `EnableRule`** whose `CustomRule → Spaarke.SidePaneManager.initialize` fires *silently* when that entity's grid/form ribbon loads (rolled to Matter/Document/Project/Event/Communication/Todo); (b) a shared `ensureNavigatorSidePane()` registrar for **code pages** (SpaarkeAi/Email/Reconciliation). OOB dashboards remain the one un-covered surface — documented, not fixed.

3. **`Xrm.Navigation.navigateTo` view selection needs `viewType` as a STRING.** `viewType:'userquery'` (or `'savedquery'`) opens the real view **in-app**; a numeric value or `openUrl(main.aspx)` either falls back to the default view or opens a new tab. UCI's sticky per-table view selector can still override exact selection.

4. **UCI applies a monochrome FILL treatment to side-pane header icons.** A `fill="none" stroke` outline star rendered as a solid black star. Fix: draw the star as a **filled even-odd "ring"** (`fill="currentColor"`, outer star minus inner star) so the platform's fill yields a hollow outline. Icon-design lesson: give UCI a filled silhouette and control the shape via the hole, don't rely on stroke.

5. **The capture engine existed but was never started.** `startNavigatorCapture()` was written in task 030 but no caller invoked it until UAT surfaced an empty Recent tab — wired into `NavigatorBody`'s mount effect. Lesson: a "service + no call site" is invisible to unit tests; a smoke check ("does Recent populate after navigating?") would have caught it at 040.

6. **"Owned by me" is the wrong scope in production — the platform is the membership resolver.** The Monitored lens shipped owner-only (`_ownerid_value eq {me}`) because "assigned to me" was an ambiguous per-entity business decision. But in production, records are owned by the **business unit/team**, not the user, so owner-equality returned nothing. **Fix: drop the owner clause entirely and query `sprk_monitor eq true`** — a host-context `retrieveMultipleRecords` is *already* security-trimmed by Dataverse to rows the user can read (owner ∪ BU ∪ team ∪ shared). Membership resolution is the platform's job; there is no BFF service to call and none to build. (`sprk_navitem` pins/history keep their per-user owner filter — that is genuine per-user isolation, a different concern.)

## Reuse wins / gaps

- **Win:** `ensureNavigatorSidePane()` became the single shared code-page registrar (barrel-exported from `@spaarke/ui-components`); SpaarkeAi/Email/Reconciliation each consume it with one import + one mount call. `recordNavigation.openEntityRecord` and `nameResolution` are single-source helpers consumed by all three tabs + QuickSwitcher.
- **Gap (accepted):** the plain-JS `sprk_SidePaneManager.js` bootstrap duplicates the registrar logic because it deploys as a raw Dataverse web resource (not an ES module) — an intentional, documented small duplication.

## Deferred / follow-on

- **052 Path B narrower scoping** — if a future requirement wants strictly-my-BU (excluding team/share), that's an additional explicit filter on top of the access-based query, NOT a return to owner-only.
- **OOB dashboards auto-load** — no supported hook; would need a per-dashboard web resource (rejected as scope creep).

## Cross-project note

The net10 cutover (`dotnet-10-upgrade-r1`) landed on master mid-project. This project is client-only (BFF=N), so it was unaffected — the Vite/Jest build is .NET-runtime-independent. Confirmed by a clean net10 BFF build after the FF-merge (no Navigator surface touched by the cutover).
