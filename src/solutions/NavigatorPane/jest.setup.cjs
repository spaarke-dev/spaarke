/**
 * Jest setup for NavigatorPane. Mirrors src/solutions/Notepad/jest.setup.cjs.
 *
 * Minimal — NavigatorBody render tests exercise Fluent v9 (TabList, Tooltip,
 * Input). Polyfills here cover the jsdom gaps Fluent internals query on
 * construction (matchMedia, ResizeObserver) so tests don't trip over them.
 */

require('@testing-library/jest-dom');

// matchMedia — Fluent UI v9 internals query it on construction.
Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: jest.fn().mockImplementation((query) => ({
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

// ResizeObserver — jsdom does not implement this. Stubbed as a no-op class.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
}
