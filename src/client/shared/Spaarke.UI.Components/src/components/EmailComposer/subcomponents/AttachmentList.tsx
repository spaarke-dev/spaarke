/**
 * AttachmentList.tsx
 *
 * Display-only, collapsible "Attachments" section (owner UAT 2026-07-24). The ADD / LINK
 * controls now live in the RichTextEditor toolbar (paperclip menu) — this component only
 * RENDERS the current attachments + the cap summary. It is DEFAULT COLLAPSED; the header
 * shows a live count so the collapsed state still communicates that files are attached.
 *
 * Rows: locally-picked FILES show no Attach/Link toggles; Document-backed items (a
 * `documentId`, e.g. a looked-up `sprk_document`) show Attach and (when a `linkUrl` is
 * resolvable) Link. A resolving local upload (task 042) renders an inline spinner. Caps
 * (150 attachments / 35 MB total hard, 25 MB soft warning — design §5.6.4) render as a
 * single non-wrapping summary line.
 */
import * as React from 'react';
import {
  Button,
  Text,
  ProgressBar,
  Checkbox,
  Spinner,
  makeStyles,
  tokens,
  mergeClasses,
} from '@fluentui/react-components';
import { DismissRegular, DocumentRegular, ChevronDown20Regular, ChevronUp20Regular } from '@fluentui/react-icons';
import {
  ATTACHMENT_MAX_COUNT,
  ATTACHMENT_MAX_TOTAL_BYTES,
  ATTACHMENT_WARN_TOTAL_BYTES,
} from '../EmailComposer.reducer';
import type { IAttachmentItem } from '../EmailComposer.types';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IAttachmentListProps {
  items: IAttachmentItem[];
  onRemove: (id: string) => void;
  onToggleSelected: (id: string) => void;
  /** Toggle the body-link inclusion for a Document-backed attachment (task 104). */
  onToggleLink?: (id: string) => void;
  /** Ids currently uploading to a governed Document (task 042) — rendered with a spinner. */
  resolvingIds?: ReadonlySet<string>;
  readOnly?: boolean;
  errorMessage?: string;
  /** Start expanded instead of the default collapsed. */
  defaultExpanded?: boolean;
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
  // Clickable header row (label + chevron). +4px vertical padding over the base section
  // header (owner UAT 2026-07-24).
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
    cursor: 'pointer',
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
  },
  // Standard Segoe UI 14px semibold, neutral foreground 1 (UI-DESIGN-STANDARDS section-header;
  // owner UAT 2026-07-24). Token-only so both themes resolve (ADR-021).
  label: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
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
  items,
  onRemove,
  onToggleSelected,
  onToggleLink,
  resolvingIds,
  readOnly,
  errorMessage,
  defaultExpanded = false,
}) => {
  const styles = useStyles();
  const [collapsed, setCollapsed] = React.useState(!defaultExpanded);

  // Document-backed items (a resolved documentId) expose the Attach/Link toggles;
  // locally-picked files never do (owner UAT round 3 #2).
  const showIncludeToggles = (item: IAttachmentItem): boolean => !readOnly && !!item.documentId;

  const includedItems = items.filter(a => a.selected !== false);
  const totalBytes = includedItems.reduce((sum, a) => sum + a.sizeBytes, 0);
  const overCount = includedItems.length > ATTACHMENT_MAX_COUNT;
  const overSize = totalBytes > ATTACHMENT_MAX_TOTAL_BYTES;
  const nearSize = !overSize && totalBytes > ATTACHMENT_WARN_TOTAL_BYTES;

  return (
    <div className={styles.wrapper} role="region" aria-label="Attachments">
      <div
        className={styles.header}
        role="button"
        tabIndex={0}
        aria-expanded={!collapsed}
        onClick={() => setCollapsed(c => !c)}
        onKeyDown={e => {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            setCollapsed(c => !c);
          }
        }}
      >
        <Text className={styles.label}>Attachments{items.length > 0 ? ` (${items.length})` : ''}</Text>
        {collapsed ? <ChevronDown20Regular /> : <ChevronUp20Regular />}
      </div>

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
              {resolvingIds?.has(item.id) && <Spinner size="tiny" label="Uploading…" labelPosition="after" />}
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
