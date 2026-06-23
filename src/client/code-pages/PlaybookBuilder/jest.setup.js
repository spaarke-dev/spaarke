/**
 * Jest setup file — mirrors sibling Spaarke.UI.Components/jest.setup.js polyfill
 * surface. Loaded by `setupFilesAfterEach` so every test file has the same
 * Fluent UI v9 + jsdom shims (matchMedia, ResizeObserver, scrollIntoView).
 *
 * Per R3 task 094: keep this file as small as the components under test need.
 * Do not add polyfills speculatively.
 */
require('@testing-library/jest-dom');

// Fluent v9 components query matchMedia for prefers-color-scheme. jsdom has no
// implementation; return a static "doesn't match" matcher so light theme wins.
Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: jest.fn().mockImplementation(query => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: jest.fn(),
    removeListener: jest.fn(),
    addEventListener: jest.fn(),
    removeEventListener: jest.fn(),
    dispatchEvent: jest.fn()
  }))
});

// Element.prototype.scrollIntoView — jsdom does not implement this method.
// Fluent v9 Menu/Combobox + Dialog focus management rely on it.
if (typeof Element !== 'undefined' && !Element.prototype.scrollIntoView) {
  Element.prototype.scrollIntoView = function () { /* noop in jsdom */ };
}

// ResizeObserver — jsdom does not implement this. Fluent v9 Drawer + Dialog
// use it internally for layout effects.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() { /* noop */ }
    unobserve() { /* noop */ }
    disconnect() { /* noop */ }
  };
}
