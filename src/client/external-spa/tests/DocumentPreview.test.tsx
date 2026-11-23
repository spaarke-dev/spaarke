/**
 * R4 task 022 (FR-14 / FR-15) — the Document record's inline preview.
 *
 * What runs for real: `DocumentPreview`, `PreviewSection` (with its `RecordSectionCard`), `previewKindOf`, Fluent v9 and the
 * parent-order loop. What is substituted: `bffApiBlob` (the SPA's authenticated binary read; its size cap has its own tests
 * in BffBlobLimits.test.ts, here a refused oversize read is `BlobTooLargeError`) and the object-URL functions jsdom lacks.
 */
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const { bffApiBlob } = vi.hoisted(() => ({ bffApiBlob: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiBlob };
});

import { ApiError } from '../src/types';
import { BlobTooLargeError } from '../src/auth/bff-client';
import {
  DocumentPreview,
  PREVIEW_MAX_BYTES,
  previewKindOf,
  type PreviewFile,
} from '../src/components/record/sections/DocumentPreview';
import { PreviewSection } from '../src/components/record/sections/PreviewSection';
import type { RecordSectionContext } from '../src/components/record/recordTemplate.types';

const guid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const DOC = guid(901);
const DOC_2 = guid(902);
const PROJECT = guid(101);
const MATTER = guid(201);

const contentPath = (root: string, id: string, doc = DOC): string =>
  `/api/v1/external/${root}/${id}/documents/${doc}/content`;
const problem = (status: number): string => JSON.stringify({ status, title: 'refused', detail: 'server detail text' });

const FILE: PreviewFile = { parents: [{ root: 'matters', id: MATTER }], name: 'Contract.pdf' };
const TWO_PARENTS: PreviewFile = {
  parents: [
    { root: 'projects', id: PROJECT },
    { root: 'matters', id: MATTER },
  ],
  name: 'Contract.pdf',
};

let urlCounter: number;
let createObjectURL: ReturnType<typeof vi.fn>;
let revokeObjectURL: ReturnType<typeof vi.fn>;

beforeEach(() => {
  bffApiBlob.mockReset();
  urlCounter = 0;
  createObjectURL = vi.fn(() => `blob:preview-${++urlCounter}`);
  revokeObjectURL = vi.fn();
  Object.assign(window.URL, { createObjectURL, revokeObjectURL });
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

function view(props: React.ComponentProps<typeof DocumentPreview>): React.ReactElement {
  return (
    <FluentProvider theme={webLightTheme}>
      <DocumentPreview {...props} />
    </FluentProvider>
  );
}

const WAIT = { timeout: 5000 };

// ---------------------------------------------------------------------------
// Which files preview
// ---------------------------------------------------------------------------

describe('previewKindOf', () => {
  it.each([
    ['PDF', null, 'pdf', 'application/pdf'],
    ['png', null, 'image', 'image/png'],
    ['JPG', null, 'image', 'image/jpeg'],
    ['JPEG', null, 'image', 'image/jpeg'],
    ['.gif', null, 'image', 'image/gif'],
    ['WEBP', null, 'image', 'image/webp'],
    // Blank or absent type: the extension of the name decides.
    [null, 'Contract.PDF', 'pdf', 'application/pdf'],
    ['', 'scan.v2.jpeg', 'image', 'image/jpeg'],
    ['  ', 'photo.webp', 'image', 'image/webp'],
    [undefined, 'logo.png', 'image', 'image/png'],
  ])('type %j / name %j previews as %s (%s)', (fileType, name, kind, mime) => {
    expect(previewKindOf(fileType, name)).toEqual({ kind, mime });
  });

  it.each([
    ['DOCX', 'Contract.pdf'], // a present type is final: the name's ".pdf" is not consulted
    ['XLSX', null],
    ['SVG', 'logo.svg'],
    ['TIFF', null],
    ['constructor', null],
    [null, 'Contract.docx'],
    [null, 'no-extension'],
    [null, 'trailing-dot.'],
    [null, null],
    [undefined, undefined],
  ])('type %j / name %j is Download only', (fileType, name) => {
    expect(previewKindOf(fileType, name)).toBeNull();
  });

  it('the cap is exactly 25 MB (26,214,400 bytes)', () => {
    expect(PREVIEW_MAX_BYTES).toBe(26_214_400);
  });
});

// ---------------------------------------------------------------------------
// Previewable files render in place
// ---------------------------------------------------------------------------

describe('DocumentPreview: previewable files', () => {
  it('renders a PDF in a titled frame from an object URL typed application/pdf', async () => {
    bffApiBlob.mockResolvedValue(new Blob(['%PDF'], { type: 'application/octet-stream' }));

    render(view({ documentId: DOC, file: FILE, fileType: 'PDF' }));

    const frame = await screen.findByTestId('record-preview-pdf', undefined, WAIT);
    expect(frame.tagName).toBe('IFRAME');
    expect(frame.getAttribute('src')).toBe('blob:preview-1');
    expect(frame.getAttribute('title')).toBe('Preview of Contract.pdf');
    // The route answers octet-stream: the blob is re-typed from the decided type before the object URL is made.
    expect((createObjectURL.mock.calls[0][0] as Blob).type).toBe('application/pdf');
    expect(screen.queryByTestId('record-preview-loading')).toBeNull();
  });

  it.each([
    ['PNG', 'scan.png', 'image/png'],
    ['JPG', 'photo.jpg', 'image/jpeg'],
    ['JPEG', 'photo.jpeg', 'image/jpeg'],
    ['GIF', 'anim.gif', 'image/gif'],
    ['WEBP', 'pic.webp', 'image/webp'],
  ])('renders a %s image with an accessible name, re-typed %s', async (fileType, name, mime) => {
    bffApiBlob.mockResolvedValue(new Blob(['bytes'], { type: 'application/octet-stream' }));

    render(view({ documentId: DOC, file: { ...FILE, name }, fileType }));

    const image = await screen.findByTestId('record-preview-image', undefined, WAIT);
    expect(image.tagName).toBe('IMG');
    expect(image.getAttribute('alt')).toBe(`Preview of ${name}`);
    expect(image.getAttribute('src')).toBe('blob:preview-1');
    expect((createObjectURL.mock.calls[0][0] as Blob).type).toBe(mime);
  });

  it('decides from the name when the file type was not read', async () => {
    bffApiBlob.mockResolvedValue(new Blob(['bytes']));

    render(view({ documentId: DOC, file: { ...FILE, name: 'Photo.JPG' }, fileType: null }));

    await screen.findByTestId('record-preview-image', undefined, WAIT);
    expect((createObjectURL.mock.calls[0][0] as Blob).type).toBe('image/jpeg');
  });

  it('shows a loading state while the bytes are on their way', async () => {
    let release: (blob: Blob) => void = () => undefined;
    bffApiBlob.mockReturnValue(new Promise<Blob>(resolve => (release = resolve)));

    render(view({ documentId: DOC, file: FILE, fileType: 'PDF' }));

    expect(screen.getByTestId('record-preview-loading')).toBeTruthy();
    expect(screen.queryByTestId('record-preview-pdf')).toBeNull();
    release(new Blob(['%PDF']));
    await screen.findByTestId('record-preview-pdf', undefined, WAIT);
  });

  it('reads only the content route, once, with the 25 MB cap and an abort signal', async () => {
    bffApiBlob.mockResolvedValue(new Blob(['%PDF']));

    render(view({ documentId: DOC, file: FILE, fileType: 'PDF' }));
    await screen.findByTestId('record-preview-pdf', undefined, WAIT);

    expect(bffApiBlob).toHaveBeenCalledTimes(1);
    const [path, options, limits] = bffApiBlob.mock.calls[0] as [string, RequestInit, { maxBytes: number }];
    expect(path).toBe(contentPath('matters', MATTER));
    expect(options.signal).toBeInstanceOf(AbortSignal);
    expect(limits).toEqual({ maxBytes: 26_214_400 });
  });
});

// ---------------------------------------------------------------------------
// Download only: other types, too large, no file
// ---------------------------------------------------------------------------

describe('DocumentPreview: Download only', () => {
  it('a .docx shows the Download-only note and reads nothing', () => {
    render(view({ documentId: DOC, file: { ...FILE, name: 'Contract.docx' }, fileType: 'DOCX' }));

    expect(screen.getByTestId('record-preview-download-only').textContent).toMatch(/Use Download in the toolbar/);
    expect(screen.queryByTestId('record-preview-stage')).toBeNull();
    expect(bffApiBlob).not.toHaveBeenCalled();
    expect(createObjectURL).not.toHaveBeenCalled();
  });

  it('a present non-previewable type wins over a ".pdf" name', () => {
    render(view({ documentId: DOC, file: FILE, fileType: 'DOCX' }));

    expect(screen.getByTestId('record-preview-download-only')).toBeTruthy();
    expect(bffApiBlob).not.toHaveBeenCalled();
  });

  it('a file over 25 MB (the read refuses it) shows Download only and renders nothing', async () => {
    bffApiBlob.mockRejectedValue(new BlobTooLargeError(PREVIEW_MAX_BYTES));

    render(view({ documentId: DOC, file: FILE, fileType: 'PDF' }));

    const note = await screen.findByTestId('record-preview-too-large', undefined, WAIT);
    expect(note.textContent).toMatch(/larger than 25 MB/);
    expect(note.textContent).toMatch(/Use Download in the toolbar/);
    expect(screen.queryByTestId('record-preview-pdf')).toBeNull();
    expect(screen.queryByTestId('record-preview-image')).toBeNull();
    expect(createObjectURL).not.toHaveBeenCalled();
    // The size refusal is not a refusal of access: no second parent is tried.
    expect(bffApiBlob).toHaveBeenCalledTimes(1);
  });

  it('a document with no file shows no preview and makes no content request (FR-15)', () => {
    render(view({ documentId: DOC, file: null, fileType: 'PDF' }));

    expect(screen.getByTestId('record-preview-no-file')).toBeTruthy();
    expect(screen.queryByTestId('record-preview-stage')).toBeNull();
    expect(bffApiBlob).not.toHaveBeenCalled();
    expect(createObjectURL).not.toHaveBeenCalled();
  });
});

// ---------------------------------------------------------------------------
// Parents and failures
// ---------------------------------------------------------------------------

describe('DocumentPreview: parents and failures', () => {
  it('a 403 from the first parent moves to the next, in the BFF order', async () => {
    bffApiBlob.mockImplementation(async (path: string) => {
      if (path === contentPath('projects', PROJECT)) throw new ApiError(403, problem(403));
      return new Blob(['%PDF']);
    });

    render(view({ documentId: DOC, file: TWO_PARENTS, fileType: 'PDF' }));

    await screen.findByTestId('record-preview-pdf', undefined, WAIT);
    expect(bffApiBlob.mock.calls.map(c => c[0])).toEqual([
      contentPath('projects', PROJECT),
      contentPath('matters', MATTER),
    ]);
    expect(screen.queryByTestId('record-preview-error')).toBeNull();
  });

  it('every parent refusing shows the shared access text, never the server detail', async () => {
    bffApiBlob.mockRejectedValue(new ApiError(403, problem(403)));

    render(view({ documentId: DOC, file: TWO_PARENTS, fileType: 'PDF' }));

    const error = await screen.findByTestId('record-preview-error', undefined, WAIT);
    expect(error.textContent).toMatch(/do not have access to download this document/i);
    expect(error.textContent).not.toMatch(/server detail text/);
    expect(bffApiBlob).toHaveBeenCalledTimes(2);
    expect(screen.queryByTestId('record-preview-pdf')).toBeNull();
  });

  it('a failure that is not a refusal stops at the first parent and shows the shared failure text', async () => {
    bffApiBlob.mockRejectedValue(new ApiError(500, problem(500)));

    render(view({ documentId: DOC, file: TWO_PARENTS, fileType: 'PDF' }));

    const error = await screen.findByTestId('record-preview-error', undefined, WAIT);
    expect(error.textContent).toMatch(/could not be downloaded just now/i);
    expect(bffApiBlob).toHaveBeenCalledTimes(1);
  });
});

// ---------------------------------------------------------------------------
// The object URL's lifetime
// ---------------------------------------------------------------------------

describe('DocumentPreview: object URL lifetime', () => {
  it('revokes the object URL when the section unmounts', async () => {
    bffApiBlob.mockResolvedValue(new Blob(['%PDF']));
    const { unmount } = render(view({ documentId: DOC, file: FILE, fileType: 'PDF' }));
    await screen.findByTestId('record-preview-pdf', undefined, WAIT);
    expect(revokeObjectURL).not.toHaveBeenCalled();

    unmount();

    expect(revokeObjectURL).toHaveBeenCalledTimes(1);
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:preview-1');
  });

  it('revokes the old URL when the shown document changes (record paging) and never shows it for the new one', async () => {
    bffApiBlob.mockResolvedValue(new Blob(['%PDF']));
    const { rerender } = render(view({ documentId: DOC, file: FILE, fileType: 'PDF' }));
    await screen.findByTestId('record-preview-pdf', undefined, WAIT);

    let release: (blob: Blob) => void = () => undefined;
    bffApiBlob.mockReturnValue(new Promise<Blob>(resolve => (release = resolve)));
    rerender(view({ documentId: DOC_2, file: { ...FILE, name: 'Second.pdf' }, fileType: 'PDF' }));

    expect(revokeObjectURL).toHaveBeenCalledWith('blob:preview-1');
    // While the next one loads the stage shows the spinner, not the previous document's frame.
    expect(screen.getByTestId('record-preview-loading')).toBeTruthy();
    expect(screen.queryByTestId('record-preview-pdf')).toBeNull();

    release(new Blob(['%PDF']));
    const frame = await screen.findByTestId('record-preview-pdf', undefined, WAIT);
    expect(frame.getAttribute('src')).toBe('blob:preview-2');
    expect(frame.getAttribute('title')).toBe('Preview of Second.pdf');
    expect(bffApiBlob.mock.calls[1][0]).toBe(contentPath('matters', MATTER, DOC_2));
    // Only the current document's URL is alive.
    expect(revokeObjectURL).toHaveBeenCalledTimes(1);
  });

  it('paging through documents and back leaves only the current URL alive', async () => {
    bffApiBlob.mockResolvedValue(new Blob(['bytes']));
    const { rerender } = render(view({ documentId: DOC, file: FILE, fileType: 'PDF' }));
    await screen.findByTestId('record-preview-pdf', undefined, WAIT);

    for (const id of [DOC_2, DOC, DOC_2]) {
      rerender(view({ documentId: id, file: FILE, fileType: 'PDF' }));
      await screen.findByTestId('record-preview-pdf', undefined, WAIT);
    }

    const alive = createObjectURL.mock.results
      .map(r => r.value as string)
      .filter(url => !revokeObjectURL.mock.calls.some(c => c[0] === url));
    expect(alive).toEqual(['blob:preview-4']);
  });

  it('paging from a previewable document to one with no file revokes the URL and reads nothing more', async () => {
    bffApiBlob.mockResolvedValue(new Blob(['%PDF']));
    const { rerender } = render(view({ documentId: DOC, file: FILE, fileType: 'PDF' }));
    await screen.findByTestId('record-preview-pdf', undefined, WAIT);

    rerender(view({ documentId: DOC_2, file: null, fileType: 'PDF' }));

    expect(revokeObjectURL).toHaveBeenCalledWith('blob:preview-1');
    expect(screen.getByTestId('record-preview-no-file')).toBeTruthy();
    expect(bffApiBlob).toHaveBeenCalledTimes(1);
  });

  it('an answer that arrives after the section is gone is dropped: no object URL is made', async () => {
    let release: (blob: Blob) => void = () => undefined;
    bffApiBlob.mockReturnValue(new Promise<Blob>(resolve => (release = resolve)));
    const { unmount } = render(view({ documentId: DOC, file: FILE, fileType: 'PDF' }));
    const signal = (bffApiBlob.mock.calls[0][1] as RequestInit).signal as AbortSignal;
    expect(signal.aborted).toBe(false);

    unmount();
    expect(signal.aborted).toBe(true);
    release(new Blob(['%PDF']));
    await new Promise(resolve => setTimeout(resolve, 20));

    expect(createObjectURL).not.toHaveBeenCalled();
  });

  it('a re-render of the same document (a new record object, the same file) does not read again', async () => {
    bffApiBlob.mockResolvedValue(new Blob(['%PDF']));
    const { rerender } = render(view({ documentId: DOC, file: FILE, fileType: 'PDF' }));
    await screen.findByTestId('record-preview-pdf', undefined, WAIT);

    rerender(view({ documentId: DOC, file: { ...FILE, parents: [...FILE.parents] }, fileType: 'PDF' }));

    expect(bffApiBlob).toHaveBeenCalledTimes(1);
    expect(revokeObjectURL).not.toHaveBeenCalled();
  });
});

// ---------------------------------------------------------------------------
// The section: what it hands the preview
// ---------------------------------------------------------------------------

describe('PreviewSection', () => {
  function section(record: Record<string, unknown>, recordId = DOC): React.ReactElement {
    return (
      <FluentProvider theme={webLightTheme}>
        <PreviewSection {...({ record, recordId } as unknown as RecordSectionContext)} />
      </FluentProvider>
    );
  }
  const linked = {
    sprk_documentname: 'Contract.pdf',
    sprk_matter: { id: MATTER, logicalName: 'sprk_matter', name: 'M' },
  };

  it("previews the document through its linked parent, using the row's sprk_filetype", async () => {
    bffApiBlob.mockResolvedValue(new Blob(['bytes']));

    render(section({ ...linked, sprk_documentname: 'scan', sprk_filetype: 'PNG' }));

    await screen.findByTestId('record-preview-image', undefined, WAIT);
    expect(bffApiBlob.mock.calls[0][0]).toBe(contentPath('matters', MATTER));
  });

  it('hasFile === false shows no preview and makes no content request', () => {
    render(section({ ...linked, sprk_filetype: 'PDF', hasFile: false }));

    expect(screen.getByTestId('record-preview-no-file')).toBeTruthy();
    expect(bffApiBlob).not.toHaveBeenCalled();
  });

  it('a document with no parent link shows no preview and makes no content request', () => {
    render(section({ sprk_documentname: 'Contract.pdf', sprk_filetype: 'PDF' }));

    expect(screen.getByTestId('record-preview-no-file')).toBeTruthy();
    expect(bffApiBlob).not.toHaveBeenCalled();
  });

  it('a hasFile row with no hasFile member (an older BFF) still previews', async () => {
    bffApiBlob.mockResolvedValue(new Blob(['%PDF']));

    render(section({ ...linked }));

    await screen.findByTestId('record-preview-pdf', undefined, WAIT);
  });
});
