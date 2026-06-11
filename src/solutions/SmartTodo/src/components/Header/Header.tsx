/**
 * SmartTodo Code Page Header — 4-row layout aligned with SemanticSearchControl.
 *
 * Renders the 4 stacked rows mandated by smart-todo-r4 spec FR-06:
 *
 *   Row 1 — Page title: "Smart To Do" (Fluent v9 <Text size={300}>)
 *   Row 2 — Search box + Refresh icon + "+ New" icon
 *   Row 3 — Filter bar: facets (Tag pills) + Clear button
 *   Row 4 — Selection-aware toolbar slot (renders null at zero selection)
 *
 * Visual reference: `src/client/pcf/SemanticSearchControl/SemanticSearchControl/
 * SemanticSearchControl.tsx` (titleRow → searchRow → emptyStateToolbar →
 * BulkActionBar) — this component matches that hierarchy + spacing + icon set.
 *
 * **Shared primitives consumed** (per ADR-012, R4 FR-10):
 *   - `<SelectionAwareToolbar>` from `@spaarke/ui-components` (R4 task 012)
 *
 * Deferred to later tasks:
 *   - `<ViewToggle>` — rendered by the surface that owns view-mode state
 *     (task 033 List/Card view toggle deliverable)
 *   - `<OrientationToggle>` — rendered by the kanban surface
 *     (task 070+ orientation deliverable)
 *
 * **Selection state**: this header is a controlled component. The parent
 * (`SmartTodoApp`) owns the `selectedIds: Set<string>` and the action handlers.
 * For task 030 the parent supplies stub action handlers that `console.log` —
 * task 032 will wire the real Open / Delete / Email / Pin behavior.
 *
 * **A11y (NFR-07 / WCAG 2.1 AA)**:
 *   - All buttons have `aria-label` (visible + invisible icon-only buttons)
 *   - `<SearchBox>` accepts a placeholder + ARIA description via Fluent v9
 *   - Focus rings come from Fluent v9 Button + SearchBox defaults
 *   - Keyboard nav: Tab cycles title → search → refresh → +New → filter
 *     facets → Clear → toolbar actions
 *
 * @see ADR-021 Fluent UI v9 design system (no v8, no inline styles, tokens only)
 * @see ADR-012 Shared component library (consumes @spaarke/ui-components)
 * @see ADR-026 Code Page React 19 + Vite build standard
 * @see smart-todo-r4 spec FR-06 / FR-10 / FR-11 / NFR-07
 */
import * as React from 'react';
import {
  Button,
  SearchBox,
  Text,
  Tooltip,
  type SearchBoxChangeEvent,
  type InputOnChangeData,
} from '@fluentui/react-components';
import { Add20Regular, ArrowClockwise20Regular } from '@fluentui/react-icons';
import {
  SelectionAwareToolbar,
  ViewToggle,
  type ToolbarAction,
  type ViewToggleMode,
} from '@spaarke/ui-components';
import { useHeaderStyles } from './Header.styles';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface HeaderProps {
  /**
   * Page title rendered in Row 1. Defaults to `"Smart To Do"` per FR-06.
   */
  title?: string;

  /**
   * Current search query (controlled). The parent owns this value so the
   * Refresh button can re-issue the search with the same text.
   */
  searchQuery?: string;

  /**
   * Called with the **debounced** search query (300 ms). The parent should
   * use this to drive its data fetch — typing into the SearchBox no longer
   * spams the data layer.
   */
  onSearchChange?: (query: string) => void;

  /** Placeholder for the SearchBox. Defaults to "Search to-dos…". */
  searchPlaceholder?: string;

  /**
   * Called when the user clicks the Refresh icon (Row 2). The parent should
   * re-query the to-do list using the current search + filters.
   */
  onRefresh?: () => void;

  /**
   * Called when the user clicks the "+ New" icon (Row 2). The parent opens
   * its new-to-do flow (task 040 wiring; for task 030 this can be a no-op
   * or a `console.log` stub).
   */
  onCreateTodo?: () => void;

  /**
   * Facet chips rendered in Row 3 (filter bar). Each `id` should be unique
   * within the array. The parent wires `onRemove` to drop that facet from
   * its filter state.
   *
   * Pass an empty array (default) and `onClearFilters === undefined` to hide
   * the Clear button (the row stays present but visually empty per FR-06).
   */
  facets?: FacetChip[];

  /**
   * Called when the user clicks "Clear" in Row 3 to reset all facets. When
   * undefined the Clear button is hidden.
   */
  onClearFilters?: () => void;

  /**
   * Number of items currently selected. Drives Row 4 visibility:
   * `<SelectionAwareToolbar>` renders `null` when this is 0.
   */
  selectedCount: number;

  /**
   * Actions rendered in Row 4 when ≥1 item is selected. Pass an empty array
   * to render the "N selected" label only (no actions). Task 032 wires the
   * real Open / Delete / Email / Pin actions.
   */
  toolbarActions?: ToolbarAction[];

  /**
   * Current SmartTodo view mode (R4 FR-09 / task 033).
   *
   * When both `viewMode` AND `onViewModeChange` are provided, the trailing-
   * edge of Row 3 renders a `<ViewToggle>` that lets the user switch between
   * Card and List renderings. When either is omitted (e.g. surfaces that do
   * not yet support a list view), the toggle is hidden — Row 3's layout stays
   * stable per FR-06.
   */
  viewMode?: ViewToggleMode;

  /**
   * Called when the user clicks the opposite view-mode segment (R4 FR-09).
   * The parent should persist the new mode via `useUserPreferences`.
   */
  onViewModeChange?: (mode: ViewToggleMode) => void;
}

/** A single filter facet (rendered as a Fluent v9 dismissible Tag in Row 3). */
export interface FacetChip {
  /** Stable React key. */
  id: string;
  /** Visible label (the displayed facet text, e.g. "Status: Open"). */
  label: string;
  /** Called when the facet's dismiss × is clicked. */
  onRemove: () => void;
}

// ---------------------------------------------------------------------------
// Debounce helper (300 ms — spec FR-06 search debounce)
// ---------------------------------------------------------------------------

const SEARCH_DEBOUNCE_MS = 300;

/**
 * Calls `callback` after `delay` ms of inactivity on `value`. The initial
 * value does NOT trigger a callback (skipped on mount), so consumers don't
 * see a spurious empty-string emission on first render.
 */
function useDebouncedEffect(
  value: string,
  delay: number,
  callback: (v: string) => void,
): void {
  // Track the latest callback in a ref so changes to the callback identity
  // don't reset the debounce timer.
  const callbackRef = React.useRef(callback);
  callbackRef.current = callback;

  // Skip the initial mount — emit only on actual user changes.
  const isFirstRun = React.useRef(true);

  React.useEffect(() => {
    if (isFirstRun.current) {
      isFirstRun.current = false;
      return;
    }
    const handle = window.setTimeout(() => {
      callbackRef.current(value);
    }, delay);
    return () => {
      window.clearTimeout(handle);
    };
  }, [value, delay]);
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * SmartTodo 4-row header — see file-level JSDoc for the full layout contract.
 */
export const Header: React.FC<HeaderProps> = ({
  title = 'Smart To Do',
  searchQuery: searchQueryProp,
  onSearchChange,
  searchPlaceholder = 'Search to-dos…',
  onRefresh,
  onCreateTodo,
  facets = [],
  onClearFilters,
  selectedCount,
  toolbarActions = [],
  viewMode,
  onViewModeChange,
}) => {
  const styles = useHeaderStyles();

  // Local controlled SearchBox value — debounced upward via onSearchChange.
  // The parent's `searchQuery` prop acts as a soft initializer + external
  // reset signal (e.g. Clear-all-filters can pass "" to reset the box).
  const [localQuery, setLocalQuery] = React.useState<string>(
    searchQueryProp ?? '',
  );

  // Re-sync the local query when the parent explicitly drives it (e.g. a
  // facet-clear that should also reset search text). We guard against the
  // common case where the prop is undefined throughout the component's life.
  React.useEffect(() => {
    if (searchQueryProp !== undefined && searchQueryProp !== localQuery) {
      setLocalQuery(searchQueryProp);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchQueryProp]);

  // Debounce emission to the parent (FR-06 — debounced search).
  useDebouncedEffect(localQuery, SEARCH_DEBOUNCE_MS, (next) => {
    onSearchChange?.(next);
  });

  const handleSearchChange = React.useCallback(
    (_e: SearchBoxChangeEvent, data: InputOnChangeData) => {
      setLocalQuery(data.value);
    },
    [],
  );

  const handleRefresh = React.useCallback(() => {
    onRefresh?.();
  }, [onRefresh]);

  const handleCreateTodo = React.useCallback(() => {
    onCreateTodo?.();
  }, [onCreateTodo]);

  const hasFacets = facets.length > 0;
  const showClear = hasFacets && onClearFilters !== undefined;

  return (
    <div className={styles.root}>
      {/* ── Row 1 — Page title ─────────────────────────────────────────── */}
      <div className={styles.titleRow}>
        <Text size={300} weight="semibold" as="h1">
          {title}
        </Text>
      </div>

      {/* ── Row 2 — Search + Refresh + "+ New" ──────────────────────────── */}
      <div className={styles.searchRow}>
        <div className={styles.searchInputWrap}>
          <SearchBox
            value={localQuery}
            placeholder={searchPlaceholder}
            onChange={handleSearchChange}
            aria-label="Search to-dos"
          />
        </div>
        <Tooltip content="Refresh" relationship="label">
          <Button
            className={styles.inlineToolbarButton}
            appearance="subtle"
            size="small"
            icon={<ArrowClockwise20Regular />}
            aria-label="Refresh to-dos"
            onClick={handleRefresh}
          />
        </Tooltip>
        <Tooltip content="New to-do" relationship="label">
          <Button
            className={styles.inlineToolbarButton}
            appearance="subtle"
            size="small"
            icon={<Add20Regular />}
            aria-label="Create new to-do"
            onClick={handleCreateTodo}
          />
        </Tooltip>
      </div>

      {/* ── Row 3 — Filter bar (facets + Clear) ─────────────────────────── */}
      <div className={styles.filterRow} role="region" aria-label="Filters">
        <div className={styles.facetGroup}>
          {/*
            Facets are deferred to task 031 ("Assigned to Me" filter). For
            task 030 the parent renders an empty array and this row stays
            present-but-empty (FR-06 — 4 stacked rows ALWAYS exist; their
            *content* is populated lazily by downstream tasks).
            When facets DO arrive, task 031 will render them here as Fluent
            v9 dismissible Tags via this slot.
          */}
        </div>
        {showClear && (
          <Button appearance="subtle" size="small" onClick={onClearFilters}>
            Clear
          </Button>
        )}
        {/* ── Row 3 trailing-edge — ViewToggle (R4 FR-09 / task 033) ─────── */}
        {viewMode !== undefined && onViewModeChange !== undefined && (
          <ViewToggle mode={viewMode} onChange={onViewModeChange} />
        )}
      </div>

      {/* ── Row 4 — Selection-aware toolbar (slot) ──────────────────────── */}
      <div className={styles.toolbarRow}>
        <SelectionAwareToolbar
          selectedCount={selectedCount}
          actions={toolbarActions}
        />
      </div>
    </div>
  );
};

Header.displayName = 'SmartTodoHeader';
