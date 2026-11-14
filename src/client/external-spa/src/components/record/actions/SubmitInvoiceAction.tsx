/**
 * SubmitInvoiceAction — the Matter, Project and Work assignment toolbar's "Submit invoice"
 * (spaarke-SPA-external-access-platform-r4 task 007, FR-10). It opens ebilling's submission page for this record
 * (`/invoices/submit?invoiceRecord={kind}:{id}`); the flow itself is ebilling's, R4 only links to it.
 *
 * OFFERED (hide, don't disable; the server re-checks Read, Create and the firm link on submit, so this is a display rule):
 *   - the host gave the template an `onNavigate` (the template imports no router);
 *   - `/me` `firms` is a non-empty array (the `InvoicesWidget` rule), AND
 *   - for at least one of those firms the firm-records answer lists this record under its kind (`submitInvoiceRightOf`).
 * Unknown (the caller context or an answer not in yet), a failed read, and an answer that does not list the record, even
 * one flagged `partial` / `truncated`, all leave it hidden.
 *
 * READS. One firm-records read per firm, not per render and not per record: the answer is the firm's list of linked
 * records, one read with no paging, so it is cached for the OPENING. The cache is keyed by the opening's `/me` object
 * (`useCallerContext` reads it once per modal instance and the openers mount a new instance per opening), so browsing to
 * the next record in the same opening reuses the answers and closing and reopening reads again. A failed read stays
 * failed for the opening (hidden); reopening retries.
 *
 * CLICK. Closes the record view first, then navigates on the next task, so the dialog is closed before a route change can
 * unmount the widget that mounted it (a Fluent Dialog unmounted while open leaves the page aria-hidden, #2658). While an
 * operation holds the view the close is refused and the click does nothing.
 */
import * as React from 'react';
import { readInvoiceFirmRecords } from '../../../api/web-api-client';
import type { ExternalUserContextResponse } from '../../../auth/bff-client';
import { invoiceSubmitPathFor } from '../../../pages/invoiceSubmitLink';
import { callerFirmIdsOf, invoiceKindOfEntity, submitInvoiceRightOf, type FirmRecordsRead } from '../recordAccess';
import type { RecordActionHostContext } from '../recordTemplate.types';

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
  callerContext,
  onActionChange,
  onNavigate,
  requestClose,
}) => {
  const kind = invoiceKindOfEntity(config.entity);
  const reads = useFirmRecordsReads(callerContext, kind !== undefined && onNavigate !== undefined);
  const visible = onNavigate !== undefined && submitInvoiceRightOf({ callerContext, kind, recordId, reads });

  // The click reads the latest props through a ref, so the offered action stays stable (a new `requestClose` on every
  // modal render would otherwise re-offer it on every render).
  const latest = React.useRef({ kind, recordId, onNavigate, requestClose });
  latest.current = { kind, recordId, onNavigate, requestClose };

  React.useEffect(() => {
    if (!visible) {
      onActionChange(null);
      return;
    }
    onActionChange({
      visible: true,
      onClick: () => {
        const { kind: currentKind, recordId: currentId, onNavigate: navigate, requestClose: close } = latest.current;
        if (!currentKind || !navigate || !close()) return;
        const path = invoiceSubmitPathFor(currentKind, currentId);
        window.setTimeout(() => navigate(path), 0);
      },
    });
    return () => onActionChange(null);
  }, [visible, onActionChange]);

  return null;
};
