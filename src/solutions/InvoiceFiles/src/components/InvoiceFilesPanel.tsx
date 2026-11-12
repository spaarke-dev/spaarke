/**
 * The invoice form's Files section (ebilling-r1 task 151e): the invoice's servable files as a table, Invoice PDF first, with Open and
 * Download on each file and a link to the email Document it came from.
 *
 * The panel imports only Fluent v9 and the page's own services (not `@spaarke/ui-components`), so it renders in the jest suite; the
 * host-only pieces (auth, theme, the Xrm record opener) are wired in `main.tsx` and arrive as props. Colours come from Fluent tokens only,
 * so the host's light/dark theme (resolved in `main.tsx`) is the only thing that decides them.
 *
 * States: loading, error (with Retry), empty ("No files"), list. A file whose malware scan has no verdict yet is listed with
 * "Scan in progress" and its buttons disabled. A failed Open or Download shows its reason above the table and leaves the list as it was.
 */

import * as React from 'react';
import {
  Badge,
  Button,
  Link,
  MessageBar,
  MessageBarBody,
  MessageBarActions,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableCellLayout,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { ArrowDownloadRegular, OpenRegular, MailRegular } from '@fluentui/react-icons';
import { listFailureMessage, type InvoiceFile, type InvoiceFilesApi } from '../services/invoiceFilesApi';
import { downloadFile, openFile, type FileActionDeps } from '../services/fileActions';
import { formatAddedOn, formatSize, isViewable, purposeLabel } from '../services/format';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingHorizontalM,
    boxSizing: 'border-box',
    minHeight: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  centered: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    padding: tokens.spacingVerticalXL,
  },
  muted: {
    color: tokens.colorNeutralForeground3,
  },
  actions: {
    display: 'flex',
    gap: tokens.spacingHorizontalXS,
    justifyContent: 'flex-end',
  },
  nameCell: {
    fontWeight: tokens.fontWeightSemibold,
    wordBreak: 'break-word',
  },
});

export interface InvoiceFilesPanelProps {
  invoiceId: string;
  api: Pick<InvoiceFilesApi, 'listFiles'>;
  /** The browser wiring Open and Download use (the content route is reached through it). */
  actionDeps: FileActionDeps;
  /** Open the source email Document's form (Xrm.Navigation.openForm, wired in main.tsx). */
  openDocument: (documentId: string) => void | Promise<void>;
}

type Load =
  | { state: 'loading' }
  | { state: 'error'; message: string }
  | { state: 'ready'; files: InvoiceFile[] };

export const InvoiceFilesPanel: React.FC<InvoiceFilesPanelProps> = ({ invoiceId, api, actionDeps, openDocument }) => {
  const s = useStyles();
  const [load, setLoad] = React.useState<Load>({ state: 'loading' });
  const [attempt, setAttempt] = React.useState(0);
  const [actionError, setActionError] = React.useState<string | null>(null);
  const [busyFileId, setBusyFileId] = React.useState<string | null>(null);

  React.useEffect(() => {
    let cancelled = false;
    setLoad({ state: 'loading' });
    setActionError(null);
    api
      .listFiles(invoiceId)
      .then(result => {
        if (cancelled) return;
        setLoad(result.ok ? { state: 'ready', files: result.value } : { state: 'error', message: listFailureMessage(result.failure) });
      })
      .catch(() => {
        if (!cancelled) setLoad({ state: 'error', message: listFailureMessage({ status: 0, errorCode: null, detail: null, signInFailed: false }) });
      });
    return () => {
      cancelled = true;
    };
  }, [api, invoiceId, attempt]);

  const run = React.useCallback(
    async (file: InvoiceFile, action: typeof openFile) => {
      setActionError(null);
      setBusyFileId(file.fileId);
      try {
        const result = await action(file, actionDeps);
        if (!result.ok) setActionError(result.message);
      } finally {
        setBusyFileId(null);
      }
    },
    [actionDeps]
  );

  if (load.state === 'loading') {
    return (
      <div className={s.root} data-testid="invoicefiles-loading">
        <div className={s.centered}>
          <Spinner size="small" label="Loading files…" />
        </div>
      </div>
    );
  }

  if (load.state === 'error') {
    return (
      <div className={s.root} data-testid="invoicefiles-error">
        <MessageBar intent="error">
          <MessageBarBody>{load.message}</MessageBarBody>
          <MessageBarActions>
            <Button size="small" onClick={() => setAttempt(n => n + 1)}>
              Retry
            </Button>
          </MessageBarActions>
        </MessageBar>
      </div>
    );
  }

  if (load.files.length === 0) {
    return (
      <div className={s.root} data-testid="invoicefiles-empty">
        <Text className={s.muted}>No files</Text>
      </div>
    );
  }

  return (
    <div className={s.root} data-testid="invoicefiles-list">
      {actionError && (
        <MessageBar intent="warning" data-testid="invoicefiles-action-error">
          <MessageBarBody>{actionError}</MessageBarBody>
        </MessageBar>
      )}
      <Table size="small" aria-label="Invoice files">
        <TableHeader>
          <TableRow>
            <TableHeaderCell>Name</TableHeaderCell>
            <TableHeaderCell>Type</TableHeaderCell>
            <TableHeaderCell>Size</TableHeaderCell>
            <TableHeaderCell>Added</TableHeaderCell>
            <TableHeaderCell>Status</TableHeaderCell>
            <TableHeaderCell>Source email</TableHeaderCell>
            <TableHeaderCell />
          </TableRow>
        </TableHeader>
        <TableBody>
          {load.files.map(file => {
            const pending = file.scanState !== 'Ready';
            const busy = busyFileId === file.fileId;
            return (
              <TableRow key={file.fileId} data-testid={`invoicefile-row-${file.fileId}`}>
                <TableCell>
                  <TableCellLayout>
                    <span className={s.nameCell}>{file.name || 'Untitled file'}</span>
                  </TableCellLayout>
                </TableCell>
                <TableCell>
                  <TableCellLayout>
                    <Badge appearance={file.purpose === 'InvoicePdf' ? 'filled' : 'outline'} color={file.purpose === 'InvoicePdf' ? 'brand' : 'informative'}>
                      {purposeLabel(file.purpose)}
                    </Badge>
                  </TableCellLayout>
                </TableCell>
                <TableCell>
                  <TableCellLayout>{formatSize(file.sizeBytes)}</TableCellLayout>
                </TableCell>
                <TableCell>
                  <TableCellLayout>{formatAddedOn(file.addedOn)}</TableCellLayout>
                </TableCell>
                <TableCell>
                  <TableCellLayout>
                    {pending ? (
                      <Badge appearance="tint" color="warning" data-testid={`invoicefile-pending-${file.fileId}`}>
                        Scan in progress
                      </Badge>
                    ) : (
                      <Text>Ready</Text>
                    )}
                  </TableCellLayout>
                </TableCell>
                <TableCell>
                  <TableCellLayout>
                    {file.sourceEmail ? (
                      <Link
                        as="button"
                        data-testid={`invoicefile-source-${file.fileId}`}
                        onClick={() => void openDocument(file.sourceEmail!.documentId)}
                      >
                        <MailRegular /> {file.sourceEmail.name || 'Email'}
                      </Link>
                    ) : (
                      <Text className={s.muted}>{'—'}</Text>
                    )}
                  </TableCellLayout>
                </TableCell>
                <TableCell>
                  <div className={s.actions}>
                    {isViewable(file.contentType) && (
                      <Button
                        size="small"
                        icon={<OpenRegular />}
                        disabled={pending || busy}
                        data-testid={`invoicefile-open-${file.fileId}`}
                        onClick={() => void run(file, openFile)}
                      >
                        Open
                      </Button>
                    )}
                    <Button
                      size="small"
                      icon={<ArrowDownloadRegular />}
                      disabled={pending || busy}
                      data-testid={`invoicefile-download-${file.fileId}`}
                      onClick={() => void run(file, downloadFile)}
                    >
                      Download
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </div>
  );
};
