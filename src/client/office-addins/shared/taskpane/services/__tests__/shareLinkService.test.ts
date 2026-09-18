/**
 * Unit tests for shareLinkService (spaarkeai-word-add-in-r1 task 037 / FR-17 — Word ribbon
 * shareDocument).
 *
 * Covers:
 *   - mints via POST /api/documents/{documentId}/share-link with NO body (no expiry override)
 *   - the document id is canonicalized (ADR-044) before it reaches the URL
 *   - a missing/empty document id never calls the network
 *   - a 401/403 maps to a friendly permission message
 *   - a ProblemDetails-shaped failure surfaces its `detail`
 *   - a network/thrown-Error failure surfaces its message
 */

import { mintDocumentShareLink } from '../shareLinkService';
import { apiClient, ApiClientError } from '@shared/services';

jest.mock('@shared/services', () => {
  const actual = jest.requireActual('@shared/services');
  return {
    ...actual,
    apiClient: {
      get: jest.fn(),
      post: jest.fn(),
      put: jest.fn(),
      delete: jest.fn(),
      uploadFile: jest.fn(),
      configure: jest.fn(),
    },
  };
});

const mockPost = apiClient.post as jest.Mock;

const DOCUMENT_ID = '11111111-1111-1111-1111-111111111111';

describe('mintDocumentShareLink', () => {
  beforeEach(() => {
    mockPost.mockReset();
  });

  it('mints via POST /api/documents/{documentId}/share-link with no body', async () => {
    mockPost.mockResolvedValue({
      url: 'https://contoso.sharepoint.com/share/abc',
      expiresAt: '2026-10-01T00:00:00Z',
      scope: 'organization',
    });

    const result = await mintDocumentShareLink(DOCUMENT_ID);

    expect(result).toEqual({ ok: true, url: 'https://contoso.sharepoint.com/share/abc' });
    expect(mockPost).toHaveBeenCalledWith(`/api/documents/${DOCUMENT_ID}/share-link`);
    expect(mockPost.mock.calls[0]).toHaveLength(1); // no body / no expiry override
  });

  it('canonicalizes a braced/uppercase document id before it reaches the URL (ADR-044)', async () => {
    mockPost.mockResolvedValue({ url: 'https://x/y', expiresAt: '2026-10-01T00:00:00Z', scope: 'organization' });

    await mintDocumentShareLink(`{${DOCUMENT_ID.toUpperCase()}}`);

    expect(mockPost).toHaveBeenCalledWith(`/api/documents/${DOCUMENT_ID}/share-link`);
  });

  it('never calls the network for a missing document id', async () => {
    const result = await mintDocumentShareLink('');
    expect(result).toEqual({ ok: false, message: 'No document id was available to share.' });
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('maps a 403 to a friendly permission message', async () => {
    mockPost.mockRejectedValue(new ApiClientError({ type: 'about:blank', title: 'Forbidden', status: 403 }));

    const result = await mintDocumentShareLink(DOCUMENT_ID);
    expect(result).toEqual({ ok: false, message: "You don't have permission to share this document." });
  });

  it('maps a 401 to the same friendly permission message', async () => {
    mockPost.mockRejectedValue(new ApiClientError({ type: 'about:blank', title: 'Unauthorized', status: 401 }));

    const result = await mintDocumentShareLink(DOCUMENT_ID);
    expect(result).toEqual({ ok: false, message: "You don't have permission to share this document." });
  });

  it('surfaces a ProblemDetails detail for a non-auth server failure', async () => {
    mockPost.mockRejectedValue(
      new ApiClientError({ type: 'about:blank', title: 'Server error', status: 500, detail: 'Document not found' })
    );

    const result = await mintDocumentShareLink(DOCUMENT_ID);
    expect(result).toEqual({ ok: false, message: 'Document not found' });
  });

  it('surfaces a thrown non-ApiClientError message (network failure)', async () => {
    mockPost.mockRejectedValue(new Error('Network request failed'));

    const result = await mintDocumentShareLink(DOCUMENT_ID);
    expect(result).toEqual({ ok: false, message: 'Network request failed' });
  });
});
