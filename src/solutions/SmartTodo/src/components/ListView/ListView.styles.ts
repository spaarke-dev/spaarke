/**
 * SmartTodo ListView — styles (Griffel makeStyles + Fluent v9 semantic tokens).
 *
 * Per ADR-021: no hard-coded colors, no inline `style={}` props, no CSS modules.
 *
 * @see ADR-021 Fluent UI v9 design system
 * @see smart-todo-r4 spec FR-09 (List/Card view toggle)
 */
import { makeStyles, shorthands, tokens } from '@fluentui/react-components';

export const useListViewStyles = makeStyles({
  /**
   * Outer container — fills the parent (Kanban-like) area and provides a
   * subtle card frame matching the existing SmartToDo Kanban container.
   */
  root: {
    display: 'flex',
    flexDirection: 'column',
    flex: '1 1 0',
    minHeight: 0,
    overflow: 'hidden',
    backgroundColor: tokens.colorNeutralBackground1,
  },

  /** Scrollable table viewport. */
  scrollArea: {
    flex: '1 1 0',
    overflowY: 'auto',
    overflowX: 'auto',
  },

  /** Fluent v9 <Table> wrapper — full-width, sticky header. */
  tableWrap: {
    minWidth: '100%',
  },

  /** Sticky header row — keeps column labels visible while scrolling. */
  headerRow: {
    position: 'sticky',
    top: 0,
    zIndex: 1,
    backgroundColor: tokens.colorNeutralBackground2,
  },

  /** Title column — flex grow, single-line truncate. */
  titleCell: {
    fontWeight: tokens.fontWeightSemibold,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },

  /** Cell with a small score chip (uses the parent score circle visual budget). */
  scoreCell: {
    fontVariantNumeric: 'tabular-nums',
    color: tokens.colorNeutralForeground2,
    whiteSpace: 'nowrap',
  },

  /** Cell with secondary text (Due, Assigned). */
  secondaryCell: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
  },

  /** Pin/badge action column — narrow, right-aligned. */
  actionCell: {
    width: '32px',
    textAlign: 'right',
    ...shorthands.padding('0', tokens.spacingHorizontalS),
  },

  /** Row hover — subtle background change. */
  row: {
    cursor: 'pointer',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },

  /** Selected row — accent background. */
  rowSelected: {
    backgroundColor: tokens.colorBrandBackground2,
    ':hover': {
      backgroundColor: tokens.colorBrandBackground2Hover,
    },
  },

  /** Completed-row state — opacity + strikethrough on title. */
  rowCompleted: {
    opacity: 0.6,
  },
  titleCompleted: {
    textDecorationLine: 'line-through',
  },

  /** Empty-state row. */
  emptyRow: {
    ...shorthands.padding(tokens.spacingVerticalXL, tokens.spacingHorizontalM),
    textAlign: 'center',
    color: tokens.colorNeutralForeground3,
  },
});
