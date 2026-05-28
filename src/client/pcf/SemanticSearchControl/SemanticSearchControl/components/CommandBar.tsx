/**
 * CommandBar — horizontal filter + view-toggle bar for the Documents PCF
 * (FR-DOC-04 + FR-DOC-05 + FR-DOC-06).
 *
 * Hosts every interactive filter affordance previously rendered in the
 * sidebar `FilterPanel.tsx`, plus the new Tags filter (FR-DOC-05) and the
 * list/card view toggle (FR-DOC-04). The sidebar is removed by FR-DOC-06 —
 * this bar is the single source of filter interaction.
 *
 * Layout (left → right):
 *   Associated Only Switch | File Type | Date Range | Threshold | Mode | Tags | [spacer] | Tabs (List | Card)
 *
 * BINDING CONSTRAINT (spec FR-DOC-06):
 *   The AssociatedOnly auto-search behavior in `SemanticSearchControl.tsx`
 *   is preserved verbatim — this CommandBar only renders the visible trigger
 *   (Switch). The `useEffect` that listens for `filters.associatedOnly`
 *   changes is unchanged in the parent.
 *
 * Standards:
 *   - ADR-021  Fluent v9 semantic tokens only
 *   - ADR-022  React 16/17 — no React 18+ APIs
 *
 * @see spec.md FR-DOC-04 / FR-DOC-05 / FR-DOC-06
 */

import * as React from 'react';
import {
  Button,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItemCheckbox,
  MenuItemRadio,
  Popover,
  PopoverTrigger,
  PopoverSurface,
  Switch,
  TabList,
  Tab,
  makeStyles,
  shorthands,
  tokens,
  Text,
} from '@fluentui/react-components';
import { AppsList20Regular, Grid20Regular, ChevronDownRegular } from '@fluentui/react-icons';
// Deep-path import (NOT the barrel) — same React-16-via-Lexical-via-barrel issue
// documented in ResultCard.tsx. TagFilter ships as its own deep-path entry.
import { TagFilter } from '@spaarke/ui-components/dist/components/TagFilter';
import type { TagFilterOption } from '@spaarke/ui-components/dist/types/TagFilter';
import { DateRangeFilter } from './DateRangeFilter';
import type { DateRange, FilterOption, SearchFilters, SearchMode } from '../types';
import type { DocumentListView } from '../hooks/useDocumentListPrefs';

// ---------------------------------------------------------------------------
// Static option sets (matches FilterPanel.tsx — relocation, not redesign)
// ---------------------------------------------------------------------------

const THRESHOLD_OPTIONS: ReadonlyArray<{ key: string; label: string }> = [
  { key: '0', label: 'Off' },
  { key: '25', label: '25%' },
  { key: '50', label: '50%' },
  { key: '75', label: '75%' },
  { key: '100', label: '100%' },
];

const MODE_OPTIONS: ReadonlyArray<{ key: SearchMode; label: string }> = [
  { key: 'hybrid', label: 'Hybrid' },
  { key: 'vectorOnly', label: 'Concept Only' },
  { key: 'keywordOnly', label: 'Keyword Only' },
];

const THRESHOLD_GROUP = 'threshold';
const MODE_GROUP = 'mode';
const FILE_TYPE_GROUP = 'fileType';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalS,
    rowGap: tokens.spacingVerticalXS,
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderBottom(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke1),
    minHeight: '40px',
    boxSizing: 'border-box',
  },
  spacer: {
    flex: 1,
    minWidth: 0,
  },
  associatedSwitch: {
    // Keep label + switch inline; rely on Fluent default sizing.
  },
  filterButton: {
    // Subtle button with the filter label + selection summary.
  },
  menuList: {
    minWidth: '200px',
    maxWidth: '320px',
    ...shorthands.padding(tokens.spacingVerticalXS, 0),
  },
  viewToggle: {
    // TabList sized to fit two icon-only tabs.
  },
  // Popover surface for the Date Range picker — gives the inline-stacked
  // DateRangeFilter a roomy panel so the From/To inputs are usable.
  dateRangePopover: {
    minWidth: '260px',
    ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalM),
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ICommandBarProps {
  /** Current filter state (owned by parent via useFilters). */
  filters: SearchFilters;
  onFiltersChange: (next: SearchFilters) => void;

  /** Whether to show the Associated Only Switch (hidden for `all`/`custom` scopes). */
  showAssociatedOnly: boolean;

  /** Loaded option sets from Dataverse (memoized in parent). */
  fileTypeOptions: ReadonlyArray<FilterOption>;
  tagOptions: ReadonlyArray<TagFilterOption>;
  optionsLoading: boolean;

  /** Selected tags (FR-DOC-05) — owned by parent. */
  selectedTags: string[];
  onSelectedTagsChange: (next: string[]) => void;

  /** Active view (list | card) — owned by parent via useDocumentListPrefs. */
  view: DocumentListView;
  onViewChange: (next: DocumentListView) => void;

  /**
   * Whether to render the list/card view toggle group (v1.1.47).
   * When `false`, the trailing tab group is hidden and the view is treated
   * as locked at the parent level (the parent forces `view` to `defaultView`).
   * Defaults to `true` at the parent (back-compat with v1.1.46 behavior).
   */
  showViewToggle?: boolean;

  /** Disable all interactive controls (during loading). */
  disabled?: boolean;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function formatFileTypeLabel(selected: string[], options: ReadonlyArray<FilterOption>): string {
  if (selected.length === 0) return 'File Type';
  if (selected.length === 1) {
    const found = options.find(o => o.key === selected[0]);
    return `File Type: ${found?.label ?? selected[0]}`;
  }
  return `File Type (${selected.length})`;
}

function formatThresholdLabel(threshold: number): string {
  const opt = THRESHOLD_OPTIONS.find(o => o.key === String(threshold));
  return `Threshold: ${opt?.label ?? `${threshold}%`}`;
}

function formatModeLabel(mode: SearchMode): string {
  const opt = MODE_OPTIONS.find(o => o.key === mode);
  return `Mode: ${opt?.label ?? mode}`;
}

function formatDateRangeLabel(range: DateRange | null): string {
  if (!range || (!range.from && !range.to)) return 'Date Range';
  return 'Date Range (active)';
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const CommandBar: React.FC<ICommandBarProps> = ({
  filters,
  onFiltersChange,
  showAssociatedOnly,
  fileTypeOptions,
  tagOptions,
  optionsLoading,
  selectedTags,
  onSelectedTagsChange,
  view,
  onViewChange,
  showViewToggle = true,
  disabled,
}) => {
  const styles = useStyles();

  // ── Associated Only ────────────────────────────────────────────────────
  // BINDING (spec FR-DOC-06): the auto-search useEffect at the parent must
  // observe `filters.associatedOnly` mutations. We MUST therefore route
  // through `onFiltersChange` — never via a separate handler — so the
  // parent's existing ref + effect logic fires unchanged.
  const handleAssociatedOnlyChange = React.useCallback(
    (ev: React.ChangeEvent<HTMLInputElement>) => {
      onFiltersChange({ ...filters, associatedOnly: ev.currentTarget.checked });
    },
    [filters, onFiltersChange]
  );

  // ── File Type (multi-select) ───────────────────────────────────────────
  const fileTypeCheckedValues = React.useMemo(
    () => ({ [FILE_TYPE_GROUP]: filters.fileTypes }),
    [filters.fileTypes]
  );

  const handleFileTypeCheckedChange = React.useCallback(
    (_ev: unknown, data: { name: string; checkedItems: string[] }) => {
      if (data.name === FILE_TYPE_GROUP) {
        onFiltersChange({ ...filters, fileTypes: data.checkedItems });
      }
    },
    [filters, onFiltersChange]
  );

  // ── Threshold (single-select) ─────────────────────────────────────────
  const thresholdCheckedValues = React.useMemo(
    () => ({ [THRESHOLD_GROUP]: [String(filters.threshold)] }),
    [filters.threshold]
  );

  const handleThresholdChange = React.useCallback(
    (_ev: unknown, data: { name: string; checkedItems: string[] }) => {
      if (data.name === THRESHOLD_GROUP && data.checkedItems.length > 0) {
        const next = parseInt(data.checkedItems[0], 10);
        onFiltersChange({ ...filters, threshold: isNaN(next) ? 0 : next });
      }
    },
    [filters, onFiltersChange]
  );

  // ── Mode (single-select) ───────────────────────────────────────────────
  const modeCheckedValues = React.useMemo(
    () => ({ [MODE_GROUP]: [filters.searchMode] }),
    [filters.searchMode]
  );

  const handleModeChange = React.useCallback(
    (_ev: unknown, data: { name: string; checkedItems: string[] }) => {
      if (data.name === MODE_GROUP && data.checkedItems.length > 0) {
        onFiltersChange({ ...filters, searchMode: data.checkedItems[0] as SearchMode });
      }
    },
    [filters, onFiltersChange]
  );

  // ── Date Range ────────────────────────────────────────────────────────
  const handleDateRangeChange = React.useCallback(
    (range: DateRange | null) => {
      onFiltersChange({ ...filters, dateRange: range });
    },
    [filters, onFiltersChange]
  );

  // ── View toggle ───────────────────────────────────────────────────────
  const handleViewTabSelect = React.useCallback(
    (_ev: unknown, data: { value: unknown }) => {
      if (data.value === 'list' || data.value === 'card') {
        onViewChange(data.value);
      }
    },
    [onViewChange]
  );

  return (
    <div className={styles.root} role="toolbar" aria-label="Document filters and view">
      {/* Associated Only — only shown for entity-scoped surfaces. The auto-search
          ref/effect in SemanticSearchControl.tsx observes mutations to filters.associatedOnly
          and re-fires the search on its own. This component must not call search() directly. */}
      {showAssociatedOnly && (
        <Switch
          className={styles.associatedSwitch}
          label="Associated Only"
          checked={filters.associatedOnly ?? false}
          onChange={handleAssociatedOnlyChange}
          disabled={disabled}
        />
      )}

      {/* File Type — multi-select via MenuItemCheckbox */}
      <Menu
        checkedValues={fileTypeCheckedValues}
        onCheckedValueChange={handleFileTypeCheckedChange}
      >
        <MenuTrigger disableButtonEnhancement>
          <Button
            className={styles.filterButton}
            appearance="subtle"
            iconPosition="after"
            icon={<ChevronDownRegular aria-hidden="true" />}
            disabled={disabled || optionsLoading}
            aria-label="File Type filter"
          >
            {formatFileTypeLabel(filters.fileTypes, fileTypeOptions)}
          </Button>
        </MenuTrigger>
        <MenuPopover>
          <MenuList className={styles.menuList}>
            {fileTypeOptions.map(opt => (
              <MenuItemCheckbox key={opt.key} name={FILE_TYPE_GROUP} value={opt.key}>
                {opt.label}
              </MenuItemCheckbox>
            ))}
          </MenuList>
        </MenuPopover>
      </Menu>

      {/* Date Range — wrapped in a Popover so the inline-stacked picker doesn't
          break command-bar horizontal layout. The trigger button summarises
          current state (label flips to "Date Range (active)" when set). */}
      <Popover>
        <PopoverTrigger disableButtonEnhancement>
          <Button
            className={styles.filterButton}
            appearance="subtle"
            iconPosition="after"
            icon={<ChevronDownRegular aria-hidden="true" />}
            disabled={disabled}
            aria-label="Date Range filter"
          >
            {formatDateRangeLabel(filters.dateRange)}
          </Button>
        </PopoverTrigger>
        <PopoverSurface className={styles.dateRangePopover}>
          <DateRangeFilter
            label="Quick select"
            value={filters.dateRange}
            onChange={handleDateRangeChange}
            disabled={!!disabled}
          />
        </PopoverSurface>
      </Popover>

      {/* Threshold — single-select via MenuItemRadio (5 preset values) */}
      <Menu
        checkedValues={thresholdCheckedValues}
        onCheckedValueChange={handleThresholdChange}
      >
        <MenuTrigger disableButtonEnhancement>
          <Button
            className={styles.filterButton}
            appearance="subtle"
            iconPosition="after"
            icon={<ChevronDownRegular aria-hidden="true" />}
            disabled={disabled}
            aria-label="Threshold filter"
          >
            {formatThresholdLabel(filters.threshold)}
          </Button>
        </MenuTrigger>
        <MenuPopover>
          <MenuList className={styles.menuList}>
            {THRESHOLD_OPTIONS.map(opt => (
              <MenuItemRadio key={opt.key} name={THRESHOLD_GROUP} value={opt.key}>
                {opt.label}
              </MenuItemRadio>
            ))}
          </MenuList>
        </MenuPopover>
      </Menu>

      {/* Mode — single-select (hybrid | vectorOnly | keywordOnly) */}
      <Menu
        checkedValues={modeCheckedValues}
        onCheckedValueChange={handleModeChange}
      >
        <MenuTrigger disableButtonEnhancement>
          <Button
            className={styles.filterButton}
            appearance="subtle"
            iconPosition="after"
            icon={<ChevronDownRegular aria-hidden="true" />}
            disabled={disabled}
            aria-label="Search mode filter"
          >
            {formatModeLabel(filters.searchMode)}
          </Button>
        </MenuTrigger>
        <MenuPopover>
          <MenuList className={styles.menuList}>
            {MODE_OPTIONS.map(opt => (
              <MenuItemRadio key={opt.key} name={MODE_GROUP} value={opt.key}>
                {opt.label}
              </MenuItemRadio>
            ))}
          </MenuList>
        </MenuPopover>
      </Menu>

      {/* Tags — consumes shared TagFilter (FR-SC-01) sourcing sprk_documenttype options
          via the parent's memoized DataverseMetadataService.fetchOptionSet. OR-semantics
          multi-select is applied in the parent's filteredResults memo. */}
      <TagFilter
        options={tagOptions as TagFilterOption[]}
        selected={selectedTags}
        onChange={onSelectedTagsChange}
        label="Tags"
        sortAlphabetical
      />

      <div className={styles.spacer} aria-hidden="true" />

      {/* View toggle — list (AppsList20Regular) / card (Grid20Regular).
          v1.1.47: hidden when `showViewToggle === false` so a host form
          can lock the surface to a single view (the parent simultaneously
          forces `view` to `defaultView` so this also enforces the lock). */}
      {showViewToggle && (
        <TabList
          className={styles.viewToggle}
          selectedValue={view}
          onTabSelect={handleViewTabSelect}
          appearance="subtle"
          size="small"
          aria-label="View toggle"
        >
          <Tab value="list" icon={<AppsList20Regular />} aria-label="List view">
            <Text size={100}>List</Text>
          </Tab>
          <Tab value="card" icon={<Grid20Regular />} aria-label="Card view">
            <Text size={100}>Card</Text>
          </Tab>
        </TabList>
      )}
    </div>
  );
};

export default CommandBar;
