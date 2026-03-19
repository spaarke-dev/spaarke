import * as React from "react";
import {
  FluentProvider,
  makeStyles,
  tokens,
  Text,
  webDarkTheme,
} from "@fluentui/react-components";
import type { Theme } from "@fluentui/react-components";
import { useMsal } from "@azure/msal-react";
import { HashRouter, Routes, Route, Navigate } from "react-router-dom";
import {
  resolveCodePageTheme,
  setupCodePageThemeListener,
  setUserThemePreference,
} from "@spaarke/ui-components";
import { APP_VERSION } from "./config";
import { AppHeader } from "./components/AppHeader";
import { AuthGuard } from "./components/AuthGuard";
import { ErrorBoundary } from "./components/ErrorBoundary";
import { WorkspaceHomePage } from "./pages/WorkspaceHomePage";
import { ProjectPage } from "./pages/ProjectPage";
import { PlaybookLibraryPage } from "./pages/PlaybookLibraryPage";
import { DocumentUploadPage } from "./pages/DocumentUploadPage";
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
});

/**
 * Map an MSAL account to the PortalUser shape used by AppHeader and pages.
 * MSAL account claims: name, username (UPN/email), localAccountId, tenantId.
 */
function accountToPortalUser(account: ReturnType<typeof useMsal>["accounts"][0] | undefined): PortalUser | null {
  if (!account) return null;
  const nameParts = (account.name ?? "").split(" ");
  return {
    userName: account.username,
    firstName: nameParts[0] ?? "",
    lastName: nameParts.slice(1).join(" "),
    displayName: account.name ?? account.username,
    tenantId: account.tenantId,
  };
}

/**
 * Root App component for the Secure Project Workspace SPA.
 *
 * Provides:
 * - FluentProvider with Fluent UI v9 light/dark theming via shared 4-level cascade (ADR-021)
 * - Dark mode toggle persisted to localStorage and synced across tabs
 * - HashRouter (required for Power Pages single-page hosting — all navigation is hash-based)
 * - AuthGuard: redirects unauthenticated users to Entra B2B login via MSAL
 * - Routes for WorkspaceHomePage (#/) and ProjectPage (#/project/:id)
 * - ErrorBoundary wrapping all route content for graceful error handling
 * - AppHeader with user info (from MSAL account) and theme toggle
 *
 * See ADR-022 for React 18 Code Page pattern.
 * See notes/auth-migration-b2b-msal.md for auth architecture.
 */
export const App: React.FC = () => {
  // Use shared 4-level theme cascade: localStorage > URL flags > navbar DOM > system preference.
  // resolveCodePageTheme() handles all levels including system preference fallback when
  // Dataverse context is unavailable (which is always the case for this external SPA).
  const [theme, setTheme] = React.useState<Theme>(resolveCodePageTheme);

  const { accounts } = useMsal();
  const portalUser = accountToPortalUser(accounts[0]);

  const isDark = theme === webDarkTheme;
  const styles = useStyles();

  // Listen for theme changes: localStorage (cross-tab), custom events (same-tab), system preference
  React.useEffect(() => {
    const cleanup = setupCodePageThemeListener(setTheme);
    return cleanup;
  }, []);

  // Toggle persists to localStorage and dispatches custom event so all listeners (including
  // other tabs and shared components) pick up the change via setupCodePageThemeListener.
  const handleToggleDark = React.useCallback(() => {
    const newPreference = isDark ? "light" : "dark";
    setUserThemePreference(newPreference);
    // setUserThemePreference dispatches THEME_CHANGE_EVENT which setupCodePageThemeListener
    // handles, but we also set state directly for immediate UI response.
    setTheme(resolveCodePageTheme());
  }, [isDark]);

  return (
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      <HashRouter>
        <div className={styles.root}>
          <AppHeader
            isDark={isDark}
            onToggleDark={handleToggleDark}
            portalUser={portalUser}
          />

          <main className={styles.content}>
            <AuthGuard>
              <ErrorBoundary>
                <Routes>
                  <Route path="/" element={<WorkspaceHomePage />} />
                  <Route path="/project/:id" element={<ProjectPage />} />
                  <Route path="/playbooks/:entityType/:entityId" element={<PlaybookLibraryPage />} />
                  <Route path="/upload" element={<DocumentUploadPage />} />
                  {/* Redirect any unknown hash paths back to home */}
                  <Route path="*" element={<Navigate to="/" replace />} />
                </Routes>
              </ErrorBoundary>
            </AuthGuard>
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
