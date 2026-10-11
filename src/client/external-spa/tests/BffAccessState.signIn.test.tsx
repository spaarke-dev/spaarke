// BffAccessState's "Sign in again" (spaarke-SPA-external-access-platform-r3 task 042 review, FR-18, #1566): it asks the
// identity provider to pick the account (the cached session's token was just refused), and a sign-in that cannot be
// started says so instead of leaving a button that does nothing.
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';

const h = vi.hoisted(() => ({ loginRedirect: vi.fn() }));

vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({ instance: { loginRedirect: h.loginRedirect }, accounts: [], inProgress: 'none' }),
}));

const SCOPE = 'api://aaaaaaaa-0000-0000-0000-00000000000a/user_impersonation';

beforeEach(() => {
  h.loginRedirect.mockReset();
  h.loginRedirect.mockResolvedValue(undefined);
  sessionStorage.clear();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

async function load() {
  vi.resetModules();
  const msalAuth = await import('../src/auth/msal-auth');
  const { BffAccessState } = await import('../src/components/auth/BffAccessState');
  return { msalAuth, BffAccessState };
}

describe('BffAccessState "Sign in again"', () => {
  it('the browser redirect asks the identity provider to pick the account', async () => {
    const { msalAuth, BffAccessState } = await load();
    msalAuth.setActiveLoginScope(SCOPE);
    render(React.createElement(BffAccessState, { failure: 'signin' }));

    fireEvent.click(screen.getByRole('button', { name: 'Sign in again' }));

    expect(h.loginRedirect).toHaveBeenCalledTimes(1);
    expect(h.loginRedirect).toHaveBeenCalledWith({ scopes: [SCOPE], prompt: 'select_account' });
    expect(screen.queryByTestId('bff-access-signin-error')).toBeNull();
  });

  it('getActiveLoginScope throws BackendSelectionError: the bar says sign-in is unavailable and nothing is redirected', async () => {
    const { BffAccessState } = await load(); // no login scope set, and the active backend is of the other plane
    const selection = await import('../src/auth/backend-selection');
    selection.activateBackend('workforce', {
      customerKey: 'contoso',
      displayName: 'Contoso',
      bffBaseUrl: 'https://contoso-bff.test',
      bffAppId: 'aaaaaaaa-0000-0000-0000-00000000000a',
    });
    render(React.createElement(BffAccessState, { failure: 'signin' }));

    fireEvent.click(screen.getByRole('button', { name: 'Sign in again' }));

    expect(await screen.findByTestId('bff-access-signin-error')).toBeTruthy();
    expect(screen.getByTestId('bff-access-signin-error').textContent).toMatch(/Sign-in could not be started/);
    expect(h.loginRedirect).not.toHaveBeenCalled();
  });

  it('MSAL refusing the redirect shows the same message', async () => {
    const { msalAuth, BffAccessState } = await load();
    msalAuth.setActiveLoginScope(SCOPE);
    h.loginRedirect.mockRejectedValue(new Error('interaction_in_progress'));
    render(React.createElement(BffAccessState, { failure: 'signin' }));

    fireEvent.click(screen.getByRole('button', { name: 'Sign in again' }));

    await waitFor(() => expect(screen.getByTestId('bff-access-signin-error')).toBeTruthy());
  });

  it('a later successful attempt clears the message', async () => {
    const { msalAuth, BffAccessState } = await load();
    const selection = await import('../src/auth/backend-selection');
    selection.activateBackend('workforce', {
      customerKey: 'contoso',
      displayName: 'Contoso',
      bffBaseUrl: 'https://contoso-bff.test',
      bffAppId: 'aaaaaaaa-0000-0000-0000-00000000000a',
    });
    render(React.createElement(BffAccessState, { failure: 'signin' }));
    fireEvent.click(screen.getByRole('button', { name: 'Sign in again' }));
    expect(await screen.findByTestId('bff-access-signin-error')).toBeTruthy();

    msalAuth.setActiveLoginScope(SCOPE);
    fireEvent.click(screen.getByRole('button', { name: 'Sign in again' }));
    await waitFor(() => expect(screen.queryByTestId('bff-access-signin-error')).toBeNull());
    expect(h.loginRedirect).toHaveBeenCalledTimes(1);
  });
});

describe('accessFailureOf', () => {
  it('maps 401 and a failed Teams token acquisition to sign-in, 403 to denied, everything else to null', async () => {
    vi.resetModules();
    const { accessFailureOf } = await import('../src/components/auth/BffAccessState');
    const { ApiError } = await import('../src/types');
    const { TeamsWorkforceAuthError } = await import('../src/auth/msal-auth');
    const { BackendSelectionError } = await import('../src/auth/backend-selection');

    expect(accessFailureOf(new ApiError(401, ''))).toBe('signin');
    expect(accessFailureOf(new TeamsWorkforceAuthError('popup blocked'))).toBe('signin');
    expect(accessFailureOf(new ApiError(403, ''))).toBe('denied');
    expect(accessFailureOf(new ApiError(500, ''))).toBeNull();
    expect(accessFailureOf(new BackendSelectionError('none'))).toBeNull();
    expect(accessFailureOf(new TypeError('Failed to fetch'))).toBeNull();
    expect(accessFailureOf(undefined)).toBeNull();
  });
});
