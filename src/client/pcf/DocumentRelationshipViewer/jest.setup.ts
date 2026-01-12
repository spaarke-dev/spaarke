/**
 * Jest setup file for DocumentRelationshipViewer tests
 *
 * Configures:
 * - @testing-library/jest-dom matchers
 * - Global mocks for Xrm and window APIs
 * - React Flow and d3-force mocks
 */

import '@testing-library/jest-dom';

// Mock Xrm.Navigation for Dataverse navigation testing
const mockXrmNavigation = {
    openForm: jest.fn().mockResolvedValue(undefined),
};

(global as any).Xrm = {
    Navigation: mockXrmNavigation,
};

// Mock window.open for SharePoint file viewing
const mockWindowOpen = jest.fn();
Object.defineProperty(window, 'open', {
    writable: true,
    value: mockWindowOpen,
});

// Mock ResizeObserver for React Flow
class MockResizeObserver {
    observe = jest.fn();
    unobserve = jest.fn();
    disconnect = jest.fn();
}
(global as any).ResizeObserver = MockResizeObserver;

// Mock IntersectionObserver for React components
class MockIntersectionObserver {
    observe = jest.fn();
    unobserve = jest.fn();
    disconnect = jest.fn();
}
(global as any).IntersectionObserver = MockIntersectionObserver;

// Clear all mocks before each test
beforeEach(() => {
    jest.clearAllMocks();
    mockXrmNavigation.openForm.mockResolvedValue(undefined);
    mockWindowOpen.mockClear();
});

// Export mocks for use in tests
export { mockXrmNavigation, mockWindowOpen };
