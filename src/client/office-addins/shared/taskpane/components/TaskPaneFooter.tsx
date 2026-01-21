import React from 'react';
import { makeStyles, tokens, Link } from '@fluentui/react-components';

/**
 * TaskPaneFooter - Footer component for Office Add-in task pane.
 *
 * Displays version information and optional status.
 * Uses Fluent UI v9 design tokens per ADR-021.
 */

const useStyles = makeStyles({
  footer: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground2,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    flexShrink: 0,
  },
  footerCompact: {
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalS}`,
    fontSize: tokens.fontSizeBase100,
  },
  left: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  right: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  link: {
    fontSize: 'inherit',
  },
  status: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
  },
  statusIndicator: {
    width: '8px',
    height: '8px',
    borderRadius: '50%',
    backgroundColor: tokens.colorPaletteGreenBorder1,
  },
  statusIndicatorError: {
    backgroundColor: tokens.colorPaletteRedBorder1,
  },
  statusIndicatorWarning: {
    backgroundColor: tokens.colorPaletteYellowBorder1,
  },
});

export type ConnectionStatus = 'connected' | 'disconnected' | 'connecting';

export interface TaskPaneFooterProps {
  /** Application version string */
  version?: string;
  /** Application name */
  appName?: string;
  /** Connection status to show */
  connectionStatus?: ConnectionStatus;
  /** Whether to show help link */
  showHelpLink?: boolean;
  /** Help URL */
  helpUrl?: string;
  /** Whether to use compact mode */
  compact?: boolean;
}

/**
 * Gets the status indicator class based on connection status.
 */
function getStatusClass(
  status: ConnectionStatus,
  styles: ReturnType<typeof useStyles>
): string {
  switch (status) {
    case 'connected':
      return styles.statusIndicator;
    case 'disconnected':
      return `${styles.statusIndicator} ${styles.statusIndicatorError}`;
    case 'connecting':
      return `${styles.statusIndicator} ${styles.statusIndicatorWarning}`;
    default:
      return styles.statusIndicator;
  }
}

/**
 * Gets the status text based on connection status.
 */
function getStatusText(status: ConnectionStatus): string {
  switch (status) {
    case 'connected':
      return 'Connected';
    case 'disconnected':
      return 'Disconnected';
    case 'connecting':
      return 'Connecting...';
    default:
      return '';
  }
}

export const TaskPaneFooter: React.FC<TaskPaneFooterProps> = ({
  version = '1.0.0',
  appName = 'Spaarke DMS',
  connectionStatus,
  showHelpLink = false,
  helpUrl = 'https://help.spaarke.com',
  compact = false,
}) => {
  const styles = useStyles();

  const footerClassName = compact
    ? `${styles.footer} ${styles.footerCompact}`
    : styles.footer;

  return (
    <footer className={footerClassName}>
      <div className={styles.left}>
        <span>{appName}</span>
        {connectionStatus && (
          <div className={styles.status}>
            <span
              className={getStatusClass(connectionStatus, styles)}
              aria-hidden="true"
            />
            {!compact && <span>{getStatusText(connectionStatus)}</span>}
          </div>
        )}
      </div>

      <div className={styles.right}>
        {showHelpLink && (
          <Link
            href={helpUrl}
            target="_blank"
            rel="noopener noreferrer"
            className={styles.link}
          >
            Help
          </Link>
        )}
        <span>v{version}</span>
      </div>
    </footer>
  );
};
