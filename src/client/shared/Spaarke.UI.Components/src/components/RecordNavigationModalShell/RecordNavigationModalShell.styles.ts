/**
 * Griffel styles for `RecordNavigationModalShell`.
 *
 * Semantic tokens only — no hard-coded colors, spacing, or radii (ADR-021).
 * `makeStyles` defined at module scope (per Fluent v9 component-authoring
 * pattern: style hooks must be stable across renders).
 */

import { makeStyles, shorthands, tokens } from '@fluentui/react-components';

export const useRecordNavigationModalShellStyles = makeStyles({
  /**
   * Root container — fills its parent (the caller's modal surface) and
   * arranges header / content vertically.
   */
  root: {
    display: 'flex',
    flexDirection: 'column',
    flex: 1,
    minHeight: 0,
    width: '100%',
    height: '100%',
    ...shorthands.overflow('hidden'),
    backgroundColor: tokens.colorNeutralBackground1,
  },

  /**
   * Header — title (left), nav controls + counter (center-right), action bar
   * (far right). Mirrors the RichFilePreview title-bar shape for visual
   * consistency.
   */
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalS,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
    gap: tokens.spacingHorizontalS,
  },

  /**
   * Title — ellipsizes on overflow so the action bar always stays visible.
   */
  title: {
    ...shorthands.overflow('hidden'),
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    flex: 1,
    minWidth: 0,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },

  /**
   * Right-side region of the header — nav group + divider + action bar slot.
   */
  headerActions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    flexShrink: 0,
  },

  /**
   * Nav group — `<` button + counter + `>` button. Tightly packed.
   */
  navGroup: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
  },

  /**
   * "N of M" counter — neutral foreground, tabular figures so the width
   * doesn't shift as the count changes.
   */
  navCounter: {
    color: tokens.colorNeutralForeground2,
    paddingLeft: tokens.spacingHorizontalXXS,
    paddingRight: tokens.spacingHorizontalXXS,
    fontVariantNumeric: 'tabular-nums',
  },

  /**
   * Vertical separator between the nav cluster and the action bar slot.
   */
  navDivider: {
    height: '20px',
    marginLeft: tokens.spacingHorizontalXS,
    marginRight: tokens.spacingHorizontalXS,
  },

  /**
   * Content area — fills remaining vertical space. Typically holds the
   * iframe pointing at the embedded MDA form (FR-13). `minHeight: 0` keeps
   * the flex child collapsible so the iframe can size to 100%.
   */
  content: {
    flex: 1,
    minHeight: 0,
    width: '100%',
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.overflow('hidden'),
  },
});
