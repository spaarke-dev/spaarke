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
// The real error classes and guards stay (the hook reads what authenticatedFetch THROWS).
jest.mock('@spaarke/auth', () => ({
  ...jest.requireActual('@spaarke/auth'),
  authenticatedFetch: jest.fn(),
}));

import { ApiError, AuthError, authenticatedFetch, type OkResponse, type OkStatus } from '@spaarke/auth';
import { useDocumentActions } from '../../src/hooks/useDocumentActions';

const BFF = 'https://bff.example.com';
const mockedFetch = authenticatedFetch as jest.MockedFunction<typeof authenticatedFetch>;
// Minimal SUCCESS Response-like shape that satisfies the hook's reads (.ok, .status,
// .json, .blob, .headers.get). Using a typed factory keeps assertions honest
// without forcing us to construct full Response objects.
function jsonResponse(body: unknown, init: { status?: OkStatus } = {}): OkResponse {
  return {
    ok: true,
    status: init.status ?? 200,
    statusText: 'OK',
    json: jest.fn().mockResolvedValue(body),
    blob: jest.fn().mockResolvedValue(new Blob(['x'])),
    headers: { get: () => null },
  } as unknown as OkResponse;
}

function blobResponse(disposition: string | null = null): OkResponse {
  return {
    ok: true,
    status: 200,
    statusText: 'OK',
    blob: jest.fn().mockResolvedValue(new Blob(['x'])),
    headers: { get: (name: string) => (name === 'Content-Disposition' ? disposition : null) },
  } as unknown as OkResponse;
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

});

describe('useDocumentActions — sendToIndex', () => {
  test('POSTs analyze per id and treats 202 as success', async () => {
    mockedFetch
      .mockResolvedValueOnce(jsonResponse({}, { status: 202 }))
      .mockResolvedValueOnce(jsonResponse({}, { status: 202 }));

    const { result } = renderHook(() => useDocumentActions({ bffBaseUrl: BFF }));

    await act(async () => {
      await result.current.sendToIndex(['a', 'b']);
    });

    expect(mockedFetch).toHaveBeenCalledWith(`${BFF}/api/documents/a/analyze`, { method: 'POST' });
    expect(mockedFetch).toHaveBeenCalledWith(`${BFF}/api/documents/b/analyze`, { method: 'POST' });
    expect(result.current.actionError).toBeNull();
  });

});

// ===========================================================================
// What production sees: `@spaarke/auth`'s authenticatedFetch THROWS ApiError (status + ProblemDetails) or,
// once its 401 retries are spent, AuthError. Before the fix every catch showed the thrown message as-is —
// a bare "HTTP 500" — and the `Failed to …: <status>` frames never ran.
// ===========================================================================

describe('useDocumentActions — thrown failures (production fetch shape)', () => {
  test("delete: a thrown ApiError shows the server's detail in the action frame", async () => {
    const detail = 'The document is checked out by another user.';
    mockedFetch.mockRejectedValueOnce(new ApiError(detail, 409, { title: 'Conflict', status: 409, detail }));

    const { result } = renderHook(() => useDocumentActions({ bffBaseUrl: BFF }));
    await act(async () => {
      await result.current.deleteDocuments(['a'], jest.fn());
    });

    expect(result.current.actionError).toBe(`Couldn't delete the document: ${detail}`);
  });

  test('delete of several: the frame counts them', async () => {
    mockedFetch
      .mockResolvedValueOnce(jsonResponse({}, { status: 204 }))
      .mockRejectedValueOnce(new ApiError('HTTP 403', 403));

    const { result } = renderHook(() => useDocumentActions({ bffBaseUrl: BFF }));
    await act(async () => {
      await result.current.deleteDocuments(['a', 'b'], jest.fn());
    });

    expect(result.current.actionError).toBe(
      "Couldn't delete 2 documents: You do not have permission to do this with this document."
    );
  });

  test('open: a thrown bare 500 reads "temporarily unavailable", not "HTTP 500"', async () => {
    mockedFetch.mockRejectedValueOnce(new ApiError('HTTP 500', 500));

    const { result } = renderHook(() => useDocumentActions({ bffBaseUrl: BFF }));
    await act(async () => {
      await result.current.openInWeb('doc-1');
    });

    expect(result.current.actionError).toBe(
      "Couldn't open the document: The document service is temporarily unavailable. Try again in a few minutes."
    );
  });

  test('open in desktop: a thrown 404 reads "not found"', async () => {
    mockedFetch.mockRejectedValueOnce(new ApiError('HTTP 404', 404));

    const { result } = renderHook(() => useDocumentActions({ bffBaseUrl: BFF }));
    await act(async () => {
      await result.current.openInDesktop('doc-1');
    });

    expect(result.current.actionError).toBe(
      "Couldn't open the document in the desktop app: The document was not found."
    );
  });

  test('download: an expired sign-in (thrown AuthError) says so', async () => {
    mockedFetch.mockRejectedValueOnce(
      new AuthError('Authentication failed after all retry attempts', 'auth_exhausted')
    );

    const { result } = renderHook(() => useDocumentActions({ bffBaseUrl: BFF }));
    await act(async () => {
      await result.current.download('doc-1');
    });

    expect(result.current.actionError).toBe(
      "Couldn't download the document: Your sign-in has expired. Refresh the page to sign in again."
    );
  });

  test('email link: a network failure does not show "Failed to fetch"', async () => {
    mockedFetch.mockRejectedValueOnce(new TypeError('Failed to fetch'));

    const { result } = renderHook(() => useDocumentActions({ bffBaseUrl: BFF }));
    await act(async () => {
      await result.current.emailLink('doc-1');
    });

    expect(result.current.actionError).toBe(
      "Couldn't create the email link: The Spaarke server could not be reached. Check your connection."
    );
  });

  test('send to index: a thrown 429 asks the user to wait', async () => {
    mockedFetch.mockRejectedValueOnce(new ApiError('HTTP 429', 429));

    const { result } = renderHook(() => useDocumentActions({ bffBaseUrl: BFF }));
    await act(async () => {
      await result.current.sendToIndex(['a']);
    });

    expect(result.current.actionError).toBe(
      "Couldn't send the document to the index: Too many requests. Wait a moment and try again."
    );
  });
});
