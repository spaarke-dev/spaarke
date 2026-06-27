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
   * Sizing model (post R4-110 follow-up, 2026-06-23):
   *   - `flex: 1 1 0` — when the shell has surplus space beyond what the
   *     rows need, distribute it equally across rows.
   *   - NO `minHeight: 0` — the implicit `min-height: auto` for flex items
   *     means the row CANNOT shrink below its content's intrinsic minimum.
   *     If a section inside the row has `minHeight: 560`, the row honors
   *     that floor; the shell's `overflow: auto` handles the case where
   *     total content > viewport by scrolling.
   *   - `alignItems: stretch` — grid items (SectionPanel cards) stretch
   *     to fill the row's track height (CSS grid default for cross axis).
   *
   * History:
   *   - Round 12 (2026-06-22) originally added `minHeight: 0` to enable
   *     flex distribution. That worked for SmartTodo because the section
   *     also set inline `height: calc(100vh - 200px)`, which forced a
   *     specific row height that grid had to honor.
   *   - R4-110 (2026-06-23) removed the calc and replaced with just
   *     `minHeight: 560`, exposing the latent bug: with row's `minHeight: 0`
   *     + section's `minHeight: 560`, the row stayed at flex-share size
   *     while the section content overflowed visually into adjacent rows
   *     (multi-widget dashboard overlap).
   *   - R4-110 follow-up (this fix) removes the `minHeight: 0` so the
   *     row's content-min pushes back. flex distribution still works for
   *     surplus space; content overflow is properly handled by the shell's
   *     overflow: auto.
   */
  row: {
    display: 'grid',
    gap: tokens.spacingHorizontalL,
    flex: '1 1 0',
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
