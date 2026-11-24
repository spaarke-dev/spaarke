/**
 * useFirmOutcome — reads one invoice's or one submission's firm outcome and keeps its state (ebilling-r1 task 134).
 *
 * The state is {@link OutcomeResult} plus `loading`. A change of subject or `refreshSignal`, or a call to `reload`, reads again;
 * a stale answer (the subject changed while it was on its way) is dropped. The client never rejects: every failure is a state.
 */
import * as React from 'react';
import { getInvoiceOutcome, getSubmissionOutcome, type OutcomeResult } from '../../api/ebilling-client';

/** What the outcome is read for. */
export interface OutcomeSubject {
  kind: 'invoice' | 'submission';
  id: string;
}

export type OutcomeLoadState = { state: 'loading' } | OutcomeResult;

export function useFirmOutcome(
  subject: OutcomeSubject,
  refreshSignal = 0
): { result: OutcomeLoadState; reload: () => void } {
  const [result, setResult] = React.useState<OutcomeLoadState>({ state: 'loading' });
  const [attempt, setAttempt] = React.useState(0);
  const { kind, id } = subject;

  React.useEffect(() => {
    let cancelled = false;
    setResult({ state: 'loading' });
    const read = kind === 'invoice' ? getInvoiceOutcome(id) : getSubmissionOutcome(id);
    void read.then(answer => {
      if (!cancelled) setResult(answer);
    });
    return () => {
      cancelled = true;
    };
  }, [kind, id, refreshSignal, attempt]);

  const reload = React.useCallback(() => setAttempt(n => n + 1), []);
  return { result, reload };
}
