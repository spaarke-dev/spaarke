/**
 * RichFilePreviewDialog — Modal dialog wrapper around the `RichFilePreview`
 * renderer. The renderer hosts the iframe + 2-column body grid + metadata pane
 * + Prev/Next nav + 3-dot menu; this wrapper supplies the Fluent v9 modal
 * `Dialog` / `DialogSurface` / `DialogActions` chrome.
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
 * Coexistence note: the simpler `FilePreviewDialog` (services-injection API,
 * 880px, single column) at `./FilePreviewDialog.tsx` is retained for back-compat
 * with `FindSimilarResultsStep` and its downstream consumers. New surfaces
 * should prefer `RichFilePreviewDialog`.
 *
 * Layout: 1280px max-width × 85vh, 2-column body (1fr iframe | 320px metadata pane).
 *
 * Optional features (degrade gracefully when callbacks are omitted):
 *   - `onFetchSummary` — gates the `aiSummary` menu item (hidden by default)
 *   - `navigationTotal` + `currentIndex` + `onNavigate` — enables Prev/Next in title bar
 *   - `onFindSimilar` — enables `findSimilar` menu item
 *   - `onToggleWorkspace` + `isInWorkspace` — workspace flag (hidden in this dialog by default)
 *
 * The 3-dot title-bar menu is `DocumentRowMenu` (from this library). Hidden by
 * default: `preview` (the dialog IS the preview), `aiSummary`, `toggleWorkspace`,
 * `rename` (no handler at most surfaces).
 *
 * @see ADR-012 - Shared component library
 * @see ADR-021 - Fluent UI v9 (semantic tokens, dark-mode parity)
 * @see ADR-022 - React 19 (no React 18-only APIs)
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
 * (R5 task 013 D2-08). All existing consumers (LegalWorkspace,
 * DocumentRelationshipViewer, SemanticSearchControl PCF) continue to compile
 * and render identically with no prop or behavior change.
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
   * `onNavigate`, the title bar renders Prev/Next + "N of M".
   */
  navigationTotal?: number;
  /**
   * 0-based position of the currently-shown document inside the parent's
   * navigation set. Required when `navigationTotal` is supplied.
   */
  currentIndex?: number;
  /**
   * Navigate to a different document inside the parent's navigation set.
   * The renderer resets its iframe-load state automatically when `documentId`
   * changes.
   */
  onNavigate?: (nextIndex: number) => void;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const DIALOG_MAX_WIDTH = '1280px';

// ---------------------------------------------------------------------------
// Styles — dialog-only chrome (surface clamp + footer). All renderer-internal
// styles moved to `RichFilePreview.tsx`.
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
  // Footer action bar — Close button only. Prev/Next nav lives inside the
  // renderer's title bar (right side, before the 3-dot menu).
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
            pre-extraction reset-on-close behavior). */}
        {open && (
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
            navigationTotal={navigationTotal}
            currentIndex={currentIndex}
            onNavigate={onNavigate}
          />
        )}

        {/* Footer: Close only. Prev/Next nav lives in the renderer's title bar
            (right side, before the 3-dot menu) so document navigation sits
            adjacent to the document-context actions. */}
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
