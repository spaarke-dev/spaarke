import React, { useRef } from 'react';
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
  Spinner,
} from '@fluentui/react-components';
import {
  MoreVerticalRegular,
  PersonRegular,
  SignOutRegular,
  SettingsRegular,
  WeatherMoonRegular,
  WeatherSunnyRegular,
  ColorRegular,
  PanelRightExpandRegular,
  PanelRightContractRegular,
  MailRegular,
  BugRegular,
} from '@fluentui/react-icons';
import { getAvailableTabs, type NavigationTab, type TabCapabilities } from './TaskPaneNavigation';
import type { HostType } from './TaskPaneHeader';
import type { ThemePreference } from '../hooks/useTheme';
import { useAnnounce } from '../hooks/useAnnounce';
import { useToolbarFit } from '../hooks/useToolbarFit';

/**
 * TaskPaneToolbar — the single Spaarke row beneath Microsoft's add-in chrome.
 *
 * Consolidates what used to be two stacked rows (logo/actions header + tab row) into
 * ONE toolbar (email-communication-intelligence-r2 UI feedback, owner 2026-09-02):
 *   [ Save ] [ To Do ] [ Find ] [ Send ] ……… [ Expand ] [ ⋮  → Theme · Settings · Account ]
 *
 * "Send" is Word's Email TAB (task 096) or, in Outlook, the Send Email ACTION button (task 106, owner UAT
 * round 8: "should be in the tool bar next to Find") — it opens Outlook's native compose, so it is not a tab.
 *
 * Tabs are left-aligned; the per-user tools (theme/settings/account) collapse into a
 * three-dots overflow on the right. Fluent UI v9 only (ADR-021).
 *
 * Task 108 (owner UAT round 10): one rule for both hosts — tabs are icon-only at a normal pane width and show labels
 * once the pane is wide/expanded (`useToolbarFit`); icon-only tabs keep their names as tooltip + accessible name.
 * Expand/Collapse (pane icons) is always visible, immediately left of "⋮" (task 109, owner UAT round 11): the title
 * row above belongs to Word/Outlook and an add-in cannot place buttons in it (Outlook's own icon there is Outlook's).
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
    // Task 106: the Send action sits directly after the last tab, not pushed to the right.
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
  },
  // Task 091 (UAT-4): visible spacing between Save / To Do / Find — the owner's round-3 UAT found them
  // crowded together. `TabList` is itself a flex row (confirmed by `TaskPaneNavigation.tsx`'s own
  // `className` override of its `justifyContent`), so an additional `gap` here is additive, not a
  // replacement of its internal layout. A modest gap (not `spacingHorizontalL`+) so three tabs plus the
  // overflow menu still fit the pane's documented 320px minimum width without wrapping or clipping.
  tabListGap: {
    gap: tokens.spacingHorizontalM,
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
  /** Task 096: host capabilities for capability-gated tabs (the Word-only Email tab). */
  capabilities?: Partial<TabCapabilities>;
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
  /**
   * Task 103: expand/collapse the pane. Supplied only when the host supports runtime pane resizing
   * (TaskPaneApi 1.1, NFR-10) - when absent, no button is rendered.
   */
  onToggleExpand?: () => void;
  isExpanded?: boolean;
  /** True while an expand request is stepping down; the button is disabled so presses do not stack. */
  isResizing?: boolean;
  /**
   * Task 106: Outlook's Send Email action (opens native compose). Supplied only when there is something to
   * link (task 036 gating) - when absent, no button is rendered (never rendered-and-disabled).
   */
  onSendEmail?: () => void;
  /** True while Send Email is opening the compose window; the button shows a spinner and is disabled. */
  isSendingEmail?: boolean;
  /** Task 113: opens the dev-only sign-in Diagnostics view. Supplied only by a diagnostics-enabled build. */
  onShowDiagnostics?: () => void;
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
  capabilities,
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
  onToggleExpand,
  isExpanded = false,
  isResizing = false,
  onSendEmail,
  isSendingEmail = false,
  onShowDiagnostics,
}) => {
  const styles = useStyles();
  const tabs = getAvailableTabs(hostType, capabilities);

  // Task 108: labels only when the pane is wide (see useToolbarFit).
  const headerRef = useRef<HTMLElement>(null);
  const tabsRef = useRef<HTMLDivElement>(null);
  const { showLabels } = useToolbarFit(
    headerRef,
    tabsRef,
    `${tabs.map(t => t.value).join(',')}|${Boolean(onSendEmail)}`
  );
  const iconOnly = !showLabels;
  const expandLabel = isExpanded ? 'Collapse pane' : 'Expand pane';
  const expandIcon = isExpanded ? <PanelRightContractRegular /> : <PanelRightExpandRegular />;

  const hasOverflow = Boolean(
    onThemeChange || onSettings || onShowDiagnostics || (isAuthenticated && (userName || userEmail))
  );

  // NFR-11: announce tab changes to screen readers via the React-owned live region
  // (task 018 pattern) — `liveRegion` must be rendered here, not created out-of-tree.
  const { announce, liveRegion } = useAnnounce();

  return (
    <header ref={headerRef} className={styles.toolbar} role="banner">
      {liveRegion}
      {showTabs && isAuthenticated && tabs.length > 0 && (
        <div ref={tabsRef} className={styles.tabs}>
          <TabList
            className={styles.tabListGap}
            selectedValue={selectedTab}
            onTabSelect={(_, data) => {
              const tab = data.value as NavigationTab;
              onTabChange?.(tab);
              const label = tabs.find(t => t.value === tab)?.label ?? tab;
              announce(`${label} tab selected`);
            }}
            size="small"
          >
            {tabs.map(tab =>
              iconOnly ? (
                <Tooltip key={tab.value} content={tab.label} relationship="label">
                  <Tab value={tab.value} icon={tab.icon} aria-label={tab.label} />
                </Tooltip>
              ) : (
                <Tab key={tab.value} value={tab.value} icon={tab.icon}>
                  {tab.label}
                </Tab>
              )
            )}
          </TabList>
          {onSendEmail && (
            <Tooltip content="Email the document and record links" relationship="description">
              <Button
                appearance="subtle"
                size="small"
                icon={isSendingEmail ? <Spinner size="tiny" /> : <MailRegular />}
                disabled={isSendingEmail}
                onClick={onSendEmail}
                {...(iconOnly ? { 'aria-label': 'Send' } : {})}
              >
                {iconOnly ? null : 'Send'}
              </Button>
            </Tooltip>
          )}
        </div>
      )}

      {/* push the overflow to the right even when tabs are hidden */}
      {(!showTabs || !isAuthenticated || tabs.length === 0) && <div className={styles.tabs} />}

      {/* Task 109 (owner UAT round 11): Expand/Collapse sits immediately left of the "⋮" menu. */}
      {onToggleExpand && (
        <div className={styles.overflow}>
          <Tooltip content={expandLabel} relationship="label">
            <Button
              appearance="subtle"
              icon={expandIcon}
              aria-label={expandLabel}
              aria-pressed={isExpanded}
              disabled={isResizing}
              onClick={onToggleExpand}
            />
          </Tooltip>
        </div>
      )}

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

                {onShowDiagnostics && (
                  <MenuItem icon={<BugRegular />} onClick={onShowDiagnostics}>
                    Diagnostics
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
