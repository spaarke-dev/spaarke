/**
 * useDocumentActions — unit tests for the canonical hook moved into
 * @spaarke/document-operations by task 031 of spaarkeai-compose-r1.
 *
 * KEEP category (per .claude/constraints/testing.md §1 / ADR-038 §7):
 *   - domain-logic (shared-lib unit tests). The hook is the unit of behavior;
 *     `@spaarke/auth.authenticatedFetch` is a true module boundary (per
 *     §Mock-boundary rules — acceptable mocking, NOT transport-level).
 *
 * Avoids the 17 banned scaffolding patterns:
 *   - NOT Mock<HttpMessageHandler> (B1) — we mock at the module-level
 *     `authenticatedFetch` API, not the wire format.
 *   - NOT DI-registration tests (B3).
 *   - NOT ctor null-checks (B4).
 *   - NOT mirror/coverage-filler/pass-through (B6, B9, B10).
 *
 * Each test asserts observable behavior (state, fetch URL, error semantics),
 * not implementation details.
 */
import { renderHook, act, waitFor } from '@testing-library/react';

// Mock @spaarke/auth at module-boundary BEFORE importing the hook.
jest.mock('@spaarke/auth', () => ({
  authenticatedFetch: jest.fn(),
}));

import { authenticatedFetch } from '@spaarke/auth';
import { useDocumentActions } from '../../src/hooks/useDocumentActions';

const BFF = 'https://bff.example.com';
const mockedFetch = authenticatedFetch as jest.MockedFunction<typeof authenticatedFetch>;

// Minimal Response-like shape that satisfies the hook's reads (.ok, .status,
// .json, .blob, .headers.get). Using a typed factory keeps assertions honest
// without forcing us to construct full Response objects.
function jsonResponse(body: unknown, init: { status?: number; ok?: boolean } = {}): Response {
  const status = init.status ?? 200;
  const ok = init.ok ?? (status >= 200 && status < 300);
  return {
    ok,
    status,
    statusText: ok ? 'OK' : 'Error',
    json: jest.fn().mockResolvedValue(body),
    blob: jest.fn().mockResolvedValue(new Blob(['x'])),
    headers: { get: () => null },
  } as unknown as Response;
}

function blobResponse(disposition: string | null = null): Response {
  return {
    ok: true,
    status: 200,
    statusText: 'OK',
    blob: jest.fn().mockResolvedValue(new Blob(['x'])),
    headers: { get: (name: string) => (name === 'Content-Disposition' ? disposition : null) },
  } as unknown as Response;
}

beforeEach(() => {
  mockedFetch.mockReset();
  // jsdom provides window.confirm — default to "OK" so deleteDocuments runs.
  jest.spyOn(window, 'confirm').mockReturnValue(true);
  jest.spyOn(window, 'open').mockImplementation(() => null);
  // URL.createObjectURL is not implemented in jsdom by default.
  if (typeof URL.createObjectURL !== 'function') {
    (URL as unknown as { createObjectURL: () => string }).createObjectURL = jest.fn(() => 'blob:test');
  } else {
    jest.spyOn(URL, 'createObjectURL').mockReturnValue('blob:test');
  }
  if (typeof URL.revokeObjectURL !== 'function') {
    (URL as unknown as { revokeObjectURL: () => void }).revokeObjectURL = jest.fn();
  } else {
    jest.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
  }
});

afterEach(() => {
  jest.restoreAllMocks();
});

describe('useDocumentActions — hook surface', () => {
  test('initial state — isActing=false, actionError=null', () => {
    const { result } = renderHook(() => useDocumentActions({ bffBaseUrl: BFF }));

    expect(result.current.isActing).toBe(false);
    expect(result.current.actionError).toBeNull();
    expect(typeof result.current.openInWeb).toBe('function');
    expect(typeof result.current.openInDesktop).toBe('function');
    expect(typeof result.current.download).toBe('function');
    expect(typeof result.current.deleteDocuments).toBe('function');
    expect(typeof result.current.emailLink).toBe('function');
    expect(typeof result.current.sendToIndex).toBe('function');
  });
});

describe('useDocumentActions — openInWeb', () => {
  test('on success opens webUrl in new tab and clears error', async () => {
    mockedFetch.mockResolvedValueOnce(
      jsonResponse({ webUrl: 'https://web/doc', mimeType: 'application/pdf', fileName: 'a.pdf' })
    );

    const { result } = renderHook(() => useDocumentActions({ bffBaseUrl: BFF }));

    await act(async () => {
      await result.current.openInWeb('doc-1');
    });

    expect(mockedFetch).toHaveBeenCalledWith(`${BFF}/api/documents/doc-1/open-links`);
    expect(window.open).toHaveBeenCalledWith('https://web/doc', '_blank');
    expect(result.current.actionError).toBeNull();
    expect(result.current.isActing).toBe(false);
  });

  test('on non-OK response sets actionError with status', async () => {
    mockedFetch.mockResolvedValueOnce(jsonResponse({}, { status: 500, ok: false }));

    const { result } = renderHook(() => useDocumentActions({ bffBaseUrl: BFF }));

    await act(async () => {
      await result.current.openInWeb('doc-1');
    });

    expect(window.open).not.toHaveBeenCalled();
    expect(result.current.actionError).toContain('500');
    expect(result.current.isActing).toBe(false);
  });
});

describe('useDocumentActions — openInDesktop', () => {
  test('uses desktopUrl when present', async () => {
    mockedFetch.mockResolvedValueOnce(
      jsonResponse({
        webUrl: 'https://web/doc',
        desktopUrl: 'ms-word:ofe%7Cu%7Chttps://web/doc',
        mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
        fileName: 'a.docx',
      })
    );

    const { result } = renderHook(() => useDocumentActions({ bffBaseUrl: BFF }));

    await act(async () => {
      await result.current.openInDesktop('doc-1');
    });

    expect(window.open).toHaveBeenCalledWith('ms-word:ofe%7Cu%7Chttps://web/doc');
  });

  test('falls back to webUrl when desktopUrl missing', async () => {
    mockedFetch.mockResolvedValueOnce(
      jsonResponse({ webUrl: 'https://web/doc', mimeType: 'application/pdf', fileName: 'a.pdf' })
    );

    const { result } = renderHook(() => useDocumentActions({ bffBaseUrl: BFF }));

    await act(async () => {
      await result.current.openInDesktop('doc-1');
    });

    expect(window.open).toHaveBeenCalledWith('https://web/doc', '_blank');
  });
});

describe('useDocumentActions — download', () => {
  // Intercept document.createElement('a') so we can assert on the synthesized
  // anchor's `download` attribute and bypass jsdom's "navigation not implemented"
  // shim on .click().
  function captureAnchor(): { getAnchor: () => HTMLAnchorElement | null } {
    let captured: HTMLAnchorElement | null = null;
    const realCreate = document.createElement.bind(document);
    jest.spyOn(document, 'createElement').mockImplementation((tag: string) => {
      const el = realCreate(tag);
      if (tag === 'a') {
        // Suppress jsdom navigation attempt on .click()
        (el as HTMLAnchorElement).click = jest.fn();
        captured = el as HTMLAnchorElement;
      }
      return el;
    });
    return { getAnchor: () => captured };
  }

  test('extracts filename from Content-Disposition', async () => {
    const headerValue = 'attachment; filename="annual-report.pdf"';
    mockedFetch.mockResolvedValueOnce(blobResponse(headerValue));
    const { getAnchor } = captureAnchor();

    const { result } = renderHook(() => useDocumentActions({ bffBaseUrl: BFF }));

    await act(async () => {
      await result.current.download('doc-1');
    });

    expect(mockedFetch).toHaveBeenCalledWith(`${BFF}/api/documents/doc-1/download`);
    const anchor = getAnchor();
    expect(anchor).not.toBeNull();
    expect(anchor!.download).toBe('annual-report.pdf');
    expect(anchor!.click).toHaveBeenCalledTimes(1);
  });

  test('falls back to document-{id} when Content-Disposition absent', async () => {
    mockedFetch.mockResolvedValueOnce(blobResponse(null));
    const { getAnchor } = captureAnchor();

    const { result } = renderHook(() => useDocumentActions({ bffBaseUrl: BFF }));

    await act(async () => {
      await result.current.download('xyz-7');
    });

    const anchor = getAnchor();
    expect(anchor!.download).toBe('document-xyz-7');
  });
});

describe('useDocumentActions — deleteDocuments', () => {
  test('issues DELETE per id and invokes onSuccess', async () => {
    mockedFetch
      .mockResolvedValueOnce(jsonResponse({}, { status: 204 }))
      .mockResolvedValueOnce(jsonResponse({}, { status: 204 }));

    const onSuccess = jest.fn();
    const { result } = renderHook(() => useDocumentActions({ bffBaseUrl: BFF }));

    await act(async () => {
      await result.current.deleteDocuments(['a', 'b'], onSuccess);
    });

    expect(mockedFetch).toHaveBeenCalledWith(`${BFF}/api/documents/a`, { method: 'DELETE' });
    expect(mockedFetch).toHaveBeenCalledWith(`${BFF}/api/documents/b`, { method: 'DELETE' });
    expect(onSuccess).toHaveBeenCalledTimes(1);
  });

  test('does NOT issue requests when user cancels confirm dialog', async () => {
    (window.confirm as jest.Mock).mockReturnValueOnce(false);

    const onSuccess = jest.fn();
    const { result } = renderHook(() => useDocumentActions({ bffBaseUrl: BFF }));

    await act(async () => {
      await result.current.deleteDocuments(['a'], onSuccess);
    });

    expect(mockedFetch).not.toHaveBeenCalled();
    expect(onSuccess).not.toHaveBeenCalled();
  });

  test('sets actionError when DELETE returns non-OK', async () => {
    mockedFetch.mockResolvedValueOnce(jsonResponse({}, { status: 403, ok: false }));

    const onSuccess = jest.fn();
    const { result } = renderHook(() => useDocumentActions({ bffBaseUrl: BFF }));

    await act(async () => {
      await result.current.deleteDocuments(['a'], onSuccess);
    });

    expect(onSuccess).not.toHaveBeenCalled();
    await waitFor(() => expect(result.current.actionError).toContain('403'));
  });
});

describe('useDocumentActions — emailLink', () => {
  test('fetches links and completes without error on success', async () => {
    // Direct testing of the mailto URL is brittle under jsdom (window.location
    // is locked down). The observable contract we assert here is:
    //   (a) the open-links endpoint is hit with the right URL,
    //   (b) the hook returns without setting actionError on a successful response.
    // The exact mailto string construction is a one-liner of well-known
    // encodeURIComponent calls; testing it through jsdom's window.location
    // sandbox adds no behavior coverage commensurate with its flakiness.
    mockedFetch.mockResolvedValueOnce(
      jsonResponse({ webUrl: 'https://web/doc', mimeType: 'application/pdf', fileName: 'My File.pdf' })
    );

    const { result } = renderHook(() => useDocumentActions({ bffBaseUrl: BFF }));

    // Silently catch the jsdom "navigation not implemented" log noise that
    // happens when the hook assigns to window.location.href under jsdom.
    const errSpy = jest.spyOn(console, 'error').mockImplementation(() => {});

    await act(async () => {
      await result.current.emailLink('doc-1');
    });

    expect(mockedFetch).toHaveBeenCalledWith(`${BFF}/api/documents/doc-1/open-links`);
    expect(result.current.actionError).toBeNull();
    expect(result.current.isActing).toBe(false);

    errSpy.mockRestore();
  });

  test('sets actionError when open-links returns non-OK', async () => {
    mockedFetch.mockResolvedValueOnce(jsonResponse({}, { status: 404, ok: false }));

    const { result } = renderHook(() => useDocumentActions({ bffBaseUrl: BFF }));

    await act(async () => {
      await result.current.emailLink('missing');
    });

    await waitFor(() => expect(result.current.actionError).toContain('404'));
  });
});

describe('useDocumentActions — sendToIndex', () => {
  test('POSTs analyze per id and treats 202 as success', async () => {
    mockedFetch
      .mockResolvedValueOnce(jsonResponse({}, { status: 202, ok: false }))
      .mockResolvedValueOnce(jsonResponse({}, { status: 202, ok: false }));

    const { result } = renderHook(() => useDocumentActions({ bffBaseUrl: BFF }));

    await act(async () => {
      await result.current.sendToIndex(['a', 'b']);
    });

    expect(mockedFetch).toHaveBeenCalledWith(`${BFF}/api/documents/a/analyze`, { method: 'POST' });
    expect(mockedFetch).toHaveBeenCalledWith(`${BFF}/api/documents/b/analyze`, { method: 'POST' });
    expect(result.current.actionError).toBeNull();
  });

  test('sets actionError when analyze returns non-success status', async () => {
    mockedFetch.mockResolvedValueOnce(jsonResponse({}, { status: 500, ok: false }));

    const { result } = renderHook(() => useDocumentActions({ bffBaseUrl: BFF }));

    await act(async () => {
      await result.current.sendToIndex(['a']);
    });

    await waitFor(() => expect(result.current.actionError).toContain('500'));
  });
});
