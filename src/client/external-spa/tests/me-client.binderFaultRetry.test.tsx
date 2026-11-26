/**
 * R4 FR-28 (#2269): a binder fault answers 503 and the SPA retries it.
 *
 * The BFF answers a sign-in it could not finish (the contact could not be read, created or bound right now) with a 503
 * naming one of three codes. `fetchMeEntitlements` retries it three times (1 s, 2 s, 4 s); after that the workspace shows
 * "We couldn't finish signing you in. Try again" with a Retry that runs the whole sequence again. Never the plane's
 * default workspace: the caller has not been identified.
 *
 * Must-not-fire pairs: a plain 503 (no binder code) keeps today's plane-default fallback; a 403 is never retried.
 * The workforce gate's half (WorkforceHomeGate) is tested in tests/workforce-home-auth.test.ts.
 */
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const { bffApiCall } = vi.hoisted(() => ({ bffApiCall: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall };
});

vi.mock('../src/registry/widgetRegistry', async () => {
  const actual = await vi.importActual<typeof import('../src/registry/widgetRegistry')>(
    '../src/registry/widgetRegistry'
  );
  const Stub: React.FC<{ title: string }> = ({ title }) => <div data-testid="widget-body">{title}</div>;
  return { ...actual, getWidgetLazyComponent: () => Stub };
});

import {
  BINDER_FAULT_CODES,
  BINDER_FAULT_MESSAGE,
  BINDER_FAULT_RETRY_DELAYS_MS,
  BinderFaultError,
  fetchMeEntitlements,
  isBinderFault,
  withBinderFaultRetry,
  type MeEntitlementsResponse,
} from '../src/api/me-client';
import { WorkspaceHomePage } from '../src/pages/WorkspaceHomePage';
import { ApiError } from '../src/types';

const LOOKUP = 'sdap.access.deny.contact_lookup_failed';

/** The BFF's binder-fault ProblemDetails, as `bffApiCall` surfaces it (the raw body is the ApiError message). */
const binderFault = (code: string = LOOKUP, status = 503) =>
  new ApiError(
    status,
    JSON.stringify({ title: 'Service Unavailable', status, reasonCode: code, errorCode: code, detail: 'x' })
  );

const partner: MeEntitlementsResponse = { displayName: 'P', email: '', plane: 'ciam', entitlements: ['assigned-work'] };

/** Advance the fake clock through every wait, flushing the promise chain between them. */
async function runRetries() {
  for (const ms of BINDER_FAULT_RETRY_DELAYS_MS) await vi.advanceTimersByTimeAsync(ms);
}

beforeEach(() => {
  bffApiCall.mockReset();
  vi.spyOn(console, 'warn').mockImplementation(() => undefined);
});

afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe('isBinderFault', () => {
  it.each(BINDER_FAULT_CODES)('isBinderFault_503With%s_IsTrue', code => {
    expect(isBinderFault(binderFault(code))).toBe(true);
  });

  it('isBinderFault_ErrorCodeOnly_IsTrue', () => {
    expect(isBinderFault(new ApiError(503, JSON.stringify({ errorCode: LOOKUP })))).toBe(true);
  });

  it.each([
    ['a plain 503 with no body', new ApiError(503, 'down')],
    [
      'a 503 with another code',
      new ApiError(503, JSON.stringify({ reasonCode: 'sdap.access.registration_owner_unconfigured' })),
    ],
    ['a 403 with a binder code', binderFault(LOOKUP, 403)],
    ['a 500 with a binder code', binderFault(LOOKUP, 500)],
    ['a non-ApiError', new Error(JSON.stringify({ reasonCode: LOOKUP }))],
  ])('isBinderFault_%s_IsFalse', (_label, err) => {
    expect(isBinderFault(err)).toBe(false);
  });
});

describe('withBinderFaultRetry', () => {
  it('withBinderFaultRetry_FaultThenSuccess_RetriesAfterOneSecondAndResolves', async () => {
    const waits: number[] = [];
    const call = vi.fn().mockRejectedValueOnce(binderFault()).mockResolvedValueOnce('ok');
    await expect(withBinderFaultRetry(call, async ms => void waits.push(ms))).resolves.toBe('ok');
    expect(call).toHaveBeenCalledTimes(2);
    expect(waits).toEqual([1000]);
  });

  it('withBinderFaultRetry_FaultEveryTime_ThreeRetriesAt1s2s4sThenBinderFaultError', async () => {
    const waits: number[] = [];
    const call = vi.fn().mockRejectedValue(binderFault('sdap.access.deny.contact_bind_failed'));
    await expect(withBinderFaultRetry(call, async ms => void waits.push(ms))).rejects.toBeInstanceOf(BinderFaultError);
    expect(call).toHaveBeenCalledTimes(4); // the first call and three retries
    expect(waits).toEqual([1000, 2000, 4000]);
  });

  it.each([
    ['a 403', new ApiError(403, JSON.stringify({ reasonCode: 'sdap.access.deny.not_invited' }))],
    ['a plain 503', new ApiError(503, 'down')],
    ['a network fault', new TypeError('Failed to fetch')],
  ])('withBinderFaultRetry_%s_RejectsAtOnceWithTheOriginalError', async (_label, err) => {
    const wait = vi.fn(async () => undefined);
    const call = vi.fn().mockRejectedValue(err);
    await expect(withBinderFaultRetry(call, wait)).rejects.toBe(err);
    expect(call).toHaveBeenCalledTimes(1);
    expect(wait).not.toHaveBeenCalled();
  });
});

describe('fetchMeEntitlements', () => {
  it('fetchMeEntitlements_BinderFaultThenSuccess_ResolvesTheServerAnswer', async () => {
    vi.useFakeTimers();
    bffApiCall.mockRejectedValueOnce(binderFault()).mockResolvedValueOnce(partner);
    const result = fetchMeEntitlements(false);
    await vi.advanceTimersByTimeAsync(1000);
    await expect(result).resolves.toEqual(partner);
    expect(bffApiCall).toHaveBeenCalledTimes(2);
  });

  it('fetchMeEntitlements_BinderFaultEveryTime_RejectsWithBinderFaultError_NeverThePlaneDefaults', async () => {
    vi.useFakeTimers();
    bffApiCall.mockRejectedValue(binderFault('sdap.access.deny.contact_create_failed'));
    const result = fetchMeEntitlements(false).catch((e: unknown) => e);
    await runRetries();
    const err = await result;
    expect(err).toBeInstanceOf(BinderFaultError);
    expect((err as Error).message).toBe(BINDER_FAULT_MESSAGE);
    expect(bffApiCall).toHaveBeenCalledTimes(4);
    expect(console.warn).not.toHaveBeenCalled(); // the plane-default fallback did not fire
  });

  it('fetchMeEntitlements_PlainServerFailure_KeepsThePlaneDefaultFallback_NoRetry', async () => {
    bffApiCall.mockRejectedValue(new ApiError(503, 'down'));
    await expect(fetchMeEntitlements(false)).resolves.toMatchObject({ plane: 'ciam', entitlements: ['assigned-work'] });
    expect(bffApiCall).toHaveBeenCalledTimes(1);
  });

  it('fetchMeEntitlements_Forbidden_RejectsAtOnce_NoRetry', async () => {
    bffApiCall.mockRejectedValue(new ApiError(403, JSON.stringify({ reasonCode: 'sdap.access.deny.not_invited' })));
    await expect(fetchMeEntitlements(false)).rejects.toMatchObject({ statusCode: 403 });
    expect(bffApiCall).toHaveBeenCalledTimes(1);
  });
});

describe('WorkspaceHomePage', () => {
  const renderHome = () =>
    render(
      <FluentProvider theme={webLightTheme}>
        <WorkspaceHomePage />
      </FluentProvider>
    );

  it('WorkspaceHomePage_RetriesUsedUp_ShowsTheMessageAndRetry_RetryRunsTheSequenceAgain', async () => {
    vi.useFakeTimers();
    bffApiCall.mockRejectedValue(binderFault());
    renderHome();
    await act(runRetries);
    expect(screen.getByText(BINDER_FAULT_MESSAGE)).toBeTruthy();
    expect(screen.queryByRole('tablist')).toBeNull(); // never the default workspace
    expect(bffApiCall).toHaveBeenCalledTimes(4);

    // Retry starts the whole sequence again: the request, then its own retries.
    bffApiCall.mockReset();
    bffApiCall.mockRejectedValueOnce(binderFault()).mockResolvedValueOnce(partner);
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(screen.queryByText(BINDER_FAULT_MESSAGE)).toBeNull();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1000);
    });
    expect(bffApiCall).toHaveBeenCalledTimes(2);
    vi.useRealTimers();
    expect(await screen.findByRole('tablist')).toBeTruthy();
    expect(screen.queryByText(BINDER_FAULT_MESSAGE)).toBeNull();
  });

  it('WorkspaceHomePage_PlainServerFailure_ShowsTheDefaultWorkspace_NotTheBinderMessage', async () => {
    bffApiCall.mockRejectedValue(new ApiError(503, 'down'));
    renderHome();
    expect(await screen.findByRole('tablist')).toBeTruthy();
    expect(screen.queryByText(BINDER_FAULT_MESSAGE)).toBeNull();
  });

  it('WorkspaceHomePage_Forbidden_ShowsTheDeniedState_NotTheBinderMessage', async () => {
    bffApiCall.mockRejectedValue(new ApiError(403, JSON.stringify({ reasonCode: 'sdap.access.deny.not_invited' })));
    renderHome();
    expect(await screen.findByTestId('bff-access-denied')).toBeTruthy();
    expect(screen.queryByText(BINDER_FAULT_MESSAGE)).toBeNull();
    expect(bffApiCall).toHaveBeenCalledTimes(1);
  });
});
