/**
 * R3 task 055 review fix - the bell's timer poll never starts an interactive sign-in.
 *
 * The REAL chain runs: `NotificationBell` -> `useNotificationFeed` (the timer) -> `notifications-client` -> `bffApiCall` ->
 * `acquireActiveBffToken` -> the standalone-plane seam -> `acquireStandaloneBffToken`. Only MSAL itself (a fake instance that
 * records redirects) and the network (`fetch`) are substituted. When the refresh token has lapsed the silent request fails
 * with InteractionRequiredAuthError; the old behaviour was `captureReturnTo()` + `acquireTokenRedirect`, i.e. a TIMER
 * navigating the page to Entra with no user action. Deleting the `noInteraction` check in `acquireStandaloneBffToken` (or not
 * passing it from the poll) makes the first test fail.
 */
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

vi.setConfig({ testTimeout: 60000 });

vi.mock('@azure/msal-browser', async importOriginal => {
  const actual = await importOriginal<typeof import('@azure/msal-browser')>();
  class FakePublicClientApplication {}
  return { ...actual, PublicClientApplication: FakePublicClientApplication };
});
vi.mock('@microsoft/teams-js', () => ({
  app: { initialize: vi.fn() },
  nestedAppAuth: { isNAAChannelRecommended: vi.fn(() => true) },
}));
// The detail view is not under test here.
vi.mock('../src/components/detail/RecordDetailModal', () => ({ RecordDetailModal: () => null }));

const RETURN_TO_KEY = 'spaarke.ext.returnTo';
const BFF_APP_ID = 'aaaaaaaa-0000-0000-0000-00000000000a';
const POLL_MS = 60_000;
const account = {
  homeAccountId: 'A',
  username: 'a@test',
  tenantId: 't',
  environment: 'login.microsoftonline.com',
  localAccountId: 'A',
};

let instance: any;
let fetchMock: ReturnType<typeof vi.fn>;

async function mountBell() {
  vi.resetModules();
  const msal = await import('@azure/msal-browser');
  const backendSelection = await import('../src/auth/backend-selection');
  const plane = await import('../src/auth/standalone-plane');
  const { NotificationBell } = await import('../src/components/shell/NotificationBell');
  const { RecordOpenerProvider } = await import('../src/components/detail/RecordOpener');

  const active = backendSelection.activateBackend('ciam', {
    customerKey: 'acme',
    displayName: 'Acme',
    bffBaseUrl: 'https://bff-acme.example.test',
    bffAppId: BFF_APP_ID,
  });
  instance = {
    getAllAccounts: vi.fn(() => [account]),
    getActiveAccount: vi.fn(() => null),
    setActiveAccount: vi.fn(),
    // The refresh token has lapsed: nothing can be had silently.
    acquireTokenSilent: vi.fn(async () => {
      throw new msal.InteractionRequiredAuthError('interaction_required');
    }),
    acquireTokenRedirect: vi.fn(async () => undefined),
    addEventCallback: vi.fn(() => 'cb'),
  };
  plane.applyStandalonePlane({ realm: 'ciam', instance, bffScope: active.bffScope } as any);

  render(
    <FluentProvider theme={webLightTheme}>
      <RecordOpenerProvider>
        <NotificationBell />
      </RecordOpenerProvider>
    </FluentProvider>
  );
}

const settle = (ms = 0): Promise<void> =>
  act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });

beforeEach(() => {
  sessionStorage.clear();
  window.history.replaceState({}, '', '/?customer=acme&record=matter:00000000-0000-4000-8000-000000000042');
  fetchMock = vi.fn(async () => new Response('{}', { status: 500 }));
  vi.stubGlobal('fetch', fetchMock);
  vi.useFakeTimers();
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  window.history.replaceState({}, '', '/');
});

describe('NotificationBell - the poll never signs in', () => {
  it('a lapsed refresh token on a poll: no redirect, no return-to capture, no request; silent retries back off', async () => {
    await mountBell();
    await settle(0);

    // The poll tried the silent path once ...
    expect(instance.acquireTokenSilent).toHaveBeenCalledTimes(1);
    // ... and a TIMER did not navigate the page away.
    expect(instance.acquireTokenRedirect).not.toHaveBeenCalled();
    expect(sessionStorage.getItem(RETURN_TO_KEY)).toBeNull();
    expect(fetchMock).not.toHaveBeenCalled();

    // A silent failure may be a transient renewal timeout, so the poll keeps trying SILENTLY on the back-off schedule
    // (2, 4, 8, then 10-minute gaps): half an hour later it has retried a bounded number of times and never redirected,
    // captured a return-to or fetched.
    await settle(30 * POLL_MS);
    const calls = instance.acquireTokenSilent.mock.calls.length;
    expect(calls).toBeGreaterThan(1);
    expect(calls).toBeLessThanOrEqual(6);
    expect(instance.acquireTokenRedirect).not.toHaveBeenCalled();
    expect(sessionStorage.getItem(RETURN_TO_KEY)).toBeNull();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('a transient silent failure on a poll recovers: the next silent success reads the feed and clears the sign-in state', async () => {
    await mountBell();
    // First poll fails silently (as above) ...
    await settle(0);
    expect(fetchMock).not.toHaveBeenCalled();
    // ... from then on the silent path succeeds.
    instance.acquireTokenSilent.mockImplementation(async () => ({ accessToken: 'tok', account }));
    fetchMock.mockImplementation(
      async () =>
        new Response(JSON.stringify({ value: [], unreadCount: 0, lastSeenAt: null }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
    );

    await settle(2 * POLL_MS);
    expect(fetchMock).toHaveBeenCalled();
    expect(instance.acquireTokenRedirect).not.toHaveBeenCalled();

    fireEvent.click(screen.getByTestId('notification-bell'));
    await settle(0);
    expect(screen.queryByTestId('notifications-error')).toBeNull();
  });

  it('a user gesture (opening the bell) is allowed to sign in: the click path still redirects, carrying the URL', async () => {
    await mountBell();
    await settle(0);
    expect(instance.acquireTokenRedirect).not.toHaveBeenCalled();

    fireEvent.click(screen.getByTestId('notification-bell'));
    await settle(0);

    expect(instance.acquireTokenRedirect).toHaveBeenCalledTimes(1);
    expect(sessionStorage.getItem(RETURN_TO_KEY)).toBe(
      '/?customer=acme&record=matter:00000000-0000-4000-8000-000000000042'
    );
  });
});
