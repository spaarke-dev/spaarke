/**
 * Unit tests for sendEmailService (spaarkeai-word-add-in-r1 task 036 / FR-15).
 *
 * Covers the task's acceptance criteria:
 *   - both a document and a related record → both links in the composed body
 *   - a document with no related record → document link only, no error
 *   - a related record but no resolved document → record link only
 *   - the document link is minted via POST /api/documents/{documentId}/share-link with NO body
 *     (no expiry override, no alternative route)
 *   - a share-link minting failure → an 'error' outcome; the caller must not open a compose window
 *   - neither a document nor a related record → 'nothing-to-send' (defensive; the UI hides the
 *     affordance in this state, but the service itself must not throw)
 */

import { prepareSendEmail } from '../sendEmailService';
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
const RECORD_ID = '22222222-2222-2222-2222-222222222222';
const ORG_URL = 'https://contoso.crm.dynamics.com';

describe('prepareSendEmail', () => {
  beforeEach(() => {
    mockPost.mockReset();
  });

  it('composes both links when both a document and a related record are given', async () => {
    mockPost.mockResolvedValue({
      url: 'https://contoso.sharepoint.com/share/abc',
      expiresAt: '2026-10-01T00:00:00Z',
      scope: 'organization',
    });

    const result = await prepareSendEmail({
      document: { documentId: DOCUMENT_ID },
      relatedRecord: { entityType: 'sprk_matter', id: RECORD_ID, typeLabel: 'Matter', displayName: 'Smith v. Jones' },
      subject: 'Contract Review',
      orgUrl: ORG_URL,
    });

    expect(result.kind).toBe('ready');
    if (result.kind !== 'ready') throw new Error('expected ready');
    expect(result.content.subject).toBe('Contract Review');
    expect(result.content.htmlBody).toContain('https://contoso.sharepoint.com/share/abc');
    // href attribute values are HTML-escaped (`&` -> `&amp;`) by buildComposeBody — correct output, not a bug.
    expect(result.content.htmlBody).toContain(
      `${ORG_URL}/main.aspx?etn=sprk_matter&amp;id=${RECORD_ID}&amp;pagetype=entityrecord`
    );
    expect(result.content.htmlBody).toContain('Smith v. Jones');
  });

  it('mints the share link via POST /api/documents/{documentId}/share-link with no body (no expiry override)', async () => {
    mockPost.mockResolvedValue({
      url: 'https://contoso.sharepoint.com/share/abc',
      expiresAt: '2026-10-01T00:00:00Z',
      scope: 'organization',
    });

    await prepareSendEmail({
      document: { documentId: DOCUMENT_ID },
      relatedRecord: null,
      subject: 'x',
      orgUrl: ORG_URL,
    });

    expect(mockPost).toHaveBeenCalledTimes(1);
    expect(mockPost).toHaveBeenCalledWith(`/api/documents/${DOCUMENT_ID}/share-link`);
  });

  it('document with no related record → document link only, no error', async () => {
    mockPost.mockResolvedValue({
      url: 'https://contoso.sharepoint.com/share/abc',
      expiresAt: '2026-10-01T00:00:00Z',
      scope: 'organization',
    });

    const result = await prepareSendEmail({
      document: { documentId: DOCUMENT_ID },
      relatedRecord: null,
      subject: 'x',
      orgUrl: ORG_URL,
    });

    expect(result.kind).toBe('ready');
    if (result.kind !== 'ready') throw new Error('expected ready');
    expect(result.content.htmlBody).toContain('https://contoso.sharepoint.com/share/abc');
    expect(result.content.htmlBody).not.toContain('main.aspx');
  });

  it('related record but no resolved document → record link only, no network call', async () => {
    const result = await prepareSendEmail({
      document: null,
      relatedRecord: { entityType: 'sprk_matter', id: RECORD_ID, typeLabel: 'Matter', displayName: 'Smith v. Jones' },
      subject: 'x',
      orgUrl: ORG_URL,
    });

    expect(mockPost).not.toHaveBeenCalled();
    expect(result.kind).toBe('ready');
    if (result.kind !== 'ready') throw new Error('expected ready');
    expect(result.content.htmlBody).toContain(
      `${ORG_URL}/main.aspx?etn=sprk_matter&amp;id=${RECORD_ID}&amp;pagetype=entityrecord`
    );
  });

  it('a share-link minting failure returns an error outcome (caller must not open compose)', async () => {
    mockPost.mockRejectedValue(new ApiClientError({ type: 'about:blank', title: 'Forbidden', status: 403 }));

    const result = await prepareSendEmail({
      document: { documentId: DOCUMENT_ID },
      relatedRecord: { entityType: 'sprk_matter', id: RECORD_ID, typeLabel: 'Matter', displayName: 'Smith v. Jones' },
      subject: 'x',
      orgUrl: ORG_URL,
    });

    expect(result.kind).toBe('error');
  });

  it('a minting failure blocks composition even when a related record link is also available', async () => {
    mockPost.mockRejectedValue(new ApiClientError({ type: 'about:blank', title: 'Forbidden', status: 403 }));

    const result = await prepareSendEmail({
      document: { documentId: DOCUMENT_ID },
      relatedRecord: { entityType: 'sprk_matter', id: RECORD_ID, typeLabel: 'Matter', displayName: 'Smith v. Jones' },
      subject: 'x',
      orgUrl: ORG_URL,
    });

    // Never a 'ready' result containing only the record link when the document was requested but failed.
    expect(result.kind).not.toBe('ready');
  });

  it('neither a document nor a related record → nothing-to-send, no network call', async () => {
    const result = await prepareSendEmail({
      document: null,
      relatedRecord: null,
      subject: 'x',
      orgUrl: ORG_URL,
    });

    expect(result.kind).toBe('nothing-to-send');
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('a related record with no ORG_URL configured and no document → nothing-to-send (no broken link)', async () => {
    const result = await prepareSendEmail({
      document: null,
      relatedRecord: { entityType: 'sprk_matter', id: RECORD_ID, displayName: 'Smith v. Jones' },
      subject: 'x',
      orgUrl: undefined,
    });

    expect(result.kind).toBe('nothing-to-send');
  });

  it('falls back to a generic subject when the given subject is blank', async () => {
    mockPost.mockResolvedValue({
      url: 'https://contoso.sharepoint.com/share/abc',
      expiresAt: '2026-10-01T00:00:00Z',
      scope: 'organization',
    });

    const result = await prepareSendEmail({
      document: { documentId: DOCUMENT_ID },
      relatedRecord: null,
      subject: '   ',
      orgUrl: ORG_URL,
    });

    expect(result.kind).toBe('ready');
    if (result.kind !== 'ready') throw new Error('expected ready');
    expect(result.content.subject).toBe('Document from Spaarke');
  });

  it('HTML-escapes an unsafe display name in the record link label', async () => {
    const result = await prepareSendEmail({
      document: null,
      relatedRecord: { entityType: 'sprk_matter', id: RECORD_ID, displayName: '<script>alert(1)</script>' },
      subject: 'x',
      orgUrl: ORG_URL,
    });

    expect(result.kind).toBe('ready');
    if (result.kind !== 'ready') throw new Error('expected ready');
    expect(result.content.htmlBody).not.toContain('<script>');
    expect(result.content.htmlBody).toContain('&lt;script&gt;');
  });
});
