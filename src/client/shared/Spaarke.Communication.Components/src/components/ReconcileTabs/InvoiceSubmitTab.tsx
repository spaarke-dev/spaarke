/**
 * InvoiceSubmitTab - the Email Review dialog's "Invoice" tab (ebilling-r1 task 118).
 *
 * Holds the SAME flow the toolbar and a flagged attachment open (`SubmitInvoiceFromEmail`); the Email Review tab
 * host takes one component per tab, so this is that component. `ReconciliationWorkspace` shows the tab only when
 * the selected email has a PDF attachment (owner 2026-10-21). Unlike Fields / Tasks it does not wait for a confirmed
 * Related-to: the route files an unfiled email under the chosen record itself, so a confirmed record is only a
 * starting choice here.
 *
 * Patterned on `TaskReconcileTab` (structure and host wiring); ADR-012 (everything arrives as props), ADR-021.
 */
import * as React from 'react';
import { makeStyles, tokens } from '@fluentui/react-components';
import { resolveInitialInvoiceRecord } from '@spaarke/ui-components';
import { SubmitInvoiceFromEmail } from '../SubmitInvoice';
import type { EmailInvoiceSubmission } from '../../logic/invoice';
import type { ReconcileRegarding } from './FieldUpdateReconcileTab';

const useStyles = makeStyles({
  root: { padding: tokens.spacingHorizontalL, overflowY: 'auto', height: '100%', boxSizing: 'border-box' },
});

export interface InvoiceSubmitTabProps {
  /** The `sprk_communication` open in the dialog. */
  communicationId: string;
  /** The injected transport (the same object the toolbar flow uses). */
  submission: EmailInvoiceSubmission;
  /** The email's confirmed Related-to record, when there is one: a Matter, Project or Work Assignment starts as the chosen record. */
  regarding?: ReconcileRegarding | null;
  /** Milliseconds between status reads while the submission is processed. Default 2000. */
  pollIntervalMs?: number;
}

export const InvoiceSubmitTab: React.FC<InvoiceSubmitTabProps> = ({
  communicationId,
  submission,
  regarding,
  pollIntervalMs,
}) => {
  const s = useStyles();
  const initialRecord = React.useMemo(
    () =>
      regarding
        ? resolveInitialInvoiceRecord({
            entityType: regarding.entityType,
            recordId: regarding.recordId,
            recordName: '',
          })
        : undefined,
    [regarding?.entityType, regarding?.recordId]
  );
  return (
    <div className={s.root} data-testid="invoice-submit-tab">
      {/* Keyed by the email: moving to another email in the queue starts a fresh flow, never carries marks across. */}
      <SubmitInvoiceFromEmail
        key={communicationId}
        communicationId={communicationId}
        submission={submission}
        initialRecord={initialRecord}
        pollIntervalMs={pollIntervalMs}
      />
    </div>
  );
};

InvoiceSubmitTab.displayName = 'InvoiceSubmitTab';
