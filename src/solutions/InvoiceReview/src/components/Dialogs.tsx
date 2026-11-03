/**
 * The page's two forms (ebilling-r1 task 110, prototype Dialogs CorrectValueModal / BulkModal), both on the shared SprkModal FormModal
 * preset (ADR-050, MODAL-DECISION-CRITERIA: a light-edit form, explicit dismiss).
 *
 *   - CorrectValueModal: "the reading is wrong" (task 101). One field, the value as read, the corrected value. A header correction offers
 *     the header fields 101 accepts. Never a decision.
 *   - BulkDecisionModal: one action and ONE reason for several lines (ticked rows, or a rule's suggestion), sent as one request.
 *
 * Neither validates what the server owns: a missing reason or an out-of-range value goes to the server, which refuses it with a stable
 * code, and the refusal is shown here, in the form, so nothing is lost.
 */

import * as React from 'react';
import { Caption1, Dropdown, Field, Input, MessageBar, MessageBarBody, Option, Text, Textarea, makeStyles, tokens } from '@fluentui/react-components';
import { FormModal } from '@spaarke/ui-components';
import { money } from '../services/reviewModel';

const useStyles = makeStyles({
  muted: { color: tokens.colorNeutralForeground3 },
  list: { margin: 0, paddingLeft: tokens.spacingHorizontalL, maxHeight: '160px', overflowY: 'auto' },
});

// ─────────────────────────────────────────────────────────────────────────────
// Correct value
// ─────────────────────────────────────────────────────────────────────────────

/** The header fields task 101 corrects (InvoiceCorrectionEndpoints CorrectHeader). */
export const HEADER_FIELDS: ReadonlyArray<{ field: string; label: string; kind: 'text' | 'date' | 'number' }> = [
  { field: 'FirmInvoiceNumber', label: 'Firm invoice number', kind: 'text' },
  { field: 'InvoiceDate', label: 'Invoice date', kind: 'date' },
  { field: 'BillingPeriodStart', label: 'Period start', kind: 'date' },
  { field: 'BillingPeriodEnd', label: 'Period end', kind: 'date' },
  { field: 'Currency', label: 'Currency', kind: 'text' },
  { field: 'TotalDue', label: 'Total due', kind: 'number' },
];

const LINE_KIND: Readonly<Record<string, 'text' | 'date' | 'number' | 'multiline'>> = {
  Date: 'date',
  Hours: 'number',
  Rate: 'number',
  Amount: 'number',
  Narrative: 'multiline',
  TaskCode: 'text',
  ActivityCode: 'text',
  ExpenseCode: 'text',
};

export interface CorrectTarget {
  /** null = a header correction. */
  lineId: string | null;
  lineSequence: number | null;
  /** The field to correct; for a header correction, the initial choice. */
  field: string;
  label: string;
  /** The value as read, shown as text (null = none). */
  asRead: string | null;
  /** The editor's starting value, in the wire format (invariant number, yyyy-mm-dd). */
  initial: string;
}

export interface CorrectValueModalProps {
  target: CorrectTarget;
  busy: boolean;
  /** The server's refusal of the last submit, shown in the form. */
  error: string | null;
  onClose: () => void;
  onSubmit: (field: string, value: string) => void;
}

export function CorrectValueModal({ target, busy, error, onClose, onSubmit }: CorrectValueModalProps) {
  const s = useStyles();
  const header = target.lineId === null;
  const [field, setField] = React.useState(target.field);
  const [value, setValue] = React.useState(target.initial);
  const headerField = HEADER_FIELDS.find(h => h.field === field);
  const kind = header ? (headerField?.kind ?? 'text') : (LINE_KIND[field] ?? 'text');
  const title = header ? 'Correct an invoice value' : `Correct ${target.label.toLowerCase()} · line ${target.lineSequence}`;

  return (
    <FormModal
      open
      title={title}
      size="sm"
      submitLabel="Save correction"
      busy={busy}
      submitDisabled={value.trim().length === 0}
      onClose={() => {
        if (!busy) onClose();
      }}
      onSubmit={() => onSubmit(field, value.trim())}
    >
      <Text>Use this when the system read the value wrong. It is recorded as a correction and the rules run again. To pay less than billed, use Reduce instead.</Text>
      {header && (
        <Field label="Field">
          <Dropdown
            value={headerField?.label ?? field}
            selectedOptions={[field]}
            onOptionSelect={(_, d) => {
              if (d.optionValue) setField(d.optionValue);
            }}
          >
            {HEADER_FIELDS.map(h => (
              <Option key={h.field} value={h.field}>
                {h.label}
              </Option>
            ))}
          </Dropdown>
        </Field>
      )}
      {!header && (
        <Caption1 className={s.muted}>
          As read: {target.asRead ?? '—'}
        </Caption1>
      )}
      <Field label="Correct value" hint={kind === 'date' ? 'yyyy-mm-dd' : kind === 'number' ? 'A number, for example 2.5' : undefined}>
        {kind === 'multiline' ? (
          <Textarea value={value} onChange={(_, d) => setValue(d.value)} resize="vertical" aria-label="Correct value" />
        ) : (
          <Input
            value={value}
            type={kind === 'date' ? 'date' : 'text'}
            inputMode={kind === 'number' ? 'decimal' : undefined}
            onChange={(_, d) => setValue(d.value)}
            aria-label="Correct value"
          />
        )}
      </Field>
      {error && (
        <MessageBar intent="error" data-testid="correct-error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}
    </FormModal>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Bulk decision
// ─────────────────────────────────────────────────────────────────────────────

export interface BulkRequest {
  action: 'accept' | 'reduce' | 'disallow';
  /** What the dialog says it will do. */
  title: string;
  lines: Array<{ lineId: string; sequence: number; billed: number; approvedAmount: number | null }>;
  /** Lines of the request's source the action leaves out (shown as a note). */
  skipped?: number;
}

export interface BulkDecisionModalProps {
  request: BulkRequest;
  busy: boolean;
  error: string | null;
  onClose: () => void;
  onSubmit: (reason: string | null) => void;
}

export function BulkDecisionModal({ request, busy, error, onClose, onSubmit }: BulkDecisionModalProps) {
  const s = useStyles();
  const [reason, setReason] = React.useState('');
  const n = request.lines.length;
  return (
    <FormModal
      open
      title={request.title}
      size="md"
      submitLabel={`${request.action === 'accept' ? 'Accept' : request.action === 'disallow' ? 'Disallow' : 'Reduce'} ${n} ${n === 1 ? 'line' : 'lines'}`}
      busy={busy}
      onClose={() => {
        if (!busy) onClose();
      }}
      onSubmit={() => onSubmit(reason.trim() || null)}
    >
      <Text>
        {n} {n === 1 ? 'line gets' : 'lines get'} the same decision and reason
        {request.action === 'reduce' ? '; each line is reduced by its own suggested amount' : ''}.
      </Text>
      <ul className={s.list} aria-label="Lines in this decision">
        {request.lines.map(l => (
          <li key={l.lineId}>
            Line {l.sequence} · {money(l.billed)}
            {request.action === 'reduce' && l.approvedAmount !== null ? ` → ${money(l.approvedAmount)}` : ''}
            {request.action === 'disallow' ? ' → 0.00' : ''}
          </li>
        ))}
      </ul>
      {request.skipped ? (
        <Caption1 className={s.muted}>
          {request.skipped} {request.skipped === 1 ? 'line is' : 'lines are'} left out (already decided, still processing, or with no suggestion).
        </Caption1>
      ) : null}
      <Field label={request.action === 'accept' ? 'Reason (optional)' : 'Reason'}>
        <Textarea value={reason} onChange={(_, d) => setReason(d.value)} resize="vertical" aria-label="Reason" />
      </Field>
      {error && (
        <MessageBar intent="error" data-testid="bulk-error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}
    </FormModal>
  );
}
