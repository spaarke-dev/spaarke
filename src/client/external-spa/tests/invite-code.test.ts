// The invite code's carriage (unified-access-control-r2 task 185; design note 185-invite-code-design.md §4): captured
// from `#invite=` (§12 I-12; `?invite=` as a fallback) on landing, stripped from the URL at once (keeping `customer` and
// every other part), kept in sessionStorage across the MSAL
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
  // Two configured backends unless a test says otherwise (pass 1 K1 depends on how many the directory holds).
  vi.stubEnv('VITE_USE_DIRECTORY_SERVICE', '');
  vi.stubEnv('VITE_BACKENDS', JSON.stringify([CONTOSO, FABRIKAM]));
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
  vi.unstubAllEnvs();
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

// §12 I-12: the link carries the code in the fragment, so no server or proxy log on the way to the SPA sees it.
describe('captureInviteCode reads the fragment first', () => {
  it('stores #invite= and strips it, keeping the query string', async () => {
    const { invite } = await load();
    land(`/workspace?customer=contoso&tab=docs#invite=${CODE}`);

    invite.captureInviteCode();

    expect(window.location.pathname).toBe('/workspace');
    expect(window.location.search).toBe('?customer=contoso&tab=docs');
    expect(window.location.hash).toBe('');
    expect(window.location.href).not.toContain(CODE);
    expect(window.location.href).not.toContain('invite');
    expect(JSON.parse(sessionStorage.getItem(invite.INVITE_CODE_STORAGE_KEY)!)).toEqual({
      code: CODE,
      customerKey: 'contoso',
    });
  });

  it('keeps every other fragment part exactly as it was', async () => {
    const { invite } = await load();
    land(`/?customer=contoso#section&invite=${CODE}&x=a%20b`);

    invite.captureInviteCode();

    expect(window.location.search).toBe('?customer=contoso');
    expect(window.location.hash).toBe('#section&x=a%20b');
    expect(invite.inviteCodeFor({ plane: 'ciam', customerKey: 'contoso' })).toBe(CODE);
  });

  it('falls back to ?invite= when the fragment has none, keeping the fragment', async () => {
    const { invite } = await load();
    land(`/?customer=contoso&invite=${CODE}#section`);

    invite.captureInviteCode();

    expect(window.location.search).toBe('?customer=contoso');
    expect(window.location.hash).toBe('#section');
    expect(invite.inviteCodeFor({ plane: 'ciam', customerKey: 'contoso' })).toBe(CODE);
  });

  it('with both present the fragment wins, and both are stripped', async () => {
    const { invite } = await load();
    land(`/?customer=contoso&invite=${OTHER_CODE}#invite=${CODE}`);

    invite.captureInviteCode();

    expect(invite.inviteCodeFor({ plane: 'ciam', customerKey: 'contoso' })).toBe(CODE);
    expect(window.location.search).toBe('?customer=contoso');
    expect(window.location.hash).toBe('');
    expect(window.location.href).not.toContain(CODE);
    expect(window.location.href).not.toContain(OTHER_CODE);
  });

  it('a malformed fragment code is stripped and never stored', async () => {
    const { invite } = await load();
    land(`/?customer=contoso#invite=${CODE}x`);

    invite.captureInviteCode();

    expect(window.location.hash).toBe('');
    expect(sessionStorage.getItem(invite.INVITE_CODE_STORAGE_KEY)).toBeNull();
  });

  it("leaves MSAL's redirect response fragment untouched (no invite there: no rewrite at all)", async () => {
    const { invite } = await load();
    land('/?customer=contoso#code=auth-code&state=msal-state&invitee=x');
    const replaceState = vi.spyOn(window.history, 'replaceState');

    invite.captureInviteCode();

    expect(replaceState).not.toHaveBeenCalled();
    expect(window.location.hash).toBe('#code=auth-code&state=msal-state&invitee=x');
    expect(sessionStorage.getItem(invite.INVITE_CODE_STORAGE_KEY)).toBeNull();
    replaceState.mockRestore();
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

  // Pass 1 K1: a single-backend SPA (e.g. the `default` dev entry) is the only backend the link can mean.
  it('with exactly ONE configured backend, a different customer key still matches', async () => {
    vi.stubEnv('VITE_BACKENDS', JSON.stringify([{ ...CONTOSO, customerKey: 'default' }]));
    const { invite } = await load();
    land(`/?customer=acme#invite=${CODE}`);
    invite.captureInviteCode();

    expect(invite.inviteCodeFor({ plane: 'ciam', customerKey: 'default' })).toBe(CODE);
    expect(invite.inviteCodeFor({ plane: 'workforce', customerKey: 'default' })).toBeNull();
  });

  it('with several backends a different key never matches; a link with no key matches any', async () => {
    const { invite } = await load();
    land(`/?customer=acme#invite=${CODE}`);
    invite.captureInviteCode();
    expect(invite.inviteCodeFor({ plane: 'ciam', customerKey: 'contoso' })).toBeNull();

    land(`/#invite=${CODE}`);
    invite.captureInviteCode();
    expect(invite.inviteCodeFor({ plane: 'ciam', customerKey: 'contoso' })).toBe(CODE);
    expect(invite.inviteCodeFor({ plane: 'ciam', customerKey: 'fabrikam' })).toBe(CODE);
  });

  it('behind the T240c directory flag the single-backend match never applies', async () => {
    vi.stubEnv('VITE_BACKENDS', JSON.stringify([{ ...CONTOSO, customerKey: 'default' }]));
    vi.stubEnv('VITE_USE_DIRECTORY_SERVICE', 'true');
    const { invite } = await load();
    land(`/?customer=acme#invite=${CODE}`);
    invite.captureInviteCode();

    expect(invite.inviteCodeFor({ plane: 'ciam', customerKey: 'default' })).toBeNull();
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

  it('a single-backend SPA sends the code although the link named another customer key (pass 1 K1)', async () => {
    vi.stubEnv('VITE_BACKENDS', JSON.stringify([{ ...CONTOSO, customerKey: 'default' }]));
    const { invite, selection, bff } = await load();
    land(`/?customer=acme#invite=${CODE}`);
    invite.captureInviteCode();
    selection.activateBackend('ciam', { ...CONTOSO, customerKey: 'default' });

    await bff.bffApiCall('/api/v1/external/me/entitlements');

    expect(fetches.map(f => f.inviteHeader)).toEqual([CODE]);
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

  it('a 403 workforce_guest_use_home_signin yields the server detail (UAC #2128); a 401 or a blank detail yields null', async () => {
    vi.resetModules();
    const { deniedDetailOf, DENY_WORKFORCE_GUEST_USE_HOME_SIGNIN } =
      await import('../src/components/auth/BffAccessState');
    const { ApiError } = await import('../src/types');
    const guidance =
      "You're signed in as a guest of the Spaarke directory. Sign in with your own organization's work account instead: " +
      'open the portal in a browser and choose your work or school account.';

    expect(DENY_WORKFORCE_GUEST_USE_HOME_SIGNIN).toBe('sdap.access.deny.workforce_guest_use_home_signin');
    expect(
      deniedDetailOf(
        new ApiError(403, JSON.stringify({ reasonCode: DENY_WORKFORCE_GUEST_USE_HOME_SIGNIN, detail: ` ${guidance} ` }))
      )
    ).toBe(guidance);
    expect(
      deniedDetailOf(
        new ApiError(401, JSON.stringify({ reasonCode: DENY_WORKFORCE_GUEST_USE_HOME_SIGNIN, detail: guidance }))
      )
    ).toBeNull();
    expect(
      deniedDetailOf(
        new ApiError(403, JSON.stringify({ reasonCode: DENY_WORKFORCE_GUEST_USE_HOME_SIGNIN, detail: '  ' }))
      )
    ).toBeNull();
    expect(deniedDetailOf(new ApiError(403, JSON.stringify({ reasonCode: 42, detail: guidance })))).toBeNull();
  });
});
