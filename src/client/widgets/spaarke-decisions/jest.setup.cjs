require('@testing-library/jest-dom');

// ResizeObserver: jsdom does not implement it, and Fluent v9 MessageBar constructs one in a layout effect (same stand-in as the shared
// library's jest.setup.js). Guarded so the node-environment suites, which have no `window`, are untouched.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
}
