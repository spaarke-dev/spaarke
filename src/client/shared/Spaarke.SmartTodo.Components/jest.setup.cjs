/**
 * Jest setup for @spaarke/smart-todo-components (smart-todo-r5 task 040).
 *
 * Minimal — today's suites are pure-function tests (no component rendering).
 * The polyfills here cover the jsdom gaps Fluent UI v9 internals commonly hit
 * even when only importing a component module for its exported helper
 * functions (e.g. `makeStyles`/griffel probing `matchMedia` at module-eval
 * time). Mirrors src/client/shared/Spaarke.UI.Components/jest.setup.js
 * (subset) and src/solutions/SmartTodo/jest.setup.cjs.
 */

require('@testing-library/jest-dom');

// matchMedia — Fluent UI v9 internals query it on construction even when we
// only import pure helpers transitively. Mock returns "no match" for all
// queries, which is the safe default.
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
