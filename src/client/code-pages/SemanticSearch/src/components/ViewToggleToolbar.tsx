/**
 * ViewToggleToolbar — 4-way view mode toggle
 *
 * Layout: spacer + Grid | Map | Treemap | Timeline buttons (right-aligned).
 * Search pane collapse is handled internally by SearchFilterPane.
 *
 * @see spec.md Section 6.1 — toolbar layout
 */

import React, { useCallback } from 'react';
import {
  makeStyles,
  tokens,
  TabList,
  Tab,
  Tooltip,
  type SelectTabData,
  type SelectTabEvent,
} from '@fluentui/react-components';
import {
  TextBulletListSquareRegular,
  DataScatterRegular,
  DataTreemapRegular,
  TimelineRegular,
} from '@fluentui/react-icons';
import type { ViewMode } from '../types';

// =============================================
// Props
// =============================================

export interface ViewToggleToolbarProps {
  /** Current view mode. */
  viewMode: ViewMode;
  /** Callback when view mode changes. */
  onViewModeChange: (mode: ViewMode) => void;
}

// =============================================
// View button configuration
// =============================================

interface ViewButtonConfig {
  mode: ViewMode;
  label: string;
  icon: React.ReactElement;
  ariaLabel: string;
}

const VIEW_BUTTONS: ViewButtonConfig[] = [
  {
    mode: 'grid',
    label: 'Grid',
    icon: <TextBulletListSquareRegular />,
    ariaLabel: 'Switch to grid view',
  },
  {
    mode: 'map',
    label: 'Network',
    icon: <DataScatterRegular />,
    ariaLabel: 'Switch to network graph view',
  },
  {
    mode: 'treemap',
    label: 'Treemap',
    icon: <DataTreemapRegular />,
    ariaLabel: 'Switch to treemap view',
  },
  {
    mode: 'timeline',
    label: 'Timeline',
    icon: <TimelineRegular />,
    ariaLabel: 'Switch to timeline view',
  },
];

// =============================================
// Styles
// =============================================

const useStyles = makeStyles({
  // TabList without wrapper / spacer — the parent (SearchCommandBar) handles
  // layout. Task 035 UI alignment: ToggleButtons -> TabList for Power Apps
  // OOB visual parity (bottom-border underline on active tab).
  tabList: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXXS,
  },
});

// =============================================
// Component
// =============================================

export const ViewToggleToolbar: React.FC<ViewToggleToolbarProps> = ({ viewMode, onViewModeChange }) => {
  const styles = useStyles();

  const handleSelect = useCallback(
    (_ev: SelectTabEvent, data: SelectTabData) => {
      onViewModeChange(data.value as ViewMode);
    },
    [onViewModeChange]
  );

  return (
    <TabList
      className={styles.tabList}
      selectedValue={viewMode}
      onTabSelect={handleSelect}
      size="small"
      appearance="transparent"
    >
      {/* Icon-only tabs per operator directive 2026-06-04. Tooltip on each
          tab surfaces the label for accessibility + discoverability. */}
      {VIEW_BUTTONS.map(btn => (
        <Tooltip key={btn.mode} content={btn.label} relationship="label">
          <Tab value={btn.mode} icon={btn.icon} aria-label={btn.ariaLabel} />
        </Tooltip>
      ))}
    </TabList>
  );
};

export default ViewToggleToolbar;
