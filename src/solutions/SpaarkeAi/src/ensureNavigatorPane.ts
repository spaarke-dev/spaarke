/**
 * ensureNavigatorPane — SpaarkeAi's thin wrapper around the SHARED Navigator
 * side-pane registrar (spaarke-side-pane-navigation-history-r1).
 *
 * Superseded 2026-08-14: the createPane/navigate/singleton-guard/retry logic
 * that used to live in this file has moved to the reusable standard module
 * `ensureNavigatorSidePane()` in `@spaarke/ui-components` — see
 * `src/client/shared/Spaarke.UI.Components/src/utils/ensureNavigatorSidePane.ts`
 * for the full implementation + docblock and
 * `docs/architecture/SPAARKE-SIDE-PANE-NAVIGATION.md` for the reference doc.
 * ANY Spaarke code page can now get this behavior with the same one-line
 * import; SpaarkeAi is simply the first (and reference) consumer.
 *
 * This file is kept ONLY as a re-export so `App.tsx`'s existing
 * `import { ensureNavigatorPane } from "./ensureNavigatorPane"` call site
 * needs no change — `sprk_spaarkeai` is co-owned with another concurrent
 * project, so this keeps the diff to that solution at a single-file,
 * additive-only change.
 *
 * WHY here originally (owner decision 2026-08-14, preserved for context):
 * modern UCI has no reliable global app-load JS hook. SpaarkeAi is the
 * universal home page, so registering the pane on its mount — the same
 * host-launch pattern EventsPage uses for CalendarSidePane — docks it when
 * the user lands on home. Because `Xrm.App.sidePanes` panes are APP-LEVEL
 * (not page-scoped) and the pane is created `canClose:false` +
 * `alwaysRender:true`, it persists (and its capture poll keeps running)
 * across all subsequent navigation.
 */

import { ensureNavigatorSidePane } from "@spaarke/ui-components";

/**
 * Register (create + navigate) the app-level Navigator pane once. Safe to
 * call on every SpaarkeAi mount — delegates entirely to the shared,
 * idempotent, never-throwing `ensureNavigatorSidePane()`.
 */
export function ensureNavigatorPane(): void {
  ensureNavigatorSidePane();
}
