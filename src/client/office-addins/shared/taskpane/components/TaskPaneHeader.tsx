import React from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Tooltip,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  MenuDivider,
  Badge,
} from '@fluentui/react-components';
import {
  PersonRegular,
  SignOutRegular,
  SettingsRegular,
  WeatherMoonRegular,
  WeatherSunnyRegular,
  ColorRegular,
} from '@fluentui/react-icons';
import { SpaarkeLogo } from './SpaarkeLogo';
import type { ThemePreference } from '../hooks/useTheme';
import type { HostType } from '../../adapters/types';

/**
 * TaskPaneHeader - Header component for Office Add-in task pane.
 *
 * Features:
 * - App title with host-specific icon (Outlook/Word)
 * - User menu with profile and sign out
 * - Settings button
 * - Theme toggle
 *
 * Uses Fluent UI v9 design tokens per ADR-021.
 */

const useStyles = makeStyles({
  header: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalM}`,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground2,
    flexShrink: 0,
  },
  headerCompact: {
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalS}`,
  },
  titleSection: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    overflow: 'hidden',
  },
  title: {
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightRegular,
    color: tokens.colorNeutralForeground2,
  },
  actions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    flexShrink: 0,
  },
  userEmail: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    maxWidth: '150px',
  },
});

// Re-export HostType from adapters/types for backwards compatibility
export type { HostType };

export interface TaskPaneHeaderProps {
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
  /** Current theme preference */
  themePreference?: ThemePreference;
  /** Callback to change theme */
  onThemeChange?: (preference: ThemePreference) => void;
  /** Whether to use compact mode (narrow task pane) */
  compact?: boolean;
}


/**
 * Gets the icon for the current theme preference.
 */
function getThemeIcon(preference: ThemePreference): React.ReactElement {
  switch (preference) {
    case 'dark':
      return <WeatherMoonRegular />;
    case 'light':
      return <WeatherSunnyRegular />;
    default:
      return <ColorRegular />;
  }
}

export const TaskPaneHeader: React.FC<TaskPaneHeaderProps> = ({
  title = 'Spaarke',
  hostType = 'outlook',
  userName,
  userEmail,
  isAuthenticated = false,
  onSignOut,
  onSettings,
  themePreference = 'auto',
  onThemeChange,
  compact = false,
}) => {
  const styles = useStyles();

  const headerClassName = compact
    ? `${styles.header} ${styles.headerCompact}`
    : styles.header;

  return (
    <header className={headerClassName}>
      <div className={styles.titleSection}>
        <SpaarkeLogo size={24} />
        {!compact && <Text className={styles.title}>{title}</Text>}
      </div>

      <div className={styles.actions}>
        {/* Theme Toggle */}
        {onThemeChange && (
          <Menu>
            <MenuTrigger>
              <Tooltip content="Theme" relationship="label">
                <Button
                  appearance="subtle"
                  icon={getThemeIcon(themePreference)}
                  aria-label="Change theme"
                />
              </Tooltip>
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                <MenuItem
                  icon={<ColorRegular />}
                  onClick={() => onThemeChange('auto')}
                >
                  Auto
                  {themePreference === 'auto' && (
                    <Badge appearance="filled" size="small" style={{ marginLeft: '8px' }}>
                      Active
                    </Badge>
                  )}
                </MenuItem>
                <MenuItem
                  icon={<WeatherSunnyRegular />}
                  onClick={() => onThemeChange('light')}
                >
                  Light
                  {themePreference === 'light' && (
                    <Badge appearance="filled" size="small" style={{ marginLeft: '8px' }}>
                      Active
                    </Badge>
                  )}
                </MenuItem>
                <MenuItem
                  icon={<WeatherMoonRegular />}
                  onClick={() => onThemeChange('dark')}
                >
                  Dark
                  {themePreference === 'dark' && (
                    <Badge appearance="filled" size="small" style={{ marginLeft: '8px' }}>
                      Active
                    </Badge>
                  )}
                </MenuItem>
              </MenuList>
            </MenuPopover>
          </Menu>
        )}

        {/* Settings Button */}
        {onSettings && (
          <Tooltip content="Settings" relationship="label">
            <Button
              appearance="subtle"
              icon={<SettingsRegular />}
              onClick={onSettings}
              aria-label="Settings"
            />
          </Tooltip>
        )}

        {/* User Menu */}
        {isAuthenticated && (userName || userEmail) && (
          <Menu>
            <MenuTrigger>
              <Tooltip content={userName || userEmail || 'User'} relationship="label">
                <Button
                  appearance="subtle"
                  icon={<PersonRegular />}
                  aria-label={`Signed in as ${userName || userEmail}`}
                />
              </Tooltip>
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                {userName && (
                  <MenuItem disabled>
                    <strong>{userName}</strong>
                  </MenuItem>
                )}
                {userEmail && (
                  <MenuItem disabled>
                    <span className={styles.userEmail}>{userEmail}</span>
                  </MenuItem>
                )}
                {(userName || userEmail) && <MenuDivider />}
                {onSignOut && (
                  <MenuItem icon={<SignOutRegular />} onClick={onSignOut}>
                    Sign out
                  </MenuItem>
                )}
              </MenuList>
            </MenuPopover>
          </Menu>
        )}
      </div>
    </header>
  );
};
