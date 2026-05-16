/**
 * App.tsx — SpaarkeAi root application component.
 *
 * Provider tree (per ADR-021, ADR-022, task 040):
 *   FluentProvider (theme detection — resolveCodePageTheme + setupCodePageThemeListener)
 *     └─ AppWithAuth (acquires BFF token via @spaarke/auth)
 *          └─ StandaloneAiProvider (from @spaarke/ai-context — entity resolution + chat state)
 *               └─ AppShell (ThreePaneLayout with all three pane components)
 *
 * Auth pattern: no React AuthProvider component — @spaarke/auth is a class-based
 * provider initialized in main.tsx (ensureAuthInitialized). App acquires the token
 * via getAuthProvider().getAccessToken() in a useEffect and passes it down as a prop
 * to StandaloneAiProvider. This follows the same pattern as AnalysisWorkspace ChatPanel.
 *
 * @see ADR-021 - Fluent v9, dark mode required, semantic tokens only
 * @see ADR-022 - React 19 createRoot for Code Pages
 * @see ADR-026 - Single-file Vite build for Dataverse web resource
 * @see StandaloneAiContext.tsx in @spaarke/ai-context — context provider
 * @see .claude/patterns/auth/spaarke-auth-initialization.md — auth bootstrap
 */

import * as React from "react";
import { FluentProvider, makeStyles, tokens } from "@fluentui/react-components";
import {
  resolveCodePageTheme,
  setupCodePageThemeListener,
  ThreePaneLayout,
} from "@spaarke/ui-components";
import { StandaloneAiProvider } from "@spaarke/ai-context";
import { getAuthProvider } from "@spaarke/auth";
import { getBffBaseUrl } from "./config/runtimeConfig";
import { LeftPane } from "./components/LeftPane";
import { OutputPanel } from "./components/OutputPanel";
import { SourcePanel } from "./components/SourcePanel";

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
}

// ---------------------------------------------------------------------------
// AppShell — inner shell rendered inside StandaloneAiProvider
// ---------------------------------------------------------------------------

const useShellStyles = makeStyles({
  shell: {
    display: "flex",
    width: "100%",
    height: "100%",
    overflow: "hidden",
  },
});

function AppShell(): React.JSX.Element {
  const styles = useShellStyles();

  return (
    <div className={styles.shell}>
      <ThreePaneLayout
        leftPane={<LeftPane />}
        centerPane={<OutputPanel />}
        rightPane={<SourcePanel />}
        storageKey="spaarke-ai-workspace"
        defaultLeftWidthPx={340}
        defaultRightWidthPx={400}
        minLeftWidthPx={240}
        minRightWidthPx={240}
        minCenterWidthPx={320}
        leftPaneCollapseLabel="Show AI Chat"
        rightPaneCollapseLabel="Show Sources"
      />
    </div>
  );
}

// ---------------------------------------------------------------------------
// AppWithAuth — acquires BFF token, mounts StandaloneAiProvider
// ---------------------------------------------------------------------------

// eslint-disable-next-line @typescript-eslint/no-unused-vars
function AppWithAuth(_props: AppProps): React.JSX.Element {
  const styles = useStyles();

  const [token, setToken] = React.useState<string | null>(null);
  const [isAuthenticated, setIsAuthenticated] = React.useState<boolean>(false);

  // Acquire BFF access token after auth is initialized (non-blocking for render).
  // StandaloneAiProvider handles partial states (isAuthenticated=false → skips BFF calls).
  React.useEffect(() => {
    let cancelled = false;

    const acquireToken = async (): Promise<void> => {
      try {
        const provider = getAuthProvider();
        const accessToken = await provider.getAccessToken();
        if (!cancelled && accessToken) {
          setToken(accessToken);
          setIsAuthenticated(true);
        }
      } catch (err) {
        if (!cancelled) {
          console.warn("[SpaarkeAi] Token acquisition failed:", err);
          setIsAuthenticated(false);
        }
      }
    };

    void acquireToken();

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
        <StandaloneAiProvider
          bffBaseUrl={bffBaseUrl}
          token={token}
          isAuthenticated={isAuthenticated}
        >
          <AppShell />
        </StandaloneAiProvider>
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
