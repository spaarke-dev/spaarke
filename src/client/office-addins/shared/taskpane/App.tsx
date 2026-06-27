import React, { useEffect, useState, useCallback } from 'react';
import { FluentProvider, Spinner, makeStyles, tokens } from '@fluentui/react-components';
import { authService } from '@shared/services';
import type { IHostAdapter, IHostContext } from '@shared/adapters';
import { useTheme } from './hooks/useTheme';
import { useOfficeTheme, useLinkedTodosForCommunication } from './hooks';
import { TaskPaneShell, type NavigationTab, type HostType } from './components/TaskPaneShell';
import { LinkedTodosBanner } from './components/LinkedTodosBanner';
import { SaveView } from './components/views/SaveView';
import { ShareView } from './components/views/ShareView';
import { StatusView } from './components/views/StatusView';
import { SignInView } from './components/views/SignInView';
import { CreateTodoView } from './components/views/CreateTodoView';
import type { SaveEmailToSpaarkeFn } from './hooks';

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
  /**
   * `sprk_communicationid` of the saved Spaarke communication record for the
   * current email, when known. When provided AND the host is Outlook, the
   * LinkedTodosBanner queries the BFF for linked sprk_todo records and renders
   * a pinned indicator (FR-28 / A-1).
   *
   * Source: task 070 (Outlook ribbon "Create To Do") is responsible for
   * wiring email → communication lookup and threading the resulting id into
   * this prop. When task 070 hasn't run yet (or the email is not saved to
   * Spaarke), leave this undefined — the banner stays hidden.
   */
  communicationId?: string;
  /**
   * Optional callback when the user clicks "View list" on the LinkedTodosBanner.
   * Host-supplied so the URL (SmartTodo Code Page filtered by communicationId)
   * stays config-driven — keeps `LinkedTodosBanner` free of hardcoded org URLs.
   */
  onViewLinkedTodos?: (communicationId: string) => void;
  /**
   * Optional Outlook "Create To Do" ribbon-action wiring (smart-todo-decoupling-r3
   * FR-27 / task 070). When provided AND `initialAction === 'createTodo'` (set by
   * the host based on the URL `?action=createTodo` query param), the taskpane
   * renders the CreateTodoView instead of the default tabs.
   *
   * - `codePageBaseUrl`: SmartTodo Code Page base URL (env-supplied; no hardcoded
   *   org URLs allowed per CLAUDE.md §16).
   * - `saveEmailToSpaarke`: callback the host wires to its existing Save flow.
   *   When the email isn't already saved, the view invokes this; the host runs
   *   the SaveView and resolves with the new sprk_communication triple.
   */
  createTodoConfig?: {
    codePageBaseUrl: string;
    saveEmailToSpaarke: SaveEmailToSpaarkeFn;
  };
  /**
   * Initial action discriminator read from the host's URL / launch context.
   * Today: 'createTodo' to mount CreateTodoView immediately. Default: undefined
   * (renders the default tab from `initialTab`).
   */
  initialAction?: 'createTodo';
}

export const App: React.FC<AppProps> = ({
  hostAdapter,
  title,
  initialTab = 'save',
  version = '1.0.1',
  buildDate,
  showErrorDetails = process.env.NODE_ENV === 'development',
  communicationId,
  onViewLinkedTodos,
  createTodoConfig,
  initialAction,
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
        await new Promise(resolve => setTimeout(resolve, 500));
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
  const displayTitle = title || 'Spaarke Add-in';

  // Outlook "Create To Do" ribbon-action mode (smart-todo-decoupling-r3 FR-27 / task 070).
  // When the host launched the taskpane with `?action=createTodo` AND a
  // `createTodoConfig` is supplied, the CreateTodoView replaces the default tabs.
  // Outlook-only — the action makes no sense in Word.
  const showCreateTodoView =
    initialAction === 'createTodo' && createTodoConfig !== undefined && hostAdapter.getHostType() === 'outlook';

  // Outlook taskpane banner indicator (smart-todo-decoupling-r3 FR-28 / A-1).
  // The hook is inert when communicationId is undefined / not Outlook, so it
  // safely no-ops on the Word add-in.
  const indicatorTargetId = hostType === 'outlook' ? communicationId : undefined;
  const linkedTodos = useLinkedTodosForCommunication(indicatorTargetId);
  const handleViewLinkedTodos = useCallback(() => {
    if (indicatorTargetId && onViewLinkedTodos) {
      onViewLinkedTodos(indicatorTargetId);
    }
  }, [indicatorTargetId, onViewLinkedTodos]);
  const showLinkedTodosBanner =
    hostType === 'outlook' &&
    indicatorTargetId !== undefined &&
    (linkedTodos.isLoading || linkedTodos.error !== null || linkedTodos.count > 0);

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
        {/* Linked Spaarke to-dos banner (Outlook only, smart-todo-decoupling-r3 FR-28) */}
        {showLinkedTodosBanner && (
          <LinkedTodosBanner
            count={linkedTodos.count}
            isLoading={linkedTodos.isLoading}
            error={linkedTodos.error}
            {...(onViewLinkedTodos ? { onViewList: handleViewLinkedTodos } : {})}
          />
        )}

        {/*
          Outlook "Create To Do" ribbon-action view (smart-todo-decoupling-r3 FR-27 / task 070).
          Mounted instead of the default tabs when the host invoked the taskpane
          with `?action=createTodo` (set by the manifest button click handler).
          Requires `createTodoConfig` to wire the save flow + code-page URL — when
          absent we fall through to the default tabs so the action degrades gracefully.
        */}
        {showCreateTodoView && (
          <CreateTodoView
            hostAdapter={hostAdapter}
            saveEmailToSpaarke={createTodoConfig!.saveEmailToSpaarke}
            codePageBaseUrl={createTodoConfig!.codePageBaseUrl}
          />
        )}

        {/* Tab Content (default — when not in createTodo action mode) */}
        {!showCreateTodoView && currentTab === 'save' && (
          <SaveView
            hostAdapter={hostAdapter}
            getAccessToken={async () => {
              const token = await authService.getAccessToken(['user_impersonation']);
              return token || '';
            }}
            apiBaseUrl={process.env.BFF_API_BASE_URL || 'https://spaarke-bff-dev.azurewebsites.net'}
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

        {!showCreateTodoView && currentTab === 'share' && (
          <ShareView
            onSearch={async query => {
              // Placeholder - will connect to API in later tasks
              console.log('Search:', query);
              return [];
            }}
            onGenerateLink={async (docId, permissions) => {
              // Placeholder - will connect to API in later tasks
              console.log('Generate link:', docId, permissions);
              return `https://spaarke.com/share/${docId}`;
            }}
            onInsertLink={async link => {
              // Placeholder - will use host adapter in later tasks
              console.log('Insert link:', link);
            }}
          />
        )}

        {!showCreateTodoView && currentTab === 'search' && (
          <StatusView
            onFetchJobs={async () => {
              // Placeholder - shows document search in later tasks
              return [];
            }}
            refreshInterval={0}
          />
        )}

        {!showCreateTodoView && currentTab === 'recent' && (
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
