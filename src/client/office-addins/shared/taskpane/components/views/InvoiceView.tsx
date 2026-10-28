import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Button,
  Field,
  Input,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  MessageBarTitle,
  Radio,
  RadioGroup,
  Select,
  Spinner,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { ReceiptRegular } from '@fluentui/react-icons';
import type { IHostAdapter } from '@shared/adapters';
import type { EntitySearchResult, EntityType } from '../../hooks/useEntitySearch';
import { useAnnounce } from '../../hooks/useAnnounce';
import { RelatedToPicker } from '../RelatedToPicker';
import { searchOfficeEntities } from '../../services/officeEntitySearch';
import {
  INVOICE_CORE_RECORD_TYPES,
  buildInvoiceSubmissionInput,
  normalizeFirmInvoiceNumber,
  toInvoiceMarkingAttachments,
  applyMarkingBudget,
  describeOverBudget,
  isPdfAttachment,
  FIRM_INVOICE_NUMBER_MAX,
  PO_NUMBER_MAX,
  type InvoiceCoreRecordType,
  type InvoiceFirmOption,
  type InvoiceSubmissionInput,
  type InvoiceSubmissionOutcome,
  type InvoiceListedAttachment,
  type InvoiceMarkingValue,
  type InvoiceSubmitHandler,
  type LoadInvoiceFirms,
} from '../../services/invoiceSubmissionSeam';
import {
  InvoiceAttachmentMarking,
  defaultInvoiceMarking,
  validateInvoiceMarking,
} from '@spaarke/communication-components/invoice-attachment-marking';

/**
 * InvoiceView — Outlook's "Submit Invoice" view (task 133, owner decisions 2026-10-21; contract
 * `projects/ebilling-r1/notes/095-from-email-contract.md`, pinned 2026-10-27).
 *
 * Reached ONLY through the ribbon's `?action=submitInvoice` (outlook/taskpane/index.tsx → App `initialAction`); no tab,
 * link or menu in the default pane leads here (slice A must be inert in the deployed pane).
 *
 * - New invoice: the core record (Matter, Project or Work Assignment — required), the firm (required; its options
 *   come from {@link InvoiceViewProps.loadFirms}), the firm's invoice number (required, 1-100), an optional PO number,
 *   and the email's attachments marked Invoice / Supporting / Don't save.
 * - Existing invoice: pick the invoice, then submit a corrected invoice or add supporting files. Both are shown
 *   disabled with their reason: corrections until the server supports them ({@link InvoiceViewProps.correctionAvailable},
 *   ebilling 097), supporting files until the upload route exists (ebilling 123; its contract is agreed, the route is
 *   not built, so the add-in does not call it yet).
 *
 * Submit goes through the injected {@link InvoiceViewProps.onSubmit} seam (App wires `invoiceSubmissionService`);
 * without it the view says submitting is not available and Submit stays disabled. Once the server accepts the
 * submission the view shows it processing until the final status is known. Results are shown generically (success /
 * refused / retry / use Spaarke instead / ended after acceptance) from {@link InvoiceSubmissionOutcome}; the view
 * never reads a reason code. A Retry sends exactly what was last submitted: changing any field or mark ends the retry
 * (the next Submit is a new attempt, with a new action id). Leaving the view aborts a running submission's polling.
 *
 * The attachment marking is ebilling's shared `InvoiceAttachmentMarking` (`value` always holds every listed id, seeded
 * from `defaultInvoiceMarking`; `validateInvoiceMarking` always gets the attachments).
 *
 * Search reuses the Save picker's component (`RelatedToPicker`) and search (`searchOfficeEntities`); the attachment
 * list comes from the host adapter as Save's does, and the content is captured at submit by A1 (no second capture).
 * Fluent UI v9 + Griffel + semantic tokens only (ADR-021).
 */
export interface InvoiceViewProps {
  /** Host adapter — lists the open email's attachments. */
  hostAdapter: IHostAdapter;
  /** BFF base URL for the record searches. */
  apiBaseUrl?: string;
  /** Token getter for the record searches. */
  getAccessToken?: () => Promise<string>;
  /** The from-email submission (contract 095 §2-§3). Absent → submitting is not available. */
  onSubmit?: InvoiceSubmitHandler;
  /** The firms linked to the chosen core record (contract 095 §8). Absent → none can be listed. */
  loadFirms?: LoadInvoiceFirms;
  /** Whether "Submit a corrected invoice" can be used (the server refuses corrections until ebilling 097). */
  correctionAvailable?: boolean;
  /**
   * Called with true while a submission is running, or its result (one the server accepted: submitted, rejected or
   * failed, with its tracking number) is showing; false otherwise and on unmount. The pane hides its tab bar while
   * true, so the user cannot walk away from a running submission or its tracking number.
   */
  onBusyChange?: (busy: boolean) => void;
}

/** The hint when Outlook cannot submit this invoice (permanent failure, or a retry that keeps failing). */
export const INVOICE_UPLOAD_HINT = 'You can still submit this invoice in Spaarke by uploading it there.';

/** Retryable failures in a row before the view also suggests uploading in Spaarke. */
const RETRIES_BEFORE_HINT = 2;

const ENDED_TITLES: Record<'Rejected' | 'Failed', string> = {
  Rejected: 'The invoice was rejected',
  Failed: 'The invoice could not be processed',
};

type AttachmentLoad =
  | { status: 'loading' }
  | { status: 'unsupported' }
  | { status: 'error'; message: string }
  | { status: 'ready'; attachments: InvoiceListedAttachment[] };

type FirmLoad =
  | { status: 'none' }
  | { status: 'unavailable' }
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; firms: InvoiceFirmOption[] };

type InvoiceMode = 'new' | 'existing';

/** Adding supporting files to an existing invoice needs ebilling 123's upload route, which is not built yet. */
const SUPPORTING_FILES_AVAILABLE = false;

function newActionId(): string {
  const random = globalThis.crypto?.randomUUID?.();
  return random ?? `${Date.now().toString(16)}-${Math.random().toString(16).slice(2)}`;
}

function isCoreRecordType(type: EntityType): type is InvoiceCoreRecordType {
  return (INVOICE_CORE_RECORD_TYPES as EntityType[]).includes(type);
}

const NO_CANDIDATES: never[] = [];
const NO_ATTACHMENTS: InvoiceListedAttachment[] = [];

type MarkingProblem = 'no-invoice' | 'more-than-one-invoice' | 'invoice-not-allowed' | 'disabled-attachment-marked';

/** The marking component's validation reason, for the user. */
function describeMarkingProblem(reason: MarkingProblem, attachments: readonly InvoiceListedAttachment[]): string {
  switch (reason) {
    case 'no-invoice':
      if (attachments.some(a => !a.disabledReason && isPdfAttachment(a))) return 'Mark the PDF that is the invoice.';
      // A PDF held back only by the size budget can be marked once another file is set to Don't save.
      if (attachments.some(a => a.budgetDisabled && isPdfAttachment(a))) {
        return "The invoice PDF does not fit with the other marked files. Set another file to Don't save to make room.";
      }
      return 'The invoice must be a PDF attached to this email, and this email has none that can be submitted.';
    case 'more-than-one-invoice':
      return 'Only one file can be the invoice.';
    case 'invoice-not-allowed':
      return 'Only a PDF that can be submitted can be the invoice.';
    case 'disabled-attachment-marked':
      return "A file that can't be submitted is marked to be saved. Mark it Don't save.";
  }
}

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
    padding: tokens.spacingVerticalM,
  },
  header: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  section: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS },
  sectionLabel: { color: tokens.colorNeutralForeground2 },
  note: { color: tokens.colorNeutralForeground3 },
  footer: { display: 'flex', justifyContent: 'flex-end', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
});

export const InvoiceView: React.FC<InvoiceViewProps> = ({
  hostAdapter,
  apiBaseUrl,
  getAccessToken,
  onSubmit,
  loadFirms,
  correctionAvailable = false,
  onBusyChange,
}) => {
  const styles = useStyles();
  const { announce, liveRegion } = useAnnounce();

  const [load, setLoad] = useState<AttachmentLoad>({ status: 'loading' });
  const [marking, setMarking] = useState<InvoiceMarkingValue>({});
  const [mode, setMode] = useState<InvoiceMode>('new');
  const [correctionChosen, setCorrectionChosen] = useState(false);
  const [coreRecord, setCoreRecord] = useState<EntitySearchResult | null>(null);
  const [invoice, setInvoice] = useState<EntitySearchResult | null>(null);
  const [firms, setFirms] = useState<FirmLoad>({ status: 'none' });
  const [firmId, setFirmId] = useState('');
  const [firmInvoiceNumber, setFirmInvoiceNumber] = useState('');
  const [poNumber, setPoNumber] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [outcome, setOutcome] = useState<InvoiceSubmissionOutcome | null>(null);
  /** Set between the server's 202 and the submission's final status. */
  const [processing, setProcessing] = useState<{ trackingNumber?: string } | null>(null);
  const [retryableInARow, setRetryableInARow] = useState(0);
  /** Bumped by the firm list's Retry; the firm effect runs again. */
  const [firmsReload, setFirmsReload] = useState(0);
  /** Bumped when a finished submission is dismissed, so the record picker starts empty for the next one. */
  const [formKey, setFormKey] = useState(0);
  const lastAttemptRef = useRef<{ input: InvoiceSubmissionInput; actionId: string } | null>(null);
  const firmRequestRef = useRef(0);
  /** The running submission's abort controller; aborted when the view unmounts. */
  const abortRef = useRef<AbortController | null>(null);
  const onBusyChangeRef = useRef(onBusyChange);
  onBusyChangeRef.current = onBusyChange;

  // Leaving the view (unmount) stops a running submission's status polling.
  useEffect(
    () => () => {
      abortRef.current?.abort();
    },
    []
  );

  // The open email's attachments, listed once (content is read at submit by the A1 service). Capability-gated, never
  // a hostType check (NFR-10): `canGetAttachments` is Outlook read mode.
  useEffect(() => {
    let active = true;
    void (async () => {
      if (!hostAdapter.getCapabilities().canGetAttachments) {
        setLoad({ status: 'unsupported' });
        return;
      }
      try {
        const listed = toInvoiceMarkingAttachments(await hostAdapter.getAttachments());
        if (!active) return;
        setLoad({ status: 'ready', attachments: listed });
        setMarking(defaultInvoiceMarking(listed, 'new'));
      } catch (err) {
        if (!active) return;
        setLoad({
          status: 'error',
          message: err instanceof Error ? err.message : "Couldn't read this email's attachments.",
        });
      }
    })();
    return () => {
      active = false;
    };
  }, [hostAdapter]);

  // The firms linked to the chosen core record. A newer choice supersedes an older request.
  useEffect(() => {
    const request = ++firmRequestRef.current;
    setFirmId('');
    if (!coreRecord || !isCoreRecordType(coreRecord.entityType)) {
      setFirms({ status: 'none' });
      return;
    }
    if (!loadFirms) {
      setFirms({ status: 'unavailable' });
      return;
    }
    setFirms({ status: 'loading' });
    const target = { entityType: coreRecord.entityType, entityId: coreRecord.id };
    loadFirms(target).then(
      list => {
        if (request !== firmRequestRef.current) return;
        setFirms({ status: 'ready', firms: list });
        // One firm linked to the record: it is the only possible answer.
        if (list.length === 1) setFirmId(list[0]!.firmId);
      },
      (err: unknown) => {
        if (request !== firmRequestRef.current) return;
        setFirms({
          status: 'error',
          message: err instanceof Error ? err.message : "Couldn't load the firms for this record.",
        });
      }
    );
  }, [coreRecord, loadFirms, firmsReload]);

  // A Retry resends the LAST submitted request. Once the user changes anything the request was built from, that
  // request is no longer what is on screen: drop the retryable outcome and the stored attempt, so the next Submit is
  // a new attempt (new action id) with the values shown.
  useEffect(() => {
    lastAttemptRef.current = null;
    setOutcome(prev => (prev?.kind === 'retryable' ? null : prev));
    setRetryableInARow(0);
  }, [coreRecord, invoice, mode, correctionChosen, firmId, firmInvoiceNumber, poNumber, marking]);

  const searchCoreRecords = useCallback(
    (query: string, type: EntityType) =>
      searchOfficeEntities(query, type, { apiBaseUrl, getAccessToken, access: 'file' }),
    [apiBaseUrl, getAccessToken]
  );
  const searchInvoices = useCallback(
    (query: string, type: EntityType) => searchOfficeEntities(query, type, { apiBaseUrl, getAccessToken }),
    [apiBaseUrl, getAccessToken]
  );

  const listedAttachments = load.status === 'ready' ? load.attachments : NO_ATTACHMENTS;
  // The size budget follows the LIVE marking: a file the budget held back becomes markable when others are set to
  // Don't save, and a marked file a mark change leaves past the budget is named (never silently unmarked) and blocks Submit.
  const { attachments, overBudget } = useMemo(
    () => applyMarkingBudget(listedAttachments, marking),
    [listedAttachments, marking]
  );
  const markingMode = mode === 'existing' ? 'correction' : 'new';
  // Always with `attachments`, so a non-PDF or disabled Invoice and a disabled attachment marked to be saved are refused.
  const markingCheck = validateInvoiceMarking(marking, markingMode, attachments);
  const markingProblem = markingCheck.ok ? null : describeMarkingProblem(markingCheck.reason, attachments);
  const normalizedNumber = normalizeFirmInvoiceNumber(firmInvoiceNumber);
  const numberTooLong = firmInvoiceNumber.trim().length > FIRM_INVOICE_NUMBER_MAX;
  const poTooLong = poNumber.trim().length > PO_NUMBER_MAX;
  const succeeded = outcome?.kind === 'success';
  const locked = submitting || succeeded;
  // Running, or showing the result of a submission the server accepted (it carries the tracking number).
  const busy = submitting || outcome?.kind === 'success' || outcome?.kind === 'ended';
  useEffect(() => {
    onBusyChangeRef.current?.(busy);
  }, [busy]);
  useEffect(
    () => () => {
      onBusyChangeRef.current?.(false);
    },
    []
  );

  // The submission form applies to a new invoice, or to a correction once corrections are available.
  const formActive = mode === 'new' || (correctionAvailable && correctionChosen && invoice !== null);

  const missing = useMemo(() => {
    const list: string[] = [];
    if (!coreRecord) list.push('the matter, project or work assignment');
    if (!firmId) list.push('the firm');
    if (!normalizedNumber) list.push("the firm's invoice number");
    return list;
  }, [coreRecord, firmId, normalizedNumber]);

  const canSubmit =
    onSubmit !== undefined &&
    formActive &&
    load.status === 'ready' &&
    coreRecord !== null &&
    isCoreRecordType(coreRecord.entityType) &&
    firmId.length > 0 &&
    normalizedNumber !== null &&
    !poTooLong &&
    markingCheck.ok &&
    overBudget.length === 0 &&
    !locked;

  const runAttempt = async (input: InvoiceSubmissionInput, actionId: string): Promise<void> => {
    if (!onSubmit) return;
    lastAttemptRef.current = { input, actionId };
    const controller = new AbortController();
    abortRef.current = controller;
    setSubmitting(true);
    setProcessing(null);
    setOutcome(null);
    let result: InvoiceSubmissionOutcome;
    try {
      result = await onSubmit(input, {
        actionId,
        signal: controller.signal,
        // Accepted (202): the final answer comes from following its status — show that it is being processed.
        onAccepted: ({ trackingNumber }) => {
          setProcessing({ ...(trackingNumber ? { trackingNumber } : {}) });
          announce('Invoice sent. Spaarke is processing it.', 'polite');
        },
      });
    } catch (err) {
      // An unexpected throw says nothing about whether a retry helps; offer one (the same action).
      result = { kind: 'retryable', message: err instanceof Error ? err.message : "Couldn't submit the invoice." };
    }
    // The view went away while this ran: there is nothing left to update.
    if (controller.signal.aborted) return;
    setSubmitting(false);
    setProcessing(null);
    setOutcome(result);
    setRetryableInARow(prev => (result.kind === 'retryable' ? prev + 1 : 0));
    announce(
      result.kind === 'success'
        ? 'Invoice submitted.'
        : result.kind === 'ended'
          ? `${ENDED_TITLES[result.status]}. ${result.message}`
          : result.message,
      result.kind === 'success' ? 'polite' : 'assertive'
    );
  };

  const handleSubmit = (): void => {
    if (!canSubmit || !coreRecord || !isCoreRecordType(coreRecord.entityType)) return;
    const input = buildInvoiceSubmissionInput({
      target: { entityType: coreRecord.entityType, entityId: coreRecord.id },
      attachments,
      marking,
      firmId,
      firmInvoiceNumber,
      poNumber,
      mode: mode === 'existing' ? 'Correction' : 'New',
      invoiceId: invoice?.id ?? null,
    });
    void runAttempt(input, newActionId());
  };

  const handleRetry = (): void => {
    const last = lastAttemptRef.current;
    if (last) void runAttempt(last.input, last.actionId);
  };

  // "Done" on a finished submission clears the result, which ends `busy` (the tabs come back).
  // - After `ended` (Rejected / Failed) the form is left as it was, ready to edit and submit again.
  // - After `success` the form starts over for the next submission; the open email stays (its attachments are re-marked).
  const handleDone = (): void => {
    if (outcome?.kind === 'success') {
      lastAttemptRef.current = null;
      setMode('new');
      setCorrectionChosen(false);
      setCoreRecord(null);
      setInvoice(null);
      setFirmId('');
      setFirmInvoiceNumber('');
      setPoNumber('');
      setRetryableInARow(0);
      if (load.status === 'ready') setMarking(defaultInvoiceMarking(load.attachments, 'new'));
      setFormKey(k => k + 1);
    }
    setOutcome(null);
    announce('Ready for another invoice.');
  };

  const doneAction = (
    <MessageBarActions>
      <Button appearance="outline" size="small" onClick={handleDone}>
        Done
      </Button>
    </MessageBarActions>
  );

  const renderOutcome = () => {
    if (processing) {
      return (
        <MessageBar intent="info" layout="multiline" role="status">
          <MessageBarBody>
            <MessageBarTitle>Processing the invoice</MessageBarTitle>
            {processing.trackingNumber ? `Tracking number ${processing.trackingNumber}. ` : ''}
            Spaarke is checking it. This can take a minute.
          </MessageBarBody>
        </MessageBar>
      );
    }
    if (!outcome) return null;
    switch (outcome.kind) {
      case 'success':
        return (
          <MessageBar intent="success" layout="multiline" role="status">
            <MessageBarBody>
              <MessageBarTitle>Invoice submitted</MessageBarTitle>
              {outcome.trackingNumber ? `Tracking number ${outcome.trackingNumber}. ` : ''}
              {outcome.message ?? ''}
            </MessageBarBody>
            {doneAction}
          </MessageBar>
        );
      case 'refused':
        return (
          <MessageBar intent="error" layout="multiline" role="alert">
            <MessageBarBody>
              <MessageBarTitle>The invoice was not submitted</MessageBarTitle>
              {outcome.message}
            </MessageBarBody>
          </MessageBar>
        );
      case 'retryable':
        return (
          <MessageBar intent="warning" layout="multiline" role="alert">
            <MessageBarBody>
              <MessageBarTitle>The invoice was not submitted</MessageBarTitle>
              {outcome.message}
              {retryableInARow >= RETRIES_BEFORE_HINT ? ` ${INVOICE_UPLOAD_HINT}` : ''}
            </MessageBarBody>
            <MessageBarActions>
              <Button appearance="outline" size="small" onClick={handleRetry} disabled={submitting}>
                Retry
              </Button>
            </MessageBarActions>
          </MessageBar>
        );
      case 'permanent':
        return (
          <MessageBar intent="error" layout="multiline" role="alert">
            <MessageBarBody>
              <MessageBarTitle>{"This invoice can't be submitted from Outlook"}</MessageBarTitle>
              {outcome.message} {INVOICE_UPLOAD_HINT}
            </MessageBarBody>
          </MessageBar>
        );
      case 'ended':
        // The server accepted it (there is a tracking number) and it then ended: not "not submitted".
        return (
          <MessageBar intent="error" layout="multiline" role="alert">
            <MessageBarBody>
              <MessageBarTitle>{ENDED_TITLES[outcome.status]}</MessageBarTitle>
              {outcome.trackingNumber ? `Tracking number ${outcome.trackingNumber}. ` : ''}
              {outcome.message}
            </MessageBarBody>
            {doneAction}
          </MessageBar>
        );
    }
  };

  /** Attachments the capture left out of the saved email, with why — the Save view's own wording (SaveView). */
  const renderSkipped = () => {
    const skipped = outcome?.skipped ?? [];
    if (processing || skipped.length === 0) return null;
    return (
      <MessageBar intent="warning" layout="multiline" data-testid="invoice-skipped-attachments">
        <MessageBarBody>
          <MessageBarTitle>
            {skipped.length === 1 ? 'One attachment was not saved' : `${skipped.length} attachments were not saved`}
          </MessageBarTitle>
          {skipped.map(s => (
            <div key={s.attachmentId}>{s.message}</div>
          ))}
        </MessageBarBody>
      </MessageBar>
    );
  };

  const renderFirmField = () => {
    if (firms.status === 'none') {
      return (
        <Field label="Firm" required hint="Choose the matter, project or work assignment first.">
          <Select disabled value="">
            <option value="">Choose a firm</option>
          </Select>
        </Field>
      );
    }
    if (firms.status === 'loading') {
      return (
        <Field label="Firm" required>
          <Spinner size="tiny" label="Loading firms…" />
        </Field>
      );
    }
    if (firms.status === 'unavailable') {
      return (
        <Field label="Firm" required validationState="warning" validationMessage="Firms can't be listed here yet.">
          <Select disabled value="">
            <option value="">Choose a firm</option>
          </Select>
        </Field>
      );
    }
    if (firms.status === 'error') {
      return (
        <>
          <Field label="Firm" required validationState="error" validationMessage={firms.message}>
            <Select disabled value="">
              <option value="">Choose a firm</option>
            </Select>
          </Field>
          <div>
            <Button
              appearance="outline"
              size="small"
              aria-label="Retry loading the firms"
              onClick={() => setFirmsReload(n => n + 1)}
            >
              Retry
            </Button>
          </div>
        </>
      );
    }
    if (firms.firms.length === 0) {
      return (
        <Field label="Firm" required validationState="warning" validationMessage="No firm is linked to this record">
          <Select disabled value="">
            <option value="">Choose a firm</option>
          </Select>
        </Field>
      );
    }
    return (
      <Field label="Firm" required>
        <Select value={firmId} onChange={(_, data) => setFirmId(data.value)} disabled={locked}>
          <option value="">Choose a firm</option>
          {firms.firms.map(firm => (
            <option key={firm.firmId} value={firm.firmId}>
              {firm.name}
            </option>
          ))}
        </Select>
      </Field>
    );
  };

  const renderSubmissionForm = () => (
    <>
      <div className={styles.section}>
        <Text size={200} weight="semibold" className={styles.sectionLabel}>
          Matter, project or work assignment (required)
        </Text>
        <RelatedToPicker
          key={formKey}
          value={coreRecord}
          onChange={setCoreRecord}
          candidates={NO_CANDIDATES}
          onSearch={searchCoreRecords}
          allowedTypes={INVOICE_CORE_RECORD_TYPES}
          defaultType="Matter"
          disabled={locked}
        />
      </div>

      {renderFirmField()}

      <Field
        label="Firm invoice number"
        required
        {...(numberTooLong
          ? { validationState: 'error' as const, validationMessage: `At most ${FIRM_INVOICE_NUMBER_MAX} characters.` }
          : {})}
      >
        <Input value={firmInvoiceNumber} onChange={(_, d) => setFirmInvoiceNumber(d.value)} disabled={locked} />
      </Field>

      <Field
        label="PO number"
        hint="Optional"
        {...(poTooLong
          ? { validationState: 'error' as const, validationMessage: `At most ${PO_NUMBER_MAX} characters.` }
          : {})}
      >
        <Input value={poNumber} onChange={(_, d) => setPoNumber(d.value)} disabled={locked} />
      </Field>

      <div className={styles.section}>
        <Text size={200} weight="semibold" className={styles.sectionLabel}>
          Attachments
        </Text>
        {load.status === 'loading' && <Spinner size="tiny" label="Reading attachments…" />}
        {load.status === 'error' && (
          <MessageBar intent="error">
            <MessageBarBody>{load.message}</MessageBarBody>
          </MessageBar>
        )}
        {load.status === 'ready' && attachments.length === 0 && (
          <Text size={200} className={styles.note}>
            This email has no attachments to submit as an invoice.
          </Text>
        )}
        {load.status === 'ready' && attachments.length > 0 && (
          <>
            <InvoiceAttachmentMarking
              attachments={attachments}
              value={marking}
              onChange={setMarking}
              mode={markingMode}
              disabled={locked}
            />
            {markingProblem && (
              <Text size={200} className={styles.note} role="status">
                {markingProblem}
              </Text>
            )}
            {overBudget.map(a => (
              <Text key={a.id} size={200} className={styles.note} role="status" data-testid="invoice-over-budget">
                {describeOverBudget(a)}
              </Text>
            ))}
          </>
        )}
      </div>

      {renderOutcome()}
      {renderSkipped()}

      <div className={styles.footer}>
        {!succeeded && missing.length > 0 && (
          <Text size={200} className={styles.note}>
            Still needed: {missing.join(', ')}.
          </Text>
        )}
        <Button appearance="primary" onClick={handleSubmit} disabled={!canSubmit}>
          {processing ? 'Processing…' : submitting ? 'Submitting…' : succeeded ? 'Submitted' : 'Submit'}
        </Button>
      </div>
    </>
  );

  return (
    <div className={styles.container} role="region" aria-label="Submit invoice">
      {liveRegion}
      <div className={styles.header}>
        <ReceiptRegular aria-hidden="true" />
        <Text size={500} weight="semibold">
          Submit an invoice
        </Text>
      </div>

      {load.status === 'unsupported' ? (
        <MessageBar intent="warning" role="status">
          <MessageBarBody>Open a received email to submit an invoice from it.</MessageBarBody>
        </MessageBar>
      ) : (
        <>
          {!onSubmit && (
            <MessageBar intent="info" role="status">
              <MessageBarBody>{"Submitting invoices from Outlook isn't available yet."}</MessageBarBody>
            </MessageBar>
          )}

          <RadioGroup
            layout="horizontal"
            value={mode}
            onChange={(_, data) => setMode(data.value as InvoiceMode)}
            disabled={locked}
            aria-label="Invoice"
          >
            <Radio value="new" label="New invoice" />
            <Radio value="existing" label="Existing invoice" />
          </RadioGroup>

          {mode === 'existing' && (
            <div className={styles.section}>
              <Text size={200} weight="semibold" className={styles.sectionLabel}>
                Invoice
              </Text>
              <RelatedToPicker
                value={invoice}
                onChange={setInvoice}
                candidates={NO_CANDIDATES}
                onSearch={searchInvoices}
                allowedTypes={['Invoice']}
                defaultType="Invoice"
                disabled={locked}
              />
              {/* Both choices are shown disabled with their reason until the server can take them. A correction is
                  offered once the server accepts one (ebilling 097). Adding supporting files is ebilling 123
                  (POST /api/finance/invoices/{invoiceId}/files; contract 123-upload-contract.md, agreed) — the
                  route is not built yet, so it is not wired here. */}
              <RadioGroup
                value={correctionChosen ? 'correction' : ''}
                onChange={(_, data) => setCorrectionChosen(data.value === 'correction')}
                disabled={locked}
                aria-label="What to do with the invoice"
              >
                <Radio value="correction" label="Submit a corrected invoice" disabled={!correctionAvailable} />
                <Radio value="supporting" label="Add supporting files" disabled={!SUPPORTING_FILES_AVAILABLE} />
              </RadioGroup>
              {!correctionAvailable && (
                <Text size={200} className={styles.note}>
                  Corrections from Outlook are not available yet
                </Text>
              )}
              {!SUPPORTING_FILES_AVAILABLE && (
                <Text size={200} className={styles.note}>
                  Adding supporting files from Outlook is not available yet
                </Text>
              )}
            </div>
          )}

          {formActive && renderSubmissionForm()}
        </>
      )}
    </div>
  );
};
