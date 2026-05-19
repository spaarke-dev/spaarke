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

  return (
    <div className={styles.appRoot}>
      <div className={styles.layoutShell}>
        <ThreePaneShell
          bffBaseUrl={bffBaseUrl}
          isAuthenticated={isAuthenticated}
          entityLogicalName={props.entityLogicalName}
          entityId={props.entityId}
          matterId={props.matterId}
          sessionId={props.sessionId}
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
