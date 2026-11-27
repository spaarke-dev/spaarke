/**
 * The page's side of the cross-frame dirty-check protocol (ebilling-r1 task 195; `RecordNavigationModalShell` README, "Cross-frame
 * dirty-check protocol").
 *
 * The Console opens this page in an iframe inside the shared modal shell (`InvoiceReviewModal`), with "N of M" previous / next in its
 * header. Before the frame moves to another invoice the modal posts `{ type: 'request-dirty-check', correlationId }` to this page; the
 * page answers `{ type: 'dirty-check-result', correlationId, dirty }` and the modal asks "Discard what you haven't saved?" when `dirty`.
 *
 * Two parts, so no component has to know about the others:
 *  - `useReportDirty(dirty)`: a component that holds unsaved work says so while it does. The page is dirty while ANY reporter is.
 *  - `useDirtyCheckResponder()`: mounted once by the page; answers a request.
 *
 * Only a request from THIS window's origin is answered (the modal is same-origin when the Console and the page are both served from the
 * org); any other origin gets no answer, which the modal treats as clean. The reply goes to `event.source` only, pinned to that origin,
 * and carries the request's correlationId.
 */

import * as React from 'react';
import { DIRTY_CHECK_REQUEST_TYPE, DIRTY_CHECK_RESULT_TYPE } from '@spaarke/ui-components';
import type { IDirtyCheckRequest, IDirtyCheckResponse } from '@spaarke/ui-components';

/** One token per reporting component instance that is dirty right now. A set of tokens, not a counter: a re-render cannot double count. */
const dirtyReporters = new Set<symbol>();

/** True while any component holds unsaved work. */
export function isPageDirty(): boolean {
  return dirtyReporters.size > 0;
}

/** Report whether this component holds unsaved work (text typed, an entry changed, a form or the wizard open, a write in flight). */
export function useReportDirty(dirty: boolean): void {
  const token = React.useRef<symbol | null>(null);
  if (token.current === null) token.current = Symbol('invoice-review-dirty');
  React.useEffect(() => {
    const mine = token.current as symbol;
    if (dirty) dirtyReporters.add(mine);
    else dirtyReporters.delete(mine);
    return () => {
      dirtyReporters.delete(mine);
    };
  }, [dirty]);
}

/** Answer the modal's `request-dirty-check` for as long as the page is mounted. */
export function useDirtyCheckResponder(): void {
  React.useEffect(() => {
    const onMessage = (ev: MessageEvent): void => {
      if (ev.origin !== window.location.origin) return;
      const data = ev.data as Partial<IDirtyCheckRequest> | null;
      if (!data || typeof data !== 'object' || data.type !== DIRTY_CHECK_REQUEST_TYPE) return;
      if (typeof data.correlationId !== 'string' || data.correlationId.length === 0) return;
      const source = ev.source as Window | null;
      if (!source || typeof source.postMessage !== 'function') return;
      const reply: IDirtyCheckResponse = {
        type: DIRTY_CHECK_RESULT_TYPE,
        correlationId: data.correlationId,
        dirty: isPageDirty(),
      };
      source.postMessage(reply, ev.origin);
    };
    window.addEventListener('message', onMessage);
    return () => window.removeEventListener('message', onMessage);
  }, []);
}
