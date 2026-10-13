/**
 * Task 112 (owner UAT round 11, item 4): a document identified only by its stamp (e.g. after Quick Save) has no
 * record in its identity. `completeStampIdentity` reads the stamped document's identity from the server
 * (`GET /api/documents/{id}/identity`); on any failure the record stays UNKNOWN, so the pane never offers to file a
 * document whose record it could not read.
 */

import { completeStampIdentity, applyStampPrecedence, type DocumentIdentityOutcome } from '../documentIdentityService';
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

const mockGet = apiClient.get as jest.Mock;

const DOC_ID = '11111111-2222-3333-4444-555555555555';
const MATTER_ID = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';

const stampOnly = (): DocumentIdentityOutcome =>
  applyStampPrecedence({ kind: 'new', reason: 'not_cloud_document' }, `{${DOC_ID.toUpperCase()}}`);

describe('completeStampIdentity (task 112)', () => {
  beforeEach(() => mockGet.mockReset());

  it('reads the stamped document by id and returns a fully known identity (filed)', async () => {
    mockGet.mockResolvedValue({
      resolved: true,
      documentId: DOC_ID,
      documentName: 'Untitled Document',
      fileName: 'Untitled Document.docx',
      relatedRecord: {
        entityType: 'sprk_matter',
        id: MATTER_ID,
        name: 'Real Estate',
        displayName: 'Real Estate',
        number: 'M-1',
      },
    });

    const outcome = await completeStampIdentity(stampOnly());

    expect(mockGet).toHaveBeenCalledWith(`/api/documents/${DOC_ID}/identity`);
    expect(outcome).toEqual({
      kind: 'resolved',
      documentId: DOC_ID,
      documentName: 'Untitled Document',
      fileName: 'Untitled Document.docx',
      relatedRecord: {
        entityType: 'sprk_matter',
        id: MATTER_ID,
        name: 'Real Estate',
        displayName: 'Real Estate',
        number: 'M-1',
      },
    });
    expect(outcome.kind === 'resolved' && outcome.relatedRecordKnown).toBeUndefined();
  });

  it('a stamped document that is not filed becomes KNOWN unfiled (the picker may be offered)', async () => {
    mockGet.mockResolvedValue({
      resolved: true,
      documentId: DOC_ID,
      documentName: 'Doc',
      fileName: 'Doc.docx',
      relatedRecord: null,
    });

    const outcome = await completeStampIdentity(stampOnly());

    expect(outcome).toMatchObject({ kind: 'resolved', documentId: DOC_ID, relatedRecord: null });
    expect(outcome.kind === 'resolved' && outcome.relatedRecordKnown).toBeUndefined();
  });

  it.each([
    ['403 (no read, or an unknown id)', new ApiClientError({ status: 403, title: 'Forbidden' } as never)],
    ['404', new ApiClientError({ status: 404, title: 'Not found' } as never)],
    ['503', new ApiClientError({ status: 503, title: 'Unavailable' } as never)],
    ['a network failure', new Error('network')],
  ])('on %s the record stays unknown (stamp outcome unchanged)', async (_label, failure) => {
    mockGet.mockRejectedValue(failure);
    const before = stampOnly();

    const outcome = await completeStampIdentity(before);

    expect(outcome).toBe(before);
    expect(outcome.kind === 'resolved' && outcome.relatedRecordKnown).toBe(false);
  });

  it('keeps the stamp outcome when the server answers for a different document', async () => {
    mockGet.mockResolvedValue({
      resolved: true,
      documentId: MATTER_ID,
      documentName: 'Other',
      fileName: 'Other.docx',
      relatedRecord: null,
    });
    const before = stampOnly();

    expect(await completeStampIdentity(before)).toBe(before);
  });

  it('does nothing for an identity that is not stamp-only', async () => {
    const urlResolved: DocumentIdentityOutcome = {
      kind: 'resolved',
      documentId: DOC_ID,
      documentName: 'Doc',
      fileName: 'Doc.docx',
      relatedRecord: null,
    };

    expect(await completeStampIdentity(urlResolved)).toBe(urlResolved);
    expect(await completeStampIdentity({ kind: 'new', reason: 'not_cloud_document' })).toEqual({
      kind: 'new',
      reason: 'not_cloud_document',
    });
    expect(mockGet).not.toHaveBeenCalled();
  });
});
