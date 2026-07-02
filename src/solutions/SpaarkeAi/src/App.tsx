/**
 * App.tsx — SpaarkeAi root application component (R2).
 *
 * Provider tree (per ADR-021, ADR-022):
 *   FluentProvider (theme detection — resolveCodePageTheme + setupCodePageThemeListener)
 *     └─ AppWithAuth (gates render on auth-ready, no token snapshot)
 *          └─ ThreePaneShell (R2 root shell — PaneEventBus + stage lifecycle + ThreePaneLayout)
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
 * R2 change: AppShell + StandaloneAiProvider replaced by ThreePaneShell.
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
} from "@spaarke/ui-components";
import { getAuthProvider } from "@spaarke/auth";
import { getBffBaseUrl } from "./config/runtimeConfig";
import { ThreePaneShell } from "./components/shell/ThreePaneShell";
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
}

// ---------------------------------------------------------------------------
// AppWithAuth — acquires BFF token, mounts ThreePaneShell
// ---------------------------------------------------------------------------

function AppWithAuth(props: AppProps): React.JSX.Element {
  const styles = useStyles();

  // UI-gating flag only — NOT the token. Downstream BFF calls acquire fresh
  // tokens per-request via authenticatedFetch / useAuth() (Spaarke Auth v2).
  // This effect probes the provider once at mount so the shell can render
  // auth-aware UI; it does NOT store the token string in React state, which
  // was the root cause of the 401-after-idle bug (audit §H-5).
  const [isAuthenticated, setIsAuthenticated] = React.useState<boolean>(false);

  React.useEffect(() => {
    let cancelled = false;

    const probeAuth = async (): Promise<void> => {
      try {
        const provider = getAuthProvider();
        const accessToken = await provider.getAccessToken();
        if (!cancelled && accessToken) {
          setIsAuthenticated(true);
        }
      } catch (err) {
        if (!cancelled) {
          console.warn("[SpaarkeAi] Auth probe failed:", err);
          setIsAuthenticated(false);
        }
      }
    };

    void probeAuth();

    return () => {
      cancelled = true;
    };
  }, []);

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
          // containerId is unused in the BFF Load contract (driveId is what's
          // queried); leave undefined and let the workspace resolve via runtime.
          containerId: undefined,
        }
      : null;

  return (
    <div
      className={styles.appRoot}
      data-spaarkeai-mode={props.composeMode === "editor" ? "compose" : undefined}
    >
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

  return (
    <FluentProvider theme={theme}>
      <AppWithAuth {...props} />
    </FluentProvider>
  );
};
