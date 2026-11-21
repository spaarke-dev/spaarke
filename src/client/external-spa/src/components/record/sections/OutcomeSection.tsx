/**
 * Outcome — the Invoice record's firm outcome report (ebilling-r1 task 134, FR-38).
 *
 * READ-ONLY. Over `GET /api/v1/external/ebilling/invoices/{id}/outcome` (`api/ebilling-client.ts`), shown as the shared
 * `InvoiceOutcomeReport` inside the record section card. Loading, no-access, not-found and unavailable states are shown in the
 * card; a 503 `ebilling.disabled` (the kill switch) renders NOTHING, card included. Refresh re-reads (`refreshSignal`).
 */
import * as React from 'react';
import { OutcomeBody } from '../../ebilling/InvoiceOutcomeReport';
import { useFirmOutcome, type OutcomeSubject } from '../../ebilling/useFirmOutcome';
import type { RecordSectionContext } from '../recordTemplate.types';
import { RecordSectionCard } from './RecordSectionCard';

export const OutcomeSection: React.FC<RecordSectionContext> = ({ recordId, refreshSignal }) => {
  const subject = React.useMemo<OutcomeSubject>(() => ({ kind: 'invoice', id: recordId }), [recordId]);
  const { result, reload } = useFirmOutcome(subject, refreshSignal);
  if (result.state === 'disabled') return null;
  return (
    <RecordSectionCard id="outcome">
      <OutcomeBody result={result} onRetry={reload} />
    </RecordSectionCard>
  );
};
