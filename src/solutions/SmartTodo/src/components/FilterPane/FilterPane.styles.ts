/**
 * FilterPane — styles (task 021, FR-06 / F-3 filter pane).
 *
 * All colors/borders are Fluent v9 semantic tokens per ADR-021 — zero hex/rgb
 * literals.
 *
 * @see ADR-021 Fluent UI v9 design system
 * @see projects/smart-todo-r5/spec.md FR-06 (F-3)
 */
import { makeStyles, shorthands, tokens } from '@fluentui/react-components';

export const useFilterPaneStyles = makeStyles({
  /** Root panel — visible state. Sits between `<Header>` and the Kanban surface. */
  rootOpen: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderBottom(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke1),
    ...shorthands.padding(
      tokens.spacingVerticalS,
      tokens.spacingHorizontalM,
      tokens.spacingVerticalM,
      tokens.spacingHorizontalM,
    ),
    flexShrink: 0,
  },

  /**
   * Root panel — closed state. Stays mounted (not unmounted) so the
   * Assigned-To typeahead's local query/results state survives a
   * close/reopen of the pane; `display: none` also removes it from the tab
   * order for free (NFR — keyboard nav test).
   */
  rootClosed: {
    display: 'none',
  },

  /** Top row — "Filter" label + "Clear all" action, left/right aligned. */
  headerRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },

  /** Vertical stack of Status checkboxes. */
  checkboxGroup: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },

  /** Assigned-To input + results-popover wrapper (positioning anchor). */
  assignedToContainer: {
    position: 'relative',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    maxWidth: '320px',
  },

  /** Assigned-To typeahead results list — floats below the input. */
  assignedToResultsList: {
    listStyleType: 'none',
    margin: 0,
    padding: 0,
    display: 'flex',
    flexDirection: 'column',
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.border(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke1),
    borderRadius: tokens.borderRadiusMedium,
    maxHeight: '200px',
    overflowY: 'auto',
    boxShadow: tokens.shadow8,
    zIndex: 10,
  },

  /** Single Assigned-To result row (native `<button>` for full keyboard operability). */
  assignedToResultItem: {
    display: 'block',
    width: '100%',
    textAlign: 'left',
    backgroundColor: 'transparent',
    color: tokens.colorNeutralForeground1,
    ...shorthands.border(0, 'none', 'transparent'),
    ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalS),
    cursor: 'pointer',
    fontFamily: tokens.fontFamilyBase,
    fontSize: tokens.fontSizeBase300,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
    ':focus-visible': {
      outlineStyle: 'solid',
      outlineWidth: tokens.strokeWidthThick,
      outlineColor: tokens.colorStrokeFocus2,
    },
  },
});
