/**
 * CreateInvoiceWizard.tsx
 * The internal "Create invoice" wizard (ebilling-r1 task 052; FR-19, D-19, D-24, D-91, ADR-002).
 *
 * It is the shared `InvoiceSubmissionWizard` (task 051) mounted against the INTERNAL submission routes (task 044): the user chooses
 * the core record (matter, project or work assignment) and, where the record has more than one linked firm, the firm; uploads the
 * invoice PDF; gives the firm invoice number (D-91); and sees the server's result (Processing, then Accepted with the tracking number,
 * or Rejected). The server creates the invoice. No code here creates `sprk_invoice` or `sprk_invoicesubmission` (FR-17): the client
 * previews and calls the server only.
 *
 * A submission the rejection rule would reject is held, not lost (D-101). The result screen then shows the findings and, per the
 * SERVER's own answer (task 105), Override (the eBilling Override privilege, a mandatory reason) and Return to firm
 * (`BlockedSubmissionPanel`).
 *
 * The follow-on cards the earlier wizard offered (Send Email / Add To Do / Assign Work, bound to the new invoice) are gone: the
 * invoice is created asynchronously by the server after processing, so its id is not known when the wizard finishes.
 *
 * Dependencies are injected via props (no solution-specific imports, ADR-012):
 *   - dataService: IDataService, Dataverse READS for the record picker
 *   - authenticatedFetch / bffBaseUrl: the BFF transport
 *
 * Default export enables React.lazy() dynamic import (same pattern as CreateEventWizard).
 *
 * @see InvoiceSubmission/InvoiceSubmissionWizard.tsx — the shared flow
 * @see internalInvoiceSubmission.ts — the adapter onto the internal routes
 * @see BlockedSubmissionPanel.tsx — findings, Override, Return to firm
 */
import * as React from 'react';
import { MessageBar, MessageBarBody, Text, tokens } from '@fluentui/react-components';

import { InvoiceSubmissionWizard } from '../InvoiceSubmission';
import type { InvoiceSubmissionStatusView } from '../InvoiceSubmission';
import type { IDataService } from '../../types/serviceInterfaces';
import type { AssociationResult } from '../AssociateToStep/types';

import { BlockedSubmissionPanel } from './BlockedSubmissionPanel';
import {
  OVERRIDDEN_REASON_CODE,
  createInternalInvoiceSubmission,
  isBlockedStatus,
  resolveInitialInvoiceRecord,
} from './internalInvoiceSubmission';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ICreateInvoiceWizardProps {
  /** Whether the wizard dialog is open. */
  open: boolean;
  /** Called when the wizard is closed/cancelled, and on Done. */
  onClose: () => void;
  /** IDataService: Dataverse reads for the record picker. The wizard writes nothing to Dataverse. */
  dataService: IDataService;
  /** When true, hides the title bar (Dataverse modal provides chrome). */
  embedded?: boolean;
  /** MSAL-backed authenticated fetch function for the BFF routes. Required: without it the wizard says it is unavailable. */
  authenticatedFetch?: typeof fetch;
  /** BFF API base URL. Required, like `authenticatedFetch`. */
  bffBaseUrl?: string;
  /**
   * Optional pre-filled record (a matter, project or work assignment the wizard was launched from). The Record step starts with it
   * chosen, unless `lockAssociation` is also `true`.
   */
  initialAssociation?: AssociationResult;
  /**
   * When `true` and `initialAssociation` names a core record, only that record is offered (a launch from a record form or a visual).
   * Defaults to `false`.
   */
  lockAssociation?: boolean;
}

// The status poll keeps going after a terminal answer only for a held submission, which Override / Return can still move.
const keepPollingWhileHeld = (status: InvoiceSubmissionStatusView): boolean => isBlockedStatus(status);

// ---------------------------------------------------------------------------
// CreateInvoiceWizard
// ---------------------------------------------------------------------------

const CreateInvoiceWizard: React.FC<ICreateInvoiceWizardProps> = ({
  open,
  onClose,
  dataService,
  embedded,
  authenticatedFetch: authFetch,
  bffBaseUrl,
  initialAssociation,
  lockAssociation,
}) => {
  const initialRecord = React.useMemo(() => resolveInitialInvoiceRecord(initialAssociation), [initialAssociation]);

  // One adapter per mount: it keeps the firms it has read for a record. Rebuilt only when the transport or the launch changes.
  const client = React.useMemo(
    () =>
      authFetch && bffBaseUrl
        ? createInternalInvoiceSubmission({
            fetch: authFetch,
            baseUrl: bffBaseUrl,
            dataService,
            initialRecord,
            lockRecord: lockAssociation === true,
          })
        : null,
    [authFetch, bffBaseUrl, dataService, initialRecord, lockAssociation]
  );

  const renderReport = React.useCallback(
    (status: InvoiceSubmissionStatusView) =>
      client ? <BlockedSubmissionPanel submissionId={status.submissionId} client={client} /> : null,
    [client]
  );

  const renderAccepted = React.useCallback(
    (status: InvoiceSubmissionStatusView) =>
      status.outcomeReasonCode === OVERRIDDEN_REASON_CODE ? (
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }} data-testid="invoice-accepted-overridden">
          Accepted (overridden). The override and its reason are recorded in the Decision Record.
        </Text>
      ) : null,
    []
  );

  if (!client) {
    return open ? (
      <MessageBar intent="error" role="alert">
        <MessageBarBody>
          Invoice submission is not available: the connection to the service is not configured.
        </MessageBarBody>
      </MessageBar>
    ) : null;
  }

  return (
    <InvoiceSubmissionWizard
      open={open}
      embedded={embedded}
      title="Create New Invoice"
      onClose={onClose}
      initialRecord={initialRecord}
      loadRecords={client.loadRecords}
      loadFirms={client.loadFirms}
      submit={client.submit}
      getStatus={client.getStatus}
      renderReport={renderReport}
      keepPolling={keepPollingWhileHeld}
      renderAccepted={renderAccepted}
    />
  );
};

export default CreateInvoiceWizard;
export { CreateInvoiceWizard };
