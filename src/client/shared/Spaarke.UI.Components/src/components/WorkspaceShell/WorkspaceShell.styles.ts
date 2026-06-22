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
   * Column layout is supplied inline via `style.gridTemplateColumns`.
   *
   * NOTE: a UAT round 7 attempt to add `flex: 1 1 0, alignItems: stretch`
   * here collapsed the workspace to ~40px (only the tab bar visible) in
   * the SpaarkeAi embedded context because the parent shell's
   * `flex: 1 1 auto` couldn't resolve a determinate height through every
   * layer above. Reverted to default grid behaviour; per-section
   * `style: { height: ... }` remains the height-supply mechanism until a
   * deeper layout audit identifies the true break point above the shell.
   */
  row: {
    display: 'grid',
    gap: tokens.spacingHorizontalL,
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
