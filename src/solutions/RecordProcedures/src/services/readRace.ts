/**
 * The race every READ client runs under (ebilling-r1 task 084d-4 review round 1; moved out of `overridesApi.ts` by 084d-5b's review, F3): a
 * timeout, and the caller's abort. This module is the neutral home of that helper, so a read module never has to import the write client
 * (`overridesApi.ts`, which holds the 089 writes) to get it, and the source scan that keeps the read modules free of the write client
 * (`ProceduresPanel.test.tsx`) can name the module in full. It imports only a TYPE from the write client (erased at build).
 */

import type { WriteFailure } from './overridesApi';

/** How long a read the wizard only advises with may take before it is given up on (task 084d-4 review round 1): Confirm is never held longer. */
export const READ_TIMEOUT_MS = 20_000;

/** The `errorCode` of the failure a read that took longer than its timeout becomes (client-made: no response arrived). */
export const CODE_TIMED_OUT = 'client.timed_out';

/** The `errorCode` of a read the page itself abandoned (the wizard closed, or moved to another row). Nobody is shown it. */
export const CODE_ABANDONED = 'client.abandoned';

const clientFailure = (errorCode: string): WriteFailure => ({
  status: 0,
  errorCode,
  title: null,
  detail: null,
  problems: [],
  fieldErrors: [],
  signInFailed: false,
});
export const TIMED_OUT_FAILURE: WriteFailure = clientFailure(CODE_TIMED_OUT);
export const ABANDONED_FAILURE: WriteFailure = clientFailure(CODE_ABANDONED);

export type Raced<T> = { kind: 'value'; value: T } | { kind: 'timeout' } | { kind: 'aborted' };

/**
 * Run `start` with an abort signal that fires after `timeoutMs` or when `outer` aborts, and answer whichever comes first. The race does not
 * depend on `start` honouring the signal: a call that ignores it is still given up on at the timeout. A rejection of `start` before either
 * propagates; one after is dropped. The timer and the listener are always released.
 */
export async function raceTimeout<T>(
  timeoutMs: number,
  outer: AbortSignal | undefined,
  start: (signal: AbortSignal) => Promise<T>
): Promise<Raced<T>> {
  const ctrl = new AbortController();
  let timer: ReturnType<typeof setTimeout> | undefined;
  let onOuter: (() => void) | undefined;
  const stop = new Promise<Raced<T>>(resolve => {
    timer = setTimeout(() => {
      resolve({ kind: 'timeout' });
      ctrl.abort();
    }, timeoutMs);
    if (outer) {
      onOuter = () => {
        resolve({ kind: 'aborted' });
        ctrl.abort();
      };
      if (outer.aborted) onOuter();
      else outer.addEventListener('abort', onOuter, { once: true });
    }
  });
  const work = start(ctrl.signal).then((value): Raced<T> => ({ kind: 'value', value }));
  work.catch(() => undefined); // a rejection after the race was decided is not an error
  try {
    return await Promise.race([work, stop]);
  } finally {
    if (timer !== undefined) clearTimeout(timer);
    if (outer && onOuter) outer.removeEventListener('abort', onOuter);
  }
}
