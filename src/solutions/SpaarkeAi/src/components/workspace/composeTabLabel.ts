/**
 * composeTabLabel.ts — derive a Compose tab's short display label + full
 * tooltip from the loaded document's filename (spaarkeai-assistant-
 * enhancements-r2, Phase 0 Fix 2).
 *
 * PROBLEM: every Compose `widget_load` dispatch previously hard-coded
 * `displayName: "Compose"`, so a session with several open documents (each
 * its own Compose tab, per the instance-keyed reuse in WorkspacePane's
 * `'compose'` branch) showed multiple tabs all labelled "Compose" —
 * indistinguishable in the tab strip.
 *
 * FIX: derive the tab label from the document's filename (stripped of its
 * extension, truncated to a short glanceable prefix) and carry the FULL
 * filename as a tooltip so the complete name is still one hover away. The
 * server-readable `widgetData.filename` contract (R3) is untouched — this
 * module only shapes the UI label, never the seed payload.
 *
 * Pure, side-effect-free, and independently unit-testable — no React, no
 * PaneEventBus coupling.
 *
 * @see WorkspacePane.tsx — the compose `widget_load` branch's NEW-tab
 *      creation call site, the single point where every compose open
 *      (upload / draft / stored-doc / ribbon-launch) funnels through after
 *      `seedFilename` resolution. Centralizing here means every entry path
 *      gets the derived label for free — no per-dispatch-site changes needed.
 * @see WorkspaceTabManager.ts — `WorkspaceTab.tooltip` (optional, display-only)
 */

/** Short glanceable prefix length before the ellipsis (chars, extension-stripped). */
export const COMPOSE_TAB_LABEL_MAX_LEN = 8;

/** Result of deriving a Compose tab's label. */
export interface ComposeTabLabel {
  /** Short tab-strip label — the truncated filename, or the fallback when no filename is known. */
  displayName: string;
  /** Full filename for the tab's hover tooltip. Omitted when there is no filename (fallback case). */
  tooltip?: string;
}

/**
 * Strip a trailing file extension (`.docx`, `.pdf`, …) from a filename.
 *
 * Conservative: only strips when the trailing segment after the last `.`
 * looks like a plausible extension (1–6 chars, no spaces) and the `.` is not
 * the first character (dotfiles like `.gitignore` are left alone — not a
 * realistic Compose filename, but a safe guard regardless).
 */
function stripExtension(name: string): string {
  const dotIdx = name.lastIndexOf('.');
  if (dotIdx <= 0) return name;
  const ext = name.slice(dotIdx + 1);
  if (ext.length === 0 || ext.length > 6 || /\s/.test(ext)) return name;
  return name.slice(0, dotIdx);
}

/**
 * Truncate a (typically extension-stripped) filename to a short glanceable
 * prefix. Names at or under `maxLen` are returned unchanged (no ellipsis —
 * nothing was actually cut). Longer names are cut to `maxLen` characters,
 * trailing separator characters (space/hyphen/underscore) are trimmed so the
 * result doesn't dangle on a stray `-` or `_`, and an ellipsis is appended.
 *
 * @example
 * truncateComposeFileName('Corteva-NDA-August 2022_Signed') // → 'Corteva…'
 * truncateComposeFileName('NDA') // → 'NDA' (already short — no ellipsis)
 */
export function truncateComposeFileName(name: string, maxLen: number = COMPOSE_TAB_LABEL_MAX_LEN): string {
  const trimmed = name.trim();
  if (trimmed.length <= maxLen) return trimmed;

  let cut = trimmed.slice(0, maxLen);
  cut = cut.replace(/[\s\-_]+$/, '');
  if (cut.length === 0) {
    // The first maxLen chars were entirely separators (pathological input) —
    // fall back to the raw (untrimmed-of-separators) slice rather than an
    // empty label.
    cut = trimmed.slice(0, maxLen);
  }
  return `${cut}…`; // …
}

/**
 * Derive a Compose tab's `{ displayName, tooltip }` from the loaded
 * document's filename.
 *
 * - A present, non-blank filename → extension-stripped, truncated
 *   `displayName` + the ORIGINAL (untruncated, WITH extension) filename as
 *   `tooltip`.
 * - No filename (blank Compose open, e.g. the Workspaces-menu "Compose"
 *   selection or the welcome-card blank open) → `{ displayName: fallback }`,
 *   no tooltip (the tab label already says everything there is to say).
 *
 * @param fileName - The resolved document filename, or null/undefined for a
 *                    blank/seedless Compose open.
 * @param fallback  - Label to use when there is no filename. Defaults to
 *                    `'Compose'` (the pre-existing hard-coded label).
 */
export function deriveComposeTabLabel(fileName: string | null | undefined, fallback = 'Compose'): ComposeTabLabel {
  const trimmed = (fileName ?? '').trim();
  if (trimmed.length === 0) {
    return { displayName: fallback };
  }
  return {
    displayName: truncateComposeFileName(stripExtension(trimmed)),
    tooltip: trimmed,
  };
}
