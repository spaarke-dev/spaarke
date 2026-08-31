/**
 * SearchFilter — styles (smart-todo-r5 UAT 2026-08-17 pass 2 — inline
 * expanding search, to the left of Header's Filter pill on the toolbar row).
 *
 * Pass 1 rendered this as its own bordered/padded bar UNDER the top bar.
 * Pass 2 moves it inline INTO `Header.tsx`'s toolbar `rightGroup`, so the
 * bordered-bar chrome (background, bottom border, bar padding) is dropped —
 * the box now just sits as a flex sibling of the Filter / + New Task / ⋮
 * cluster. The "Search" caption `label` style is also removed (the label
 * itself was dropped from `SearchFilter.tsx`).
 *
 * All colors/borders are Fluent v9 semantic tokens per ADR-021 — zero hex/rgb
 * literals.
 *
 * @see ADR-021 Fluent UI v9 design system
 * @see projects/smart-todo-r5/notes/uat-filter-text-search.md
 */
import { makeStyles, tokens } from '@fluentui/react-components';

export const useSearchFilterStyles = makeStyles({
  /**
   * Root — visible state. Flex sibling of the Filter pill inside Header's
   * `rightGroup`; sits immediately to its left, "expanding" into view when
   * the Filter pill is toggled on.
   */
  rootOpen: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    flexShrink: 0,
    marginRight: tokens.spacingHorizontalXS,
  },

  /**
   * Root — closed state. Stays mounted (not unmounted) so the search text
   * survives a close/reopen of the box (matches the prior bordered-bar's
   * stay-mounted precedent); `display: none` also removes it from the tab
   * order for free (NFR — keyboard nav test).
   */
  rootClosed: {
    display: 'none',
  },

  /** The search input's inline width when expanded — sized to sit comfortably
   * beside the Filter / + New Task / ⋮ cluster without crowding it. */
  searchBox: {
    width: '240px',
    maxWidth: '240px',
    flexShrink: 0,
  },
});
