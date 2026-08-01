import * as React from 'react';
import { Button, Text, makeStyles, tokens } from '@fluentui/react-components';
import { SprkModal } from '../SprkModal';

/**
 * PreviewModal — thin `SprkModal` config for rich document/file preview (spec
 * FR-09; design §6.1). Fixed `lg` / `layout="landscape"` / unpadded; the body
 * is a `1fr 320px` stage+meta grid — the stage cell mounts caller-supplied
 * content (typically `RichFilePreview`) via `children`, the meta column
 * renders `metadata` label/value rows. Footer is a single primary Close.
 *
 * `BrowseModal` (same folder) is this preset + the `SprkModal` `nav` prop; it
 * composes `PreviewGridBody` below instead of duplicating the grid/makeStyles
 * (CLAUDE.md §11 — no parallel abstraction).
 */
const useStyles = makeStyles({
  previewGrid: {
    display: 'grid',
    gridTemplateColumns: '1fr 320px',
    height: '100%',
    minHeight: 0,
  },
  previewStage: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground3,
    minWidth: 0,
  },
  previewMeta: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalL,
    borderLeft: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    overflow: 'auto',
  },
  metaRow: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXXS },
  metaLabel: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
});

/** A single label/value row rendered in the `PreviewModal`/`BrowseModal` meta column. */
export interface PreviewModalMetadataItem {
  label: string;
  value: string;
}

/**
 * Shared stage+meta grid body. Exported so `BrowseModal` can compose the same
 * grid + makeStyles rather than forking it — not part of the public preset
 * surface (not re-exported from the components barrel; task 009).
 */
export const PreviewGridBody: React.FC<{
  metadata: PreviewModalMetadataItem[];
  children?: React.ReactNode;
}> = ({ metadata, children }) => {
  const styles = useStyles();
  return (
    <div className={styles.previewGrid} data-testid="preview-grid">
      <div className={styles.previewStage} data-testid="preview-stage">
        {children ?? <Text>Preview unavailable</Text>}
      </div>
      <div className={styles.previewMeta} data-testid="preview-meta">
        <Text weight="semibold">Details</Text>
        {metadata.map((m) => (
          <div key={m.label} className={styles.metaRow}>
            <span className={styles.metaLabel}>{m.label}</span>
            <Text>{m.value}</Text>
          </div>
        ))}
      </div>
    </div>
  );
};

export interface PreviewModalProps {
  /** Whether the modal is open. */
  open: boolean;
  /** Close callback — wired to the × and the footer's primary Close. */
  onClose: () => void;
  /** Header title (ellipsized; announced). */
  title: string;
  /** The `--sprk-ui-scale` factor for sizing, forwarded to the shell. */
  uiScale?: number;
  /** Metadata rows rendered in the meta column. */
  metadata?: PreviewModalMetadataItem[];
  /** Stage content — e.g. a `RichFilePreview` renderer. Defaults to a placeholder. */
  children?: React.ReactNode;
}

export const PreviewModal: React.FC<PreviewModalProps> = ({
  open,
  onClose,
  title,
  uiScale,
  metadata = [],
  children,
}) => {
  return (
    <SprkModal
      open={open}
      onClose={onClose}
      title={title}
      size="lg"
      layout="landscape"
      padded={false}
      uiScale={uiScale}
      footer={
        <Button appearance="primary" onClick={onClose}>
          Close
        </Button>
      }
    >
      <PreviewGridBody metadata={metadata}>{children}</PreviewGridBody>
    </SprkModal>
  );
};

export default PreviewModal;
