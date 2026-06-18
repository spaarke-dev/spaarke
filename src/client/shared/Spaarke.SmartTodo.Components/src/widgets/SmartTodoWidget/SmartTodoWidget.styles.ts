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
 * R4 task 099 (W-1 — toolbar consolidation, 2026-06-18):
 *   - Added `toolbar` (sole chrome row grid: SearchBox left, buttons right)
 *   - Added `searchWrap` / `toolbarActions` (left+right halves of the toolbar)
 *   - Added `todoCardSelected` (selected-card visual treatment — brand-tinted
 *     border + background for selection-aware Open button).
 *   - PaneHeader removed from widget; the host SectionPanel provides the title.
 *
 * R4 task 101 (W-3 — Today/Tomorrow/Future grouping, 2026-06-18):
 *   - Added `groupList` (vertical stack of grouped sections, gap between).
 *   - Added `groupSection` (single bucket's container — header + list).
 *   - Added `groupHeader` (column-name + count strip with accent-colored left
 *     border; the accent color comes from `bucketTodoItems()` via inline
 *     `style.borderLeftColor` — the only place inline style is allowed,
 *     because Griffel cannot author dynamic per-rule colors from runtime
 *     values; the rest of the style is fully Griffel/token).
 *   - Added `groupTitle` + `groupCount` (header text composition).
 *   - Added `groupEmpty` (placeholder shown when a bucket has zero items so
 *     the user can always see the grouping structure — closes the audit's
 *     "empty buckets visible" requirement at the section-list level).
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

  /**
   * Sole chrome row — single `<Toolbar>` containing [SearchBox, +, Open, refresh].
   *
   * Layout: SearchBox grows on the left; action buttons cluster on the right.
   * Uses `justifyContent: space-between` via flex; no fixed widths so the
   * SearchBox shrinks gracefully in narrow panes (mobile-narrow layout
   * compatibility per spec FR-04).
   */
  toolbar: {
    flexShrink: 0,
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
    ...shorthands.padding(
      tokens.spacingVerticalXS,
      tokens.spacingHorizontalM,
      tokens.spacingVerticalXS,
      tokens.spacingHorizontalM
    ),
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
    minHeight: '36px',
  },

  /**
   * Left half of the toolbar — SearchBox container. Grows to fill available
   * width; `minWidth: 0` lets it shrink below its intrinsic content width
   * in narrow panes (preventing toolbar overflow).
   */
  searchWrap: {
    flex: '1 1 auto',
    minWidth: 0,
    display: 'flex',
    alignItems: 'center',
  },

  /**
   * Right half of the toolbar — action button cluster. `flexShrink: 0` so
   * buttons stay visible even when the SearchBox compresses.
   */
  toolbarActions: {
    flexShrink: 0,
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalXS),
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

  /**
   * Outer wrapper for the three Today/Tomorrow/Future grouped sections.
   * Vertical stack with a soft gap between sections so the visual rhythm
   * makes the grouping obvious without heavy dividers.
   */
  groupList: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalM),
    minWidth: 0,
  },

  /** Single grouped section — header + list. */
  groupSection: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalXS),
    minWidth: 0,
  },

  /**
   * Section header strip — accent-color left border (set inline from the
   * column's `accentColor`), section title, item count. Subtle background
   * + uppercase letter-spacing so it reads as a divider, not a card.
   */
  groupHeader: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    ...shorthands.gap(tokens.spacingHorizontalS),
    ...shorthands.padding(
      tokens.spacingVerticalXS,
      tokens.spacingHorizontalS,
      tokens.spacingVerticalXS,
      tokens.spacingHorizontalS
    ),
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderRadius(tokens.borderRadiusSmall),
    borderLeftWidth: '3px',
    borderLeftStyle: 'solid',
    // borderLeftColor is supplied via inline style from the runtime accent —
    // fallback to neutral when the bucket has no accent (defensive).
    borderLeftColor: tokens.colorNeutralStroke2,
    color: tokens.colorNeutralForeground2,
    minWidth: 0,
  },

  /** Section title text — semibold, small, uppercase for divider feel. */
  groupTitle: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    textTransform: 'uppercase',
    letterSpacing: '0.02em',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },

  /** Section count — subtle pill on the right end of the header. */
  groupCount: {
    flexShrink: 0,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    fontVariantNumeric: 'tabular-nums',
  },

  /**
   * Empty-section placeholder — small italic note so the user can always see
   * the section structure (Today/Tomorrow/Future) even when a bucket has no
   * items. Avoids the "section header floats alone" look.
   */
  groupEmpty: {
    ...shorthands.padding(
      tokens.spacingVerticalXS,
      tokens.spacingHorizontalM,
      tokens.spacingVerticalXS,
      tokens.spacingHorizontalM
    ),
    color: tokens.colorNeutralForeground3,
    fontStyle: 'italic',
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

  /**
   * Selected-card variant — brand-tinted border + subtle brand background
   * so the user sees which card the Open button will act on. Matches the
   * Code Page card-selection treatment (subtle brand emphasis, NOT a heavy
   * fill that competes with the card's own content). Inherits hover/focus
   * from the base `todoCard` rule above by repeating the relevant base
   * declarations — Griffel does not merge across classes.
   */
  todoCardSelected: {
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
    backgroundColor: tokens.colorBrandBackground2,
    ...shorthands.borderWidth('1px'),
    ...shorthands.borderStyle('solid'),
    ...shorthands.borderColor(tokens.colorBrandStroke1),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    boxShadow: tokens.shadow4,
    cursor: 'pointer',
    minWidth: 0,
    ':hover': {
      backgroundColor: tokens.colorBrandBackground2Hover,
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
