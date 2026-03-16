import * as React from "react";
import {
  FluentProvider,
  makeStyles,
  tokens,
  Text,
  Spinner,
  webDarkTheme,
  webLightTheme,
} from "@fluentui/react-components";
import { HashRouter, Routes, Route, Navigate } from "react-router-dom";
import { APP_VERSION } from "./config";
import { AppHeader } from "./components/AppHeader";
import { ErrorBoundary } from "./components/ErrorBoundary";
import { WorkspaceHomePage } from "./pages/WorkspaceHomePage";
import { ProjectPage } from "./pages/ProjectPage";
import type { PortalUser } from "./types";

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    overflow: "hidden",
  },
  content: {
    flex: "1 1 auto",
    display: "flex",
    flexDirection: "column",
    overflow: "auto",
  },
  footer: {
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalL}`,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
    display: "flex",
    justifyContent: "flex-end",
    flexShrink: 0,
  },
  loadingOverlay: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    flex: "1 1 auto",
    gap: tokens.spacingVerticalM,
  },
});

/**
 * Attempt to read portal user context from the Power Pages shell globals.
 * Power Pages injects `window["Microsoft.Dynamic365.Portal.User"]` (or similar)
 * on authenticated pages. Returns null when running locally or unauthenticated.
 */
function readPortalUser(): PortalUser | null {
  try {
    // Power Pages standard portal user global
    const raw = (window as Record<string, unknown>)["Microsoft.Dynamic365.Portal.User"];
    if (raw && typeof raw === "object") {
      const u = raw as Record<string, string>;
      const firstName = u["firstName"] ?? "";
      const lastName = u["lastName"] ?? "";
      const fullName = u["fullName"];
      const email = u["email"] ?? "";
      const nameParts = `${firstName} ${lastName}`.trim();
      return {
        userName: email || (u["userName"] ?? ""),
        firstName,
        lastName,
        displayName: fullName ?? (nameParts || email),
        tenantId: u["tenantId"],
      };
    }
  } catch {
    // Silently ignore — portal context may not be available in dev
  }
  return null;
}

/**
 * Root App component for the Secure Project Workspace SPA.
 *
 * Provides:
 * - FluentProvider with Fluent UI v9 light/dark theming (ADR-021)
 * - Dark mode toggle synced to OS preference with manual override
 * - HashRouter (required for Power Pages single-page hosting — all navigation is hash-based)
 * - Routes for WorkspaceHomePage (#/) and ProjectPage (#/project/:id)
 * - ErrorBoundary wrapping all route content for graceful error handling
 * - AppHeader with user info and theme toggle
 * - Loading state while portal context is being read
 *
 * See ADR-022 for React 18 Code Page pattern.
 */
export const App: React.FC = () => {
  const [isDark, setIsDark] = React.useState(
    () => window.matchMedia?.("(prefers-color-scheme: dark)").matches ?? false
  );

  const [isLoadingUser, setIsLoadingUser] = React.useState(true);
  const [portalUser, setPortalUser] = React.useState<PortalUser | null>(null);

  const theme = isDark ? webDarkTheme : webLightTheme;
  const styles = useStyles();

  // Listen for OS-level color scheme changes
  React.useEffect(() => {
    const mediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
    const handler = (e: MediaQueryListEvent) => setIsDark(e.matches);
    mediaQuery.addEventListener("change", handler);
    return () => mediaQuery.removeEventListener("change", handler);
  }, []);

  // Load portal user context (Power Pages injects user globals asynchronously)
  React.useEffect(() => {
    const user = readPortalUser();
    setPortalUser(user);
    setIsLoadingUser(false);
  }, []);

  return (
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      <HashRouter>
        <div className={styles.root}>
          <AppHeader
            isDark={isDark}
            onToggleDark={() => setIsDark((prev) => !prev)}
            portalUser={portalUser}
          />

          <main className={styles.content}>
            {isLoadingUser ? (
              <div className={styles.loadingOverlay}>
                <Spinner size="large" label="Loading workspace..." />
              </div>
            ) : (
              <ErrorBoundary>
                <Routes>
                  <Route path="/" element={<WorkspaceHomePage />} />
                  <Route path="/project/:id" element={<ProjectPage />} />
                  {/* Redirect any unknown hash paths back to home */}
                  <Route path="*" element={<Navigate to="/" replace />} />
                </Routes>
              </ErrorBoundary>
            )}
          </main>

          <footer className={styles.footer}>
            <Text size={100} style={{ color: tokens.colorNeutralForeground4 }}>
              v{APP_VERSION}
            </Text>
          </footer>
        </div>
      </HashRouter>
    </FluentProvider>
  );
};

export default App;
