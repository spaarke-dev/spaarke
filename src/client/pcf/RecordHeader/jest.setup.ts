/**
 * Jest setup for MatterHeader PCF tests.
 *
 * Installs @testing-library/jest-dom matchers and a global Xrm shim that
 * MatterHeaderView + its shared-lib hooks can walk to. Individual tests may
 * override `Xrm.WebApi.retrieveRecord` / `retrieveMultipleRecords` /
 * `Xrm.Navigation.navigateTo` for their scenarios.
 */

import '@testing-library/jest-dom';

// Mock ResizeObserver (Fluent v9 uses it internally)
class MockResizeObserver {
  observe = jest.fn();
  unobserve = jest.fn();
  disconnect = jest.fn();
}
(global as unknown as { ResizeObserver: typeof MockResizeObserver }).ResizeObserver = MockResizeObserver;

// Mock IntersectionObserver
class MockIntersectionObserver {
  observe = jest.fn();
  unobserve = jest.fn();
  disconnect = jest.fn();
}
(global as unknown as { IntersectionObserver: typeof MockIntersectionObserver }).IntersectionObserver =
  MockIntersectionObserver;

beforeEach(() => {
  jest.clearAllMocks();
});
