import React from 'react';
import { Radio, RadioGroup, Text, makeStyles, mergeClasses, tokens, useId } from '@fluentui/react-components';

/**
 * INTERIM — a local copy of ebilling-r1's shared `InvoiceAttachmentMarking` (ebilling task 118a, PR #2342,
 * `src/client/shared/Spaarke.Communication.Components/src/components/InvoiceAttachmentMarking/`), task 133 slice A.
 *
 * It is a DROP-IN: the exported names, props, pure functions and their results are the ones #2342 exports from its
 * folder's `index.ts` (`InvoiceAttachmentMarking`, `InvoiceAttachmentMarkingProps`, `defaultInvoiceMarking`,
 * `validateInvoiceMarking`, `InvoiceAttachmentRole`, `MarkableAttachment`), and the rules are #2342's. Slice B deletes
 * this file and points the import at the shared component (webpack + jest alias and a type shim, never the barrel —
 * task 100 rule). It does NOT import #2342 (not on master yet). Do not grow or change its API: anything that differs
 * from #2342 is something slice B would silently change.
 *
 * Rules (owner decisions 2026-10-21; model 9.1):
 * - Invoice is PDF only (content type says PDF, else a `.pdf` file name); a non-PDF row offers no Invoice choice;
 * - pre-select: exactly one `suggestedInvoice` PDF, else (none suggested) exactly one PDF; every other attachment is
 *   Supporting; the user can choose Don't save;
 * - an attachment with a `disabledReason` is Don't save and cannot be changed.
 */

export type InvoiceAttachmentRole = 'invoice' | 'supporting' | 'skip';

export interface MarkableAttachment {
  /** Opaque: sprk_document id in the email UI, Office attachment id in the add-in. */
  id: string;
  fileName: string;
  contentType?: string;
  sizeBytes?: number;
  suggestedInvoice?: boolean;
  disabledReason?: string;
}

type Mode = 'new' | 'correction' | 'supporting-only';

const PDF_NAME = /\.pdf$/i;

/** Invoice is PDF only (model 9.1). Content type wins when it says PDF; the file name is the fallback. */
function isPdf(a: MarkableAttachment): boolean {
  return /pdf/i.test(a.contentType ?? '') || PDF_NAME.test(a.fileName.trim());
}

/** Pre-select an invoice only when exactly one candidate exists; everything else is 'supporting' (or 'skip' when disabled). */
export function defaultInvoiceMarking(
  attachments: readonly MarkableAttachment[],
  mode: Mode
): Record<string, InvoiceAttachmentRole> {
  const next: Record<string, InvoiceAttachmentRole> = {};
  for (const a of attachments) next[a.id] = a.disabledReason ? 'skip' : 'supporting';
  if (mode === 'supporting-only') return next;

  const pdfs = attachments.filter(a => !a.disabledReason && isPdf(a));
  const suggested = pdfs.filter(a => a.suggestedInvoice);
  const pick =
    suggested.length === 1 ? suggested[0] : suggested.length === 0 && pdfs.length === 1 ? pdfs[0] : undefined;
  if (pick) next[pick.id] = 'invoice';
  return next;
}

/**
 * Validate a marking. Counts and mode are checked from `value` alone; pass `attachments` (optional third argument) to
 * also refuse a disabled attachment marked anything but 'skip' ('disabled-attachment-marked') and a non-PDF marked
 * 'invoice' ('invoice-not-allowed').
 *
 * `value` must hold every attachment id: `defaultInvoiceMarking` does. The component shows an id missing from `value`
 * as 'supporting', but validation and a host's wire marks read `value` only.
 */
export function validateInvoiceMarking(
  value: Record<string, InvoiceAttachmentRole>,
  mode: Mode,
  attachments?: readonly MarkableAttachment[]
):
  | { ok: true }
  | {
      ok: false;
      reason: 'no-invoice' | 'more-than-one-invoice' | 'invoice-not-allowed' | 'disabled-attachment-marked';
    } {
  if (attachments?.some(a => a.disabledReason && value[a.id] !== undefined && value[a.id] !== 'skip')) {
    return { ok: false, reason: 'disabled-attachment-marked' };
  }
  const invoiceIds = Object.keys(value).filter(id => value[id] === 'invoice');
  if (mode === 'supporting-only') {
    return invoiceIds.length > 0 ? { ok: false, reason: 'invoice-not-allowed' } : { ok: true };
  }
  if (attachments) {
    const byId = new Map(attachments.map(a => [a.id, a] as const));
    for (const id of invoiceIds) {
      const a = byId.get(id);
      if (a && (!isPdf(a) || a.disabledReason)) return { ok: false, reason: 'invoice-not-allowed' };
    }
  }
  if (invoiceIds.length === 0) return { ok: false, reason: 'no-invoice' };
  if (invoiceIds.length > 1) return { ok: false, reason: 'more-than-one-invoice' };
  return { ok: true };
}

/** Whether a row offers the Invoice choice. Not exported (#2342 keeps it internal to its folder). */
function canBeInvoice(a: MarkableAttachment, mode: Mode): boolean {
  return mode !== 'supporting-only' && !a.disabledReason && isPdf(a);
}

export interface InvoiceAttachmentMarkingProps {
  attachments: MarkableAttachment[];
  value: Record<string, InvoiceAttachmentRole>;
  onChange: (next: Record<string, InvoiceAttachmentRole>) => void;
  mode: 'new' | 'correction' | 'supporting-only';
  disabled?: boolean;
}

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalS },
  row: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    columnGap: tokens.spacingHorizontalM,
    padding: tokens.spacingVerticalS,
    borderRadius: tokens.borderRadiusMedium,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  rowDisabled: { backgroundColor: tokens.colorNeutralBackground3 },
  info: { display: 'flex', flexDirection: 'column', minWidth: 0 },
  name: { overflowWrap: 'anywhere', color: tokens.colorNeutralForeground1 },
  meta: { color: tokens.colorNeutralForeground3 },
  reason: { color: tokens.colorNeutralForeground3, fontStyle: 'italic' },
});

function formatSize(bytes?: number): string | undefined {
  if (bytes === undefined || bytes < 0) return undefined;
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export const InvoiceAttachmentMarking: React.FC<InvoiceAttachmentMarkingProps> = ({
  attachments,
  value,
  onChange,
  mode,
  disabled,
}) => {
  const styles = useStyles();
  const baseId = useId('invoice-marking');

  return (
    <div className={styles.root} role="list">
      {attachments.map((a, i) => {
        const locked = !!a.disabledReason;
        const role: InvoiceAttachmentRole = locked ? 'skip' : (value[a.id] ?? 'supporting');
        const size = formatSize(a.sizeBytes);
        const nameId = `${baseId}-${i}`;
        return (
          <div key={a.id} role="listitem" className={mergeClasses(styles.row, locked && styles.rowDisabled)}>
            <div className={styles.info}>
              <Text className={styles.name} id={nameId}>
                {a.fileName}
              </Text>
              {size && (
                <Text size={200} className={styles.meta}>
                  {size}
                </Text>
              )}
              {locked && (
                <Text size={200} className={styles.reason}>
                  {a.disabledReason}
                </Text>
              )}
            </div>
            <RadioGroup
              layout="horizontal"
              aria-labelledby={nameId}
              value={role}
              disabled={disabled || locked}
              onChange={(_e, data) => {
                if (disabled || locked) return;
                onChange({ ...value, [a.id]: data.value as InvoiceAttachmentRole });
              }}
            >
              {canBeInvoice(a, mode) && <Radio value="invoice" label="Invoice" />}
              <Radio value="supporting" label="Supporting" />
              <Radio value="skip" label="Don't save" />
            </RadioGroup>
          </div>
        );
      })}
    </div>
  );
};
