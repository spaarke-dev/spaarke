/**
 * SmartTodo Header — styles (Griffel makeStyles + Fluent v9 semantic tokens).
 *
 * Per ADR-021: no hard-coded colors, no inline `style={}` props, no CSS modules.
 * Visual reference: `SemanticSearchControl.tsx` `header`/`titleRow`/`searchRow`
 * rules (R4 FR-06 — pixel-comparable 4-row layout).
 *
 * @see ADR-021 Fluent UI v9 design system
 * @see smart-todo-r4 spec FR-06
 */
import { makeStyles, shorthands, tokens } from '@fluentui/react-components';

export const useHeaderStyles = makeStyles({
  /**
   * Header container — vertical stack of the 4 rows (title + search/actions +
   * filter bar + selection-aware toolbar slot). Matches SemanticSearchControl's
   * `styles.header` rule (column flex, padded, subtle bottom border).
   */
  root: {
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
   * Row 1 — Page title row. Matches SemanticSearchControl `titleRow`:
   * <Text size={300}> aligned left, modest bottom spacing.
   */
  titleRow: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.padding(
      tokens.spacingVerticalS,
      tokens.spacingHorizontalM,
      '0',
      tokens.spacingHorizontalM,
    ),
  },

  /**
   * Row 2 — Search + Refresh + "+ New". Matches SemanticSearchControl
   * `searchRow`: flex, search wraps grow, action icons trail.
   */
  searchRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    ...shorthands.padding(
      tokens.spacingVerticalS,
      tokens.spacingHorizontalM,
    ),
  },

  /** Search box wrapper — flex-grows so the SearchBox fills available width. */
  searchInputWrap: {
    flexGrow: 1,
    minWidth: 0,
  },

  /** Inline icon buttons in Row 2 — match SemanticSearchControl visual. */
  inlineToolbarButton: {
    minWidth: 'auto',
    ...shorthands.padding('0px'),
  },

  /**
   * Row 3 — Filter bar (facets + Clear). Matches SemanticSearchControl
   * `emptyStateToolbar`: subtle bottom border, medium horizontal gap.
   * When there are no active filters and no Clear shown, the row collapses
   * to its empty padding so the layout stays a stable 4-row hierarchy
   * (FR-06 — 4 rows present even when populated lazily).
   */
  filterRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    minHeight: '36px',
    ...shorthands.padding(
      tokens.spacingVerticalXS,
      tokens.spacingHorizontalM,
    ),
    ...shorthands.borderTop(
      tokens.strokeWidthThin,
      'solid',
      tokens.colorNeutralStroke2,
    ),
  },

  /** Facets pill group — wraps to a new line on narrow screens. */
  facetGroup: {
    display: 'flex',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalXS,
    flexGrow: 1,
    minWidth: 0,
  },

  /**
   * Row 4 — Selection-aware toolbar slot. The slot itself has zero padding;
   * `<SelectionAwareToolbar>` brings its own subtle background + border per
   * its own Griffel styles. When `selectedCount === 0` the primitive renders
   * `null`, which leaves Row 4 visually collapsed (FR-06 — selection-aware).
   */
  toolbarRow: {
    display: 'flex',
    flexDirection: 'column',
  },
});
