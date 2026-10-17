/**
 * InvoiceSubmissionWizard — the shared "submit an invoice PDF" wizard (ebilling-r1 task 051, FR-18 / FR-19).
 *
 * One component for every entry point: the external SPA page (task 051) and the internal CreateInvoiceWizard rewire
 * (task 052) inject the same three callbacks and get the same flow. It composes the shared `WizardShell`,
 * `FileUploadZone` / `UploadedFileList` and `StatusBadge`; it holds no SPA auth, Xrm or BFF URL (ADR-012).
 *
 * Steps:
 *   1. Record  — choose an accessible core record (matter / project / work assignment), from `loadRecords`.
 *   2. File    — one PDF. Type and size are checked here only as a convenience; the server is the authority.
 *   3. Details — the firm invoice number (REQUIRED, D-91) and an optional PO number. Finish submits.
 *   4. Processing then Result — the success screen mounts {@link InvoiceSubmissionOutcome}, which polls `getStatus`
 *      and shows Accepted (tracking number), Rejected (reason + the host's report slot) or Failed (retry guidance).
 *
 * The client never creates a submission or invoice itself and never decides authorization (ADR-002 / FR-17): a
 * refusal from `submit` is shown by its error code and the wizard stays where it is.
 *
 * Phone width: `WizardShell` draws a fixed-width step list beside the content. The `compact` styles below hide it
 * under 600px (each step carries its own "Step n of 3" caption), so a host should mount with `embedded` there.
 *
 * ADR-021: Fluent v9 tokens only, no hard-coded colour. Dark mode follows the host's FluentProvider.
 */
import * as React from 'react';
import {
  Button,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  Radio,
  RadioGroup,
  Spinner,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { DocumentCheckmarkRegular } from '@fluentui/react-icons';
import { WizardShell } from '../Wizard/WizardShell';
import type { IWizardStepConfig, IWizardSuccessConfig } from '../Wizard/wizardShellTypes';
import { FileUploadZone, UploadedFileList } from '../FileUpload';
import type { IFileValidationError, IUploadedFile } from '../FileUpload/fileUploadTypes';
import { InvoiceSubmissionOutcome } from './InvoiceSubmissionOutcome';
import { invoiceSubmissionErrorMessage } from './messages';
import type {
  InvoiceCoreRecordKind,
  InvoiceCoreRecordOption,
  InvoiceSubmissionAccepted,
  InvoiceSubmissionCallbacks,
  InvoiceSubmissionStatusView,
} from './types';

/** The server's configured limit defaults to 25 MB (EbillingOptions.MaxSubmissionFileSizeMb); a convenience only. */
export const DEFAULT_MAX_INVOICE_FILE_BYTES = 25 * 1024 * 1024;

/** The longest firm invoice number the server accepts (tracking-number column). */
const FIRM_INVOICE_NUMBER_MAX = 89;
/** The longest PO number the server accepts. */
const PO_NUMBER_MAX = 100;

const KIND_LABEL: Readonly<Record<InvoiceCoreRecordKind, string>> = {
  matter: 'Matter',
  project: 'Project',
  workassignment: 'Work assignment',
};

const useStyles = makeStyles({
  // Under 600px the step list beside the content takes the whole screen: hide it (each step shows its own caption).
  compact: {
    height: '100%',
    '@media (max-width: 600px)': {
      '& nav[aria-label="Wizard steps"]': {
        display: 'none',
      },
    },
  },
  stepContent: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    minWidth: 0,
  },
  caption: {
    color: tokens.colorNeutralForeground3,
  },
  recordList: {
    maxHeight: '260px',
    overflowY: 'auto',
    paddingLeft: tokens.spacingHorizontalXS,
  },
  recordLabel: {
    overflowWrap: 'anywhere',
  },
  summary: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    overflowWrap: 'anywhere',
  },
  successIcon: {
    fontSize: '48px',
    color: tokens.colorBrandForeground1,
  },
});

export interface InvoiceSubmissionWizardProps extends InvoiceSubmissionCallbacks {
  /** Whether the wizard is shown. Default true. */
  open?: boolean;
  /**
   * Full-page layout with no modal envelope (see `WizardShell.embedded`). Use it for a page, and on phones: the
   * modal preset is a fraction of the viewport width.
   */
  embedded?: boolean;
  /** Wizard title. Default "Submit invoice". */
  title?: string;
  /** Called on Cancel, the close button, and Done on the result screen. */
  onClose: () => void;
  /**
   * Called once the server has accepted the submission (the 202), before the result screen shows. A host that wants
   * leaving and returning to resume the same submission records the id here (the SPA puts it in the URL).
   */
  onSubmitted?: (accepted: InvoiceSubmissionAccepted) => void;
  /** The host's slot for a Rejected submission's report. Absent: a placeholder (task 134 supplies the real one). */
  renderReport?: (status: InvoiceSubmissionStatusView) => React.ReactNode;
  /** Client-side size check in bytes (a convenience; the server's 413 is authoritative). Default 25 MB. */
  maxFileSizeBytes?: number;
  /** Milliseconds between status reads while processing. Default 2000. */
  pollIntervalMs?: number;
  /** Label of the button on the result screen. Default "Done". */
  doneLabel?: string;
}

/**
 * PDF only. FileUploadZone accepts a file whose extension OR MIME type is in its accepted set, and the Word / Excel
 * MIME types are always in it, so the extension list alone would let a .docx through: the custom validator closes that.
 * A convenience only; the server checks the declared type and the %PDF- header.
 */
function pdfOnly(file: File): string | null {
  const isPdfName = /\.pdf$/i.test(file.name);
  const isPdfType = file.type === '' || file.type === 'application/pdf';
  return isPdfName && isPdfType ? null : 'File type not supported. Only a PDF can be submitted.';
}

const PDF_VALIDATION = (maxFileSizeBytes: number) => ({
  acceptedExtensions: ['.pdf'],
  inputAccept: '.pdf,application/pdf',
  maxFileSizeBytes,
  customValidator: pdfOnly,
});

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function recordLabel(record: InvoiceCoreRecordOption): string {
  const number = record.number?.trim();
  return number && number !== record.name ? `${record.name} (${number})` : record.name;
}

const recordKey = (record: InvoiceCoreRecordOption): string => `${record.kind}:${record.recordId}`;

export const InvoiceSubmissionWizard: React.FC<InvoiceSubmissionWizardProps> = ({
  open = true,
  embedded = false,
  title = 'Submit invoice',
  onClose,
  onSubmitted,
  renderReport,
  maxFileSizeBytes = DEFAULT_MAX_INVOICE_FILE_BYTES,
  pollIntervalMs,
  doneLabel = 'Done',
  loadRecords,
  submit,
  getStatus,
}) => {
  const styles = useStyles();

  // ── Record step ─────────────────────────────────────────────────────────
  const [records, setRecords] = React.useState<InvoiceCoreRecordOption[] | null>(null);
  const [recordsTruncated, setRecordsTruncated] = React.useState(false);
  const [recordsError, setRecordsError] = React.useState<string | null>(null);
  const [recordFilter, setRecordFilter] = React.useState('');
  const [selected, setSelected] = React.useState<InvoiceCoreRecordOption | null>(null);

  const loadRecordsRef = React.useRef(loadRecords);
  loadRecordsRef.current = loadRecords;

  React.useEffect(() => {
    if (!open) return;
    let cancelled = false;
    setRecords(null);
    setRecordsError(null);
    loadRecordsRef
      .current()
      .then(result => {
        if (cancelled) return;
        setRecords(result.records);
        setRecordsTruncated(result.truncated === true);
      })
      .catch(err => {
        if (cancelled) return;
        setRecordsError(invoiceSubmissionErrorMessage(err));
      });
    return () => {
      cancelled = true;
    };
  }, [open]);

  const visibleRecords = React.useMemo(() => {
    const needle = recordFilter.trim().toLowerCase();
    if (!records) return [];
    if (!needle) return records;
    return records.filter(r => `${r.name} ${r.number ?? ''} ${KIND_LABEL[r.kind]}`.toLowerCase().includes(needle));
  }, [records, recordFilter]);

  // ── File step ───────────────────────────────────────────────────────────
  const [file, setFile] = React.useState<IUploadedFile | null>(null);
  const [fileNotices, setFileNotices] = React.useState<IFileValidationError[]>([]);

  const handleFilesAccepted = React.useCallback((files: IUploadedFile[]) => {
    const [first, ...rest] = files;
    if (!first) return;
    setFile(first);
    setFileNotices(
      rest.map(extra => ({
        fileName: extra.name,
        reason: 'Only one PDF can be submitted at a time. This file was not used.',
      }))
    );
  }, []);

  const handleFileErrors = React.useCallback((errors: IFileValidationError[]) => setFileNotices(errors), []);

  const handleRemoveFile = React.useCallback(() => {
    setFile(null);
    setFileNotices([]);
  }, []);

  // ── Details step ────────────────────────────────────────────────────────
  const [firmInvoiceNumber, setFirmInvoiceNumber] = React.useState('');
  const [poNumber, setPoNumber] = React.useState('');
  const firmInvoiceNumberTrimmed = firmInvoiceNumber.trim();

  // ── Finish: submit, then mount the Processing / Result screen ───────────
  const handleFinish = React.useCallback(async (): Promise<IWizardSuccessConfig | void> => {
    if (!selected || !file || !firmInvoiceNumberTrimmed) return;

    let accepted: InvoiceSubmissionAccepted;
    try {
      accepted = await submit({
        record: selected,
        file: file.file,
        firmInvoiceNumber: firmInvoiceNumberTrimmed,
        poNumber: poNumber.trim() || undefined,
      });
    } catch (err) {
      // WizardShell shows a thrown message above the step and stays on it: the flow does not advance.
      throw new Error(invoiceSubmissionErrorMessage(err));
    }
    onSubmitted?.(accepted);

    return {
      icon: <DocumentCheckmarkRegular className={styles.successIcon} />,
      title: 'Invoice received',
      body: (
        <InvoiceSubmissionOutcome
          submissionId={accepted.submissionId}
          trackingNumber={accepted.trackingNumber}
          getStatus={getStatus}
          pollIntervalMs={pollIntervalMs}
          renderReport={renderReport}
        />
      ),
      actions: (
        <Button appearance="primary" onClick={onClose}>
          {doneLabel}
        </Button>
      ),
    };
  }, [
    selected,
    file,
    firmInvoiceNumberTrimmed,
    poNumber,
    submit,
    onSubmitted,
    styles.successIcon,
    getStatus,
    pollIntervalMs,
    renderReport,
    onClose,
    doneLabel,
  ]);

  // ── Steps ───────────────────────────────────────────────────────────────
  const steps: IWizardStepConfig[] = [
    {
      id: 'record',
      label: 'Record',
      renderContent: () => (
        <div className={styles.stepContent}>
          <Text size={200} className={styles.caption}>
            Step 1 of 3
          </Text>
          <Text size={400} weight="semibold">
            Choose the record this invoice is for
          </Text>

          {recordsError && (
            <MessageBar intent="error" role="alert">
              <MessageBarBody>{recordsError}</MessageBarBody>
            </MessageBar>
          )}
          {!recordsError && records === null && <Spinner size="small" label="Loading your records" />}
          {records !== null && records.length === 0 && (
            <MessageBar intent="info">
              <MessageBarBody>You do not have a record to submit an invoice against.</MessageBarBody>
            </MessageBar>
          )}
          {records !== null && records.length > 0 && (
            <>
              <Field label="Find a record">
                <Input value={recordFilter} onChange={(_e, data) => setRecordFilter(data.value)} />
              </Field>
              {recordsTruncated && (
                <MessageBar intent="warning">
                  <MessageBarBody>Only part of your list is shown. Use the search box to narrow it.</MessageBarBody>
                </MessageBar>
              )}
              <div className={styles.recordList}>
                <RadioGroup
                  aria-label="Records"
                  value={selected ? recordKey(selected) : ''}
                  onChange={(_e, data) => setSelected(records.find(r => recordKey(r) === data.value) ?? null)}
                >
                  {visibleRecords.map(record => (
                    <Radio
                      key={recordKey(record)}
                      value={recordKey(record)}
                      label={
                        <span
                          className={styles.recordLabel}
                        >{`${recordLabel(record)} · ${KIND_LABEL[record.kind]}`}</span>
                      }
                    />
                  ))}
                </RadioGroup>
                {visibleRecords.length === 0 && <Text size={200}>No record matches that search.</Text>}
              </div>
            </>
          )}
        </div>
      ),
      canAdvance: () => selected !== null,
    },
    {
      id: 'file',
      label: 'File',
      renderContent: () => (
        <div className={styles.stepContent}>
          <Text size={200} className={styles.caption}>
            Step 2 of 3
          </Text>
          <Text size={400} weight="semibold">
            Upload the invoice PDF
          </Text>
          <Text size={300}>Drag a PDF here or browse (one file, up to {formatBytes(maxFileSizeBytes)}).</Text>
          <FileUploadZone
            onFilesAccepted={handleFilesAccepted}
            onValidationErrors={handleFileErrors}
            validationConfig={PDF_VALIDATION(maxFileSizeBytes)}
          />
          {fileNotices.map((notice, i) => (
            <MessageBar key={i} intent="warning">
              <MessageBarBody>
                {notice.fileName}: {notice.reason}
              </MessageBarBody>
            </MessageBar>
          ))}
          {file && <UploadedFileList files={[file]} onRemove={handleRemoveFile} />}
        </div>
      ),
      canAdvance: () => file !== null,
    },
    {
      id: 'details',
      label: 'Details',
      renderContent: () => (
        <div className={styles.stepContent}>
          <Text size={200} className={styles.caption}>
            Step 3 of 3
          </Text>
          <Text size={400} weight="semibold">
            Invoice details
          </Text>
          <Field label="Firm invoice number" required hint="The number on the invoice as your firm issued it.">
            <Input
              value={firmInvoiceNumber}
              maxLength={FIRM_INVOICE_NUMBER_MAX}
              onChange={(_e, data) => setFirmInvoiceNumber(data.value)}
            />
          </Field>
          <Field label="PO number (optional)">
            <Input value={poNumber} maxLength={PO_NUMBER_MAX} onChange={(_e, data) => setPoNumber(data.value)} />
          </Field>
          <div className={styles.summary}>
            <Text size={200} weight="semibold">
              Summary
            </Text>
            <Text size={200}>Record: {selected ? `${recordLabel(selected)} (${KIND_LABEL[selected.kind]})` : '-'}</Text>
            <Text size={200}>File: {file ? `${file.name} (${formatBytes(file.sizeBytes)})` : '-'}</Text>
          </div>
        </div>
      ),
      canAdvance: () => selected !== null && file !== null && firmInvoiceNumberTrimmed.length > 0,
    },
  ];

  return (
    <div className={styles.compact}>
      <WizardShell
        open={open}
        embedded={embedded}
        title={title}
        steps={steps}
        onClose={onClose}
        onFinish={handleFinish}
        finishLabel="Submit invoice"
        finishingLabel="Submitting..."
      />
    </div>
  );
};

InvoiceSubmissionWizard.displayName = 'InvoiceSubmissionWizard';
