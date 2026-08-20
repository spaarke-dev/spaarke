/**
 * Shared thin-scrollbar style fragment (spaarkeai-assistant-enhancements-r2
 * Phase 0 FIX 2).
 *
 * Before this file, the "thin, light-gray, theme-aware" scrollbar look was
 * duplicated inline across several `makeStyles` blocks (ConversationView,
 * DataGrid, WorkspaceShell, SprkModal/ModalScrollArea) with small drifts
 * between copies (different stroke tokens, different border radii). This is
 * the ONE canonical definition — new call sites should spread this object
 * into a `makeStyles` slot rather than re-declaring the scrollbar rules.
 *
 * Usage (Griffel/`makeStyles` allows spreading plain style objects):
 * ```ts
 * const useStyles = makeStyles({
 *   scrollArea: {
 *     overflowY: 'auto',
 *     ...thinScrollbarStyle,
 *   },
 * });
 * ```
 *
 * Semantic tokens only (ADR-021) — `colorNeutralStroke1` / `Stroke1Hover`
 * resolve to a light-gray thumb in light theme and an appropriate
 * (lighter-on-dark) thumb in dark theme automatically via the active Fluent
 * theme; no hardcoded hex, no light/dark branching needed here.
 *
 * `scrollbarWidth`/`scrollbarColor` cover Firefox; the `::-webkit-scrollbar*`
 * pseudo-elements cover Chromium/Edge/Safari. Track is transparent in both.
 */
import { tokens, type GriffelStyle } from '@fluentui/react-components';

export const thinScrollbarStyle: GriffelStyle = {
  scrollbarWidth: 'thin',
  scrollbarColor: `${tokens.colorNeutralStroke1} transparent`,
  '::-webkit-scrollbar': { width: '8px', height: '8px' },
  '::-webkit-scrollbar-track': { backgroundColor: 'transparent' },
  '::-webkit-scrollbar-thumb': {
    backgroundColor: tokens.colorNeutralStroke1,
    borderRadius: tokens.borderRadiusMedium,
  },
  '::-webkit-scrollbar-thumb:hover': { backgroundColor: tokens.colorNeutralStroke1Hover },
};

/**
 * Descendant variant of {@link thinScrollbarStyle}.
 *
 * `::-webkit-scrollbar` pseudo-elements do NOT cascade — they only style the
 * element they are declared on. So `thinScrollbarStyle` alone thins only the
 * one container it is spread into. Spread THIS object into an app / Code Page
 * ROOT slot instead to thin EVERY scrollable descendant (`& *`) in one place —
 * no need to find and annotate each nested scroll container (kanban columns,
 * dialogs, grids added later). Same semantic tokens, so it stays theme-aware
 * (light-gray thumb in light theme, lighter-on-dark in dark theme) with no
 * hardcoded hex.
 *
 * Prefer {@link thinScrollbarStyle} on a single known scroller; reach for this
 * only at a surface root where you want blanket coverage.
 *
 * Usage:
 * ```ts
 * const useStyles = makeStyles({
 *   page: { height: '100%', overflow: 'hidden', ...thinScrollbarDescendantStyle },
 * });
 * ```
 */
export const thinScrollbarDescendantStyle: GriffelStyle = {
  '& *': {
    scrollbarWidth: 'thin',
    scrollbarColor: `${tokens.colorNeutralStroke1} transparent`,
  },
  '& *::-webkit-scrollbar': { width: '8px', height: '8px' },
  '& *::-webkit-scrollbar-track': { backgroundColor: 'transparent' },
  '& *::-webkit-scrollbar-thumb': {
    backgroundColor: tokens.colorNeutralStroke1,
    borderRadius: tokens.borderRadiusMedium,
  },
  '& *::-webkit-scrollbar-thumb:hover': { backgroundColor: tokens.colorNeutralStroke1Hover },
};
