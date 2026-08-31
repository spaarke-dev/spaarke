/**
 * useComposeCheckoutLifecycle.honestFailure.test.tsx — FR-S09 item 4 (spaarkeai-compose-r8 task 016)
 *
 * The checkout lifecycle carried the SAME dead-`!response.ok` defect FR-S01 removed from the save path.
 * `authenticatedFetch` (ADR-028) returns ONLY when `response.ok` and THROWS a typed `ApiError` on every
 * non-2xx — so the `if (response.status === 409)` conflict branch, the 404/403 copy, and the discard
 * path's `if (!discardResponse.ok)` block were all unreachable from the day that contract landed.
 *
 * Two consequences, both user-facing and neither cosmetic:
 *
 *   1. A document locked by a COLLEAGUE never produced `checkoutConflict`, so the conflict banner (and
 *      the lock holder's name) never rendered. The user got one undifferentiated "Could not acquire
 *      document lock: HTTP 409".
 *   2. Force-close's 400 case — "the other session released the lock between our probe and our discard"
 *      — is the SUCCESS path. Being dead, it threw instead, so the one action available inside a
 *      non-dismissible conflict dialog reported failure every time it actually worked.
 *
 * These tests drive REAL thrown `ApiError`s (never a mocked `{ ok: false }` Response, which is a shape
 * the transport cannot produce — see the same note in ComposeWorkspace.saveErrorRouting.test.tsx).
 */
import * as React from 'react';
import { renderHook, act } from '@testing-library/react';

// NO `virtual: true` — see the "Sibling `@spaarke/*` resolution" note in jest.config.js.
const authenticatedFetchMock = jest.fn();
jest.mock('@spaarke/auth', () => {
  class ApiError extends Error {
    public readonly status: number;
    public readonly problemDetails: Record<string, unknown> | null;
    constructor(message: string, status: number, problemDetails: Record<string, unknown> | null = null) {
      super(message);
      this.name = 'ApiError';
      this.status = status;
      this.problemDetails = problemDetails;
    }
  }
  return {
    ApiError,
    authenticatedFetch: (...args: unknown[]) => authenticatedFetchMock(...args),
    buildBffApiUrl: (base: string, path: string) => `${base}/api${path}`,
  };
});

// eslint-disable-next-line import/first
import { useComposeCheckoutLifecycle } from './useComposeCheckoutLifecycle';
// eslint-disable-next-line import/first
import type { ComposeWorkspaceAction, ComposeWorkspaceState } from '../ComposeWorkspace.types';

const SPRK_DOC_ID = 'sprk-doc-42';
const BFF = 'https://bff.example.test';

const apiError = (message: string, status: number, problemDetails: Record<string, unknown> | null = null): Error => {
  const { ApiError } = jest.requireMock('@spaarke/auth') as {
    ApiError: new (m: string, s: number, p?: Record<string, unknown> | null) => Error;
  };
  return new ApiError(message, status, problemDetails);
};

/**
 * A state that will NOT trigger the hook's own probe effect (that fires on `status === 'loaded'` with
 * `checkoutStatus` idle/skipped). `checkoutStatus: 'acquired'` keeps the effect quiet so each test
 * drives exactly the callback it is about — no ambient requests to disentangle from the assertions.
 */
function quietState(): ComposeWorkspaceState {
  return {
    status: 'loaded',
    checkoutStatus: 'acquired',
    documentRef: { speDriveItemId: 'spe-1', sprkDocumentId: SPRK_DOC_ID, fileName: 'contract.docx' },
    sessionId: 'session-1',
  } as unknown as ComposeWorkspaceState;
}

function renderLifecycle(postForceClosed?: () => void) {
  const dispatched: ComposeWorkspaceAction[] = [];
  const { result } = renderHook(() =>
    useComposeCheckoutLifecycle({
      state: quietState(),
      dispatch: (action: ComposeWorkspaceAction) => {
        dispatched.push(action);
      },
      bffBaseUrl: BFF,
      postForceClosed,
    })
  );
  return { result, dispatched };
}

beforeEach(() => {
  authenticatedFetchMock.mockReset();
  jest.spyOn(console, 'info').mockImplementation(() => undefined);
});

afterEach(() => {
  jest.restoreAllMocks();
});

describe('useComposeCheckoutLifecycle — FR-S09 item 4: the checkout path routes on the THROWN status', () => {
  it('a cross-user 409 raises the conflict — with the lock holder NAMED, not a generic failure', async () => {
    authenticatedFetchMock.mockRejectedValue(
      apiError('HTTP 409', 409, {
        status: 409,
        title: 'Document Locked',
        checkedOutBy: { id: 'user-7', name: 'Dana Whitfield' },
        checkedOutAt: '2026-08-21T09:15:00.000Z',
      })
    );

    const { result, dispatched } = renderLifecycle();
    await act(async () => {
      await result.current.runCheckout(SPRK_DOC_ID);
    });

    const conflict = dispatched.find(a => a.kind === 'checkoutConflict');
    expect(conflict).toBeDefined();
    expect((conflict as { lockedBy: { name: string; checkedOutAt: string | null } }).lockedBy.name).toBe(
      'Dana Whitfield'
    );
    expect((conflict as { lockedBy: { checkedOutAt: string | null } }).lockedBy.checkedOutAt).toBe(
      '2026-08-21T09:15:00.000Z'
    );
    // The old behaviour: every non-2xx became one undifferentiated failure, and the conflict dialog
    // (with its force-close affordance) never opened.
    expect(dispatched.some(a => a.kind === 'checkoutFailed')).toBe(false);
  });

  it('a 409 with no parseable body still raises the conflict — the name is a courtesy, the lock is the fact', async () => {
    authenticatedFetchMock.mockRejectedValue(apiError('HTTP 409', 409, null));

    const { result, dispatched } = renderLifecycle();
    await act(async () => {
      await result.current.runCheckout(SPRK_DOC_ID);
    });

    const conflict = dispatched.find(a => a.kind === 'checkoutConflict');
    expect(conflict).toBeDefined();
    expect((conflict as { lockedBy: { name: string } }).lockedBy.name).toBe('Another user');
  });

  it('a 404 says the document is not recorded yet — not "HTTP 404"', async () => {
    authenticatedFetchMock.mockRejectedValue(apiError('HTTP 404', 404));

    const { result, dispatched } = renderLifecycle();
    await act(async () => {
      await result.current.runCheckout(SPRK_DOC_ID);
    });

    const failed = dispatched.find(a => a.kind === 'checkoutFailed') as { failureMessage: string } | undefined;
    expect(failed?.failureMessage).toMatch(/not yet recorded in Spaarke/i);
    // Editing is never blocked by a missing lock, and the copy must keep saying so.
    expect(failed?.failureMessage).toMatch(/after first save|continue editing/i);
  });

  it('force-close: a 400 "lock already released" is the SUCCESS path — it acquires, it does not report failure', async () => {
    // The exact race the dialog exists to resolve: the other session let go between our probe and our
    // discard. SharePoint answers 400 "nothing to discard", which means the lock is gone — which is
    // what the user asked for. The dead block made this throw, so the button reported failure every
    // time it worked, inside a dialog with no dismiss.
    const calls: string[] = [];
    authenticatedFetchMock.mockImplementation(async (url: string) => {
      calls.push(url);
      if (url.includes('/discard')) throw apiError('HTTP 400', 400);
      return { ok: true, status: 200, json: async () => ({}) } as unknown as Response;
    });

    const postForceClosed = jest.fn();
    const { result, dispatched } = renderLifecycle(postForceClosed);
    await act(async () => {
      await result.current.forceCloseAndAcquire();
    });

    expect(calls.some(u => u.includes('/discard'))).toBe(true);
    expect(calls.some(u => u.includes('/checkout'))).toBe(true);
    expect(postForceClosed).toHaveBeenCalledTimes(1);
    expect(dispatched.some(a => a.kind === 'checkoutAcquired')).toBe(true);
    expect(dispatched.some(a => a.kind === 'checkoutFailed')).toBe(false);
  });

  it('force-close: a 403 IS a failure — it says so, and does not go on to acquire', async () => {
    // NEGATIVE to the above: treating the 400 as success must not turn every discard failure into one.
    const calls: string[] = [];
    authenticatedFetchMock.mockImplementation(async (url: string) => {
      calls.push(url);
      if (url.includes('/discard')) throw apiError('HTTP 403', 403);
      return { ok: true, status: 200, json: async () => ({}) } as unknown as Response;
    });

    const postForceClosed = jest.fn();
    const { result, dispatched } = renderLifecycle(postForceClosed);
    await act(async () => {
      await result.current.forceCloseAndAcquire();
    });

    const failed = dispatched.find(a => a.kind === 'checkoutFailed') as { failureMessage: string } | undefined;
    expect(failed?.failureMessage).toMatch(/permission to release this lock/i);
    expect(calls.some(u => u.includes('/checkout'))).toBe(false);
    expect(postForceClosed).not.toHaveBeenCalled();
  });

  it('NEGATIVE: a healthy checkout still acquires, with no conflict and no failure', async () => {
    authenticatedFetchMock.mockResolvedValue({ ok: true, status: 200, json: async () => ({}) } as unknown as Response);

    const { result, dispatched } = renderLifecycle();
    await act(async () => {
      await result.current.runCheckout(SPRK_DOC_ID);
    });

    expect(dispatched.map(a => a.kind)).toEqual(['checkoutRequested', 'checkoutAcquired']);
  });
});
