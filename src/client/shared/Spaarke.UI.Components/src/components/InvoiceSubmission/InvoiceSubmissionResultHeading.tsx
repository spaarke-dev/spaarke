/**
 * InvoiceSubmissionResultHeading — the title and icon above an invoice submission's outcome (ebilling-r1 iss048).
 *
 * The wizard's success config is built once, when the submit is accepted, and the outcome is polled afterwards. A fixed
 * "Invoice received" title and success icon therefore stayed over a Failed or Rejected answer. The wizard now puts
 * {@link InvoiceResultTitle} and {@link InvoiceResultIcon} in that config; both read a per-submission {@link createStatusStore}
 * that `InvoiceSubmissionOutcome.onStatusChange` feeds, so the heading, the icon and the body follow ONE poll.
 *
 *   Received / Processing / not yet known / Accepted but held for review: "Invoice received", the checkmark icon.
 *   Accepted:  "Invoice accepted", the checkmark icon.
 *   Rejected:  "Invoice rejected", an error document icon.
 *   Failed:    "Invoice not processed", a warning document icon.
 *
 * Fluent v9 tokens only (ADR-021); no host import (ADR-012).
 */
import * as React from 'react';
import { makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { DocumentCheckmarkRegular, DocumentDismissRegular, DocumentErrorRegular } from '@fluentui/react-icons';
import { nonBlank } from './InvoiceSubmissionOutcome';
import type { InvoiceSubmissionStatusView } from './types';

/** Which heading a status reads as. */
export type InvoiceResultKind = 'received' | 'accepted' | 'rejected' | 'failed';

/** The heading kind for a status read; null (nothing read yet) and any non-terminal status read as "received". */
export function invoiceResultKind(view: InvoiceSubmissionStatusView | null | undefined): InvoiceResultKind {
  switch (view?.status) {
    case 'Accepted':
      // A held Accepted invoice (the outcome shows its warning bar and "In Review" badge) has been received, not yet accepted.
      return nonBlank(view.warning) ? 'received' : 'accepted';
    case 'Rejected':
      return 'rejected';
    case 'Failed':
      return 'failed';
    default:
      return 'received';
  }
}

const TITLES: Record<InvoiceResultKind, string> = {
  received: 'Invoice received',
  accepted: 'Invoice accepted',
  rejected: 'Invoice rejected',
  failed: 'Invoice not processed',
};

/** The last status read of one submission, shared by the outcome (which writes it) and the heading and icon (which read it). */
export interface InvoiceSubmissionStatusStore {
  get(): InvoiceSubmissionStatusView | null;
  set(view: InvoiceSubmissionStatusView): void;
  subscribe(listener: () => void): () => void;
}

/** One store per submission: the wizard makes one when the submit is accepted. */
export function createStatusStore(): InvoiceSubmissionStatusStore {
  let current: InvoiceSubmissionStatusView | null = null;
  const listeners = new Set<() => void>();
  return {
    get: () => current,
    set: view => {
      current = view;
      listeners.forEach(l => l());
    },
    subscribe: listener => {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
  };
}

function useResultKind(store: InvoiceSubmissionStatusStore): InvoiceResultKind {
  const [kind, setKind] = React.useState<InvoiceResultKind>(() => invoiceResultKind(store.get()));
  React.useEffect(() => {
    const sync = () => setKind(invoiceResultKind(store.get()));
    sync(); // a status read between the store's creation and this mount
    return store.subscribe(sync);
  }, [store]);
  return kind;
}

const useStyles = makeStyles({
  icon: {
    fontSize: '48px',
    display: 'inline-flex',
  },
  neutral: { color: tokens.colorBrandForeground1 },
  error: { color: tokens.colorStatusDangerForeground1 },
  warning: { color: tokens.colorStatusWarningForeground1 },
});

export const InvoiceResultTitle: React.FC<{ store: InvoiceSubmissionStatusStore }> = ({ store }) => {
  const kind = useResultKind(store);
  return <span data-testid="invoice-result-title">{TITLES[kind]}</span>;
};
InvoiceResultTitle.displayName = 'InvoiceResultTitle';

export const InvoiceResultIcon: React.FC<{ store: InvoiceSubmissionStatusStore }> = ({ store }) => {
  const styles = useStyles();
  const kind = useResultKind(store);
  const Icon =
    kind === 'rejected' ? DocumentDismissRegular : kind === 'failed' ? DocumentErrorRegular : DocumentCheckmarkRegular;
  const tone = kind === 'rejected' ? styles.error : kind === 'failed' ? styles.warning : styles.neutral;
  return (
    <span className={mergeClasses(styles.icon, tone)} data-testid="invoice-result-icon" data-outcome={kind}>
      <Icon />
    </span>
  );
};
InvoiceResultIcon.displayName = 'InvoiceResultIcon';
