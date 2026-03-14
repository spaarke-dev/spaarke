/**
 * FindSimilarDialog - Reusable iframe dialog for the DocumentRelationshipViewer.
 *
 * Renders a near-fullscreen Dialog containing an iframe that loads the
 * DocumentRelationshipViewer Code Page web resource.
 *
 * Consumer builds the URL (since URL construction differs between PCF and
 * LegalWorkspace) and passes it in. This component just provides the dialog
 * shell with correct sizing and no scrollbars.
 *
 * Zero service dependencies — fully prop-based.
 *
 * @see ADR-021 for Fluent UI v9 requirements
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  Dialog,
  DialogSurface,
  DialogBody,
  Button,
  Tooltip,
  shorthands,
} from '@fluentui/react-components';
import { DismissRegular, ArrowExpandRegular } from '@fluentui/react-icons';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IFindSimilarDialogProps {
  /** Whether the dialog is open. */
  open: boolean;
  /** Called when the dialog requests to close (backdrop click, Escape). */
  onClose: () => void;
  /** The URL to load in the iframe. When null/undefined the iframe is not rendered. */
  url: string | null;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  surface: {
    padding: '0px',
    width: '85vw',
    maxWidth: '85vw',
    height: '85vh',
    maxHeight: '85vh',
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.overflow('hidden'),
    ...shorthands.borderRadius(tokens.borderRadiusXLarge),
  },
  titleBar: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'flex-end',
    gap: tokens.spacingHorizontalXS,
    paddingTop: tokens.spacingVerticalXS,
    paddingRight: tokens.spacingHorizontalS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalS,
    backgroundColor: tokens.colorNeutralBackground3,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    flexShrink: 0,
  },
  body: {
    padding: '0px',
    flex: 1,
    minHeight: 0,
    position: 'relative' as const,
  },
  frame: {
    position: 'absolute' as const,
    top: 0,
    left: 0,
    width: '100%',
    height: '100%',
    border: 'none',
    display: 'block',
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const FindSimilarDialog: React.FC<IFindSimilarDialogProps> = ({ open, onClose, url }) => {
  const styles = useStyles();

  const handleExpand = React.useCallback(() => {
    if (url) {
      window.open(url, '_blank', 'noopener,noreferrer');
    }
  }, [url]);

  return (
    <Dialog
      open={open}
      onOpenChange={(_, data) => {
        if (!data.open) onClose();
      }}
    >
      <DialogSurface className={styles.surface}>
        <div className={styles.titleBar}>
          <Tooltip content="Open in new tab" relationship="label">
            <Button
              appearance="subtle"
              icon={<ArrowExpandRegular />}
              size="small"
              onClick={handleExpand}
              aria-label="Open in new tab"
            />
          </Tooltip>
          <Tooltip content="Close" relationship="label">
            <Button appearance="subtle" icon={<DismissRegular />} size="small" onClick={onClose} aria-label="Close" />
          </Tooltip>
        </div>
        <DialogBody className={styles.body}>
          {url && <iframe src={url} title="Document Relationships" className={styles.frame} />}
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

export default FindSimilarDialog;
