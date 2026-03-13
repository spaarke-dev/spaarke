/**
 * Jest setup file for RelatedDocumentCount tests
 *
 * Extends Jest matchers and sets up global mocks for Dataverse runtime.
 */
import "@testing-library/jest-dom";

// Mock Xrm global object (Dataverse runtime)
const mockXrm = {
  Navigation: {
    navigateTo: jest.fn().mockResolvedValue(undefined),
  },
  Utility: {
    getGlobalContext: jest.fn().mockReturnValue({
      getClientUrl: () => "https://test.crm.dynamics.com",
      getCurrentAppUrl: () => "https://test.crm.dynamics.com/main.aspx",
      authenticateToken: jest.fn().mockResolvedValue(null),
    }),
  },
};

// @ts-expect-error - Xrm is a global provided by Dataverse runtime
global.Xrm = mockXrm;

// Suppress console.error in tests (fetch error logging)
const originalError = console.error;
beforeAll(() => {
  console.error = jest.fn();
});
afterAll(() => {
  console.error = originalError;
});
