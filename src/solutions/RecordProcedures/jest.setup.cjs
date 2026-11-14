/**
 * Jest setup for the Record Procedures page. Mirrors src/solutions/NavigatorPane/jest.setup.cjs: the jsdom gaps Fluent v9 queries on
 * construction (matchMedia, ResizeObserver).
 */

require('@testing-library/jest-dom');

// The wizard tests wait for the steps of a Fluent dialog; under a parallel run testing-library's 1 s default for findBy / waitFor is too tight
// (ebilling-r1 084d-4 review round 1: K-6 of the first round). A test that waits for something that never comes still ends at jest's testTimeout.
require('@testing-library/react').configure({ asyncUtilTimeout: 5000 });

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
    dispatchEvent: jest.fn(),
  })),
});

if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
}
