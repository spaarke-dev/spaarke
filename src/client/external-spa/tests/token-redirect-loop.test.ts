// A failed token redirect must not loop (spaarke-SPA-external-access-platform-r3 task 056 re-check, known limit fixed).
//
// A signed-in user whose refresh token expired reaches `acquireStandaloneBffToken` -> `acquireTokenRedirect`. If they
// cancel the prompt / MFA or decline consent, Entra returns an error to the redirect URI; the app stays up and the NEXT
// BFF call used to start the same redirect again, indefinitely. The failed redirect is now remembered for the page load
// (the same per-instance record the sign-in gate uses, fed by the same MSAL event callback): while it is set, the token
// path starts no redirect and answers with the `signin` access state. Success clears it.
//
// MSAL is a fake that records redirects and the callbacks the code registers; event names, interaction types and
// InteractionRequiredAuthError are msal-browser's real ones.
import { beforeEach, describe, expect, it, vi } from 'vitest';

// The first dynamic import of the app tree (BffAccessState) is slow on a cold transform cache.
vi.setConfig({ testTimeout: 60000 });

const BFF_SCOPE = 'api://99999999-8888-7777-6666-555555555555/user_impersonation';
const PII = 'jane.doe@contoso.example';

vi.mock('@azure/msal-browser', async importOriginal => {
  const actual = await importOriginal<typeof import('@azure/msal-browser')>();
  class FakePublicClientApplication {}
  return { ...actual, PublicClientApplication: FakePublicClientApplication };
});
vi.mock('@microsoft/teams-js', () => ({
  app: { initialize: vi.fn() },
  nestedAppAuth: { isNAAChannelRecommended: vi.fn(() => true) },
}));

const account = {
  homeAccountId: 'A',
  username: 'a@test',
  tenantId: 't',
  environment: 'login.microsoftonline.com',
  localAccountId: 'A',
};

/** A fake instance whose silent acquisition is controllable; its event callbacks are the ones the code registers. */
function fakeInstance(cached: unknown[] = [account]) {
  const callbacks: Array<(event: any) => void> = [];
  return {
    getAllAccounts: vi.fn(() => cached),
    getActiveAccount: vi.fn(() => null),
    setActiveAccount: vi.fn(),
    acquireTokenSilent: vi.fn(),
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
  const msal = await import('@azure/msal-browser');
  const msalAuth = await import('../src/auth/msal-auth');
  const { accessFailureOf } = await import('../src/components/auth/BffAccessState');
  const interactionRequired = () => new msal.InteractionRequiredAuthError('interaction_required');
  /** The failure MSAL reports for a redirect whose response was an error (declined consent / cancelled prompt). */
  const failedRedirect = (instance: any, over: Record<string, unknown> = {}) =>
    instance.emit({
      eventType: msal.EventType.ACQUIRE_TOKEN_FAILURE,
      interactionType: msal.InteractionType.Redirect,
      payload: null,
      timestamp: 0,
      error: {
        errorCode: 'access_denied',
        errorMessage: `AADSTS65004: User declined to consent. ${PII}`,
        message: `access_denied: ${PII}`,
      },
      ...over,
    });
  return { msal, msalAuth, accessFailureOf, interactionRequired, failedRedirect };
}

beforeEach(() => {
  vi.clearAllMocks();
  sessionStorage.clear();
});

describe('token redirect for a signed-in user', () => {
  it('expired silent token and no recorded failure: redirects exactly once (unchanged)', async () => {
    const { msalAuth, interactionRequired } = await load();
    const instance = fakeInstance();
    msalAuth.syncActiveAccountOnInteractiveSignIn(instance);
    instance.acquireTokenSilent.mockRejectedValue(interactionRequired());

    await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE)).rejects.toThrow(/redirecting to login/);

    expect(instance.acquireTokenRedirect).toHaveBeenCalledTimes(1);
    expect(instance.acquireTokenRedirect.mock.calls[0][0]).toMatchObject({ scopes: [BFF_SCOPE], account });
  });

  it('after a FAILED token redirect, the next BFF call starts no second redirect and yields the signin state', async () => {
    const { msalAuth, accessFailureOf, interactionRequired, failedRedirect } = await load();
    const instance = fakeInstance();
    msalAuth.syncActiveAccountOnInteractiveSignIn(instance);
    instance.acquireTokenSilent.mockRejectedValue(interactionRequired());

    // First call: a normal redirect (it navigates away; the tab comes back with Entra's error).
    await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE)).rejects.toThrow(/redirecting to login/);
    expect(instance.acquireTokenRedirect).toHaveBeenCalledTimes(1);
    failedRedirect(instance);

    // The app stays up and makes more BFF calls (several, as a page does): none redirects again.
    for (let i = 0; i < 3; i++) {
      const error = await msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE).catch(e => e);
      expect(accessFailureOf(error)).toBe('signin');
    }
    // The 401 retry's forced refresh takes the same path.
    const retry = await msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE, { forceRefresh: true }).catch(e => e);
    expect(accessFailureOf(retry)).toBe('signin');
    expect(instance.acquireTokenRedirect).toHaveBeenCalledTimes(1);
  });

  it('the signin error carries the safe code only, never the description', async () => {
    const { msalAuth, interactionRequired, failedRedirect } = await load();
    const instance = fakeInstance();
    msalAuth.syncActiveAccountOnInteractiveSignIn(instance);
    instance.acquireTokenSilent.mockRejectedValue(interactionRequired());
    failedRedirect(instance);

    const error = await msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE).catch(e => e);

    expect(error.message).toContain('access_denied');
    expect(error.message).not.toContain(PII);
    expect(JSON.stringify(error.failure)).not.toContain(PII);
  });

  it('with no account at all, a recorded failure also stops the sign-in redirect', async () => {
    const { msalAuth, accessFailureOf, failedRedirect } = await load();
    const instance = fakeInstance([]);
    msalAuth.syncActiveAccountOnInteractiveSignIn(instance);
    failedRedirect(instance);

    const error = await msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE).catch(e => e);

    expect(accessFailureOf(error)).toBe('signin');
    expect(instance.acquireTokenRedirect).not.toHaveBeenCalled();
  });

  it('a failure recorded for another instance (plane) does not stop this one from redirecting', async () => {
    const { msalAuth, interactionRequired, failedRedirect } = await load();
    const other = fakeInstance();
    const instance = fakeInstance();
    msalAuth.syncActiveAccountOnInteractiveSignIn(other);
    msalAuth.syncActiveAccountOnInteractiveSignIn(instance);
    instance.acquireTokenSilent.mockRejectedValue(interactionRequired());
    failedRedirect(other);

    await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE)).rejects.toThrow(/redirecting to login/);

    expect(instance.acquireTokenRedirect).toHaveBeenCalledTimes(1);
  });

  it('a silent token that works again clears the record, so a later expiry redirects once more', async () => {
    const { msalAuth, interactionRequired, failedRedirect } = await load();
    const instance = fakeInstance();
    msalAuth.syncActiveAccountOnInteractiveSignIn(instance);
    failedRedirect(instance);
    expect(msalAuth.getSignInFailure(instance)).not.toBeNull();

    // Silent acquisition is still tried first, and succeeds (e.g. the cache recovered).
    instance.acquireTokenSilent.mockResolvedValueOnce({ accessToken: 'tok' });
    await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE)).resolves.toBe('tok');
    expect(msalAuth.getSignInFailure(instance)).toBeNull();

    instance.acquireTokenSilent.mockRejectedValue(interactionRequired());
    await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE)).rejects.toThrow(/redirecting to login/);
    expect(instance.acquireTokenRedirect).toHaveBeenCalledTimes(1);
  });

  it('a successful interactive sign-in clears the record', async () => {
    const { msal, msalAuth, failedRedirect } = await load();
    const instance = fakeInstance();
    msalAuth.syncActiveAccountOnInteractiveSignIn(instance);
    failedRedirect(instance);
    expect(msalAuth.getSignInFailure(instance)).not.toBeNull();

    instance.emit({
      eventType: msal.EventType.ACQUIRE_TOKEN_SUCCESS,
      interactionType: msal.InteractionType.Redirect,
      payload: { account },
      error: null,
      timestamp: 0,
    });

    expect(msalAuth.getSignInFailure(instance)).toBeNull();
  });

  it('interaction_in_progress is not a failure: it does not stop later redirects', async () => {
    const { msalAuth, interactionRequired, failedRedirect } = await load();
    const instance = fakeInstance();
    msalAuth.syncActiveAccountOnInteractiveSignIn(instance);
    instance.acquireTokenSilent.mockRejectedValue(interactionRequired());
    failedRedirect(instance, { error: { errorCode: 'interaction_in_progress', errorMessage: '' } });

    await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE)).rejects.toThrow(/redirecting to login/);
    expect(instance.acquireTokenRedirect).toHaveBeenCalledTimes(1);
  });
});
