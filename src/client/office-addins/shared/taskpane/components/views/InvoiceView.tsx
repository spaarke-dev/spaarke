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
import { fetchRelatedCandidates, type RelatedCandidate } from '../../services/communicationSuggestionsService';
import {
  INVOICE_CORE_RECORD_TYPES,
  buildInvoiceSubmissionInput,
  normalizeFirmInvoiceNumber,
  toInvoiceMarkingAttachments,
  applyMarkingBudget,
  describeOverBudget,
  isPdfAttachment,
  filedUnderRecord,
  FIRM_INVOICE_NUMBER_MAX,
  PO_NUMBER_MAX,
  type InvoiceCoreRecordType,
  type InvoiceCorrectionPrefill,
  type InvoiceFirmOption,
  type InvoiceSubmissionInput,
  type InvoiceSubmissionOutcome,
  type InvoiceListedAttachment,
  type InvoiceMarkingValue,
  type InvoiceSubmitHandler,
  type LoadCorrectionPrefill,
  type LoadInvoiceFirms,
} from '../../services/invoiceSubmissionSeam';
import { describeCorrectionBlock } from '../../utils/invoiceSubmissionMessages';
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
 * - Existing invoice: pick the invoice, then submit a corrected invoice or add supporting files. Corrections are shown
 *   disabled with their reason until {@link InvoiceViewProps.correctionAvailable} (ebilling 097 on the server); supporting
 *   files until the upload route exists (ebilling 123; its contract is agreed, the route is not built, so the add-in
 *   does not call it yet).
 * - Correction: the chosen invoice's prefill ({@link InvoiceViewProps.loadCorrectionPrefill}, contract §10) decides.
 *   When it can be corrected, the form shows the firm, core record and firm invoice number the correction keeps (not
 *   editable: the server refuses any other) and the attachment marking; otherwise the view says why, and offers a
 *   Superseded invoice's replacement.
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
 * The core record starts as the record the email is already filed to ({@link InvoiceViewProps.filedRecord}, the Save
 * tab's "Filed to"): the server files the email under the chosen record, and refuses a record other than the one it is
 * filed under (contract §6 `document_core_record_mismatch`), so the view names the filed record when the user picks
 * another. An email that is not filed gets the Save picker's suggested-record cards (`fetchRelatedCandidates`).
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
   * The chosen invoice's correction prefill (contract §10, ebilling 097e): the firm, core record and number a correction
   * keeps, and whether it can be made. Absent → a correction can't be prepared.
   */
  loadCorrectionPrefill?: LoadCorrectionPrefill;
  /**
   * Called with true while a submission is running, or its result (one the server accepted: submitted, rejected or
   * failed, with its tracking number) is showing; false otherwise and on unmount. The pane hides its tab bar while
   * true, so the user cannot walk away from a running submission or its tracking number.
   */
  onBusyChange?: (busy: boolean) => void;
  /**
   * The Matter, Project or Work Assignment the open email is already filed to ({@link invoiceFiledRecord} of the pane's
   * email identity). Seeds the core record until the user picks one. Null or absent: not filed, not known yet, or filed
   * to another kind of record.
   */
  filedRecord?: EntitySearchResult | null;
}

function sameRecordId(a: string, b: string): boolean {
  return a.replace(/[{}]/g, '').toLowerCase() === b.replace(/[{}]/g, '').toLowerCase();
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

type PrefillLoad =
  | { status: 'none' }
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; prefill: InvoiceCorrectionPrefill };

const CORE_TYPE_LABEL: Record<InvoiceCoreRecordType, string> = {
  Matter: 'Matter',
  Project: 'Project',
  WorkAssignment: 'Work assignment',
};

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
  loadCorrectionPrefill,
  onBusyChange,
  filedRecord = null,
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
  /** Set once the user picks or clears the core record: the filed record no longer replaces their choice. */
  const userPickedRef = useRef(false);
  const [candidates, setCandidates] = useState<RelatedCandidate[]>(NO_CANDIDATES);
  const [candidatesLoading, setCandidatesLoading] = useState(false);
  const [prefill, setPrefill] = useState<PrefillLoad>({ status: 'none' });
  /** Bumped by the prefill's Retry; the prefill effect runs again. */
  const [prefillReload, setPrefillReload] = useState(0);
  const prefillRequestRef = useRef(0);

  const chooseCoreRecord = useCallback((record: EntitySearchResult | null) => {
    userPickedRef.current = true;
    setCoreRecord(record);
  }, []);

  // The email's filing can be known after the view opens (the pane's lookup runs once signed in): seed it then.
  useEffect(() => {
    if (filedRecord && !userPickedRef.current) setCoreRecord(prev => prev ?? filedRecord);
  }, [filedRecord]);

  // The Save picker's suggested records for this email, as cards (best-effort: none on any failure; the user
  // searches). Capability-gated (NFR-10), keyed by the RFC Message-ID like Save's (task 121).
  useEffect(() => {
    const capabilities = hostAdapter.getCapabilities();
    if (
      !capabilities.canSuggestRelatedRecords ||
      !capabilities.canResolveEmailIdentity ||
      !hostAdapter.getEmailIdentityKeys
    ) {
      return;
    }
    let active = true;
    setCandidatesLoading(true);
    void (async () => {
      try {
        const keys = await hostAdapter.getEmailIdentityKeys?.();
        const found = keys?.internetMessageId ? await fetchRelatedCandidates(keys.internetMessageId) : [];
        if (active) setCandidates(found.filter(c => isCoreRecordType(c.entityType)));
      } catch (err) {
        console.warn('[InvoiceView] No suggested records; the user searches.', err);
      } finally {
        if (active) setCandidatesLoading(false);
      }
    })();
    return () => {
      active = false;
    };
  }, [hostAdapter]);

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

  // A correction of the chosen invoice: read what it must keep and whether it can be made (§10). A newer choice
  // supersedes an older request.
  const correcting = correctionAvailable && mode === 'existing' && correctionChosen && invoice !== null;
  useEffect(() => {
    const request = ++prefillRequestRef.current;
    if (!correcting || !invoice) {
      setPrefill({ status: 'none' });
      return;
    }
    if (!loadCorrectionPrefill) {
      setPrefill({ status: 'error', message: "A correction can't be prepared here yet." });
      return;
    }
    setPrefill({ status: 'loading' });
    loadCorrectionPrefill(invoice.id).then(
      result => {
        if (request === prefillRequestRef.current) setPrefill({ status: 'ready', prefill: result });
      },
      (err: unknown) => {
        if (request !== prefillRequestRef.current) return;
        setPrefill({
          status: 'error',
          message: err instanceof Error ? err.message : "Couldn't read this invoice. Try again.",
        });
      }
    );
  }, [correcting, invoice, loadCorrectionPrefill, prefillReload]);

  // A Retry resends the LAST submitted request. Once the user changes anything the request was built from, that
  // request is no longer what is on screen: drop the retryable outcome and the stored attempt, so the next Submit is
  // a new attempt (new action id) with the values shown.
  useEffect(() => {
    lastAttemptRef.current = null;
    setOutcome(prev => (prev?.kind === 'retryable' ? null : prev));
    setRetryableInARow(0);
  }, [coreRecord, invoice, mode, correctionChosen, firmId, firmInvoiceNumber, poNumber, marking]);

  // A refusal describes the invoice it was for: choosing another invoice or another kind of submission drops it, so it
  // never shows under the new choice. (A result the server accepted locks these choices until Done.)
  useEffect(() => {
    setOutcome(prev => (prev?.kind === 'refused' || prev?.kind === 'permanent' ? null : prev));
  }, [invoice, mode, correctionChosen]);

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
  // While busy, the choices that decide which form shows (new or existing, the invoice, correction) are locked, so the
  // result and its Done stay on screen with the form they belong to. An ended one can still be edited and sent again.
  const choicesLocked = busy;
  useEffect(() => {
    onBusyChangeRef.current?.(busy);
  }, [busy]);
  useEffect(
    () => () => {
      onBusyChangeRef.current?.(false);
    },
    []
  );

  // A correction is sent with the invoice's own firm, core record and number (§10), never with values typed here.
  // Only the chosen invoice's own prefill: on the render where the choice changes, the previous one is not used.
  const correction =
    correcting &&
    invoice !== null &&
    prefill.status === 'ready' &&
    prefill.prefill.canCorrect &&
    sameRecordId(prefill.prefill.invoiceId, invoice.id)
      ? prefill.prefill
      : null;

  // The submission form applies to a new invoice, or to a correction the server says can be made.
  const formActive = mode === 'new' || correction !== null;

  const missing = useMemo(() => {
    const list: string[] = [];
    if (mode !== 'new') return list;
    if (!coreRecord) list.push('the matter, project or work assignment');
    if (!firmId) list.push('the firm');
    if (!normalizedNumber) list.push("the firm's invoice number");
    return list;
  }, [mode, coreRecord, firmId, normalizedNumber]);

  const detailsReady =
    mode === 'new'
      ? coreRecord !== null && isCoreRecordType(coreRecord.entityType) && firmId.length > 0 && normalizedNumber !== null
      : correction !== null && normalizeFirmInvoiceNumber(correction.firmInvoiceNumber) !== null;

  const canSubmit =
    onSubmit !== undefined &&
    formActive &&
    load.status === 'ready' &&
    detailsReady &&
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
    if (!canSubmit) return;
    let input: InvoiceSubmissionInput;
    if (correction) {
      input = buildInvoiceSubmissionInput({
        target: { entityType: correction.coreRecord.entityType, entityId: correction.coreRecord.entityId },
        attachments,
        marking,
        firmId: correction.firm.id,
        firmInvoiceNumber: correction.firmInvoiceNumber,
        poNumber,
        mode: 'Correction',
        invoiceId: correction.invoiceId,
      });
    } else {
      if (!coreRecord || !isCoreRecordType(coreRecord.entityType)) return;
      input = buildInvoiceSubmissionInput({
        target: { entityType: coreRecord.entityType, entityId: coreRecord.id },
        attachments,
        marking,
        firmId,
        firmInvoiceNumber,
        poNumber,
        mode: 'New',
        invoiceId: null,
      });
    }
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
      userPickedRef.current = false;
      setCoreRecord(filedRecord);
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

  // A 409 that names the record the email is filed under (ebilling task 192): switch the form to it. The refusal is
  // cleared because it described the record the user had picked; nothing is sent until they submit again.
  const handleUseFiledUnder = (record: EntitySearchResult): void => {
    chooseCoreRecord(record);
    setOutcome(null);
    announce(`${record.name} chosen. Submit to send the invoice to it.`);
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
      case 'refused': {
        const filedUnder = outcome.filedUnder ? filedUnderRecord(outcome.filedUnder) : null;
        // A correction keeps the invoice's own record, so only a new invoice can switch to the filed-under record.
        const offerFiledUnder =
          mode === 'new' && filedUnder !== null && !(coreRecord && sameRecordId(coreRecord.id, filedUnder.id));
        return (
          <MessageBar intent="error" layout="multiline" role="alert">
            <MessageBarBody>
              <MessageBarTitle>The invoice was not submitted</MessageBarTitle>
              {outcome.message}
            </MessageBarBody>
            {offerFiledUnder && (
              <MessageBarActions>
                <Button
                  appearance="outline"
                  size="small"
                  onClick={() => handleUseFiledUnder(filedUnder)}
                  disabled={locked}
                  data-testid="invoice-use-filed-under"
                >
                  {`Use ${filedUnder.name}`}
                </Button>
              </MessageBarActions>
            )}
          </MessageBar>
        );
      }
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

  // A correction keeps the original invoice's firm, record and number: shown, not editable (§10).
  const renderCorrectionDetails = (c: InvoiceCorrectionPrefill) => (
    <div className={styles.section} data-testid="invoice-correction-details">
      <Text size={200} weight="semibold" className={styles.sectionLabel}>
        Correcting{c.trackingNumber ? ` (tracking ${c.trackingNumber})` : ''}
      </Text>
      <Text size={200}>{`${CORE_TYPE_LABEL[c.coreRecord.entityType]}: ${c.coreRecord.name}`}</Text>
      <Text size={200}>{`Firm: ${c.firm.name}`}</Text>
      <Text size={200}>{`Firm invoice number: ${c.firmInvoiceNumber}`}</Text>
      <Text size={200} className={styles.note}>
        {"A correction keeps the original invoice's firm, record and firm invoice number."}
      </Text>
      {filedRecord && !sameRecordId(filedRecord.id, c.coreRecord.entityId) && (
        <Text size={200} className={styles.note} data-testid="invoice-filed-record-note">
          {`This email is filed to ${filedRecord.displayInfo ? `${filedRecord.displayInfo} ` : ''}${filedRecord.name} in Spaarke, but this invoice is for ${c.coreRecord.name}. Spaarke accepts a correction only from an email filed to the invoice's record.`}
        </Text>
      )}
      {normalizeFirmInvoiceNumber(c.firmInvoiceNumber) === null && (
        <Text size={200} className={styles.note} role="status" data-testid="invoice-correction-no-number">
          {"This invoice has no firm invoice number in Spaarke, so it can't be corrected from Outlook."}
        </Text>
      )}
    </div>
  );

  // The chosen invoice's correction prefill while it loads, fails, or says the invoice can't be corrected.
  const renderPrefillState = () => {
    if (prefill.status === 'loading') return <Spinner size="tiny" label="Checking this invoice…" />;
    if (prefill.status === 'error') {
      return (
        <MessageBar intent="error" layout="multiline" role="alert">
          <MessageBarBody>{prefill.message}</MessageBarBody>
          <MessageBarActions>
            <Button
              appearance="outline"
              size="small"
              aria-label="Retry reading the invoice"
              onClick={() => setPrefillReload(n => n + 1)}
            >
              Retry
            </Button>
          </MessageBarActions>
        </MessageBar>
      );
    }
    if (prefill.status !== 'ready' || prefill.prefill.canCorrect) return null;
    const replacedBy = prefill.prefill.replacedBy;
    return (
      <MessageBar intent="warning" layout="multiline" role="status" data-testid="invoice-correction-blocked">
        <MessageBarBody>{describeCorrectionBlock(prefill.prefill)}</MessageBarBody>
        {replacedBy && (
          <MessageBarActions>
            <Button
              appearance="outline"
              size="small"
              disabled={choicesLocked}
              onClick={() =>
                setInvoice({
                  id: replacedBy.invoiceId,
                  entityType: 'Invoice',
                  logicalName: 'sprk_invoice',
                  name: replacedBy.name,
                })
              }
            >
              {`Use ${replacedBy.name}`}
            </Button>
          </MessageBarActions>
        )}
      </MessageBar>
    );
  };

  const renderSubmissionForm = () => (
    <>
      {correction ? renderCorrectionDetails(correction) : renderNewInvoiceDetails()}

      <Field
        label="PO number"
        hint="Optional"
        {...(poTooLong
          ? { validationState: 'error' as const, validationMessage: `At most ${PO_NUMBER_MAX} characters.` }
          : {})}
      >
        <Input value={poNumber} onChange={(_, d) => setPoNumber(d.value)} disabled={locked} />
      </Field>

      {renderAttachmentsAndSubmit()}
    </>
  );

  const renderNewInvoiceDetails = () => (
    <>
      <div className={styles.section}>
        <Text size={200} weight="semibold" className={styles.sectionLabel}>
          Matter, project or work assignment (required)
        </Text>
        <RelatedToPicker
          key={formKey}
          value={coreRecord}
          onChange={chooseCoreRecord}
          candidates={candidates}
          candidatesLoading={candidatesLoading}
          onSearch={searchCoreRecords}
          allowedTypes={INVOICE_CORE_RECORD_TYPES}
          defaultType={filedRecord && isCoreRecordType(filedRecord.entityType) ? filedRecord.entityType : 'Matter'}
          disabled={locked}
        />
        {filedRecord && coreRecord && (
          <Text size={200} className={styles.note} data-testid="invoice-filed-record-note">
            {sameRecordId(coreRecord.id, filedRecord.id)
              ? 'This email is filed to this record in Spaarke.'
              : `This email is filed to ${filedRecord.displayInfo ? `${filedRecord.displayInfo} ` : ''}${filedRecord.name} in Spaarke, so its invoice can only be submitted to that record.`}
          </Text>
        )}
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
    </>
  );

  const renderAttachmentsAndSubmit = () => (
    <>
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
            disabled={choicesLocked}
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
                disabled={choicesLocked}
              />
              {/* Both choices are shown disabled with their reason until the server can take them. A correction is
                  offered once the server accepts one (ebilling 097). Adding supporting files is ebilling 123
                  (POST /api/finance/invoices/{invoiceId}/files; contract 123-upload-contract.md, agreed) — the
                  route is not built yet, so it is not wired here. */}
              <RadioGroup
                value={correctionChosen ? 'correction' : ''}
                onChange={(_, data) => setCorrectionChosen(data.value === 'correction')}
                disabled={choicesLocked}
                aria-label="What to do with the invoice"
              >
                <Radio
                  value="correction"
                  label="Submit a corrected invoice"
                  disabled={!correctionAvailable || choicesLocked}
                />
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
              {correcting && renderPrefillState()}
            </div>
          )}

          {formActive && renderSubmissionForm()}
        </>
      )}
    </div>
  );
};
