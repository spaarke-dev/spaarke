/**
 * SmartTodo Header — styles for the SINGLE-ROW consolidated toolbar
 * (R4-104 / Wave E-3 — 2026-06-18).
 *
 * COLLAPSED from the prior R4-030 4-row layout (titleRow + searchRow +
 * filterRow + toolbarRow) to a single Fluent v9 `<Toolbar>` row mirroring
 * the SmartTodoWidget chrome (R4-099). All hard-coded colors and inline
 * styles are forbidden per ADR-021 — every visual property below is a
 * Fluent v9 semantic token.
 *
 * Layout zones (left → right within the single row):
 *   - `titleGroup`     : Microsoft To Do icon + "Smart To Do" text
 *   - `quickAddGroup`  : compact Input + Add button + optional "+ New" wizard
 *   - `spacer`         : flex-grow gap that pushes the right cluster to the edge
 *   - `rightGroup`     : selection-aware actions (when count > 0) OR default
 *                        action cluster (Refresh, ViewToggle, OrientationToggle,
 *                        Settings, SearchBox)
 *
 * @see ADR-021 Fluent UI v9 design system
 * @see R4-104 audit notes/e-widget-app-parity-audit-2026-06-18.md
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
   * QuickAdd cluster — Title + Due Date + Assigned To + Add button.
   * UAT 2026-06-19: maxWidth raised + flex-grow on so the three fields
   * have generous room (matches widget chrome). Title field gets the
   * lion's share via flex; Due Date stays content-sized; Assigned To
   * gets a moderate min/preferred width.
   */
  quickAddGroup: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flex: '1 1 auto',
    minWidth: 0,
    // No maxWidth — let the right cluster shrink first via its own flex sizing.
  },

  /**
   * QuickAdd Input — flex-grow within the QuickAdd cluster. `width: '100%'`
   * is required because `<Input>` from Fluent v9 has a default intrinsic
   * width that wouldn't fill the slot otherwise.
   */
  quickAddInput: {
    flex: '1 1 auto',
    minWidth: 0,
    width: '100%',
  },

  /** UAT 2026-06-19 — Due Date native HTML input. */
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

  /** UAT 2026-06-19 — Assigned To input — moderate width; same as widget. */
  quickAddAssignedInput: {
    flex: '0 1 220px',
    minWidth: '140px',
  },

  /**
   * UAT 2026-06-20 round 4 — typeahead wrapper + dropdown for the Assigned
   * To field. Mirrors the widget's `assignedToWrap` / `assignedToResults` /
   * `assignedToResultItem` styles so the two surfaces look identical.
   */
  assignedToWrap: {
    position: 'relative',
    flex: '0 1 220px',
    minWidth: '140px',
  },

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

  assignedToResultsHint: {
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalSNudge}`,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },

  /**
   * Flex spacer — pushes the right cluster to the trailing edge of the row.
   * `flex: '1 1 0'` (vs auto) means it absorbs leftover space without
   * fighting the QuickAdd for room.
   */
  spacer: {
    flex: '1 1 0',
    minWidth: 0,
  },

  /**
   * Right cluster — either the selection-aware toolbar (when count > 0) or
   * the default action cluster (Refresh / ViewToggle / OrientationToggle /
   * Settings / SearchBox). `flexShrink: 0` so buttons stay visible even
   * when the QuickAdd input compresses.
   */
  rightGroup: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    flexShrink: 0,
  },

  /**
   * SearchBox wrapper — fixed-ish width so the search input has predictable
   * sizing without stealing space from the action buttons. `minWidth: 0` is
   * defensive for very narrow viewports.
   */
  searchWrap: {
    minWidth: 0,
    maxWidth: '220px',
    display: 'flex',
    alignItems: 'center',
  },
});
