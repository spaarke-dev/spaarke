/**
 * Task 111: `fileDocumentToRecord` — the pane's "File to record" PUT. Exactly one lookup per request, the matching
 * one for the picked entity type; Account/Contact are refused without a request; 403 and other failures are described,
 * never thrown.
 */
import { buildFilingBody, fileDocumentToRecord } from '../documentFilingService';
import { applyFiledRecord, type DocumentIdentityState } from '../documentIdentityService';

const mockFetch = jest.fn();
global.fetch = mockFetch;

const DOC = '8C135B45-5DA8-F111-AAAB-7CED8DDC4A05';
const REC = '{AAAAAAAA-0000-0000-0000-00000000000A}';

const base = {
  apiBaseUrl: 'https://bff',
  getAccessToken: jest.fn().mockResolvedValue('tok'),
  documentId: DOC,
  recordId: REC,
  recordName: 'Acme v. Beta',
};

beforeEach(() => {
  jest.clearAllMocks();
  mockFetch.mockReset();
});

describe('buildFilingBody', () => {
  it.each([
    ['Matter', 'matterLookup'],
    ['Project', 'projectLookup'],
    ['Invoice', 'invoiceLookup'],
  ] as const)('%s → only %s, canonical id', (type, field) => {
    expect(buildFilingBody(type, REC)).toEqual({ [field]: 'aaaaaaaa-0000-0000-0000-00000000000a' });
  });

  it('is null for Account / Contact (not filing targets)', () => {
    expect(buildFilingBody('Account', REC)).toBeNull();
    expect(buildFilingBody('Contact', REC)).toBeNull();
  });
});

describe('fileDocumentToRecord', () => {
  it('PUTs /api/v1/documents/{id} with the one lookup and returns the filed record', async () => {
    mockFetch.mockResolvedValue({ ok: true, status: 200, json: async () => ({}) });

    const outcome = await fileDocumentToRecord({ ...base, entityType: 'Matter', recordNumber: 'MAT-1' });

    expect(mockFetch).toHaveBeenCalledTimes(1);
    const [url, init] = mockFetch.mock.calls[0]!;
    expect(url).toBe('https://bff/api/v1/documents/8c135b45-5da8-f111-aaab-7ced8ddc4a05');
    expect((init as RequestInit).method).toBe('PUT');
    expect(JSON.parse(String((init as RequestInit).body))).toEqual({
      matterLookup: 'aaaaaaaa-0000-0000-0000-00000000000a',
    });
    expect(outcome).toEqual({
      ok: true,
      record: {
        entityType: 'sprk_matter',
        id: 'aaaaaaaa-0000-0000-0000-00000000000a',
        name: 'Acme v. Beta',
        displayName: 'Acme v. Beta',
        number: 'MAT-1',
      },
    });
  });

  it('refuses an Account without sending a request', async () => {
    const outcome = await fileDocumentToRecord({ ...base, entityType: 'Account' });

    expect(outcome).toMatchObject({ ok: false, reason: 'unsupported' });
    expect(mockFetch).not.toHaveBeenCalled();
  });

  it('403 → forbidden with a permission message', async () => {
    mockFetch.mockResolvedValue({ ok: false, status: 403, json: async () => ({}) });

    const outcome = await fileDocumentToRecord({ ...base, entityType: 'Project' });

    expect(outcome).toMatchObject({ ok: false, reason: 'forbidden' });
  });

  it('a network failure → error, not a throw', async () => {
    mockFetch.mockRejectedValue(new Error('offline'));

    const outcome = await fileDocumentToRecord({ ...base, entityType: 'Invoice' });

    expect(outcome).toMatchObject({ ok: false, reason: 'error' });
  });
});

describe('applyFiledRecord (the identity after filing)', () => {
  const record = { entityType: 'sprk_matter', id: 'aaaaaaaa-0000-0000-0000-00000000000a', name: 'MAT-1' };
  const resolved: DocumentIdentityState = {
    kind: 'resolved',
    documentId: '8c135b45-5da8-f111-aaab-7ced8ddc4a05',
    documentName: 'Doc',
    fileName: 'Doc.docx',
    relatedRecord: null,
  };

  it('puts the record on the same document and drops relatedRecordKnown', () => {
    const next = applyFiledRecord({ ...resolved, relatedRecordKnown: false }, DOC, record);

    expect(next).toEqual({ ...resolved, relatedRecord: record });
    expect(next).not.toHaveProperty('relatedRecordKnown');
  });

  it('leaves a different document, a non-resolved state and undefined alone (same reference)', () => {
    const other = { ...resolved, documentId: '11111111-1111-1111-1111-111111111111' };
    expect(applyFiledRecord(other, DOC, record)).toBe(other);
    expect(applyFiledRecord('checking', DOC, record)).toBe('checking');
    expect(applyFiledRecord(undefined, DOC, record)).toBeUndefined();
    const neu: DocumentIdentityState = { kind: 'new', reason: 'not_cloud_document' };
    expect(applyFiledRecord(neu, DOC, record)).toBe(neu);
  });
});
