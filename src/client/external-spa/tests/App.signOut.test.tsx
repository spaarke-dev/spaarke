// Sign-out clears the tab's active customer key along with the realm (spaarke-SPA-external-access-platform-r3 task 041,
// review-fix round). The real App and AppHeader run; MSAL's useMsal is a fake whose logoutRedirect is recorded, and the
// route pages / auth gate are stubs (nothing here reaches the BFF).
import * as React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';

const h = vi.hoisted(() => ({ logoutRedirect: vi.fn() }));

vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({
    accounts: [{ homeAccountId: 'a', username: 'jane@partner.test', name: 'Jane Partner', tenantId: 'partner' }],
    instance: { logoutRedirect: h.logoutRedirect },
    inProgress: 'none',
  }),
}));
vi.mock('../src/components/AuthGuard', () => ({
  AuthGuard: ({ children }: { children: React.ReactNode }) => children,
}));
vi.mock('../src/pages/WorkspaceHomePage', () => ({ WorkspaceHomePage: () => null }));
vi.mock('../src/pages/ProjectPage', () => ({ ProjectPage: () => null }));
vi.mock('../src/pages/DocumentUploadPage', () => ({ DocumentUploadPage: () => null }));
vi.mock('../src/pages/SettingsPage', () => ({ SettingsPage: () => null }));

const CONTOSO = {
  customerKey: 'contoso',
  displayName: 'Contoso',
  bffBaseUrl: 'https://contoso-bff.test',
  bffAppId: 'aaaaaaaa-0000-0000-0000-00000000000a',
};

beforeEach(() => {
  h.logoutRedirect.mockReset();
  localStorage.clear();
  sessionStorage.clear();
});

describe('App sign-out', () => {
  it(
    'clears the stored realm AND the active customer (memory + the tab key), then logs out',
    { timeout: 60000 },
    async () => {
      const selection = await import('../src/auth/backend-selection');
      const realm = await import('../src/auth/realm');
      const { App } = await import('../src/App');

      realm.setStoredRealm('ciam');
      selection.activateBackend('ciam', { ...CONTOSO });
      expect(sessionStorage.getItem('spaarke.ext.activeCustomer')).toBe('contoso');

      render(React.createElement(App, { teamsHost: false }));
      fireEvent.click(await screen.findByRole('button', { name: /Jane Partner/ }));
      fireEvent.click(await screen.findByRole('menuitem', { name: /Sign out/ }));

      expect(h.logoutRedirect).toHaveBeenCalledTimes(1);
      expect(realm.getStoredRealm()).toBeNull();
      expect(() => selection.getActiveBackend()).toThrow(/No backend is active/);
      expect(sessionStorage.getItem('spaarke.ext.activeCustomer')).toBeNull();
      // The remembered customer keys (the picker's list) are deliberately kept.
      expect(JSON.parse(localStorage.getItem('spaarke.ext.knownCustomers') as string)).toEqual(['contoso']);
    }
  );
});
