import { resolveConfig } from '../src/config';

describe('resolveConfig', () => {
  // Pre-v2 the package shipped hardcoded default clientId / bffApiScope as
  // dev fallbacks. v2 removed those defaults to force explicit configuration
  // via Dataverse env vars / runtime resolution (commit 9e480d75) — these
  // tests cover the post-v2 contract.

  it('throws when clientId is not configured', () => {
    expect(() => resolveConfig()).toThrow(/MSAL Client ID not configured/);
  });

  it('uses user-provided clientId; non-overridden fields take internal defaults', () => {
    const config = resolveConfig({ clientId: 'custom-id' });

    expect(config.clientId).toBe('custom-id');
    expect(config.bffApiScope).toBe(''); // No default — Dataverse env var supplies this in production
    expect(config.proactiveRefresh).toBe(false);
    expect(config.requireXrm).toBe(false);
    // Authority defaults to the fallback /organizations when no Xrm tenant available (test env)
    expect(config.authority).toBe('https://login.microsoftonline.com/organizations');
  });

  it('user-config overrides take precedence', () => {
    const config = resolveConfig({
      clientId: 'custom-id',
      proactiveRefresh: true,
      requireXrm: true,
    });

    expect(config.clientId).toBe('custom-id');
    expect(config.proactiveRefresh).toBe(true);
    expect(config.requireXrm).toBe(true);
  });

  it('reads clientId from window global when no user config supplied', () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any).__SPAARKE_MSAL_CLIENT_ID__ = 'from-window';
    try {
      const config = resolveConfig();
      expect(config.clientId).toBe('from-window');
    } finally {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      delete (window as any).__SPAARKE_MSAL_CLIENT_ID__;
    }
  });

  it('prefers user config over window global', () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any).__SPAARKE_MSAL_CLIENT_ID__ = 'from-window';
    try {
      const config = resolveConfig({ clientId: 'from-user' });
      expect(config.clientId).toBe('from-user');
    } finally {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      delete (window as any).__SPAARKE_MSAL_CLIENT_ID__;
    }
  });
});
