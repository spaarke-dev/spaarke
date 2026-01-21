/**
 * Jest setup file for Office Add-ins tests
 *
 * This file runs before each test file and sets up global mocks.
 */

// Mock Office.js global object
global.Office = {
  context: {
    diagnostics: {
      platform: 'OfficeOnline',
      version: '16.0.0.0',
      host: 'Outlook',
    },
    requirements: {
      isSetSupported: jest.fn().mockReturnValue(true),
    },
    ui: {
      displayDialogAsync: jest.fn(),
      messageParent: jest.fn(),
    },
    mailbox: {
      item: null,
    },
    document: null,
  },
  onReady: jest.fn().mockImplementation((callback) => {
    if (callback) callback({ host: 'Outlook', platform: 'OfficeOnline' });
    return Promise.resolve({ host: 'Outlook', platform: 'OfficeOnline' });
  }),
  PlatformType: {
    PC: 'PC',
    Mac: 'Mac',
    OfficeOnline: 'OfficeOnline',
    iOS: 'iOS',
    Android: 'Android',
    Universal: 'Universal',
  },
  HostType: {
    Word: 'Word',
    Excel: 'Excel',
    PowerPoint: 'PowerPoint',
    Outlook: 'Outlook',
    OneNote: 'OneNote',
    Project: 'Project',
    Access: 'Access',
  },
  AsyncResultStatus: {
    Succeeded: 'succeeded',
    Failed: 'failed',
  },
  EventType: {
    DialogMessageReceived: 'dialogMessageReceived',
    DialogEventReceived: 'dialogEventReceived',
  },
  CoercionType: {
    Text: 'text',
    Html: 'html',
    Ooxml: 'ooxml',
  },
};

// Mock window.location
Object.defineProperty(window, 'location', {
  value: {
    origin: 'https://localhost:3000',
    href: 'https://localhost:3000/taskpane.html',
    protocol: 'https:',
    host: 'localhost:3000',
    hostname: 'localhost',
    port: '3000',
    pathname: '/taskpane.html',
    search: '',
    hash: '',
  },
  writable: true,
});

// Mock sessionStorage
const mockSessionStorage = (() => {
  let store = {};
  return {
    getItem: jest.fn((key) => store[key] || null),
    setItem: jest.fn((key, value) => {
      store[key] = String(value);
    }),
    removeItem: jest.fn((key) => {
      delete store[key];
    }),
    clear: jest.fn(() => {
      store = {};
    }),
    get length() {
      return Object.keys(store).length;
    },
    key: jest.fn((i) => Object.keys(store)[i] || null),
  };
})();

Object.defineProperty(window, 'sessionStorage', {
  value: mockSessionStorage,
});

// Mock console methods for cleaner test output
beforeAll(() => {
  jest.spyOn(console, 'log').mockImplementation(() => {});
  jest.spyOn(console, 'info').mockImplementation(() => {});
  jest.spyOn(console, 'debug').mockImplementation(() => {});
  // Keep console.warn and console.error for debugging
});

afterAll(() => {
  jest.restoreAllMocks();
});

// Reset session storage between tests
beforeEach(() => {
  mockSessionStorage.clear();
});
