/**
 * Jest setup file — runs before each test file.
 *
 * R2 task 019 / NFR-05:
 *   - Adds jsdom polyfills used by Fluent v9 component paths.
 *   - Wires `@testing-library/jest-dom` matchers (toBeInTheDocument, etc.).
 */

import "@testing-library/jest-dom";

// matchMedia polyfill — Fluent v9 uses it in some render paths.
if (typeof window !== "undefined" && !window.matchMedia) {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: (query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => false,
    }),
  });
}
