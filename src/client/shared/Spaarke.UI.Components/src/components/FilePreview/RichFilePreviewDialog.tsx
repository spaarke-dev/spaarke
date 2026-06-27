/**
 * RichFilePreviewDialog — Modal dialog wrapper around the `RichFilePreview`
 * renderer. The renderer hosts the iframe + 2-column body grid + metadata pane
 * + 3-dot menu; this wrapper supplies the Fluent v9 modal
 * `Dialog` / `DialogSurface` / `DialogActions` chrome and (as of R4 task 011)
 * delegates cross-record navigation + counter chrome to the shared
 * `<RecordNavigationModalShell>` from `@spaarke/ui-components`.
 *
 * Originally authored as the SemanticSearchControl PCF's `FilePreviewDialog.tsx`
 * for the `spaarke-matter-ui-enhancement-r1` project, then promoted to
 * `@spaarke/ui-components` so other Spaarke surfaces (LegalWorkspace,
 * DocumentRelationshipViewer, Office Add-ins) can consume the same rich UX.
 *
 * R5 task 013 (D2-08) extracted the renderer core into `RichFilePreview.tsx`
 * so non-modal consumers (Context-pane widget, Workspace viewer widget) can
 * mount the renderer directly without modal chrome. This wrapper's public
 * prop API (`IFilePreviewDialogProps`) is unchanged — all existing consumers
 * compile and render identically.
 *
 * R4 task 011 (smart-todo-r4) refactored this wrapper to consume
 * `<RecordNavigationModalShell>` for cross-record navigation chrome. The
 * shell is mounted INSIDE `<DialogSurface>` (the dialog envelope is still
 * owned here). The shell's `onNavigate(direction)` callback is adapted to
 * the legacy consumer-facing `onNavigate(nextIndex)` shape via a direction →
 * index-delta translation. Dirty-check is disabled (`dirtyCheckTargetWindow`
 * unset) — file preview is read-only with no unsaved-state concept.
 *
 * Coexistence note: the simpler `FilePreviewDialog` (services-injection API,
 * 880px, single column) at `./FilePreviewDialog.tsx` is retained for back-compat
 * with `FindSimilarResultsStep` and its downstream consumers. New surfaces
 * should prefer `RichFilePreviewDialog`.
 *
 * Layout: 1280px max-width × 85vh, 2-column body (1fr iframe | 320px metadata pane).
 *
 * Optional features (degrade gracefully when callbacks are omitted):
 *   - `onFetchSummary` — gates the `aiSummary` menu item (hidden by default)
 *   - `navigationTotal` + `currentIndex` + `onNavigate` — enables Prev/Next via the shell
 *   - `onFindSimilar` — enables `findSimilar` menu item
 *   - `onToggleWorkspace` + `isInWorkspace` — workspace flag (hidden in this dialog by default)
 *
 * The 3-dot title-bar menu is `DocumentRowMenu` (from this library), rendered
 * by `RichFilePreview`. Hidden by default: `preview`, `aiSummary`,
 * `toggleWorkspace`, `rename`.
 *
 * @see ADR-012 - Shared component library
 * @see ADR-021 - Fluent UI v9 (semantic tokens, dark-mode parity)
 * @see ADR-022 - React 19 (no React 18-only APIs)
 * @see spec.md (smart-todo-r4) FR-12, FR-13, FR-15 — task 011 refactor
 */

import * as React from 'react';
import {
  Dialog,
  DialogSurface,
  DialogActions,
  Button,
  makeStyles,
  shorthands,
  tokens,
} from '@fluentui/react-components';
import { RichFilePreview, type IFilePreviewDialogSummary } from './RichFilePreview';
import { RecordNavigationModalShell } from '../RecordNavigationModalShell';
import type { RecordNavigationDirection } from '../RecordNavigationModalShell';

// ---------------------------------------------------------------------------
// Types — re-exported from the renderer to preserve back-compat for any
// consumer that imports the summary type from this module.
// ---------------------------------------------------------------------------

export type { IFilePreviewDialogSummary };

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

/**
 * `IFilePreviewDialogProps` — unchanged from the pre-extraction component
 * (R5 task 013 D2-08) and unchanged by R4 task 011. All existing consumers
 * (LegalWorkspace, DocumentRelationshipViewer, SemanticSearchControl PCF)
 * continue to compile and render identically with no prop or behavior change.
 *
 * Internal mapping (R4 task 011):
 *   - `currentIndex` / `navigationTotal` flow directly to the shell.
 *   - `onNavigate(nextIndex)` is wrapped via a direction → delta adapter so
 *     callers retain their existing index-based callback shape.
 */
export interface IFilePreviewDialogProps {
  open: boolean;
  documentName: string;
  /** Stable document identifier — required for the 3-dot menu's aria-label. */
  documentId: string;
  /** Optional document type (label, e.g. "Contract"). Drives the Tag chip. */
  documentType?: string;
  /** Optional "Created by" display name for the Details section. */
  createdBy?: string | null;
  /** Optional ISO date string for the Details section "Created" row. */
  createdAt?: string | null;
  /** Optional file size in bytes for the Details section "Size" row. */
  fileSize?: number | null;
  onClose: () => void;
  /** Fetch the preview embed URL. Called when the dialog opens. */
  fetchPreviewUrl: () => Promise<string | null>;
  /**
   * Fetch the AI summary payload. Reserved for back-compat; the dialog no
   * longer renders the inline summary section but the prop still drives the
   * 3-dot menu's `aiSummary` item visibility (hidden when omitted).
   */
  onFetchSummary?: () => Promise<IFilePreviewDialogSummary>;
  /** Open the file in desktop or web app. */
  onOpenFile: (mode: 'desktop' | 'web') => void;
  /** Open the Dataverse record in a new tab. */
  onOpenRecord: () => void;
  /** Open the email document dialog. */
  onEmailDocument: () => void;
  /** Copy the document link to clipboard. */
  onCopyLink: () => void;
  /** Toggle workspace flag. */
  onToggleWorkspace?: () => void;
  /** Whether document is currently in workspace. */
  isInWorkspace?: boolean;
  /**
   * Open the "Find similar" surface for this document. When provided, the
   * `findSimilar` menu item is visible; when omitted, it is hidden.
   */
  onFindSimilar?: () => void;
  /**
   * Navigation set total. When provided alongside `currentIndex` +
   * `onNavigate`, the shell renders Prev/Next + "N of M" in its header.
   */
  navigationTotal?: number;
  /**
   * 0-based position of the currently-shown document inside the parent's
   * navigation set. Required when `navigationTotal` is supplied.
   */
  currentIndex?: number;
  /**
   * Navigate to a different document inside the parent's navigation set.
   * Legacy index-based shape — the dialog internally adapts the shell's
   * direction-based callback. The renderer resets its iframe-load state
   * automatically when `documentId` changes.
   */
  onNavigate?: (nextIndex: number) => void;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const DIALOG_MAX_WIDTH = '1280px';

// ---------------------------------------------------------------------------
// Styles — dialog-only chrome (surface clamp + footer). All renderer-internal
// styles moved to `RichFilePreview.tsx`. Shell chrome is owned by
// `@spaarke/ui-components` `<RecordNavigationModalShell>`.
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  surface: {
    // surface uses `width: 100%` + `maxWidth: 1280px` so it clamps gracefully
    // on smaller viewports (laptops below 1280 px wide) without horizontal
    // overflow. The renderer's 2-col grid uses `1fr 320px` so the iframe cell
    // always consumes the remaining horizontal space regardless of surface
    // width.
    width: '100%',
    maxWidth: DIALOG_MAX_WIDTH,
    height: '85vh',
    maxHeight: '85vh',
    ...shorthands.padding('0px'),
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.overflow('hidden'),
    ...shorthands.borderRadius(tokens.borderRadiusXLarge),
  },
  // Wrapper around the shell so it consumes the available height inside the
  // DialogSurface flex column. The shell itself is height-agnostic; this
  // wrapper gives it a flex context to grow into.
  shellWrap: {
    display: 'flex',
    flexDirection: 'column',
    flex: 1,
    minHeight: 0,
    width: '100%',
    ...shorthands.overflow('hidden'),
  },
  // Footer action bar — Close button only. Cross-record nav lives in the
  // shell's header chrome (consumed when `navigationTotal` is provided).
  footer: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'flex-end',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    borderTopWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const RichFilePreviewDialog: React.FC<IFilePreviewDialogProps> = ({
  open,
  documentName,
  documentId,
  documentType,
  createdBy,
  createdAt,
  fileSize,
  onClose,
  fetchPreviewUrl,
  onFetchSummary,
  onOpenFile,
  onOpenRecord,
  onEmailDocument,
  onCopyLink,
  onToggleWorkspace,
  isInWorkspace,
  onFindSimilar,
  navigationTotal,
  currentIndex,
  onNavigate,
}) => {
  const styles = useStyles();

  // -----------------------------------------------------------------------
  // Navigation enablement + direction → index-delta adapter (R4 task 011)
  //
  // The legacy public API exposes `onNavigate(nextIndex)`. The shared
  // `<RecordNavigationModalShell>` exposes `onNavigate(direction)`. We adapt
  // here so the public prop shape is unchanged for all existing consumers.
  // -----------------------------------------------------------------------

  const navEnabled =
    typeof navigationTotal === 'number' &&
    navigationTotal > 0 &&
    typeof currentIndex === 'number' &&
    typeof onNavigate === 'function';

  const handleShellNavigate = React.useCallback(
    (direction: RecordNavigationDirection) => {
      if (!navEnabled || typeof currentIndex !== 'number' || !onNavigate) return;
      const nextIndex = direction === 'next' ? currentIndex + 1 : currentIndex - 1;
      onNavigate(nextIndex);
    },
    [navEnabled, currentIndex, onNavigate]
  );

  return (
    <Dialog
      open={open}
      onOpenChange={(_, data) => {
        if (!data.open) onClose();
      }}
    >
      <DialogSurface className={styles.surface}>
        {/* Renderer is conditionally mounted only while `open` is true so the
            iframe-load state resets naturally on close (back-compat with the
            pre-extraction reset-on-close behavior).

            When cross-record navigation is enabled, the shared shell wraps
            the renderer and surfaces the prev/next + "N of M" chrome above
            the renderer's own title bar. Nav props are NOT forwarded to the
            renderer in that case — the shell owns the nav chrome to avoid
            double-rendering (the renderer's internal title-bar nav cluster
            is gated on `navigationTotal && currentIndex && onNavigate` ALL
            being defined, so omitting them suppresses it). The renderer's
            title text + 3-dot menu still render — they are the canonical
            document-context UX and are kept for back-compat. */}
        {open && navEnabled && (
          <div className={styles.shellWrap}>
            <RecordNavigationModalShell
              currentIndex={currentIndex as number}
              navigationTotal={navigationTotal as number}
              onNavigate={handleShellNavigate}
              title={documentName}
              dirtyCheckTargetWindow={undefined}
            >
              <RichFilePreview
                documentName={documentName}
                documentId={documentId}
                documentType={documentType}
                createdBy={createdBy}
                createdAt={createdAt}
                fileSize={fileSize}
                fetchPreviewUrl={fetchPreviewUrl}
                onFetchSummary={onFetchSummary}
                onOpenFile={onOpenFile}
                onOpenRecord={onOpenRecord}
                onEmailDocument={onEmailDocument}
                onCopyLink={onCopyLink}
                onToggleWorkspace={onToggleWorkspace}
                isInWorkspace={isInWorkspace}
                onFindSimilar={onFindSimilar}
                /* nav props deliberately omitted — shell owns nav chrome */
              />
            </RecordNavigationModalShell>
          </div>
        )}

        {/* Non-navigation path — single document, no shell needed. Renderer
            renders without its nav cluster (since nav props are undefined)
            and without shell chrome. Preserves the pre-task-011 visual for
            the dominant consumer (LegalWorkspace FilePreviewDialog, which
            does not pass nav props today). */}
        {open && !navEnabled && (
          <RichFilePreview
            documentName={documentName}
            documentId={documentId}
            documentType={documentType}
            createdBy={createdBy}
            createdAt={createdAt}
            fileSize={fileSize}
            fetchPreviewUrl={fetchPreviewUrl}
            onFetchSummary={onFetchSummary}
            onOpenFile={onOpenFile}
            onOpenRecord={onOpenRecord}
            onEmailDocument={onEmailDocument}
            onCopyLink={onCopyLink}
            onToggleWorkspace={onToggleWorkspace}
            isInWorkspace={isInWorkspace}
            onFindSimilar={onFindSimilar}
          />
        )}

        {/* Footer: Close only. Per task 011 carry-forward instruction, the
            Close button stays in DialogActions rather than moving into the
            shell's actionBar slot — preserves the existing footer chrome
            for all consumers. */}
        <DialogActions className={styles.footer}>
          <Button appearance="primary" onClick={onClose}>
            Close
          </Button>
        </DialogActions>
      </DialogSurface>
    </Dialog>
  );
};

RichFilePreviewDialog.displayName = 'RichFilePreviewDialog';
