// A grant-email deep link through the REAL sign-in gate (spaarke-SPA-external-access-platform-r3 task 055 review fix).
//
// The real `App` runs, including the real `AuthGuard` -> `MsalAccountGate` (the post-sign-in restore effect that reads
// `sessionStorage['spaarke.ext.returnTo']` and soft-navigates to it), the real `RecordDeepLink`, `RecordOpenerProvider`,
// `RecordDetailModal` and `NotificationBell`. Stubbed: MSAL's `useMsal` (a signed-in account, nothing in progress), the route
// pages, and the network seam (`bffApiCall`, which answers the record read and the bell's feed). `AuthGuard` is NOT stubbed.
//
// What this proves: after sign-in the link opens the record EXACTLY ONCE (one id-filtered record read, one detail view),
// whether the browser landed on `/` with the link only in the return-to key (the normal redirect return) or MSAL already put
// the link in the address bar (the gate then finds target === current), and the address bar ends without `record` once the
// record has loaded. The link is the one the grant email writes (`GrantAccessNotifier.cs`: `?customer={key}&record={type}:{id}`,
// each part `Uri.EscapeDataString`-ed, so the colon travels as `%3A`).
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';

vi.setConfig({ testTimeout: 60000 });

const RETURN_TO_KEY = 'spaarke.ext.returnTo';
const ID = '00000000-0000-4000-8000-000000000042';
const LINK = `/?customer=demo&record=matter%3A${ID}`;
const JANE = { homeAccountId: 'a', username: 'jane@partner.test', name: 'Jane Partner', tenantId: 'partner' };

const h = vi.hoisted(() => ({
  bffApiCall: vi.fn(),
  loginRedirect: vi.fn(),
  recordReads: [] as string[],
  feedReads: { count: 0 },
}));

vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({ accounts: [JANE], instance: { loginRedirect: h.loginRedirect }, inProgress: 'none' }),
}));
vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall: h.bffApiCall };
});
vi.mock('../src/pages/WorkspaceHomePage', () => ({ WorkspaceHomePage: () => null }));
vi.mock('../src/pages/ProjectPage', () => ({ ProjectPage: () => null }));
vi.mock('../src/pages/DocumentUploadPage', () => ({ DocumentUploadPage: () => null }));
vi.mock('../src/pages/SettingsPage', () => ({ SettingsPage: () => null }));

import { ApiError } from '../src/types';

beforeEach(() => {
  sessionStorage.clear();
  window.history.replaceState({}, '', '/');
  h.loginRedirect.mockReset();
  h.recordReads.length = 0;
  h.feedReads.count = 0;
  h.bffApiCall.mockReset();
  h.bffApiCall.mockImplementation(async (path: string, init?: RequestInit) => {
    if (String(path).endsWith('/dataverse/fetch')) {
      const body = JSON.parse(String(init?.body ?? '{}')) as { entityName?: string; fetchXml?: string };
      // The detail view's id-filtered read of the matter named by the link.
      if (body.entityName === 'sprk_matter' && body.fetchXml?.includes(ID)) {
        h.recordReads.push(ID);
        return { entities: [{ sprk_matterid: ID, sprk_mattername: 'Acme v. Widget' }] };
      }
      return { entities: [] };
    }
    if (path === '/api/v1/external/notifications') {
      h.feedReads.count += 1;
      return { value: [], unreadCount: 0, lastSeenAt: null };
    }
    throw new ApiError(500, 'not needed');
  });
});
afterEach(() => {
  window.history.replaceState({}, '', '/');
});

async function renderApp(): Promise<void> {
  const { App } = await import('../src/App');
  render(React.createElement(App, { teamsHost: false }));
}

describe('grant-email deep link through the real sign-in gate', () => {
  it('browser returns to `/`, the gate restores the captured link, and the record opens once', async () => {
    sessionStorage.setItem(RETURN_TO_KEY, LINK);
    await renderApp();

    // The restore navigated to the link, the deep link opened the matter, and its (single) read answered.
    expect(await screen.findByRole('dialog', undefined, { timeout: 30000 })).toBeTruthy();
    await waitFor(() => expect(h.recordReads).toHaveLength(1), { timeout: 30000 });
    // Terminal (loaded): the link is spent. The customer key stays.
    await waitFor(() => expect(window.location.search).toBe('?customer=demo'), { timeout: 30000 });
    expect(window.location.pathname).toBe('/');

    // Nothing opened it a second time.
    await new Promise(resolve => setTimeout(resolve, 300));
    expect(h.recordReads).toHaveLength(1);
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
    // The gate consumed its key and started no sign-in.
    expect(sessionStorage.getItem(RETURN_TO_KEY)).toBeNull();
    expect(h.loginRedirect).not.toHaveBeenCalled();
  });

  it('MSAL already landed on the link (target === current): still one open, link spent after the load', async () => {
    window.history.replaceState({}, '', LINK);
    sessionStorage.setItem(RETURN_TO_KEY, LINK);
    await renderApp();

    expect(await screen.findByRole('dialog', undefined, { timeout: 30000 })).toBeTruthy();
    await waitFor(() => expect(window.location.search).toBe('?customer=demo'), { timeout: 30000 });
    await new Promise(resolve => setTimeout(resolve, 300));

    expect(h.recordReads).toHaveLength(1);
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
    expect(sessionStorage.getItem(RETURN_TO_KEY)).toBeNull();
  });

  it("the link survives the bell's first request: the feed is read once and the URL still held the link at that moment", async () => {
    // A bell read that needed a redirect would capture `pathname + search` as it stands. Hold the record read so the link
    // is still in the address bar, and check the bell has read by then.
    let release: (rows: unknown[]) => void = () => undefined;
    const original = h.bffApiCall.getMockImplementation()!;
    h.bffApiCall.mockImplementation(async (path: string, init?: RequestInit) => {
      if (String(path).endsWith('/dataverse/fetch')) {
        const body = JSON.parse(String(init?.body ?? '{}')) as { entityName?: string; fetchXml?: string };
        if (body.entityName === 'sprk_matter' && body.fetchXml?.includes(ID)) {
          h.recordReads.push(ID);
          return new Promise(resolve => {
            release = rows => resolve({ entities: rows });
          });
        }
      }
      return original(path, init);
    });
    sessionStorage.setItem(RETURN_TO_KEY, LINK);
    await renderApp();

    await waitFor(() => expect(h.recordReads).toHaveLength(1), { timeout: 30000 });
    await waitFor(() => expect(h.feedReads.count).toBe(1), { timeout: 30000 });
    // Both the record read and the bell's first GET have happened, and the address bar still carries the link.
    expect(window.location.search).toContain('record=');

    release([{ sprk_matterid: ID, sprk_mattername: 'Acme v. Widget' }]);
    await waitFor(() => expect(window.location.search).toBe('?customer=demo'), { timeout: 30000 });
    expect(h.recordReads).toHaveLength(1);
  });
});
