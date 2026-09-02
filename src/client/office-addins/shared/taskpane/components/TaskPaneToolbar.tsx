import React from 'react';
import {
  makeStyles,
  tokens,
  TabList,
  Tab,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  MenuDivider,
  Button,
  Tooltip,
  Badge,
} from '@fluentui/react-components';
import {
  MoreVerticalRegular,
  PersonRegular,
  SignOutRegular,
  SettingsRegular,
  WeatherMoonRegular,
  WeatherSunnyRegular,
  ColorRegular,
} from '@fluentui/react-icons';
import { SpaarkeLogo } from './SpaarkeLogo';
import { getAvailableTabs, type NavigationTab } from './TaskPaneNavigation';
import type { HostType } from './TaskPaneHeader';
import type { ThemePreference } from '../hooks/useTheme';

/**
 * TaskPaneToolbar — the single Spaarke row beneath Microsoft's add-in chrome.
 *
 * Consolidates what used to be two stacked rows (logo/actions header + tab row) into
 * ONE toolbar (email-communication-intelligence-r2 UI feedback, owner 2026-09-02):
 *   [ logo ] [ Save ] [ Create To Do ] ……… [ ⋮  → Theme · Settings · Account ]
 *
 * Tabs are left-aligned; the per-user tools (theme/settings/account) collapse into a
 * three-dots overflow on the right. Fluent UI v9 only (ADR-021).
 */

const useStyles = makeStyles({
  toolbar: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: `0 ${tokens.spacingHorizontalS}`,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground2,
    flexShrink: 0,
    minHeight: '40px',
  },
  logo: {
    display: 'flex',
    alignItems: 'center',
    flexShrink: 0,
  },
  tabs: {
    flexGrow: 1,
    minWidth: 0,
  },
  overflow: {
    flexShrink: 0,
  },
  userEmail: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
});

export interface TaskPaneToolbarProps {
  hostType?: HostType;
  /** Whether to render the tab strip (hidden pre-auth). */
  showTabs?: boolean;
  selectedTab?: NavigationTab;
  onTabChange?: (tab: NavigationTab) => void;
  isAuthenticated?: boolean;
  userName?: string;
  userEmail?: string;
  onSignOut?: () => void;
  onSettings?: () => void;
  themePreference?: ThemePreference;
  onThemeChange?: (preference: ThemePreference) => void;
}

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

function activeBadge(isActive: boolean): React.ReactElement | null {
  return isActive ? (
    <Badge appearance="filled" size="small" style={{ marginLeft: '8px' }}>
      Active
    </Badge>
  ) : null;
}

export const TaskPaneToolbar: React.FC<TaskPaneToolbarProps> = ({
  hostType = 'outlook',
  showTabs = true,
  selectedTab,
  onTabChange,
  isAuthenticated = false,
  userName,
  userEmail,
  onSignOut,
  onSettings,
  themePreference = 'auto',
  onThemeChange,
}) => {
  const styles = useStyles();
  const tabs = getAvailableTabs(hostType);
  const hasOverflow = Boolean(onThemeChange || onSettings || (isAuthenticated && (userName || userEmail)));

  return (
    <header className={styles.toolbar} role="banner">
      <div className={styles.logo}>
        <SpaarkeLogo size={22} aria-label="Spaarke" />
      </div>

      {showTabs && isAuthenticated && tabs.length > 0 && (
        <div className={styles.tabs}>
          <TabList
            selectedValue={selectedTab}
            onTabSelect={(_, data) => onTabChange?.(data.value as NavigationTab)}
            size="small"
          >
            {tabs.map(tab => (
              <Tab key={tab.value} value={tab.value} icon={tab.icon}>
                {tab.label}
              </Tab>
            ))}
          </TabList>
        </div>
      )}

      {/* push the overflow to the right even when tabs are hidden */}
      {(!showTabs || !isAuthenticated || tabs.length === 0) && <div className={styles.tabs} />}

      {hasOverflow && (
        <div className={styles.overflow}>
          <Menu>
            <MenuTrigger disableButtonEnhancement>
              <Tooltip content="More" relationship="label">
                <Button appearance="subtle" icon={<MoreVerticalRegular />} aria-label="More options" />
              </Tooltip>
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                {onThemeChange && (
                  <Menu>
                    <MenuTrigger disableButtonEnhancement>
                      <MenuItem icon={getThemeIcon(themePreference)}>Theme</MenuItem>
                    </MenuTrigger>
                    <MenuPopover>
                      <MenuList>
                        <MenuItem icon={<ColorRegular />} onClick={() => onThemeChange('auto')}>
                          Auto
                          {activeBadge(themePreference === 'auto')}
                        </MenuItem>
                        <MenuItem icon={<WeatherSunnyRegular />} onClick={() => onThemeChange('light')}>
                          Light
                          {activeBadge(themePreference === 'light')}
                        </MenuItem>
                        <MenuItem icon={<WeatherMoonRegular />} onClick={() => onThemeChange('dark')}>
                          Dark
                          {activeBadge(themePreference === 'dark')}
                        </MenuItem>
                      </MenuList>
                    </MenuPopover>
                  </Menu>
                )}

                {onSettings && (
                  <MenuItem icon={<SettingsRegular />} onClick={onSettings}>
                    Settings
                  </MenuItem>
                )}

                {isAuthenticated && (userName || userEmail) && (
                  <>
                    <MenuDivider />
                    {userName && (
                      <MenuItem disabled icon={<PersonRegular />}>
                        <strong>{userName}</strong>
                      </MenuItem>
                    )}
                    {userEmail && (
                      <MenuItem disabled>
                        <span className={styles.userEmail}>{userEmail}</span>
                      </MenuItem>
                    )}
                    {onSignOut && (
                      <MenuItem icon={<SignOutRegular />} onClick={onSignOut}>
                        Sign out
                      </MenuItem>
                    )}
                  </>
                )}
              </MenuList>
            </MenuPopover>
          </Menu>
        </div>
      )}
    </header>
  );
};

export default TaskPaneToolbar;
