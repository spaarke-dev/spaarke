/**
 * Jest setup for AssociationResolver PCF tests.
 *
 * Extends Jest matchers and stubs the platform globals (Xrm, ResizeObserver,
 * matchMedia) that RecordSelectionHandler + React components touch at mount time.
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
// matchMedia (theme detection paths)
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
// fetch (nav-prop discovery — not used by RecordSelectionHandler but
// available for future tests exercising the sibling handler path)
// ---------------------------------------------------------------------------

if (!globalThis.fetch) {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (globalThis as any).fetch = jest.fn().mockResolvedValue({
    ok: true,
    json: async () => ({ value: [] }),
  });
}
