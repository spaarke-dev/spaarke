/**
 * DocumentToolbar.tsx
 *
 * Small reusable toolbar of icon buttons (reload / add / email / open-viewer)
 * intended for document grids in SemanticSearchControl and
 * DocumentRelationshipViewer. Each action is opt-in: when the corresponding
 * handler is `undefined`, the button is not rendered.
 *
 * Styling mirrors the subtle icon buttons used in
 * `SemanticSearchControl/components/ResultsList.tsx` — `appearance="subtle"`,
 * `size="small"`, `minWidth: auto`, no horizontal padding. When
 * `selectionCount` is provided, the email button shows a numeric badge and
 * remains disabled while the count is zero.
 *
 * Constraints: Fluent UI v9 only; ZERO hardcoded colors (uses tokens).
 */
import * as React from 'react';
import { Button, Tooltip, Badge, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { ArrowClockwise20Regular, Add20Regular, MailRegular, Open20Regular } from '@fluentui/react-icons';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

/** Props for {@link DocumentToolbar}. */
export interface IDocumentToolbarProps {
  /** Reload action — when omitted, the button is hidden. */
  onReload?: () => void;
  /** Add-document action — when omitted, the button is hidden. */
  onAddDocument?: () => void;
  /**
   * Email action — when omitted, the email button is hidden entirely.
   * When provided, the button is disabled while `selectionCount` is 0.
   */
  onEmail?: () => void;
  /** Open-viewer action — when omitted, the button is hidden. */
  onOpenViewer?: () => void;
  /**
   * Current multi-selection count. When `> 0` the email button is enabled
   * and a count badge is rendered next to its icon. When omitted or `0`
   * the email button is disabled.
   */
  selectionCount?: number;
  /**
   * When `true`, drops the horizontal gap between buttons for tight layouts.
   * Defaults to `false`.
   */
  compact?: boolean;
  /** Optional extra className applied to the toolbar container. */
  className?: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  toolbar: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  toolbarCompact: {
    gap: tokens.spacingHorizontalXS,
  },
  iconButton: {
    // Match the SemanticSearchControl pattern: subtle icon with no extra padding.
    minWidth: 'auto',
    paddingLeft: '0px',
    paddingRight: '0px',
  },
  emailWrapper: {
    position: 'relative',
    display: 'inline-flex',
    alignItems: 'center',
  },
  emailBadge: {
    // Pull the badge tight to the icon's upper-right corner without
    // changing the button's hit area.
    position: 'absolute',
    top: '-4px',
    right: '-6px',
    pointerEvents: 'none',
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Reusable icon toolbar for document grid surfaces.
 *
 * @example
 * ```tsx
 * <DocumentToolbar
 *   onReload={refetch}
 *   onAddDocument={openUploader}
 *   onEmail={openEmailWizard}
 *   onOpenViewer={openDrv}
 *   selectionCount={selection.count}
 * />
 * ```
 */
export const DocumentToolbar: React.FC<IDocumentToolbarProps> = ({
  onReload,
  onAddDocument,
  onEmail,
  onOpenViewer,
  selectionCount,
  compact = false,
  className,
}) => {
  const styles = useStyles();
  const count = selectionCount ?? 0;
  const emailEnabled = !!onEmail && count > 0;

  return (
    <div className={mergeClasses(styles.toolbar, compact ? styles.toolbarCompact : undefined, className)}>
      {onReload && (
        <Tooltip content="Reload" relationship="label">
          <Button
            className={styles.iconButton}
            appearance="subtle"
            size="small"
            icon={<ArrowClockwise20Regular />}
            aria-label="Reload"
            onClick={onReload}
          />
        </Tooltip>
      )}

      {onAddDocument && (
        <Tooltip content="Add Document" relationship="label">
          <Button
            className={styles.iconButton}
            appearance="subtle"
            size="small"
            icon={<Add20Regular />}
            aria-label="Add Document"
            onClick={onAddDocument}
          />
        </Tooltip>
      )}

      {onEmail && (
        <Tooltip
          content={emailEnabled ? `Email ${count} document(s)` : 'Email (select documents first)'}
          relationship="label"
        >
          <span className={styles.emailWrapper}>
            <Button
              className={styles.iconButton}
              appearance="subtle"
              size="small"
              icon={<MailRegular />}
              aria-label={emailEnabled ? `Email ${count} selected document(s)` : 'Email'}
              onClick={onEmail}
              disabled={!emailEnabled}
            />
            {count > 0 && (
              <Badge className={styles.emailBadge} appearance="filled" color="brand" size="small" aria-hidden="true">
                {count}
              </Badge>
            )}
          </span>
        </Tooltip>
      )}

      {onOpenViewer && (
        <Tooltip content="Open full viewer" relationship="label">
          <Button
            className={styles.iconButton}
            appearance="subtle"
            size="small"
            icon={<Open20Regular />}
            aria-label="Open full viewer"
            onClick={onOpenViewer}
          />
        </Tooltip>
      )}
    </div>
  );
};

DocumentToolbar.displayName = 'DocumentToolbar';
