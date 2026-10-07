/**
 * Unit tests for sendEmailService (spaarkeai-word-add-in-r1 task 036 / FR-15; rewritten by task 096).
 *
 * Outlook's native-compose Send Email:
 *   - a document and a related record → both SPAARKE record links in the composed body
 *   - task 096: the document link is the document's Spaarke record (`etn=sprk_document`) — never a Graph
 *     sharing link (SharePoint Embedded refuses those: "This sharing scenario is not supported on CSP
 *     Container site"), and composing makes NO network call at all, so it cannot fail that way again
 *   - one source only → that link only; neither, or ORG_URL unset → 'nothing-to-send' (never a broken link)
 *   - `resolveSendEmailAffordance`: Outlook (canComposeEmail) → the button; Word → none (Word has the Email tab)
 */

import {
  prepareSendEmail,
  resolveSendEmailAffordance,
  buildRecordLink,
  buildDocumentRecordLink,
} from '../sendEmailService';
import { apiClient } from '@shared/services';

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
const fetchSpy = jest.fn();

const DOCUMENT_ID = '11111111-1111-1111-1111-111111111111';
const RECORD_ID = '22222222-2222-2222-2222-222222222222';
const ORG_URL = 'https://contoso.crm.dynamics.com';

const MATTER = { entityType: 'sprk_matter', id: RECORD_ID, typeLabel: 'Matter', displayName: 'Smith v. Jones' };

beforeEach(() => {
  mockPost.mockReset();
  fetchSpy.mockReset();
  (global as unknown as { fetch: unknown }).fetch = fetchSpy;
});

describe('prepareSendEmail (Outlook native compose)', () => {
  it('composes the document record link and the related record link', () => {
    const result = prepareSendEmail({
      document: { documentId: DOCUMENT_ID, name: 'Agreement' },
      relatedRecord: MATTER,
      subject: 'Contract Review',
      orgUrl: ORG_URL,
    });

    if (result.kind !== 'ready') throw new Error('expected ready');
    expect(result.content.subject).toBe('Contract Review');
    // href values are HTML-escaped (`&` -> `&amp;`).
    expect(result.content.htmlBody).toContain(
      `${ORG_URL}/main.aspx?etn=sprk_document&amp;id=${DOCUMENT_ID}&amp;pagetype=entityrecord&amp;navbar=off`
    );
    expect(result.content.htmlBody).toContain('Document: Agreement');
    expect(result.content.htmlBody).toContain(
      `${ORG_URL}/main.aspx?etn=sprk_matter&amp;id=${RECORD_ID}&amp;pagetype=entityrecord&amp;navbar=off`
    );
    expect(result.content.htmlBody).toContain('Matter: Smith v. Jones');
  });

  it('task 096: never mints a sharing link — no share-link route call and no network call at all', () => {
    prepareSendEmail({
      document: { documentId: DOCUMENT_ID },
      relatedRecord: MATTER,
      subject: 'x',
      orgUrl: ORG_URL,
    });

    expect(mockPost).not.toHaveBeenCalled();
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it('task 096: the body carries no SharePoint / sharing URL', () => {
    const result = prepareSendEmail({
      document: { documentId: DOCUMENT_ID },
      relatedRecord: null,
      subject: 'x',
      orgUrl: ORG_URL,
    });

    if (result.kind !== 'ready') throw new Error('expected ready');
    expect(result.content.htmlBody).not.toMatch(/sharepoint|share-link|\/share\//i);
  });

  it('the record links name the Spaarke app when SPAARKE_APP_NAME is set (task 088)', () => {
    const original = process.env.SPAARKE_APP_NAME;
    process.env.SPAARKE_APP_NAME = 'sprk_MatterManagement';
    try {
      const result = prepareSendEmail({
        document: { documentId: DOCUMENT_ID },
        relatedRecord: MATTER,
        subject: 'x',
        orgUrl: ORG_URL,
      });
      if (result.kind !== 'ready') throw new Error('expected ready');
      expect(result.content.htmlBody).toContain(
        `${ORG_URL}/main.aspx?appname=sprk_MatterManagement&amp;etn=sprk_document&amp;id=${DOCUMENT_ID}`
      );
      expect(result.content.htmlBody).toContain(
        `${ORG_URL}/main.aspx?appname=sprk_MatterManagement&amp;etn=sprk_matter&amp;id=${RECORD_ID}`
      );
    } finally {
      if (original === undefined) delete process.env.SPAARKE_APP_NAME;
      else process.env.SPAARKE_APP_NAME = original;
    }
  });

  it('a document with no related record → the document link only', () => {
    const result = prepareSendEmail({
      document: { documentId: DOCUMENT_ID },
      relatedRecord: null,
      subject: 'x',
      orgUrl: ORG_URL,
    });

    if (result.kind !== 'ready') throw new Error('expected ready');
    expect(result.content.htmlBody).toContain('etn=sprk_document');
    expect(result.content.htmlBody).not.toContain('etn=sprk_matter');
  });

  it('a related record but no document → the record link only', () => {
    const result = prepareSendEmail({ document: null, relatedRecord: MATTER, subject: 'x', orgUrl: ORG_URL });

    if (result.kind !== 'ready') throw new Error('expected ready');
    expect(result.content.htmlBody).toContain('etn=sprk_matter');
    expect(result.content.htmlBody).not.toContain('etn=sprk_document');
  });

  it('neither a document nor a related record → nothing-to-send', () => {
    expect(prepareSendEmail({ document: null, relatedRecord: null, subject: 'x', orgUrl: ORG_URL }).kind).toBe(
      'nothing-to-send'
    );
  });

  it('ORG_URL unset → nothing-to-send (no broken link), even with both a document and a record', () => {
    expect(
      prepareSendEmail({
        document: { documentId: DOCUMENT_ID },
        relatedRecord: MATTER,
        subject: 'x',
        orgUrl: undefined,
      }).kind
    ).toBe('nothing-to-send');
  });

  it('falls back to a generic subject when the given subject is blank', () => {
    const result = prepareSendEmail({
      document: { documentId: DOCUMENT_ID },
      relatedRecord: null,
      subject: '   ',
      orgUrl: ORG_URL,
    });
    if (result.kind !== 'ready') throw new Error('expected ready');
    expect(result.content.subject).toBe('Document from Spaarke');
  });

  it('HTML-escapes an unsafe display name in a link label', () => {
    const result = prepareSendEmail({
      document: null,
      relatedRecord: { entityType: 'sprk_matter', id: RECORD_ID, displayName: '<script>alert(1)</script>' },
      subject: 'x',
      orgUrl: ORG_URL,
    });
    if (result.kind !== 'ready') throw new Error('expected ready');
    expect(result.content.htmlBody).not.toContain('<script>');
    expect(result.content.htmlBody).toContain('&lt;script&gt;');
  });

  it('canonicalizes a braced, upper-case document id (ADR-044)', () => {
    const result = prepareSendEmail({
      document: { documentId: `{${DOCUMENT_ID.toUpperCase()}}` },
      relatedRecord: null,
      subject: 'x',
      orgUrl: ORG_URL,
    });
    if (result.kind !== 'ready') throw new Error('expected ready');
    expect(result.content.htmlBody).toContain(`id=${DOCUMENT_ID}&amp;`);
  });
});

describe('buildRecordLink / buildDocumentRecordLink', () => {
  it('labels a record "Type: Name (Number)" when the number is known', () => {
    expect(buildRecordLink(ORG_URL, { ...MATTER, number: 'M-0042' })?.label).toBe('Matter: Smith v. Jones (M-0042)');
  });

  it('labels a document without a name "Document record"', () => {
    expect(buildDocumentRecordLink(ORG_URL, { documentId: DOCUMENT_ID })?.label).toBe('Document record');
  });

  it('returns null for an empty id or an unset ORG_URL', () => {
    expect(buildRecordLink(ORG_URL, { entityType: 'sprk_matter', id: '' })).toBeNull();
    expect(buildDocumentRecordLink(undefined, { documentId: DOCUMENT_ID })).toBeNull();
  });
});

describe('resolveSendEmailAffordance (NFR-10 — capability only)', () => {
  it('Outlook (canComposeEmail true) with something to link → outlook-native', () => {
    expect(resolveSendEmailAffordance({ canComposeEmail: true }, true)).toBe('outlook-native');
  });

  it('task 096: Word (canComposeEmail false) → none — Word emails from its Email tab, not this button', () => {
    expect(resolveSendEmailAffordance({ canComposeEmail: false }, true)).toBe('none');
  });

  it('nothing to link → none, even on Outlook (never rendered-and-disabled)', () => {
    expect(resolveSendEmailAffordance({ canComposeEmail: true }, false)).toBe('none');
  });
});
