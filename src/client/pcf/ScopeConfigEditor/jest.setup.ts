/**
 * Jest setup file for ScopeConfigEditor tests
 *
 * Extends Jest matchers and sets up global mocks.
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
        }),
    },
};

// @ts-expect-error - Xrm is a global
global.Xrm = mockXrm;

// Mock window.open
global.open = jest.fn();

// Mock IntersectionObserver
const mockIntersectionObserver = jest.fn();
mockIntersectionObserver.mockReturnValue({
    observe: jest.fn(),
    unobserve: jest.fn(),
    disconnect: jest.fn(),
});
global.IntersectionObserver = mockIntersectionObserver;

// Mock ResizeObserver (used by Fluent UI MessageBar and other components)
const mockResizeObserver = jest.fn();
mockResizeObserver.mockReturnValue({
    observe: jest.fn(),
    unobserve: jest.fn(),
    disconnect: jest.fn(),
});
global.ResizeObserver = mockResizeObserver;

// Suppress console warnings in tests
const originalWarn = console.warn;
beforeAll(() => {
    console.warn = jest.fn();
});
afterAll(() => {
    console.warn = originalWarn;
});
