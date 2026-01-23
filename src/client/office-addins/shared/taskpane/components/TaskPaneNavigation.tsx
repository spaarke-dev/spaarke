import React from 'react';
import {
  makeStyles,
  tokens,
  TabList,
  Tab,
} from '@fluentui/react-components';
import {
  SaveRegular,
  // V1: Disabled icons - uncomment for future releases
  // ShareRegular,
  // ClockRegular,
  // DocumentSearchRegular,
} from '@fluentui/react-icons';
import type { HostType } from './TaskPaneHeader';

/**
 * TaskPaneNavigation - Tab navigation for Office Add-in task pane.
 *
 * Provides different navigation options based on host type:
 * - Outlook: Save (emails/attachments), Share (insert links), Recent (status)
 * - Word: Save (document), Share (insert links), Recent (status)
 *
 * Uses Fluent UI v9 TabList per ADR-021.
 */

const useStyles = makeStyles({
  navigation: {
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground1,
    flexShrink: 0,
  },
  tabList: {
    display: 'flex',
    justifyContent: 'center',
    paddingTop: tokens.spacingVerticalXS,
  },
  tabListCompact: {
    paddingTop: 0,
  },
});

/**
 * Available navigation tabs.
 */
export type NavigationTab = 'save' | 'share' | 'recent' | 'search';

/**
 * Tab configuration.
 */
interface TabConfig {
  value: NavigationTab;
  label: string;
  icon: React.ReactElement;
  /** Whether this tab is available for the host type */
  availableFor: HostType[];
}

/**
 * All available tabs with their configuration.
 * V1: Only Save tab is enabled. Share, Search, Recent are for future releases.
 */
const TAB_CONFIGS: TabConfig[] = [
  {
    value: 'save',
    label: 'Save',
    icon: <SaveRegular />,
    availableFor: ['outlook', 'word'],
  },
  // V1: Disabled - uncomment for future releases
  // {
  //   value: 'share',
  //   label: 'Share',
  //   icon: <ShareRegular />,
  //   availableFor: ['outlook', 'word'],
  // },
  // {
  //   value: 'search',
  //   label: 'Search',
  //   icon: <DocumentSearchRegular />,
  //   availableFor: ['outlook', 'word'],
  // },
  // {
  //   value: 'recent',
  //   label: 'Recent',
  //   icon: <ClockRegular />,
  //   availableFor: ['outlook', 'word'],
  // },
];

export interface TaskPaneNavigationProps {
  /** Currently selected tab */
  selectedTab: NavigationTab;
  /** Callback when tab changes */
  onTabChange: (tab: NavigationTab) => void;
  /** Type of Office host (affects available tabs) */
  hostType?: HostType;
  /** Whether to use compact mode (icon-only tabs) */
  compact?: boolean;
  /** Whether navigation is disabled */
  disabled?: boolean;
}

export const TaskPaneNavigation: React.FC<TaskPaneNavigationProps> = ({
  selectedTab,
  onTabChange,
  hostType = 'outlook',
  compact = false,
  disabled = false,
}) => {
  const styles = useStyles();

  // Filter tabs based on host type
  const availableTabs = TAB_CONFIGS.filter((tab) =>
    tab.availableFor.includes(hostType)
  );

  const tabListClassName = compact
    ? `${styles.tabList} ${styles.tabListCompact}`
    : styles.tabList;

  return (
    <nav className={styles.navigation} aria-label="Task pane navigation">
      <TabList
        className={tabListClassName}
        selectedValue={selectedTab}
        onTabSelect={(_, data) => onTabChange(data.value as NavigationTab)}
        disabled={disabled}
        size={compact ? 'small' : 'medium'}
      >
        {availableTabs.map((tab) => (
          <Tab
            key={tab.value}
            value={tab.value}
            icon={tab.icon}
            aria-label={tab.label}
          >
            {!compact && tab.label}
          </Tab>
        ))}
      </TabList>
    </nav>
  );
};

/**
 * Gets the default tab for a host type.
 */
export function getDefaultTab(hostType: HostType): NavigationTab {
  return 'save';
}
