/**
 * authInit — PlaybookBuilder hands the BFF scope and the environment tenant to @spaarke/auth (#1453).
 *
 * Before: initializeAuth() passed neither, so a B2B guest signed in against their home tenant and
 * the token was not scoped to the BFF.
 */
import { initAuth } from '@spaarke/auth';
import type { IRuntimeConfig } from '@spaarke/auth';

jest.mock('@spaarke/auth', () => ({
  initAuth: jest.fn(async () => ({ getAccessToken: async () => 'token' })),
  getAuthProvider: jest.fn(),
  authenticatedFetch: jest.fn(),
  AuthError: class AuthError extends Error {},
}));
jest.mock('@spaarke/ui-components', () => ({ getXrm: jest.fn() }));

const initAuthMock = initAuth as unknown as jest.Mock;

const runtimeConfig: IRuntimeConfig = {
  bffBaseUrl: 'https://api.example.com',
  bffOAuthScope: 'api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation',
  msalClientId: '170c98e1-d486-4355-bcbe-170454e0207c',
  tenantId: 'a221a95e-6abc-4434-aecc-e48338a1b2f2',
} as IRuntimeConfig;

// The module keeps the recorded runtime config for the page's lifetime, so the
// "nothing recorded yet" case runs first.
describe('PlaybookBuilder initializeAuth', () => {
  beforeEach(() => initAuthMock.mockClear());

  it('still initialises (globals only) when no runtime config was recorded', async () => {
    const { initializeAuth } = await import('../authInit');
    await initializeAuth();
    expect(initAuthMock).toHaveBeenCalledWith({ proactiveRefresh: true });
  });

  it('passes client, BFF URL, scope and tenant from the runtime config', async () => {
    const { initializeAuth, setAuthRuntimeConfig } = await import('../authInit');
    setAuthRuntimeConfig(runtimeConfig);
    await initializeAuth();
    expect(initAuthMock).toHaveBeenCalledWith(
      expect.objectContaining({
        proactiveRefresh: true,
        clientId: runtimeConfig.msalClientId,
        bffBaseUrl: runtimeConfig.bffBaseUrl,
        bffApiScope: runtimeConfig.bffOAuthScope,
        tenantId: runtimeConfig.tenantId,
      })
    );
  });
});
