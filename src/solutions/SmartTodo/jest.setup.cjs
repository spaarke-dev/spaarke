/**
 * Jest setup for SmartTodo (R4-114, 2026-06-25).
 *
 * Minimal — these tests are mostly pure unit tests of helpers + hooks; no
 * Fluent UI component rendering. The polyfills here cover the rare cases
 * where jsdom gaps surface (matchMedia, ResizeObserver) so future test
 * authors don't trip over them.
 *
 * Mirrors src/client/shared/Spaarke.UI.Components/jest.setup.js (subset).
 */

require('@testing-library/jest-dom');

// matchMedia — Fluent UI v9 internals query it on construction even when we
// only import pure helpers transitively. Mock returns "no match" for all
// queries which is the safe default.
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
