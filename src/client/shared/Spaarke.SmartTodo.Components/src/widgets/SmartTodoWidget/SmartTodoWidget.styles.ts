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
 *   - Added `groupList` / `groupSection` / `groupHeader` / `groupTitle` /
 *     `groupCount` / `groupEmpty` (now legacy — superseded by R4-102 full
 *     `<SmartTodoKanban>` rendering; preserved for back-compat / no-regret
 *     removal in a future hoist task).
 *
 * R4 task 103 (E-2 — toolbar polish, 2026-06-18):
 *   - Reorganised `toolbar` to a left/spacer/right layout matching the app's
 *     R4-104 Header pattern. Left slot holds the `+` wizard button + inline
 *     QuickAdd (Input + Add btn). Right slot holds the action cluster (Open /
 *     Refresh / Orient / Search toggle).
 *   - Added `searchRow` — the expand-on-toggle SearchBox row that renders
 *     BELOW the toolbar when the search icon is active. Slides in / out via
 *     state (no animation lib — just conditional render). Carries its own
 *     border + padding so it visually nests with the toolbar.
 *   - Added `quickAddGroup` + `quickAddInput` for the inline quick-add layout
 *     (mirrors app Header pattern from R4-104).
 *   - Added `errorWithLink` — surfaces the "open full wizard" recovery link
 *     when quick-add fails (UAT 7).
 *   - Removed `searchWrap` flex-growth on the toolbar (search moved to its own
 *     expanded row); kept the class as a tighter wrapper for the expanded
 *     SearchBox.
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
   * Title row — sits ABOVE the toolbar (UAT 2026-06-19) for chrome uniformity
   * with the Code Page Header. Brand icon + "Smart To Do" text.
   *
   * Production hosts that already render their own section title (e.g.,
   * LegalWorkspace SectionPanel) can suppress this via the `showTitle={false}`
   * prop on `SmartTodoWidget`. Default shows the title for standalone /
   * harness consumption.
   */
  titleRow: {
    flexShrink: 0,
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    ...shorthands.padding(
      tokens.spacingVerticalS,
      tokens.spacingHorizontalM,
      tokens.spacingVerticalXS,
      tokens.spacingHorizontalM
    ),
    backgroundColor: tokens.colorNeutralBackground2,
  },

  /** Title text color/typography (matches the Code Page Header). */
  titleText: {
    color: tokens.colorNeutralForeground1,
    whiteSpace: 'nowrap',
  },

  /**
   * Sole chrome row — single `<Toolbar>` with left (wizard + QuickAdd),
   * spacer, right (action cluster + search icon toggle). R4-103 reorganised
   * from R4-099's [SearchBox left | actions right] layout.
   *
   * Layout: left group sits left, spacer grows, right group sits right.
   * No fixed widths so the QuickAdd input shrinks gracefully in narrow panes
   * (mobile-narrow layout compatibility per spec FR-04).
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
   * Left half of the toolbar (R4-103) — `[+ wizard] [QuickAdd Input] [Add btn]`.
   * Mirrors the app's R4-104 Header `quickAddGroup` pattern so widget + app
   * feel like one product. `minWidth: 0` lets the Input shrink in narrow panes.
   */
  toolbarLeft: {
    flex: '1 1 auto',
    minWidth: 0,
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalXS),
  },

  /** QuickAdd Title Input — grows to fill the left slot; min-width keeps it usable. */
  quickAddInput: {
    flex: '1 1 auto',
    minWidth: '120px',
  },

  /** UAT 2026-06-19 — Due Date input (native HTML date picker). */
  quickAddDateInput: {
    flexShrink: 0,
    minWidth: '130px',
    height: '24px',
    boxSizing: 'border-box',
    borderRadius: tokens.borderRadiusMedium,
    borderTopWidth: tokens.strokeWidthThin,
    borderRightWidth: tokens.strokeWidthThin,
    borderBottomWidth: tokens.strokeWidthThin,
    borderLeftWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderRightStyle: 'solid',
    borderBottomStyle: 'solid',
    borderLeftStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke1,
    borderRightColor: tokens.colorNeutralStroke1,
    borderBottomColor: tokens.colorNeutralStroke1,
    borderLeftColor: tokens.colorNeutralStroke1,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    fontSize: tokens.fontSizeBase200,
    fontFamily: tokens.fontFamilyBase,
    color: tokens.colorNeutralForeground1,
    backgroundColor: tokens.colorNeutralBackground1,
  },

  /** UAT 2026-06-19 — Assigned To Input — moderate width to display name. */
  quickAddAssignedInput: {
    flex: '0 1 220px',
    minWidth: '140px',
  },

  /**
   * UAT 2026-06-20 round 4 — Typeahead container for the inline Assigned To
   * input. Wraps the Input + the floating results dropdown so the dropdown
   * positions relative to the input.
   */
  assignedToWrap: {
    position: 'relative',
    flex: '0 1 220px',
    minWidth: '140px',
  },

  /** Results dropdown — absolutely positioned just below the Assigned To input. */
  assignedToResults: {
    position: 'absolute',
    top: '100%',
    left: 0,
    right: 0,
    zIndex: 50,
    marginTop: '2px',
    maxHeight: '220px',
    overflowY: 'auto',
    backgroundColor: tokens.colorNeutralBackground1,
    borderTopWidth: '1px',
    borderRightWidth: '1px',
    borderBottomWidth: '1px',
    borderLeftWidth: '1px',
    borderTopStyle: 'solid',
    borderRightStyle: 'solid',
    borderBottomStyle: 'solid',
    borderLeftStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    borderRightColor: tokens.colorNeutralStroke2,
    borderBottomColor: tokens.colorNeutralStroke2,
    borderLeftColor: tokens.colorNeutralStroke2,
    borderTopLeftRadius: tokens.borderRadiusMedium,
    borderTopRightRadius: tokens.borderRadiusMedium,
    borderBottomLeftRadius: tokens.borderRadiusMedium,
    borderBottomRightRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow8,
  },

  /** Each search result row in the Assigned To dropdown. */
  assignedToResultItem: {
    display: 'block',
    width: '100%',
    textAlign: 'left',
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalSNudge}`,
    backgroundColor: 'transparent',
    border: 'none',
    cursor: 'pointer',
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground1,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
    ':focus': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
      outlineStyle: 'none',
    },
  },

  /** Empty / no-results / loading state inside the Assigned To dropdown. */
  assignedToResultsHint: {
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalSNudge}`,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },

  /**
   * UAT 2026-06-19 — Inline filter SearchBox shown in the right toolbar
   * cluster when Filter is active. Takes the horizontal space that the
   * action buttons (Open/Refresh/Orient) normally occupy.
   */
  inlineFilterBox: {
    flex: '1 1 auto',
    minWidth: '180px',
    maxWidth: '320px',
  },

  /**
   * Spacer — flex grow with no content so the right group anchors to the
   * right edge regardless of left group content width.
   */
  toolbarSpacer: {
    flex: '0 1 auto',
    minWidth: tokens.spacingHorizontalS,
  },

  /**
   * Search row — appears BELOW the toolbar when the user toggles the search
   * icon ON. Conditionally rendered (not always present) so it doesn't reserve
   * vertical space when collapsed. Carries its own subtle border + padding so
   * it reads as a contiguous extension of the toolbar.
   */
  searchRow: {
    flexShrink: 0,
    display: 'flex',
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
    backgroundColor: tokens.colorNeutralBackground1,
  },

  /** Wrapper for the (now-expanded) SearchBox — grows to fill the row. */
  searchWrap: {
    flex: '1 1 auto',
    minWidth: 0,
    display: 'flex',
    alignItems: 'center',
  },

  /**
   * Right half of the toolbar — action button cluster + search toggle.
   * `flexShrink: 0` so buttons stay visible even when the left group compresses.
   */
  toolbarActions: {
    flexShrink: 0,
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalXS),
  },

  /**
   * Quick-add error row — surfaces graceful errors when Dataverse rejects
   * the title-only create (e.g., other required fields). The error text
   * carries a link "Open full wizard" that dispatches the full wizard via
   * `onAddTodo` so the user is never stuck. Sits below the toolbar / above
   * the body so it's visible without scrolling.
   */
  errorWithLink: {
    flexShrink: 0,
    ...shorthands.padding(
      tokens.spacingVerticalXS,
      tokens.spacingHorizontalM,
      tokens.spacingVerticalXS,
      tokens.spacingHorizontalM
    ),
  },

  /** Scrollable body — owns internal scroll for the cards list. */
  body: {
    flex: '1 1 auto',
    minHeight: 0,
    overflowY: 'auto',
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
  },

  /**
   * R4 task 102 (E-1, 2026-06-18) — container that hosts the full
   * `<SmartTodoKanban>`. Replaces the R4-101 `groupList` section stack with
   * a flex column that gives the Kanban board its own minimum height + lets
   * the inner `<KanbanBoard>` own the row/column flex layout.
   *
   * `minHeight: 0` is essential — without it the parent's `flex: 1 1 auto`
   * body wins the height race and the inner card list cannot scroll.
   *
   * 2026-06-19 fix #1: `flex: '1 1 0'` (NOT `1 1 auto`) — matches the Code Page
   * boardContainer. With `auto` flex-basis the kanban height was content-driven,
   * collapsed to 0 in nested flex chains. `flex: 1 1 0` is the canonical
   * "fill remaining height in a column-flex parent" idiom.
   *
   * 2026-06-19 fix #2: `minHeight: 'max(400px, 60vh)'` (NOT `0`) — even with
   * the flex fix, the kanban region's Griffel-shared `min-height: 0` class
   * collapsed the inner KanbanBoard when the widget's host pane had ambiguous
   * height resolution (LegalWorkspace SectionPanel, harness frames, etc.).
   *
   * `max(400px, 60vh)` is a FLEXIBLE floor:
   *   - Absolute minimum 400px (so the kanban never disappears even on tiny
   *     viewports or in broken host layouts)
   *   - Scales to 60% of viewport height on any reasonable screen
   *     (e.g., 540px on a 900px viewport, 648px on 1080p, 864px on 1440p)
   *   - Actual rendered height GROWS BEYOND this floor when the host
   *     provides more vertical space via flex: 1 1 0
   *
   * Discovered via spaarke-prototype/smart-todo-r4-uat harness: manually
   * overriding the Griffel `min-height: 0` class made cards appear. The Code
   * Page already gets enough height from its full-page host chrome
   * (~viewport - chrome ≈ 800px); the widget needs an explicit floor since
   * SectionPanel + variable host layouts don't guarantee height.
   */
  kanbanContainer: {
    display: 'flex',
    flexDirection: 'column',
    flex: '1 1 0',
    // UAT 2026-06-21 round 6: removed the artificial `minHeight: max(400px,
    // 60vh)` floor that was preventing pure responsive fill. The parent
    // chain (SectionPanel.card height → SectionPanel.content flex → widget
    // root height:100% → kanbanContainer flex:1 1 0) now correctly lets the
    // container size to the section's available space. The 400px floor is
    // kept only as a hard minimum so the kanban never collapses to nothing.
    minHeight: '400px',
    minWidth: 0,
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
