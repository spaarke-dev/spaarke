/**
 * Jest setup for CommunicationTimelineRegarding PCF tests.
 *
 * Extends Jest matchers and stubs the platform globals (Xrm, ResizeObserver,
 * matchMedia) the React component touches at mount time. Mirrors
 * CommunicationTimeline/jest.setup.ts, with the Xrm page mock updated to a
 * representative regarding-family host entity (sprk_matter) instead of
 * sprk_communicationthread.
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
// Xrm global (navigation, page data) — regarding-family host (sprk_matter)
// ---------------------------------------------------------------------------

const mockXrm = {
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
        getId: () => '44444444-4444-4444-4444-444444444444',
        getEntityName: () => 'sprk_matter',
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
