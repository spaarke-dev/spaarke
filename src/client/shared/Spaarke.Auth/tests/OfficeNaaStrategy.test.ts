/**
 * Tests for OfficeNaaStrategy's choice of MSAL client — the Office add-ins' sign-in path.
 *
 * Mocks `@azure/msal-browser` and the `Office` global so each host shape (Outlook on the web and new Outlook, which
 * both report `OfficeOnline`; classic Outlook; Word on the web; a host without NestedAppAuth 1.1) can be exercised
 * without a real Office host. The bridge's availability is reported the way MSAL reports it: an Info log line.
 */

import type { Configuration } from '@azure/msal-browser';
import type { IAuthConfig } from '../src/types';

function makeJwt(expSeconds: number): string {
  const header = Buffer.from(JSON.stringify({ alg: 'none', typ: 'JWT' })).toString('base64');
  const payload = Buffer.from(JSON.stringify({ exp: expSeconds })).toString('base64');
  return `${header}.${payload}.sig`;
}

function freshJwt(): string {
  return makeJwt(Math.floor(Date.now() / 1000) + 3600);
}

const mockAccount = { username: 'user@tenant.onmicrosoft.com' };

function makeInstance() {
  return {
    initialize: jest.fn(() => Promise.resolve()),
    handleRedirectPromise: jest.fn(() => Promise.resolve(null)),
    getAllAccounts: jest.fn((): unknown[] => [mockAccount]),
    acquireTokenSilent: jest.fn(),
    acquireTokenPopup: jest.fn(),
    clearCache: jest.fn(() => Promise.resolve()),
    logoutPopup: jest.fn(() => Promise.resolve()),
  };
}

/** The client `createNestablePublicClientApplication` returns, and the standard popup client. */
let naaInstance = makeInstance();
let pcaInstance = makeInstance();

/** What the mocked nestable factory says about the bridge, through the config's logger (null = no line at all). */
let bridgeLine: 'true' | 'false' | null = 'true';

const mockCreateNestable = jest.fn(async (config: Configuration) => {
  if (bridgeLine !== null) {
    config.system?.loggerOptions?.loggerCallback?.(
      2,
      `[Mon, 03 Nov 2026 14:00:00 GMT] : [] : @azure/msal-browser@3.30.0 : Info - Nested App Auth Bridge available: ${bridgeLine}`,
      false
    );
  }
  return naaInstance;
});
const mockPca = jest.fn().mockImplementation(() => pcaInstance);

jest.mock('@azure/msal-browser', () => ({
  createNestablePublicClientApplication: (config: Configuration) => mockCreateNestable(config),
  PublicClientApplication: function (this: unknown, config: Configuration) {
    return mockPca(config);
  },
}));

import { OfficeNaaStrategy } from '../src/strategies/OfficeNaaStrategy';

const baseConfig: Required<IAuthConfig> = {
  clientId: 'test-client-id',
  authority: 'https://login.microsoftonline.com/tenant-guid',
  redirectUri: 'http://localhost/',
  bffApiScope: 'api://bff-app-id/user_impersonation',
  bffBaseUrl: 'http://localhost/api',
  proactiveRefresh: false,
  requireXrm: false,
};

const FALLBACK = 'https://addins.example.com/auth-callback.html';

interface OfficeShape {
  host?: string;
  platform?: string;
  version?: string;
  /** undefined = no `requirements` object at all. */
  nestedAppAuth?: boolean;
}

function installOffice(shape: OfficeShape): jest.Mock {
  const isSetSupported = jest.fn((name: string, version: string) =>
    name === 'NestedAppAuth' && version === '1.1' ? shape.nestedAppAuth === true : false
  );
  (globalThis as unknown as { Office: unknown }).Office = {
    onReady: (cb: () => void) => cb(),
    PlatformType: { PC: 'PC', Mac: 'Mac', OfficeOnline: 'OfficeOnline' },
    HostType: { Outlook: 'Outlook', Word: 'Word' },
    context: {
      host: shape.host,
      diagnostics: { platform: shape.platform, version: shape.version ?? '16.0.19328.20000', host: shape.host },
      ...(shape.nestedAppAuth === undefined ? {} : { requirements: { isSetSupported } }),
    },
  };
  return isSetSupported;
}

async function start(options = {}): Promise<OfficeNaaStrategy> {
  const strategy = new OfficeNaaStrategy(baseConfig, { fallbackRedirectUri: FALLBACK, ...options });
  await strategy.acquire();
  return strategy;
}

describe('OfficeNaaStrategy — which MSAL client the host gets', () => {
  let saved: Pick<Console, 'warn' | 'info' | 'error'>;

  beforeEach(() => {
    jest.clearAllMocks();
    bridgeLine = 'true';
    naaInstance = makeInstance();
    pcaInstance = makeInstance();
    for (const instance of [naaInstance, pcaInstance]) {
      instance.acquireTokenSilent.mockResolvedValue({ accessToken: freshJwt(), expiresOn: null });
    }
    saved = { warn: console.warn, info: console.info, error: console.error };
    console.warn = jest.fn();
    console.info = jest.fn();
    console.error = jest.fn();
  });

  afterEach(() => {
    Object.assign(console, saved);
    delete (globalThis as unknown as { Office?: unknown }).Office;
  });

  it('new Outlook and Outlook on the web (OfficeOnline) use NAA when NestedAppAuth 1.1 is supported', async () => {
    const isSetSupported = installOffice({ host: 'Outlook', platform: 'OfficeOnline', nestedAppAuth: true });

    const strategy = await start();

    expect(isSetSupported).toHaveBeenCalledWith('NestedAppAuth', '1.1');
    expect(mockCreateNestable).toHaveBeenCalledTimes(1);
    expect(mockPca).not.toHaveBeenCalled();
    expect(strategy.isNaaActive()).toBe(true);
  });

  it('classic Outlook on Windows uses NAA when supported, whatever its build number', async () => {
    installOffice({ host: 'Outlook', platform: 'PC', version: '16.0.12000.20000', nestedAppAuth: true });

    const strategy = await start();

    expect(mockCreateNestable).toHaveBeenCalledTimes(1);
    expect(strategy.isNaaActive()).toBe(true);
  });

  it('the NAA config never carries a brk-multihub redirect: it is the https popup redirect', async () => {
    // Classic Windows, a host the old rules also sent to NAA, so this checks the redirect and not the detection.
    installOffice({ host: 'Outlook', platform: 'PC', version: '16.0.19328.20000', nestedAppAuth: true });
    const strategy = new OfficeNaaStrategy(
      { ...baseConfig, redirectUri: 'brk-multihub://addins.example.com' },
      { fallbackRedirectUri: FALLBACK }
    );

    await strategy.acquire();

    const config = mockCreateNestable.mock.calls[0]![0];
    expect(config.auth.redirectUri).toBe(FALLBACK);
    expect(config.auth.redirectUri).not.toMatch(/^brk-/);
  });

  it('NAA chosen but the bridge is not available: reports the standard client, not NAA', async () => {
    installOffice({ host: 'Outlook', platform: 'OfficeOnline', nestedAppAuth: true });
    bridgeLine = 'false';

    const strategy = await start();

    expect(mockCreateNestable).toHaveBeenCalledTimes(1);
    expect(strategy.isNaaActive()).toBe(false);
  });

  it('NAA chosen and MSAL logs nothing about the bridge: does not claim NAA', async () => {
    installOffice({ host: 'Outlook', platform: 'OfficeOnline', nestedAppAuth: true });
    bridgeLine = null;

    const strategy = await start();

    expect(mockCreateNestable).toHaveBeenCalledTimes(1);
    expect(strategy.isNaaActive()).toBe(false);
  });

  it('a failed NAA token request falls back once to the standard popup client, which answers', async () => {
    installOffice({ host: 'Outlook', platform: 'OfficeOnline', nestedAppAuth: true });
    naaInstance.acquireTokenSilent.mockRejectedValue(new Error('bridge: AADSTS700046'));
    naaInstance.acquireTokenPopup.mockRejectedValue(Object.assign(new Error('bridge'), { errorCode: 'server_error' }));
    const strategy = new OfficeNaaStrategy(baseConfig, { fallbackRedirectUri: FALLBACK });

    const token = await strategy.acquire();

    expect(token.accessToken).not.toBe('');
    expect(mockPca).toHaveBeenCalledTimes(1);
    expect(mockPca.mock.calls[0]![0].auth.redirectUri).toBe(FALLBACK);
    expect(pcaInstance.acquireTokenSilent).toHaveBeenCalledTimes(1);
    expect(strategy.isNaaActive()).toBe(false);

    // The switch lasts: the next acquisition goes straight to the standard client.
    naaInstance.acquireTokenSilent.mockClear();
    await strategy.acquire();
    expect(naaInstance.acquireTokenSilent).not.toHaveBeenCalled();
    expect(mockPca).toHaveBeenCalledTimes(1);
  });

  it('a user cancel on the NAA prompt is not answered with a second (popup) prompt', async () => {
    installOffice({ host: 'Outlook', platform: 'OfficeOnline', nestedAppAuth: true });
    naaInstance.acquireTokenSilent.mockRejectedValue(new Error('interaction required'));
    naaInstance.acquireTokenPopup.mockRejectedValue(
      Object.assign(new Error('cancelled'), { errorCode: 'user_cancelled' })
    );
    const strategy = new OfficeNaaStrategy(baseConfig, { fallbackRedirectUri: FALLBACK });

    const token = await strategy.acquire();

    expect(token.accessToken).toBe('');
    expect(mockPca).not.toHaveBeenCalled();
    expect(strategy.isNaaActive()).toBe(true);
  });

  it('the standard popup client failing is not retried (no fallback loop)', async () => {
    installOffice({ host: 'Outlook', platform: 'PC', nestedAppAuth: false });
    pcaInstance.acquireTokenSilent.mockRejectedValue(new Error('no session'));
    pcaInstance.acquireTokenPopup.mockRejectedValue(new Error('400 Request Too Long'));
    const strategy = new OfficeNaaStrategy(baseConfig, { fallbackRedirectUri: FALLBACK });

    const token = await strategy.acquire();

    expect(token.accessToken).toBe('');
    expect(mockPca).toHaveBeenCalledTimes(1);
    expect(pcaInstance.acquireTokenPopup).toHaveBeenCalledTimes(1);
  });

  it('a host without NestedAppAuth 1.1 gets the standard popup client with the https redirect', async () => {
    installOffice({ host: 'Outlook', platform: 'PC', nestedAppAuth: false });

    const strategy = await start();

    expect(mockCreateNestable).not.toHaveBeenCalled();
    expect(mockPca).toHaveBeenCalledTimes(1);
    expect(mockPca.mock.calls[0]![0].auth.redirectUri).toBe(FALLBACK);
    expect(strategy.isNaaActive()).toBe(false);
  });

  it('a host without the requirements API gets the standard popup client', async () => {
    installOffice({ host: 'Outlook', platform: 'OfficeOnline', nestedAppAuth: undefined });

    await start();

    expect(mockCreateNestable).not.toHaveBeenCalled();
    expect(mockPca).toHaveBeenCalledTimes(1);
  });

  it('Word on the web stays on the standard popup even when NestedAppAuth 1.1 is supported', async () => {
    installOffice({ host: 'Word', platform: 'OfficeOnline', nestedAppAuth: true });

    await start();

    expect(mockCreateNestable).not.toHaveBeenCalled();
    expect(mockPca).toHaveBeenCalledTimes(1);
  });

  it('Word on Windows uses NAA when supported', async () => {
    installOffice({ host: 'Word', platform: 'PC', nestedAppAuth: true });

    await start();

    expect(mockCreateNestable).toHaveBeenCalledTimes(1);
  });

  it('outside an Office host the standard popup client is used', async () => {
    await start();

    expect(mockCreateNestable).not.toHaveBeenCalled();
    expect(mockPca).toHaveBeenCalledTimes(1);
  });

  it('forceFallback skips detection', async () => {
    const isSetSupported = installOffice({ host: 'Outlook', platform: 'OfficeOnline', nestedAppAuth: true });

    await start({ forceFallback: true });

    expect(isSetSupported).not.toHaveBeenCalled();
    expect(mockCreateNestable).not.toHaveBeenCalled();
    expect(mockPca).toHaveBeenCalledTimes(1);
  });
});
