import React from 'react';
import { makeStyles, tokens, TabList, Tab } from '@fluentui/react-components';
import {
  SaveRegular,
  TaskListAddRegular,
  SearchRegular,
  // V1: Disabled icons - uncomment for future releases
  // ShareRegular,
  // ClockRegular,
  // DocumentSearchRegular,
} from '@fluentui/react-icons';
import type { HostType } from './TaskPaneHeader';

/**
 * TaskPaneNavigation — tab DATA + the (unmounted) tab-row component for the Office
 * Add-in task pane.
 *
 * ## Renderer decision (task 015 / FR-03 — see notes/015-tab-shell-decisions.md)
 *
 * `TaskPaneToolbar` is the ONE live tab-row renderer, mounted by `TaskPaneShell`. The
 * `TaskPaneNavigation` React component below is deliberately NOT mounted anywhere in
 * production — it is kept as a documented helper/test surface only (isolated coverage
 * of tab-list rendering: compact mode, disabled state, selection). Do not wire it into
 * the shell alongside the toolbar; that would reintroduce the two-renderer split this
 * task resolved. `getAvailableTabs` and `getDefaultTab` are the parts of this module
 * that ARE consumed live (by `TaskPaneToolbar` and `TaskPaneShell` respectively).
 *
 * r1 tabs (spec.md FR-03): Save + Find, available in both Outlook and Word. Create To
 * Do is Outlook-only. Share / Search / Recent remain modeled in `NavigationTab` but
 * stay commented out of `TAB_CONFIGS` — hidden, unbuilt, r1 placeholders. Note `search`
 * is NOT `find`: `search` is a pre-existing, still-hidden placeholder wired (in App.tsx)
 * to a job-status view, unrelated to the new Find frame this task adds.
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
 *
 * `find` is the r1 Find tab (FR-03 / task 015) — the frame only; the real view (three-state
 * gating, similarity results) is tasks 033-034. `search` is a distinct, still-hidden
 * legacy member wired to a job-status placeholder in App.tsx — do not conflate the two.
 */
export type NavigationTab = 'save' | 'createTodo' | 'find' | 'share' | 'recent' | 'search';

/**
 * Tab configuration.
 */
export interface TabConfig {
  value: NavigationTab;
  label: string;
  icon: React.ReactElement;
  /** Whether this tab is available for the host type */
  availableFor: HostType[];
}

/**
 * All available tabs with their configuration.
 * r1 (FR-03): Save + Find are enabled in both hosts; Create To Do is Outlook-only.
 * Share, Search, Recent stay commented out — hidden r1 placeholders (spec.md Assumptions).
 */
const TAB_CONFIGS: TabConfig[] = [
  {
    value: 'save',
    label: 'Save',
    icon: <SaveRegular />,
    availableFor: ['outlook', 'word'],
  },
  {
    // Inline "Create To Do" tool — Outlook only (a To Do is created from an email).
    value: 'createTodo',
    label: 'Create To Do',
    icon: <TaskListAddRegular />,
    availableFor: ['outlook'],
  },
  {
    // Find frame (task 015 / FR-03) — both hosts. The Find VIEW (similarity results,
    // per-row authorized by task 032) is tasks 033-034; this entry only makes the tab
    // selectable and routes to the placeholder mount point built in App.tsx.
    value: 'find',
    label: 'Find',
    icon: <SearchRegular />,
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

/** Tabs available for a given host — shared by the nav row and the consolidated toolbar. */
export function getAvailableTabs(hostType: HostType): TabConfig[] {
  return TAB_CONFIGS.filter(tab => tab.availableFor.includes(hostType));
}

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
  const availableTabs = TAB_CONFIGS.filter(tab => tab.availableFor.includes(hostType));

  const tabListClassName = compact ? `${styles.tabList} ${styles.tabListCompact}` : styles.tabList;

  return (
    <nav className={styles.navigation} aria-label="Task pane navigation">
      <TabList
        className={tabListClassName}
        selectedValue={selectedTab}
        onTabSelect={(_, data) => onTabChange(data.value as NavigationTab)}
        disabled={disabled}
        size={compact ? 'small' : 'medium'}
      >
        {availableTabs.map(tab => (
          <Tab key={tab.value} value={tab.value} icon={tab.icon} aria-label={tab.label}>
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
export function getDefaultTab(_hostType: HostType): NavigationTab {
  return 'save';
}
