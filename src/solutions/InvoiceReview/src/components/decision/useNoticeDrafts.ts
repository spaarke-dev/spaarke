/**
 * Asking for the draft of the notice to the firm (ebilling-r1 task 132ui; ISS-041): the one place that calls the draft route and turns its
 * answer into a Notify-step entry. The decision wizard and the "Finish decision…" form both use it, so the draft call, the once-per-kind rule
 * and the failure wording are the same wherever the Notify step shows.
 *
 * The caller owns where the entries live (the wizard keeps them in its form, the Finish form in its own state) and passes how to write them.
 */

import * as React from 'react';
import { decisionFailureMessage, type FirmNoticeKind, type InvoiceDecisionApi } from '../../services/decisionApi';
import { subjectSuffixOf, type NoticeEntry } from '../../services/noticeModel';

export interface NoticeDraftWriter {
  /** Write part of one kind's entry (the loading mark). */
  patch: (kind: FirmNoticeKind, patch: Partial<NoticeEntry>) => void;
  /**
   * Write the answer. It must land only on an entry still waiting for it (`status` `loading`, see `settleNoticeEntries`): a decision that came
   * back unsure has fixed the entry as it was sent, and a late draft must not change what the repeat will carry.
   */
  settle: (kind: FirmNoticeKind, patch: Partial<NoticeEntry>) => void;
}

/** `loadNotice(kind)` asks once per kind (until `reset`); `loadNotice(kind, true)` asks again after a failure. */
export function useNoticeDrafts(api: Pick<InvoiceDecisionApi, 'getFirmNoticeDraft'>, invoiceId: string, writer: NoticeDraftWriter) {
  // The kinds already asked for: a kind is asked for once (a step may mount again, or twice under StrictMode).
  const asked = React.useRef<Set<FirmNoticeKind>>(new Set());
  // Fix round 1 (K): each request carries the generation it was asked in; `reset` starts a new one, and an answer from an older one is dropped, so a
  // draft still in flight from an earlier opening (cancelled, then reopened) cannot settle the new opening's entry. Nothing resets the wizard's.
  const generation = React.useRef(0);
  const { patch, settle } = writer;
  const loadNotice = React.useCallback(
    (kind: FirmNoticeKind, retry = false) => {
      if (!retry && asked.current.has(kind)) return;
      asked.current.add(kind);
      const asking = generation.current;
      patch(kind, { status: 'loading', failure: null });
      void api.getFirmNoticeDraft(invoiceId, kind).then(r => {
        if (asking !== generation.current) return;
        if (r.ok) {
          // On by default once a draft has loaded; the reviewer turns it off, or skips the step, to send the decision alone.
          settle(kind, { status: 'ready', draft: r.value, subjectSuffix: subjectSuffixOf(r.value.subject), subject: r.value.subject, prose: r.value.prose, send: true, failure: null });
        } else {
          // Never blocks the decision: the message and code are shown with Retry, and nothing is sent for this kind.
          // The server's message with its code (the disabled answer is the one sentence that carries no code of its own).
          const text = decisionFailureMessage(r.failure);
          const code = r.failure.errorCode;
          // Review round 1 K2: an answer with no code of its own (a 429 from the rate limiter) shows its HTTP status instead.
          const tag = code ?? (r.failure.status > 0 && !r.failure.signInFailed ? `HTTP ${r.failure.status}` : null);
          settle(kind, { status: 'failed', draft: null, send: false, failure: tag && !text.includes(tag) ? `${text} (${tag})` : text });
        }
      });
    },
    [api, invoiceId, patch, settle]
  );
  /** Forget what was asked for, so the next opening of a form asks again; answers still in flight are ignored. */
  const reset = React.useCallback(() => {
    asked.current.clear();
    generation.current += 1;
  }, []);
  return { loadNotice, reset };
}
