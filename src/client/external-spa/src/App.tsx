import * as React from "react";
import {
  FluentProvider,
  makeStyles,
  tokens,
  Text,
  webDarkTheme,
  webLightTheme,
} from "@fluentui/react-components";
import { useMsal } from "@azure/msal-react";
import { HashRouter, Routes, Route, Navigate, useNavigate } from "react-router-dom";
import { APP_VERSION } from "./config";
import { AppHeader } from "./components/AppHeader";
import { AuthGuard } from "./components/AuthGuard";
import { ErrorBoundary } from "./components/ErrorBoundary";
import { WorkspaceHomePage } from "./pages/WorkspaceHomePage";
import { ProjectPage } from "./pages/ProjectPage";
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
 * - FluentProvider with Fluent UI v9 light/dark theming (ADR-021)
 * - Dark mode defaults to OS preference; manual override via toggle and settings page
 * - HashRouter (required for Power Pages single-page hosting)
 * - AuthGuard: redirects unauthenticated users to Entra B2B login via MSAL
 * - Routes for WorkspaceHomePage (#/), ProjectPage (#/project/:id), SettingsPage (#/settings)
 * - ErrorBoundary wrapping all route content
 * - AppHeader with Spaarke logo, user settings link, and theme toggle
 */
export const App: React.FC = () => {
  // Default: light mode unless the OS explicitly prefers dark.
  const [isDark, setIsDark] = React.useState(
    () => window.matchMedia?.("(prefers-color-scheme: dark)").matches ?? false
  );

  const { accounts } = useMsal();
  const portalUser = import.meta.env.VITE_DEV_MOCK === "true"
    ? { userName: "jane.smith@externalfirm.com", firstName: "Jane", lastName: "Smith", displayName: "Jane Smith (Mock)", tenantId: "mock" }
    : accountToPortalUser(accounts[0]);

  const theme = isDark ? webDarkTheme : webLightTheme;

  // Track OS-level color scheme changes
  React.useEffect(() => {
    const mediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
    const handler = (e: MediaQueryListEvent) => setIsDark(e.matches);
    mediaQuery.addEventListener("change", handler);
    return () => mediaQuery.removeEventListener("change", handler);
  }, []);

  return (
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      <HashRouter>
        <AppShell
          isDark={isDark}
          onToggleDark={() => setIsDark((prev) => !prev)}
          portalUser={portalUser}
        />
      </HashRouter>
    </FluentProvider>
  );
};

export default App;
