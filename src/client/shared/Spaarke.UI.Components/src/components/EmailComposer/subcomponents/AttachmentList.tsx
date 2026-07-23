/**
 * AttachmentList.tsx
 *
 * A single collapsible "Attachments" section with a header toolbar (owner UAT
 * round 3, 2026-07-22): a title + right-justified tools — add-from-computer,
 * look-up-document, and an expand/collapse chevron. No per-source pills/labels.
 *
 * Rows: locally-picked FILES show no Attach/Link toggles; Document-backed items
 * (a `documentId`, e.g. a looked-up `sprk_document`) show Attach and (when a
 * `linkUrl` is resolvable) Link. Caps (150 attachments / 35 MB total hard, 25 MB
 * soft warning — design §5.6.4) render as a single non-wrapping summary line.
 *
 * `local` file picking is UI-only in this task (browser `File` selection +
 * display). Resolving a picked `File` to a `sprk_document` GUID requires a
 * host-injected upload path; locally-picked files are tracked for display/cap
 * purposes but excluded from the outbound send payload until resolved (see
 * `mapStateToSendRequest`) — a documented scope boundary, not a bug.
 */
import * as React from 'react';
import { Button, Text, ProgressBar, Checkbox, Tooltip, makeStyles, tokens, mergeClasses } from '@fluentui/react-components';
import {
  DismissRegular,
  DocumentRegular,
  DocumentArrowUp20Regular,
  DocumentAdd20Regular,
  ChevronDown20Regular,
  ChevronUp20Regular,
} from '@fluentui/react-icons';
import {
  ATTACHMENT_MAX_COUNT,
  ATTACHMENT_MAX_TOTAL_BYTES,
  ATTACHMENT_WARN_TOTAL_BYTES,
} from '../EmailComposer.reducer';
import type { EmailComposerMode, IAttachmentItem, IComposerAttachmentSource } from '../EmailComposer.types';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

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
  /** Toggle the body-link inclusion for a Document-backed attachment (task 104). */
  onToggleLink?: (id: string) => void;
  /**
   * Open the host's document-lookup overlay (owner UAT round 3). When supplied,
   * the "look up document" tool renders. The host returns the picked documents by
   * calling `onAdd` for each. Absent → only the from-computer tool renders.
   */
  onBrowseDocuments?: () => void;
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
    gap: tokens.spacingVerticalXS,
  },
  headerRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
  },
  headerTitle: {
    color: tokens.colorNeutralForeground2,
    fontWeight: tokens.fontWeightSemibold,
  },
  headerActions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
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
  rowSize: {
    flexShrink: 0,
    whiteSpace: 'nowrap',
  },
  includeToggles: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flexShrink: 0,
  },
  // Single non-wrapping summary line, right-aligned (owner UAT round 4).
  summaryRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'flex-end',
    gap: tokens.spacingHorizontalS,
    whiteSpace: 'nowrap',
    overflow: 'hidden',
  },
  summaryBar: {
    flexShrink: 1,
    minWidth: '60px',
    maxWidth: '160px',
  },
  summaryText: {
    flexShrink: 0,
    whiteSpace: 'nowrap',
  },
  warnText: {
    color: tokens.colorPaletteYellowForeground1,
  },
  errorText: {
    color: tokens.colorPaletteRedForeground1,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const AttachmentList: React.FC<IAttachmentListProps> = ({
  sources,
  items,
  onAdd,
  onRemove,
  onToggleSelected,
  onToggleLink,
  onBrowseDocuments,
  readOnly,
  errorMessage,
}) => {
  const styles = useStyles();
  const fileInputRef = React.useRef<HTMLInputElement>(null);
  const [collapsed, setCollapsed] = React.useState(false);

  // Document-backed items (a resolved documentId) expose the Attach/Link toggles;
  // locally-picked files never do (owner UAT round 3 #2).
  const showIncludeToggles = (item: IAttachmentItem): boolean => !readOnly && !!item.documentId;

  const showLocalAdd = !readOnly && sources.some(s => s.kind === 'local');
  const showDocAdd = !readOnly && !!onBrowseDocuments;

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
      e.target.value = ''; // reset so re-selecting the same file fires onChange again
    },
    [onAdd]
  );

  return (
    <div className={styles.wrapper} role="region" aria-label="Attachments">
      <div className={styles.headerRow}>
        <Text size={200} className={styles.headerTitle}>
          Attachments{items.length > 0 ? ` (${items.length})` : ''}
        </Text>
        <div className={styles.headerActions}>
          {showLocalAdd && (
            <Tooltip content="Add files from your computer" relationship="label">
              <Button
                appearance="subtle"
                size="small"
                icon={<DocumentArrowUp20Regular />}
                aria-label="Add files from your computer"
                onClick={() => fileInputRef.current?.click()}
              />
            </Tooltip>
          )}
          {showDocAdd && (
            <Tooltip content="Look up a document" relationship="label">
              <Button
                appearance="subtle"
                size="small"
                icon={<DocumentAdd20Regular />}
                aria-label="Look up a document"
                onClick={() => onBrowseDocuments?.()}
              />
            </Tooltip>
          )}
          <Tooltip content={collapsed ? 'Expand attachments' : 'Collapse attachments'} relationship="label">
            <Button
              appearance="subtle"
              size="small"
              icon={collapsed ? <ChevronDown20Regular /> : <ChevronUp20Regular />}
              aria-label={collapsed ? 'Expand attachments' : 'Collapse attachments'}
              aria-expanded={!collapsed}
              onClick={() => setCollapsed(c => !c)}
            />
          </Tooltip>
        </div>
      </div>

      <input
        ref={fileInputRef}
        type="file"
        multiple
        hidden
        onChange={handleLocalFilesPicked}
        aria-label="Choose files from your computer"
      />

      {!collapsed && (
        <>
          {items.map(item => (
            <div key={item.id} className={styles.row}>
              <DocumentRegular aria-hidden="true" />
              <Text size={200} className={styles.rowName} title={item.fileName}>
                {item.fileName}
              </Text>
              <Text size={100} className={styles.rowSize}>
                {formatBytes(item.sizeBytes)}
              </Text>
              {showIncludeToggles(item) && (
                <div className={styles.includeToggles}>
                  <Checkbox
                    label="Attach"
                    checked={item.selected !== false}
                    onChange={() => onToggleSelected(item.id)}
                    aria-label={`Attach ${item.fileName} as a file`}
                  />
                  {item.linkUrl && (
                    <Checkbox
                      label="Link"
                      checked={item.linkSelected === true}
                      onChange={() => onToggleLink?.(item.id)}
                      aria-label={`Insert a link to ${item.fileName} in the message body`}
                    />
                  )}
                </div>
              )}
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

          {items.length > 0 && (
            <div className={styles.summaryRow}>
              <ProgressBar
                className={styles.summaryBar}
                value={Math.min(totalBytes / ATTACHMENT_MAX_TOTAL_BYTES, 1)}
                color={overSize ? 'error' : nearSize ? 'warning' : 'success'}
                aria-label="Attachment size used"
              />
              <Text
                size={100}
                className={mergeClasses(
                  styles.summaryText,
                  overSize ? styles.errorText : nearSize ? styles.warnText : undefined
                )}
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
        </>
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
