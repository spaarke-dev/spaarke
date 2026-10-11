// App provides its `teamsHost` to every BffAccessState below it (spaarke-SPA-external-access-platform-r3 task 051 review,
// NFR-04). The real App runs; the route page is a stub that renders BffAccessState with NO host prop, exactly as a
// registry-built widget body would. Teams: "Sign in again" reloads and never calls loginRedirect. Browser: it redirects.
import * as React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';

const h = vi.hoisted(() => ({ loginRedirect: vi.fn() }));

vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({ accounts: [], instance: { loginRedirect: h.loginRedirect }, inProgress: 'none' }),
}));
vi.mock('../src/components/AuthGuard', () => ({
  AuthGuard: ({ children }: { children: React.ReactNode }) => children,
}));
vi.mock('../src/pages/WorkspaceHomePage', async () => {
  const { BffAccessState } = await import('../src/components/auth/BffAccessState');
  return { WorkspaceHomePage: () => React.createElement(BffAccessState, { failure: 'signin' }) };
});
vi.mock('../src/pages/ProjectPage', () => ({ ProjectPage: () => null }));
vi.mock('../src/pages/PlaybookLibraryPage', () => ({ PlaybookLibraryPage: () => null }));
vi.mock('../src/pages/DocumentUploadPage', () => ({ DocumentUploadPage: () => null }));
vi.mock('../src/pages/SettingsPage', () => ({ SettingsPage: () => null }));

const SCOPE = 'api://aaaaaaaa-0000-0000-0000-00000000000a/user_impersonation';

beforeEach(() => {
  h.loginRedirect.mockReset();
  h.loginRedirect.mockResolvedValue(undefined);
  sessionStorage.clear();
});

async function clickSignInAgain(teamsHost: boolean): Promise<ReturnType<typeof vi.fn>> {
  const msalAuth = await import('../src/auth/msal-auth');
  msalAuth.setActiveLoginScope(SCOPE);
  const { App } = await import('../src/App');
  const reload = vi.fn();
  const realLocation = window.location;
  Object.defineProperty(window, 'location', { configurable: true, value: { ...realLocation, reload } });
  try {
    render(React.createElement(App, { teamsHost }));
    fireEvent.click(await screen.findByRole('button', { name: 'Sign in again' }));
  } finally {
    Object.defineProperty(window, 'location', { configurable: true, value: realLocation });
  }
  return reload;
}

describe('App -> BffAccessState host signal', () => {
  it('Teams: reloads, never redirects', { timeout: 60000 }, async () => {
    const reload = await clickSignInAgain(true);
    expect(reload).toHaveBeenCalledTimes(1);
    expect(h.loginRedirect).not.toHaveBeenCalled();
  });

  it('browser: redirects, never reloads', { timeout: 60000 }, async () => {
    const reload = await clickSignInAgain(false);
    expect(h.loginRedirect).toHaveBeenCalledTimes(1);
    expect(reload).not.toHaveBeenCalled();
  });
});
