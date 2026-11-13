/**
 * SubmitInvoiceAction — "Submit invoice" on a Matter, Project and Work assignment record
 * (spaarke-SPA-external-access-platform-r4 task 007 FR-10; task 010 moved it from a page to a modal over the record).
 * It opens ebilling's shared submission wizard in a CHILD `SprkModal` over the record view, with THIS record fixed
 * (`lockedRecord`: the wizard has no Record step and no picker). The flow itself is ebilling's; R4 hosts it.
 *
 * WHERE. A Matter and a Project list it under `sectionActions.invoices` (the Invoices section's header button); a Work
 * assignment has no Invoices section (no `sprk_invoice` lookup to it), so it keeps the action in the header toolbar. The
 * host mounts for either, and its dialog is a React descendant of the record modal (the same child-dialog arrangement as
 * Upload and New message).
 *
 * OFFERED (hide, don't disable; the server re-checks Read, Create and the firm link on submit, so this is a display rule):
 *   - `/me` `firms` is a non-empty array (the `InvoicesWidget` rule), AND
 *   - for at least one of those firms the firm-records answer lists this record under its kind (`submitInvoiceRightOf`).
 * Unknown (the caller context or an answer not in yet), a failed read, and an answer that does not list the record, even
 * one flagged `partial` / `truncated`, all leave it hidden. It needs no router: nothing navigates.
 *
 * READS. One firm-records read per firm, not per render and not per record: the answer is the firm's list of linked
 * records, one read with no paging, so it is cached for the OPENING. The cache is keyed by the opening's `/me` object
 * (`useCallerContext` reads it once per modal instance and the openers mount a new instance per opening), so browsing to
 * the next record in the same opening reuses the answers and closing and reopening reads again. A failed read stays
 * failed for the opening (hidden); reopening retries.
 *
 * THE DIALOG. A `SprkModal` of the record modal's own size (`lg`, so it sits where the record does), `explicit` dismiss
 * (the child-dialog Esc standard: Esc and the backdrop close neither this dialog nor the record, a half-filled invoice is
 * never lost; × and the wizard's Cancel close it). The wizard is mounted `embedded` with its own title bar hidden, so the
 * `SprkModal` is the one dialog (one focus trap, one close button) and its × goes to the wizard's `onClose`. The wizard is
 * mounted only while the dialog is open, so every opening starts clean. Focus returns to the control that opened it.
 *
 * IN FLIGHT (review 1). While a submission request is in flight the dialog cannot be closed: × (and Cancel) are ignored, and
 * the action reports busy (`onBusyChange`) so the record behind it is held too. Closing then would hide a refusal (a 4xx or
 * 5xx) the wizard is about to show; the wizard shows it and the user closes the dialog afterwards.
 *
 * REFRESH. Once the server has accepted the submission (`onSubmitted`), ANY close (Done, Cancel, ×) re-reads the record's
 * Invoices section, so the new invoice shows as soon as it exists. A submission that is accepted after the dialog was
 * closed mid-flight refreshes at once.
 */
import * as React from 'react';
import { makeStyles } from '@fluentui/react-components';
import { InvoiceSubmissionWizard } from '@spaarke/ui-components/components/InvoiceSubmission';
import type { InvoiceCoreRecordOption } from '@spaarke/ui-components/components/InvoiceSubmission';
import { SprkModal } from '@spaarke/ui-components/components/SprkModal';
import {
  getInvoiceSubmission,
  listInvoiceCoreRecords,
  listInvoiceFirms,
  readInvoiceFirmRecords,
  submitInvoice,
} from '../../../api/web-api-client';
import type { ExternalUserContextResponse } from '../../../auth/bff-client';
import { useChildDialog } from '../../createAction';
import { callerFirmIdsOf, invoiceKindOfEntity, submitInvoiceRightOf, type FirmRecordsRead } from '../recordAccess';
import { recordText } from '../recordTemplates';
import type { RecordActionHostContext } from '../recordTemplate.types';

const useStyles = makeStyles({
  // The unpadded `SprkModal` body is filled by the embedded wizard, which keeps its own footer on screen.
  frame: {
    height: '100%',
    minHeight: 0,
  },
});

/** Each opening's firm-records reads, by firm id (see READS above). */
const readsByOpening = new WeakMap<ExternalUserContextResponse, Map<string, Promise<FirmRecordsRead>>>();

function openingReads(callerContext: ExternalUserContextResponse): Map<string, Promise<FirmRecordsRead>> {
  let reads = readsByOpening.get(callerContext);
  if (!reads) {
    reads = new Map();
    readsByOpening.set(callerContext, reads);
  }
  return reads;
}

/** The firm-records answers of the caller's firms, as they arrive (`undefined` until a firm's answer is in). */
function useFirmRecordsReads(
  callerContext: ExternalUserContextResponse | null,
  enabled: boolean
): Readonly<Record<string, FirmRecordsRead>> {
  const [answers, setAnswers] = React.useState<Readonly<Record<string, FirmRecordsRead>>>({});
  const firmIds = React.useMemo(() => callerFirmIdsOf(callerContext), [callerContext]);

  React.useEffect(() => {
    if (!enabled || !callerContext || firmIds.length === 0) return;
    let cancelled = false;
    const reads = openingReads(callerContext);
    for (const firmId of firmIds) {
      let read = reads.get(firmId);
      if (!read) {
        read = readInvoiceFirmRecords(firmId).then(
          answer => answer,
          (): FirmRecordsRead => 'failed'
        );
        reads.set(firmId, read);
      }
      void read.then(answer => {
        if (!cancelled) setAnswers(prev => (prev[firmId] === answer ? prev : { ...prev, [firmId]: answer }));
      });
    }
    return () => {
      cancelled = true;
    };
  }, [callerContext, enabled, firmIds]);

  return answers;
}

export const SubmitInvoiceAction: React.FC<RecordActionHostContext> = ({
  config,
  recordId,
  record,
  callerContext,
  onActionChange,
  onBusyChange,
  refreshSection,
}) => {
  const styles = useStyles();
  const kind = invoiceKindOfEntity(config.entity);
  const reads = useFirmRecordsReads(callerContext, kind !== undefined);
  const visible = submitInvoiceRightOf({ callerContext, kind, recordId, reads });
  const dialog = useChildDialog();

  // The record the wizard fixes: this one, named as the record view names it (never read again).
  const lockedRecord = React.useMemo<InvoiceCoreRecordOption | undefined>(() => {
    if (!kind) return undefined;
    return {
      kind,
      recordId,
      name: recordText(record, config.nameAttribute) ?? config.label,
      number: config.titleNumberAttribute ? recordText(record, config.titleNumberAttribute) : null,
    };
  }, [kind, recordId, record, config.nameAttribute, config.titleNumberAttribute, config.label]);

  // Set by `onSubmitted`, consumed by the next close (see REFRESH above). `isOpen` lets a late `onSubmitted` (after the
  // dialog was closed mid-flight) refresh immediately.
  const submitted = React.useRef(false);
  const isOpen = React.useRef(false);
  isOpen.current = dialog.open;

  // A submission request in flight: the dialog stays open until it settles (see IN FLIGHT above).
  const inFlight = React.useRef(false);
  const trackedSubmit = React.useCallback(
    async (input: Parameters<typeof submitInvoice>[0]): ReturnType<typeof submitInvoice> => {
      inFlight.current = true;
      onBusyChange(true);
      try {
        return await submitInvoice(input);
      } finally {
        inFlight.current = false;
        onBusyChange(false);
      }
    },
    [onBusyChange]
  );
  // Never leave the record held if this host goes away mid-request.
  React.useEffect(() => () => onBusyChange(false), [onBusyChange]);

  const { hide } = dialog;
  const handleClose = React.useCallback((): void => {
    if (inFlight.current) return;
    hide();
    if (submitted.current) {
      submitted.current = false;
      refreshSection('invoices');
    }
  }, [hide, refreshSection]);

  const handleSubmitted = React.useCallback((): void => {
    if (isOpen.current) {
      submitted.current = true;
    } else {
      refreshSection('invoices');
    }
  }, [refreshSection]);

  // The click reads `show` through a ref, so the offered action stays stable across renders.
  const latest = React.useRef({ show: dialog.show });
  latest.current = { show: dialog.show };

  React.useEffect(() => {
    if (!visible) {
      onActionChange(null);
      return;
    }
    onActionChange({ visible: true, onClick: () => latest.current.show() });
    return () => onActionChange(null);
  }, [visible, onActionChange]);

  if (!dialog.open || !lockedRecord) return null;
  return (
    <SprkModal open onClose={handleClose} title="Submit invoice" size="lg" dismiss="explicit" padded={false}>
      <div className={styles.frame} data-testid="submit-invoice-modal">
        <InvoiceSubmissionWizard
          embedded
          hideTitle
          open
          title="Submit invoice"
          doneLabel="Done"
          lockedRecord={lockedRecord}
          loadRecords={listInvoiceCoreRecords}
          loadFirms={listInvoiceFirms}
          submit={trackedSubmit}
          getStatus={getInvoiceSubmission}
          onSubmitted={handleSubmitted}
          onClose={handleClose}
        />
      </div>
    </SprkModal>
  );
};
