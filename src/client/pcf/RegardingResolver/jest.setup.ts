/**
 * Jest setup for RegardingResolver PCF tests.
 *
 * Extends Jest matchers and stubs the platform globals (Xrm, ResizeObserver,
 * matchMedia) that the React component touches at mount time.
 */

import '@testing-library/jest-dom';

// ---------------------------------------------------------------------------
// ResizeObserver (Fluent UI v9 dropdown uses it under the hood)
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
// Xrm global (lookup, navigation, page data)
// ---------------------------------------------------------------------------

const mockXrm = {
  Navigation: {
    openForm: jest.fn(),
  },
  Utility: {
    getGlobalContext: jest.fn().mockReturnValue({
      getClientUrl: () => 'https://test.crm.dynamics.com',
    }),
    lookupObjects: jest.fn().mockResolvedValue([]),
  },
  Page: {
    data: {
      entity: {
        getId: () => '11111111-1111-1111-1111-111111111111',
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
