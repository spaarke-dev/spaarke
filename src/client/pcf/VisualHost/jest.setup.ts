/// <reference types="@testing-library/jest-dom" />
import '@testing-library/jest-dom';

// Mock ResizeObserver for components that use it
class ResizeObserverMock {
  observe = jest.fn();
  unobserve = jest.fn();
  disconnect = jest.fn();
}

global.ResizeObserver = ResizeObserverMock;

// Mock matchMedia for theme detection
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

// Mock SVG text methods for @fluentui/react-charting
if (typeof SVGElement !== 'undefined') {
  Object.defineProperty(SVGElement.prototype, 'getComputedTextLength', {
    writable: true,
    value: jest.fn().mockReturnValue(100),
  });

  Object.defineProperty(SVGElement.prototype, 'getBBox', {
    writable: true,
    value: jest.fn().mockReturnValue({
      x: 0,
      y: 0,
      width: 100,
      height: 20,
    }),
  });

  Object.defineProperty(SVGElement.prototype, 'getTotalLength', {
    writable: true,
    value: jest.fn().mockReturnValue(100),
  });

  Object.defineProperty(SVGElement.prototype, 'getPointAtLength', {
    writable: true,
    value: jest.fn().mockReturnValue({ x: 0, y: 0 }),
  });
}
