/**
 * Jest setup for UniversalDatasetGrid PCF control
 *
 * Task 018: Add Unit Tests for Grid Enhancements
 */
require('@testing-library/jest-dom');

// Polyfills for streaming APIs (not available in jsdom)
const { TextEncoder, TextDecoder } = require('util');
const { ReadableStream } = require('stream/web');

global.TextEncoder = TextEncoder;
global.TextDecoder = TextDecoder;
global.ReadableStream = ReadableStream;

// Mock Xrm global object for PCF testing
global.Xrm = {
  App: {
    sidePanes: {
      state: 1,
      createPane: jest.fn().mockResolvedValue({
        paneId: 'eventDetailPane',
        navigate: jest.fn().mockResolvedValue(undefined),
        select: jest.fn(),
        close: jest.fn()
      }),
      getPane: jest.fn().mockReturnValue(undefined),
      getAllPanes: jest.fn().mockReturnValue([])
    }
  },
  Navigation: {
    openForm: jest.fn().mockResolvedValue(undefined)
  }
};
