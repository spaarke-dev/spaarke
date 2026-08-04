/**
 * RichFilePreviewDialog — Modal wrapper around the `RichFilePreview` renderer,
 * composing the canonical `PreviewModal` (single document) / `BrowseModal`
 * (record-set "N of M" browse) presets from `SprkModal/presets` instead of a
 * hand-rolled `Dialog`/`DialogSurface` + `RecordNavigationModalShell` (spec
 * FR-15; spaarke-modal-system task 060, P4).
 *
 * Originally authored as the SemanticSearchControl PCF's `FilePreviewDialog.tsx`
 * for the `spaarke-matter-ui-enhancement-r1` project, then promoted to
 * `@spaarke/ui-components` so other Spaarke surfaces (LegalWorkspace,
 * DocumentRelationshipViewer, Office Add-ins, SpaarkeAi, Compose, Daily
 * Briefing, Communication components) can consume the same rich UX.
 *
 * R5 task 013 (D2-08) extracted the renderer core into `RichFilePreview.tsx`
 * so non-modal consumers (Context-pane widget, Workspace viewer widget) can
 * mount the renderer directly without modal chrome. R4 task 011
 * (smart-todo-r4) then refactored this wrapper to consume
 * `<RecordNavigationModalShell>` for cross-record navigation chrome, and P1
 * task 030 wired an interim `ModalWindowControls` cluster into both this
 * wrapper's non-nav path and `RichFilePreview` itself (before the `SprkModal`
 * presets existed).
 *
 * **Task 060 re-base**: this wrapper's public prop API
 * (`IFilePreviewDialogProps`) is UNCHANGED — every existing consumer
 * (LegalWorkspace, DocumentRelationshipViewer, SemanticSearchControl PCF,
 * CommunicationAttachments PCF, SpaarkeAi, Compose, Daily Briefing,
 * Communication.Components) continues to compile and render identically.
 * Internally, the hand-rolled `Dialog`/`DialogSurface`/`DialogActions` +
 * `RecordNavigationModalShell` + local maximize state are all REPLACED by
 * composing the `PreviewModal` (single document) / `BrowseModal` (record-set,
 * via its own `nav` prop — NOT a nested `RecordNavigationModalShell`; see
 * `BrowseModal`'s own docblock + `notes/wave-b-completion.md` for why)
 * presets. Both presets are thin `SprkModal` configs — `SprkModal` owns ALL
 * chrome (title, nav "N of M", `ModalWindowControls`, maximize/restore state,
 * footer Close) via its own header/footer, and sizes to `lg`
 * (`min(1280px,94vw) × min(85vh,880px)` — content-driven per
 * `record-modal-selection.md` Layout 2, NOT the OOB 85%×85% record-open
 * rectangle).
 *
 * The renderer (`RichFilePreview`) is mounted as the preset's stage
 * (`children`); it renders with `showTitle={false}` (the preset's header owns
 * the title) and `showMetadataPane={false}` (the preset's own `metadata` /
 * `PreviewGridBody` meta column owns the Tags+Details side panel — avoiding a
 * nested duplicate 320px panel). The renderer's fetch/loading/error/iframe
 * logic (`fetchPreviewUrl`) is 100% preserved, unchanged, per ADR-028
 * (function-passing, never snapshotted).
 *
 * The 3-dot title-bar menu (`DocumentRowMenu`, rendered by `RichFilePreview`)
 * still renders inside the stage cell (the renderer's own title bar collapses
 * to just the menu when `showTitle` + nav + window-controls are all
 * suppressed) — `PreviewModal`/`BrowseModal` do not currently expose a
 * `headerActions` passthrough to relocate it into the shell's own
 * header-right slot; see `notes/task-060-completion.md` for this preset-gap
 * report.
 *
 * @see ADR-012 - Shared component library
 * @see ADR-021 - Fluent UI v9 (semantic tokens, dark-mode parity)
 * @see ADR-022 - React 19 (no React 18-only APIs)
 * @see ADR-028 - Spaarke Auth Architecture (function-passing, not snapshotted)
 * @see spec.md (spaarke-modal-system) FR-15 — task 060 preset re-base
 * @see record-modal-selection.md Layout 2 — content-driven preview sizing
 */

import * as React from 'react';
import {
  RichFilePreview,
  formatDate,
  formatFileSize,
  nonEmpty,
  type IFilePreviewDialogSummary,
} from './RichFilePreview';
import { PreviewModal } from '../SprkModal/presets/PreviewModal';
import { BrowseModal } from '../SprkModal/presets/BrowseModal';
import type { PreviewModalMetadataItem } from '../SprkModal/presets/PreviewModal';

// ---------------------------------------------------------------------------
// Types — re-exported from the renderer to preserve back-compat for any
// consumer that imports the summary type from this module.
// ---------------------------------------------------------------------------

export type { IFilePreviewDialogSummary };

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

/**
 * `IFilePreviewDialogProps` — UNCHANGED by the task 060 preset re-base (and
 * unchanged since the R5 task 013 renderer extraction before it). All
 * existing consumers continue to compile and render identically with no prop
 * or behavior change.
 */
export interface IFilePreviewDialogProps {
  open: boolean;
  documentName: string;
  /** Stable document identifier — required for the 3-dot menu's aria-label. */
  documentId: string;
  /** Optional document type (label, e.g. "Contract"). Drives the Details "Type" row. */
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
   * 3-dot menu's `aiSummary` item visibility (hidden by default regardless).
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
   * `onNavigate`, the dialog renders via `BrowseModal` with `‹ N of M ›` in
   * its header (composing `SprkModal`'s own nav group — see docblock).
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
  // -----------------------------------------------------------------------
  // Navigation enablement + direction → index-delta adapter. The legacy
  // public API exposes `onNavigate(nextIndex)`; `BrowseModal`'s `nav`
  // contract is direction-based (`'prev' | 'next'`) — adapt here so the
  // public prop shape is unchanged for all existing consumers.
  // -----------------------------------------------------------------------

  const navEnabled =
    typeof navigationTotal === 'number' &&
    navigationTotal > 0 &&
    typeof currentIndex === 'number' &&
    typeof onNavigate === 'function';

  const handleNavigate = React.useCallback(
    (dir: 'prev' | 'next') => {
      if (!navEnabled || typeof currentIndex !== 'number' || !onNavigate) return;
      const nextIndex = dir === 'next' ? currentIndex + 1 : currentIndex - 1;
      onNavigate(nextIndex);
    },
    [navEnabled, currentIndex, onNavigate]
  );

  // Metadata rows for the preset's own `PreviewGridBody` meta column —
  // mirrors the renderer's own (now-suppressed, `showMetadataPane={false}`)
  // Details section, reusing its exact formatting helpers (composed, not
  // duplicated — CLAUDE.md §11).
  const metadata: PreviewModalMetadataItem[] = React.useMemo(
    () => [
      { label: 'Created by', value: nonEmpty(createdBy) },
      { label: 'Created', value: formatDate(createdAt) },
      { label: 'Size', value: formatFileSize(fileSize) },
      { label: 'Type', value: nonEmpty(documentType) },
    ],
    [createdBy, createdAt, fileSize, documentType]
  );

  // Stage content — the renderer, with its own title/nav/window-controls
  // chrome suppressed (the preset's `SprkModal` header owns all of that) and
  // its own metadata pane suppressed (the preset's `metadata` prop owns that
  // column instead). The fetch/loading/error/iframe logic + the 3-dot
  // `DocumentRowMenu` are 100% preserved, unchanged.
  const stage = (
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
      showTitle={false}
      showMetadataPane={false}
    />
  );

  if (navEnabled) {
    return (
      <BrowseModal
        open={open}
        onClose={onClose}
        title={documentName}
        metadata={metadata}
        nav={{
          index: currentIndex as number,
          total: navigationTotal as number,
          onNavigate: handleNavigate,
        }}
      >
        {stage}
      </BrowseModal>
    );
  }

  return (
    <PreviewModal open={open} onClose={onClose} title={documentName} metadata={metadata}>
      {stage}
    </PreviewModal>
  );
};

RichFilePreviewDialog.displayName = 'RichFilePreviewDialog';
