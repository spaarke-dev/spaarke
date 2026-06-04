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
