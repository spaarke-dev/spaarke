/**
 * SmartTodo Code Page Header — SINGLE-ROW consolidated toolbar
 * (R4-104 / Wave E-3 / UAT items 8, 9, 10, 11 — 2026-06-18).
 *
 * COLLAPSED FROM the prior R4-030 4-row layout into ONE Fluent v9 `<Toolbar>`
 * landmark. Mirrors the SmartTodoWidget chrome (R4-099) so the standalone Code
 * Page and the workspace widget feel like one product.
 *
 *   ┌─────────────────────────────────────────────────────────────────────┐
 *   │  [icon] Smart To Do  |  [QuickAdd Input] [Add]  |  [+ Wizard]      │
 *   │     [(grows)]                                                       │
 *   │     [Refresh] [ViewToggle] [OrientationToggle?] [Settings?] [Search]│
 *   │     ── OR (when selectedCount > 0) ──                              │
 *   │     [N selected] [Open] [Delete] [Email] [Pin] [Search]            │
 *   └─────────────────────────────────────────────────────────────────────┘
 *
 * **Why single-row** (UAT 11): The 4-row layout created duplicate chrome with
 * the inner `<KanbanHeader>` (its own title + AddTodoBar) — UAT users saw two
 * "Smart To Do" titles + a confusing visual hierarchy. The widget's compact
 * single-toolbar (Wave D R4-099) became the design reference.
 *
 * **Compact QuickAdd** (UAT 9): The quick-create input uses `<Input size="small">`
 * matching the widget's QuickAdd pattern (R4-103). Submission dispatches the
 * canonical `QUICK_ADD_TODO_EVENT` window event so the inner `<SmartToDo>` (which
 * owns the optimistic add + Dataverse create logic) handles it via its existing
 * `handleAdd`. This keeps the create state path single-source while letting the
 * QuickAdd live in the consolidated toolbar.
 *
 * **Preserved behaviour** (all Wave 2a/2b features keep working, just relocated
 * into the single toolbar):
 *   - R4-031 Assigned-to-Me filter — baked into the query at the data layer;
 *     this header no longer renders facet chips (the Tag was redundant — the
 *     filter is non-removable per OD-2).
 *   - R4-032 Selection-aware toolbar actions (Open/Delete/Email/Pin) — wired
 *     via the embedded `<SelectionAwareToolbar>` when `selectedCount > 0`.
 *   - R4-033 List/Card view toggle — `<ViewToggle>` rendered in the right group
 *     when `viewMode` + `onViewModeChange` are provided.
 *   - R4-070 Vertical/Horizontal orientation toggle — `<OrientationToggle>` in
 *     the right group when `orientation` + `onOrientationChange` are provided
 *     AND the current view is kanban (orientation has no meaning in list view).
 *   - R4-040 OPEN_TODOS_EVENT — unchanged; the selection toolbar's Open
 *     dispatches it; the modal subscriber in `SmartTodoApp` handles routing.
 *
 * **A11y (NFR-07 / WCAG 2.1 AA)**:
 *   - Single `<Toolbar>` landmark replaces 4 stacked regions (better SR rhythm).
 *   - All icon-only buttons have `aria-label` + `<Tooltip relationship="label">`.
 *   - `<SearchBox>` carries its own `aria-label`.
 *   - Tab order matches visual order: Title (non-focusable) → QuickAdd input →
 *     Add → +Wizard → Refresh → ViewToggle → OrientationToggle → Settings →
 *     SearchBox → (SelectionAware actions when count > 0).
 *
 * @see ADR-021 Fluent UI v9 design system (no v8, no inline styles, tokens only)
 * @see ADR-012 Shared component library (consumes @spaarke/ui-components)
 * @see ADR-026 Code Page React 19 + Vite build standard
 * @see smart-todo-r4 R4-104 audit notes/e-widget-app-parity-audit-2026-06-18.md
 * @see SmartTodoWidget chrome (R4-099) — visual reference
 */
import * as React from 'react';
import {
  Button,
  Input,
  SearchBox,
  Text,
  ToggleButton,
  Toolbar,
  Tooltip,
  type InputOnChangeData,
  type SearchBoxChangeEvent,
} from '@fluentui/react-components';
import {
  Add20Regular,
  ArrowClockwise20Regular,
  Search20Regular,
  SettingsRegular,
} from '@fluentui/react-icons';
import {
  MicrosoftToDoIcon,
  OrientationToggle,
  SelectionAwareToolbar,
  ViewToggle,
  type Orientation,
  type ToolbarAction,
  type ViewToggleMode,
} from '@spaarke/ui-components';
import { useHeaderStyles } from './Header.styles';

// ---------------------------------------------------------------------------
// QuickAdd event contract
// ---------------------------------------------------------------------------

/**
 * Canonical event name dispatched by the Header's QuickAdd input when the user
 * submits a new title. The inner `<SmartToDo>` subscribes and routes to its
 * existing `handleAdd` — keeping the optimistic-add Dataverse create logic
 * single-source while letting the input live in the consolidated toolbar.
 *
 * Detail: `{ title: string }` — already-trimmed, non-empty.
 */
export const QUICK_ADD_TODO_EVENT = 'sprk-smarttodo:quick-add' as const;

/** Detail payload for the QUICK_ADD_TODO_EVENT custom event.
 *  UAT 2026-06-19: extended from title-only to three-field. */
export interface QuickAddTodoEventDetail {
  /** Trimmed, non-empty title for the new to-do. */
  title: string;
  /** ISO date string (YYYY-MM-DD) or empty/undefined for "no due date". */
  dueDate?: string;
  /** systemuser GUID to set on sprk_assignedto, or empty/undefined to default. */
  assignedToId?: string;
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface HeaderProps {
  /** Page title rendered alongside the icon. Defaults to `"Smart To Do"`. */
  title?: string;

  /** Current search query (controlled). */
  searchQuery?: string;

  /**
   * Called with the **debounced** search query (300 ms). Parent uses this to
   * drive its data fetch — typing into the SearchBox does not spam the data
   * layer.
   */
  onSearchChange?: (query: string) => void;

  /** Placeholder for the SearchBox. Defaults to "Search…". */
  searchPlaceholder?: string;

  /** Called when the user clicks the Refresh icon. */
  onRefresh?: () => void;

  /**
   * Called when the user clicks the "+" wizard button. Parent opens the full
   * `CreateTodoWizard` (richer fields than QuickAdd). When omitted, the
   * wizard button is hidden.
   */
  onOpenWizard?: () => void;

  /**
   * Called when the user clicks the Settings gear (Kanban thresholds + future
   * preferences). When omitted, the Settings button is hidden.
   */
  onOpenSettings?: () => void;

  /**
   * Number of items currently selected. Drives the selection-aware action
   * cluster: when 0, the right-side controls render; when ≥1, the
   * `<SelectionAwareToolbar>` renders (Open/Delete/Email/Pin).
   */
  selectedCount: number;

  /**
   * Actions rendered by the embedded `<SelectionAwareToolbar>` when
   * `selectedCount ≥ 1`. R4-032 wires the real Open / Delete / Email / Pin
   * handlers from `createToolbarActions`.
   */
  toolbarActions?: ToolbarAction[];

  /**
   * Current SmartTodo view mode (R4 FR-09 / task 033).
   *
   * When both `viewMode` AND `onViewModeChange` are provided, the right group
   * renders a `<ViewToggle>`. When either is omitted, the toggle is hidden.
   */
  viewMode?: ViewToggleMode;

  /**
   * Called when the user clicks the opposite view-mode segment (R4 FR-09).
   * Parent persists the new mode via `useUserPreferences`.
   */
  onViewModeChange?: (mode: ViewToggleMode) => void;

  /**
   * Current Kanban orientation (R4 FR-28 / task 070).
   *
   * When both `orientation` AND `onOrientationChange` are provided AND the
   * current view is kanban (viewMode !== 'list'), the right group renders an
   * `<OrientationToggle>`. Orientation has no meaning in list view so the
   * toggle is suppressed there.
   */
  orientation?: Orientation;

  /**
   * Called when the user clicks the orientation toggle (R4 FR-28).
   * Parent persists via `useUserPreferences`.
   */
  onOrientationChange?: (orientation: Orientation) => void;

  /**
   * Placeholder for the QuickAdd input. Defaults to "Add a to-do…".
   */
  quickAddPlaceholder?: string;

  /**
   * UAT 2026-06-19 — current user's sprk_contact GUID (resolved upstream
   * via useCurrentContactId in SmartTodoApp). Used as the default
   * assignedTo for new todos created via the quick-add. Hidden from the UI
   * (only the contact NAME is shown in the Assigned To text field).
   */
  defaultAssignedToContactId?: string;
  /**
   * UAT 2026-06-19 — display name for the current user's contact. Used as
   * the visible value in the quick-add Assigned To field (so the user sees
   * "Jane Doe" not a GUID). User can edit; on edit, only the visible text
   * changes (the bind still goes to the original contactId).
   */
  defaultAssignedToName?: string;
}

// ---------------------------------------------------------------------------
// Debounce helper (300 ms — preserved from R4-030 spec FR-06 search debounce)
// ---------------------------------------------------------------------------

const SEARCH_DEBOUNCE_MS = 300;

function useDebouncedEffect(
  value: string,
  delay: number,
  callback: (v: string) => void,
): void {
  const callbackRef = React.useRef(callback);
  callbackRef.current = callback;

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

export const Header: React.FC<HeaderProps> = ({
  defaultAssignedToContactId,
  defaultAssignedToName,
  title = 'Smart To Do',
  searchQuery: searchQueryProp,
  onSearchChange,
  searchPlaceholder = 'Search…',
  onRefresh,
  onOpenWizard,
  onOpenSettings,
  selectedCount,
  toolbarActions = [],
  viewMode,
  onViewModeChange,
  orientation,
  onOrientationChange,
  quickAddPlaceholder = 'Add a to-do…',
}) => {
  const styles = useHeaderStyles();

  // ── SearchBox local state (debounced upward) ────────────────────────────
  const [localQuery, setLocalQuery] = React.useState<string>(
    searchQueryProp ?? '',
  );

  // External reset signal (e.g. Clear-all elsewhere passing "" via prop).
  React.useEffect(() => {
    if (searchQueryProp !== undefined && searchQueryProp !== localQuery) {
      setLocalQuery(searchQueryProp);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchQueryProp]);

  useDebouncedEffect(localQuery, SEARCH_DEBOUNCE_MS, (next) => {
    onSearchChange?.(next);
  });

  const handleSearchChange = React.useCallback(
    (_e: SearchBoxChangeEvent, data: InputOnChangeData) => {
      setLocalQuery(data.value);
    },
    [],
  );

  // ── QuickAdd local state ────────────────────────────────────────────────
  // UAT 2026-06-19: three-field quick-add (title + due date + assigned to).
  // The actual create logic lives inside `<SmartToDo>`; we dispatch a window
  // CustomEvent with the extended detail payload that SmartToDo subscribes to.
  const todayISODate = React.useMemo(() => {
    const d = new Date();
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  }, []);
  const [quickAddValue, setQuickAddValue] = React.useState<string>('');
  const [quickAddDueDate, setQuickAddDueDate] = React.useState<string>(todayISODate);
  // Display = name (visible); internal = contactId (used for bind).
  const [quickAddAssignedTo, setQuickAddAssignedTo] = React.useState<string>('');
  const [quickAddAssignedToContactId, setQuickAddAssignedToContactId] = React.useState<string>('');

  // UAT 2026-06-20 — track contactId internally (for the bind default);
  // do NOT pre-fill the visible name field. User sees placeholder
  // "Assigned to" by default; implicit assignment is to current user.
  React.useEffect(() => {
    if (defaultAssignedToContactId && !quickAddAssignedToContactId) {
      setQuickAddAssignedToContactId(defaultAssignedToContactId);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [defaultAssignedToContactId]);

  const dispatchQuickAdd = React.useCallback((title: string) => {
    const trimmed = title.trim();
    if (!trimmed) return;
    const detail: QuickAddTodoEventDetail = {
      title: trimmed,
      dueDate: quickAddDueDate || undefined,
      // UAT 2026-06-19 — pass the internal contactId for the bind, not
      // the display name. Falls back to upstream default if the user
      // hasn't changed it.
      assignedToId:
        quickAddAssignedToContactId ||
        defaultAssignedToContactId ||
        undefined,
    };
    window.dispatchEvent(
      new CustomEvent<QuickAddTodoEventDetail>(QUICK_ADD_TODO_EVENT, { detail }),
    );
    setQuickAddValue('');
  }, [quickAddDueDate, quickAddAssignedToContactId, defaultAssignedToContactId]);

  const handleQuickAddChange = React.useCallback(
    (_e: React.ChangeEvent<HTMLInputElement>, data: { value: string }) => {
      setQuickAddValue(data.value);
    },
    [],
  );

  const handleQuickAddDueDateChange = React.useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      setQuickAddDueDate(e.target.value);
    },
    [],
  );

  const handleQuickAddAssignedToChange = React.useCallback(
    (_e: React.ChangeEvent<HTMLInputElement>, data: { value: string }) => {
      setQuickAddAssignedTo(data.value);
    },
    [],
  );

  const handleQuickAddKeyDown = React.useCallback(
    (ev: React.KeyboardEvent<HTMLInputElement>) => {
      if (ev.key === 'Enter') {
        ev.preventDefault();
        dispatchQuickAdd(quickAddValue);
      }
    },
    [dispatchQuickAdd, quickAddValue],
  );

  const handleQuickAddClick = React.useCallback(() => {
    dispatchQuickAdd(quickAddValue);
  }, [dispatchQuickAdd, quickAddValue]);

  // UAT 2026-06-19 — Filter slide toggle. When active, the right action
  // cluster swaps to an inline SearchBox + close button (same pattern as widget).
  const [isFilterOpen, setIsFilterOpen] = React.useState<boolean>(false);
  const handleToggleFilter = React.useCallback(() => {
    setIsFilterOpen((v) => !v);
    if (isFilterOpen) {
      // Closing → clear the query so next open starts fresh.
      setLocalQuery('');
    }
  }, [isFilterOpen]);

  // ── Right-group derived flags ───────────────────────────────────────────
  const showViewToggle = viewMode !== undefined && onViewModeChange !== undefined;
  const showOrientationToggle =
    orientation !== undefined &&
    onOrientationChange !== undefined &&
    viewMode !== 'list'; // orientation only meaningful in kanban
  const hasSelection = selectedCount > 0;
  const quickAddDisabled = quickAddValue.trim().length === 0;

  return (
    <div className={styles.headerColumn}>
      {/* ── Title row (UAT 2026-06-19): brand icon + "Smart To Do" text
            now sits in its OWN row above the toolbar (was inline at the
            start of the toolbar). Mirrors the widget's title row for
            uniform chrome across surfaces. */}
      <div className={styles.titleRow}>
        <div className={styles.titleGroup}>
          <MicrosoftToDoIcon size={20} active />
          <Text size={400} weight="semibold" as="h1" className={styles.title}>
            {title}
          </Text>
        </div>
      </div>

      <Toolbar
        aria-label="Smart To Do toolbar"
        size="small"
        className={styles.toolbar}
      >
      {/* ── Center-left: QuickAdd (compact, matches widget pattern) ────── */}
      {/*
        QuickAdd is suppressed when there is an active selection — the
        right-side action cluster transitions to the selection-aware toolbar
        and the QuickAdd input would compete with it visually. Users can
        clear their selection to add a new to-do.
      */}
      {!hasSelection && (
        // UAT 2026-06-19: three-field quick-add (Title + Due Date + Assigned To + Add).
        // Replaces single-input title-only quick-add. Dispatches extended
        // QUICK_ADD_TODO_EVENT detail consumed by SmartToDo.
        <div className={styles.quickAddGroup}>
          <Input
            size="small"
            value={quickAddValue}
            placeholder={quickAddPlaceholder}
            onChange={handleQuickAddChange}
            onKeyDown={handleQuickAddKeyDown}
            aria-label="To-do name"
            className={styles.quickAddInput}
          />
          <input
            type="date"
            value={quickAddDueDate}
            onChange={handleQuickAddDueDateChange}
            aria-label="Due date"
            className={styles.quickAddDateInput}
          />
          <Input
            size="small"
            value={quickAddAssignedTo}
            onChange={handleQuickAddAssignedToChange}
            placeholder="Assigned to"
            aria-label="Assigned to"
            className={styles.quickAddAssignedInput}
          />
          <Tooltip
            content="Add to-do (Enter)"
            relationship="label"
          >
            <Button
              appearance="primary"
              size="small"
              icon={<Add20Regular />}
              disabled={quickAddDisabled}
              onClick={handleQuickAddClick}
              aria-label="Add to-do"
            />
          </Tooltip>
          {onOpenWizard !== undefined && (
            <Tooltip content="New to-do (full form)" relationship="label">
              <Button
                appearance="subtle"
                size="small"
                onClick={onOpenWizard}
                aria-label="New to-do (full form)"
              >
                +&nbsp;New
              </Button>
            </Tooltip>
          )}
        </div>
      )}

      {/* ── Spacer ─────────────────────────────────────────────────────── */}
      <div className={styles.spacer} />

      {/* ── Right: selection-aware OR default action cluster ───────────── */}
      {/* UAT 2026-06-19 — Filter slide UX:
            - When Filter is OFF: show the action cluster (Refresh + Orient + Settings) + Filter icon.
            - When Filter is ON: hide the action cluster; show an inline SearchBox
              that takes that space + a Filter-close icon. (Matches the widget's
              Filter slide UX for chrome uniformity.)
            Selection-aware actions still take precedence over Filter open:
            if there's a selection, the SelectionAwareToolbar replaces the
            action cluster, and the Filter icon stays in its rightmost slot. */}
      {hasSelection ? (
        <div className={styles.rightGroup}>
          <SelectionAwareToolbar
            selectedCount={selectedCount}
            actions={toolbarActions}
          />
          {isFilterOpen ? (
            <>
              <div className={styles.searchWrap}>
                <SearchBox
                  size="small"
                  value={localQuery}
                  placeholder={searchPlaceholder}
                  onChange={handleSearchChange}
                  aria-label="Filter to-dos"
                  autoFocus
                />
              </div>
              <Tooltip content="Close filter" relationship="label">
                <ToggleButton
                  appearance="subtle"
                  size="small"
                  icon={<Search20Regular />}
                  checked
                  onClick={handleToggleFilter}
                  aria-label="Close filter"
                  aria-expanded
                />
              </Tooltip>
            </>
          ) : (
            <Tooltip content="Filter to-dos" relationship="label">
              <ToggleButton
                appearance="subtle"
                size="small"
                icon={<Search20Regular />}
                checked={false}
                onClick={handleToggleFilter}
                aria-label="Open filter"
                aria-expanded={false}
              />
            </Tooltip>
          )}
        </div>
      ) : (
        <div className={styles.rightGroup}>
          {isFilterOpen ? (
            <>
              <div className={styles.searchWrap}>
                <SearchBox
                  size="small"
                  value={localQuery}
                  placeholder={searchPlaceholder}
                  onChange={handleSearchChange}
                  aria-label="Filter to-dos"
                  autoFocus
                />
              </div>
              <Tooltip content="Close filter" relationship="label">
                <ToggleButton
                  appearance="subtle"
                  size="small"
                  icon={<Search20Regular />}
                  checked
                  onClick={handleToggleFilter}
                  aria-label="Close filter"
                  aria-expanded
                />
              </Tooltip>
            </>
          ) : (
            <>
              {onRefresh !== undefined && (
                <Tooltip content="Refresh" relationship="label">
                  <Button
                    appearance="subtle"
                    size="small"
                    icon={<ArrowClockwise20Regular />}
                    aria-label="Refresh to-dos"
                    onClick={onRefresh}
                  />
                </Tooltip>
              )}
              {showViewToggle && (
                <ViewToggle
                  mode={viewMode as ViewToggleMode}
                  onChange={onViewModeChange as (mode: ViewToggleMode) => void}
                />
              )}
              {showOrientationToggle && (
                <OrientationToggle
                  orientation={orientation as Orientation}
                  onChange={onOrientationChange as (orientation: Orientation) => void}
                />
              )}
              {onOpenSettings !== undefined && (
                <Tooltip content="Settings" relationship="label">
                  <Button
                    appearance="subtle"
                    size="small"
                    icon={<SettingsRegular />}
                    aria-label="Settings"
                    onClick={onOpenSettings}
                  />
                </Tooltip>
              )}
              <Tooltip content="Filter to-dos" relationship="label">
                <ToggleButton
                  appearance="subtle"
                  size="small"
                  icon={<Search20Regular />}
                  checked={false}
                  onClick={handleToggleFilter}
                  aria-label="Open filter"
                  aria-expanded={false}
                />
              </Tooltip>
            </>
          )}
        </div>
      )}
      </Toolbar>
    </div>
  );
};

Header.displayName = 'SmartTodoHeader';
