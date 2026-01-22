import React, { useState, useEffect, useCallback } from 'react';
import { makeStyles, tokens } from '@fluentui/react-components';
import { TaskPaneHeader, type HostType } from './TaskPaneHeader';
import { TaskPaneNavigation, type NavigationTab, getDefaultTab } from './TaskPaneNavigation';
import { TaskPaneFooter, type ConnectionStatus } from './TaskPaneFooter';
import { ErrorBoundary } from './ErrorBoundary';
import { LoadingSkeleton } from './LoadingSkeleton';
import type { ThemePreference } from '../hooks/useTheme';

/**
 * TaskPaneShell - Main layout component for Office Add-in task pane.
 *
 * Provides:
 * - Consistent header with host-specific branding
 * - Tab-based navigation (Save, Share, Search, Recent)
 * - Content area with error boundary
 * - Footer with version info
 * - Responsive layout for different task pane widths
 * - Loading skeleton state
 *
 * Uses Fluent UI v9 design tokens per ADR-021.
 */

/**
 * Breakpoints for responsive layout.
 * Task panes can be resized between 320px and 450px.
 */
const COMPACT_BREAKPOINT = 360;

const useStyles = makeStyles({
  shell: {
    display: 'flex',
    flexDirection: 'column',
    height: '100vh',
    width: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
  },
  content: {
    flex: 1,
    overflow: 'auto',
    padding: tokens.spacingVerticalM,
  },
  contentCompact: {
    padding: tokens.spacingVerticalS,
  },
  contentNoPadding: {
    padding: 0,
  },
});

export interface TaskPaneShellProps {
  /** Title displayed in header */
  title?: string;
  /** Type of Office host */
  hostType?: HostType;
  /** Current user display name */
  userName?: string;
  /** Current user email */
  userEmail?: string;
  /** Whether user is authenticated */
  isAuthenticated?: boolean;
  /** Callback when user clicks sign out */
  onSignOut?: () => void;
  /** Callback when user clicks settings */
  onSettings?: () => void;
  /** Whether the shell is in loading state */
  isLoading?: boolean;
  /** Version string for footer */
  version?: string;
  /** Build date string for footer */
  buildDate?: string;
  /** Application name for footer */
  appName?: string;
  /** Connection status for footer */
  connectionStatus?: ConnectionStatus;
  /** Whether to show navigation tabs */
  showNavigation?: boolean;
  /** Currently selected navigation tab */
  selectedTab?: NavigationTab;
  /** Callback when navigation tab changes */
  onTabChange?: (tab: NavigationTab) => void;
  /** Theme preference */
  themePreference?: ThemePreference;
  /** Callback when theme changes */
  onThemeChange?: (preference: ThemePreference) => void;
  /** Error handler for error boundary */
  onError?: (error: Error, errorInfo: React.ErrorInfo) => void;
  /** Whether to show error details (development mode) */
  showErrorDetails?: boolean;
  /** Content to render in main area */
  children: React.ReactNode;
  /** Whether to remove padding from content area */
  noPadding?: boolean;
}

/**
 * Hook to detect task pane width and determine compact mode.
 */
function useResponsiveLayout(): { isCompact: boolean; width: number } {
  const [width, setWidth] = useState<number>(
    typeof window !== 'undefined' ? window.innerWidth : 400
  );

  useEffect(() => {
    if (typeof window === 'undefined') {
      return undefined;
    }

    const handleResize = () => {
      setWidth(window.innerWidth);
    };

    window.addEventListener('resize', handleResize);
    return () => {
      window.removeEventListener('resize', handleResize);
    };
  }, []);

  return {
    isCompact: width < COMPACT_BREAKPOINT,
    width,
  };
}

export const TaskPaneShell: React.FC<TaskPaneShellProps> = ({
  title = 'Spaarke',
  hostType = 'outlook',
  userName,
  userEmail,
  isAuthenticated = false,
  onSignOut,
  onSettings,
  isLoading = false,
  version = '1.0.1',
  buildDate,
  appName = 'Spaarke DMS',
  connectionStatus,
  showNavigation = true,
  selectedTab: controlledSelectedTab,
  onTabChange,
  themePreference = 'auto',
  onThemeChange,
  onError,
  showErrorDetails = false,
  children,
  noPadding = false,
}) => {
  const styles = useStyles();
  const { isCompact } = useResponsiveLayout();

  // Internal state for uncontrolled navigation
  const [internalSelectedTab, setInternalSelectedTab] = useState<NavigationTab>(() =>
    getDefaultTab(hostType)
  );

  // Use controlled or uncontrolled tab state
  const selectedTab = controlledSelectedTab ?? internalSelectedTab;

  const handleTabChange = useCallback(
    (tab: NavigationTab) => {
      if (onTabChange) {
        onTabChange(tab);
      } else {
        setInternalSelectedTab(tab);
      }
    },
    [onTabChange]
  );

  // Show loading skeleton during initialization
  if (isLoading) {
    return (
      <div className={styles.shell}>
        <LoadingSkeleton
          showHeader={true}
          showNavigation={showNavigation}
          showFooter={true}
          contentCards={2}
        />
      </div>
    );
  }

  // Determine content class based on compact mode and noPadding
  let contentClassName = styles.content;
  if (noPadding) {
    contentClassName = `${styles.content} ${styles.contentNoPadding}`;
  } else if (isCompact) {
    contentClassName = `${styles.content} ${styles.contentCompact}`;
  }

  return (
    <div className={styles.shell}>
      {/* Header */}
      <TaskPaneHeader
        title={title}
        hostType={hostType}
        userName={userName}
        userEmail={userEmail}
        isAuthenticated={isAuthenticated}
        onSignOut={onSignOut}
        onSettings={onSettings}
        themePreference={themePreference}
        onThemeChange={onThemeChange}
        compact={isCompact}
      />

      {/* Navigation (only show if authenticated and enabled) */}
      {showNavigation && isAuthenticated && (
        <TaskPaneNavigation
          selectedTab={selectedTab}
          onTabChange={handleTabChange}
          hostType={hostType}
          compact={isCompact}
        />
      )}

      {/* Main Content with Error Boundary */}
      <main className={contentClassName}>
        <ErrorBoundary
          onError={onError}
          showDetails={showErrorDetails}
          onReset={() => {
            // Optionally navigate back to default tab on error reset
            handleTabChange(getDefaultTab(hostType));
          }}
        >
          {children}
        </ErrorBoundary>
      </main>

      {/* Footer */}
      <TaskPaneFooter
        version={version}
        buildDate={buildDate}
        appName={appName}
        connectionStatus={connectionStatus}
        showHelpLink={true}
        compact={isCompact}
      />
    </div>
  );
};

// Re-export types for convenience
export type { HostType } from './TaskPaneHeader';
export type { NavigationTab } from './TaskPaneNavigation';
export type { ConnectionStatus } from './TaskPaneFooter';
