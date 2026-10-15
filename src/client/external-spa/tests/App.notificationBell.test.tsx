// Where the bell and the deep link are wired in the app shell (spaarke-SPA-external-access-platform-r3 task 055).
// The real App, AppHeader, RecordOpenerProvider and RecordDeepLink run. Stubs: MSAL's useMsal (accounts / inProgress are
// the test's lever), the auth gate and the route pages, the bell itself (its behaviour is NotificationBell.test.tsx) and
// the detail view (a recorder).
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';

const h = vi.hoisted(() => ({
  msal: { accounts: [] as Array<Record<string, string>>, inProgress: 'none' },
  bellMounts: { count: 0 },
}));

vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({ accounts: h.msal.accounts, instance: {}, inProgress: h.msal.inProgress }),
}));
vi.mock('../src/components/AuthGuard', () => ({
  AuthGuard: ({ children }: { children: React.ReactNode }) => children,
}));
vi.mock('../src/pages/WorkspaceHomePage', () => ({ WorkspaceHomePage: () => null }));
vi.mock('../src/pages/ProjectPage', () => ({ ProjectPage: () => null }));
vi.mock('../src/pages/DocumentUploadPage', () => ({ DocumentUploadPage: () => null }));
vi.mock('../src/pages/SettingsPage', () => ({ SettingsPage: () => null }));
vi.mock('../src/components/shell/NotificationBell', async () => {
  const R = await import('react');
  return {
    NotificationBell: () => {
      R.useEffect(() => {
        h.bellMounts.count += 1;
      }, []);
      return R.createElement('button', { 'data-testid': 'bell-stub' }, 'bell');
    },
  };
});
vi.mock('../src/components/detail/RecordDetailModal', () => ({
  RecordDetailModal: (props: {
    entityName: string;
    recordIds: readonly string[];
    index: number;
    onSettled?: () => void;
  }) => {
    // The real modal reports a settled record (loaded, denied, not found) once its read answers; the deep link keeps
    // `record` in the address bar until then. This stub settles at once.
    const { onSettled } = props;
    React.useEffect(() => {
      onSettled?.();
    }, [onSettled]);
    return React.createElement('div', {
      role: 'dialog',
      'data-testid': 'detail',
      'data-entity': props.entityName,
      'data-id': props.recordIds[props.index],
    });
  },
}));

const ID = '00000000-0000-4000-8000-000000000042';
const JANE = { homeAccountId: 'a', username: 'jane@partner.test', name: 'Jane Partner', tenantId: 'partner' };

beforeEach(() => {
  h.msal.accounts = [];
  h.msal.inProgress = 'none';
  h.bellMounts.count = 0;
  sessionStorage.clear();
  window.history.replaceState({}, '', '/');
});
afterEach(() => {
  window.history.replaceState({}, '', '/');
});

async function renderApp(teamsHost: boolean): Promise<void> {
  const { App } = await import('../src/App');
  render(React.createElement(App, { teamsHost }));
}

describe('App shell - notification bell', () => {
  it('browser header: the bell is there for a signed-in user', { timeout: 60000 }, async () => {
    h.msal.accounts = [JANE];
    await renderApp(false);
    expect(await screen.findByTestId('bell-stub')).toBeTruthy();
  });

  it('Teams: the bell is in the Teams hint bar too (parity)', { timeout: 60000 }, async () => {
    h.msal.accounts = [JANE];
    await renderApp(true);
    expect(await screen.findByTestId('bell-stub')).toBeTruthy();
    expect(screen.getByText(/Teams-embedded/)).toBeTruthy();
  });

  it('is not mounted (so reads nothing) when nobody is signed in', { timeout: 60000 }, async () => {
    h.msal.accounts = [];
    await renderApp(false);
    await screen.findByRole('banner');
    expect(screen.queryByTestId('bell-stub')).toBeNull();
    expect(h.bellMounts.count).toBe(0);
  });

  it(
    'is not mounted while an MSAL interaction (a redirect, a renewal) is still running',
    { timeout: 60000 },
    async () => {
      h.msal.accounts = [JANE];
      h.msal.inProgress = 'handleRedirect';
      await renderApp(false);
      await screen.findByRole('banner');
      expect(screen.queryByTestId('bell-stub')).toBeNull();
      expect(h.bellMounts.count).toBe(0);
    }
  );
});

describe('App shell - record deep link', () => {
  it(
    '?record= opens the detail view through the shell opener and leaves the rest of the URL',
    { timeout: 60000 },
    async () => {
      h.msal.accounts = [JANE];
      window.history.replaceState({}, '', `/?customer=default&record=matter:${ID}`);
      await renderApp(false);
      const detail = await screen.findByTestId('detail');
      expect(detail.getAttribute('data-entity')).toBe('sprk_matter');
      expect(detail.getAttribute('data-id')).toBe(ID);
      await waitFor(() => expect(window.location.search).toBe('?customer=default'));
    }
  );

  it('an invalid ?record= opens nothing', { timeout: 60000 }, async () => {
    h.msal.accounts = [JANE];
    window.history.replaceState({}, '', `/?record=matter:not-a-guid`);
    await renderApp(false);
    await screen.findByRole('banner');
    expect(screen.queryByTestId('detail')).toBeNull();
  });
});
