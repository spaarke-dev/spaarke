/**
 * useChatFileAttachment Hook Tests (Task 024)
 *
 * Covers FR-07 validation gates + lazy-loaded PDF.js / mammoth extraction
 * paths + FR-24 telemetry callback. All tests mock the dynamic `import()`
 * calls for `pdfjs-dist` and `mammoth` to keep the suite fast and runtime-
 * independent.
 *
 * @see ADR-022 - React hook patterns
 * @see src/components/SprkChat/hooks/useChatFileAttachment.ts
 */

import { renderHook, act } from '@testing-library/react';

// ---------------------------------------------------------------------------
// Dynamic-import mocks — must be set up BEFORE importing the hook so the
// hook's `import('pdfjs-dist')` / `import('mammoth')` calls resolve through
// jest's module registry.
// ---------------------------------------------------------------------------

const mockGetTextContent = jest.fn();
const mockGetPage = jest.fn();
const mockGetDocumentPromise = jest.fn();

jest.mock(
  'pdfjs-dist',
  () => ({
    __esModule: true,
    getDocument: jest.fn((..._args: unknown[]) => ({
      promise: mockGetDocumentPromise(),
    })),
    GlobalWorkerOptions: { workerSrc: '' },
  }),
  { virtual: true }
);

const mockExtractRawText = jest.fn();

jest.mock(
  'mammoth',
  () => ({
    __esModule: true,
    extractRawText: mockExtractRawText,
    default: { extractRawText: mockExtractRawText },
  }),
  { virtual: true }
);

// ---------------------------------------------------------------------------
// Hook under test — imported AFTER the mocks above.
// ---------------------------------------------------------------------------

import {
  useChatFileAttachment,
  MAX_ATTACHMENTS,
  MAX_FILE_BYTES,
} from '../hooks/useChatFileAttachment';

// ---------------------------------------------------------------------------
// File helpers
// ---------------------------------------------------------------------------

/**
 * Builds a `File` for tests. Avoids real Blob-string round-trips for PDF /
 * DOCX bodies — we mock the extractors, so the body content is irrelevant.
 * For txt/md files the body IS read via File.text(), so callers should pass
 * meaningful text.
 */
function makeFile(name: string, type: string, body: string | ArrayBuffer = '', sizeOverride?: number): File {
  const blob = new Blob([body], { type });
  const file = new File([blob], name, { type });
  if (sizeOverride !== undefined) {
    // Jest's File polyfill uses Blob.size — override via defineProperty for
    // the `too-large` validation gate.
    Object.defineProperty(file, 'size', { value: sizeOverride, configurable: true });
  }
  return file;
}

// ---------------------------------------------------------------------------
// Reset between tests
// ---------------------------------------------------------------------------

beforeEach(() => {
  // Task 071: `jest.clearAllMocks()` only clears CALL HISTORY, not mock
  // implementations. Earlier tests' `mockExtractRawText.mockRejectedValue(...)`
  // / `mockGetDocumentPromise.mockRejectedValue(...)` calls persisted across
  // tests and corrupted later `addFiles` calls (rendering the hook null).
  // Reset both implementations AND history explicitly.
  mockGetTextContent.mockReset();
  mockGetPage.mockReset();
  mockGetDocumentPromise.mockReset();
  mockExtractRawText.mockReset();
  jest.clearAllMocks();
});

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('useChatFileAttachment', () => {
  describe('initial state', () => {
    it('starts with empty files, attachments, and errors', () => {
      const { result } = renderHook(() => useChatFileAttachment());
      expect(result.current.files).toEqual([]);
      expect(result.current.attachments).toEqual([]);
      expect(result.current.errors).toEqual([]);
    });
  });

  describe('validation gates (NFR-04)', () => {
    it('rejects the 6th file with too-many error', async () => {
      const { result } = renderHook(() => useChatFileAttachment());

      const sixFiles = Array.from({ length: 6 }, (_, i) =>
        makeFile(`f${i}.txt`, 'text/plain', `body-${i}`)
      );

      await act(async () => {
        await result.current.addFiles(sixFiles);
      });

      // 5 chips accepted (eventually `ready` after extraction)
      expect(result.current.files).toHaveLength(MAX_ATTACHMENTS);
      // Exactly 1 too-many error
      const tooMany = result.current.errors.filter((e) => e.reason === 'too-many');
      expect(tooMany).toHaveLength(1);
      expect(tooMany[0].filename).toBe('f5.txt');
      expect(tooMany[0].message).toMatch(/Maximum 5/);
    });

    it('rejects a >25 MB file with too-large error (R4 A-4: raised from 10 MB)', async () => {
      const { result } = renderHook(() => useChatFileAttachment());

      const oversize = makeFile('huge.txt', 'text/plain', 'tiny', MAX_FILE_BYTES + 1);

      await act(async () => {
        await result.current.addFiles([oversize]);
      });

      expect(result.current.files).toHaveLength(0);
      const tooLarge = result.current.errors.filter((e) => e.reason === 'too-large');
      expect(tooLarge).toHaveLength(1);
      expect(tooLarge[0].filename).toBe('huge.txt');
      // Error message must reference the 25 MB limit
      expect(tooLarge[0].message).toMatch(/25 MB/);
    });

    it('locks MAX_FILE_BYTES at 25 MB (R4 A-4 acceptance criterion)', () => {
      // Lock the constant value. A change here is a breaking change for the
      // attachment-policy contract; the test must be updated alongside any
      // policy doc revision.
      expect(MAX_FILE_BYTES).toBe(25 * 1024 * 1024);
    });

    describe('R4 A-4 boundary cases (FR-04 acceptance — 1 / 10 / 24 / 25 / 26 MB)', () => {
      const ONE_MB = 1 * 1024 * 1024;
      const TEN_MB = 10 * 1024 * 1024;
      const TWENTYFOUR_MB = 24 * 1024 * 1024;
      const TWENTYFIVE_MB = 25 * 1024 * 1024;
      const TWENTYSIX_MB = 26 * 1024 * 1024;

      it.each([
        ['1 MB', ONE_MB],
        ['10 MB', TEN_MB],
        ['24 MB', TWENTYFOUR_MB],
        ['25 MB (boundary == cap)', TWENTYFIVE_MB],
      ])('accepts a %s file (under or at cap)', async (_label, sizeBytes) => {
        const { result } = renderHook(() => useChatFileAttachment());
        const file = makeFile('within-cap.txt', 'text/plain', 'content', sizeBytes);

        await act(async () => {
          await result.current.addFiles([file]);
        });

        expect(
          result.current.errors.filter((e) => e.reason === 'too-large'),
        ).toHaveLength(0);
        // Chip should be accepted (status ready after text extraction)
        expect(result.current.files).toHaveLength(1);
      });

      it('rejects a 26 MB file (just over cap) with clear error', async () => {
        const { result } = renderHook(() => useChatFileAttachment());
        const file = makeFile('over-cap.txt', 'text/plain', 'content', TWENTYSIX_MB);

        await act(async () => {
          await result.current.addFiles([file]);
        });

        expect(result.current.files).toHaveLength(0);
        const tooLarge = result.current.errors.filter((e) => e.reason === 'too-large');
        expect(tooLarge).toHaveLength(1);
        expect(tooLarge[0].message).toMatch(/25 MB/);
      });
    });

    it('rejects unsupported MIME types', async () => {
      const { result } = renderHook(() => useChatFileAttachment());

      const bad = makeFile('blob.bin', 'application/octet-stream', 'x');

      await act(async () => {
        await result.current.addFiles([bad]);
      });

      expect(result.current.files).toHaveLength(0);
      const unsupported = result.current.errors.filter((e) => e.reason === 'unsupported-mime');
      expect(unsupported).toHaveLength(1);
    });

    it('rejects a PDF with >200 pages', async () => {
      mockGetTextContent.mockResolvedValue({ items: [] });
      mockGetPage.mockResolvedValue({ getTextContent: mockGetTextContent });
      mockGetDocumentPromise.mockResolvedValue({
        numPages: 201,
        getPage: mockGetPage,
      });

      const { result } = renderHook(() => useChatFileAttachment());

      const pdf = makeFile('huge.pdf', 'application/pdf', new ArrayBuffer(1024));

      await act(async () => {
        await result.current.addFiles([pdf]);
      });

      const cap = result.current.errors.filter((e) => e.reason === 'pdf-too-many-pages');
      expect(cap).toHaveLength(1);
      // Chip transitions to `error` state
      const chip = result.current.files.find((c) => c.filename === 'huge.pdf');
      expect(chip?.status).toBe('error');
    });
  });

  describe('extraction paths', () => {
    it('reads text/plain content via File.text()', async () => {
      const { result } = renderHook(() => useChatFileAttachment());
      const txt = makeFile('notes.txt', 'text/plain', 'hello world');

      await act(async () => {
        await result.current.addFiles([txt]);
      });

      expect(result.current.files).toHaveLength(1);
      expect(result.current.files[0].status).toBe('ready');
      expect(result.current.attachments).toHaveLength(1);
      expect(result.current.attachments[0]).toEqual({
        filename: 'notes.txt',
        contentType: 'text/plain',
        textContent: 'hello world',
      });
    });

    it('reads text/markdown via File.text()', async () => {
      const { result } = renderHook(() => useChatFileAttachment());
      const md = makeFile('readme.md', 'text/markdown', '# Title');

      await act(async () => {
        await result.current.addFiles([md]);
      });

      expect(result.current.attachments).toHaveLength(1);
      expect(result.current.attachments[0].textContent).toBe('# Title');
    });

    it('extracts PDF via lazy-loaded pdfjs-dist', async () => {
      mockGetTextContent.mockResolvedValue({
        items: [{ str: 'page' }, { str: 'one' }],
      });
      mockGetPage.mockResolvedValue({ getTextContent: mockGetTextContent });
      mockGetDocumentPromise.mockResolvedValue({
        numPages: 1,
        getPage: mockGetPage,
      });

      const { result } = renderHook(() => useChatFileAttachment());
      const pdf = makeFile('doc.pdf', 'application/pdf', new ArrayBuffer(1024));

      await act(async () => {
        await result.current.addFiles([pdf]);
      });

      // Ensure the pdfjs-dist `getDocument` was invoked (proves the dynamic
      // import resolved through the mock, not via a static import).
      // eslint-disable-next-line @typescript-eslint/no-require-imports
      const pdfjsMock = require('pdfjs-dist');
      expect(pdfjsMock.getDocument).toHaveBeenCalled();
      expect(mockGetPage).toHaveBeenCalledWith(1);
      expect(result.current.attachments).toHaveLength(1);
      expect(result.current.attachments[0].textContent).toContain('page');
      expect(result.current.attachments[0].textContent).toContain('one');
    });

    it('extracts DOCX via lazy-loaded mammoth', async () => {
      mockExtractRawText.mockResolvedValue({ value: 'docx body', messages: [] });

      const { result } = renderHook(() => useChatFileAttachment());
      const docx = makeFile(
        'doc.docx',
        'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
        new ArrayBuffer(1024)
      );

      await act(async () => {
        await result.current.addFiles([docx]);
      });

      expect(mockExtractRawText).toHaveBeenCalled();
      expect(result.current.attachments).toHaveLength(1);
      expect(result.current.attachments[0].textContent).toBe('docx body');
      expect(result.current.attachments[0].contentType).toBe(
        'application/vnd.openxmlformats-officedocument.wordprocessingml.document'
      );
    });
  });

  describe('FR-24 telemetry', () => {
    it('emits onExtractionError when extraction throws', async () => {
      mockGetDocumentPromise.mockRejectedValue(new Error('corrupted pdf'));

      const onExtractionError = jest.fn();
      const { result } = renderHook(() =>
        useChatFileAttachment({ onExtractionError })
      );

      const pdf = makeFile('bad.pdf', 'application/pdf', new ArrayBuffer(1024));

      await act(async () => {
        await result.current.addFiles([pdf]);
      });

      expect(onExtractionError).toHaveBeenCalledTimes(1);
      const [filename, mimeType, sizeBytes, error] = onExtractionError.mock.calls[0];
      expect(filename).toBe('bad.pdf');
      expect(mimeType).toBe('application/pdf');
      expect(typeof sizeBytes).toBe('number');
      expect((error as Error).message).toBe('corrupted pdf');

      // Chip ends in `error` state with `extraction-failed` reason
      const errorEntries = result.current.errors.filter((e) => e.reason === 'extraction-failed');
      expect(errorEntries).toHaveLength(1);
    });

    it('does not throw when onExtractionError callback itself throws', async () => {
      mockExtractRawText.mockRejectedValue(new Error('mammoth boom'));

      const onExtractionError = jest.fn(() => {
        throw new Error('telemetry boom');
      });

      const { result } = renderHook(() =>
        useChatFileAttachment({ onExtractionError })
      );

      const docx = makeFile(
        'bad.docx',
        'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
        new ArrayBuffer(1024)
      );

      // Should NOT propagate the telemetry exception
      await expect(
        act(async () => {
          await result.current.addFiles([docx]);
        })
      ).resolves.not.toThrow();

      expect(onExtractionError).toHaveBeenCalledTimes(1);
      expect(result.current.errors.filter((e) => e.reason === 'extraction-failed')).toHaveLength(1);
    });
  });

  describe('mutations', () => {
    it('removeFile(0) splices both the chip and the derived attachment', async () => {
      const { result } = renderHook(() => useChatFileAttachment());

      const a = makeFile('a.txt', 'text/plain', 'aaa');
      const b = makeFile('b.txt', 'text/plain', 'bbb');

      await act(async () => {
        await result.current.addFiles([a, b]);
      });

      expect(result.current.files).toHaveLength(2);
      expect(result.current.attachments).toHaveLength(2);

      act(() => {
        result.current.removeFile(0);
      });

      expect(result.current.files).toHaveLength(1);
      expect(result.current.attachments).toHaveLength(1);
      expect(result.current.files[0].filename).toBe('b.txt');
      expect(result.current.attachments[0].filename).toBe('b.txt');
    });

    it('clearAll empties chips, attachments, and errors', async () => {
      const { result } = renderHook(() => useChatFileAttachment());

      const ok = makeFile('a.txt', 'text/plain', 'aaa');
      const bad = makeFile('blob.bin', 'application/octet-stream', 'x');

      await act(async () => {
        await result.current.addFiles([ok, bad]);
      });

      expect(result.current.files.length).toBeGreaterThan(0);
      expect(result.current.errors.length).toBeGreaterThan(0);

      act(() => {
        result.current.clearAll();
      });

      expect(result.current.files).toEqual([]);
      expect(result.current.attachments).toEqual([]);
      expect(result.current.errors).toEqual([]);
    });
  });

  describe('NFR-12 lazy-load guarantee (source-level)', () => {
    it('hook source does not statically import pdfjs-dist or mammoth', () => {
      // Read the hook source directly and verify NO top-level static
      // `from 'pdfjs-dist'` or `from 'mammoth'` import line exists. This
      // is the load-bearing check for the bundle-budget constraint —
      // task 061 also verifies via bundle-analyzer output.
      // eslint-disable-next-line @typescript-eslint/no-var-requires, @typescript-eslint/no-require-imports
      const fs = require('fs');
      // eslint-disable-next-line @typescript-eslint/no-var-requires, @typescript-eslint/no-require-imports
      const path = require('path');
      const source = fs.readFileSync(
        path.resolve(__dirname, '..', 'hooks', 'useChatFileAttachment.ts'),
        'utf-8'
      );

      // Match top-level static `import ... from 'pdfjs-dist'` (any quote
      // style, optional default + named). The dynamic `await import('pdfjs-dist')`
      // call uses parentheses, so this regex won't false-positive on it.
      const staticPdfRegex = /^\s*import\s+[^;]*from\s+['"]pdfjs-dist['"]/m;
      const staticMammothRegex = /^\s*import\s+[^;]*from\s+['"]mammoth['"]/m;

      expect(staticPdfRegex.test(source)).toBe(false);
      expect(staticMammothRegex.test(source)).toBe(false);

      // Both libs MUST be referenced via dynamic `import('...')`.
      expect(source.includes("import('pdfjs-dist')")).toBe(true);
      expect(source.includes("import('mammoth')")).toBe(true);
    });
  });
});
