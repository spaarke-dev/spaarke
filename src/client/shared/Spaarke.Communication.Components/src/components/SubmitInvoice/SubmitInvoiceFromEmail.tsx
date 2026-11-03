/**
 * SubmitInvoiceFromEmail - the ONE Submit Invoice flow of the email UI (ebilling-r1 task 118).
 *
 * Three entry points open it and nothing else: the email toolbar's "Submit Invoice" button, a flagged attachment's
 * "Submit as invoice" action (`preMarkedInvoiceId`), and the Email Review dialog's "Invoice" tab. All three hand it
 * a communication and an injected {@link EmailInvoiceSubmission}; this component never fetches, never creates a
 * `sprk_invoice`, a submission or a Stored File, and never decides authorization (model 9.1, ADR-002, FR-17).
 *
 * What the user does: marks each attachment Invoice (exactly one, a PDF) / Supporting / Don't save (the shared
 * `InvoiceAttachmentMarking`, task 118a), chooses the record and the firm, and enters the firm's invoice number
 * (required) and an optional PO number. Submit stays disabled until the marking is valid and the details are in.
 * A refusal from the server (403, 409, 413, 422, 503 ...) is shown by its error code and detail, in the same wording
 * the other Submit Invoice screens use, and nothing the user entered or marked is lost. Once the server accepts the
 * submission the shared `InvoiceSubmissionOutcome` follows it to its final answer.
 *
 * ADR-012: host-agnostic (props only). ADR-021: Fluent v9 tokens only; light and dark follow the host FluentProvider.
 * ADR-022: `React.FC` and standard hooks only (the PCF host runs React 16).
 */
import * as React from 'react';
import {
  Button,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  Select,
  Spinner,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import {
  InvoiceSubmissionOutcome,
  invoiceSubmissionErrorMessage,
  type InvoiceCoreRecordKind,
  type InvoiceCoreRecordOption,
  type InvoiceFirmOption,
  type InvoiceSubmissionAccepted,
} from '@spaarke/ui-components';
import {
  InvoiceAttachmentMarking,
  defaultInvoiceMarking,
  validateInvoiceMarking,
  type InvoiceAttachmentRole,
  type MarkableAttachment,
} from '../InvoiceAttachmentMarking';
import type { EmailInvoiceSubmission } from '../../logic/invoice';

/** The longest firm invoice number the server accepts (tracking-number column). */
export const FIRM_INVOICE_NUMBER_MAX = 89;
/** The longest PO number the server accepts. */
export const PO_NUMBER_MAX = 100;

const KIND_LABEL: Readonly<Record<InvoiceCoreRecordKind, string>> = {
  matter: 'Matters',
  project: 'Projects',
  workassignment: 'Work assignments',
};
const KIND_ORDER: readonly InvoiceCoreRecordKind[] = ['matter', 'project', 'workassignment'];

/** Why Submit is disabled while the marking is not valid (the pure rule's `reason`, in words). */
const MARKING_HINT = {
  'no-invoice': 'Mark one PDF as the Invoice.',
  'more-than-one-invoice': 'Only one attachment can be the Invoice.',
  'invoice-not-allowed': 'Only a PDF can be the Invoice.',
  'disabled-attachment-marked': "An attachment that cannot be submitted can only be marked Don't save.",
} as const;

/** The firm step's answer when the chosen record has none (the server would refuse it as `firm_invalid`). */
const NO_FIRM_TEXT =
  'No active firm is linked to this record, so an invoice cannot be submitted for it. Link the firm to the record and try again.';

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM, minWidth: 0, width: '100%' },
  center: { display: 'flex', alignItems: 'center', justifyContent: 'center', padding: tokens.spacingVerticalL },
  caption: { color: tokens.colorNeutralForeground3 },
  hint: { color: tokens.colorNeutralForeground3 },
  actions: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
});

const recordKey = (record: Pick<InvoiceCoreRecordOption, 'kind' | 'recordId'>): string =>
  `${record.kind}:${record.recordId.toLowerCase()}`;

function recordLabel(record: InvoiceCoreRecordOption): string {
  const number = record.number?.trim();
  return number && number !== record.name ? `${record.name} (${number})` : record.name;
}

/**
 * The marking the flow opens with: the shared default (one candidate is pre-selected, everything else is Supporting),
 * or, when an entry point names an attachment (`Submit as invoice`), that attachment as the Invoice. A named
 * attachment that cannot be the Invoice (not a PDF) leaves the default in place; the hint then says what is needed.
 */
export function initialMarking(
  attachments: readonly MarkableAttachment[],
  preMarkedInvoiceId?: string
): Record<string, InvoiceAttachmentRole> {
  const base = defaultInvoiceMarking(attachments, 'new');
  if (!preMarkedInvoiceId || !(preMarkedInvoiceId in base)) return base;
  const named: Record<string, InvoiceAttachmentRole> = {};
  for (const id of Object.keys(base)) named[id] = base[id] === 'invoice' ? 'supporting' : base[id];
  named[preMarkedInvoiceId] = 'invoice';
  return validateInvoiceMarking(named, 'new', attachments).ok ? named : base;
}

export interface SubmitInvoiceFromEmailProps {
  /** The `sprk_communication` whose attachments are submitted. */
  communicationId: string;
  /** The injected transport: attachments, records, firms, submit, status. */
  submission: EmailInvoiceSubmission;
  /** A `sprk_document` id to open with marked Invoice (the flagged attachment's "Submit as invoice"). */
  preMarkedInvoiceId?: string;
  /** The core record the email is already filed to, when known: it starts as the chosen record. */
  initialRecord?: InvoiceCoreRecordOption;
  /** When set, the flow offers a Done button on the outcome screen. */
  onClose?: () => void;
  /** Called with true while a submit request is in flight, false otherwise (a dialog host holds its close button). */
  onBusyChange?: (busy: boolean) => void;
  /** Milliseconds between status reads while the submission is processed. Default 2000. */
  pollIntervalMs?: number;
}

type Loaded<T> = { status: 'loading' } | { status: 'error'; message: string } | { status: 'ready'; value: T };

export const SubmitInvoiceFromEmail: React.FC<SubmitInvoiceFromEmailProps> = ({
  communicationId,
  submission,
  preMarkedInvoiceId,
  initialRecord,
  onClose,
  onBusyChange,
  pollIntervalMs,
}) => {
  const s = useStyles();

  // The latest transport, without reloading everything when the host re-renders with a new object identity.
  const submissionRef = React.useRef(submission);
  submissionRef.current = submission;

  const [attachments, setAttachments] = React.useState<Loaded<MarkableAttachment[]>>({ status: 'loading' });
  const [marking, setMarking] = React.useState<Record<string, InvoiceAttachmentRole>>({});
  const [records, setRecords] = React.useState<Loaded<{ list: InvoiceCoreRecordOption[]; note?: string }>>({
    status: 'loading',
  });
  const [chosenKey, setChosenKey] = React.useState<string>(initialRecord ? recordKey(initialRecord) : '');
  const [firms, setFirms] = React.useState<Loaded<InvoiceFirmOption[]> | null>(null);
  const [firmId, setFirmId] = React.useState('');
  const [firmInvoiceNumber, setFirmInvoiceNumber] = React.useState('');
  const [poNumber, setPoNumber] = React.useState('');
  const [submitting, setSubmitting] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const [accepted, setAccepted] = React.useState<InvoiceSubmissionAccepted | null>(null);

  React.useEffect(() => {
    onBusyChange?.(submitting);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- onBusyChange is a host callback; report on the flag only.
  }, [submitting]);

  // Attachments of this email, marked by default (or with the named attachment as the Invoice).
  React.useEffect(() => {
    let cancelled = false;
    setAttachments({ status: 'loading' });
    submissionRef.current
      .loadAttachments(communicationId)
      .then(list => {
        if (cancelled) return;
        setAttachments({ status: 'ready', value: list });
        setMarking(initialMarking(list, preMarkedInvoiceId));
      })
      .catch(() => {
        if (!cancelled)
          setAttachments({ status: 'error', message: 'The attachments of this email could not be loaded.' });
      });
    return () => {
      cancelled = true;
    };
  }, [communicationId, preMarkedInvoiceId]);

  // The records the user may submit against.
  React.useEffect(() => {
    let cancelled = false;
    setRecords({ status: 'loading' });
    submissionRef.current
      .loadRecords()
      .then(result => {
        if (cancelled) return;
        const list = [...result.records];
        // The record the email is filed to is always offered, even when it is outside the first page of its source.
        if (initialRecord && !list.some(r => recordKey(r) === recordKey(initialRecord))) list.unshift(initialRecord);
        const note =
          result.partial || result.truncated
            ? 'Only the first records of each kind are listed, so a record may be missing from the list.'
            : undefined;
        setRecords({ status: 'ready', value: { list, note } });
      })
      .catch(err => {
        if (!cancelled) setRecords({ status: 'error', message: invoiceSubmissionErrorMessage(err) });
      });
    return () => {
      cancelled = true;
    };
    // The initial record is a seed: a later change of it must not reload the list.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [communicationId]);

  const chosenRecord = React.useMemo(
    () => (records.status === 'ready' ? records.value.list.find(r => recordKey(r) === chosenKey) : undefined),
    [records, chosenKey]
  );

  // The firms linked to the chosen record (D-106). One firm is used as is; several ask the user to choose.
  React.useEffect(() => {
    if (!chosenRecord) {
      setFirms(null);
      setFirmId('');
      return;
    }
    let cancelled = false;
    setFirms({ status: 'loading' });
    setFirmId('');
    submissionRef.current
      .loadFirms(chosenRecord)
      .then(list => {
        if (cancelled) return;
        setFirms({ status: 'ready', value: list });
        if (list.length === 1) setFirmId(list[0].firmId);
      })
      .catch(err => {
        if (!cancelled) setFirms({ status: 'error', message: invoiceSubmissionErrorMessage(err) });
      });
    return () => {
      cancelled = true;
    };
  }, [chosenRecord]);

  const attachmentList = attachments.status === 'ready' ? attachments.value : [];
  const markingCheck = validateInvoiceMarking(marking, 'new', attachmentList);
  const trimmedNumber = firmInvoiceNumber.trim();
  const canSubmit =
    !submitting &&
    attachments.status === 'ready' &&
    attachmentList.length > 0 &&
    markingCheck.ok &&
    chosenRecord !== undefined &&
    firmId !== '' &&
    trimmedNumber.length > 0;

  const mounted = React.useRef(true);
  React.useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  const handleSubmit = (): void => {
    if (!canSubmit || !chosenRecord) return;
    const invoiceDocumentId = attachmentList.find(a => marking[a.id] === 'invoice')?.id;
    if (!invoiceDocumentId) return;
    const supportingDocumentIds = attachmentList
      .filter(a => marking[a.id] === 'supporting' && !a.disabledReason)
      .map(a => a.id);
    setSubmitting(true);
    setError(null);
    void submission
      .submit({
        communicationId,
        invoiceDocumentId,
        supportingDocumentIds,
        record: { kind: chosenRecord.kind, recordId: chosenRecord.recordId },
        firmId,
        firmInvoiceNumber: trimmedNumber,
        poNumber: poNumber.trim() || undefined,
      })
      .then(result => {
        if (mounted.current) setAccepted(result);
      })
      .catch(err => {
        // A refusal leaves the marks and the details where they are; the user fixes what the server named and submits again.
        if (mounted.current) setError(invoiceSubmissionErrorMessage(err));
      })
      .finally(() => {
        if (mounted.current) setSubmitting(false);
      });
  };

  if (accepted) {
    return (
      <div className={s.root} data-testid="submit-invoice-flow">
        <InvoiceSubmissionOutcome
          submissionId={accepted.submissionId}
          trackingNumber={accepted.trackingNumber}
          getStatus={id => submission.getStatus(id)}
          pollIntervalMs={pollIntervalMs}
        />
        {onClose && (
          <div className={s.actions}>
            <Button appearance="primary" onClick={onClose}>
              Done
            </Button>
          </div>
        )}
      </div>
    );
  }

  if (attachments.status === 'loading') {
    return (
      <div className={s.center} data-testid="submit-invoice-flow">
        <Spinner label="Loading attachments..." />
      </div>
    );
  }
  if (attachments.status === 'error') {
    return (
      <div className={s.root} data-testid="submit-invoice-flow">
        <MessageBar intent="error" role="alert">
          <MessageBarBody>{attachments.message}</MessageBarBody>
        </MessageBar>
      </div>
    );
  }
  if (attachmentList.length === 0) {
    return (
      <div className={s.root} data-testid="submit-invoice-flow">
        <Text className={s.caption}>This email has no attachments to submit as an invoice.</Text>
      </div>
    );
  }

  const recordList = records.status === 'ready' ? records.value.list : [];

  return (
    <div className={s.root} data-testid="submit-invoice-flow">
      {error && (
        <MessageBar intent="error" role="alert" data-testid="submit-invoice-error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      <Field label="Attachments" required>
        <InvoiceAttachmentMarking
          attachments={attachmentList}
          value={marking}
          onChange={setMarking}
          mode="new"
          disabled={submitting}
        />
      </Field>
      {!markingCheck.ok && (
        <Text size={200} className={s.hint} data-testid="submit-invoice-marking-hint">
          {MARKING_HINT[markingCheck.reason]}
        </Text>
      )}

      <Field label="Record" required hint="The matter, project or work assignment the invoice is for.">
        {records.status === 'loading' ? (
          <Spinner size="tiny" label="Loading records..." />
        ) : records.status === 'error' ? (
          <MessageBar intent="error" role="alert">
            <MessageBarBody>{records.message}</MessageBarBody>
          </MessageBar>
        ) : (
          <Select
            value={chosenKey}
            disabled={submitting}
            aria-label="Record"
            onChange={(_e, d) => setChosenKey(d.value)}
          >
            <option value="">Choose a record</option>
            {KIND_ORDER.map(kind => {
              const ofKind = recordList.filter(r => r.kind === kind);
              return ofKind.length === 0 ? null : (
                <optgroup key={kind} label={KIND_LABEL[kind]}>
                  {ofKind.map(r => (
                    <option key={recordKey(r)} value={recordKey(r)}>
                      {recordLabel(r)}
                    </option>
                  ))}
                </optgroup>
              );
            })}
          </Select>
        )}
      </Field>
      {records.status === 'ready' && records.value.note && (
        <Text size={200} className={s.hint}>
          {records.value.note}
        </Text>
      )}

      {firms?.status === 'loading' && <Spinner size="tiny" label="Loading firms..." />}
      {firms?.status === 'error' && (
        <MessageBar intent="error" role="alert">
          <MessageBarBody>{firms.message}</MessageBarBody>
        </MessageBar>
      )}
      {firms?.status === 'ready' && firms.value.length === 0 && (
        <MessageBar intent="warning" data-testid="submit-invoice-no-firm">
          <MessageBarBody>{NO_FIRM_TEXT}</MessageBarBody>
        </MessageBar>
      )}
      {firms?.status === 'ready' && firms.value.length === 1 && (
        <Text data-testid="submit-invoice-firm">Firm: {firms.value[0].name}</Text>
      )}
      {firms?.status === 'ready' && firms.value.length > 1 && (
        <Field label="Firm" required hint="More than one firm is linked to this record.">
          <Select value={firmId} disabled={submitting} aria-label="Firm" onChange={(_e, d) => setFirmId(d.value)}>
            <option value="">Choose the firm</option>
            {firms.value.map(f => (
              <option key={f.firmId} value={f.firmId}>
                {f.name}
              </option>
            ))}
          </Select>
        </Field>
      )}

      <Field label="Firm invoice number" required>
        <Input
          value={firmInvoiceNumber}
          maxLength={FIRM_INVOICE_NUMBER_MAX}
          disabled={submitting}
          onChange={(_e, d) => setFirmInvoiceNumber(d.value)}
        />
      </Field>
      <Field label="PO number">
        <Input
          value={poNumber}
          maxLength={PO_NUMBER_MAX}
          disabled={submitting}
          onChange={(_e, d) => setPoNumber(d.value)}
        />
      </Field>

      <div className={s.actions}>
        <Button
          appearance="primary"
          disabled={!canSubmit}
          icon={submitting ? <Spinner size="tiny" /> : undefined}
          onClick={handleSubmit}
          data-testid="submit-invoice-submit"
        >
          Submit Invoice
        </Button>
      </div>
    </div>
  );
};

SubmitInvoiceFromEmail.displayName = 'SubmitInvoiceFromEmail';
