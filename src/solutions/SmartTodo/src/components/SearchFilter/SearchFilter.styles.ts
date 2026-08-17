/**
 * SearchFilter — styles (smart-todo-r5 UAT 2026-08-17 — expanding text search).
 *
 * All colors/borders are Fluent v9 semantic tokens per ADR-021 — zero hex/rgb
 * literals.
 *
 * @see ADR-021 Fluent UI v9 design system
 * @see projects/smart-todo-r5/notes/uat-filter-text-search.md
 */
import { makeStyles, shorthands, tokens } from '@fluentui/react-components';

export const useSearchFilterStyles = makeStyles({
  /** Root bar — visible state. Sits between `<Header>` and the Kanban surface. */
  rootOpen: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderBottom(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke1),
    ...shorthands.padding(
      tokens.spacingVerticalS,
      tokens.spacingHorizontalM,
      tokens.spacingVerticalS,
      tokens.spacingHorizontalM,
    ),
    flexShrink: 0,
  },

  /**
   * Root bar — closed state. Stays mounted (not unmounted) so the search
   * text survives a close/reopen of the bar (matches the structured
   * FilterPane's stay-mounted precedent); `display: none` also removes it
   * from the tab order for free (NFR — keyboard nav test).
   */
  rootClosed: {
    display: 'none',
  },

  /** The search input grows to fill the bar; capped so it doesn't sprawl on wide viewports. */
  searchBox: {
    maxWidth: '480px',
    width: '100%',
  },

  /** "Search" caption to the left of the input — matches the old FilterPane header-row label style. */
  label: {
    flexShrink: 0,
    marginRight: tokens.spacingHorizontalS,
  },
});
