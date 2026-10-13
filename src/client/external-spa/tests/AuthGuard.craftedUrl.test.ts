// A crafted `#error=...` URL cannot force the sign-in failure screen (spaarke-SPA-external-access-platform-r3 task 056
// re-check). Uses the REAL msal-browser PublicClientApplication: handleRedirectPromise returns null (and emits no
// failure event) unless THIS tab has a redirect interaction in progress, so a link someone crafts does nothing.
import { afterEach, describe, expect, it } from 'vitest';

const CLIENT = '00000000-0000-0000-0000-000000000140';
const AUTHORITY = 'https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111';
const metadata = JSON.stringify({
  authorization_endpoint: `${AUTHORITY}/oauth2/v2.0/authorize`,
  token_endpoint: `${AUTHORITY}/oauth2/v2.0/token`,
  issuer: `${AUTHORITY}/v2.0`,
  end_session_endpoint: `${AUTHORITY}/oauth2/v2.0/logout`,
  jwks_uri: `${AUTHORITY}/discovery/v2.0/keys`,
});
const config = (onRedirectNavigate?: (url: string) => boolean) => ({
  auth: {
    clientId: CLIENT,
    authority: AUTHORITY,
    knownAuthorities: ['login.microsoftonline.com'],
    authorityMetadata: metadata,
    redirectUri: 'http://localhost/',
    onRedirectNavigate,
  },
  cache: { cacheLocation: 'sessionStorage' as const },
});

describe('crafted #error URL with no real MSAL redirect', () => {
  afterEach(() => {
    window.history.replaceState(null, '', '/');
    sessionStorage.clear();
  });

  it('records no sign-in failure', async () => {
    const msal = await import('@azure/msal-browser');
    const msalAuth = await import('../src/auth/msal-auth');
    window.history.replaceState(
      null,
      '',
      '/#error=access_denied&error_description=AADSTS65004%3A%20crafted&state=forged'
    );
    const instance = new msal.PublicClientApplication(config());
    const seen: string[] = [];
    instance.addEventCallback(e => seen.push(`${e.eventType}:${e.interactionType ?? ''}`));
    msalAuth.syncActiveAccountOnInteractiveSignIn(instance);
    await instance.initialize();

    await expect(instance.handleRedirectPromise()).resolves.toBeNull();

    expect(msalAuth.getSignInFailure(instance)).toBeNull();
    expect(seen.filter(s => s.startsWith('msal:acquireTokenFailure'))).toEqual([]);
  }, 60000);

  // Positive control: the same hash IS honoured when this tab really started a redirect (so the test above would catch a
  // gate that could be forced), and what is recorded is the safe code only.
  it('control: a real redirect that comes back with an error records the safe code only', async () => {
    const msal = await import('@azure/msal-browser');
    const msalAuth = await import('../src/auth/msal-auth');
    let authorizeUrl = '';
    const starter = new msal.PublicClientApplication(
      config(url => {
        authorizeUrl = url;
        return false; // do not navigate
      })
    );
    await starter.initialize();
    await starter.loginRedirect({ scopes: ['api://bff.test/user_impersonation'] });
    const state = new URL(authorizeUrl).searchParams.get('state');
    expect(state).toBeTruthy();

    // The page the IdP returns to: a fresh instance over the same sessionStorage.
    window.history.replaceState(
      null,
      '',
      `/#error=access_denied&error_description=AADSTS65004%3A%20User%20declined%20jane.doe%40contoso.example&state=${encodeURIComponent(state!)}`
    );
    const returning = new msal.PublicClientApplication(config());
    msalAuth.syncActiveAccountOnInteractiveSignIn(returning);
    await returning.initialize();
    await expect(returning.handleRedirectPromise()).rejects.toBeTruthy();

    expect(msalAuth.getSignInFailure(returning)).toEqual({ errorCode: 'access_denied', aadsts: 'AADSTS65004' });
    expect(JSON.stringify(Object.entries(sessionStorage))).not.toContain('jane.doe');
  }, 60000);
});
