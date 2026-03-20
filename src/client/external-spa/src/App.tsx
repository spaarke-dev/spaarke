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
import { HashRouter, Routes, Route, Navigate, useNavigate } from "react-router-dom";
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
import { SettingsPage } from "./pages/SettingsPage";
import type { PortalUser } from "./types";

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    // Use 100dvh to guarantee full viewport height regardless of parent container
    // height in the Power Pages Code Site context (parent may be height: auto)
    height: "100dvh",
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    overflow: "hidden",
  },
  content: {
    flex: "1 1 auto",
    display: "flex",
    flexDirection: "column",
    overflow: "auto",
    // Hide scrollbar track but preserve mouse-wheel scrolling
    scrollbarWidth: "none",
    msOverflowStyle: "none",
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
 * Inner shell — needs to be inside HashRouter to use useNavigate.
 */
const AppShell: React.FC<{
  isDark: boolean;
  onToggleDark: () => void;
  portalUser: PortalUser | null;
}> = ({ isDark, onToggleDark, portalUser }) => {
  const styles = useStyles();
  const navigate = useNavigate();

  return (
    <div className={styles.root}>
      <AppHeader
        isDark={isDark}
        onToggleDark={onToggleDark}
        portalUser={portalUser}
        onSettingsClick={() => navigate("/settings")}
      />

      <main className={styles.content}>
        <AuthGuard>
          <ErrorBoundary>
            <Routes>
              <Route path="/" element={<WorkspaceHomePage />} />
              <Route path="/project/:id" element={<ProjectPage />} />
              <Route path="/playbooks/:entityType/:entityId" element={<PlaybookLibraryPage />} />
              <Route path="/upload" element={<DocumentUploadPage />} />
              <Route
                path="/settings"
                element={
                  <SettingsPage
                    isDark={isDark}
                    onToggleDark={onToggleDark}
                    portalUser={portalUser}
                  />
                }
              />
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
  );
};

/**
 * Root App component for the Secure External Workspace SPA.
 *
 * Provides:
 * - FluentProvider with Fluent UI v9 light/dark theming via shared 4-level cascade (ADR-021)
 * - Dark mode toggle persisted to localStorage and synced across tabs
 * - HashRouter (required for Power Pages single-page hosting — all navigation is hash-based)
 * - AuthGuard: redirects unauthenticated users to Entra B2B login via MSAL
 * - Routes for WorkspaceHomePage (#/), ProjectPage (#/project/:id), SettingsPage (#/settings)
 * - ErrorBoundary wrapping all route content
 * - AppHeader with Spaarke logo, user settings link, and theme toggle
 */
export const App: React.FC = () => {
  // Use shared 4-level theme cascade: localStorage > URL flags > navbar DOM > system preference.
  // resolveCodePageTheme() handles all levels including system preference fallback when
  // Dataverse context is unavailable (which is always the case for this external SPA).
  const [theme, setTheme] = React.useState<Theme>(resolveCodePageTheme);

  const { accounts } = useMsal();
  const portalUser = import.meta.env.VITE_DEV_MOCK === "true"
    ? { userName: "jane.smith@externalfirm.com", firstName: "Jane", lastName: "Smith", displayName: "Jane Smith (Mock)", tenantId: "mock" }
    : accountToPortalUser(accounts[0]);

  const isDark = theme === webDarkTheme;

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
        <AppShell
          isDark={isDark}
          onToggleDark={handleToggleDark}
          portalUser={portalUser}
        />
      </HashRouter>
    </FluentProvider>
  );
};

export default App;
