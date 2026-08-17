/**
 * SmartTodo Header — styles for the FR-05 / U-3 top-bar redesign
 * (2026-08-15/16, replaces the R4-104 single-row consolidated toolbar).
 *
 * All hard-coded colors and inline styles are forbidden per ADR-021 — every
 * visual property below is a Fluent v9 semantic token.
 *
 * Layout zones (left → right):
 *   - `titleGroup`      : Microsoft To Do icon + "Smart To Do" text
 *   - `spacer`          : flex-grow gap that pushes the right cluster to the edge
 *   - `rightGroup`      : selection-aware actions (when count > 0) + Filter, OR
 *                         Filter / + New Task / ⋮ overflow (default cluster)
 *   - `filterButton`    : outlined Filter pill (disclosure trigger for the
 *                         filter pane — task 021 owns pane content)
 *   - `newTaskButton`   : primary "+ New Task" button
 *   - `overflowTrigger` : icon-only "⋮" Menu trigger (Settings / Layout / Refresh)
 *
 * @see ADR-021 Fluent UI v9 design system
 * @see projects/smart-todo-r5/spec.md FR-05 (U-3)
 */
import { makeStyles, shorthands, tokens } from '@fluentui/react-components';

export const useHeaderStyles = makeStyles({
  /**
   * Header column — wraps the title row + toolbar row (UAT 2026-06-19:
   * title moved to its OWN row above the toolbar per user feedback;
   * previously the title sat inline at the start of the toolbar).
   */
  headerColumn: {
    flexShrink: 0,
    display: 'flex',
    flexDirection: 'column',
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderBottom(
      tokens.strokeWidthThin,
      'solid',
      tokens.colorNeutralStroke1,
    ),
  },

  /**
   * Title row — sits ABOVE the toolbar, full width, slightly larger text +
   * brand icon. Mirrors the widget's title row for uniformity (UAT 2026-06-19).
   */
  titleRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    ...shorthands.padding(
      tokens.spacingVerticalS,
      tokens.spacingHorizontalM,
      tokens.spacingVerticalXS,
      tokens.spacingHorizontalM,
    ),
  },

  /**
   * Toolbar row — flex row below the title row. NO bottom border (the
   * headerColumn carries it instead).
   */
  toolbar: {
    flexShrink: 0,
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    flexWrap: 'wrap',
    columnGap: tokens.spacingHorizontalM,
    rowGap: tokens.spacingVerticalXS,
    ...shorthands.padding(
      tokens.spacingVerticalXS,
      tokens.spacingHorizontalM,
      tokens.spacingVerticalS,
      tokens.spacingHorizontalM,
    ),
    minHeight: '44px',
    boxSizing: 'border-box',
  },

  /** Brand title cluster (icon + text). Flex-shrink: 0 to keep title intact.
   *  Kept for back-compat — still applied to the titleRow children. */
  titleGroup: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flexShrink: 0,
  },

  /** Title text — color from token; trailing margin handled by gap. */
  title: {
    color: tokens.colorNeutralForeground1,
    whiteSpace: 'nowrap',
  },

  /**
   * Flex spacer — pushes the right cluster to the trailing edge of the row.
   * `flex: '1 1 0'` (vs auto) means it absorbs leftover space so the right
   * cluster stays pinned to the trailing edge regardless of title length.
   */
  spacer: {
    flex: '1 1 0',
    minWidth: 0,
  },

  /**
   * Right cluster — either the selection-aware toolbar + Filter (when count
   * > 0) or the default Filter / + New Task / ⋮ overflow cluster.
   * `flexShrink: 0` so buttons stay visible on narrow viewports.
   */
  rightGroup: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    flexShrink: 0,
  },

  /**
   * Filter pill — outlined disclosure trigger (opens the filter pane owned
   * by task 021). `flexShrink: 0` keeps it from compressing on narrow
   * viewports alongside the other right-cluster controls.
   */
  filterButton: {
    flexShrink: 0,
  },

  /** "+ New Task" primary button — flexShrink: 0 for the same reason as `filterButton`. */
  newTaskButton: {
    flexShrink: 0,
  },

  /** "⋮" overflow Menu trigger (icon-only) — flexShrink: 0 for the same reason as `filterButton`. */
  overflowTrigger: {
    flexShrink: 0,
  },
});
