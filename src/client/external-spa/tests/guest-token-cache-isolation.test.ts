// Regression: the guest-token cleanup must not touch the home plane's cache (spaarke-SPA-external-access-platform-r3
// task 058, review fix round 1, finding F-A).
//
// `acquireSpaarkeGuestToken` (msal-auth.ts) uses a memory-only A8 instance and clears it after every call. In
// @azure/msal-browser 5.5.0 the no-account `clearCache()` runs `BrowserCacheManager.clear()`, which removes EVERY
// sessionStorage key containing "msal" - the home (multitenant) plane's account and tokens included, whatever the
// guest instance's `cacheLocation`. This file uses the REAL `@azure/msal-browser` (no module mock, unlike
// workforce-home-auth.test.ts, whose fake instances each own a private account array and so cannot see a shared-storage
// wipe). Only the network/iframe/popup boundary is stubbed: `ssoSilent` / `acquireTokenPopup` on the prototype, which
// write their result through the real `hydrateCache` into the instance's own cache, exactly where a real response lands.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  BrowserAuthErrorCodes,
  PublicClientApplication,
  type AccountInfo,
  type AuthenticationResult,
} from '@azure/msal-browser';

vi.setConfig({ testTimeout: 60000 });

const SPAARKE_TENANT = 'a221a95e-6abc-4434-aecc-e48338a1b2f2';
const A8_CLIENT_ID = '11111111-2222-3333-4444-555555555555';
const HOME_CLIENT_ID = '66666666-7777-8888-9999-000000000001';
const HOME_TID = 'c0c0c0c0-0000-4000-8000-000000000001';
const SCOPE = 'api://aaaaaaaa-0000-0000-0000-00000000000a/user_impersonation';
const APP_KEY = 'spaarke.ext.realm';
const GUEST_TOKEN = 'guest-access-token-must-not-linger';

function b64url(value: unknown): string {
  return btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function jwt(claims: Record<string, unknown>): string {
  return `${b64url({ alg: 'none', typ: 'JWT' })}.${b64url(claims)}.sig`;
}

function resultFor(clientId: string, tid: string, oid: string, accessToken: string): AuthenticationResult {
  const claims = { aud: clientId, tid, oid, sub: oid, preferred_username: 'jane@contoso.test', name: 'Jane Doe' };
  const account: AccountInfo = {
    homeAccountId: `${oid}.${tid}`,
    environment: 'login.windows.net',
    tenantId: tid,
    username: 'jane@contoso.test',
    localAccountId: oid,
    name: 'Jane Doe',
    idTokenClaims: claims,
  };
  return {
    authority: `https://login.microsoftonline.com/${tid}`,
    uniqueId: oid,
    tenantId: tid,
    scopes: clientId === HOME_CLIENT_ID ? ['openid'] : [SCOPE],
    account,
    idToken: jwt(claims),
    idTokenClaims: claims,
    accessToken,
    fromCache: false,
    expiresOn: new Date(Date.now() + 3600_000),
    tokenType: 'Bearer',
    correlationId: 'test-correlation',
  };
}

const env = {
  VITE_TEAMS_MSAL_CLIENT_ID: A8_CLIENT_ID,
  VITE_TEAMS_MSAL_TENANT_ID: SPAARKE_TENANT,
  VITE_WORKFORCE_HOME_MSAL_CLIENT_ID: HOME_CLIENT_ID,
};

// Every guest (A8) instance the stubbed boundary was reached on, in order; the last is the one under test.
const guests: PublicClientApplication[] = [];
const guestInstance = (): PublicClientApplication | null => guests.at(-1) ?? null;
let clearCacheCalls: unknown[][] = [];

/** What the stubbed network boundary does for one guest call. */
type Boundary = 'succeed' | 'fail' | 'cancel' | 'succeed-then-throw';

function stubBoundary(method: 'ssoSilent' | 'acquireTokenPopup', boundary: Boundary): void {
  vi.spyOn(PublicClientApplication.prototype, method).mockImplementation(async function (
    this: PublicClientApplication,
    request: unknown
  ) {
    if (this.getConfiguration().auth.clientId !== A8_CLIENT_ID)
      throw new Error('boundary reached by a non-guest client');
    guests.push(this);
    if (boundary === 'fail') throw Object.assign(new Error('login_required'), { errorCode: 'login_required' });
    if (boundary === 'cancel') {
      throw Object.assign(new Error('user_cancelled'), { errorCode: BrowserAuthErrorCodes.userCancelled });
    }
    const result = resultFor(A8_CLIENT_ID, SPAARKE_TENANT, 'guest-oid', GUEST_TOKEN);
    await this.hydrateCache(result, request as never);
    if (boundary === 'succeed-then-throw') throw new Error('network dropped after the cache write');
    return result;
  });
}

/** The home plane exactly as the app creates it: a real sessionStorage-backed instance with a signed-in account. */
async function seedHomePlane(): Promise<Record<string, string>> {
  const home = new PublicClientApplication({
    auth: { clientId: HOME_CLIENT_ID, authority: 'https://login.microsoftonline.com/organizations' },
    cache: { cacheLocation: 'sessionStorage' },
  });
  await home.initialize();
  const result = resultFor(HOME_CLIENT_ID, HOME_TID, 'home-oid', 'home-access-token');
  await home.hydrateCache(result, { scopes: ['openid'] } as never);
  home.setActiveAccount(result.account);
  sessionStorage.setItem(APP_KEY, 'workforce');
  return sessionSnapshot();
}

function sessionSnapshot(): Record<string, string> {
  const snapshot: Record<string, string> = {};
  for (let i = 0; i < sessionStorage.length; i += 1) {
    const key = sessionStorage.key(i)!;
    snapshot[key] = sessionStorage.getItem(key)!;
  }
  return snapshot;
}

/** The guest instance's own cache, read through the library's internals (no public token listing exists in 5.5.0). */
function guestTokenKeys(): { idToken: string[]; accessToken: string[]; refreshToken: string[] } {
  const keys = (
    guestInstance() as unknown as { controller?: { browserStorage?: { getTokenKeys(): never } } }
  ).controller?.browserStorage?.getTokenKeys();
  expect(keys, 'msal-browser internals moved: update guestTokenKeys()').toBeDefined();
  return keys as never;
}

describe('acquireSpaarkeGuestToken leaves the home plane cache alone (real msal-browser)', () => {
  beforeEach(() => {
    sessionStorage.clear();
    localStorage.clear();
    guests.length = 0;
    clearCacheCalls = [];
    for (const [key, value] of Object.entries(env)) vi.stubEnv(key, value);
    // Cross-tab messaging boundary: Node's BroadcastChannel delivers a MessageEvent that jsdom's EventTarget rejects, and
    // nothing here has a second tab. msal-browser only uses it to tell other tabs about account changes.
    vi.stubGlobal(
      'BroadcastChannel',
      class {
        onmessage = null;
        postMessage(): void {}
        close(): void {}
        addEventListener(): void {}
        removeEventListener(): void {}
      }
    );
    const original = PublicClientApplication.prototype.clearCache;
    vi.spyOn(PublicClientApplication.prototype, 'clearCache').mockImplementation(function (
      this: PublicClientApplication,
      ...args: unknown[]
    ) {
      clearCacheCalls.push(args);
      return original.apply(this, args as never);
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllEnvs();
    vi.unstubAllGlobals();
  });

  const cases: Array<{
    name: string;
    mode: 'silent' | 'popup';
    boundary: Boundary;
    outcome: 'token' | 'null' | 'cancelled' | 'failed';
  }> = [
    { name: 'silent success', mode: 'silent', boundary: 'succeed', outcome: 'token' },
    { name: 'silent failure', mode: 'silent', boundary: 'fail', outcome: 'null' },
    { name: 'popup success', mode: 'popup', boundary: 'succeed', outcome: 'token' },
    { name: 'popup cancelled', mode: 'popup', boundary: 'cancel', outcome: 'cancelled' },
    { name: 'popup throws after the cache write', mode: 'popup', boundary: 'succeed-then-throw', outcome: 'failed' },
  ];

  it.each(cases)('$name: home keys survive and the guest instance holds nothing', async c => {
    const homeKeys = await seedHomePlane();
    expect(Object.keys(homeKeys).length).toBeGreaterThan(2); // account keys, token keys, account + credentials, app key
    expect(Object.keys(homeKeys).some(k => k.startsWith('msal.'))).toBe(true);

    stubBoundary(c.mode === 'silent' ? 'ssoSilent' : 'acquireTokenPopup', c.boundary);
    const { acquireSpaarkeGuestToken } = await import('../src/auth/msal-auth');

    const run = acquireSpaarkeGuestToken(c.mode, 'jane@contoso.test', SCOPE);
    if (c.outcome === 'token') await expect(run).resolves.toBe(GUEST_TOKEN);
    else if (c.outcome === 'null') await expect(run).resolves.toBeNull();
    else await expect(run).rejects.toMatchObject({ reason: c.outcome });

    expect(guestInstance(), 'the stubbed boundary was reached').not.toBeNull();

    // 1. Every home-plane key is still there, byte for byte.
    const after = sessionSnapshot();
    for (const [key, value] of Object.entries(homeKeys)) {
      expect(after[key], `home key ${key} survived`).toBe(value);
    }

    // 2. The home plane still reads its account back through a fresh real instance.
    const reread = new PublicClientApplication({
      auth: { clientId: HOME_CLIENT_ID, authority: 'https://login.microsoftonline.com/organizations' },
      cache: { cacheLocation: 'sessionStorage' },
    });
    await reread.initialize();
    expect(reread.getAllAccounts().map(a => a.homeAccountId)).toEqual([`home-oid.${HOME_TID}`]);

    // 3. The guest instance holds no account and no token.
    expect(guestInstance()!.getAllAccounts()).toEqual([]);
    const tokenKeys = guestTokenKeys();
    expect(tokenKeys.idToken).toEqual([]);
    expect(tokenKeys.accessToken).toEqual([]);
    expect(tokenKeys.refreshToken).toEqual([]);

    // 4. The guest token reached no persistent storage.
    const everything = JSON.stringify({ ...sessionSnapshot(), ...Object.fromEntries(Object.entries(localStorage)) });
    expect(everything).not.toContain(GUEST_TOKEN);
    expect(everything).not.toContain('guest-oid');
    expect(Object.keys(localStorage).filter(k => k.includes('msal'))).toEqual([]);

    // 5. The cleanup never used the no-account form, which clears every "msal" key in sessionStorage.
    for (const args of clearCacheCalls) {
      expect(args[0], 'clearCache() must always name an account').toMatchObject({ account: expect.anything() });
    }
  });

  it('a second call after a cleaned first call still works and still leaves the home plane alone', async () => {
    const homeKeys = await seedHomePlane();
    stubBoundary('ssoSilent', 'succeed');
    const { acquireSpaarkeGuestToken } = await import('../src/auth/msal-auth');
    await expect(acquireSpaarkeGuestToken('silent', 'jane@contoso.test', SCOPE)).resolves.toBe(GUEST_TOKEN);
    await expect(acquireSpaarkeGuestToken('silent', 'jane@contoso.test', SCOPE)).resolves.toBe(GUEST_TOKEN);
    const after = sessionSnapshot();
    for (const [key, value] of Object.entries(homeKeys)) expect(after[key]).toBe(value);
    expect(guestInstance()!.getAllAccounts()).toEqual([]);
  });
});
