/**
 * SmartTodoWidget styles — Fluent v9 + Griffel + semantic tokens only (ADR-021).
 *
 * Layout principles for 6-layout compatibility (R4 spec FR-04):
 *   - Root is a flex column with `height: 100%; minHeight: 0` so it sizes to
 *     the host container without forcing a fixed height.
 *   - Body uses `overflow: auto` so it can scroll independently within the
 *     widget header. The header (PaneHeader) stays sticky at the top.
 *   - Card list uses CSS grid with `minmax(0, 1fr)` columns to stack cleanly
 *     in narrow panes (mobile-narrow consideration).
 *   - No fixed widths anywhere — only flex / grid intrinsic sizing.
 *   - No hardcoded colors — every visible color is a Fluent v9 semantic token.
 *
 * For Pattern D rationale see `@spaarke/smart-todo-components/README.md`.
 */

import { makeStyles, shorthands, tokens } from '@fluentui/react-components';

export const useSmartTodoWidgetStyles = makeStyles({
  /** Outer container. Fills the host pane; root scroll OFF (body owns scroll). */
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    width: '100%',
    minHeight: 0,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    boxSizing: 'border-box',
    overflow: 'hidden',
  },

  /** Scrollable body — owns internal scroll for the cards list. */
  body: {
    flex: '1 1 auto',
    minHeight: 0,
    overflowY: 'auto',
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
  },

  /** Empty-state container — centered, soft tone. */
  emptyContainer: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    ...shorthands.gap(tokens.spacingVerticalS),
    ...shorthands.padding(tokens.spacingHorizontalXL),
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
  },

  /** Loading-state container — centered spinner. */
  loadingContainer: {
    flex: '1 1 auto',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: 0,
  },

  /** Error banner — uses inherent MessageBar tokens. */
  errorContainer: {
    ...shorthands.padding(
      tokens.spacingVerticalS,
      tokens.spacingHorizontalM,
      tokens.spacingVerticalXS,
      tokens.spacingHorizontalM
    ),
    flexShrink: 0,
  },

  /** Vertical card list — narrow-pane friendly. */
  cardList: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalXS),
    minWidth: 0,
  },

  /** Individual todo card. */
  todoCard: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
    ...shorthands.padding(
      tokens.spacingVerticalS,
      tokens.spacingHorizontalM,
      tokens.spacingVerticalS,
      tokens.spacingHorizontalM
    ),
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderWidth('1px'),
    ...shorthands.borderStyle('solid'),
    ...shorthands.borderColor(tokens.colorNeutralStroke2),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    boxShadow: tokens.shadow2,
    cursor: 'pointer',
    minWidth: 0,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
      ...shorthands.borderColor(tokens.colorNeutralStroke1Hover),
    },
    ':focus-visible': {
      outline: `2px solid ${tokens.colorStrokeFocus2}`,
      outlineOffset: '2px',
    },
  },

  /** Card title — semibold, truncates with ellipsis when pane narrow. */
  cardTitle: {
    flex: '1 1 auto',
    minWidth: 0,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },

  /** Card metadata row — due date + status badge. */
  cardMeta: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalXS),
    flexShrink: 0,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },

  /** Status pill — Open vs In Progress. */
  statusBadge: {
    display: 'inline-flex',
    alignItems: 'center',
    ...shorthands.padding('1px', tokens.spacingHorizontalXS),
    ...shorthands.borderRadius(tokens.borderRadiusSmall),
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorBrandForeground2,
  },
});
