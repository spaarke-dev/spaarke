/**
 * Outcome — the Invoice record's firm outcome report (ebilling-r1 task 134, FR-38).
 *
 * READ-ONLY. Over `GET /api/v1/external/ebilling/invoices/{id}/outcome` (`api/ebilling-client.ts`), shown as the shared
 * `InvoiceOutcomeReport` inside the record section card. Loading, not-found and unavailable states are shown in the card;
 * a 503 `ebilling.disabled` (the kill switch) renders NOTHING, card included. Refresh re-reads (`refreshSignal`).
 *
 * WHO IS ASKED (spa-r4 F1; R4 FR-06/07, NFR-04: the UI never offers what the server answers 403). The route answers 403 to a
 * workforce systemuser and to any caller outside the invoice's firm (`FirmOutcomeAuthorizationFilter`), so the section makes
 * NO call, and renders nothing, unless `/me` says the caller belongs to at least one firm: the `InvoicesWidget` "Submit
 * invoice" rule (`callerFirmIdsOf`: `firms` is sent only when eBilling is on, lists the caller's own firms, and is `[]` for a
 * systemuser). A caller context that is not in yet, failed, or has `firms` null/absent (unknown, never "no firm") asks
 * nothing. A licensed systemuser asks nothing even if `firms` were sent (the filter refuses one before anything else).
 *
 * WHY "A FIRM", NOT "THIS INVOICE'S FIRM". The invoice record read (`RECORD_DETAIL_ENTITIES.sprk_invoice`) does not carry
 * `sprk_vendororg`, so the section cannot name the invoice's firm, and adding the column to the shared read would change a
 * contract this fix does not own. So a member of ANOTHER firm, on a shared matter, still passes this gate; the server's 403
 * for that caller is the BACKSTOP: a 403 that still happens renders nothing (as the disabled state does), never a
 * "no access" card. The residual cost is one refused read for that caller, with nothing shown.
 */
import * as React from 'react';
import { OutcomeBody } from '../../ebilling/InvoiceOutcomeReport';
import { useFirmOutcome, type OutcomeSubject } from '../../ebilling/useFirmOutcome';
import type { ExternalUserContextResponse } from '../../../auth/bff-client';
import { callerFirmIdsOf } from '../recordAccess';
import type { RecordSectionContext } from '../recordTemplate.types';
import { RecordSectionCard } from './RecordSectionCard';

/** True only when `/me` positively says the caller is a firm member and not a workforce systemuser (see WHO IS ASKED). */
export function callerMayReadFirmOutcome(callerContext: ExternalUserContextResponse | null): boolean {
  if (!callerContext) return false;
  if (callerContext.systemUserId) return false;
  return callerFirmIdsOf(callerContext).length > 0;
}

const OutcomeSectionBody: React.FC<Pick<RecordSectionContext, 'recordId' | 'refreshSignal'>> = ({
  recordId,
  refreshSignal,
}) => {
  const subject = React.useMemo<OutcomeSubject>(() => ({ kind: 'invoice', id: recordId }), [recordId]);
  const { result, reload } = useFirmOutcome(subject, refreshSignal);
  // `disabled`: the kill switch. `noAccess`: the backstop (a 403 the gate did not foresee). Neither shows a card.
  if (result.state === 'disabled' || result.state === 'noAccess') return null;
  return (
    <RecordSectionCard id="outcome">
      <OutcomeBody result={result} onRetry={reload} />
    </RecordSectionCard>
  );
};

export const OutcomeSection: React.FC<RecordSectionContext> = ({ recordId, refreshSignal, callerContext }) => {
  if (!callerMayReadFirmOutcome(callerContext)) return null;
  return <OutcomeSectionBody recordId={recordId} refreshSignal={refreshSignal} />;
};
