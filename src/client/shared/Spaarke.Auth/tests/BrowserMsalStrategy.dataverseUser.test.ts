/**
 * #1656 — BrowserMsalStrategy on a shared workstation. MSAL's localStorage cache outlives a
 * Dataverse sign-out, so user A's account can still be cached when user B signs in. Inside a
 * Dataverse host the strategy reads the signed-in user's Entra object id
 * (systemusers({userSettings.userId}).azureactivedirectoryobjectid, session cookie) and uses only
 * an account — and accepts only a token — with that `oid`. Outside Dataverse nothing changes.
 *
 * MSAL, Xrm and fetch are mocked. Applies to every @spaarke/auth consumer (PCFs, code pages,
 * the form scripts' Spaarke.BffAuth).
 */
import type { IAuthConfig } from '../src/types';

const TENANT = '11111111-2222-3333-4444-555555555555';
const OTHER_TENANT = '99999999-8888-7777-6666-555555555555';
const ORG = 'https://org.crm.dynamics.test';
const SYSTEMUSER_ID = '5c6b1d2e-0000-0000-0000-000000000001';
const OID_A = 'a0000000-0000-0000-0000-00000000000a';
const OID_B = 'b0000000-0000-0000-0000-00000000000b';

function jwt(oid: string): string {
  // base64url, as Entra issues it (the decoder must not rely on plain base64).
  const enc = (o: unknown) => Buffer.from(JSON.stringify(o)).toString('base64url');
  return `${enc({ alg: 'none' })}.${enc({ oid, tid: TENANT, exp: Math.floor(Date.now() / 1000) + 3600 })}.sig`;
}
const account = (oid: string, tenantId = TENANT, username = `${oid.slice(0, 1)}@x.test`) => ({
  homeAccountId: `${oid}.${tenantId}`,
  tenantId,
  localAccountId: oid,
  idTokenClaims: { oid },
  username,
});
const A = account(OID_A, TENANT, 'usera@spaarke.test');
const B = account(OID_B, TENANT, 'userb@spaarke.test');
const A_HOME = account('c0000000-0000-0000-0000-00000000000c', OTHER_TENANT, 'usera@home.test');
const ok = (accessToken: string, acct: unknown) => ({
  accessToken,
  account: acct,
  expiresOn: new Date(Date.now() + 3600_000),
});

const mockMsal = {
  initialize: jest.fn(() => Promise.resolve()),
  handleRedirectPromise: jest.fn(() => Promise.resolve(null)),
  getAllAccounts: jest.fn((): unknown[] => []),
  acquireTokenSilent: jest.fn(),
  ssoSilent: jest.fn(),
  acquireTokenPopup: jest.fn(),
  clearCache: jest.fn(() => Promise.resolve()),
  logoutPopup: jest.fn(() => Promise.resolve()),
};
jest.mock('@azure/msal-browser', () => ({ PublicClientApplication: jest.fn().mockImplementation(() => mockMsal) }));

const config: Required<IAuthConfig> = {
  clientId: 'client',
  authority: `https://login.microsoftonline.com/${TENANT}`,
  tenantId: TENANT,
  redirectUri: 'http://localhost/',
  bffApiScope: 'api://bff/user_impersonation',
  bffBaseUrl: 'https://bff.test',
  proactiveRefresh: false,
  requireXrm: false,
  requireSilentOnly: true,
  requireTenantAuthority: true,
};

type Win = typeof window & { Xrm?: unknown };
const win = window as Win;
let fetchMock: jest.Mock;
let systemUser: { azureactivedirectoryobjectid: string } | number;

function load() {
  let Strategy: any;
  jest.isolateModules(() => {
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    Strategy = require('../src/strategies/BrowserMsalStrategy').BrowserMsalStrategy;
  });
  return new Strategy(config);
}

const silentAccounts = () => mockMsal.acquireTokenSilent.mock.calls.map(c => (c[0] as { account: unknown }).account);
const hints = () => mockMsal.ssoSilent.mock.calls.map(c => (c[0] as { loginHint?: string }).loginHint);

beforeEach(() => {
  Object.values(mockMsal).forEach(fn => (fn as jest.Mock).mockClear());
  mockMsal.getAllAccounts.mockReturnValue([]);
  mockMsal.acquireTokenSilent.mockImplementation(async (req: { account: { localAccountId: string } }) =>
    ok(jwt(req.account.localAccountId), req.account)
  );
  // The browser's Entra session is B's.
  mockMsal.ssoSilent.mockImplementation(async () => ok(jwt(OID_B), B));
  systemUser = { azureactivedirectoryobjectid: OID_B.toUpperCase() }; // Dataverse returns either case
  fetchMock = jest.fn(async (url: string) => {
    if (url === `${ORG}/api/data/v9.2/systemusers(${SYSTEMUSER_ID})?$select=azureactivedirectoryobjectid`) {
      return typeof systemUser === 'number'
        ? { ok: false, status: systemUser, json: async () => ({}) }
        : { ok: true, status: 200, json: async () => systemUser };
    }
    throw new Error(`unexpected fetch ${url}`);
  });
  (global as any).fetch = fetchMock;
  win.Xrm = {
    Utility: {
      getGlobalContext: () => ({
        getClientUrl: () => ORG,
        userSettings: { userId: `{${SYSTEMUSER_ID.toUpperCase()}}`, userName: 'User B' },
      }),
    },
  };
  sessionStorage.clear();
  jest.spyOn(console, 'info').mockImplementation(() => {});
  jest.spyOn(console, 'warn').mockImplementation(() => {});
  jest.spyOn(console, 'error').mockImplementation(() => {});
});

afterEach(() => {
  expect(mockMsal.acquireTokenPopup).not.toHaveBeenCalled();
  delete win.Xrm;
  jest.restoreAllMocks();
});

describe('BrowserMsalStrategy — the signed-in Dataverse user (#1656)', () => {
  it("only A's account cached, Dataverse user B: acquireTokenSilent is never called with A; ssoSilent runs with NO hint", async () => {
    mockMsal.getAllAccounts.mockReturnValue([A]);

    const result = await load().acquire();

    expect(silentAccounts()).not.toContainEqual(A);
    expect(hints()).toEqual([undefined]);
    expect(result.accessToken).toBe(jwt(OID_B));
  });

  it("only A's home-tenant account cached: never used, and its UPN is never the hint", async () => {
    mockMsal.getAllAccounts.mockReturnValue([A_HOME]);

    await load().acquire();

    expect(silentAccounts()).not.toContainEqual(A_HOME);
    expect(hints()).toEqual([undefined]);
  });

  it("ssoSilent answered with A's token (another Entra session): discarded, result empty", async () => {
    mockMsal.ssoSilent.mockImplementation(async () => ok(jwt(OID_A), A));

    const result = await load().acquire();

    expect(result.accessToken).toBe('');
  });

  it("acquireTokenSilent on B's account returning A's token: discarded", async () => {
    mockMsal.getAllAccounts.mockReturnValue([B]);
    mockMsal.acquireTokenSilent.mockImplementation(async () => ok(jwt(OID_A), B));
    mockMsal.ssoSilent.mockImplementation(async () => ok(jwt(OID_A), A));

    const result = await load().acquire();

    expect(result.accessToken).toBe('');
  });

  it('only B cached and B is the Dataverse user: used, no ssoSilent iframe; one systemuser read per page', async () => {
    mockMsal.getAllAccounts.mockReturnValue([B]);
    const strategy = load();

    const first = await strategy.acquire();
    await strategy.acquire();

    expect(first.accessToken).toBe(jwt(OID_B));
    expect(silentAccounts()).toEqual([B, B]);
    expect(mockMsal.ssoSilent).not.toHaveBeenCalled();
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('A and B cached (A first): B is chosen', async () => {
    mockMsal.getAllAccounts.mockReturnValue([A, B]);

    const result = await load().acquire();

    expect(silentAccounts()).toEqual([B]);
    expect(result.accessToken).toBe(jwt(OID_B));
  });

  it.each([
    ['answers 403', () => (systemUser = 403)],
    ['has no Entra object id', () => (systemUser = { azureactivedirectoryobjectid: '' })],
    [
      'throws (network)',
      () =>
        fetchMock.mockImplementation(async () => {
          throw new TypeError('Failed to fetch');
        }),
    ],
  ])('the systemuser read %s: no cached account is used (fails closed to a no-hint ssoSilent)', async (_l, arrange) => {
    arrange();
    mockMsal.getAllAccounts.mockReturnValue([B]);

    await load().acquire();

    expect(mockMsal.acquireTokenSilent).not.toHaveBeenCalled();
    expect(hints()).toEqual([undefined]);
  });

  it('outside a Dataverse host (no Xrm, not a Dataverse domain): unchanged — the lone cached account is used, no read', async () => {
    delete win.Xrm;
    mockMsal.getAllAccounts.mockReturnValue([A]);

    const result = await load().acquire();

    expect(silentAccounts()).toEqual([A]);
    expect(result.accessToken).toBe(jwt(OID_A));
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
