// Real msal-browser 5.5.0 regressions for the Teams tab (spaarke-SPA-external-access-platform-r3 task 075, review fix round 1).
//
// tests/teams-home-auth.test.ts fakes MSAL, which is why it cannot see two defects that live in the library's own behaviour:
//
//   F1  The interaction lock. msal-browser keeps ONE per-tab sessionStorage key (`msal.interaction.status`) that survives a
//       Teams iframe reload in the middle of a popup. While it exists every `acquireTokenPopup` throws
//       `interaction_in_progress`, so the guest "Link my account" click, the BFF popup fallback and the "Sign in" button all
//       fail until Teams restarts. `clearStaleTeamsInteractionLock` removes it at page load.
//   F4  The tenant. Under NAA `AuthenticationResult.tenantId` is the Teams HOST account's tenant
//       (`NestedAppAuthAdapter.fromNaaTokenResponse` -> `fromNaaAccountInfo`), whatever tenant issued the token. The tenant
//       checks therefore read the token's own `tid` (id token claims, else the access token payload).
//
// Only the boundaries are faked: the Teams JS handshake, the NAA bridge (a window.nestedAppAuthBridge that answers like a
// host), and the browser's window.open. Everything else is the real library.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PublicClientApplication } from '@azure/msal-browser';

vi.setConfig({ testTimeout: 60000 });

const SPAARKE_TENANT = 'a221a95e-6abc-4434-aecc-e48338a1b2f2';
const A8_CLIENT_ID = '11111111-2222-3333-4444-555555555555';
const HOME_CLIENT_ID = '66666666-7777-8888-9999-000000000001';
const TID = 'c0c0c0c0-0000-4000-8000-000000000001';
const OTHER_TID = 'dddddddd-0000-4000-8000-000000000003';
const SCOPE = 'api://aaaaaaaa-0000-0000-0000-00000000000a/user_impersonation';
const LOCK_KEY = 'msal.interaction.status';

const teams = vi.hoisted(() => ({
  getContext: vi.fn(),
  naaRecommended: vi.fn(() => true),
}));

// The shared App tree is not under test here (only main.tsx's Teams bootstrap): a marker.
vi.mock('../src/App', () => ({ App: () => 'app mounted' }));

vi.mock('@microsoft/teams-js', () => ({
  app: {
    initialize: vi.fn(async () => undefined),
    getContext: teams.getContext,
    notifyAppLoaded: vi.fn(),
    notifySuccess: vi.fn(),
    notifyFailure: vi.fn(),
    registerOnThemeChangeHandler: vi.fn(),
    FailedReason: { AuthFailed: 'AuthFailed', Timeout: 'Timeout', Other: 'Other' },
  },
  nestedAppAuth: { isNAAChannelRecommended: teams.naaRecommended },
  authentication: { getAuthToken: vi.fn() },
}));

const b64url = (value: unknown) =>
  btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
const jwt = (claims: Record<string, unknown>) => `${b64url({ alg: 'none' })}.${b64url(claims)}.sig`;

beforeEach(() => {
  sessionStorage.clear();
  localStorage.clear();
  delete (window as unknown as { nestedAppAuthBridge?: unknown }).nestedAppAuthBridge;
  vi.stubEnv('VITE_WORKFORCE_HOME_MSAL_CLIENT_ID', HOME_CLIENT_ID);
  vi.stubEnv('VITE_TEAMS_MSAL_CLIENT_ID', A8_CLIENT_ID);
  vi.stubEnv('VITE_TEAMS_MSAL_TENANT_ID', SPAARKE_TENANT);
  // Cross-tab messaging boundary (see tests/guest-token-cache-isolation.test.ts): nothing here has a second tab.
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
  // The browser boundary: jsdom has no popup windows, so window.open yields no window (msal then raises a popup error).
  vi.spyOn(window, 'open').mockReturnValue(null);
  vi.spyOn(console, 'warn').mockImplementation(() => undefined);
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
});

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
  delete (window as unknown as { nestedAppAuthBridge?: unknown }).nestedAppAuthBridge;
});

async function load() {
  vi.resetModules();
  const msalAuth = await import('../src/auth/msal-auth');
  const plane = await import('../src/auth/standalone-plane');
  return { msalAuth, plane };
}

const errorCodeOf = (e: unknown) => (e as { errorCode?: string } | null)?.errorCode;

// ---------------------------------------------------------------------------------------------------------------
// F1: the stale interaction lock
// ---------------------------------------------------------------------------------------------------------------

describe('a stale msal interaction lock at Teams page load (real msal-browser)', () => {
  const cfg = { clientId: HOME_CLIENT_ID, tenantId: TID, bffScope: SCOPE };

  /** Run the BFF popup sign-in ("Sign in" button) and report the error code msal-browser raised. */
  async function signInPopupCode(msalAuth: Awaited<ReturnType<typeof load>>['msalAuth']): Promise<string | undefined> {
    const error = await msalAuth.signInTeamsWorkforceViaPopup(cfg).then(
      () => null,
      (e: unknown) => e
    );
    return errorCodeOf(error);
  }

  /** Run the guest "Link my account" popup and report the error code msal-browser raised on acquireTokenPopup. */
  async function guestPopupCode(msalAuth: Awaited<ReturnType<typeof load>>['msalAuth']): Promise<string | undefined> {
    const spy = vi.spyOn(PublicClientApplication.prototype, 'acquireTokenPopup');
    await msalAuth.acquireSpaarkeGuestToken('popup', 'jane@contoso.test', SCOPE).catch(() => undefined);
    expect(spy).toHaveBeenCalled();
    const outcome = await (spy.mock.results[0].value as Promise<unknown>).then(
      () => null,
      (e: unknown) => e
    );
    spy.mockRestore();
    return errorCodeOf(outcome);
  }

  it('CONTROL: a stale lock makes the BFF popup and the guest popup throw interaction_in_progress (the defect)', async () => {
    sessionStorage.setItem(LOCK_KEY, JSON.stringify({ clientId: A8_CLIENT_ID, type: 'signin' }));
    const { msalAuth } = await load();
    expect(await signInPopupCode(msalAuth)).toBe('interaction_in_progress');
    expect(await guestPopupCode(msalAuth)).toBe('interaction_in_progress');
  });

  it.each([
    ['the A8 (guest popup) client', A8_CLIENT_ID],
    ['the multitenant home (BFF popup) client', HOME_CLIENT_ID],
  ])(
    'a lock owned by %s: after clearStaleTeamsInteractionLock neither popup is blocked any more',
    async (_label, owner) => {
      sessionStorage.setItem(LOCK_KEY, JSON.stringify({ clientId: owner, type: 'signin' }));
      const { msalAuth, plane } = await load();
      expect(plane.clearStaleTeamsInteractionLock()).toBe(true);
      expect(sessionStorage.getItem(LOCK_KEY)).toBeNull();

      // The popup now reaches the browser (window.open, which jsdom answers with no window); the lock is not what stops it.
      const home = await signInPopupCode(msalAuth);
      expect(home).toBeDefined();
      expect(home).not.toBe('interaction_in_progress');
      expect(window.open).toHaveBeenCalled();
      const guest = await guestPopupCode(msalAuth);
      expect(guest).not.toBe('interaction_in_progress');
    }
  );

  it('END TO END (bootstrapTeams): with a stale lock the Teams popup fallback is not blocked by interaction_in_progress', async () => {
    teams.naaRecommended.mockReturnValue(false); // no NAA: the bootstrap goes straight to the standard popup instance
    teams.getContext.mockResolvedValue({
      app: { theme: 'default', locale: 'en-us', host: { clientType: 'web' } },
      page: { subPageId: '' },
      user: { tenant: { id: TID } },
    });
    vi.stubEnv(
      'VITE_BACKENDS',
      JSON.stringify([
        {
          customerKey: 'contoso',
          displayName: 'Contoso',
          bffBaseUrl: 'https://contoso-bff.test',
          bffAppId: 'aaaaaaaa-0000-0000-0000-00000000000a',
          homeTenantIds: [TID],
        },
      ])
    );
    sessionStorage.setItem(LOCK_KEY, JSON.stringify({ clientId: A8_CLIENT_ID, type: 'signin' }));
    document.body.innerHTML = '<div id="root"></div>';
    vi.resetModules();
    await import('../src/main');
    const rtl = await import('@testing-library/react');
    // The popup cannot open in jsdom (no window), so the sign-in error screen shows - with a diagnostic that is NOT the lock.
    expect(await rtl.screen.findByText('Sign-in failed', undefined, { timeout: 30000 })).toBeTruthy();
    expect(window.open).toHaveBeenCalled();
    expect(document.body.textContent ?? '').not.toContain('interaction_in_progress');
    expect(sessionStorage.getItem(LOCK_KEY)).toBeNull();
  });

  it('MUST-NOT-FIRE: a lock owned by another client (CIAM) stays - it is not ours to remove', async () => {
    const foreign = JSON.stringify({ clientId: '12345678-0000-4000-8000-0000000000aa', type: 'signin' });
    sessionStorage.setItem(LOCK_KEY, foreign);
    sessionStorage.setItem('msal.token.keys.' + HOME_CLIENT_ID, '{"idToken":[]}');
    const { plane } = await load();
    expect(plane.clearStaleTeamsInteractionLock()).toBe(false);
    expect(sessionStorage.getItem(LOCK_KEY)).toBe(foreign);
    expect(sessionStorage.getItem('msal.token.keys.' + HOME_CLIENT_ID)).toBe('{"idToken":[]}');
  });
});

// ---------------------------------------------------------------------------------------------------------------
// F4: the tenant of a token through the real NAA controller
// ---------------------------------------------------------------------------------------------------------------

/**
 * A window.nestedAppAuthBridge that answers like a Teams host: the init context carries the host account (a Teams user in the
 * Teams context tenant), and every token request is answered with a token issued by `issuer` (`tid` claim) and the SAME host
 * account - which is what a real bridge does (the account is the host's, the token's tenant is the authority's).
 */
function installBridge(options: { issuer: string; idTokenTid?: string | null; accessTokenTid?: string }) {
  const hostAccount = {
    homeAccountId: `jane-oid.${TID}`,
    environment: 'login.microsoftonline.com',
    tenantId: TID,
    username: 'jane@contoso.test',
    localAccountId: 'jane-oid',
  };
  const listeners: Array<(m: unknown) => void> = [];
  const requests: Array<{ method: string; tokenParams?: Record<string, unknown> }> = [];
  (window as unknown as { nestedAppAuthBridge: unknown }).nestedAppAuthBridge = {
    addEventListener: (_t: string, cb: (m: unknown) => void) => listeners.push(cb),
    removeEventListener: () => undefined,
    postMessage: (raw: string) => {
      const msg = JSON.parse(raw) as { method: string; requestId: string; tokenParams?: Record<string, unknown> };
      requests.push({ method: msg.method, tokenParams: msg.tokenParams });
      const reply: Record<string, unknown> = {
        messageType: 'NestedAppAuthResponse',
        requestId: msg.requestId,
        success: true,
      };
      if (msg.method === 'GetInitContext') {
        reply.initContext = {
          sdkName: 'test-host',
          sdkVersion: '1',
          capabilities: { queryAccount: false },
          accountContext: {
            homeAccountId: hostAccount.homeAccountId,
            environment: hostAccount.environment,
            tenantId: hostAccount.tenantId,
          },
        };
      } else {
        const base = { oid: 'jane-oid', sub: 'x', preferred_username: 'jane@contoso.test', aud: A8_CLIENT_ID };
        const idClaims = options.idTokenTid === null ? base : { ...base, tid: options.idTokenTid ?? options.issuer };
        reply.token = {
          access_token: jwt({ ...base, aud: 'api://x', tid: options.accessTokenTid ?? options.issuer }),
          id_token: jwt(idClaims),
          expires_in: 3600,
          scope: SCOPE,
        };
        reply.account = hostAccount;
      }
      setTimeout(() => listeners.forEach(l => l({ data: JSON.stringify(reply) })), 0);
    },
  };
  return { requests, hostAccount };
}

describe('the tenant check reads the TOKEN through the real NAA controller (real msal-browser, fake host bridge)', () => {
  const cfg = { clientId: HOME_CLIENT_ID, tenantId: TID, bffScope: SCOPE };

  it('PROBE: result.tenantId is the host account tenant even for a token another tenant issued', async () => {
    installBridge({ issuer: OTHER_TID });
    const { createNestablePublicClientApplication } = await import('@azure/msal-browser');
    const app = await createNestablePublicClientApplication({
      auth: { clientId: HOME_CLIENT_ID, authority: `https://login.microsoftonline.com/${TID}` },
      cache: { cacheLocation: 'memoryStorage' },
    });
    const result = await app.ssoSilent({
      scopes: [SCOPE],
      authority: `https://login.microsoftonline.com/${OTHER_TID}`,
    });
    expect(result.tenantId).toBe(TID); // the HOST's tenant ...
    expect((result.idTokenClaims as { tid?: string }).tid).toBe(OTHER_TID); // ... the token's tenant is the claim
  });

  describe('the BFF token (acquireTeamsWorkforceBffToken)', () => {
    it('a token issued by another tenant is refused although result.tenantId is the Teams tid (the defect)', async () => {
      installBridge({ issuer: OTHER_TID });
      const { msalAuth } = await load();
      await expect(msalAuth.acquireTeamsWorkforceBffToken(cfg)).rejects.toBeInstanceOf(
        msalAuth.TeamsWorkforceAuthError
      );
      expect(msalAuth.getTeamsWorkforceSignedInInstance()).toBeNull();
    });

    it('a token issued by the Teams tenant is accepted', async () => {
      installBridge({ issuer: TID });
      const { msalAuth } = await load();
      await expect(msalAuth.acquireTeamsWorkforceBffToken(cfg)).resolves.toEqual(expect.stringContaining('.'));
      expect(msalAuth.getTeamsWorkforceSignedInInstance()).not.toBeNull();
    });

    it('an id token without a tid: the access token payload tid decides (accepted for the Teams tenant, refused otherwise)', async () => {
      installBridge({ issuer: TID, idTokenTid: null });
      const ok = await load();
      await expect(ok.msalAuth.acquireTeamsWorkforceBffToken(cfg)).resolves.toEqual(expect.stringContaining('.'));

      sessionStorage.clear();
      delete (window as unknown as { nestedAppAuthBridge?: unknown }).nestedAppAuthBridge;
      installBridge({ issuer: TID, idTokenTid: null, accessTokenTid: OTHER_TID });
      const refused = await load();
      await expect(refused.msalAuth.acquireTeamsWorkforceBffToken(cfg)).rejects.toBeInstanceOf(
        refused.msalAuth.TeamsWorkforceAuthError
      );
    });
  });

  describe('the guest token (acquireSpaarkeGuestToken, silent, Teams host)', () => {
    async function guest() {
      const loaded = await load();
      loaded.msalAuth.setGuestLinkHost('teams');
      return loaded;
    }

    it('a token issued by Spaarke is accepted although result.tenantId is the Teams tid (the defect)', async () => {
      const bridge = installBridge({ issuer: SPAARKE_TENANT });
      const { msalAuth } = await guest();
      const token = await msalAuth.acquireSpaarkeGuestToken('silent', 'jane@contoso.test', SCOPE);
      expect(token).toEqual(expect.stringContaining('.'));
      // The request that reached the bridge named Spaarke's authority; the loginHint is dropped by NAA (no such bridge field).
      const request = bridge.requests.find(r => r.method !== 'GetInitContext')!;
      expect(request.tokenParams?.authority).toBe(`https://login.microsoftonline.com/${SPAARKE_TENANT}`);
      expect(request.tokenParams).not.toHaveProperty('loginHint');
    });

    it('a token issued by the Teams tenant is refused although result.tenantId is the Teams tid: null, tenant_mismatch', async () => {
      installBridge({ issuer: TID });
      const { msalAuth } = await guest();
      await expect(msalAuth.acquireSpaarkeGuestToken('silent', 'jane@contoso.test', SCOPE)).resolves.toBeNull();
      expect((console.warn as unknown as { mock: { calls: unknown[][] } }).mock.calls.flat().join(' ')).toContain(
        'tenant_mismatch'
      );
    });

    it('an id token without a tid: the access token payload decides', async () => {
      installBridge({ issuer: SPAARKE_TENANT, idTokenTid: null });
      const ok = await guest();
      await expect(ok.msalAuth.acquireSpaarkeGuestToken('silent', 'jane@contoso.test', SCOPE)).resolves.toEqual(
        expect.stringContaining('.')
      );

      delete (window as unknown as { nestedAppAuthBridge?: unknown }).nestedAppAuthBridge;
      installBridge({ issuer: SPAARKE_TENANT, idTokenTid: null, accessTokenTid: TID });
      const refused = await guest();
      await expect(refused.msalAuth.acquireSpaarkeGuestToken('silent', 'jane@contoso.test', SCOPE)).resolves.toBeNull();
    });
  });
});
