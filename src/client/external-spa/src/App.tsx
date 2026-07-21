import * as React from 'react';
import { Button, FluentProvider, makeStyles, tokens, Text, webDarkTheme } from '@fluentui/react-components';
import type { Theme } from '@fluentui/react-components';
import { useMsal } from '@azure/msal-react';
import { BrowserRouter, Routes, Route, useNavigate } from 'react-router-dom';
import { resolveCodePageTheme, setupCodePageThemeListener, setUserThemePreference } from '@spaarke/ui-components/utils/themeStorage';
import { APP_VERSION } from './config';
import { AppHeader } from './components/AppHeader';
import { AuthGuard } from './components/AuthGuard';
import { ErrorBoundary } from './components/ErrorBoundary';
import { WorkspaceHomePage } from './pages/WorkspaceHomePage';
import { ProjectPage } from './pages/ProjectPage';
import { PlaybookLibraryPage } from './pages/PlaybookLibraryPage';
import { DocumentUploadPage } from './pages/DocumentUploadPage';
import { SettingsPage } from './pages/SettingsPage';
import type { PortalUser } from './types';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    // Use 100dvh to guarantee full viewport height regardless of parent container
    // height in the SWA hosting context (parent may be height: auto)
    height: '100dvh',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
  },
  content: {
    flex: '1 1 auto',
    display: 'flex',
    flexDirection: 'column',
    overflow: 'auto',
    // Hide scrollbar track but preserve mouse-wheel scrolling
    scrollbarWidth: 'none',
    msOverflowStyle: 'none',
  },
  footer: {
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalL}`,
    borderTopWidth: '1px',
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
    display: 'flex',
    justifyContent: 'flex-end',
    flexShrink: 0,
  },
  notFound: {
    flex: '1 1 auto',
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    rowGap: tokens.spacingVerticalM,
    padding: tokens.spacingVerticalXXL,
    textAlign: 'center',
  },
  notFoundCode: {
    fontSize: '48px',
    lineHeight: '1',
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
  },
});

/**
 * Map an MSAL account to the PortalUser shape used by AppHeader and pages.
 */
function accountToPortalUser(account: ReturnType<typeof useMsal>['accounts'][0] | undefined): PortalUser | null {
  if (!account) return null;
  const nameParts = (account.name ?? '').split(' ');
  return {
    userName: account.username,
    firstName: nameParts[0] ?? '',
    lastName: nameParts.slice(1).join(' '),
    displayName: account.name ?? account.username,
    tenantId: account.tenantId,
  };
}

/**
 * In-app not-found (404) view for unknown clean-URL paths. Anchored to the external-spa
 * Fluent v9 look (tokens + Text) so it renders correctly in both light and dark themes
 * (ADR-021 — no hardcoded colors). Replaces the former `Navigate to="/"` silent redirect
 * so a genuinely bad path is visible instead of masked.
 */
const NotFoundView: React.FC = () => {
  const styles = useStyles();
  const navigate = useNavigate();
  return (
    <div className={styles.notFound}>
      <Text as="h1" className={styles.notFoundCode}>404</Text>
      <Text size={500} weight="semibold">Page not found</Text>
      <Text size={300} style={{ color: tokens.colorNeutralForeground3 }}>
        The page you&apos;re looking for doesn&apos;t exist or may have moved.
      </Text>
      <Button appearance="primary" onClick={() => navigate('/')}>
        Back to my workspace
      </Button>
    </div>
  );
};

/**
 * Inner shell — needs to be inside BrowserRouter to use useNavigate.
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
        onSettingsClick={() => navigate('/settings')}
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
                element={<SettingsPage isDark={isDark} onToggleDark={onToggleDark} portalUser={portalUser} />}
              />
              {/* Unknown paths render an explicit in-app 404 (not a silent redirect home). */}
              <Route path="*" element={<NotFoundView />} />
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
 * - BrowserRouter (clean URLs) — deep links resolve via the SWA navigationFallback rewrite
 *   (staticwebapp.config.json, task 010); unknown paths render an in-app 404 view
 * - AuthGuard: redirects unauthenticated users to Entra login via MSAL
 * - Routes for WorkspaceHomePage (/), ProjectPage (/project/:id), SettingsPage (/settings)
 * - ErrorBoundary wrapping all route content
 * - AppHeader with Spaarke logo, user settings link, and theme toggle
 */
export const App: React.FC = () => {
  // Use shared 4-level theme cascade: localStorage > URL flags > navbar DOM > system preference.
  // resolveCodePageTheme() handles all levels including system preference fallback when
  // Dataverse context is unavailable (which is always the case for this external SPA).
  const [theme, setTheme] = React.useState<Theme>(resolveCodePageTheme);

  const { accounts } = useMsal();
  const portalUser =
    import.meta.env.VITE_DEV_MOCK === 'true'
      ? {
          userName: 'jane.smith@externalfirm.com',
          firstName: 'Jane',
          lastName: 'Smith',
          displayName: 'Jane Smith (Mock)',
          tenantId: 'mock',
        }
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
    const newPreference = isDark ? 'light' : 'dark';
    setUserThemePreference(newPreference);
    // setUserThemePreference dispatches THEME_CHANGE_EVENT which setupCodePageThemeListener
    // handles, but we also set state directly for immediate UI response.
    setTheme(resolveCodePageTheme());
  }, [isDark]);

  return (
    <FluentProvider theme={theme} style={{ height: '100%' }}>
      <BrowserRouter>
        <AppShell isDark={isDark} onToggleDark={handleToggleDark} portalUser={portalUser} />
      </BrowserRouter>
    </FluentProvider>
  );
};

export default App;
