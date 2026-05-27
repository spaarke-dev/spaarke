/**
 * BulkActionBar (FR-DOC-02)
 *
 * Sticky bulk-action bar rendered above the ListView column-header row (or
 * card grid) when one or more rows are selected. Encapsulates the six bulk
 * actions called out in spec FR-DOC-02:
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
 *   - "Clear" button resets the selection (parent-owned state)
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
  Badge,
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
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
} from '@fluentui/react-components';
import {
  ArrowDownload20Regular,
  Delete20Regular,
  Dismiss20Regular,
  Link20Regular,
  Mail20Regular,
  Pin20Regular,
  Share20Regular,
  TagMultiple20Regular,
  ChevronDownRegular,
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
  // Sticky bar — pinned to the top of the results region so it overlays the
  // ListView column-header row when the user scrolls. The parent renders the
  // bar OUTSIDE the scroll container so `position: sticky` resolves against
  // the bar's own ancestor.
  root: {
    display: 'flex',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalS,
    rowGap: tokens.spacingVerticalXS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground3,
    ...shorthands.borderBottom(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke1),
    position: 'sticky',
    top: 0,
    zIndex: 2,
    minHeight: '40px',
    boxSizing: 'border-box',
  },
  // Count badge wrapper — keeps the badge and "selected" text aligned.
  countWrap: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
  },
  countText: {
    color: tokens.colorNeutralForeground2,
  },
  actionGroup: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    flexWrap: 'wrap',
  },
  spacer: {
    flex: 1,
    minWidth: 0,
  },
  // Confirmation dialog body
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

  return (
    <>
      <div
        className={mergeClasses(styles.root, className)}
        role="region"
        aria-label={`Bulk actions for ${selectionCount} selected document${selectionCount !== 1 ? 's' : ''}`}
      >
        <span className={styles.countWrap}>
          <Badge appearance="filled" color="brand" shape="rounded" size="medium">
            {selectionCount}
          </Badge>
          <Text size={200} className={styles.countText}>
            selected
          </Text>
        </span>

        <Button
          appearance="subtle"
          size="small"
          icon={<Dismiss20Regular aria-hidden="true" />}
          onClick={onClear}
          aria-label="Clear selection"
        >
          Clear
        </Button>

        <div className={styles.spacer} aria-hidden="true" />

        <div className={styles.actionGroup}>
          {/* 1. Email selected */}
          <Button
            appearance="subtle"
            size="small"
            icon={<Mail20Regular aria-hidden="true" />}
            onClick={onEmail}
            aria-label={`Email ${selectionCount} selected document${selectionCount !== 1 ? 's' : ''}`}
          >
            Email
          </Button>

          {/* 2. Download selected */}
          <Button
            appearance="subtle"
            size="small"
            icon={
              downloading ? (
                <Spinner size="tiny" aria-label="Downloading" />
              ) : (
                <ArrowDownload20Regular aria-hidden="true" />
              )
            }
            disabled={downloading}
            onClick={() => {
              void handleDownloadClick();
            }}
            aria-label={`Download ${selectionCount} selected document${selectionCount !== 1 ? 's' : ''}`}
          >
            Download
          </Button>

          {/* 3. Pin selected */}
          <Button
            appearance="subtle"
            size="small"
            icon={<Pin20Regular aria-hidden="true" />}
            onClick={onPin}
            aria-label={`Pin ${selectionCount} selected document${selectionCount !== 1 ? 's' : ''}`}
          >
            Pin
          </Button>

          {/* 4. Delete selected — confirmation Dialog gates the destructive op */}
          <Button
            appearance="subtle"
            size="small"
            icon={<Delete20Regular aria-hidden="true" />}
            onClick={() => setConfirmDeleteOpen(true)}
            aria-label={`Delete ${selectionCount} selected document${selectionCount !== 1 ? 's' : ''}`}
          >
            Delete
          </Button>

          {/* 5. Document Type → selected — sub-menu of sprk_documenttype options */}
          <Menu>
            <MenuTrigger disableButtonEnhancement>
              <Button
                appearance="subtle"
                size="small"
                icon={<TagMultiple20Regular aria-hidden="true" />}
                iconPosition="before"
                aria-label="Set document type for selected"
              >
                Document Type
                <ChevronDownRegular aria-hidden="true" />
              </Button>
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
          <Button
            appearance="subtle"
            size="small"
            icon={<Share20Regular aria-hidden="true" />}
            onClick={onShareLink}
            aria-label={`Share Dataverse record links for ${selectionCount} selected document${selectionCount !== 1 ? 's' : ''}`}
          >
            Share link
          </Button>
        </div>
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
