/**
 * BulkActionBar (FR-DOC-02)
 *
 * Bulk-action affordances for the Documents PCF. v1.1.45 refactored the bar
 * to an INLINE icon-only mode so the same buttons can live in the list's
 * top-right toolbar adjacent to the existing Reload + Add affordances
 * (UAT request — bulk actions on the same row as the toolbar, no separate
 * sticky bar). Six bulk actions per spec FR-DOC-02:
 *
 *   1. Email selected      — open multi-doc email composer (DocumentEmailWizard)
 *   2. Download selected   — POST /api/documents/bulk-download → zip stream
 *   3. Pin selected        — set pinned: true in localStorage for each id
 *   4. Delete selected     — Fluent v9 Dialog confirmation → Xrm.WebApi delete
 *   5. Document Type →     — sub-menu of `sprk_documenttype` options;
 *      optimistic UI + 5-second Undo toast
 *   6. Share link          — open mailto: composer with one
 *      `{DocName} → {DataverseRecordURL}` line per selected document
 *
 * Behavior:
 *   - Hidden when `selectedIds.size === 0`
 *   - "Clear" affordance (Dismiss icon) resets the selection
 *   - Each button is icon-only (Fluent v9 `*20Regular` icons matching the
 *     existing Reload `ArrowClockwise20Regular` / Add `Add20Regular`
 *     glyph size) wrapped in a `Tooltip relationship="label"` so screen
 *     readers + hover both get a usable name.
 *   - Each action surfaces successes/failures via the parent-provided toaster
 *     (parent passes `dispatchToast`-friendly callbacks).
 *
 * Standards:
 *   - ADR-006  PCF UI surface
 *   - ADR-012  Reuses shared `DocumentEmailWizard` (consumed by parent)
 *   - ADR-021  Fluent v9 semantic tokens only; dark-mode safe
 *   - ADR-022  React 16/17-safe — no React 18+ exclusive APIs
 *   - ADR-028  All BFF calls go through @spaarke/auth `authenticatedFetch`
 *              (the parent passes the function in as the `bulkDownload`
 *              callback — see SemanticSearchControl.tsx).
 *
 * @see spec.md FR-DOC-02
 */

import * as React from 'react';
import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Menu,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  Spinner,
  Text,
  Tooltip,
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
} from '@fluentui/react-components';
import {
  ArrowDownload20Regular,
  Delete20Regular,
  DocumentBulletList20Regular,
  Link20Regular,
  Mail20Regular,
  Pin20Regular,
  Share20Regular,
} from '@fluentui/react-icons';
import type { TagFilterOption } from '@spaarke/ui-components/dist/types/TagFilter';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IBulkActionBarProps {
  /** Currently-selected document IDs (parent-owned). The bar hides at 0. */
  selectedIds: ReadonlySet<string>;

  /** Document Type option set (sprk_documenttype) for the "Document Type → selected" sub-menu. */
  docTypeOptions: ReadonlyArray<TagFilterOption>;

  /** Reset selection — wired to a Clear affordance in the bar. */
  onClear: () => void;

  /** Open multi-doc email composer for the selected set. */
  onEmail: () => void;

  /**
   * Download selected as a zip.
   *
   * Returns a Promise that resolves on success and rejects with an Error
   * whose message is suitable for surfacing in an error toast. The Bar
   * shows a Spinner on the Download button while the promise is pending.
   */
  onDownload: () => Promise<void>;

  /** Pin every selected id (localStorage write — no Dataverse call). */
  onPin: () => void;

  /** Delete every selected id (Xrm.WebApi soft-delete). Called AFTER user
   *  confirms the Bar's confirmation Dialog — caller does not need to
   *  prompt again. Returns a Promise so the Bar can show feedback. */
  onDelete: () => Promise<void>;

  /** Apply a new document type to every selected id. The Bar handles the
   *  optimistic-UI + 5s Undo toast via the parent-provided `onDocTypeChange`
   *  callback (parent does the actual Xrm.WebApi updates and Undo restore). */
  onDocTypeChange: (newType: string) => void;

  /** Build the email body containing one `{DocName} → {DataverseRecordURL}` line
   *  per selected doc and open the mailto: composer. */
  onShareLink: () => void;

  /** Optional className override (Spaarke standard slot-pass-through). */
  className?: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  // Inline icon-only bar — composed into the list view's top-right toolbar
  // (v1.1.45). Bare flex row, no background, no border, no sticky positioning;
  // the parent toolbar owns the surrounding chrome. The row groups the six
  // bulk-action icons + the selection-count text + a leading separator.
  //
  // v1.1.55 — `gap` widened from XS → M per UAT: "for the selected tool bar,
  // increase the space between the tool icons". Provides a more comfortable
  // hit target spacing without breaking the inline-toolbar composition.
  root: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    minHeight: '32px',
    boxSizing: 'border-box',
  },
  // Selection-count label (e.g. "3 selected"). Sits before the action icons.
  // v1.1.55 — paddingRight removed; the new leading `divider` (rendered between
  // countText and the first icon) carries the visual separation now.
  countText: {
    color: tokens.colorNeutralForeground2,
  },
  // Tight icon-button override — match the existing Reload/Add toolbar size.
  iconButton: {
    minWidth: 'auto',
    ...shorthands.padding('0px'),
  },
  // Vertical hairline separator. v1.1.55 — now used as a LEADING separator
  // between the selection-count text and the first action icon (UAT request:
  // "add subtle separator line between number of document selected and the
  // tool bar icons"). Token-driven so dark mode is automatic.
  divider: {
    width: '1px',
    alignSelf: 'stretch',
    backgroundColor: tokens.colorNeutralStroke2,
    marginTop: tokens.spacingVerticalXXS,
    marginBottom: tokens.spacingVerticalXXS,
  },
  // Confirmation dialog body — unchanged from v1.1.44.
  dialogBody: {
    color: tokens.colorNeutralForeground1,
  },
  menuList: {
    minWidth: '180px',
    maxWidth: '320px',
    maxHeight: '360px',
    overflowY: 'auto',
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const BulkActionBar: React.FC<IBulkActionBarProps> = ({
  selectedIds,
  docTypeOptions,
  // v1.1.49 — `onClear` is no longer surfaced as a visible Dismiss button
  // (UAT Item 4) but the prop stays in the contract for back-compat with
  // any caller (or future affordance) that wants to drive a programmatic
  // clear. We accept + ignore here; intentionally unused.
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  onClear,
  onEmail,
  onDownload,
  onPin,
  onDelete,
  onDocTypeChange,
  onShareLink,
  className,
}) => {
  const styles = useStyles();
  const selectionCount = selectedIds.size;

  // Local UI state: confirmation dialog open + in-flight Download + in-flight Delete.
  const [confirmDeleteOpen, setConfirmDeleteOpen] = React.useState(false);
  const [downloading, setDownloading] = React.useState(false);
  const [deleting, setDeleting] = React.useState(false);

  // Hide entirely at zero selection — caller can still render us unconditionally.
  if (selectionCount === 0) {
    return null;
  }

  const handleDownloadClick = async (): Promise<void> => {
    if (downloading) return;
    setDownloading(true);
    try {
      await onDownload();
    } catch {
      // The parent is responsible for toast dispatch; swallow here to keep
      // the Bar's UI state clean.
    } finally {
      setDownloading(false);
    }
  };

  const handleConfirmDelete = async (): Promise<void> => {
    if (deleting) return;
    setDeleting(true);
    try {
      await onDelete();
      setConfirmDeleteOpen(false);
    } catch {
      // Parent handles toast; close the dialog so the user can dismiss.
      setConfirmDeleteOpen(false);
    } finally {
      setDeleting(false);
    }
  };

  const pluralS = selectionCount !== 1 ? 's' : '';
  const countLabel = `${selectionCount} selected`;

  return (
    <>
      <div
        className={mergeClasses(styles.root, className)}
        role="group"
        aria-label={`Bulk actions for ${selectionCount} selected document${pluralS}`}
      >
        <Text size={200} className={styles.countText} aria-live="polite">
          {countLabel}
        </Text>

        {/* v1.1.55 — leading vertical hairline between the count text and the
            first action icon (UAT: "add subtle separator line between number
            of document selected and the tool bar icons"). */}
        <span className={styles.divider} aria-hidden="true" />

        {/* 1. Email selected — icon-only with Tooltip for hover label */}
        <Tooltip content={`Email ${countLabel}`} relationship="label">
          <Button
            className={styles.iconButton}
            appearance="subtle"
            size="small"
            icon={<Mail20Regular />}
            onClick={onEmail}
            aria-label={`Email ${selectionCount} selected document${pluralS}`}
          />
        </Tooltip>

        {/* 2. Download selected */}
        <Tooltip content={`Download ${countLabel}`} relationship="label">
          <Button
            className={styles.iconButton}
            appearance="subtle"
            size="small"
            icon={
              downloading ? (
                <Spinner size="tiny" aria-label="Downloading" />
              ) : (
                <ArrowDownload20Regular />
              )
            }
            disabled={downloading}
            onClick={() => {
              void handleDownloadClick();
            }}
            aria-label={`Download ${selectionCount} selected document${pluralS}`}
          />
        </Tooltip>

        {/* 3. Pin selected */}
        <Tooltip content={`Pin ${countLabel}`} relationship="label">
          <Button
            className={styles.iconButton}
            appearance="subtle"
            size="small"
            icon={<Pin20Regular />}
            onClick={onPin}
            aria-label={`Pin ${selectionCount} selected document${pluralS}`}
          />
        </Tooltip>

        {/* 4. Delete selected — confirmation Dialog gates the destructive op */}
        <Tooltip content={`Delete ${countLabel}`} relationship="label">
          <Button
            className={styles.iconButton}
            appearance="subtle"
            size="small"
            icon={<Delete20Regular />}
            onClick={() => setConfirmDeleteOpen(true)}
            aria-label={`Delete ${selectionCount} selected document${pluralS}`}
          />
        </Tooltip>

        {/* 5. Document Type → selected — sub-menu of sprk_documenttype options */}
        <Menu>
          <MenuTrigger disableButtonEnhancement>
            <Tooltip
              content={`Set document type for ${countLabel}`}
              relationship="label"
            >
              <Button
                className={styles.iconButton}
                appearance="subtle"
                size="small"
                icon={<DocumentBulletList20Regular />}
                aria-label={`Set document type for ${selectionCount} selected document${pluralS}`}
              />
            </Tooltip>
          </MenuTrigger>
          <MenuPopover>
            <MenuList className={styles.menuList}>
              {docTypeOptions.length === 0 ? (
                <MenuItem disabled>(no options)</MenuItem>
              ) : (
                docTypeOptions.map(opt => (
                  <MenuItem
                    key={opt.value}
                    onClick={() => onDocTypeChange(opt.value)}
                  >
                    {opt.label}
                  </MenuItem>
                ))
              )}
            </MenuList>
          </MenuPopover>
        </Menu>

        {/* 6. Share link */}
        <Tooltip content={`Share link for ${countLabel}`} relationship="label">
          <Button
            className={styles.iconButton}
            appearance="subtle"
            size="small"
            icon={<Share20Regular />}
            onClick={onShareLink}
            aria-label={`Share Dataverse record links for ${selectionCount} selected document${pluralS}`}
          />
        </Tooltip>

        {/* v1.1.49 — UAT Item 4: the X (Dismiss) "Clear selection" affordance
            is removed. Users clear selection via the column-header select-all
            checkbox (uncheck it) OR by clicking individual row/card checkboxes.
            The `onClear` prop stays in the contract so callers (and tests)
            don't need a shape change; it is simply no longer surfaced as a
            visible action in the bulk bar.
            v1.1.55 — trailing divider was removed; the leading divider
            between countText and the first icon carries the visual
            separation now (UAT request). */}
      </div>

      {/* Delete confirmation Dialog — Fluent v9 Dialog is portal-rendered;
          the surrounding FluentProvider mounted at the PCF root re-applies
          styles via `applyStylesToPortals` (default true) per
          `.claude/patterns/ui/fluent-v9-portal-gotcha.md`. */}
      <Dialog
        open={confirmDeleteOpen}
        onOpenChange={(_, data) => {
          if (!data.open) setConfirmDeleteOpen(false);
        }}
      >
        <DialogSurface>
          <DialogBody>
            <DialogTitle>
              Delete {selectionCount} selected document{selectionCount !== 1 ? 's' : ''}?
            </DialogTitle>
            <DialogContent className={styles.dialogBody}>
              <Text>
                This will permanently delete {selectionCount} document
                {selectionCount !== 1 ? 's' : ''} from the matter. This action cannot
                be undone.
              </Text>
            </DialogContent>
            <DialogActions>
              <Button
                appearance="secondary"
                onClick={() => setConfirmDeleteOpen(false)}
                disabled={deleting}
              >
                Cancel
              </Button>
              <Button
                appearance="primary"
                onClick={() => {
                  void handleConfirmDelete();
                }}
                disabled={deleting}
                icon={
                  deleting ? <Spinner size="tiny" aria-label="Deleting" /> : <Delete20Regular />
                }
              >
                Delete
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </>
  );
};

export default BulkActionBar;

// Re-export the icon's Link20Regular for any consumer needing it — currently
// not used (Share Link uses Share20Regular for the action button), but kept
// available for future "Copy link" affordance growth.
export { Link20Regular as ShareLinkIconAlternate };
