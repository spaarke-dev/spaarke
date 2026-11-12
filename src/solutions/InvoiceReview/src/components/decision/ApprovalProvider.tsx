/**
 * The approval state of the open invoice (ebilling-r1 task 111; task 103 GET approval), shared by the header "Awaiting" line, the
 * "Decide invoice…" action and the wizard, which 110's page renders in three separate slots.
 *
 * One provider wraps the page. The slots call `sync(signature)` with a signature of what the approval depends on in the review (status, line
 * statuses, totals, history); the state is re-read when that changes (and only then, so a Processing poll does not hit the route every few
 * seconds). While the state says the invoice needs its firm, every sync re-reads (`approvalNeedsRead`: the Set-firm re-run finishing changes
 * nothing in the signature). Two slots syncing the same review make one request; a sync that arrives while a read is out marks it dirty and one more read runs.
 * A failed re-read keeps the state last shown (the server stays authoritative at the click) and records the failure; its signature is
 * not kept as seen, so the next sync of it asks again, and `refresh()` (the Retry on the header line) always asks.
 */

import * as React from 'react';
import type { ApiFailure } from '../../services/invoiceReviewApi';
import type { ApprovalState, InvoiceDecisionApi } from '../../services/decisionApi';
import { approvalNeedsRead } from '../../services/decisionModel';

export interface ApprovalContextValue {
  state: ApprovalState | null;
  /** The last read failed (state may still hold an earlier answer). */
  failure: ApiFailure | null;
  /** The first read has not finished. */
  loading: boolean;
  /** Re-read when `signature` differs from the last one asked for. */
  sync: (signature: string) => void;
  /** Re-read now; resolves with what is shown after the read (null when there is nothing to show). */
  refresh: () => Promise<ApprovalState | null>;
  api: InvoiceDecisionApi;
  invoiceId: string;
}

const Ctx = React.createContext<ApprovalContextValue | null>(null);

export function useApproval(): ApprovalContextValue {
  const v = React.useContext(Ctx);
  if (!v) throw new Error('useApproval needs an ApprovalProvider.');
  return v;
}

export interface ApprovalProviderProps {
  api: InvoiceDecisionApi;
  invoiceId: string;
  children: React.ReactNode;
}

export function ApprovalProvider({ api, invoiceId, children }: ApprovalProviderProps) {
  const [state, setState] = React.useState<ApprovalState | null>(null);
  const [failure, setFailure] = React.useState<ApiFailure | null>(null);
  const [loading, setLoading] = React.useState(true);

  const alive = React.useRef(true);
  const shown = React.useRef<ApprovalState | null>(null);
  const inflight = React.useRef<Promise<ApprovalState | null> | null>(null);
  const dirty = React.useRef(false);
  const seen = React.useRef<string | null>(null);

  React.useEffect(() => {
    alive.current = true;
    return () => {
      alive.current = false;
    };
  }, []);

  const run = React.useCallback(async (): Promise<ApprovalState | null> => {
    let lastOk = true;
    do {
      dirty.current = false;
      const r = await api.getApproval(invoiceId);
      if (!alive.current) return null;
      lastOk = r.ok;
      if (r.ok) {
        shown.current = r.value;
        setState(r.value);
        setFailure(null);
      } else {
        setFailure(r.failure);
      }
    } while (dirty.current);
    // A failed read must not count as seen: the next sync of the same signature (or Retry) asks again.
    if (!lastOk) seen.current = null;
    setLoading(false);
    return shown.current;
  }, [api, invoiceId]);

  const refresh = React.useCallback((): Promise<ApprovalState | null> => {
    if (inflight.current) {
      dirty.current = true;
      return inflight.current;
    }
    const p = run().finally(() => {
      inflight.current = null;
    });
    inflight.current = p;
    return p;
  }, [run]);

  const sync = React.useCallback(
    (signature: string) => {
      if (!approvalNeedsRead(seen.current, signature, shown.current)) return;
      seen.current = signature;
      void refresh();
    },
    [refresh]
  );

  const value = React.useMemo<ApprovalContextValue>(
    () => ({ state, failure, loading, sync, refresh, api, invoiceId }),
    [state, failure, loading, sync, refresh, api, invoiceId]
  );
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}
