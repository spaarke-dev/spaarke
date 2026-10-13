/**
 * Task 120 (owner UAT round 12 O6): Outlook knows an email is already saved.
 *
 * - `resolveEmailIdentity` asks `POST /api/documents/resolve-email-identity` with the email's two keys and maps a
 *   resolved answer to the SAME outcome resolve-identity produces. Every other answer — not saved, 403, 503, network,
 *   no Message-ID — is `null`: the pane shows its ordinary save form (no box, no blocked Save).
 * - `identityOfSavedDocument` turns the id a pane save just produced into the saved `.eml`'s identity (task 112's
 *   by-id read); a failed read keeps the record UNKNOWN.
 */

import { identityOfSavedDocument, resolveEmailIdentity } from '../documentIdentityService';
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
const mockPost = apiClient.post as jest.Mock;

const DOC_ID = '6fb8c3fa-13c3-f111-a05c-0022482913fc';
const MATTER_ID = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';
const MESSAGE_ID = '<CAF0a1b2c3@mail.example.com>';
const ITEM_ID = 'AAMkADAxZWI4YzM4LWVjNTgtNDcwOC1iY2EzLTI1MTc1MGU1MmFhMQBGAAAAAAD/abc+def=';

const resolvedResponse = {
  resolved: true,
  documentId: DOC_ID.toUpperCase(),
  documentName: 'RE: Discovery schedule',
  fileName: 'RE_ Discovery schedule_a1b2.eml',
  relatedRecord: {
    entityType: 'sprk_matter',
    id: MATTER_ID,
    name: 'MAT-000042',
    displayName: 'Acme',
    number: 'MAT-000042',
  },
  reason: null,
};

describe('resolveEmailIdentity (task 120)', () => {
  let warn: jest.SpyInstance;
  beforeEach(() => {
    mockPost.mockReset();
    warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
  });
  afterEach(() => warn.mockRestore());

  it('sends both keys in the body and returns the saved .eml as a resolved identity (canonical id)', async () => {
    mockPost.mockResolvedValue(resolvedResponse);

    const outcome = await resolveEmailIdentity({ internetMessageId: ` ${MESSAGE_ID} `, exchangeItemId: ITEM_ID });

    expect(mockPost).toHaveBeenCalledWith('/api/documents/resolve-email-identity', {
      internetMessageId: MESSAGE_ID,
      exchangeItemId: ITEM_ID,
    });
    expect(outcome).toEqual({
      kind: 'resolved',
      documentId: DOC_ID,
      documentName: 'RE: Discovery schedule',
      fileName: 'RE_ Discovery schedule_a1b2.eml',
      relatedRecord: {
        entityType: 'sprk_matter',
        id: MATTER_ID,
        name: 'MAT-000042',
        displayName: 'Acme',
        number: 'MAT-000042',
      },
    });
  });

  it('a saved but unfiled email is a KNOWN-unfiled identity (the filing picker may be offered)', async () => {
    mockPost.mockResolvedValue({ ...resolvedResponse, relatedRecord: null });

    const outcome = await resolveEmailIdentity({ internetMessageId: MESSAGE_ID, exchangeItemId: null });

    expect(mockPost).toHaveBeenCalledWith('/api/documents/resolve-email-identity', { internetMessageId: MESSAGE_ID });
    expect(outcome).toMatchObject({ kind: 'resolved', documentId: DOC_ID, relatedRecord: null });
    expect(outcome && outcome.kind === 'resolved' && outcome.relatedRecordKnown).toBeUndefined();
  });

  it('not saved (200 resolved:false, not_saved) is null — the ordinary save form', async () => {
    mockPost.mockResolvedValue({ resolved: false, documentId: null, reason: 'not_saved' });

    expect(await resolveEmailIdentity({ internetMessageId: MESSAGE_ID, exchangeItemId: ITEM_ID })).toBeNull();
  });

  it.each([
    ['403 (the newest saved copy is not readable)', new ApiClientError({ status: 403, title: 'Forbidden' } as never)],
    ['503', new ApiClientError({ status: 503, title: 'Unavailable' } as never)],
    ['400', new ApiClientError({ status: 400, title: 'Bad Request' } as never)],
    ['a network failure', new Error('network')],
  ])('on %s it is null and never throws', async (_label, failure) => {
    mockPost.mockRejectedValue(failure);

    await expect(resolveEmailIdentity({ internetMessageId: MESSAGE_ID, exchangeItemId: ITEM_ID })).resolves.toBeNull();
  });

  it.each([[null], [undefined], [{ internetMessageId: '  ', exchangeItemId: ITEM_ID }]])(
    'without a Message-ID (%p) it makes no call and is null',
    async keys => {
      expect(await resolveEmailIdentity(keys)).toBeNull();
      expect(mockPost).not.toHaveBeenCalled();
    }
  );
});

describe('identityOfSavedDocument (task 120)', () => {
  beforeEach(() => mockGet.mockReset());

  it('reads the just-saved document by id and returns its full identity', async () => {
    mockGet.mockResolvedValue(resolvedResponse);

    const outcome = await identityOfSavedDocument(`{${DOC_ID.toUpperCase()}}`);

    expect(mockGet).toHaveBeenCalledWith(`/api/documents/${DOC_ID}/identity`);
    expect(outcome).toMatchObject({ kind: 'resolved', documentId: DOC_ID, relatedRecord: { id: MATTER_ID } });
    expect(outcome && outcome.kind === 'resolved' && outcome.relatedRecordKnown).toBeUndefined();
  });

  it('a failed read keeps the id with the record UNKNOWN', async () => {
    mockGet.mockRejectedValue(new ApiClientError({ status: 503, title: 'Unavailable' } as never));

    const outcome = await identityOfSavedDocument(DOC_ID);

    expect(outcome).toEqual({
      kind: 'resolved',
      documentId: DOC_ID,
      documentName: '',
      fileName: '',
      relatedRecord: null,
      relatedRecordKnown: false,
    });
  });

  it.each([[''], ['{}']])('an empty id (%p) is null and makes no call', async id => {
    expect(await identityOfSavedDocument(id)).toBeNull();
    expect(mockGet).not.toHaveBeenCalled();
  });
});
