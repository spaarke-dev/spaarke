// Jest setup file
require('@testing-library/jest-dom');

// Mock window.matchMedia (required for Fluent UI components)
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

// ──────────────────────────────────────────────────────────────────────────
// jsdom polyfills (task 071)
//
// React 19 + RTL v16 surfaces several jsdom gaps that React 16 + RTL v14
// happened to hide. We patch them globally here so tests don't need
// per-file mocks. None of these patches alter production behavior.
// ──────────────────────────────────────────────────────────────────────────

// Element.prototype.scrollIntoView — jsdom does not implement this method.
// Fluent v9 components (Menu, Combobox) and SprkChatActionMenu rely on it
// to scroll focused items into view. Without this polyfill, focus-management
// effects throw "scrollIntoView is not a function".
if (typeof Element !== 'undefined' && !Element.prototype.scrollIntoView) {
  Element.prototype.scrollIntoView = function () { /* noop in jsdom */ };
}

// Layout visibility for Tabster (Fluent v9's focus manager) — #1290.
//
// jsdom has no layout: `HTMLElement.offsetParent` is always null and
// `document.body.getBoundingClientRect()` is 0×0. Tabster treats both as
// "invisible" (FocusableAPI.isVisible → isDisplayNone / zero-size body), so in
// jsdom it finds NO focusable element anywhere. Consequence for every Fluent
// `Dialog` (incl. SprkModal and all its presets):
//   1. DialogSurface's `useFocusFirstElement` finds nothing and falls back to
//      focusing the surface itself, before Tabster has processed the surface's
//      `data-tabster` modalizer attribute (that happens in a MutationObserver
//      callback);
//   2. Tabster therefore never activates the dialog's modalizer (it only
//      auto-activates a new modalizer when focus is strictly INSIDE it);
//   3. 250 ms later Tabster's debounced `_hiddenUpdate` sets
//      `aria-hidden="true"` on every inactive modalizer — i.e. on the open
//      dialog — and every `getByRole` inside it stops matching.
// Tests passed only if they finished their queries inside that 250 ms window,
// so they failed under full-suite CPU load (ConversationView forward/emailInFlow,
// templatePicker, RecordNavigationModalShell, AccessGrantModal). In a browser
// layout exists, focus lands on the first focusable inside the surface, and the
// modalizer activates. These two shims give Tabster the same answer a browser
// would, using computed `display` only — no fake geometry for anything else.
if (typeof HTMLElement !== 'undefined') {
  Object.defineProperty(HTMLElement.prototype, 'offsetParent', {
    configurable: true,
    get() {
      // CSSOM View: null when the element (or an ancestor) is not rendered
      // (display:none), is <body>/<html>, is detached, or is position:fixed;
      // otherwise the nearest positioned ancestor (or <body>).
      const doc = this.ownerDocument;
      if (!doc || !this.isConnected || this === doc.body || this === doc.documentElement) {
        return null;
      }
      const view = doc.defaultView;
      for (let el = this; el && el !== doc.documentElement; el = el.parentElement) {
        if (el.hidden || view.getComputedStyle(el).display === 'none') {
          return null;
        }
      }
      if (view.getComputedStyle(this).position === 'fixed') {
        return null;
      }
      for (let el = this.parentElement; el && el !== doc.body; el = el.parentElement) {
        if (view.getComputedStyle(el).position !== 'static') {
          return el;
        }
      }
      return doc.body;
    },
  });
}
if (typeof HTMLBodyElement !== 'undefined') {
  // A rendered <body> spans the viewport; jsdom reports 0×0, which Tabster
  // reads as "this document is in a hidden iframe".
  HTMLBodyElement.prototype.getBoundingClientRect = function () {
    const width = window.innerWidth;
    const height = window.innerHeight;
    return { x: 0, y: 0, top: 0, left: 0, right: width, bottom: height, width, height, toJSON() { return this; } };
  };
}

// ResizeObserver — jsdom does not implement this. Fluent v9 components
// (MessageBar/useMessageBarReflow, Drawer, ScrollPosition) construct one in
// layout effects. Without this polyfill, render throws "ResizeObserver is
// not a constructor".
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() { /* noop */ }
    unobserve() { /* noop */ }
    disconnect() { /* noop */ }
  };
}

// Range.prototype.getBoundingClientRect — jsdom implements ranges but the
// `getBoundingClientRect()` on a Range returns undefined-shaped data. The
// SprkChatHighlightRefine selection handler calls `range.getBoundingClientRect()`
// on document selectionchange events. Provide a stub returning a DOMRect-like.
if (typeof Range !== 'undefined' && Range.prototype) {
  const _origGetBCR = Range.prototype.getBoundingClientRect;
  if (typeof _origGetBCR !== 'function') {
    Range.prototype.getBoundingClientRect = function () {
      return { x: 0, y: 0, top: 0, left: 0, right: 0, bottom: 0, width: 0, height: 0, toJSON() { return this; } };
    };
  }
  if (typeof Range.prototype.getClientRects !== 'function') {
    Range.prototype.getClientRects = function () {
      return [];
    };
  }
}

// File / Blob `.arrayBuffer()` — jsdom's File polyfill does not implement
// `.arrayBuffer()`. useChatFileAttachment calls it for binary extraction
// (PDF / DOCX paths). FileReader-based polyfill is unreliable on empty Blobs
// in jsdom — use Buffer.from() via stream() or direct internal access.
function blobToArrayBuffer(blob) {
  return new Promise((resolve) => {
    // Jest's Blob polyfill stores body chunks in an internal symbol/slot;
    // the public Blob.prototype.text() implementation is safe for both string
    // and binary chunks. Read as text and re-encode for arrayBuffer.
    if (typeof blob.size === 'number' && blob.size === 0) {
      resolve(new ArrayBuffer(0));
      return;
    }
    // Use the FileReader path; jsdom does load empty/normal blobs through it.
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result);
    reader.onerror = () => resolve(new ArrayBuffer(0));
    try {
      reader.readAsArrayBuffer(blob);
    } catch (_e) {
      resolve(new ArrayBuffer(0));
    }
  });
}
if (typeof Blob !== 'undefined' && typeof Blob.prototype.arrayBuffer !== 'function') {
  Blob.prototype.arrayBuffer = function () { return blobToArrayBuffer(this); };
}
if (typeof File !== 'undefined' && typeof File.prototype.arrayBuffer !== 'function') {
  File.prototype.arrayBuffer = function () { return blobToArrayBuffer(this); };
}

// Blob.text() — jsdom may lack this on older versions. The useChatFileAttachment
// hook calls `file.text()` for text/plain and text/markdown attachments.
if (typeof Blob !== 'undefined' && typeof Blob.prototype.text !== 'function') {
  Blob.prototype.text = function () {
    return new Promise((resolve, reject) => {
      try {
        const reader = new FileReader();
        reader.onload = () => resolve(reader.result);
        reader.onerror = () => reject(reader.error);
        reader.readAsText(this);
      } catch (e) {
        reject(e);
      }
    });
  };
}
if (typeof File !== 'undefined' && typeof File.prototype.text !== 'function') {
  File.prototype.text = function () {
    return new Promise((resolve, reject) => {
      try {
        const reader = new FileReader();
        reader.onload = () => resolve(reader.result);
        reader.onerror = () => reject(reader.error);
        reader.readAsText(this);
      } catch (e) {
        reject(e);
      }
    });
  };
}
