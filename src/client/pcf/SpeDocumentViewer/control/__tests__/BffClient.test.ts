/**
 * SpeDocumentViewer BffClient — URL construction goes through buildBffApiUrl
 * (constraints/auth.md "BFF Base URL Convention"), so a host-only base URL with or
 * without a trailing slash yields exactly one `/api/` segment.
 */

const mockAuthenticatedFetch = jest.fn();
jest.mock('@spaarke/auth', () => {
  // The real helper (library source; its dist is ESM, which Jest's CJS runtime cannot load),
  // so the test exercises the canonical rule rather than a copy of it.
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  const { buildBffApiUrl } = require('../../../../shared/Spaarke.Auth/src/buildBffApiUrl');
  class ApiError extends Error {
    constructor(
      public status: number,
      public problemDetails?: unknown
    ) {
      super(`HTTP ${status}`);
    }
  }
  return {
    authenticatedFetch: (...args: unknown[]) => mockAuthenticatedFetch(...args),
    buildBffApiUrl,
    ApiError,
  };
});

import { BffClient, BffProblemError, DocumentLockedException, describeDeleteFailure } from '../BffClient';

const DOC_ID = '3f2504e0-4f89-11d3-9a0c-0305e82c3301';

// The mocked ApiError above takes (status, problemDetails); the real one takes (message, status, problemDetails).
const { ApiError: MockApiError } = jest.requireMock('@spaarke/auth') as {
  ApiError: new (status: number, problemDetails?: unknown) => Error;
};

beforeEach(() => {
  mockAuthenticatedFetch.mockReset();
  mockAuthenticatedFetch.mockResolvedValue({ json: async () => ({ documentInfo: { name: 'a.docx' } }) });
});

// The host-only base comes from getApiBaseUrl(), which strips the env var's `/api` suffix.
it.each(['https://bff.example.net', 'https://bff.example.net/'])(
  'calls /api/documents/{id}/view-url exactly once-prefixed for base %p',
  async base => {
    await new BffClient(base).getViewUrl(DOC_ID, 'corr-1');

    expect(mockAuthenticatedFetch).toHaveBeenCalledTimes(1);
    expect(mockAuthenticatedFetch.mock.calls[0][0]).toBe(`https://bff.example.net/api/documents/${DOC_ID}/view-url`);
  }
);

it('builds the v1 download URL with a single /api segment', () => {
  expect(new BffClient('https://bff.example.net/').getDownloadUrl(DOC_ID)).toBe(
    `https://bff.example.net/api/v1/documents/${DOC_ID}/download`
  );
});

it('refuses to build a URL from an empty base (no relative-URL fallback)', () => {
  expect(() => new BffClient('').getDownloadUrl(DOC_ID)).toThrow(/baseUrl is empty/);
});

// R3 ISS-004 review F2 (#1650): a delete answers 409 for more than a lock, and 503 when the document was kept because its
// file could not be deleted. The exception is chosen by the problem's code, and the user is shown the BFF's `detail`.
describe('deleteDocument failures', () => {
  const lockedByPaula = {
    error: 'document_locked',
    detail: 'Cannot delete document. It is checked out by Paula Partner.',
    checkedOutBy: { id: '11111111-1111-1111-1111-111111111111', name: 'Paula Partner', email: null },
    checkedOutAt: null,
  };

  // BffClient logs every request and failure; keep the test output readable.
  beforeEach(() => {
    jest.spyOn(console, 'log').mockImplementation(() => undefined);
    jest.spyOn(console, 'error').mockImplementation(() => undefined);
  });
  afterEach(() => jest.restoreAllMocks());

  const failWith = (status: number, problem: unknown) =>
    mockAuthenticatedFetch.mockRejectedValue(new MockApiError(status, problem));

  const deleteAndCatch = async (): Promise<unknown> => {
    try {
      await new BffClient('https://bff.example.net').deleteDocument(DOC_ID, 'corr-1');
    } catch (error) {
      return error;
    }
    throw new Error('deleteDocument was expected to fail');
  };

  it('calls DELETE on /api/documents/{id} and returns the BFF answer on success', async () => {
    mockAuthenticatedFetch.mockResolvedValue({
      json: async () => ({ success: true, message: 'ok', correlationId: 'c' }),
    });

    const result = await new BffClient('https://bff.example.net').deleteDocument(DOC_ID, 'corr-1');

    expect(result.success).toBe(true);
    expect(mockAuthenticatedFetch.mock.calls[0][0]).toBe(`https://bff.example.net/api/documents/${DOC_ID}`);
    expect(mockAuthenticatedFetch.mock.calls[0][1]).toMatchObject({ method: 'DELETE' });
  });

  it('a 409 whose reasonCode is document_locked is a DocumentLockedException', async () => {
    failWith(409, { title: 'Conflict', status: 409, reasonCode: 'document_locked', ...lockedByPaula });

    const error = await deleteAndCatch();

    expect(error).toBeInstanceOf(DocumentLockedException);
    expect((error as DocumentLockedException).message).toBe(lockedByPaula.detail);
  });

  it('a 409 lock body that names its code in "error" (the DocumentLockedError shape) is a DocumentLockedException', async () => {
    failWith(409, { title: 'Conflict', status: 409, ...lockedByPaula });

    expect(await deleteAndCatch()).toBeInstanceOf(DocumentLockedException);
  });

  it.each([
    [
      'the pointer was refused',
      'document_storage_unverified',
      "The document's file could not be deleted, so the document was kept. Nothing was deleted.",
    ],
    [
      'the document has child documents',
      'document_has_child_documents',
      "The document was not deleted: other documents, such as an email's attachments, name it as their parent.",
    ],
  ])(
    'a 409 because %s is NOT a lock: a BffProblemError carrying the detail and the code',
    async (_why, reasonCode, detail) => {
      failWith(409, { title: 'Document not deleted', status: 409, detail, reasonCode });

      const error = await deleteAndCatch();

      expect(error).not.toBeInstanceOf(DocumentLockedException);
      expect(error).toBeInstanceOf(BffProblemError);
      expect((error as BffProblemError).message).toBe(detail);
      expect((error as BffProblemError).status).toBe(409);
      expect((error as BffProblemError).reasonCode).toBe(reasonCode);
    }
  );

  it("a 503 because the file could not be deleted is a BffProblemError with the BFF's retry sentence", async () => {
    const detail =
      "The document's file could not be deleted right now, so the document was kept. Nothing was deleted. Try again shortly.";
    failWith(503, { title: 'Document not deleted', status: 503, detail, reasonCode: 'document_file_not_deleted' });

    const error = await deleteAndCatch();

    expect(error).toBeInstanceOf(BffProblemError);
    expect((error as BffProblemError).message).toBe(detail);
    expect((error as BffProblemError).status).toBe(503);
    expect((error as BffProblemError).reasonCode).toBe('document_file_not_deleted');
  });

  it('a 409 with no readable problem body is still not a lock: the mapped generic message', async () => {
    failWith(409, null);

    const error = await deleteAndCatch();

    expect(error).not.toBeInstanceOf(DocumentLockedException);
    expect(error).not.toBeInstanceOf(BffProblemError);
    expect((error as Error).message).toBe('This document cannot be accessed in its current state.');
  });

  it('other statuses keep their mapped messages (403)', async () => {
    failWith(403, { title: 'Forbidden', status: 403 });

    expect(((await deleteAndCatch()) as Error).message).toBe('You do not have permission to access this file.');
  });

  it('a network failure is rethrown unchanged', async () => {
    const networkFailure = new TypeError('Failed to fetch');
    mockAuthenticatedFetch.mockRejectedValue(networkFailure);

    expect(await deleteAndCatch()).toBe(networkFailure);
  });
});

describe('describeDeleteFailure', () => {
  it('shows the BFF detail of a refused delete as written', () => {
    const refused = new BffProblemError('Nothing was deleted. Try again shortly.', 503, 'document_file_not_deleted');
    expect(describeDeleteFailure(refused)).toBe('Nothing was deleted. Try again shortly.');
  });

  it('shows who holds the lock', () => {
    const locked = new DocumentLockedException({
      error: 'document_locked',
      detail: 'Cannot delete document. It is checked out by Paula Partner.',
      checkedOutBy: { id: 'u', name: 'Paula Partner', email: null },
      checkedOutAt: null,
    });
    expect(describeDeleteFailure(locked)).toBe('Cannot delete document. It is checked out by Paula Partner.');
  });

  it('does not show a raw network message, or a non-Error', () => {
    const neutral = 'The document could not be deleted. Check your connection and try again.';
    expect(describeDeleteFailure(new TypeError('Failed to fetch'))).toBe(neutral);
    expect(describeDeleteFailure('boom')).toBe(neutral);
    expect(describeDeleteFailure(undefined)).toBe(neutral);
  });
});
