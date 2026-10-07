/**
 * Jest setup for CommunicationConnections PCF tests.
 *
 * Extends Jest matchers and stubs the platform globals (Xrm, ResizeObserver,
 * matchMedia) the React component touches at mount time. Mirrors
 * RegardingResolver/jest.setup.ts.
 */

import '@testing-library/jest-dom';

// ---------------------------------------------------------------------------
// ResizeObserver (Fluent UI v9 uses it under the hood)
// ---------------------------------------------------------------------------

class ResizeObserverMock {
  observe = jest.fn();
  unobserve = jest.fn();
  disconnect = jest.fn();
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
(global as any).ResizeObserver = ResizeObserverMock;

// ---------------------------------------------------------------------------
// matchMedia (theme detection inside resolveThemeWithUserPreference)
// ---------------------------------------------------------------------------

Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: jest.fn().mockImplementation((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: jest.fn(),
    removeListener: jest.fn(),
    addEventListener: jest.fn(),
    removeEventListener: jest.fn(),
    dispatchEvent: jest.fn(),
  })),
});

// ---------------------------------------------------------------------------
// Xrm global (navigation, page data)
// ---------------------------------------------------------------------------

const mockXrm = {
  // Required for the shared `getXrm()` walker (`@spaarke/ui-components`
  // `xrmContext.ts`) to accept this frame — task 081 / C-8. `launchCreate.ts`
  // now resolves `Xrm.Navigation.navigateTo` via `getXrm()`, which only
  // accepts a frame whose `Xrm` has `WebApi` (matching the real Dataverse
  // host shape where `WebApi` and `Navigation` are always present together).
  WebApi: {
    retrieveMultipleRecords: jest.fn(),
    retrieveRecord: jest.fn(),
    createRecord: jest.fn(),
    updateRecord: jest.fn(),
    deleteRecord: jest.fn(),
  },
  Navigation: {
    openForm: jest.fn(),
    navigateTo: jest.fn().mockResolvedValue(undefined),
  },
  Utility: {
    getGlobalContext: jest.fn().mockReturnValue({
      getClientUrl: () => 'https://test.crm.dynamics.com',
    }),
  },
  Page: {
    data: {
      entity: {
        getId: () => '22222222-2222-2222-2222-222222222222',
      },
    },
  },
};

// eslint-disable-next-line @typescript-eslint/no-explicit-any
(global as any).Xrm = mockXrm;

// ---------------------------------------------------------------------------
// fetch (nav-prop discovery)
// ---------------------------------------------------------------------------

if (!globalThis.fetch) {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (globalThis as any).fetch = jest.fn().mockResolvedValue({
    ok: true,
    json: async () => ({ value: [] }),
  });
}
