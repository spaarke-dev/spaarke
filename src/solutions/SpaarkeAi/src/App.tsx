/**
 * App.tsx — SpaarkeAi root application component (R2).
 *
 * Provider tree (per ADR-021, ADR-022):
 *   FluentProvider (theme detection — resolveCodePageTheme + setupCodePageThemeListener;
 *     scaled via scaleTheme(theme, uiScale) — P0.5/FR-06, see useUiScale below)
 *     └─ AppWithAuth (gates render on auth-ready, no token snapshot)
 *          └─ ThreePaneShell (R2 root shell — PaneEventBus + stage lifecycle + ThreePaneLayout)
 *              (DisplaySizeMenu removed from the embedded surface 2026-08-03 — see render comment)
 *
 * App-shell UI-scale (P0.5, spec FR-06 / design §6.9, spaarke-modal-system project):
 *   `useUiScale()` resolves ONE `uiScale` value from (a) the auto ≥2560 CSS px
 *   viewport breakpoint and (b) the persisted "Display size" setting. That
 *   value feeds `scaleTheme(theme, uiScale)` at the FluentProvider below
 *   (Fluent internals — buttons/inputs/spacing/text — scale in lock-step) AND
 *   sets the `--sprk-ui-scale` CSS variable on `document.documentElement` (the
 *   SprkModal size math + any future custom CSS reads this). Mechanism is the
 *   scaled theme, NEVER CSS `zoom` (rejected — under-scales portaled fixed
 *   dialogs at 4K). SprkModal itself only INHERITS `uiScale` as a prop; no
 *   SprkModal instance is rendered by this shell yet (conversions are P2+) —
 *   `uiScale` from this hook is the seam those tasks thread through.
 *
 * Auth pattern (Spaarke Auth v2, post-task-021):
 *   - @spaarke/auth is initialized in main.tsx via ensureAuthInitialized().
 *   - App.tsx does NOT snapshot the access token. It only verifies the provider
 *     can mint a token at mount (sets isAuthenticated=true on success) for UI
 *     gating; downstream BFF calls go through authenticatedFetch / useAuth(),
 *     which always asks the provider for a fresh token.
 *   - This eliminates the H-5 snapshot bug (App.tsx:81-105 in pre-v2 code) where
 *     useEffect captured a token once at mount and never refreshed → 401 after
 *     ~80min idle.
 *
 * R2 change: AppShell + the R1 standalone provider replaced by ThreePaneShell.
 * AiSessionProvider (AIPU2-076) lives inside ThreePaneShell.
 *
 * @see ADR-021 - Fluent v9, dark mode required, semantic tokens only
 * @see ADR-022 - React 19 createRoot for Code Pages
 * @see ADR-026 - Single-file Vite build for Dataverse web resource
 * @see ThreePaneShell — R2 shell with PaneEventBus + stage lifecycle
 * @see .claude/AUDIT-FINDINGS-AUTH-SYSTEM.md §H-5 — root-cause snapshot bug fixed by this file
 */

import * as React from "react";
import { FluentProvider, makeStyles, tokens } from "@fluentui/react-components";
import {
  resolveCodePageTheme,
  setupCodePageThemeListener,
  scaleTheme,
  useUiScale,
} from "@spaarke/ui-components";
import { getBffBaseUrl } from "./config/runtimeConfig";
import { useAuthProbe } from "./hooks/useAuthProbe";
import { ThreePaneShell } from "./components/shell/ThreePaneShell";
import { ensureNavigatorPane } from "./ensureNavigatorPane";
// spaarkeai-compose-r1 task 092 (Phase 7 three-pane pivot, supersedes task 046's
// Path A shortcut per spec-supplement-2026-07-01-three-pane-pivot.md FR-S1):
// When the modal is launched with `?composeMode=editor&…`, we NO LONGER render
// <ComposeWorkspace> directly here. Instead, App.tsx always renders
// ThreePaneShell (the canonical mount) and forwards the compose launch params
// so the shell can (a) auto-select the "Compose" workspace layout in
// WorkspacePane and (b) expose the document ref to the compose-editor section
// factory via ComposeLaunchContext (consumed in task 093).
import type { ComposeDocumentRef } from "@spaarke/compose-components";

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  appRoot: {
    display: "flex",
    flexDirection: "column",
    width: "100vw",
    height: "100vh",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  // P0.5 (FR-06): the app-shell "Display size" affordance sits in a slim,
  // right-aligned strip above the three-pane shell. SpaarkeAi has no
  // pre-existing appearance/settings surface (ThemeToggle lives only in
  // LegalWorkspace's standalone PageHeader) — this is the new, narrow,
  // single-purpose home for it. Does not touch ThreePaneShell/pane headers.
  scaleBar: {
    display: "flex",
    justifyContent: "flex-end",
    alignItems: "center",
    flexShrink: 0,
    paddingInline: tokens.spacingHorizontalS,
    paddingBlock: tokens.spacingVerticalXXS,
  },
  layoutShell: {
    flex: 1,
    minHeight: 0,
    overflow: "hidden",
  },
});

// ---------------------------------------------------------------------------
// AppProps — URL search params parsed by main.tsx
// ---------------------------------------------------------------------------

export interface AppProps {
  /** Entity logical name (e.g. "sprk_matter", "sprk_project") from URL ?entityType= */
  entityLogicalName?: string;
  /** Entity record GUID from URL ?entityId= */
  entityId?: string;
  /** Matter ID shorthand from URL ?matterId= */
  matterId?: string;
  /** Session ID for restore flow (AIPU2-106). When present, triggers session restore before first render. */
  sessionId?: string;

  // ---------------------------------------------------------------------------
  // Compose launch params (spaarkeai-compose-r1 task 046 — Path A entry).
  //
  // When `composeMode === 'editor'`, App mounts `ComposeWorkspace` directly
  // (bypassing ThreePaneShell). The document pointer (sprkDocumentId +
  // speDriveItemId) is forwarded to ComposeWorkspace so the editor can load
  // the DOCX on mount. When `composeMode` is undefined the standard three-pane
  // shell renders unchanged.
  //
  // The "modal with full-screen toggle" UX is provided by the Xrm dialog chrome
  // itself (opened with target=2 at 90%×90%; the platform-provided Expand
  // button is the full-screen toggle) — no new modal abstraction.
  // ---------------------------------------------------------------------------

  /** Routes the app to the Compose editor surface (Path A). */
  composeMode?: "editor";
  /** Dataverse `sprk_document` record GUID (Compose-only). */
  sprkDocumentId?: string;
  /** SPE drive-item id of the DOCX to load (Compose-only). */
  speDriveItemId?: string;
  /** SPE container drive id (Compose-only — may be omitted; resolved at runtime). */
  speDriveId?: string;
  /** Display name of the document for the workspace title (Compose-only). */
  speFileName?: string;
  /**
   * ai-advanced-capabilities-analysis-hub-r1 task 041 (FR-13): the ACTIVE work type the launch
   * is scoped to (e.g. `"agreement-analysis"` for an Agreement Review; Compose-only). Forwarded
   * to `ThreePaneShell` → `ComposeLaunchContext` → `ComposeWorkspace` → `ComposeEditor`, scoping
   * the inline AI toolbar via `getToolsForSurface`. Omitted preserves the unscoped `'*'` default.
   */
  activeWorkType?: string;

  // ---------------------------------------------------------------------------
  // Analysis entry-matrix params (task 050 — spec §12 / FR-14).
  //
  // Parsed by main.tsx from the URL and forwarded to ThreePaneShell, which
  // publishes them as an AnalysisLaunchContext consumed by WorkspacePane:
  //   - analysisMode='new'      → open the Analysis hub (Create-new cards). With a
  //                               record context present (entityLogicalName/entityId)
  //                               the hub pre-sets regarding=parent (2b); else 2a.
  //   - analysisMode='existing' → open the existing analysis by id (2d/2c), no cards.
  // Omitted for every non-analysis launch (Compose, session-restore, plain).
  // ---------------------------------------------------------------------------

  /** Analysis entry mode: 'new' opens the hub, 'existing' opens an analysis by id. */
  analysisMode?: "new" | "existing";
  /** `sprk_analysis` GUID to open (analysisMode='existing'). */
  analysisId?: string;
  /** `sprk_worktype` Choice value for a new analysis (analysisMode='new'). */
  worktype?: string;
  /**
   * ai-advanced-capabilities-agreements-r1 task 022 (spec FR-09; hub A3 deferred deep-threading
   * leg — cold-load/deep-link door): the level-2 agreement sub-domain (`sprk_agreementtype.sprk_key`,
   * e.g. "nda") for a cold-load open of the Analysis entry matrix. Forwarded to `ThreePaneShell` →
   * `AnalysisLaunchContext`. Omitted for every non-agreement launch.
   */
  subDomain?: string;
}

// ---------------------------------------------------------------------------
// AppWithAuth — acquires BFF token, mounts ThreePaneShell
// ---------------------------------------------------------------------------

function AppWithAuth(props: AppProps): React.JSX.Element {
  const styles = useStyles();

  // UI-gating flag only — NOT the token. Downstream BFF calls acquire fresh
  // tokens per-request via authenticatedFetch / useAuth() (Spaarke Auth v2).
  // `useAuthProbe()` probes the provider at mount (retrying with backoff — see
  // its docblock for the FIX 3 root-cause writeup) so the shell can render
  // auth-aware UI; it does NOT store the token string in React state, which
  // was the root cause of the 401-after-idle bug (audit §H-5).
  const isAuthenticated = useAuthProbe();

  const bffBaseUrl = (() => {
    try {
      return getBffBaseUrl();
    } catch {
      return "";
    }
  })();

  // -------------------------------------------------------------------------
  // Canonical mount (spaarkeai-compose-r1 task 092, supersedes task 046 Path A).
  //
  // App.tsx ALWAYS renders ThreePaneShell now, including the ribbon
  // "Open in Compose" modal launch. When `composeMode === 'editor'`, we
  // forward the document pointer + drive id so ThreePaneShell can (a)
  // auto-select the "Compose" workspace layout in WorkspacePane (task 092)
  // and (b) expose the document ref via `ComposeLaunchContext` for the
  // compose-editor section factory to consume (task 093).
  //
  // The Xrm dialog chrome (target=2, 80%×80%, platform-provided expand-to-
  // full-screen button) provides the modal UX from the locked design decision.
  //
  // Auth is still gated via the AppWithAuth probe — if `isAuthenticated` is
  // false ComposeWorkspace's BFF load will simply surface the standard
  // unauthorized error via its existing MessageBar (no special handling here).
  // -------------------------------------------------------------------------
  const initialComposeDocument: ComposeDocumentRef | null =
    props.composeMode === "editor" && props.speDriveItemId
      ? {
          speDriveItemId: props.speDriveItemId,
          sprkDocumentId: props.sprkDocumentId,
          fileName: props.speFileName,
          // FR-05 (task 100, gap 1.3): containerId is intentionally NOT set here. This ref is the
          // LOAD path (an existing stored document), where the BFF Load contract keys off driveId
          // — containerId plays no role. The create-on-save container (for a NEW transient
          // Browse/Upload draft) is resolved CLIENT-SIDE at the compose mount host
          // (composeEditor.registration.ts → EntityCreationService.resolveUserBuDefaults) and
          // threaded as the `containerId` prop, so it covers every entry path, not just this one.
        }
      : null;

  return (
    <div
      className={styles.appRoot}
      data-spaarkeai-mode={props.composeMode === "editor" ? "compose" : undefined}
    >
      {/* P0.5 (FR-06) "Display size" control — REMOVED from the embedded surface
          (owner UAT 2026-08-03): the manual toggle only scales Spaarke code-page
          content, not the surrounding OOB MDA chrome/views, which reads as broken
          when SpaarkeAi runs inside the MDA. The auto ≥2560px breakpoint in
          useUiScale() still applies (silent, load-bearing for 4K modal sizing).
          Restore `<div className={styles.scaleBar}><DisplaySizeMenu /></div>`
          (+ the DisplaySizeMenu import) when the three-pane page ships as a
          standalone app, where whole-surface scaling is coherent. */}
      <div className={styles.layoutShell}>
        <ThreePaneShell
          bffBaseUrl={bffBaseUrl}
          isAuthenticated={isAuthenticated}
          entityLogicalName={props.entityLogicalName}
          entityId={props.entityId}
          matterId={props.matterId}
          sessionId={props.sessionId}
          composeMode={props.composeMode}
          composeDocument={initialComposeDocument}
          composeDriveId={props.speDriveId ?? ""}
          activeWorkType={props.activeWorkType}
          analysisMode={props.analysisMode}
          analysisId={props.analysisId}
          worktype={props.worktype}
          subDomain={props.subDomain}
        />
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// App — top-level component that owns theme state
// ---------------------------------------------------------------------------

/**
 * Root App component.
 *
 * Theme resolution follows the Code Page priority chain (theme-consistency.md):
 *   1. localStorage `spaarke-theme` (user's explicit preference)
 *   2. URL `flags` parameter (themeOption=dark|light)
 *   3. Navbar DOM background color detection
 *   4. Default: light
 *
 * MUST NOT use OS prefers-color-scheme (per theme-consistency.md constraints).
 */
export const App: React.FC<AppProps> = (props) => {
  // resolveCodePageTheme() is the initializer — runs once at mount
  const [theme, setTheme] = React.useState(resolveCodePageTheme);

  // Listen for theme changes (localStorage events, custom spaarke-theme-changed events)
  React.useEffect(() => {
    const cleanup = setupCodePageThemeListener(setTheme);
    return cleanup;
  }, []);

  // P0.5 (FR-06 / design §6.9): the app-shell `uiScale` — auto ≥2560 breakpoint
  // + persisted "Display size" setting (useUiScale reuses the SAME
  // theme-storage listener as above; see hooks/useUiScale.ts). scaleTheme
  // short-circuits to `theme` itself when uiScale===1 (the common case), so
  // this is a no-op until a user opts into Large/Extra-large or the viewport
  // crosses the 2560 breakpoint.
  const { uiScale } = useUiScale();
  const scaledTheme = React.useMemo(() => scaleTheme(theme, uiScale), [theme, uiScale]);

  // The SAME uiScale value also sets the `--sprk-ui-scale` CSS variable
  // (design §6.9) on the document root — a portal-safe location, since
  // Fluent Dialog surfaces (SprkModal) portal to document.body, still a
  // descendant of :root, regardless of where they mount in the React tree.
  React.useEffect(() => {
    document.documentElement.style.setProperty("--sprk-ui-scale", String(uiScale));
  }, [uiScale]);

  // Global Navigator side pane (spaarke-side-pane-navigation-history-r1 task 086):
  // SpaarkeAi is the universal home page, so it registers the app-level Navigator
  // pane on mount (host-launch pattern, like EventsPage → CalendarSidePane). The
  // pane persists across all navigation (Xrm.App.sidePanes are app-level).
  // Idempotent + never throws — see ensureNavigatorPane.ts.
  React.useEffect(() => {
    ensureNavigatorPane();
  }, []);

  return (
    <FluentProvider theme={scaledTheme}>
      <AppWithAuth {...props} />
    </FluentProvider>
  );
};
