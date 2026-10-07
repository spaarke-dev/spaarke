import React from 'react';
import { makeStyles, mergeClasses, tokens, TabList, Tab } from '@fluentui/react-components';
import {
  SaveRegular,
  SearchRegular,
  MailRegular,
  // V1: Disabled icons - uncomment for future releases
  // ShareRegular,
  // ClockRegular,
  // DocumentSearchRegular,
} from '@fluentui/react-icons';
import type { HostType } from './TaskPaneHeader';
import type { HostCapabilities } from '@shared/adapters';
import { MicrosoftToDoIcon } from './icons/MicrosoftToDoIcon';

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
 * r1 tabs (spec.md FR-03/FR-14): Save, Find and Create To Do are all available in both
 * Outlook and Word (task 049 made Create To Do parity-correct; see the `TAB_CONFIGS`
 * entry below). Share / Search / Recent remain modeled in `NavigationTab` but stay
 * commented out of `TAB_CONFIGS` — hidden, unbuilt, r1 placeholders. Note `search`
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
export type NavigationTab = 'save' | 'createTodo' | 'find' | 'email' | 'share' | 'recent' | 'search';

/**
 * The host capabilities the tab table can be gated on (task 096). A subset of `HostCapabilities`, so callers
 * pass `hostAdapter.getCapabilities()` straight through.
 */
export type TabCapabilities = Pick<HostCapabilities, 'canEmailFromPane'>;

/**
 * Tab configuration.
 */
export interface TabConfig {
  value: NavigationTab;
  label: string;
  icon: React.ReactElement;
  /** Whether this tab is available for the host type */
  availableFor: HostType[];
  /**
   * Task 096: a capability the host must ALSO report for the tab to show (NFR-10 — a capability, never a
   * `hostType` branch). Absent → the tab depends on `availableFor` alone, as every tab did before.
   */
  requiresCapability?: keyof TabCapabilities;
}

/**
 * All available tabs with their configuration.
 * r1 (FR-03/FR-14): Save, Find and Create To Do are all enabled in both hosts (task 049).
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
    // Inline "Create To Do" tool — a SHARED capability (task 049 / FR-14 / FR-19), available in both
    // hosts. `CreateTodoView` contains zero host-type logic and `OfficeService.CreateTodoAsync` (task
    // 035) writes a document-carrier block for Word exactly parallel to the communication-carrier
    // block for Outlook — see `App.tsx`'s `handleCreateTodo` / `todoRegardingContext` and
    // `notes/035-todo-regarding-decision.md`. Spec's Assumptions list "To Do" under the
    // Outlook-PARITY (both-hosts) set, not the Outlook-only set, and FR-14's acceptance names Word
    // explicitly ("both Word (document + record) and Outlook (communication + record)").
    //
    // History: this was `availableFor: ['outlook']` from task 015 through task 040. Task 040's parity
    // audit (`notes/parity-checklist.md` §2) flagged it as a stale gate but could not fix it without
    // also changing `TaskPaneNavigation.test.tsx`'s "renders only the Save tab for Word" test, which
    // its own hard rule against weakening a test blocked. Task 049 changes the gate and the test
    // together (that test now asserts Save + Find + Create To Do for Word, renamed accordingly).
    //
    // Task 091 (UAT-2, owner 2026-10-03): the tab's own value/underlying capability is unchanged
    // (`createTodo`) — only the LABEL ("To Do", was "Create To Do") and ICON (the Microsoft To Do blue
    // check, `active` so it renders brand blue regardless of selection state — matches the Smart To Do
    // surfaces' own fixed usage, e.g. `KanbanHeader.tsx:169`) changed, per the owner's UAT feedback.
    value: 'createTodo',
    label: 'To Do',
    icon: <MicrosoftToDoIcon active />,
    availableFor: ['outlook', 'word'],
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
  {
    // Email tab (task 096, owner UAT round 4 item 6: "Add the send email to the main add-in bar next to
    // 'Find'"): an in-pane form, built from the shared Spaarke compose engine, that emails the open document as
    // an attachment from the user's own mailbox. Word only by the owner's decision ("Outlook unchanged") —
    // expressed as the `canEmailFromPane` CAPABILITY (Word true, Outlook false), never a `hostType` list.
    // Task 106 (owner UAT round 8): labelled "Send" - the mail icon already says it is email - matching
    // Outlook's Send action in the same toolbar position.
    value: 'email',
    label: 'Send',
    icon: <MailRegular />,
    availableFor: ['outlook', 'word'],
    requiresCapability: 'canEmailFromPane',
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

/**
 * Tabs available for a given host — shared by the nav row and the consolidated toolbar.
 *
 * task 040 / FR-19 audit: `TAB_CONFIGS[].availableFor: HostType[]` is a single declarative table
 * (not conditionals scattered through views), the sanctioned task-015 mechanism for tab-shell
 * routing — classified as legitimate, NOT converted to `hostAdapter.getCapabilities()` (which would
 * mean inventing a same-purpose boolean per tab). See notes/parity-checklist.md.
 */
export function getAvailableTabs(hostType: HostType, capabilities?: Partial<TabCapabilities>): TabConfig[] {
  return TAB_CONFIGS.filter(
    tab =>
      tab.availableFor.includes(hostType) &&
      // A capability-gated tab shows only when the host reports the capability; without capabilities, it
      // never shows (fail closed — a tab that cannot work is not offered).
      (!tab.requiresCapability || capabilities?.[tab.requiresCapability] === true)
  );
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
  /** Task 096: host capabilities for capability-gated tabs (see `getAvailableTabs`). */
  capabilities?: Partial<TabCapabilities>;
}

export const TaskPaneNavigation: React.FC<TaskPaneNavigationProps> = ({
  selectedTab,
  onTabChange,
  hostType = 'outlook',
  compact = false,
  disabled = false,
  capabilities,
}) => {
  const styles = useStyles();

  // Filter tabs based on host type and (task 096) host capabilities — the same rule the live toolbar uses.
  const availableTabs = getAvailableTabs(hostType, capabilities);

  const tabListClassName = compact ? mergeClasses(styles.tabList, styles.tabListCompact) : styles.tabList;

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
