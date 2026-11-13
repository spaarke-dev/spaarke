/**
 * InvoiceSubmissionWizard — the shared "submit an invoice PDF" wizard (ebilling-r1 task 051, FR-18 / FR-19).
 *
 * One component for every entry point: the external SPA page (task 051) and the internal CreateInvoiceWizard rewire
 * (task 052) inject the same three callbacks and get the same flow. It composes the shared `WizardShell`,
 * `FileUploadZone` / `UploadedFileList` and `StatusBadge`; it holds no SPA auth, Xrm or BFF URL (ADR-012).
 *
 * Steps:
 *   0. Firm    — ONLY when `loadFirms` resolves with more than one firm: a required choice of which firm the invoice is
 *                from; the choice goes to `submit` as `firmId`. With one firm (or none, or no `loadFirms`) there is no
 *                step and no `firmId` is sent: the server uses the caller's single firm itself.
 *                When the server answers `firm_required` although no step was shown (the firm list was unknown or stale),
 *                the wizard asks `loadFirms` again and inserts the step, keeping the file and the details.
 *   1. Record  — choose an accessible core record (matter / project / work assignment), from `loadRecords`. The host is
 *                asked for the records linked to the invoice's firm (D-106: the one firm, or the chosen one), and again
 *                when the choice changes. A capped or partly failed read is said on screen, not hidden.
 *              With `lockedRecord` (the host fixed the record) there is NO Record step: the host's `loadRecords` is still
 *              asked for the firm's records, and the first step after the firm step shows a check instead of the upload
 *              until the locked record is among a complete list (see {@link InvoiceSubmissionWizardProps.lockedRecord}).
 *   2. File    — one PDF. Type and size are checked here only as a convenience; the server is the authority.
 *   3. Details — the firm invoice number (REQUIRED, D-91) and an optional PO number. Finish submits.
 *   4. Processing then Result — the success screen mounts {@link InvoiceSubmissionOutcome}, which polls `getStatus`
 *      and shows Accepted (tracking number), Rejected (reason + the host's report slot) or Failed (retry guidance).
 *
 * The client never creates a submission or invoice itself and never decides authorization (ADR-002 / FR-17): a
 * refusal from `submit` is shown by its error code and the wizard stays where it is.
 *
 * Phone width: `WizardShell` draws a fixed-width step list beside the content. The `compact` styles below hide it
 * under 600px (each step carries its own "Step n of N" caption), so a host should mount with `embedded` there.
 *
 * ADR-021: Fluent v9 tokens only, no hard-coded colour. Dark mode follows the host's FluentProvider.
 */
import * as React from 'react';
import {
  Button,
  Field,
  Input,
  MessageBar,
  MessageBarActions,
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
import { FIRM_REQUIRED_ERROR_CODE, InvoiceSubmissionError } from './types';
import type {
  InvoiceCoreRecordKind,
  InvoiceCoreRecordOption,
  InvoiceFirmOption,
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

/** Shown on the "Choose firm" step when the server's `firm_required` brought it in after the first firm list said there was none to choose. */
const FIRM_CHOICE_NOTICE = 'Your account belongs to more than one firm. Choose the firm this invoice is from.';

/** Shown (with a locked record) when the firm's COMPLETE record list does not include the locked record. Fixed text. */
export const LOCKED_RECORD_MISSING_MESSAGE = "This record can't take invoices for the selected firm.";

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
  /**
   * With `embedded`: hide the shell's own title bar and close button, for a host that supplies its own header and close
   * (the SPA mounts the wizard inside a `SprkModal` and wires that modal's close to `onClose`). Ignored when not embedded.
   * Default false.
   */
  hideTitle?: boolean;
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
  /**
   * Optional (task 052): a record to start the Record step with already chosen (the internal wizard, launched from a
   * record). It stays chosen while the host's `loadRecords` still offers it; otherwise it is dropped like any other choice.
   */
  initialRecord?: InvoiceCoreRecordOption;
  /**
   * Optional: the record this invoice is for, fixed by the host (a host that opens the wizard from a record). Same
   * option type and identity as {@link initialRecord}: `kind` + `recordId` (the id compared without regard to case).
   *
   * ABSENT: the wizard behaves exactly as before. PRESENT:
   * - there is no Record step and no record picker anywhere; the submit callback always receives this record;
   * - `loadRecords` still decides whether the record can take an invoice: the wizard asks for the firm's records
   *   (per firm, as before) and the locked record must be among them. When the firm's list is COMPLETE (neither
   *   `truncated` nor `partial`) and lacks it, the wizard says "This record can't take invoices for the selected firm."
   *   and `submit` is never called. When the read failed, or the list is truncated or partial and lacks the record, the
   *   wizard cannot tell, so it shows the read message with Retry (Back and Cancel stay in the footer) instead;
   * - the check runs again on every firm change (a message is cleared and an older firm's list never counts);
   * - the captions count the steps without the Record step: "Step n of 2", or "Step n of 3" with the firm step.
   *
   * If both `lockedRecord` and `initialRecord` are given, `lockedRecord` wins and `initialRecord` is ignored.
   */
  lockedRecord?: InvoiceCoreRecordOption;
  /** Optional (task 052): passed to the result screen (`InvoiceSubmissionOutcome.keepPolling`). */
  keepPolling?: (status: InvoiceSubmissionStatusView) => boolean;
  /** Optional (task 052): passed to the result screen (`InvoiceSubmissionOutcome.renderAccepted`). */
  renderAccepted?: (status: InvoiceSubmissionStatusView) => React.ReactNode;
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
/** The locked record's identity: kind + id, the id compared without regard to case (a GUID has no case). */
const lockedKey = (record: InvoiceCoreRecordOption): string => `${record.kind}:${record.recordId.toLowerCase()}`;

export const InvoiceSubmissionWizard: React.FC<InvoiceSubmissionWizardProps> = ({
  open = true,
  embedded = false,
  hideTitle = false,
  title = 'Submit invoice',
  onClose,
  onSubmitted,
  renderReport,
  maxFileSizeBytes = DEFAULT_MAX_INVOICE_FILE_BYTES,
  pollIntervalMs,
  doneLabel = 'Done',
  initialRecord,
  lockedRecord,
  keepPolling,
  renderAccepted,
  loadRecords,
  loadFirms,
  submit,
  getStatus,
}) => {
  const styles = useStyles();

  // ── Firm step (only for a caller with more than one firm) ───────────────
  // WizardShell fixes its step list when it mounts, so the shell is not mounted until the firm list is known: the step
  // either exists from the first frame or never does. A failed load means "no step" (the server answers `firm_required`
  // for a multi-firm caller it cannot resolve, and that message is shown on submit).
  const [firms, setFirms] = React.useState<InvoiceFirmOption[] | null>(loadFirms ? null : []);
  const [firmId, setFirmId] = React.useState('');
  // Bumped to ask the host for the firms again (K1: the server answered `firm_required`, so the first answer was wrong or unknown).
  const [firmsReload, setFirmsReload] = React.useState(0);
  // The message to show on the first step after a `firm_required` reload: the server's own words when the reload finds no choice to make.
  const firmRequiredMessage = React.useRef<string | null>(null);
  // Shown at the top of the first step (a refusal the wizard answered by changing its steps, not by staying on Details).
  const [notice, setNotice] = React.useState<string | null>(null);

  const loadFirmsRef = React.useRef(loadFirms);
  loadFirmsRef.current = loadFirms;
  const hasLoadFirms = loadFirms !== undefined;

  React.useEffect(() => {
    const load = loadFirmsRef.current;
    if (!open || !load) return;
    let cancelled = false;
    const settle = (result: InvoiceFirmOption[]) => {
      if (cancelled) return;
      const pending = firmRequiredMessage.current;
      if (pending !== null) {
        firmRequiredMessage.current = null;
        setNotice(result.length > 1 ? FIRM_CHOICE_NOTICE : pending);
      }
      setFirms(result);
    };
    // `null` is "unknown", which is not "no firm": it leaves no step, and the server's `firm_required` can still bring the step in.
    load()
      .then(result => settle(result ?? []))
      .catch(() => settle([]));
    return () => {
      cancelled = true;
    };
  }, [open, hasLoadFirms, firmsReload]);

  const needsFirmStep = (firms?.length ?? 0) > 1;
  const chosenFirm = needsFirmStep ? (firms?.find(f => f.firmId === firmId) ?? null) : null;
  // A locked record removes the Record step, so the steps after it count from one less.
  const totalSteps = (needsFirmStep ? 1 : 0) + (lockedRecord ? 2 : 3);
  // `n` is the step's number among the steps after the firm step, counted as if the Record step existed (Record 1, File 2, Details 3).
  const stepCaption = (n: number): string =>
    `Step ${(needsFirmStep ? 1 : 0) + (lockedRecord ? n - 1 : n)} of ${totalSteps}`;

  // ── Record step ─────────────────────────────────────────────────────────
  // The records are those linked to the invoice's firm (D-106): the chosen firm, or the one firm of a single-firm caller.
  // With several firms none is known until one is chosen, so nothing is loaded before that; with the firms unknown the host is
  // asked without a firm and the server still checks the link on submit.
  const [records, setRecords] = React.useState<InvoiceCoreRecordOption[] | null>(null);
  const [recordsTruncated, setRecordsTruncated] = React.useState(false);
  const [recordsPartial, setRecordsPartial] = React.useState(false);
  const [recordsError, setRecordsError] = React.useState<string | null>(null);
  const [recordFilter, setRecordFilter] = React.useState('');
  const [selected, setSelected] = React.useState<InvoiceCoreRecordOption | null>(initialRecord ?? null);
  // Which firm the current `records` / `recordsError` answer was for ('' = no firm). Only the locked-record check reads it: a
  // list or error that belongs to another firm is never counted (it is "still loading").
  const [recordsFirmKey, setRecordsFirmKey] = React.useState<string | null>(null);
  // Bumped by the locked-record Retry button to ask the host for the records again.
  const [recordsReload, setRecordsReload] = React.useState(0);

  const loadRecordsRef = React.useRef(loadRecords);
  loadRecordsRef.current = loadRecords;
  const firmsKnown = firms !== null;
  const recordFirmId = needsFirmStep ? chosenFirm?.firmId : firms?.length === 1 ? firms[0].firmId : undefined;

  React.useEffect(() => {
    if (!open || !firmsKnown) return;
    setRecords(null);
    setRecordsError(null);
    if (needsFirmStep && !recordFirmId) return;
    let cancelled = false;
    const firmKey = recordFirmId ?? '';
    loadRecordsRef
      .current(recordFirmId ? { firmId: recordFirmId } : {})
      .then(result => {
        if (cancelled) return;
        setRecords(result.records);
        setRecordsTruncated(result.truncated === true);
        setRecordsPartial(result.partial === true);
        // A record picked under another firm (or no firm) that is not offered now is dropped.
        setSelected(prev => (prev && result.records.some(r => recordKey(r) === recordKey(prev)) ? prev : null));
        setRecordsFirmKey(firmKey);
      })
      .catch(err => {
        if (cancelled) return;
        setRecordsError(invoiceSubmissionErrorMessage(err));
        setRecordsFirmKey(firmKey);
      });
    return () => {
      cancelled = true;
    };
  }, [open, firmsKnown, needsFirmStep, recordFirmId, recordsReload]);

  // ── Locked record (the host fixed the record: no Record step) ───────────
  // The check is derived, never stored, so it cannot go stale: it reads only an answer that belongs to the current firm.
  //   loading     — the firm is not chosen yet, or its records have not arrived;
  //   ok          — the firm's list offers the locked record (kind + id), complete or not;
  //   missing     — a COMPLETE list (neither truncated nor partial) lacks it: the record cannot take invoices for this firm;
  //   unreadable  — the read threw, or the list is truncated / partial and lacks it: it is unknown, so not "missing".
  type LockedCheck = 'loading' | 'ok' | 'missing' | 'unreadable';
  let lockedCheck: LockedCheck | null = null;
  if (lockedRecord) {
    const answerIsCurrent = firmsKnown && !(needsFirmStep && !recordFirmId) && recordsFirmKey === (recordFirmId ?? '');
    if (!answerIsCurrent) lockedCheck = 'loading';
    else if (recordsError) lockedCheck = 'unreadable';
    else if (records === null) lockedCheck = 'loading';
    else if (records.some(r => lockedKey(r) === lockedKey(lockedRecord))) lockedCheck = 'ok';
    else lockedCheck = recordsTruncated || recordsPartial ? 'unreadable' : 'missing';
  }
  // The record a submit carries: the locked one once the check passed (it wins over `initialRecord` and any pick), else the pick.
  const chosenRecord: InvoiceCoreRecordOption | null = lockedRecord
    ? lockedCheck === 'ok'
      ? lockedRecord
      : null
    : selected;

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
    if (files.length === 0) return;
    const [first, ...rest] = files;
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
    if (!chosenRecord || !file || !firmInvoiceNumberTrimmed) return;
    // A caller with several firms must have chosen one: never submit without it (the server would answer firm_required).
    if (needsFirmStep && !chosenFirm) return;

    setNotice(null);
    let accepted: InvoiceSubmissionAccepted;
    try {
      accepted = await submit({
        record: chosenRecord,
        file: file.file,
        firmInvoiceNumber: firmInvoiceNumberTrimmed,
        poNumber: poNumber.trim() || undefined,
        ...(chosenFirm ? { firmId: chosenFirm.firmId } : {}),
      });
    } catch (err) {
      const message = invoiceSubmissionErrorMessage(err);
      if (err instanceof InvoiceSubmissionError && err.errorCode === FIRM_REQUIRED_ERROR_CODE && loadFirmsRef.current) {
        // K1: the server says the caller has several firms although the first answer showed none to choose (unknown, or stale).
        // Ask again; with several firms the shell is rebuilt with the "Choose firm" step first and the file and details are kept.
        firmRequiredMessage.current = message;
        setFirms(null);
        setFirmsReload(n => n + 1);
      }
      // WizardShell shows a thrown message above the step and stays on it: the flow does not advance.
      throw new Error(message);
    }
    onSubmitted?.(accepted);

    return {
      icon: <DocumentCheckmarkRegular className={styles.successIcon} />,
      title: 'Invoice received',
      body: (
        <InvoiceSubmissionOutcome
          submissionId={accepted.submissionId}
          trackingNumber={accepted.trackingNumber}
          submittedWarning={accepted.warning}
          getStatus={getStatus}
          pollIntervalMs={pollIntervalMs}
          renderReport={renderReport}
          keepPolling={keepPolling}
          renderAccepted={renderAccepted}
        />
      ),
      actions: (
        <Button appearance="primary" onClick={onClose}>
          {doneLabel}
        </Button>
      ),
    };
  }, [
    chosenRecord,
    file,
    firmInvoiceNumberTrimmed,
    poNumber,
    needsFirmStep,
    chosenFirm,
    submit,
    onSubmitted,
    styles.successIcon,
    getStatus,
    pollIntervalMs,
    renderReport,
    keepPolling,
    renderAccepted,
    onClose,
    doneLabel,
  ]);

  // ── Steps ───────────────────────────────────────────────────────────────
  const firmStep: IWizardStepConfig = {
    id: 'firm',
    label: 'Firm',
    renderContent: () => (
      <div className={styles.stepContent}>
        <Text size={200} className={styles.caption}>
          {`Step 1 of ${totalSteps}`}
        </Text>
        <Text size={400} weight="semibold">
          Choose the firm this invoice is from
        </Text>
        {notice && (
          <MessageBar intent="warning" role="alert">
            <MessageBarBody>{notice}</MessageBarBody>
          </MessageBar>
        )}
        <Text size={300}>Your account belongs to more than one firm. Pick the one that issued this invoice.</Text>
        <div className={styles.recordList}>
          <RadioGroup
            aria-label="Firms"
            value={firmId}
            onChange={(_e, data) => {
              setFirmId(data.value);
              setNotice(null);
            }}
          >
            {(firms ?? []).map(firm => (
              <Radio
                key={firm.firmId}
                value={firm.firmId}
                label={<span className={styles.recordLabel}>{firm.name}</span>}
              />
            ))}
          </RadioGroup>
        </div>
      </div>
    ),
    canAdvance: () => chosenFirm !== null,
  };

  const recordStep: IWizardStepConfig = {
    id: 'record',
    label: 'Record',
    renderContent: () => (
      <div className={styles.stepContent}>
        <Text size={200} className={styles.caption}>
          {stepCaption(1)}
        </Text>
        <Text size={400} weight="semibold">
          Choose the record this invoice is for
        </Text>

        {notice && !needsFirmStep && (
          <MessageBar intent="warning" role="alert">
            <MessageBarBody>{notice}</MessageBarBody>
          </MessageBar>
        )}
        {recordsError && (
          <MessageBar intent="error" role="alert">
            <MessageBarBody>{recordsError}</MessageBarBody>
          </MessageBar>
        )}
        {!recordsError && records === null && <Spinner size="small" label="Loading your records" />}
        {records !== null && recordsPartial && (
          <MessageBar intent="warning" data-testid="records-partial">
            <MessageBarBody>
              Some of your records could not be loaded, so this list is incomplete. Try again in a moment.
            </MessageBarBody>
          </MessageBar>
        )}
        {records !== null && recordsTruncated && (
          <MessageBar intent="warning" data-testid="records-truncated">
            <MessageBarBody>
              Only the first part of your list is shown, and the search box searches only those records. If the record
              you need is missing, contact your administrator.
            </MessageBarBody>
          </MessageBar>
        )}
        {records !== null && records.length === 0 && (
          <MessageBar intent="info">
            <MessageBarBody>
              {recordFirmId
                ? 'No record that you can submit an invoice against is linked to this firm.'
                : 'You do not have a record to submit an invoice against.'}
            </MessageBarBody>
          </MessageBar>
        )}
        {records !== null && records.length > 0 && (
          <>
            <Field label="Find a record">
              <Input value={recordFilter} onChange={(_e, data) => setRecordFilter(data.value)} />
            </Field>
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
  };

  // With a locked record the check above is shown here, on the first step after the firm step, in place of the upload: the
  // user can go Back to another firm (when there is a firm step) or Cancel, and nothing is submitted until the record passes.
  const renderLockedGate = (): React.ReactNode => {
    if (lockedCheck === 'loading') return <Spinner size="small" label="Checking this record" />;
    if (lockedCheck === 'missing') {
      return (
        <MessageBar intent="warning" role="alert" data-testid="locked-record-missing">
          <MessageBarBody>{LOCKED_RECORD_MISSING_MESSAGE}</MessageBarBody>
        </MessageBar>
      );
    }
    // 'unreadable': today's read messages (the failure text, or the partial / truncated note), with a Retry.
    const retry = (
      <MessageBarActions>
        <Button size="small" onClick={() => setRecordsReload(n => n + 1)}>
          Retry
        </Button>
      </MessageBarActions>
    );
    if (recordsError) {
      return (
        <MessageBar intent="error" role="alert" data-testid="locked-record-read-failed">
          <MessageBarBody>{recordsError}</MessageBarBody>
          {retry}
        </MessageBar>
      );
    }
    if (recordsPartial) {
      return (
        <MessageBar intent="warning" role="alert" data-testid="records-partial">
          <MessageBarBody>
            Some of your records could not be loaded, so this list is incomplete. Try again in a moment.
          </MessageBarBody>
          {retry}
        </MessageBar>
      );
    }
    return (
      <MessageBar intent="warning" role="alert" data-testid="records-truncated">
        <MessageBarBody>
          Only the first part of your list could be read, so this record could not be checked. Try again, or contact
          your administrator.
        </MessageBarBody>
        {retry}
      </MessageBar>
    );
  };

  const fileStep: IWizardStepConfig = {
    id: 'file',
    label: 'File',
    renderContent: () => (
      <div className={styles.stepContent}>
        <Text size={200} className={styles.caption}>
          {stepCaption(2)}
        </Text>
        {/* With a locked record there is no Record step to carry a refusal's notice (firm_required with no choice to make). */}
        {lockedRecord && notice && !needsFirmStep && (
          <MessageBar intent="warning" role="alert" data-testid="locked-record-notice">
            <MessageBarBody>{notice}</MessageBarBody>
          </MessageBar>
        )}
        {lockedCheck !== null && lockedCheck !== 'ok' ? (
          renderLockedGate()
        ) : (
          <>
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
          </>
        )}
      </div>
    ),
    canAdvance: () => file !== null && (lockedCheck === null || lockedCheck === 'ok'),
  };

  const steps: IWizardStepConfig[] = [
    ...(needsFirmStep ? [firmStep] : []),
    ...(lockedRecord ? [] : [recordStep]),
    fileStep,
    {
      id: 'details',
      label: 'Details',
      renderContent: () => (
        <div className={styles.stepContent}>
          <Text size={200} className={styles.caption}>
            {stepCaption(3)}
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
            {chosenFirm && <Text size={200}>Firm: {chosenFirm.name}</Text>}
            <Text size={200}>
              Record: {chosenRecord ? `${recordLabel(chosenRecord)} (${KIND_LABEL[chosenRecord.kind]})` : '-'}
            </Text>
            <Text size={200}>File: {file ? `${file.name} (${formatBytes(file.sizeBytes)})` : '-'}</Text>
          </div>
        </div>
      ),
      canAdvance: () =>
        chosenRecord !== null &&
        file !== null &&
        firmInvoiceNumberTrimmed.length > 0 &&
        (!needsFirmStep || chosenFirm !== null),
    },
  ];

  // The shell fixes its steps at mount: wait for the firm list (see above).
  if (firms === null) {
    return open ? (
      <div className={styles.compact}>
        <Spinner size="small" label="Loading" />
      </div>
    ) : null;
  }

  return (
    <div className={styles.compact}>
      <WizardShell
        open={open}
        embedded={embedded}
        hideTitle={hideTitle}
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
