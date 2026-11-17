/**
 * openInDesktopApp — the one "Open in desktop" behaviour every surface uses (ISS-004, #2807):
 * Office files open through the protocol link; every other file downloads through the BFF `/content`
 * route (never the SharePoint webUrl, which a read-only user on a secure record cannot open).
 *
 * KEEP (shared-lib domain logic, ADR-038 §7). The fetch is the caller-supplied module boundary.
 */
import { documentContentUrl, openInDesktopApp } from '../openInDesktopApp';

const BFF = 'https://bff.example.com';

function okBlobResponse(): Response {
  return { ok: true, status: 200, blob: jest.fn().mockResolvedValue(new Blob(['x'])) } as unknown as Response;
}

function captureAnchor(): { getAnchor: () => HTMLAnchorElement | null } {
  let captured: HTMLAnchorElement | null = null;
  const realCreate = document.createElement.bind(document);
  jest.spyOn(document, 'createElement').mockImplementation((tag: string) => {
    const el = realCreate(tag);
    if (tag === 'a') {
      (el as HTMLAnchorElement).click = jest.fn(); // jsdom does not implement navigation
      captured = el as HTMLAnchorElement;
    }
    return el;
  });
  return { getAnchor: () => captured };
}

beforeEach(() => {
  jest.useFakeTimers();
  (URL as unknown as { createObjectURL: () => string }).createObjectURL = jest.fn(() => 'blob:test');
  (URL as unknown as { revokeObjectURL: () => void }).revokeObjectURL = jest.fn();
});

afterEach(() => {
  jest.useRealTimers();
  jest.restoreAllMocks();
});

describe('openInDesktopApp', () => {
  test('Office file: hands the desktop link to the Office app and does not download', async () => {
    const fetch = jest.fn();
    const open = jest.spyOn(window, 'open').mockImplementation(() => null);
    // jsdom logs "navigation not implemented" when location.href is assigned.
    jest.spyOn(console, 'error').mockImplementation(() => {});

    const result = await openInDesktopApp({
      bffBaseUrl: BFF,
      documentId: 'doc-1',
      links: { desktopUrl: 'ms-word:https://contoso/doc.docx', fileName: 'doc.docx' },
      fetch,
    });

    expect(result).toBe('office-app');
    expect(fetch).not.toHaveBeenCalled();
    expect(open).not.toHaveBeenCalled(); // same tab, no empty window left behind
  });

  test('non-Office file: downloads through /content with the file name from open-links', async () => {
    const fetch = jest.fn().mockResolvedValue(okBlobResponse());
    const { getAnchor } = captureAnchor();

    const result = await openInDesktopApp({
      bffBaseUrl: BFF,
      documentId: 'doc 1',
      links: { desktopUrl: null, fileName: 'contract.pdf' },
      fetch,
      fallbackFileName: 'Record name',
    });

    expect(result).toBe('downloaded');
    expect(fetch).toHaveBeenCalledWith(`${BFF}/api/documents/doc%201/content`);
    expect(getAnchor()!.download).toBe('contract.pdf');
    expect(getAnchor()!.click).toHaveBeenCalledTimes(1);
    jest.runAllTimers();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:test');
  });

  test('non-Office file without a file name: uses the fallback, then "document"', async () => {
    const fetch = jest.fn().mockResolvedValue(okBlobResponse());
    const { getAnchor } = captureAnchor();

    await openInDesktopApp({ bffBaseUrl: BFF, documentId: 'd', links: {}, fetch, fallbackFileName: 'Lease' });
    expect(getAnchor()!.download).toBe('Lease');

    await openInDesktopApp({ bffBaseUrl: BFF, documentId: 'd', links: {}, fetch });
    expect(getAnchor()!.download).toBe('document');
  });

  test('a failed download throws so the surface can show an error, and saves nothing', async () => {
    const fetch = jest.fn().mockResolvedValue({ ok: false, status: 403 } as Response);
    const { getAnchor } = captureAnchor();

    await expect(
      openInDesktopApp({ bffBaseUrl: BFF, documentId: 'd', links: { fileName: 'a.pdf' }, fetch })
    ).rejects.toThrow('HTTP 403');
    expect(getAnchor()).toBeNull();
  });

  test('a fetch that throws (authenticatedFetch on non-2xx) propagates', async () => {
    const fetch = jest.fn().mockRejectedValue(new Error('The document was not found.'));

    await expect(openInDesktopApp({ bffBaseUrl: BFF, documentId: 'd', links: {}, fetch })).rejects.toThrow(
      'The document was not found.'
    );
  });
});

describe('documentContentUrl', () => {
  test('a trailing slash or /api on the BFF host never produces /api/api', () => {
    expect(documentContentUrl(`${BFF}/`, 'abc')).toBe(`${BFF}/api/documents/abc/content`);
    expect(documentContentUrl(`${BFF}/api`, 'abc')).toBe(`${BFF}/api/documents/abc/content`);
    expect(documentContentUrl(`${BFF}/api/`, 'abc')).toBe(`${BFF}/api/documents/abc/content`);
  });
});
