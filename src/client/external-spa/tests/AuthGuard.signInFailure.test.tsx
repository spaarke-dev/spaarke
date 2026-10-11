// AuthGuard's sign-in gate after a FAILED redirect sign-in (spaarke-SPA-external-access-platform-r3 task 056 review fix).
//
// A new guest who declines Entra's "Review permissions" page comes back to the redirect URI with `#error=access_denied`
// (AADSTS65004). msal-browser rejects handleRedirectPromise and emits ACQUIRE_TOKEN_FAILURE for a redirect interaction. The gate used to start the redirect again on its own whenever there was no account and nothing in
// progress, so the SPA bounced between Entra and itself forever. These tests drive the real AuthGuard, the real
// msal-auth event callback and the real failure screen; only `useMsal` is replaced, and the fake instance records every
// loginRedirect.
import * as React from 'react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const h = vi.hoisted(() => ({
  loginRedirect: vi.fn(),
  inProgress: 'none',
  accounts: [] as unknown[],
  // The MSAL instance the mounted gate is bound to (`useMsal().instance`); load() sets it.
  instance: null as unknown,
}));

vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({
    instance: h.instance,
    accounts: h.accounts,
    inProgress: h.inProgress,
  }),
}));

// The first dynamic import of the app tree is slow on a cold transform cache (as in main-bootstrap.test.tsx).
vi.setConfig({ testTimeout: 60000 });

const SCOPE = 'api://aaaaaaaa-0000-0000-0000-00000000000a/user_impersonation';
const PII = 'jane.doe@contoso.example';

beforeEach(() => {
  h.loginRedirect.mockReset();
  h.loginRedirect.mockResolvedValue(undefined);
  h.instance = null;
  h.inProgress = 'none';
  h.accounts = [];
  sessionStorage.clear();
  vi.spyOn(console, 'warn').mockImplementation(() => undefined);
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
});

afterEach(() => {
  vi.restoreAllMocks();
});

/** Fresh modules, the real event callback registered on a fake instance, and the gate ready to render. */
async function load() {
  vi.resetModules();
  const msal = await import('@azure/msal-browser');
  const msalAuth = await import('../src/auth/msal-auth');
  const { AuthGuard } = await import('../src/components/AuthGuard');
  msalAuth.setActiveLoginScope(SCOPE);
  // The bootstrap registers this exact callback before initialize(); capture it the way MSAL would. The same object is
  // what `useMsal()` hands the gate, as in the app (one MSAL instance per plane).
  const makeInstance = () => {
    const callbacks: Array<(event: unknown) => void> = [];
    const instance = {
      loginRedirect: h.loginRedirect,
      addEventCallback: (cb: (event: unknown) => void) => {
        callbacks.push(cb);
        return 'cb';
      },
      setActiveAccount: () => undefined,
    };
    msalAuth.syncActiveAccountOnInteractiveSignIn(instance as never);
    return { instance, emitOn: (event: Record<string, unknown>) => act(() => callbacks.forEach(cb => cb(event))) };
  };
  const first = makeInstance();
  h.instance = first.instance;
  const emit = first.emitOn;
  const view = () => (
    <FluentProvider theme={webLightTheme}>
      <MemoryRouter>
        <AuthGuard>
          <div data-testid="app">app</div>
        </AuthGuard>
      </MemoryRouter>
    </FluentProvider>
  );
  return { msal, msalAuth, emit, view, makeInstance };
}

function failureEvent(
  msal: typeof import('@azure/msal-browser'),
  eventType: string,
  over: Record<string, unknown> = {}
) {
  return {
    eventType,
    interactionType: msal.InteractionType.Redirect,
    payload: null,
    timestamp: 0,
    error: {
      name: 'ServerError',
      errorCode: 'access_denied',
      errorMessage: `AADSTS65004: User declined to consent to access the app. Trace ID: abc. Account ${PII}`,
      message: `access_denied: AADSTS65004: User declined to consent. ${PII}`,
    },
    ...over,
  };
}

describe('Sign-in gate: normal path', () => {
  it('no account and nothing in progress: exactly one loginRedirect for the active scope', async () => {
    const { view } = await load();
    render(view());

    await waitFor(() => expect(h.loginRedirect).toHaveBeenCalledTimes(1));
    expect(h.loginRedirect).toHaveBeenCalledWith({ scopes: [SCOPE] });
    expect(screen.queryByTestId('signin-failed')).toBeNull();
  });
});

describe('Sign-in gate: a failed redirect sign-in', () => {
  it('ACQUIRE_TOKEN_FAILURE of a redirect: the failure screen shows and the gate starts NO redirect', async () => {
    {
      const { msal, emit, view } = await load();
      await emit(failureEvent(msal, msal.EventType.ACQUIRE_TOKEN_FAILURE));
      render(view());

      expect(await screen.findByTestId('signin-failed')).toBeTruthy();
      expect(screen.getByRole('heading', { level: 1, name: 'Sign-in did not complete' })).toBeTruthy();
      // Give a (buggy) effect every chance to fire.
      await act(async () => {
        await new Promise(r => setTimeout(r, 20));
      });
      expect(h.loginRedirect).not.toHaveBeenCalled();
      expect(screen.queryByTestId('app')).toBeNull();
    }
  });

  it('shows the safe error code only: never the raw description (it can carry an address or a trace id)', async () => {
    const { msal, emit, view } = await load();
    await emit(failureEvent(msal, msal.EventType.ACQUIRE_TOKEN_FAILURE));
    render(view());

    expect((await screen.findByTestId('signin-failed-code')).textContent).toBe(
      'Error code: access_denied (AADSTS65004)'
    );
    const text = document.body.textContent ?? '';
    expect(text).not.toContain(PII);
    expect(text).not.toContain('Trace ID');
    expect(text).not.toContain('declined to consent');
  });

  it('an error code that is not a plain identifier is replaced, never shown', async () => {
    const { msal, emit, view } = await load();
    await emit(
      failureEvent(msal, msal.EventType.ACQUIRE_TOKEN_FAILURE, { error: { errorCode: `x ${PII}`, errorMessage: '' } })
    );
    render(view());

    expect((await screen.findByTestId('signin-failed-code')).textContent).toBe('Error code: unknown_error');
    expect(document.body.textContent).not.toContain(PII);
  });

  it('"Try again" starts exactly one loginRedirect, and only on the click', async () => {
    const { msal, emit, view } = await load();
    await emit(failureEvent(msal, msal.EventType.ACQUIRE_TOKEN_FAILURE));
    render(view());
    const button = await screen.findByRole('button', { name: 'Try again' });
    expect(h.loginRedirect).not.toHaveBeenCalled();

    fireEvent.click(button);

    await waitFor(() => expect(h.loginRedirect).toHaveBeenCalledTimes(1));
    expect(h.loginRedirect).toHaveBeenCalledWith({ scopes: [SCOPE] });
    // The screen stays up while the redirect starts; the gate does not add a second redirect of its own.
    await act(async () => {
      await new Promise(r => setTimeout(r, 20));
    });
    expect(h.loginRedirect).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId('signin-failed')).toBeTruthy();
  });

  it('"Try again" that cannot start says so instead of doing nothing', async () => {
    const { msal, emit, view } = await load();
    h.loginRedirect.mockRejectedValue(new Error('network'));
    await emit(failureEvent(msal, msal.EventType.ACQUIRE_TOKEN_FAILURE));
    render(view());

    fireEvent.click(await screen.findByRole('button', { name: 'Try again' }));

    expect((await screen.findByTestId('signin-failed-start-error')).textContent).toMatch(/could not be started/);
  });

  it('the failure arrives while MSAL is still handling the redirect: spinner, then the failure screen — no redirect', async () => {
    h.inProgress = 'handleRedirect';
    const { msal, emit, view } = await load();
    const { rerender } = render(view());
    expect(screen.getByText('Signing in...')).toBeTruthy();

    await emit(failureEvent(msal, msal.EventType.ACQUIRE_TOKEN_FAILURE));
    h.inProgress = 'none';
    rerender(view());

    expect(await screen.findByTestId('signin-failed')).toBeTruthy();
    await act(async () => {
      await new Promise(r => setTimeout(r, 20));
    });
    expect(h.loginRedirect).not.toHaveBeenCalled();
  });

  it('events that are not failures (a redirect ending, a login success) record nothing', async () => {
    const { msal, msalAuth, emit } = await load();
    for (const eventType of [msal.EventType.HANDLE_REDIRECT_END, msal.EventType.LOGIN_SUCCESS]) {
      await emit({
        eventType,
        interactionType: msal.InteractionType.Redirect,
        payload: null,
        error: null,
        timestamp: 0,
      });
    }
    expect(msalAuth.getSignInFailure()).toBeNull();
  });

  it('a later successful interactive sign-in clears the failure', async () => {
    const { msal, msalAuth, emit } = await load();
    await emit(failureEvent(msal, msal.EventType.ACQUIRE_TOKEN_FAILURE));
    expect(msalAuth.getSignInFailure()).toEqual({ errorCode: 'access_denied', aadsts: 'AADSTS65004' });

    await emit({
      eventType: msal.EventType.LOGIN_SUCCESS,
      interactionType: msal.InteractionType.Redirect,
      payload: { account: { homeAccountId: 'a' } },
      error: null,
      timestamp: 0,
    });
    expect(msalAuth.getSignInFailure()).toBeNull();
  });

  it.each([
    ['interaction_in_progress (another redirect is already running)', 'redirect', 'interaction_in_progress'],
    ['a silent failure', 'silent', 'access_denied'],
    ['a popup failure', 'popup', 'access_denied'],
  ])('%s is not a failed redirect sign-in', async (_label, interaction, errorCode) => {
    const { msal, msalAuth, emit, view } = await load();
    const interactionType = {
      redirect: msal.InteractionType.Redirect,
      silent: msal.InteractionType.Silent,
      popup: msal.InteractionType.Popup,
    }[interaction as 'redirect' | 'silent' | 'popup'];
    await emit(failureEvent(msal, msal.EventType.ACQUIRE_TOKEN_FAILURE, { interactionType, error: { errorCode } }));
    expect(msalAuth.getSignInFailure()).toBeNull();
    render(view());

    await waitFor(() => expect(h.loginRedirect).toHaveBeenCalledTimes(1));
    expect(screen.queryByTestId('signin-failed')).toBeNull();
  });
});

describe('Sign-in gate: the automatic redirect is rejected (no spinner that never ends)', () => {
  const settle = () =>
    act(async () => {
      await new Promise(r => setTimeout(r, 20));
    });

  it('a network failure starting the redirect shows the failure screen with its retry, and does not retry on its own', async () => {
    const { view } = await load();
    h.loginRedirect.mockRejectedValue(
      Object.assign(new Error(`fetch failed for ${PII}`), { errorCode: 'endpoints_resolution_error' })
    );
    render(view());

    expect(await screen.findByTestId('signin-failed')).toBeTruthy();
    expect(screen.queryByText('Redirecting to sign-in...')).toBeNull();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeTruthy();
    await settle();
    expect(h.loginRedirect).toHaveBeenCalledTimes(1);
    // Safe code only: the message (which carries an address here) never reaches the DOM.
    expect(screen.getByTestId('signin-failed-code').textContent).toBe('Error code: endpoints_resolution_error');
    expect(document.body.textContent).not.toContain(PII);
  });

  it('interaction_in_progress rejects the start: the failure screen shows and nothing loops', async () => {
    const { view } = await load();
    h.loginRedirect.mockRejectedValue(
      Object.assign(new Error('Interaction is currently in progress.'), { errorCode: 'interaction_in_progress' })
    );
    render(view());

    expect(await screen.findByTestId('signin-failed')).toBeTruthy();
    await settle();
    expect(h.loginRedirect).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId('signin-failed-code').textContent).toBe('Error code: interaction_in_progress');

    // "Try again" is a user gesture: one more attempt, which reports honestly when it cannot start either.
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect((await screen.findByTestId('signin-failed-start-error')).textContent).toMatch(/could not be started/);
    await settle();
    expect(h.loginRedirect).toHaveBeenCalledTimes(2);
  });

  it('a redirect that throws synchronously (e.g. no active backend) shows the failure screen instead of crashing', async () => {
    const { view } = await load();
    h.loginRedirect.mockImplementation(() => {
      throw new Error('No active backend');
    });
    render(view());

    expect(await screen.findByTestId('signin-failed')).toBeTruthy();
    expect(screen.getByTestId('signin-failed-code').textContent).toBe('Error code: unknown_error');
    await settle();
    expect(h.loginRedirect).toHaveBeenCalledTimes(1);
  });

  it('a redirect that starts normally (never resolves, the page navigates away) keeps the spinner', async () => {
    const { view } = await load();
    h.loginRedirect.mockReturnValue(new Promise(() => undefined));
    render(view());

    expect(await screen.findByText('Redirecting to sign-in...')).toBeTruthy();
    await settle();
    expect(screen.queryByTestId('signin-failed')).toBeNull();
    expect(h.loginRedirect).toHaveBeenCalledTimes(1);
  });
});

describe('Sign-in gate: a failure is scoped and short-lived', () => {
  it('a failure recorded for one plane does not block the gate of the other plane', async () => {
    const { msal, msalAuth, emit, view, makeInstance } = await load();
    // Plane A (the first instance) failed its redirect sign-in.
    await emit(failureEvent(msal, msal.EventType.ACQUIRE_TOKEN_FAILURE));
    expect(msalAuth.getSignInFailure(h.instance as never)).not.toBeNull();
    // The user is now on plane B, which has its own MSAL instance and no failure.
    h.instance = makeInstance().instance;
    expect(msalAuth.getSignInFailure(h.instance as never)).toBeNull();
    render(view());

    await waitFor(() => expect(h.loginRedirect).toHaveBeenCalledTimes(1));
    expect(screen.queryByTestId('signin-failed')).toBeNull();
  });

  it('a signed-in gate drops a stale failure (e.g. a token redirect that failed for a working session)', async () => {
    const { msal, msalAuth, emit, view } = await load();
    await emit(failureEvent(msal, msal.EventType.ACQUIRE_TOKEN_FAILURE));
    h.accounts = [{ homeAccountId: 'a', tenantId: 't' }];
    render(view());

    // Authenticated: children render, never the failure screen, and the failure is forgotten.
    expect(await screen.findByTestId('app')).toBeTruthy();
    expect(screen.queryByTestId('signin-failed')).toBeNull();
    await waitFor(() => expect(msalAuth.getSignInFailure()).toBeNull());
    expect(h.loginRedirect).not.toHaveBeenCalled();
  });
});

describe('Teams', () => {
  it('never reaches the gate: children render and nothing redirects, even with a recorded failure', async () => {
    const { msal, emit } = await load();
    await emit(failureEvent(msal, msal.EventType.ACQUIRE_TOKEN_FAILURE));
    const { AuthGuard } = await import('../src/components/AuthGuard');
    render(
      <FluentProvider theme={webLightTheme}>
        <MemoryRouter>
          <AuthGuard teamsHost>
            <div data-testid="app">app</div>
          </AuthGuard>
        </MemoryRouter>
      </FluentProvider>
    );

    expect(screen.getByTestId('app')).toBeTruthy();
    expect(screen.queryByTestId('signin-failed')).toBeNull();
    expect(h.loginRedirect).not.toHaveBeenCalled();
  });
});
