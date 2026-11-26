/**
 * queryDirtyState - the parent side of the cross-frame dirty-check protocol (RecordNavigationModalShell README, "Cross-frame
 * dirty-check protocol"), as a plain function so a modal that composes `SprkModal` directly and guards its own browse nav
 * (`nav.onBeforeNavigate`) runs the SAME round trip as `RecordNavigationModalShell`.
 *
 * Posts `{ type: 'request-dirty-check', correlationId }` to `targetWindow` and resolves with the frame's answer. It resolves `false`
 * (clean) when there is no target window, when the frame does not answer within `timeoutMs`, when the post throws (a detached frame),
 * and when only untrusted answers arrive (an untrusted origin, another correlationId or, with `requireSourceMatch`, another window
 * are all ignored, so they are the same as no answer). The listener lives only for the round trip.
 *
 * Extracted unchanged from `RecordNavigationModalShell` (which now calls it); `requireSourceMatch` is the one addition and is off by
 * default, so the shell's behaviour is as it was.
 *
 * @see ADR-050 - `nav.onBeforeNavigate` is the seam for this protocol
 */

import {
  DIRTY_CHECK_REQUEST_TYPE,
  DIRTY_CHECK_RESULT_TYPE,
  type IDirtyCheckRequest,
  type IDirtyCheckResponse,
} from './types';

let correlationCounter = 0;

/**
 * A unique correlation id for one round trip. Falls back to `Math.random()` + a counter when `crypto.randomUUID` is unavailable
 * (older jsdom, IE-edge cases).
 */
export function generateCorrelationId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  correlationCounter += 1;
  return `rnms-${Date.now()}-${correlationCounter}-${Math.floor(Math.random() * 1e9)}`;
}

export interface QueryDirtyStateOptions {
  /** The frame's `contentWindow`. Absent or null: no round trip, the answer is clean. */
  targetWindow: Window | null | undefined;
  /** `targetOrigin` of the outbound `postMessage`. Default `'*'`; pin it to the frame's origin where it is known. */
  targetOrigin?: string;
  /** Milliseconds before the frame is treated as clean. Default 1000 (the protocol's rule). */
  timeoutMs?: number;
  /** Whether an answer from this origin is trusted. Answers from any other origin are ignored. */
  isOriginTrusted: (origin: string) => boolean;
  /** When true, an answer counts only if `event.source` is `targetWindow` (another window's answer is ignored). Default false. */
  requireSourceMatch?: boolean;
}

/** Ask the frame whether it holds unsaved work. Never rejects. */
export function queryDirtyState({
  targetWindow,
  targetOrigin = '*',
  timeoutMs = 1000,
  isOriginTrusted,
  requireSourceMatch = false,
}: QueryDirtyStateOptions): Promise<boolean> {
  if (!targetWindow) {
    return Promise.resolve(false);
  }

  return new Promise<boolean>(resolve => {
    const correlationId = generateCorrelationId();
    let settled = false;

    const handleMessage = (ev: MessageEvent): void => {
      if (settled) return;
      if (requireSourceMatch && ev.source !== targetWindow) return;
      // Validate inbound origin against the allow-list.
      if (!isOriginTrusted(ev.origin)) return;
      // Validate payload shape.
      const data = ev.data as Partial<IDirtyCheckResponse> | null;
      if (!data || typeof data !== 'object') return;
      if (data.type !== DIRTY_CHECK_RESULT_TYPE) return;
      if (data.correlationId !== correlationId) return;
      settled = true;
      window.removeEventListener('message', handleMessage);
      clearTimeout(timeoutId);
      resolve(Boolean(data.dirty));
    };

    const timeoutId = setTimeout(() => {
      if (settled) return;
      settled = true;
      window.removeEventListener('message', handleMessage);
      // Timeout: treat as clean per spec FR-14 fallback semantics.
      resolve(false);
    }, timeoutMs);

    window.addEventListener('message', handleMessage);

    const request: IDirtyCheckRequest = {
      type: DIRTY_CHECK_REQUEST_TYPE,
      correlationId,
    };
    try {
      targetWindow.postMessage(request, targetOrigin);
    } catch {
      // postMessage can throw if the target window is cross-origin and detached (e.g. iframe was just unmounted). Treat as clean.
      if (!settled) {
        settled = true;
        window.removeEventListener('message', handleMessage);
        clearTimeout(timeoutId);
        resolve(false);
      }
    }
  });
}
