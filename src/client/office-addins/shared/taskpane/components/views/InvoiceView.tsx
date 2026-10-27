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
  FIRM_INVOICE_NUMBER_MAX,
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
 * - Existing invoice: pick the invoice, then submit a corrected invoice. Corrections are off until the server
 *   supports them ({@link InvoiceViewProps.correctionAvailable}); adding supporting files has no server route and is
 *   not offered.
 *
 * Submit goes through the injected {@link InvoiceViewProps.onSubmit} seam (App wires `invoiceSubmissionService`);
 * without it the view says submitting is not available and Submit stays disabled. Once the server accepts the
 * submission the view shows it processing until the final status is known. Results are shown generically (success /
 * refused / retry / use Spaarke instead) from {@link InvoiceSubmissionOutcome}; the view never reads a reason code.
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
}

/** The hint when Outlook cannot submit this invoice (permanent failure, or a retry that keeps failing). */
export const INVOICE_UPLOAD_HINT = 'You can still submit this invoice in Spaarke by uploading it there.';

/** Retryable failures in a row before the view also suggests uploading in Spaarke. */
const RETRIES_BEFORE_HINT = 2;

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

function newActionId(): string {
  const random = globalThis.crypto?.randomUUID?.();
  return random ?? `${Date.now().toString(16)}-${Math.random().toString(16).slice(2)}`;
}

function isCoreRecordType(type: EntityType): type is InvoiceCoreRecordType {
  return (INVOICE_CORE_RECORD_TYPES as EntityType[]).includes(type);
}

const NO_CANDIDATES: never[] = [];

type MarkingProblem = 'no-invoice' | 'more-than-one-invoice' | 'invoice-not-allowed' | 'disabled-attachment-marked';

/** The marking component's validation reason, for the user. */
function describeMarkingProblem(reason: MarkingProblem, attachments: readonly InvoiceListedAttachment[]): string {
  switch (reason) {
    case 'no-invoice':
      // The marking component's PDF rule (content type says PDF, else a .pdf name).
      return attachments.some(
        a => !a.disabledReason && (/pdf/i.test(a.contentType) || /\.pdf$/i.test(a.fileName.trim()))
      )
        ? 'Mark the PDF that is the invoice.'
        : 'The invoice must be a PDF attached to this email, and this email has none that can be submitted.';
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
  const lastAttemptRef = useRef<{ input: InvoiceSubmissionInput; actionId: string } | null>(null);
  const firmRequestRef = useRef(0);

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
  }, [coreRecord, loadFirms]);

  const searchCoreRecords = useCallback(
    (query: string, type: EntityType) =>
      searchOfficeEntities(query, type, { apiBaseUrl, getAccessToken, access: 'file' }),
    [apiBaseUrl, getAccessToken]
  );
  const searchInvoices = useCallback(
    (query: string, type: EntityType) => searchOfficeEntities(query, type, { apiBaseUrl, getAccessToken }),
    [apiBaseUrl, getAccessToken]
  );

  const attachments = load.status === 'ready' ? load.attachments : [];
  const markingMode = mode === 'existing' ? 'correction' : 'new';
  // Always with `attachments`, so a non-PDF or disabled Invoice and a disabled attachment marked to be saved are refused.
  const markingCheck = validateInvoiceMarking(marking, markingMode, attachments);
  const markingProblem = markingCheck.ok ? null : describeMarkingProblem(markingCheck.reason, attachments);
  const normalizedNumber = normalizeFirmInvoiceNumber(firmInvoiceNumber);
  const numberTooLong = firmInvoiceNumber.trim().length > FIRM_INVOICE_NUMBER_MAX;
  const succeeded = outcome?.kind === 'success';
  const locked = submitting || succeeded;

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
    markingCheck.ok &&
    !locked;

  const runAttempt = async (input: InvoiceSubmissionInput, actionId: string): Promise<void> => {
    if (!onSubmit) return;
    lastAttemptRef.current = { input, actionId };
    setSubmitting(true);
    setProcessing(null);
    setOutcome(null);
    let result: InvoiceSubmissionOutcome;
    try {
      result = await onSubmit(input, {
        actionId,
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
    setSubmitting(false);
    setProcessing(null);
    setOutcome(result);
    setRetryableInARow(prev => (result.kind === 'retryable' ? prev + 1 : 0));
    announce(
      result.kind === 'success' ? 'Invoice submitted.' : result.message,
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
    }
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
        <Field label="Firm" required validationState="error" validationMessage={firms.message}>
          <Select disabled value="">
            <option value="">Choose a firm</option>
          </Select>
        </Field>
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

      <Field label="PO number" hint="Optional">
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
          </>
        )}
      </div>

      {renderOutcome()}

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
              {/* Adding supporting files is not offered: there is no server route for it (contract, "Not on this
                  route"). A correction is offered once the server accepts one (ebilling 097). */}
              <RadioGroup
                value={correctionChosen ? 'correction' : ''}
                onChange={(_, data) => setCorrectionChosen(data.value === 'correction')}
                disabled={!correctionAvailable || locked}
                aria-label="What to do with the invoice"
              >
                <Radio value="correction" label="Submit a corrected invoice" />
              </RadioGroup>
              {!correctionAvailable && (
                <Text size={200} className={styles.note}>
                  Corrections from Outlook are not available yet
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
