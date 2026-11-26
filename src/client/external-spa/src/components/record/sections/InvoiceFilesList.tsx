/**
 * InvoiceFilesList — the Invoice record's read-only Files list (spec FR-13; R4 task 021).
 *
 * Over ebilling's `GET /api/v1/external/invoices/{id}/files` and `.../files/{fileId}/content` (contract cited in
 * `web-api-client.ts`). It lists and downloads only: it renders no Upload, no Version history and no Delete.
 *
 * ROW STATES (the list's `scanStatus`):
 *   - `Clean` and `NotScanned` are served: a Download button saves the bytes (`bffApiBlob`, never a signed URL, Graph or
 *     an SPE pointer) as the row's `fileName`, because the content route sends no Content-Disposition name.
 *   - `Checking` (the malware scan has no verdict yet) shows "Checking file…" and no Download. Any value the SPA does not
 *     know is treated the same way (hide, don't guess).
 *   - There is no failed state: the BFF omits a malicious file from the list.
 *   - A content read that answers 409 `file_scan_pending` (the scan state changed after the list was read) turns the row
 *     into "Checking file…" and tells the user; a later Refresh re-reads the list.
 *
 * LIST FAILURES say what the status means: 404 (an invoice the caller cannot see, or no files route), 403 (the workforce
 * role ceiling), 503 (a retryable fault) and anything else (retryable). 503 and the generic failure offer Retry. A 503
 * `ebilling.disabled` (the kill switch both routes sit behind; off by default outside dev) is not an outage: one neutral
 * line and no Retry, on the list and on a content read alike. Download failures reuse DocumentLibrary's texts (`downloadFailureMessage`).
 *
 * ADR-021: Fluent v9 semantic tokens only. The shared `EmptyState` shows an invoice with no files.
 */
import * as React from 'react';
import {
  Button,
  MessageBar,
  MessageBarBody,
  Spinner,
  Text,
  Tooltip,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { ArrowDownloadRegular, DocumentRegular } from '@fluentui/react-icons';
import { EmptyState } from '@spaarke/ui-components/components/EmptyState';
import {
  getInvoiceFileContent,
  getInvoiceFiles,
  invoiceFileDisplayName,
  invoiceFileSaveName,
  isFileScanPendingError,
  isInvoiceFilesDisabledError,
  type InvoiceFileItem,
} from '../../../api/web-api-client';
import { ApiError } from '../../../types';
import {
  downloadFailureLogText,
  downloadFailureMessage,
  formatDate,
  formatFileSize,
  saveBlobAs,
} from '../../DocumentLibrary';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    minWidth: 0,
  },
  list: {
    listStyleType: 'none',
    margin: 0,
    padding: 0,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  row: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    minWidth: 0,
    paddingBlock: tokens.spacingVerticalS,
    paddingInline: tokens.spacingHorizontalS,
    borderBottom: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
  },
  icon: {
    flexShrink: 0,
    fontSize: tokens.fontSizeBase500,
    color: tokens.colorNeutralForeground3,
  },
  text: {
    display: 'flex',
    flexDirection: 'column',
    flexGrow: 1,
    flexShrink: 1,
    flexBasis: 0,
    minWidth: 0,
  },
  name: {
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  meta: {
    display: 'flex',
    flexDirection: 'row',
    flexWrap: 'wrap',
    columnGap: tokens.spacingHorizontalM,
    color: tokens.colorNeutralForeground3,
  },
  action: {
    flexShrink: 0,
    display: 'flex',
    alignItems: 'center',
    color: tokens.colorNeutralForeground3,
  },
});

/** Which file states are served: only a scan verdict the BFF serves offers Download (anything else is "Checking"). */
function isServed(file: InvoiceFileItem): boolean {
  return file.scanStatus === 'Clean' || file.scanStatus === 'NotScanned';
}

type LoadState =
  | { phase: 'loading' }
  | { phase: 'ready'; files: InvoiceFileItem[] }
  | { phase: 'error'; status: number | undefined; disabled: boolean };

const INVOICE_FILES_DISABLED_TEXT = "Invoice files aren't available.";

/** What a failed list read says, by status (never the server's detail text). `retry` marks the states worth retrying. */
export function filesListFailure(status: number | undefined, disabled = false): { message: string; retry: boolean } {
  // The eBilling kill switch is off for this environment: not an outage, so one neutral line and no Retry.
  if (disabled) return { message: INVOICE_FILES_DISABLED_TEXT, retry: false };
  if (status === 404) return { message: "This invoice's files aren't available.", retry: false };
  if (status === 403) return { message: "You don't have access to this invoice's files.", retry: false };
  if (status === 503)
    return { message: "This invoice's files couldn't be loaded just now. Please try again.", retry: true };
  return { message: "This invoice's files couldn't be loaded. Please try again.", retry: true };
}

export interface InvoiceFilesListProps {
  invoiceId: string;
}

export const InvoiceFilesList: React.FC<InvoiceFilesListProps> = ({ invoiceId }) => {
  const styles = useStyles();
  const [state, setState] = React.useState<LoadState>({ phase: 'loading' });
  const [attempt, setAttempt] = React.useState(0);
  // Files whose content answered 409 `file_scan_pending` after the list said they were served.
  const [checkingIds, setCheckingIds] = React.useState<ReadonlySet<string>>(() => new Set());
  const [downloadingId, setDownloadingId] = React.useState<string | null>(null);
  const [notice, setNotice] = React.useState<string | null>(null);
  // A second click while the bytes are on their way starts nothing.
  const downloading = React.useRef(false);

  React.useEffect(() => {
    let cancelled = false;
    setState({ phase: 'loading' });
    setCheckingIds(new Set());
    setNotice(null);
    getInvoiceFiles(invoiceId).then(
      files => {
        if (!cancelled) setState({ phase: 'ready', files });
      },
      (err: unknown) => {
        if (cancelled) return;
        console.error(`[InvoiceFilesList] Files list failed: ${downloadFailureLogText(err)}`);
        setState({
          phase: 'error',
          status: err instanceof ApiError ? err.statusCode : undefined,
          disabled: isInvoiceFilesDisabledError(err),
        });
      }
    );
    return () => {
      cancelled = true;
    };
  }, [invoiceId, attempt]);

  const download = React.useCallback(
    async (file: InvoiceFileItem) => {
      if (downloading.current) return;
      downloading.current = true;
      setDownloadingId(file.id);
      setNotice(null);
      try {
        saveBlobAs(await getInvoiceFileContent(invoiceId, file.id), invoiceFileSaveName(file));
      } catch (err) {
        if (isFileScanPendingError(err)) {
          setCheckingIds(prev => new Set(prev).add(file.id));
          setNotice('This file is still being checked. Try again in a few minutes.');
        } else if (isInvoiceFilesDisabledError(err)) {
          setNotice(INVOICE_FILES_DISABLED_TEXT);
        } else {
          console.error(`[InvoiceFilesList] Download failed for file ${file.id}: ${downloadFailureLogText(err)}`);
          setNotice(downloadFailureMessage(err));
        }
      } finally {
        downloading.current = false;
        setDownloadingId(null);
      }
    },
    [invoiceId]
  );

  if (state.phase === 'loading') {
    return (
      <div className={styles.root} data-testid="record-detail-files">
        <Spinner size="small" label="Loading files…" labelPosition="after" />
      </div>
    );
  }

  if (state.phase === 'error') {
    const failure = filesListFailure(state.status, state.disabled);
    return (
      <div className={styles.root} data-testid="record-detail-files">
        <MessageBar intent="error" data-testid="invoice-files-error">
          <MessageBarBody>{failure.message}</MessageBarBody>
        </MessageBar>
        {failure.retry && (
          <div>
            <Button appearance="secondary" size="small" onClick={() => setAttempt(n => n + 1)}>
              Retry
            </Button>
          </div>
        )}
      </div>
    );
  }

  if (state.files.length === 0) {
    return (
      <div className={styles.root} data-testid="record-detail-files">
        <EmptyState
          size="compact"
          icon={<DocumentRegular />}
          heading="No files"
          description="No files have been submitted for this invoice."
        />
      </div>
    );
  }

  return (
    <div className={styles.root} data-testid="record-detail-files">
      {notice && (
        <MessageBar intent="warning" data-testid="invoice-files-notice">
          <MessageBarBody>{notice}</MessageBarBody>
        </MessageBar>
      )}
      <ul className={styles.list} aria-label="Invoice files">
        {state.files.map(file => {
          const served = isServed(file) && !checkingIds.has(file.id);
          return (
            <li key={file.id} className={styles.row} data-testid="invoice-file-row">
              <DocumentRegular className={styles.icon} aria-hidden="true" />
              <div className={styles.text}>
                <Text className={styles.name} weight="semibold" title={invoiceFileDisplayName(file)}>
                  {invoiceFileDisplayName(file)}
                </Text>
                <div className={styles.meta}>
                  <Text size={200} data-testid="invoice-file-size">
                    {formatFileSize(file.sizeBytes)}
                  </Text>
                  <Text size={200} data-testid="invoice-file-date">
                    {formatDate(file.createdOn)}
                  </Text>
                </div>
              </div>
              <div className={styles.action}>
                {served ? (
                  <Tooltip content="Download" relationship="label">
                    <Button
                      appearance="subtle"
                      size="small"
                      icon={downloadingId === file.id ? <Spinner size="tiny" /> : <ArrowDownloadRegular />}
                      disabled={downloadingId === file.id}
                      onClick={() => void download(file)}
                      aria-label={`Download ${invoiceFileDisplayName(file)}`}
                    />
                  </Tooltip>
                ) : (
                  <Text size={200} data-testid="invoice-file-checking">
                    Checking file…
                  </Text>
                )}
              </div>
            </li>
          );
        })}
      </ul>
    </div>
  );
};
