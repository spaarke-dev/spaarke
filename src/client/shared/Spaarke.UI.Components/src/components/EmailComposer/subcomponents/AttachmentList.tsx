/**
 * AttachmentList.tsx
 *
 * Renders one section per configured attachment source (`local | spe | related
 * | wizard`); enforces the project caps (150 attachments / 35 MB total hard,
 * 25 MB soft warning — design §5.6.4); shows a source badge + remove button
 * per item; renders forward-mode inclusion checkboxes when `mode === 'forward'`.
 *
 * `local` file picking is UI-only in this task (browser `File` selection +
 * display). Resolving a picked `File` to a `sprk_document` GUID requires a
 * host-injected upload path — no such prop exists in task 020's props
 * contract (§5.4 has no `uploadService`), so locally-picked files are tracked
 * for display/cap purposes but are excluded from the outbound send payload
 * (`EmailComposer.tsx` `mapStateToSendRequest` filters on `documentId`)
 * until a future task wires an upload callback. This is a documented scope
 * boundary, not a bug — see task 020 Decisions Made.
 */
import * as React from 'react';
import {
  Button,
  Text,
  Badge,
  ProgressBar,
  Checkbox,
  makeStyles,
  tokens,
  mergeClasses,
} from '@fluentui/react-components';
import { DismissRegular, DocumentRegular, ArrowUploadRegular } from '@fluentui/react-icons';
import {
  ATTACHMENT_MAX_COUNT,
  ATTACHMENT_MAX_TOTAL_BYTES,
  ATTACHMENT_WARN_TOTAL_BYTES,
} from '../EmailComposer.reducer';
import type {
  EmailAttachmentSourceKind,
  EmailComposerMode,
  IAttachmentItem,
  IComposerAttachmentSource,
} from '../EmailComposer.types';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const SOURCE_LABELS: Record<EmailAttachmentSourceKind, string> = {
  local: 'From this device',
  spe: 'From SharePoint',
  related: 'Related documents',
  wizard: 'Uploaded in this wizard',
};

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

let localIdCounter = 0;
function nextLocalId(): string {
  localIdCounter += 1;
  return `local:${Date.now()}:${localIdCounter}`;
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IAttachmentListProps {
  mode: EmailComposerMode;
  sources: IComposerAttachmentSource[];
  items: IAttachmentItem[];
  onAdd: (item: IAttachmentItem) => void;
  onRemove: (id: string) => void;
  onToggleSelected: (id: string) => void;
  readOnly?: boolean;
  errorMessage?: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  wrapper: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  sectionTitle: {
    color: tokens.colorNeutralForeground3,
  },
  row: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  rowName: {
    flex: 1,
    minWidth: 0,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  summaryRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  warnText: {
    color: tokens.colorPaletteYellowForeground1,
  },
  errorText: {
    color: tokens.colorPaletteRedForeground1,
  },
  localPicker: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const AttachmentList: React.FC<IAttachmentListProps> = ({
  mode,
  sources,
  items,
  onAdd,
  onRemove,
  onToggleSelected,
  readOnly,
  errorMessage,
}) => {
  const styles = useStyles();
  const fileInputRef = React.useRef<HTMLInputElement>(null);

  const includedItems = items.filter(a => a.selected !== false);
  const totalBytes = includedItems.reduce((sum, a) => sum + a.sizeBytes, 0);
  const overCount = includedItems.length > ATTACHMENT_MAX_COUNT;
  const overSize = totalBytes > ATTACHMENT_MAX_TOTAL_BYTES;
  const nearSize = !overSize && totalBytes > ATTACHMENT_WARN_TOTAL_BYTES;

  const handleLocalFilesPicked = React.useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const files = e.target.files;
      if (!files) return;
      for (let i = 0; i < files.length; i += 1) {
        const file = files[i];
        onAdd({
          id: nextLocalId(),
          source: 'local',
          fileName: file.name,
          sizeBytes: file.size,
          mimeType: file.type || undefined,
          file,
          selected: true,
        });
      }
      // Reset so re-selecting the same file fires onChange again.
      e.target.value = '';
    },
    [onAdd]
  );

  const bySource = React.useMemo(() => {
    const map = new Map<EmailAttachmentSourceKind, IAttachmentItem[]>();
    for (const item of items) {
      const list = map.get(item.source) ?? [];
      list.push(item);
      map.set(item.source, list);
    }
    return map;
  }, [items]);

  return (
    <div className={styles.wrapper} role="region" aria-label="Attachments">
      {sources.map(source => {
        const sectionItems = bySource.get(source.kind) ?? [];
        if (sectionItems.length === 0 && source.kind !== 'local') return null;

        return (
          <div key={source.kind}>
            <Text size={200} weight="semibold" className={styles.sectionTitle}>
              {SOURCE_LABELS[source.kind]}
            </Text>

            {source.kind === 'local' && !readOnly && (
              <div className={styles.localPicker}>
                <input
                  ref={fileInputRef}
                  type="file"
                  multiple
                  hidden
                  onChange={handleLocalFilesPicked}
                  aria-label="Choose files from this device"
                />
                <Button
                  appearance="secondary"
                  size="small"
                  icon={<ArrowUploadRegular />}
                  onClick={() => fileInputRef.current?.click()}
                >
                  Add files
                </Button>
              </div>
            )}

            {sectionItems.map(item => (
              <div key={item.id} className={styles.row}>
                {mode === 'forward' && (
                  <Checkbox
                    checked={item.selected !== false}
                    onChange={() => onToggleSelected(item.id)}
                    disabled={readOnly}
                    aria-label={`Include ${item.fileName} in forward`}
                  />
                )}
                <DocumentRegular aria-hidden="true" />
                <Text size={200} className={styles.rowName} title={item.fileName}>
                  {item.fileName}
                </Text>
                <Badge appearance="tint" size="small">
                  {SOURCE_LABELS[item.source]}
                </Badge>
                <Text size={100}>{formatBytes(item.sizeBytes)}</Text>
                {!readOnly && (
                  <Button
                    appearance="subtle"
                    size="small"
                    icon={<DismissRegular fontSize={14} />}
                    onClick={() => onRemove(item.id)}
                    aria-label={`Remove ${item.fileName}`}
                  />
                )}
              </div>
            ))}
          </div>
        );
      })}

      {items.length > 0 && (
        <div className={styles.summaryRow}>
          <ProgressBar
            value={Math.min(totalBytes / ATTACHMENT_MAX_TOTAL_BYTES, 1)}
            color={overSize ? 'error' : nearSize ? 'warning' : 'success'}
            aria-label="Attachment size used"
          />
          <Text
            size={100}
            className={mergeClasses(overSize ? styles.errorText : nearSize ? styles.warnText : undefined)}
          >
            {formatBytes(totalBytes)} / {formatBytes(ATTACHMENT_MAX_TOTAL_BYTES)} · {includedItems.length}/
            {ATTACHMENT_MAX_COUNT} files
          </Text>
        </div>
      )}

      {(overCount || overSize) && (
        <Text size={200} className={styles.errorText} role="alert">
          {overCount && `Too many attachments (max ${ATTACHMENT_MAX_COUNT}). `}
          {overSize && `Total attachment size exceeds ${formatBytes(ATTACHMENT_MAX_TOTAL_BYTES)}.`}
        </Text>
      )}

      {errorMessage && (
        <Text size={200} className={styles.errorText} role="alert">
          {errorMessage}
        </Text>
      )}
    </div>
  );
};

AttachmentList.displayName = 'AttachmentList';
