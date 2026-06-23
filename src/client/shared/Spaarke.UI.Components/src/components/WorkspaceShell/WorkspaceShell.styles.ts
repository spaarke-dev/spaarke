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
   * UAT 2026-06-22 round 12: NOW SAFE to add `flex: 1 1 0, minHeight: 0,
   * alignItems: stretch`. The round 7 attempt failed because the parent
   * chain ABOVE the shell didn't have determinate height. Round 11
   * resolved that by adding `height: 100%` to WorkspaceLayoutWidget.root
   * (which is the block-parent's child). With the chain now propagating
   * height, the row's flex sizing can take effect — it claims the shell's
   * vertical space and stretches the SectionPanel grid cell to row height,
   * which is the final missing link from console diagnostics:
   *   1. WorkspaceLayoutWidget.root height:100% (round 11)
   *   2. WorkspaceShell.row flex:1 1 0 + alignItems:stretch (this fix)
   *   3. SmartTodoWidget.body display:flex (round 11)
   * Together they restore the full height chain to the kanban.
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
