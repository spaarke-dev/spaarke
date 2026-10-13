// Active account after an interactive sign-in (spaarke-SPA-external-access-platform-r3 task 042, re-check fix round).
//
// BffAccessState's "Sign in again" starts a login redirect with prompt=select_account so the user can pick a DIFFERENT
// identity after the BFF refused the cached one. That only works if (a) the account the redirect signs in becomes the
// instance's ACTIVE account and (b) token acquisition prefers the active account over the oldest cached one (it used
// to take accounts[0] always, so the refused session came straight back). The 040 tenant filter must still hold: an
// active account from another tenant is never used on the workforce plane.
//
// MSAL is a fake that records the account each silent request carries and the callbacks the code registers; the
// event names and interaction types are msal-browser's real constants.
import { beforeEach, describe, expect, it, vi } from 'vitest';

const SPAARKE_TENANT = 'a221a95e-6abc-4434-aecc-e48338a1b2f2';
const OTHER_TENANT = '7052feba-bfc4-43e0-b09e-65014b429131';
const BFF_SCOPE = 'api://99999999-8888-7777-6666-555555555555/user_impersonation';

vi.mock('@azure/msal-browser', async importOriginal => {
  const actual = await importOriginal<typeof import('@azure/msal-browser')>();
  class FakePublicClientApplication {}
  return { ...actual, PublicClientApplication: FakePublicClientApplication };
});
vi.mock('@microsoft/teams-js', () => ({
  app: { initialize: vi.fn() },
  nestedAppAuth: { isNAAChannelRecommended: vi.fn(() => true) },
}));

const acct = (id: string, tenantId = SPAARKE_TENANT) => ({
  homeAccountId: id,
  username: `${id}@test`,
  tenantId,
  environment: 'login.microsoftonline.com',
  localAccountId: id,
});

/** A fake instance: `cached` accounts (getAllAccounts honours the tenant filter), an optional active account. */
function fakeInstance(cached: any[], active: any = null) {
  let current = active;
  const callbacks: Array<(event: any) => void> = [];
  return {
    getAllAccounts: vi.fn((filter?: { tenantId?: string }) =>
      filter?.tenantId ? cached.filter(a => a.tenantId === filter.tenantId) : cached
    ),
    getActiveAccount: vi.fn(() => current),
    setActiveAccount: vi.fn((a: any) => {
      current = a;
    }),
    acquireTokenSilent: vi.fn(async (req: any) => ({ accessToken: `tok-${req.account.homeAccountId}` })),
    acquireTokenRedirect: vi.fn(async () => undefined),
    addEventCallback: vi.fn((cb: (event: any) => void) => {
      callbacks.push(cb);
      return 'cb-id';
    }),
    emit: (event: any) => callbacks.forEach(cb => cb(event)),
  } as any;
}

async function load() {
  vi.resetModules();
  const msalBrowser = await import('@azure/msal-browser');
  const msalAuth = await import('../src/auth/msal-auth');
  return { msalAuth, EventType: msalBrowser.EventType, InteractionType: msalBrowser.InteractionType };
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe('acquireStandaloneBffToken picks the active account', () => {
  it('two cached accounts, the newer one (B) active: the token is acquired for B, not the oldest (A)', async () => {
    const { msalAuth } = await load();
    const A = acct('A');
    const B = acct('B');
    const instance = fakeInstance([A, B], B);
    await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE)).resolves.toBe('tok-B');
    expect(instance.acquireTokenSilent.mock.calls[0][0].account).toBe(B);
  });

  it('workforce plane: an active account from another tenant is never used — the oldest Spaarke-tenant account is', async () => {
    const { msalAuth } = await load();
    const A = acct('A');
    const foreign = acct('B', OTHER_TENANT);
    const instance = fakeInstance([A, foreign], foreign);
    await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE, { tenantId: SPAARKE_TENANT })).resolves.toBe(
      'tok-A'
    );
    for (const call of instance.acquireTokenSilent.mock.calls) expect(call[0].account).toBe(A);
    expect(instance.acquireTokenSilent.mock.calls[0][0].account.tenantId).toBe(SPAARKE_TENANT);
  });

  it('workforce plane: an active account of the right tenant still wins over an older one', async () => {
    const { msalAuth } = await load();
    const A = acct('A');
    const B = acct('B');
    const instance = fakeInstance([A, acct('X', OTHER_TENANT), B], B);
    await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE, { tenantId: SPAARKE_TENANT })).resolves.toBe(
      'tok-B'
    );
  });

  it('no active account: the oldest cached account is used (unchanged behaviour)', async () => {
    const { msalAuth } = await load();
    const A = acct('A');
    const instance = fakeInstance([A, acct('B')], null);
    await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE)).resolves.toBe('tok-A');
  });

  it('an active account that is no longer cached is ignored: the oldest cached account is used', async () => {
    const { msalAuth } = await load();
    const A = acct('A');
    const instance = fakeInstance([A, acct('B')], acct('GONE'));
    await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE)).resolves.toBe('tok-A');
  });
});

describe('syncActiveAccountOnInteractiveSignIn', () => {
  it.each([
    ['LOGIN_SUCCESS', 'Redirect'],
    ['ACQUIRE_TOKEN_SUCCESS', 'Redirect'],
    ['LOGIN_SUCCESS', 'Popup'],
    ['ACQUIRE_TOKEN_SUCCESS', 'Popup'],
  ] as const)('%s from a %s makes the signed-in account the active account', async (eventName, interaction) => {
    const { msalAuth, EventType, InteractionType } = await load();
    const A = acct('A');
    const B = acct('B');
    const instance = fakeInstance([A, B], A);
    msalAuth.syncActiveAccountOnInteractiveSignIn(instance);
    expect(instance.addEventCallback).toHaveBeenCalledTimes(1);

    instance.emit({
      eventType: EventType[eventName],
      interactionType: InteractionType[interaction],
      payload: { account: B, accessToken: 'x' },
      error: null,
      timestamp: 1,
    });

    expect(instance.setActiveAccount).toHaveBeenCalledWith(B);
    expect(instance.getActiveAccount()).toBe(B);
  });

  it('end to end: after the redirect login of B, the next acquisition is for B', async () => {
    const { msalAuth, EventType, InteractionType } = await load();
    const A = acct('A');
    const B = acct('B');
    const instance = fakeInstance([A, B], A);
    msalAuth.syncActiveAccountOnInteractiveSignIn(instance);
    instance.emit({
      eventType: EventType.LOGIN_SUCCESS,
      interactionType: InteractionType.Redirect,
      payload: { account: B },
      error: null,
      timestamp: 1,
    });
    await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE)).resolves.toBe('tok-B');
  });

  it('ignores a silent renewal, other events, and a success without an account', async () => {
    const { msalAuth, EventType, InteractionType } = await load();
    const A = acct('A');
    const instance = fakeInstance([A, acct('B')], A);
    msalAuth.syncActiveAccountOnInteractiveSignIn(instance);
    const base = { error: null, timestamp: 1 };
    instance.emit({
      ...base,
      eventType: EventType.ACQUIRE_TOKEN_SUCCESS,
      interactionType: InteractionType.Silent,
      payload: { account: acct('B') },
    });
    instance.emit({
      ...base,
      eventType: EventType.LOGIN_START,
      interactionType: InteractionType.Redirect,
      payload: { account: acct('B') },
    });
    instance.emit({
      ...base,
      eventType: EventType.LOGIN_SUCCESS,
      interactionType: InteractionType.Redirect,
      payload: null,
    });
    expect(instance.setActiveAccount).not.toHaveBeenCalled();
  });

  it('registers once per instance, however often it is offered', async () => {
    const { msalAuth } = await load();
    const instance = fakeInstance([acct('A')]);
    msalAuth.syncActiveAccountOnInteractiveSignIn(instance);
    msalAuth.syncActiveAccountOnInteractiveSignIn(instance);
    expect(instance.addEventCallback).toHaveBeenCalledTimes(1);
  });
});
