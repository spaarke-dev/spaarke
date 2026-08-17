/**
 * SmartTodo Code Page Header — top-bar redesign (FR-05 / U-3, 2026-08-15/16).
 *
 * REPLACES the R4-104 single-row consolidated toolbar (3-field QuickAdd group
 * + toggle-into-inline-SearchBox "Filter" affordance) with the mockup's
 * minimal chrome per `projects/smart-todo-r5/to-do-header-revision.jpg`:
 *
 *   ┌─────────────────────────────────────────────────────────────────────┐
 *   │  [icon] Smart To Do                    [Filter] [+ New Task] [⋮]   │
 *   │     ── OR (when selectedCount > 0) ──                              │
 *   │  [icon] Smart To Do          [N selected][Open][Delete][Email][Pin] │
 *   │                                                          [Filter]   │
 *   └─────────────────────────────────────────────────────────────────────┘
 *
 * **Why** (spec FR-05, owner-confirmed 2026-08-14): the R4-104 toolbar grew a
 * 3-field QuickAdd group (F-3) plus a broken/mislabeled toggle-into-inline-
 * SearchBox "Filter" affordance (F-6 — it filtered client-side substrings,
 * not real search). FR-05 replaces both with: a Filter pill (ENTRY POINT
 * only — opens the expanding text search `components/SearchFilter` mounts
 * (task 021's structured Filter pane was REPLACED by this search per the
 * smart-todo-r5 UAT 2026-08-17 decision); no filter logic lives here),
 * a primary "+ New Task" button (stubbed until task 030 wires the OOB
 * main-form create modal per FR-10), and a "⋮" overflow menu holding exactly
 * Settings / Layout / Refresh (owner-decided mapping, U-3, 2026-08-15).
 *
 * **Preserved behaviour**: Settings (`onOpenSettings` via
 * `settingsOpenerRef`), Layout (`onOrientationChange` via `updateViewPrefs`),
 * and Refresh (`onRefresh` via `innerRefetchRef`) all keep their EXACT prior
 * handlers — only their trigger UI relocated from inline buttons into the
 * overflow Menu. Selection-aware actions (Open/Delete/Email/Pin via
 * `<SelectionAwareToolbar>`) are untouched. Per the pre-existing precedent
 * (QuickAdd/+Wizard were already suppressed during an active selection),
 * the overflow menu and "+ New Task" are likewise suppressed when
 * `selectedCount > 0` — only the Filter entry point remains available
 * alongside the selection-aware toolbar.
 *
 * **Removed** (FR-05 acceptance: "removed, not relabeled"): the 3-field
 * QuickAdd group (Title + Due Date + Assigned-To typeahead + Add + subtle
 * "+ New" wizard button) and the inline-SearchBox "Filter" toggle
 * (`isFilterOpen` + `<SearchBox>` pattern). The deleted Assigned-To
 * `Xrm.WebApi` contact-typeahead (debounced 250ms, `contains(fullname,'…')`
 * OData filter against `contact`, statecode eq 0, $top=8) is the canonical
 * reference for task 021's new filter pane Assigned-To category.
 *
 * **A11y (NFR-07 / WCAG 2.1 AA)**:
 *   - Tab order: Title (non-focusable) → Filter → + New Task → ⋮ trigger →
 *     menu items (Settings, Layout, Refresh) when the menu is open.
 *   - The Filter pill uses `aria-expanded` (disclosure semantics — it opens
 *     a pane, task 021) rather than `aria-pressed` (binary toggle).
 *   - The ⋮ trigger carries `aria-label="More options"` + a `<Tooltip>`
 *     (icon-only button). Fluent's `<Menu>` provides built-in Escape-to-close
 *     + focus-return-to-trigger + Arrow-key item navigation.
 *
 * @see ADR-021 Fluent UI v9 design system (no v8, no inline styles, tokens only)
 * @see ADR-012 Shared component library (consumes @spaarke/ui-components)
 * @see ADR-026 Code Page React 19 + Vite build standard
 * @see projects/smart-todo-r5/spec.md FR-05 (U-3)
 * @see projects/smart-todo-r5/to-do-header-revision.jpg — mockup
 * @see OrientationToggle.tsx — source of the horizontal↔vertical flip semantics
 *      reused (not imported — the map is two lines) by the Layout menu item.
 */
import * as React from 'react';
import {
  Button,
  Menu,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  Text,
  Toolbar,
  Tooltip,
} from '@fluentui/react-components';
import {
  Add20Regular,
  ArrowClockwise20Regular,
  LayoutColumnTwo20Regular,
  LayoutRowTwo20Regular,
  MoreHorizontal20Regular,
  Search20Regular,
  SettingsRegular,
} from '@fluentui/react-icons';
import {
  MicrosoftToDoIcon,
  SelectionAwareToolbar,
  type Orientation,
  type ToolbarAction,
} from '@spaarke/ui-components';
import { useHeaderStyles } from './Header.styles';

// ---------------------------------------------------------------------------
// QuickAdd event contract — RETAINED, not dispatched (task 020 / FR-05 / U-3,
// 2026-08-16).
//
// This Header no longer renders the QuickAdd input group or dispatches this
// event (removed per FR-05 — "removed, not relabeled"). The constant + type
// are kept exported SOLELY because `../SmartToDo.tsx` (out of this task's
// scope — not one of the 4 files task 020 may touch) still imports both for
// its `handleAdd` window-event listener (`window.addEventListener(
// QUICK_ADD_TODO_EVENT, ...)`), and deleting them would break that file's
// compile. That listener is now ORPHANED (no producer fires this event
// anymore) — a direct, in-scope consequence of FR-05 removing the QuickAdd
// UI, not a new regression introduced here. Flagged as a follow-up for task
// 030 (which owns the next create-path decision) in
// `projects/smart-todo-r5/notes/task-020-topbar.md`.
// ---------------------------------------------------------------------------

/** @deprecated Retained only for `SmartToDo.tsx`'s type import — see comment above. Not dispatched by this Header. */
export const QUICK_ADD_TODO_EVENT = 'sprk-smarttodo:quick-add' as const;

/** @deprecated Retained only for `SmartToDo.tsx`'s type import — see comment above. Not dispatched by this Header. */
export interface QuickAddTodoEventDetail {
  /** Trimmed, non-empty title for the new to-do. */
  title: string;
  /** ISO date string (YYYY-MM-DD) or empty/undefined for "no due date". */
  dueDate?: string;
  /** systemuser GUID to set on sprk_assignedto, or empty/undefined to default. */
  assignedToId?: string;
}

// ---------------------------------------------------------------------------
// Orientation flip map — mirrors OrientationToggle.tsx's NEXT_ORIENTATION
// (not imported: that module doesn't export it, and the map is trivial).
// Kept in sync manually; both are two-key Record<Orientation, Orientation>.
// ---------------------------------------------------------------------------

const NEXT_ORIENTATION: Record<Orientation, Orientation> = {
  horizontal: 'vertical',
  vertical: 'horizontal',
};

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface HeaderProps {
  /** Page title rendered alongside the icon. Defaults to `"Smart To Do"`. */
  title?: string;

  /** Called when the user clicks Refresh in the overflow menu. When omitted, the Refresh menu item is hidden. */
  onRefresh?: () => void;

  /** Called when the user clicks Settings in the overflow menu (Kanban thresholds). When omitted, the Settings menu item is hidden. */
  onOpenSettings?: () => void;

  /**
   * Number of items currently selected. Drives the selection-aware action
   * cluster: when 0, the Filter / + New Task / ⋮ overflow cluster renders;
   * when ≥1, the `<SelectionAwareToolbar>` renders (Open/Delete/Email/Pin)
   * alongside the Filter pill (matches the pre-existing precedent that
   * creation + settings/layout/refresh controls are suppressed during an
   * active selection).
   */
  selectedCount: number;

  /**
   * Actions rendered by the embedded `<SelectionAwareToolbar>` when
   * `selectedCount ≥ 1`.
   */
  toolbarActions?: ToolbarAction[];

  /**
   * Current Kanban orientation (R4 FR-28 / task 070).
   *
   * When both `orientation` AND `onOrientationChange` are provided, the
   * overflow menu renders a "Layout" item that flips between 'horizontal'
   * and 'vertical'. When either is omitted, the Layout item is hidden.
   */
  orientation?: Orientation;

  /** Called with the flipped orientation when the user clicks Layout in the overflow menu. */
  onOrientationChange?: (orientation: Orientation) => void;

  /**
   * Controlled open state for the search bar underneath (`components/SearchFilter`,
   * smart-todo-r5 UAT 2026-08-17 — replaced task 021's structured Filter pane;
   * this Header only renders the trigger, not the pane content). Required: the
   * sole consumer, `SmartTodoApp`, always lifts this state.
   */
  isFilterPaneOpen: boolean;

  /** Called when the user clicks the Filter pill — toggles `isFilterPaneOpen` upstream. */
  onToggleFilterPane: () => void;

  /**
   * Called when the user clicks "+ New Task". Until task 030 (FR-10) lands,
   * the parent wires a documented no-op stub — the button stays visible and
   * clickable (never hidden, never throws). When omitted entirely, the
   * button is hidden (matches the optional-prop convention used elsewhere
   * in this file).
   */
  onNewTask?: () => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const Header: React.FC<HeaderProps> = ({
  title = 'Smart To Do',
  onRefresh,
  onOpenSettings,
  selectedCount,
  toolbarActions = [],
  orientation,
  onOrientationChange,
  isFilterPaneOpen,
  onToggleFilterPane,
  onNewTask,
}) => {
  const styles = useHeaderStyles();

  const hasSelection = selectedCount > 0;
  const showLayoutMenuItem =
    orientation !== undefined && onOrientationChange !== undefined;

  const handleLayoutClick = React.useCallback(() => {
    if (orientation === undefined || onOrientationChange === undefined) return;
    onOrientationChange(NEXT_ORIENTATION[orientation]);
  }, [orientation, onOrientationChange]);

  const LayoutIcon =
    orientation === 'vertical' ? LayoutRowTwo20Regular : LayoutColumnTwo20Regular;

  return (
    <div className={styles.headerColumn}>
      {/* ── Title row: brand icon + "Smart To Do" text (left cluster,
            mockup-matched: checkmark glyph + title, left-aligned). */}
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
        <div className={styles.spacer} />

        {/* ── Right cluster: selection-aware actions OR Filter / + New Task
              / ⋮ overflow, per mockup order (Filter · + New Task · ⋮). ── */}
        {hasSelection ? (
          <div className={styles.rightGroup}>
            <SelectionAwareToolbar
              selectedCount={selectedCount}
              actions={toolbarActions}
            />
            <Button
              appearance="outline"
              size="small"
              icon={<Search20Regular />}
              onClick={onToggleFilterPane}
              aria-expanded={isFilterPaneOpen}
              className={styles.filterButton}
            >
              Filter
            </Button>
          </div>
        ) : (
          <div className={styles.rightGroup}>
            <Button
              appearance="outline"
              size="small"
              icon={<Search20Regular />}
              onClick={onToggleFilterPane}
              aria-expanded={isFilterPaneOpen}
              className={styles.filterButton}
            >
              Filter
            </Button>
            {onNewTask !== undefined && (
              <Button
                appearance="primary"
                size="small"
                icon={<Add20Regular />}
                onClick={onNewTask}
                className={styles.newTaskButton}
              >
                New Task
              </Button>
            )}
            <Menu>
              <MenuTrigger disableButtonEnhancement>
                <Tooltip content="More options" relationship="label">
                  <Button
                    appearance="subtle"
                    size="small"
                    icon={<MoreHorizontal20Regular />}
                    aria-label="More options"
                    className={styles.overflowTrigger}
                  />
                </Tooltip>
              </MenuTrigger>
              <MenuPopover>
                <MenuList>
                  {onOpenSettings !== undefined && (
                    <MenuItem icon={<SettingsRegular />} onClick={onOpenSettings}>
                      Settings
                    </MenuItem>
                  )}
                  {showLayoutMenuItem && (
                    <MenuItem icon={<LayoutIcon />} onClick={handleLayoutClick}>
                      Layout
                    </MenuItem>
                  )}
                  {onRefresh !== undefined && (
                    <MenuItem icon={<ArrowClockwise20Regular />} onClick={onRefresh}>
                      Refresh
                    </MenuItem>
                  )}
                </MenuList>
              </MenuPopover>
            </Menu>
          </div>
        )}
      </Toolbar>
    </div>
  );
};

Header.displayName = 'SmartTodoHeader';
