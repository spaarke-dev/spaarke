// The invite code's carriage (unified-access-control-r2 task 185; design note 185-invite-code-design.md §4): captured
// from `?invite=` on landing, stripped from the URL at once (keeping `customer`), kept in sessionStorage across the MSAL
// redirect, sent as X-Spaarke-Invite-Code on CIAM-plane BFF calls for the link's customer, and cleared after the first
// 2xx. The denied state shows the server's `contact_not_invited` guidance (owner Q9).
//
// MSAL is out of the picture: the token seam (`acquireActiveBffToken`) is a fake and fetch is a recording stub.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('@azure/msal-browser', async importOriginal => {
  const actual = await importOriginal<typeof import('@azure/msal-browser')>();
  class FakePublicClientApplication {}
  return { ...actual, PublicClientApplication: FakePublicClientApplication };
});
vi.mock('../src/auth/msal-auth', async importOriginal => ({
  ...(await importOriginal<typeof import('../src/auth/msal-auth')>()),
  acquireActiveBffToken: vi.fn(async () => 'token'),
}));

// The first dynamic import of the component tree (BffAccessState) is slow on a cold transform cache.
vi.setConfig({ testTimeout: 60000 });

const CODE = 'AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE'; // 43 base64url characters
const OTHER_CODE = 'ZyXwVuTsRqPoNmLkJiHgFeDcBa9876543210_-ZyXwV';
const CONTOSO = {
  customerKey: 'contoso',
  displayName: 'Contoso',
  bffBaseUrl: 'https://bff-contoso.test.invalid',
  bffAppId: 'aaaaaaaa-0000-0000-0000-00000000000a',
};
const FABRIKAM = { ...CONTOSO, customerKey: 'fabrikam', bffBaseUrl: 'https://bff-fabrikam.test.invalid' };

interface Recorded {
  url: string;
  inviteHeader: string | undefined;
}

const fetches: Recorded[] = [];
let statuses: number[] = [];

function land(url: string) {
  window.history.replaceState(null, '', url);
}

async function load() {
  vi.resetModules();
  const invite = await import('../src/auth/invite-code');
  const selection = await import('../src/auth/backend-selection');
  const bff = await import('../src/auth/bff-client');
  return { invite, selection, bff };
}

beforeEach(() => {
  fetches.length = 0;
  statuses = [];
  sessionStorage.clear();
  localStorage.clear();
  land('/');
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      const headers = (init?.headers ?? {}) as Record<string, string>;
      fetches.push({ url, inviteHeader: headers['X-Spaarke-Invite-Code'] });
      const status = statuses.shift() ?? 200;
      return new Response(status === 204 ? null : JSON.stringify({ ok: status < 300 }), { status });
    })
  );
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('captureInviteCode', () => {
  it('stores the code for this tab and strips it from the URL, keeping customer, other parameters and the hash', async () => {
    const { invite } = await load();
    land(`/workspace?customer=contoso&invite=${CODE}&tab=docs#section`);

    invite.captureInviteCode();

    expect(window.location.pathname).toBe('/workspace');
    expect(window.location.search).toBe('?customer=contoso&tab=docs');
    expect(window.location.hash).toBe('#section');
    expect(window.location.href).not.toContain(CODE);
    expect(JSON.parse(sessionStorage.getItem(invite.INVITE_CODE_STORAGE_KEY)!)).toEqual({
      code: CODE,
      customerKey: 'contoso',
    });
    expect(localStorage.length).toBe(0);
  });

  it('a URL whose only parameter is invite is left with no query string', async () => {
    const { invite } = await load();
    land(`/?invite=${CODE}`);

    invite.captureInviteCode();

    expect(window.location.search).toBe('');
    expect(invite.inviteCodeFor({ plane: 'ciam', customerKey: 'any' })).toBe(CODE);
  });

  it('no invite parameter is a no-op: the URL and the stored entry are untouched', async () => {
    const { invite } = await load();
    land('/?customer=contoso');
    const replaceState = vi.spyOn(window.history, 'replaceState');

    invite.captureInviteCode();

    expect(replaceState).not.toHaveBeenCalled();
    expect(window.location.search).toBe('?customer=contoso');
    expect(sessionStorage.getItem(invite.INVITE_CODE_STORAGE_KEY)).toBeNull();
    replaceState.mockRestore();
  });

  it('a malformed code is stripped from the URL and never stored', async () => {
    const { invite } = await load();
    land(`/?customer=contoso&invite=${CODE}x`);

    invite.captureInviteCode();

    expect(window.location.search).toBe('?customer=contoso');
    expect(sessionStorage.getItem(invite.INVITE_CODE_STORAGE_KEY)).toBeNull();
  });

  it('the stored code survives a simulated MSAL redirect (a fresh page load in the same tab)', async () => {
    const first = await load();
    land(`/?customer=contoso&invite=${CODE}`);
    first.invite.captureInviteCode();

    // The redirect: the page unloads, the identity provider sends the tab back to the redirect URI with its response,
    // and every module is evaluated afresh. Only sessionStorage carries over.
    land('/#code=auth-code&state=msal-state');
    const second = await load();
    second.invite.captureInviteCode(); // the next main() — no invite parameter now

    expect(second.invite.inviteCodeFor({ plane: 'ciam', customerKey: 'contoso' })).toBe(CODE);
    expect(window.location.href).not.toContain(CODE);
  });
});

describe('inviteCodeFor', () => {
  it('is sent only on the CIAM plane and only to the customer the link named', async () => {
    const { invite } = await load();
    land(`/?customer=contoso&invite=${CODE}`);
    invite.captureInviteCode();

    expect(invite.inviteCodeFor({ plane: 'ciam', customerKey: 'contoso' })).toBe(CODE);
    expect(invite.inviteCodeFor({ plane: 'ciam', customerKey: 'fabrikam' })).toBeNull();
    expect(invite.inviteCodeFor({ plane: 'workforce', customerKey: 'contoso' })).toBeNull();
  });

  it('a tampered stored entry is ignored', async () => {
    const { invite } = await load();
    sessionStorage.setItem(invite.INVITE_CODE_STORAGE_KEY, JSON.stringify({ code: 'short', customerKey: null }));
    expect(invite.inviteCodeFor({ plane: 'ciam', customerKey: 'contoso' })).toBeNull();
    sessionStorage.setItem(invite.INVITE_CODE_STORAGE_KEY, 'not json');
    expect(invite.inviteCodeFor({ plane: 'ciam', customerKey: 'contoso' })).toBeNull();
  });
});

describe('bff-client carries the code', () => {
  it('sends X-Spaarke-Invite-Code until the first 2xx, then clears it', async () => {
    const { invite, selection, bff } = await load();
    land(`/?customer=contoso&invite=${CODE}`);
    invite.captureInviteCode();
    selection.activateBackend('ciam', { ...CONTOSO });

    await bff.bffApiCall('/api/v1/external/me/entitlements');
    await bff.bffApiCall('/api/v1/external/me/entitlements');

    expect(fetches.map(f => f.inviteHeader)).toEqual([CODE, undefined]);
    expect(sessionStorage.getItem(invite.INVITE_CODE_STORAGE_KEY)).toBeNull();
  });

  it('keeps the code on a 403, so "Sign in again" in the same tab still presents it', async () => {
    const { invite, selection, bff } = await load();
    land(`/?customer=contoso&invite=${CODE}`);
    invite.captureInviteCode();
    selection.activateBackend('ciam', { ...CONTOSO });
    statuses = [403, 200];

    await expect(bff.bffApiCall('/api/v1/external/me/entitlements')).rejects.toMatchObject({ statusCode: 403 });
    await bff.bffApiCall('/api/v1/external/me/entitlements');
    await bff.bffApiCall('/api/v1/external/me/entitlements');

    expect(fetches.map(f => f.inviteHeader)).toEqual([CODE, CODE, undefined]);
  });

  it('the 401 retry carries the code too', async () => {
    const { invite, selection, bff } = await load();
    land(`/?invite=${CODE}`);
    invite.captureInviteCode();
    selection.activateBackend('ciam', { ...CONTOSO });
    statuses = [401, 200];

    await bff.bffApiCall('/api/v1/external/me/entitlements');

    expect(fetches.map(f => f.inviteHeader)).toEqual([CODE, CODE]);
    expect(sessionStorage.getItem(invite.INVITE_CODE_STORAGE_KEY)).toBeNull();
  });

  it('is never sent to another customer, nor on the workforce plane, and neither clears it', async () => {
    const { invite, selection, bff } = await load();
    land(`/?customer=contoso&invite=${CODE}`);
    invite.captureInviteCode();

    selection.activateBackend('ciam', { ...FABRIKAM });
    await bff.bffApiCall('/api/v1/external/me/entitlements');
    selection.activateBackend('workforce', { ...CONTOSO });
    await bff.bffApiCall('/api/v1/external/me/entitlements');

    expect(fetches.map(f => f.inviteHeader)).toEqual([undefined, undefined]);
    expect(invite.inviteCodeFor({ plane: 'ciam', customerKey: 'contoso' })).toBe(CODE);
  });

  it('no stored code → no header', async () => {
    const { selection, bff } = await load();
    selection.activateBackend('ciam', { ...CONTOSO });

    await bff.bffApiCall('/api/v1/external/me/entitlements');

    expect(fetches).toHaveLength(1);
    expect(fetches[0].inviteHeader).toBeUndefined();
  });

  it('clearInviteCode forgets it (sign-out, "Choose a different sign-in")', async () => {
    const { invite } = await load();
    land(`/?invite=${OTHER_CODE}`);
    invite.captureInviteCode();

    invite.clearInviteCode();

    expect(invite.inviteCodeFor({ plane: 'ciam', customerKey: 'contoso' })).toBeNull();
  });
});

describe('the denied state shows the server guidance (owner Q9)', () => {
  it('a 403 contact_not_invited yields the server detail; anything else yields null', async () => {
    vi.resetModules();
    const { deniedDetailOf, DENY_CONTACT_NOT_INVITED } = await import('../src/components/auth/BffAccessState');
    const { ApiError } = await import('../src/types');
    const guidance = 'Open the invitation link from your email in this browser and sign in again.';

    expect(
      deniedDetailOf(new ApiError(403, JSON.stringify({ reasonCode: DENY_CONTACT_NOT_INVITED, detail: guidance })))
    ).toBe(guidance);
    expect(
      deniedDetailOf(
        new ApiError(403, JSON.stringify({ reasonCode: 'sdap.access.deny.contact_inactive', detail: 'x' }))
      )
    ).toBeNull();
    expect(
      deniedDetailOf(new ApiError(401, JSON.stringify({ reasonCode: DENY_CONTACT_NOT_INVITED, detail: guidance })))
    ).toBeNull();
    expect(deniedDetailOf(new ApiError(403, 'not json'))).toBeNull();
    expect(deniedDetailOf(new Error('x'))).toBeNull();
  });
});
