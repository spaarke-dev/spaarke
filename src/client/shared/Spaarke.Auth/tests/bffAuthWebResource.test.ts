/**
 * `Spaarke.BffAuth` — the classic web resource `sprk_/scripts/bff_auth.js` that Dataverse form
 * scripts and ribbon commands call for a BFF token (#1655). Since #1655 it is a thin wrapper over
 * this library (src/webresource/bffAuth.ts, bundled by scripts/build-webresource.mjs).
 *
 * Only the edges are replaced: MSAL (`@azure/msal-browser`), `Xrm`, and `fetch` (the Dataverse
 * environment-variable queries, the BFF's /api/config/client and the BFF call itself).
 *
 * Covers:
 *  - the ssoSilent login hint is NEVER the display name (`userSettings.userName`) — root cause 1;
 *  - the cached account is chosen, never taken by position: with another user's account cached
 *    first, that account is never used; with two candidates none is guessed and ssoSilent runs
 *    with no hint — root cause 2 (security: a form script must not act as a different user);
 *  - no token → `getToken` resolves null and `authenticatedFetch` resolves null WITHOUT a request;
 *  - no interactive MSAL call is ever made (popup or redirect), in any test;
 *  - the caller contract is unchanged: Bearer attached, options kept, a non-OK Response returned as-is;
 *  - the MSAL config: the PCFs' client id (`sprk_MsalClientId`), tenant authority, localStorage cache;
 *  - the committed bundle installs `Spaarke.BffAuth` and both committed copies are identical.
 *
 * RED PROOF against the pre-#1655 hand-written helper: set BFF_AUTH_LEGACY_FILE to a copy of it,
 *   git show 848f63913:src/solutions/webresources/sprk_bff_auth.js > /tmp/legacy_bff_auth.js
 *   BFF_AUTH_LEGACY_FILE=/tmp/legacy_bff_auth.js npx jest tests/bffAuthWebResource.test.ts
 * The legacy rows run the same scenarios through that file (MSAL injected as `window.msal`, the
 * way it loaded MSAL from the CDN) and fail the hint and account tests.
 */
import * as fs from 'fs';
import * as path from 'path';

const ORG = 'https://org.crm.dynamics.test';
const BFF = 'https://bff.example.test';
const TENANT = '11111111-2222-3333-4444-555555555555';
const OTHER_TENANT = '99999999-8888-7777-6666-555555555555';
const UI_CLIENT_ID = 'aaaaaaaa-0000-0000-0000-00000000c11e'; // sprk_MsalClientId — the PCFs' client
const BFF_APP_ID = 'bbbbbbbb-0000-0000-0000-000000000bff'; // sprk_BffApiAppId (also the BFF's AzureAd:ClientId)
const DISPLAY_NAME = 'Test User One';
const OID_A = 'a0000000-0000-0000-0000-00000000000a';
const OID_B = 'b0000000-0000-0000-0000-00000000000b'; // the signed-in Dataverse user (systemuser.azureactivedirectoryobjectid)
const OID_B_HOME = 'c0000000-0000-0000-0000-00000000000c';
const SYSTEMUSER_ID = '5c6b1d2e-0000-0000-0000-000000000001';

type Win = typeof window & { Spaarke?: any; Xrm?: any; msal?: any };
const win = window as Win;

function jwt(claims: Record<string, unknown>): string {
  const enc = (o: unknown) => Buffer.from(JSON.stringify(o)).toString('base64');
  return `${enc({ alg: 'none', typ: 'JWT' })}.${enc({ exp: Math.floor(Date.now() / 1000) + 3600, ...claims })}.sig`;
}

const USER_B = {
  homeAccountId: `oid-b.${TENANT}`,
  tenantId: TENANT,
  localAccountId: OID_B,
  idTokenClaims: { oid: OID_B },
  username: 'userb@spaarke.test',
  name: 'User B',
};
const USER_A = {
  homeAccountId: `oid-a.${TENANT}`,
  tenantId: TENANT,
  localAccountId: OID_A,
  idTokenClaims: { oid: OID_A },
  username: 'usera@spaarke.test',
  name: 'User A',
};
const GUEST_HOME = {
  homeAccountId: `oid-b.${OTHER_TENANT}`,
  tenantId: OTHER_TENANT,
  localAccountId: OID_B_HOME,
  idTokenClaims: { oid: OID_B_HOME },
  username: 'userb@home.test',
  name: 'User B (home)',
};
const TOKEN_A = jwt({ oid: OID_A, tid: TENANT });
const TOKEN_B = jwt({ oid: OID_B, tid: TENANT });
/** What the browser's Entra session (user B's) answers to a no-hint ssoSilent. */
const TOKEN_SSO = jwt({ oid: OID_B, tid: TENANT, sid: 'session' });

function result(accessToken: string, account: unknown) {
  return { accessToken, account, expiresOn: new Date(Date.now() + 3600_000) };
}

// ---- MSAL (shared by both implementations) -------------------------------------------------
const mockMsal = {
  initialize: jest.fn(() => Promise.resolve()),
  handleRedirectPromise: jest.fn(() => Promise.resolve(null)),
  getAllAccounts: jest.fn((): unknown[] => []),
  getActiveAccount: jest.fn(() => null),
  acquireTokenSilent: jest.fn(),
  ssoSilent: jest.fn(),
  acquireTokenPopup: jest.fn(() => Promise.reject(new Error('popup must never be called'))),
  loginPopup: jest.fn(() => Promise.reject(new Error('popup must never be called'))),
  acquireTokenRedirect: jest.fn(() => Promise.reject(new Error('redirect must never be called'))),
  loginRedirect: jest.fn(() => Promise.reject(new Error('redirect must never be called'))),
  clearCache: jest.fn(() => Promise.resolve()),
};
const mockPcaConfigs: any[] = [];
const MockPca = jest.fn().mockImplementation((config: unknown) => {
  mockPcaConfigs.push(config);
  return mockMsal;
});

jest.mock('@azure/msal-browser', () => ({
  PublicClientApplication: jest.fn().mockImplementation((config: unknown) => {
    mockPcaConfigs.push(config);
    return mockMsal;
  }),
}));

// ---- fetch: Dataverse env vars, BFF config, BFF call -----------------------------------------
function response(body: unknown, status = 200) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: { get: () => 'application/json' },
    json: async () => body,
  };
}

const ENV_VARS: Record<string, string> = {
  sprk_BffApiBaseUrl: `${BFF}/api`,
  sprk_BffApiAppId: BFF_APP_ID,
  sprk_MsalClientId: UI_CLIENT_ID,
  sprk_TenantId: TENANT,
};

let fetchMock: jest.Mock;
let bffAnswer: { body: unknown; status: number };
/** What Dataverse answers for the signed-in user's systemuser row (#1656); a number is an HTTP failure. */
let systemUserAnswer: { azureactivedirectoryobjectid: string } | number;

function installFetch(): void {
  bffAnswer = { body: { ok: true }, status: 200 };
  systemUserAnswer = { azureactivedirectoryobjectid: OID_B };
  fetchMock = jest.fn(async (url: string) => {
    if (url.includes('/api/data/v9.2/environmentvariabledefinitions')) {
      return response({
        value: Object.keys(ENV_VARS).map((name, i) => ({
          environmentvariabledefinitionid: `def-${i}`,
          schemaname: name,
          defaultvalue: ENV_VARS[name],
        })),
      });
    }
    if (url.startsWith(`${ORG}/api/data/v9.2/systemusers(${SYSTEMUSER_ID})`)) {
      return typeof systemUserAnswer === 'number' ? response({}, systemUserAnswer) : response(systemUserAnswer);
    }
    if (url.includes('/api/data/v9.2/environmentvariablevalues')) {
      return response({ value: [] });
    }
    if (url.endsWith('/api/config/client')) {
      return response({
        bffBaseUrl: BFF,
        msalClientId: BFF_APP_ID,
        msalAuthority: `https://login.microsoftonline.com/${TENANT}`,
        msalScopes: [`api://${BFF_APP_ID}/user_impersonation`],
        tenantId: TENANT,
      });
    }
    return response(bffAnswer.body, bffAnswer.status);
  });
  (global as any).fetch = fetchMock;
}

function installXrm(): void {
  win.Xrm = {
    Utility: {
      getGlobalContext: () => ({
        getClientUrl: () => ORG,
        organizationSettings: { tenantId: TENANT },
        // What Dataverse really exposes: the systemuser id and the DISPLAY NAME. No UPN.
        userSettings: { userId: '{5C6B1D2E-0000-0000-0000-000000000001}', userName: DISPLAY_NAME },
      }),
    },
  };
}

// ---- the two implementations ------------------------------------------------------------------
interface Impl {
  name: string;
  load(): any;
}

const LIBRARY: Impl = {
  name: '@spaarke/auth wrapper (#1655)',
  load() {
    let api: any;
    jest.isolateModules(() => {
      // eslint-disable-next-line @typescript-eslint/no-require-imports
      api = require('../src/webresource/bffAuth').createBffAuth();
    });
    return api;
  },
};

const LEGACY_FILE = process.env.BFF_AUTH_LEGACY_FILE;
const LEGACY: Impl | null = LEGACY_FILE
  ? {
      name: `legacy hand-written helper (${path.basename(LEGACY_FILE)})`,
      load() {
        win.msal = { PublicClientApplication: MockPca };
        if (win.Spaarke) win.Spaarke.BffAuth = undefined;
        const script = document.createElement('script');
        script.textContent = fs.readFileSync(LEGACY_FILE, 'utf8');
        document.head.appendChild(script);
        return win.Spaarke.BffAuth;
      },
    }
  : null;

const IMPLS: Impl[] = LEGACY ? [LIBRARY, LEGACY] : [LIBRARY];

/** Every argument object ever passed to ssoSilent. */
const ssoRequests = () => mockMsal.ssoSilent.mock.calls.map(c => c[0] as { loginHint?: string });
/** Every account ever passed to acquireTokenSilent. */
const silentAccounts = () => mockMsal.acquireTokenSilent.mock.calls.map(c => (c[0] as { account: unknown }).account);
const bffCalls = () => fetchMock.mock.calls.filter(c => String(c[0]).startsWith(`${BFF}/api/v1/`));

beforeEach(() => {
  Object.values(mockMsal).forEach(fn => (fn as jest.Mock).mockClear());
  MockPca.mockClear();
  mockPcaConfigs.length = 0;
  mockMsal.getAllAccounts.mockReturnValue([]);
  // acquireTokenSilent answers as whichever account it is given (what a real refresh token would do).
  mockMsal.acquireTokenSilent.mockImplementation(async (req: { account: typeof USER_A }) =>
    result(req.account.homeAccountId === USER_A.homeAccountId ? TOKEN_A : TOKEN_B, req.account)
  );
  // ssoSilent with a display-name hint fails the way Entra does; otherwise the browser session answers.
  mockMsal.ssoSilent.mockImplementation(async (req: { loginHint?: string }) => {
    if (req.loginHint === DISPLAY_NAME) {
      throw Object.assign(
        new Error('login_required: AADSTS50058: A silent sign-in request was sent but no user is signed in.'),
        {
          errorCode: 'login_required',
        }
      );
    }
    return result(TOKEN_SSO, USER_B);
  });
  try {
    localStorage.clear();
    sessionStorage.clear();
  } catch {
    /* jsdom */
  }
  installFetch();
  installXrm();
  jest.spyOn(console, 'info').mockImplementation(() => {});
  jest.spyOn(console, 'log').mockImplementation(() => {});
  jest.spyOn(console, 'warn').mockImplementation(() => {});
  jest.spyOn(console, 'error').mockImplementation(() => {});
});

afterEach(() => {
  // No interactive MSAL call, in any scenario (ADR-028: a form load is not intent to sign in).
  expect(mockMsal.acquireTokenPopup).not.toHaveBeenCalled();
  expect(mockMsal.loginPopup).not.toHaveBeenCalled();
  expect(mockMsal.acquireTokenRedirect).not.toHaveBeenCalled();
  expect(mockMsal.loginRedirect).not.toHaveBeenCalled();
  jest.restoreAllMocks();
});

describe.each(IMPLS)('Spaarke.BffAuth — $name', impl => {
  describe('root cause 1: the ssoSilent login hint', () => {
    it('is never the display name when no account is cached (InPrivate / first visit) — and the token is acquired', async () => {
      const api = impl.load();

      const token = await api.getToken(BFF);

      expect(ssoRequests().length).toBeGreaterThan(0);
      for (const req of ssoRequests()) {
        expect(req.loginHint).not.toBe(DISPLAY_NAME);
        expect(req.loginHint).toBeUndefined();
      }
      expect(token).toBe(TOKEN_SSO);
    });

    it('authenticatedFetch reaches the BFF in that window (the "Access status unavailable" case)', async () => {
      const api = impl.load();
      const url = `${BFF}/api/v1/records/sprk_matter/1b4e28ba-2fa1-11d2-883f-0016d3cca427/no-access`;

      const res = await api.authenticatedFetch(url, { method: 'GET', headers: { Accept: 'application/json' } }, BFF);

      expect(res).not.toBeNull();
      expect(res.status).toBe(200);
      expect(bffCalls()).toHaveLength(1);
    });
  });

  describe('root cause 2: which cached account is used (security)', () => {
    it("never uses another user's account cached first; with two cached it uses the signed-in Dataverse user's (#1656)", async () => {
      mockMsal.getAllAccounts.mockReturnValue([USER_A, USER_B]);
      const api = impl.load();

      const token = await api.getToken(BFF);

      expect(silentAccounts()).not.toContainEqual(USER_A);
      expect(token).not.toBe(TOKEN_A);
      for (const req of ssoRequests()) {
        expect(req.loginHint).not.toBe(USER_A.username);
      }
      expect(token).toBe(TOKEN_B);
    });

    it("uses the signed-in user's tenant account, not a home-tenant account cached ahead of it", async () => {
      mockMsal.getAllAccounts.mockReturnValue([GUEST_HOME, USER_B]);
      const api = impl.load();

      const token = await api.getToken(BFF);

      expect(silentAccounts()).not.toContainEqual(GUEST_HOME);
      expect(silentAccounts()).toContainEqual(USER_B);
      expect(token).toBe(TOKEN_B);
    });

    it('the Access ribbon path (authenticatedFetch) never sends another user’s token', async () => {
      mockMsal.getAllAccounts.mockReturnValue([USER_A, USER_B]);
      const api = impl.load();

      await api.authenticatedFetch(`${BFF}/api/v1/access/grant`, { method: 'POST', body: '{}' }, BFF);

      expect(bffCalls()).toHaveLength(1);
      const sent = bffCalls()[0][1] as { headers: Record<string, string> };
      expect(sent.headers['Authorization']).not.toBe('Bearer ' + TOKEN_A);
    });
  });

  describe('#1656: a shared workstation (A signed out, B is the signed-in Dataverse user)', () => {
    it("only A's account cached: acquireTokenSilent is never called with it, and ssoSilent runs with NO hint", async () => {
      mockMsal.getAllAccounts.mockReturnValue([USER_A]);
      const api = impl.load();

      const token = await api.getToken(BFF);

      expect(silentAccounts()).not.toContainEqual(USER_A);
      expect(ssoRequests().length).toBeGreaterThan(0);
      for (const req of ssoRequests()) expect(req.loginHint).toBeUndefined();
      expect(token).toBe(TOKEN_SSO);
    });

    it("only A's account cached: the Access ribbon's call never carries A's token", async () => {
      mockMsal.getAllAccounts.mockReturnValue([USER_A]);
      const api = impl.load();

      await api.authenticatedFetch(`{BFF}/api/v1/access/unshare`, { method: 'POST', body: '{}' }, BFF);

      for (const call of bffCalls()) {
        expect((call[1] as { headers: Record<string, string> }).headers['Authorization']).not.toBe('Bearer ' + TOKEN_A);
      }
    });

    it("a no-hint ssoSilent answered by A's Entra session: A's token is discarded and the result is null", async () => {
      mockMsal.ssoSilent.mockImplementation(async () => result(TOKEN_A, USER_A));
      const api = impl.load();

      await expect(api.getToken(BFF)).resolves.toBeNull();
    });

    it("only B's account cached and it matches the Dataverse user: used silently, with no extra ssoSilent iframe", async () => {
      mockMsal.getAllAccounts.mockReturnValue([USER_B]);
      const api = impl.load();

      const token = await api.getToken(BFF);

      expect(silentAccounts()).toEqual([USER_B]);
      expect(mockMsal.ssoSilent).not.toHaveBeenCalled();
      expect(token).toBe(TOKEN_B);
    });

    it('the systemuser read fails: no cached account is used (fails closed to a no-hint ssoSilent)', async () => {
      systemUserAnswer = 503;
      mockMsal.getAllAccounts.mockReturnValue([USER_B]);
      const api = impl.load();

      await api.getToken(BFF);

      expect(mockMsal.acquireTokenSilent).not.toHaveBeenCalled();
      for (const req of ssoRequests()) expect(req.loginHint).toBeUndefined();
    });
  });

  describe('no token', () => {
    beforeEach(() => {
      const interactionRequired = Object.assign(new Error('login_required: AADSTS50058'), {
        errorCode: 'login_required',
      });
      mockMsal.acquireTokenSilent.mockRejectedValue(interactionRequired);
      mockMsal.ssoSilent.mockRejectedValue(interactionRequired);
    });

    it('getToken resolves null (never rejects)', async () => {
      const api = impl.load();
      await expect(api.getToken(BFF)).resolves.toBeNull();
    });

    it('authenticatedFetch resolves null WITHOUT sending the request', async () => {
      const api = impl.load();

      const res = await api.authenticatedFetch(`${BFF}/api/v1/records/sprk_matter/x/no-access`, { method: 'GET' }, BFF);

      expect(res).toBeNull();
      expect(bffCalls()).toHaveLength(0);
    });

    it('getToken with no BFF base URL resolves null without touching MSAL', async () => {
      const api = impl.load();
      await expect(api.getToken('')).resolves.toBeNull();
      expect(mockMsal.acquireTokenSilent).not.toHaveBeenCalled();
      expect(mockMsal.ssoSilent).not.toHaveBeenCalled();
    });
  });
});

describe('Spaarke.BffAuth — the caller contract and MSAL config (#1655 wrapper)', () => {
  describe('the token goes only to the BFF origin', () => {
    it.each([
      ['another host', 'https://evil.example.test/api/v1/records/sprk_matter/x/no-access'],
      ['the BFF host over http', 'http://bff.example.test/api/v1/x'],
      ['a BFF look-alike host', 'https://bff.example.test.evil.example/api/v1/x'],
      ['a page-relative URL (the Dataverse origin)', '/api/data/v9.2/accounts'],
    ])('%s: null, no request, no token acquired', async (_label, url) => {
      mockMsal.getAllAccounts.mockReturnValue([USER_B]);
      const api = LIBRARY.load();

      const res = await api.authenticatedFetch(url, { method: 'POST', body: '{}' }, BFF);

      expect(res).toBeNull();
      expect(fetchMock.mock.calls.filter(c => (c[1] as any)?.headers?.Authorization)).toHaveLength(0);
      expect(fetchMock.mock.calls.map(c => String(c[0]))).not.toContain(url);
    });

    it('the BFF origin (any path, with or without /api in the base URL handed in): sent with the token', async () => {
      mockMsal.getAllAccounts.mockReturnValue([USER_B]);
      const api = LIBRARY.load();

      const a = await api.authenticatedFetch(`${BFF}/api/v1/records/sprk_matter/x/no-access`, {}, `${BFF}/api`);
      const b = await api.authenticatedFetch(`${BFF}/api/finance/matters/x/recalculate`, { method: 'POST' }, BFF);

      expect(a.status).toBe(200);
      expect(b.status).toBe(200);
      expect(fetchMock.mock.calls.filter(c => (c[1] as any)?.headers?.Authorization)).toHaveLength(2);
    });
  });

  it('normalizeBaseUrl strips a trailing slash and a trailing /api (the sprk_BffApiBaseUrl shape)', () => {
    const api = LIBRARY.load();
    expect(api.normalizeBaseUrl('https://spaarke-bff-dev.azurewebsites.net/api')).toBe(
      'https://spaarke-bff-dev.azurewebsites.net'
    );
    expect(api.normalizeBaseUrl(' https://host.test/API/ ')).toBe('https://host.test');
    expect(api.normalizeBaseUrl('https://host.test')).toBe('https://host.test');
  });

  it('with no session, the first call runs the silent chain ONCE (not again after the eager acquisition)', async () => {
    const interactionRequired = Object.assign(new Error('login_required'), { errorCode: 'login_required' });
    mockMsal.ssoSilent.mockRejectedValue(interactionRequired);
    const api = LIBRARY.load();

    await expect(api.getToken(BFF)).resolves.toBeNull();
    expect(mockMsal.ssoSilent).toHaveBeenCalledTimes(1);

    // A later call (e.g. a ribbon click after the user signed in elsewhere) does try again.
    await api.getToken(BFF);
    expect(mockMsal.ssoSilent).toHaveBeenCalledTimes(2);
  });

  it('attaches the Bearer token, keeps method/body/headers/signal, and returns a non-OK Response as-is', async () => {
    mockMsal.getAllAccounts.mockReturnValue([USER_B]);
    bffAnswer = { body: { title: 'Forbidden', status: 403, detail: 'nope', reasonCode: 'sdap.x' }, status: 403 };
    const api = LIBRARY.load();
    const controller = new AbortController();

    const res = await api.authenticatedFetch(
      `${BFF}/api/v1/access/unshare`,
      { method: 'POST', body: '{"a":1}', headers: { 'Content-Type': 'application/json' }, signal: controller.signal },
      BFF
    );

    expect(res.ok).toBe(false);
    expect(res.status).toBe(403);
    await expect(res.json()).resolves.toMatchObject({ detail: 'nope', reasonCode: 'sdap.x' });
    const [url, init] = bffCalls()[0] as [string, any];
    expect(url).toBe(`${BFF}/api/v1/access/unshare`);
    expect(init.method).toBe('POST');
    expect(init.body).toBe('{"a":1}');
    expect(init.signal).toBe(controller.signal);
    expect(init.headers).toEqual({ 'Content-Type': 'application/json', Authorization: 'Bearer ' + TOKEN_B });
  });

  it('builds ONE MSAL client with the PCFs’ client id, a tenant authority and the localStorage cache', async () => {
    mockMsal.getAllAccounts.mockReturnValue([USER_B]);
    const api = LIBRARY.load();

    await Promise.all([api.getToken(BFF), api.getToken(BFF), api.authenticatedFetch(`${BFF}/api/v1/x`, {}, BFF)]);

    expect(mockPcaConfigs).toHaveLength(1);
    const config = mockPcaConfigs[0];
    expect(config.auth.clientId).toBe(UI_CLIENT_ID);
    expect(config.auth.authority).toBe(`https://login.microsoftonline.com/${TENANT}`);
    expect(config.cache).toEqual({ cacheLocation: 'localStorage', storeAuthStateInCookie: true });
    expect(mockMsal.acquireTokenSilent).toHaveBeenCalledWith({
      scopes: [`api://${BFF_APP_ID}/user_impersonation`],
      account: USER_B,
    });
  });

  it('retries initialisation on a later call when the environment could not be read', async () => {
    const api = LIBRARY.load();
    const original = fetchMock.getMockImplementation()!;
    fetchMock.mockImplementationOnce(async () => response({}, 503)); // env var query fails once

    await expect(api.getToken(BFF)).resolves.toBeNull();
    fetchMock.mockImplementation(original);
    await expect(api.getToken(BFF)).resolves.toBe(TOKEN_SSO);
  });

  it('exposes the same public members as before (getToken, authenticatedFetch)', () => {
    const api = LIBRARY.load();
    expect(typeof api.getToken).toBe('function');
    expect(typeof api.authenticatedFetch).toBe('function');
    expect(api._version).toBe('2.0.0');
  });
});

describe('the committed web resource bundle', () => {
  const repoRoot = path.resolve(__dirname, '../../../../..');
  const CANONICAL = path.join(repoRoot, 'src/solutions/webresources/sprk_bff_auth.js');
  const SOLUTION = path.join(repoRoot, 'src/dataverse/solutions/SpaarkeMaster/WebResources/sprk_/scripts/bff_auth.js');
  const read = (p: string) => fs.readFileSync(p, 'utf8').replace(/\r\n/g, '\n');

  it('is identical in the canonical and the SpaarkeMaster copy', () => {
    expect(read(SOLUTION)).toBe(read(CANONICAL));
  });

  it('loads nothing from a CDN and carries no hand-written MSAL flow', () => {
    const text = read(CANONICAL);
    expect(text).not.toMatch(/alcdn\.msauth\.net|msauth\.net\/browser/);
    expect(text).not.toMatch(/userSettings\.userName|settings\.userName/);
    expect(text).toMatch(/GENERATED by src\/client\/shared\/Spaarke\.Auth\/scripts\/build-webresource\.mjs/);
  });

  it('installs Spaarke.BffAuth on the form window, and a later `var Spaarke = Spaarke || {}` library keeps it', () => {
    if (win.Spaarke) win.Spaarke.BffAuth = undefined;
    const load = (text: string) => {
      const script = document.createElement('script');
      script.textContent = text;
      document.head.appendChild(script);
    };

    load(read(CANONICAL));
    load('var Spaarke = Spaarke || {}; Spaarke.Other = { loaded: true };');

    expect(typeof win.Spaarke.BffAuth.getToken).toBe('function');
    expect(typeof win.Spaarke.BffAuth.authenticatedFetch).toBe('function');
    expect(win.Spaarke.BffAuth._version).toBe('2.0.0');
    expect(win.Spaarke.Other.loaded).toBe(true);
  });
});
