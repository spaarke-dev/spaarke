/**
 * Jest setup file for Office Add-ins tests
 *
 * This file runs before each test file and sets up global mocks.
 */

// Registers the jest-dom custom matchers (toBeInTheDocument, toHaveTextContent,
// etc.) used throughout this package's component tests. The matchers were
// referenced but the package was never installed — task 009 (2026-09-09)
// added the dependency and this import to close the gap identified in task
// 001's baseline (notes/typecheck-baseline.md).
require('@testing-library/jest-dom');

// TextEncoder/TextDecoder are not exposed by jsdom's test environment (Node's
// implementations exist, but jsdom doesn't put them on `global`). Required by
// `computeIdempotencyKey` (useSaveFlow.ts) and any other code path that
// encodes/hashes text. Added task 040 / FR-B0 so the new 401-retry tests can
// actually exercise `startSave`'s fetch path — this also fixes several
// pre-existing `useSaveFlow.test.ts` failures ("TextEncoder is not defined")
// that were unrelated to auth and predate this task.
const { TextEncoder, TextDecoder } = require('util');
if (typeof global.TextEncoder === 'undefined') {
  global.TextEncoder = TextEncoder;
}
if (typeof global.TextDecoder === 'undefined') {
  global.TextDecoder = TextDecoder;
}

// jsdom has no ResizeObserver; Fluent v9 components that use auto layout (MessageBar reflow,
// Dropdown popup positioning, etc.) need one to render without throwing. Previously polyfilled
// per-file in 11 suites (SaveFlow.* variants, RelatedToPicker.*, DocumentProfileSection,
// LinkedTodosBanner, FindView) with copy-pasted stub classes; `ShareView.test.tsx` had no copy and
// failed 16/20 with "ResizeObserver is not a constructor" as a result (task 071). Defined once,
// globally, here instead — the per-file copies were removed in the same change.
class ResizeObserverStub {
  observe() {
    /* no-op: layout is irrelevant to test assertions */
  }
  unobserve() {
    /* no-op */
  }
  disconnect() {
    /* no-op */
  }
}
if (typeof global.ResizeObserver === 'undefined') {
  Object.defineProperty(window, 'ResizeObserver', {
    configurable: true,
    writable: true,
    value: ResizeObserverStub,
  });
}

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
      // task 027 / FR-10 — Spike-2's chosen mechanism (Option 3) opens a Dataverse record via
      // OpenBrowserWindowApi 1.1, not the Dialog API.
      openBrowserWindow: jest.fn(),
    },
    mailbox: {
      item: null,
      // task 036 / FR-15 — Send Email via Outlook opens a new-message compose window via
      // `Office.context.mailbox.displayNewMessageForm` (Mailbox 1.6).
      displayNewMessageForm: jest.fn(),
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
  // Task 010 / FR-04: the Word .docx save path reads
  // `Office.context.document.getFileAsync(Office.FileType.Compressed, ...)`. `FileType` was absent
  // from this stub, so any test of that path saw `undefined.Compressed` and threw before reaching
  // the adapter.
  FileType: {
    Text: 'text',
    Compressed: 'compressed',
    Pdf: 'pdf',
  },
  // Task 010 / FR-04: `OutlookAdapter.test.ts` reads `Office.MailboxEnums.Importance.Normal` at
  // module scope. `MailboxEnums` was absent here, so that ENTIRE suite failed to run with
  // "Cannot read properties of undefined (reading 'Importance')" — one of the two Office.js mock
  // gaps recorded in task 017's notes. The Outlook adapter suite is this task's step-5
  // Outlook-unregressed evidence, so the gap had to close for the verification to mean anything.
  MailboxEnums: {
    Importance: { Low: 'low', Normal: 'normal', High: 'high' },
    ItemType: { Message: 'message', Appointment: 'appointment' },
    AttachmentType: { File: 'file', Item: 'item', Cloud: 'cloud' },
    RecipientType: { DistributionList: 'distributionList', ExternalUser: 'externalUser', Other: 'other', User: 'user' },
    BodyType: { Html: 'html', Text: 'text' },
    // Task 071: `OutlookAdapter.test.ts` reads `Office.MailboxEnums.AttachmentContentFormat.Base64`
    // (getAttachmentContent, Mailbox 1.8) — absent here, same class of gap as the other MailboxEnums
    // members above (undefined.Base64 threw before the assertion ever ran).
    AttachmentContentFormat: { Base64: 'base64', Url: 'url', Eml: 'eml', ICalendar: 'iCalendar' },
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
