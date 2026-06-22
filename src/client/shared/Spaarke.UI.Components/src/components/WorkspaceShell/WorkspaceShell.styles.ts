/**
 * WorkspaceShell.styles.ts — Shared Griffel styles for the WorkspaceShell layout.
 *
 * Extracted to a separate file so WorkspaceShell.tsx stays focused on
 * rendering logic. Consumers can import individual style hooks if needed.
 *
 * Standards: ADR-021 (Fluent v9 tokens, no hard-coded colors, dark mode)
 */

import { makeStyles, tokens } from '@fluentui/react-components';

/**
 * Styles for the WorkspaceShell outer container and row layout.
 */
export const useWorkspaceShellStyles = makeStyles({
  /**
   * Outer shell container — vertical flex column with gap between rows.
   * Scrolls vertically when content exceeds available height.
   */
  shell: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXL,
    flex: '1 1 auto',
    minHeight: 0,
    overflow: 'auto',
  },

  /**
   * A grid row within the shell.
   *
   * Column layout is supplied inline via `style.gridTemplateColumns`.
   *
   * smart-todo-r4 UAT round 7 (2026-06-21): the row now claims available
   * vertical space from the flex `shell` via `flex: 1 1 0` (with `minHeight:
   * 0` so it can shrink in tight viewports). `alignItems: stretch` is the
   * grid default but expressed explicitly so SectionPanel cards stretch to
   * the row's height — combined with `SectionPanel.card { height: 100% }`,
   * this establishes the height contract from viewport → pane → row → card
   * → widget without per-section inline `style: { height: "560px" }` hacks.
   *
   * For multi-row workspaces, all rows share the shell's vertical space
   * equally (each gets `flex-basis: 0`); to size a row to content instead,
   * pass `style: { flex: '0 0 auto' }` on the row config.
   */
  row: {
    display: 'grid',
    gap: tokens.spacingHorizontalL,
    flex: '1 1 0',
    minHeight: 0,
    alignItems: 'stretch',
  },
});

/**
 * Styles for content padding inside a SectionPanel.
 * Used when card rows or other content need standard interior spacing.
 */
export const useSectionContentPaddingStyles = makeStyles({
  padded: {
    padding: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalM,
  },
});

/**
 * Toolbar divider — a thin vertical line separator between toolbar button groups.
 */
export const useToolbarDividerStyles = makeStyles({
  divider: {
    width: '1px',
    height: '20px',
    backgroundColor: tokens.colorNeutralStroke2,
    marginLeft: tokens.spacingHorizontalXS,
    marginRight: tokens.spacingHorizontalXS,
    flexShrink: 0,
  },
});
