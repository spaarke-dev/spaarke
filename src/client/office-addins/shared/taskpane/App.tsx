import React, { useEffect, useState, useCallback } from 'react';
import { FluentProvider, Spinner, makeStyles, tokens } from '@fluentui/react-components';
import { authService } from '@shared/services';
import type { IHostAdapter, IHostContext } from '@shared/adapters';
import { useTheme } from './hooks/useTheme';
import { useOfficeTheme } from './hooks';
import {
  TaskPaneShell,
  type NavigationTab,
  type HostType,
} from './components/TaskPaneShell';
import { SaveView } from './components/views/SaveView';
import { ShareView } from './components/views/ShareView';
import { StatusView } from './components/views/StatusView';
import { SignInView } from './components/views/SignInView';

/**
 * Main App shell for Office Add-in taskpane.
 *
 * Uses Fluent UI v9 exclusively per ADR-021.
 * Supports dark mode based on Office theme detection and user preference.
 * Features:
 * - NAA authentication with fallback
 * - Host-specific adapters (Outlook/Word)
 * - Tab-based navigation
 * - Responsive layout
 * - Error boundary
 * - Loading skeleton
 */

const useStyles = makeStyles({
  loadingContainer: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    width: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
  },
});

export interface AppProps {
  /** Host adapter for Outlook/Word operations */
  hostAdapter: IHostAdapter;
  /** Title displayed in header (optional, derived from host if not provided) */
  title?: string;
  /** Initial navigation tab */
  initialTab?: NavigationTab;
  /** Application version */
  version?: string;
  /** Build date string */
  buildDate?: string;
  /** Whether to show error details (development mode) */
  showErrorDetails?: boolean;
}

export const App: React.FC<AppProps> = ({
  hostAdapter,
  title,
  initialTab = 'save',
  version = '1.0.1',
  buildDate,
  showErrorDetails = process.env.NODE_ENV === 'development',
}) => {
  const styles = useStyles();

  // Theme management - combines Office detection with user preference
  const { theme, preference, setPreference, isDarkMode } = useTheme();

  // State
  const [isInitializing, setIsInitializing] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [hostContext, setHostContext] = useState<IHostContext | null>(null);
  const [currentTab, setCurrentTab] = useState<NavigationTab>(initialTab);

  // Save operation state
  const [isSaving, setIsSaving] = useState(false);
  const [saveProgress, setSaveProgress] = useState(0);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saveSuccess, setSaveSuccess] = useState<string | null>(null);

  // Connection status
  const [connectionStatus, setConnectionStatus] = useState<'connected' | 'disconnected' | 'connecting'>('connecting');

  // Initialize the application
  const initializeApp = useCallback(async () => {
    try {
      setIsInitializing(true);
      setError(null);
      setConnectionStatus('connecting');

      // Initialize host adapter (may already be initialized in index.tsx)
      if (!hostAdapter.isInitialized()) {
        await hostAdapter.initialize();
      }

      // Build host context from new adapter methods
      const itemId = await hostAdapter.getItemId();
      const subject = await hostAdapter.getSubject();
      const itemType = hostAdapter.getItemType();

      const context: IHostContext = {
        itemId,
        itemType,
        displayName: subject || 'No Subject',
        metadata: {
          hostType: hostAdapter.getHostType(),
        },
      };
      setHostContext(context);

      // Check authentication status
      setIsAuthenticated(authService.isAuthenticated());
      setConnectionStatus('connected');

      setIsInitializing(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to initialize');
      setConnectionStatus('disconnected');
      setIsInitializing(false);
    }
  }, [hostAdapter]);

  useEffect(() => {
    initializeApp();
  }, [initializeApp]);

  // Sign in handler
  const handleSignIn = async () => {
    try {
      setError(null);
      await authService.signIn();
      setIsAuthenticated(authService.isAuthenticated());
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Sign in failed');
    }
  };

  // Sign out handler
  const handleSignOut = async () => {
    await authService.signOut();
    setIsAuthenticated(false);
    setCurrentTab('save'); // Reset to default tab
  };

  // Save handler (placeholder - will connect to API in later tasks)
  const handleSave = async () => {
    setIsSaving(true);
    setSaveError(null);
    setSaveSuccess(null);
    setSaveProgress(0);

    try {
      // Simulate save progress
      for (let i = 0; i <= 100; i += 20) {
        setSaveProgress(i);
        await new Promise((resolve) => setTimeout(resolve, 500));
      }

      setSaveSuccess('Document saved successfully to Spaarke');
    } catch (err) {
      setSaveError(err instanceof Error ? err.message : 'Save failed');
    } finally {
      setIsSaving(false);
    }
  };

  // Settings handler (placeholder)
  const handleSettings = () => {
    // Will open settings dialog in later tasks
    console.log('Settings clicked');
  };

  // Error handler for error boundary
  const handleError = (boundaryError: Error, errorInfo: React.ErrorInfo) => {
    console.error('App error boundary caught:', boundaryError, errorInfo);
    // Could send to telemetry service here
  };

  // Determine title and host type
  const hostType: HostType = hostAdapter.getHostType() === 'outlook' ? 'outlook' : 'word';
  const displayTitle = title || (hostType === 'outlook' ? 'Spaarke for Outlook' : 'Spaarke for Word');

  // Get user info
  const account = authService.getAccount();
  const userName = account?.name || account?.username;
  const userEmail = account?.username;

  // Show minimal loading spinner during initial auth check
  if (isInitializing) {
    return (
      <FluentProvider theme={theme}>
        <div className={styles.loadingContainer}>
          <Spinner label="Loading..." />
        </div>
      </FluentProvider>
    );
  }

  // Not authenticated - show sign in view
  if (!isAuthenticated) {
    return (
      <FluentProvider theme={theme}>
        <TaskPaneShell
          title={displayTitle}
          hostType={hostType}
          isAuthenticated={false}
          version={version}
          buildDate={buildDate}
          connectionStatus={connectionStatus}
          showNavigation={false}
          themePreference={preference}
          onThemeChange={setPreference}
          showErrorDetails={showErrorDetails}
          onError={handleError}
        >
          <SignInView onSignIn={handleSignIn} error={error} />
        </TaskPaneShell>
      </FluentProvider>
    );
  }

  // Authenticated - show main app with navigation
  return (
    <FluentProvider theme={theme}>
      <TaskPaneShell
        title={displayTitle}
        hostType={hostType}
        userName={userName}
        userEmail={userEmail}
        isAuthenticated={true}
        onSignOut={handleSignOut}
        onSettings={handleSettings}
        version={version}
        buildDate={buildDate}
        connectionStatus={connectionStatus}
        showNavigation={false}
        selectedTab={currentTab}
        onTabChange={setCurrentTab}
        themePreference={preference}
        onThemeChange={setPreference}
        showErrorDetails={showErrorDetails}
        onError={handleError}
      >
        {/* Tab Content */}
        {currentTab === 'save' && (
          <SaveView
            hostAdapter={hostAdapter}
            getAccessToken={async () => {
              const token = await authService.getAccessToken(['user_impersonation']);
              return token || '';
            }}
            apiBaseUrl={process.env.BFF_API_BASE_URL || 'https://spe-api-dev-67e2xz.azurewebsites.net'}
            onComplete={(docId, docUrl) => {
              console.log('Save complete:', docId, docUrl);
            }}
            onQuickCreate={(entityType, searchQuery) => {
              // Quick Create - opens Dataverse form in new window
              const baseUrl = 'https://spaarkedev1.crm.dynamics.com';
              const entityMap: Record<string, string> = {
                Matter: 'sprk_matter',
                Project: 'sprk_project',
                Account: 'account',
                Contact: 'contact',
                Invoice: 'invoice',
              };
              const logicalName = entityMap[entityType] || entityType.toLowerCase();
              // Open quick create form in Dataverse
              const createUrl = `${baseUrl}/main.aspx?etn=${logicalName}&pagetype=entityrecord&cmdbar=false&navbar=off`;
              window.open(createUrl, '_blank', 'width=600,height=700');
              console.log('Quick create:', entityType, searchQuery);
            }}
          />
        )}

        {currentTab === 'share' && (
          <ShareView
            onSearch={async (query) => {
              // Placeholder - will connect to API in later tasks
              console.log('Search:', query);
              return [];
            }}
            onGenerateLink={async (docId, permissions) => {
              // Placeholder - will connect to API in later tasks
              console.log('Generate link:', docId, permissions);
              return `https://spaarke.com/share/${docId}`;
            }}
            onInsertLink={async (link) => {
              // Placeholder - will use host adapter in later tasks
              console.log('Insert link:', link);
            }}
          />
        )}

        {currentTab === 'search' && (
          <StatusView
            onFetchJobs={async () => {
              // Placeholder - shows document search in later tasks
              return [];
            }}
            refreshInterval={0}
          />
        )}

        {currentTab === 'recent' && (
          <StatusView
            onFetchJobs={async () => {
              // Placeholder - will connect to API in later tasks
              return [];
            }}
            refreshInterval={5000}
          />
        )}
      </TaskPaneShell>
    </FluentProvider>
  );
};
