import React from 'react';
import { Radio, RadioGroup, Text, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { InfoRegular } from '@fluentui/react-icons';
import { formatSize } from '../../services/emailContentCapture';

/**
 * InterimInvoiceAttachmentMarking — INTERIM, local stand-in for ebilling-r1 task 118's shared
 * `InvoiceAttachmentMarking` (`@spaarke/communication-components/invoice-attachment-marking`), task 133 slice A.
 *
 * Slice B replaces this file with the shared component (webpack + jest alias and a type shim, never the barrel —
 * task 100 rule). So the props and the two pure functions below are EXACTLY the contract agreed with ebilling-r1
 * (notes/ebilling-r1-review-2026-10-21.md, "Shared attachment-marking component"): controlled and free of I/O,
 * `attachments` / `value` / `onChange` / `mode` / `disabled`, no submit callback (each host owns its route).
 * Do not grow this file's API: anything added here is something slice B would lose.
 *
 * Rules (owner decisions 2026-10-21):
 * - exactly one attachment is the Invoice (except when only adding supporting files); it is pre-selected when there
 *   is exactly one PDF the add-in can send;
 * - every other attachment defaults to Supporting; the user can choose Don't save;
 * - an attachment the add-in cannot send (it carries a `disabledReason`) is Don't save and cannot be changed.
 */

/** One attachment as the marking list shows it. `id` is opaque to the component. */
export interface InvoiceMarkingAttachment {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  /** The host's hint that this is probably the invoice (shown as a hint only; the default rule is the PDF rule). */
  suggestedInvoice?: boolean;
  /** Why this attachment cannot be submitted. Present → the row is disabled and its mark is always 'skip'. */
  disabledReason?: string;
}

/** What happens to one attachment. 'skip' is shown as "Don't save". */
export type InvoiceAttachmentMark = 'invoice' | 'supporting' | 'skip';

/** 'new' and 'correction' need exactly one Invoice; 'supporting-only' offers no Invoice choice at all. */
export type InvoiceMarkingMode = 'new' | 'correction' | 'supporting-only';

/** The mark per attachment id. An id with no entry counts as 'skip'. */
export type InvoiceMarking = Readonly<Record<string, InvoiceAttachmentMark>>;

export interface InterimInvoiceAttachmentMarkingProps {
  attachments: readonly InvoiceMarkingAttachment[];
  value: InvoiceMarking;
  onChange: (next: InvoiceMarking) => void;
  mode: InvoiceMarkingMode;
  disabled?: boolean;
}

/** The outcome of {@link validateInvoiceMarking}: valid, or the first rule broken, written for the user. */
export type InvoiceMarkingValidation = { valid: true } | { valid: false; reason: string };

function isPdf(attachment: InvoiceMarkingAttachment): boolean {
  return (
    attachment.contentType.toLowerCase() === 'application/pdf' || attachment.fileName.toLowerCase().endsWith('.pdf')
  );
}

function markOf(marking: InvoiceMarking, id: string): InvoiceAttachmentMark {
  return marking[id] ?? 'skip';
}

/**
 * The marking a list starts with. An attachment with a `disabledReason` is 'skip'. Of the rest: when exactly one is a
 * PDF (and the mode needs an Invoice) it is 'invoice'; every other one is 'supporting'.
 */
export function defaultInvoiceMarking(
  attachments: readonly InvoiceMarkingAttachment[],
  mode: InvoiceMarkingMode = 'new'
): InvoiceMarking {
  const sendable = attachments.filter(a => !a.disabledReason);
  const pdfs = sendable.filter(isPdf);
  const invoiceId = mode !== 'supporting-only' && pdfs.length === 1 ? pdfs[0]!.id : undefined;
  const marking: Record<string, InvoiceAttachmentMark> = {};
  for (const attachment of attachments) {
    marking[attachment.id] = attachment.disabledReason
      ? 'skip'
      : attachment.id === invoiceId
        ? 'invoice'
        : 'supporting';
  }
  return marking;
}

/**
 * Whether a marking may be submitted:
 * - an attachment with a `disabledReason` cannot be Invoice or Supporting;
 * - 'new' / 'correction': exactly one Invoice;
 * - 'supporting-only': no Invoice, and at least one Supporting (otherwise there is nothing to add).
 */
export function validateInvoiceMarking(
  attachments: readonly InvoiceMarkingAttachment[],
  marking: InvoiceMarking,
  mode: InvoiceMarkingMode
): InvoiceMarkingValidation {
  for (const attachment of attachments) {
    if (attachment.disabledReason && markOf(marking, attachment.id) !== 'skip') {
      return { valid: false, reason: `"${attachment.fileName}" can't be submitted: ${attachment.disabledReason}` };
    }
  }
  const invoices = attachments.filter(a => markOf(marking, a.id) === 'invoice').length;
  if (mode === 'supporting-only') {
    if (invoices > 0) {
      return { valid: false, reason: 'Adding supporting files: no file can be marked as the invoice.' };
    }
    if (!attachments.some(a => markOf(marking, a.id) === 'supporting')) {
      return { valid: false, reason: 'Mark at least one file as supporting.' };
    }
    return { valid: true };
  }
  if (invoices === 0) {
    return { valid: false, reason: 'Mark one file as the invoice.' };
  }
  if (invoices > 1) {
    return { valid: false, reason: 'Only one file can be the invoice.' };
  }
  return { valid: true };
}

const useStyles = makeStyles({
  list: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS },
  row: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    padding: tokens.spacingVerticalS,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  rowDisabled: { backgroundColor: tokens.colorNeutralBackground3 },
  name: { overflow: 'hidden', textOverflow: 'ellipsis', wordBreak: 'break-word' },
  meta: { color: tokens.colorNeutralForeground3 },
  reason: {
    display: 'flex',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorNeutralForeground2,
  },
  reasonIcon: { flexShrink: 0, marginTop: tokens.spacingVerticalXXS },
  radios: { flexWrap: 'wrap' },
});

export const InterimInvoiceAttachmentMarking: React.FC<InterimInvoiceAttachmentMarkingProps> = ({
  attachments,
  value,
  onChange,
  mode,
  disabled = false,
}) => {
  const styles = useStyles();

  const setMark = (id: string, mark: InvoiceAttachmentMark): void => {
    const next: Record<string, InvoiceAttachmentMark> = { ...value };
    // One Invoice at a time: choosing a new one makes the previous one Supporting (its default).
    if (mark === 'invoice') {
      for (const attachment of attachments) {
        if (attachment.id !== id && next[attachment.id] === 'invoice') {
          next[attachment.id] = 'supporting';
        }
      }
    }
    next[id] = mark;
    onChange(next);
  };

  return (
    <div className={styles.list} role="list" aria-label="Attachments to submit">
      {attachments.map(attachment => {
        const rowDisabled = disabled || Boolean(attachment.disabledReason);
        const mark = attachment.disabledReason ? 'skip' : markOf(value, attachment.id);
        return (
          <div
            key={attachment.id}
            role="listitem"
            aria-label={attachment.fileName}
            className={mergeClasses(styles.row, attachment.disabledReason ? styles.rowDisabled : undefined)}
          >
            <Text weight="semibold" className={styles.name}>
              {attachment.fileName}
            </Text>
            <Text size={200} className={styles.meta}>
              {formatSize(attachment.sizeBytes)}
              {attachment.suggestedInvoice && !attachment.disabledReason ? ' · looks like the invoice' : ''}
            </Text>
            {attachment.disabledReason && (
              <Text size={200} className={styles.reason}>
                <InfoRegular className={styles.reasonIcon} aria-hidden="true" />
                <span>{attachment.disabledReason}</span>
              </Text>
            )}
            <RadioGroup
              layout="horizontal"
              className={styles.radios}
              value={mark}
              disabled={rowDisabled}
              aria-label={`What to do with ${attachment.fileName}`}
              onChange={(_, data) => setMark(attachment.id, data.value as InvoiceAttachmentMark)}
            >
              {mode !== 'supporting-only' && <Radio value="invoice" label="Invoice" />}
              <Radio value="supporting" label="Supporting" />
              <Radio value="skip" label="Don't save" />
            </RadioGroup>
          </div>
        );
      })}
    </div>
  );
};
