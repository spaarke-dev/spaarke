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
 *
 * Also covers task 086 (FR-15 amended 2026-10-02) — the Word Send Email choice:
 *   - `buildSpaarkeComposeUrl` / `buildOutlookWebComposeUrl` (encoding, `associatedTo`, the link lines)
 *   - `prepareWordSendEmailChoice` (reuses the SAME link resolution; no related record → associatedTo
 *     omitted and the email still opens; ORG_URL unset → the Spaarke option degrades to null while the
 *     Outlook-web option still builds)
 *   - `resolveSendEmailAffordance` (the gating decision: Word shows the choice; Outlook unchanged; no
 *     `canOpenBrowserWindow` ⇒ no choice)
 */

import {
  prepareSendEmail,
  prepareWordSendEmailChoice,
  buildSpaarkeComposeUrl,
  buildOutlookWebComposeUrl,
  resolveSendEmailAffordance,
} from '../sendEmailService';
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
    // navbar=off (task 086 / FR-10 amended 2026-10-02): buildOpenRecordUrl now carries it unconditionally.
    expect(result.content.htmlBody).toContain(
      `${ORG_URL}/main.aspx?etn=sprk_matter&amp;id=${RECORD_ID}&amp;pagetype=entityrecord&amp;navbar=off`
    );
    expect(result.content.htmlBody).toContain('Smith v. Jones');
  });

  it('the record link names the Spaarke app when SPAARKE_APP_NAME is set (task 088 — the same rule as every record link)', async () => {
    const original = process.env.SPAARKE_APP_NAME;
    process.env.SPAARKE_APP_NAME = 'sprk_MatterManagement';
    try {
      const result = await prepareSendEmail({
        document: null,
        relatedRecord: { entityType: 'sprk_matter', id: RECORD_ID, typeLabel: 'Matter', displayName: 'Smith v. Jones' },
        subject: 'Contract Review',
        orgUrl: ORG_URL,
      });

      if (result.kind !== 'ready') throw new Error('expected ready');
      expect(result.content.htmlBody).toContain(
        `${ORG_URL}/main.aspx?appname=sprk_MatterManagement&amp;etn=sprk_matter&amp;id=${RECORD_ID}&amp;pagetype=entityrecord&amp;navbar=off`
      );
    } finally {
      if (original === undefined) delete process.env.SPAARKE_APP_NAME;
      else process.env.SPAARKE_APP_NAME = original;
    }
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
      `${ORG_URL}/main.aspx?etn=sprk_matter&amp;id=${RECORD_ID}&amp;pagetype=entityrecord&amp;navbar=off`
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

/**
 * Replicates the EXACT decode algorithm `CommunicationPage/src/services/parseParams.ts`'s
 * `unwrapDataEnvelope` + `parseCommunicationParams` document — read from that file, not assumed —
 * so these tests prove `buildSpaarkeComposeUrl`'s output round-trips through the Communication Page's
 * REAL, documented contract rather than merely asserting our own encoding choices. This package
 * cannot import that file directly (a separate Vite workspace, not a shared lib), so the few lines of
 * decode logic are reproduced here, verbatim in spirit, with the source cited.
 *
 * `parseParams.ts`:
 *   const outer = new URLSearchParams(search);
 *   const envelope = outer.get('data');
 *   if (envelope) { return new URLSearchParams(decodeURIComponent(envelope)); }
 */
function decodeSpaarkeComposeUrl(fullUrl: string): {
  pagetype: string | null;
  webresourceName: string | null;
  mode: string | null;
  subject: string | null;
  body: string | null;
  associatedTo: string[];
} {
  const outer = new URL(fullUrl);
  const envelope = outer.searchParams.get('data');
  const inner = new URLSearchParams(envelope ? decodeURIComponent(envelope) : '');
  return {
    pagetype: outer.searchParams.get('pagetype'),
    webresourceName: outer.searchParams.get('webresourceName'),
    mode: inner.get('mode'),
    subject: inner.get('subject'),
    body: inner.get('body'),
    associatedTo: inner.getAll('associatedTo'),
  };
}

describe('buildSpaarkeComposeUrl (task 086 / FR-15 — the Word "Spaarke email" choice)', () => {
  it('builds a main.aspx webresource URL for sprk_communicationpage', () => {
    const url = buildSpaarkeComposeUrl({
      orgUrl: 'https://contoso.crm.dynamics.com',
      subject: 'Contract Review',
      htmlBody: '<p>hi</p>',
      associatedTo: null,
    });

    expect(url.startsWith('https://contoso.crm.dynamics.com/main.aspx?')).toBe(true);
    const decoded = decodeSpaarkeComposeUrl(url);
    expect(decoded.pagetype).toBe('webresource');
    expect(decoded.webresourceName).toBe('sprk_communicationpage');
    expect(decoded.mode).toBe('compose');
  });

  it('round-trips subject, HTML body and associatedTo through the real parseParams.ts decode algorithm', () => {
    const url = buildSpaarkeComposeUrl({
      orgUrl: 'https://contoso.crm.dynamics.com',
      subject: 'Re: Smith & Jones — review',
      htmlBody: '<p><a href="https://contoso.sharepoint.com/share/abc?x=1&y=2">Open document</a></p>',
      associatedTo: { entityType: 'sprk_matter', id: '22222222-2222-2222-2222-222222222222' },
    });

    const decoded = decodeSpaarkeComposeUrl(url);
    expect(decoded.subject).toBe('Re: Smith & Jones — review');
    expect(decoded.body).toBe('<p><a href="https://contoso.sharepoint.com/share/abc?x=1&y=2">Open document</a></p>');
    expect(decoded.associatedTo).toEqual(['sprk_matter:22222222-2222-2222-2222-222222222222']);
  });

  it('omits associatedTo entirely when no related record is given (acceptance criterion: still opens)', () => {
    const url = buildSpaarkeComposeUrl({
      orgUrl: 'https://contoso.crm.dynamics.com',
      subject: 'x',
      htmlBody: '<p>hi</p>',
      associatedTo: null,
    });

    const decoded = decodeSpaarkeComposeUrl(url);
    expect(decoded.associatedTo).toEqual([]);
    expect(decoded.mode).toBe('compose');
  });

  it('never embeds a token, secret or the BFF base URL', () => {
    const url = buildSpaarkeComposeUrl({
      orgUrl: 'https://contoso.crm.dynamics.com',
      subject: 'x',
      htmlBody: '<p>hi</p>',
      associatedTo: null,
    });

    expect(url).not.toContain('Bearer');
    expect(url).not.toContain('access_token');
    expect(url).not.toContain('spaarke-bff');
  });
});

describe('buildOutlookWebComposeUrl (task 086 / FR-15 — the Word "Outlook on the web" choice)', () => {
  it('builds the documented outlook.office.com compose deep link', () => {
    const url = buildOutlookWebComposeUrl({ subject: 'Contract Review', plainTextBody: 'Open document: https://x' });
    expect(url.startsWith('https://outlook.office.com/mail/deeplink/compose?')).toBe(true);
  });

  it('round-trips subject and a plain-text body carrying both links', () => {
    const plainTextBody =
      'Open document: https://contoso.sharepoint.com/share/abc\nMatter: Smith v. Jones: https://contoso.crm.dynamics.com/main.aspx?etn=sprk_matter&id=22222222-2222-2222-2222-222222222222&pagetype=entityrecord&navbar=off';
    const url = buildOutlookWebComposeUrl({ subject: 'Re: Smith & Jones', plainTextBody });

    const parsed = new URL(url);
    expect(parsed.searchParams.get('subject')).toBe('Re: Smith & Jones');
    expect(parsed.searchParams.get('body')).toBe(plainTextBody);
  });

  it('never embeds a token, secret or the BFF base URL', () => {
    const url = buildOutlookWebComposeUrl({ subject: 'x', plainTextBody: 'Open document: https://x' });
    expect(url).not.toContain('Bearer');
    expect(url).not.toContain('access_token');
    expect(url).not.toContain('spaarke-bff');
  });
});

describe('prepareWordSendEmailChoice (task 086 / FR-15 — Word Send Email orchestration)', () => {
  beforeEach(() => {
    mockPost.mockReset();
  });

  it('both a document and a related record → both destinations carry both links + associatedTo', async () => {
    mockPost.mockResolvedValue({
      url: 'https://contoso.sharepoint.com/share/abc',
      expiresAt: '2026-10-01T00:00:00Z',
      scope: 'organization',
    });

    const result = await prepareWordSendEmailChoice({
      document: { documentId: DOCUMENT_ID },
      relatedRecord: { entityType: 'sprk_matter', id: RECORD_ID, typeLabel: 'Matter', displayName: 'Smith v. Jones' },
      subject: 'Contract Review',
      orgUrl: ORG_URL,
    });

    expect(result.kind).toBe('ready');
    if (result.kind !== 'ready') throw new Error('expected ready');
    expect(result.spaarkeUrl).not.toBeNull();
    const decoded = decodeSpaarkeComposeUrl(result.spaarkeUrl as string);
    expect(decoded.associatedTo).toEqual([`sprk_matter:${RECORD_ID}`]);
    expect(decoded.body).toContain('https://contoso.sharepoint.com/share/abc');
    // The Spaarke body is HTML (buildComposeBody): href values are HTML-escaped (`&` -> `&amp;`).
    expect(decoded.body).toContain(
      `${ORG_URL}/main.aspx?etn=sprk_matter&amp;id=${RECORD_ID}&amp;pagetype=entityrecord&amp;navbar=off`
    );

    expect(result.outlookWebUrl).toContain('outlook.office.com/mail/deeplink/compose');
    const outlookParsed = new URL(result.outlookWebUrl);
    const outlookBody = outlookParsed.searchParams.get('body') ?? '';
    // The Outlook-web body is PLAIN TEXT (buildPlainTextComposeBody): no HTML escaping at all.
    expect(outlookBody).toContain('https://contoso.sharepoint.com/share/abc');
    expect(outlookBody).toContain(
      `${ORG_URL}/main.aspx?etn=sprk_matter&id=${RECORD_ID}&pagetype=entityrecord&navbar=off`
    );
  });

  it('no related record → associatedTo is omitted and the email still opens (acceptance criterion)', async () => {
    mockPost.mockResolvedValue({
      url: 'https://contoso.sharepoint.com/share/abc',
      expiresAt: '2026-10-01T00:00:00Z',
      scope: 'organization',
    });

    const result = await prepareWordSendEmailChoice({
      document: { documentId: DOCUMENT_ID },
      relatedRecord: null,
      subject: 'x',
      orgUrl: ORG_URL,
    });

    expect(result.kind).toBe('ready');
    if (result.kind !== 'ready') throw new Error('expected ready');
    expect(result.spaarkeUrl).not.toBeNull();
    const decoded = decodeSpaarkeComposeUrl(result.spaarkeUrl as string);
    expect(decoded.associatedTo).toEqual([]);
    expect(decoded.mode).toBe('compose');
  });

  it('ORG_URL unset → spaarkeUrl is null but outlookWebUrl still builds (degrade, never break)', async () => {
    // With ORG_URL unset, the record link itself cannot be built (it is a Dataverse deep link) — so a
    // document is the only link source that still resolves (the document share link never depends on
    // ORG_URL). This isolates prepareWordSendEmailChoice's OWN orgUrl check (spaarkeUrl: null) from
    // resolveSendEmailLinks' pre-existing orgUrl-dependent record-link behavior.
    mockPost.mockResolvedValue({
      url: 'https://contoso.sharepoint.com/share/abc',
      expiresAt: '2026-10-01T00:00:00Z',
      scope: 'organization',
    });

    const result = await prepareWordSendEmailChoice({
      document: { documentId: DOCUMENT_ID },
      relatedRecord: null,
      subject: 'x',
      orgUrl: undefined,
    });

    expect(result.kind).toBe('ready');
    if (result.kind !== 'ready') throw new Error('expected ready');
    expect(result.spaarkeUrl).toBeNull();
    expect(result.outlookWebUrl).toContain('outlook.office.com');
    const outlookBody = new URL(result.outlookWebUrl).searchParams.get('body') ?? '';
    expect(outlookBody).toContain('https://contoso.sharepoint.com/share/abc');
  });

  it('a share-link minting failure returns an error outcome (no compose destination is opened)', async () => {
    mockPost.mockRejectedValue(new ApiClientError({ type: 'about:blank', title: 'Forbidden', status: 403 }));

    const result = await prepareWordSendEmailChoice({
      document: { documentId: DOCUMENT_ID },
      relatedRecord: { entityType: 'sprk_matter', id: RECORD_ID, displayName: 'Smith v. Jones' },
      subject: 'x',
      orgUrl: ORG_URL,
    });

    expect(result.kind).toBe('error');
  });

  it('neither a document nor a related record → nothing-to-send, no network call', async () => {
    const result = await prepareWordSendEmailChoice({
      document: null,
      relatedRecord: null,
      subject: 'x',
      orgUrl: ORG_URL,
    });

    expect(result.kind).toBe('nothing-to-send');
    expect(mockPost).not.toHaveBeenCalled();
  });
});

describe('resolveSendEmailAffordance (task 086 / FR-15 — the gating decision, NFR-10)', () => {
  it('Outlook (canComposeEmail true) → outlook-native, regardless of canOpenBrowserWindow', () => {
    expect(resolveSendEmailAffordance({ canComposeEmail: true, canOpenBrowserWindow: false }, true)).toBe(
      'outlook-native'
    );
    expect(resolveSendEmailAffordance({ canComposeEmail: true, canOpenBrowserWindow: true }, true)).toBe(
      'outlook-native'
    );
  });

  it('Word (canComposeEmail false, canOpenBrowserWindow true) → word-choice', () => {
    expect(resolveSendEmailAffordance({ canComposeEmail: false, canOpenBrowserWindow: true }, true)).toBe(
      'word-choice'
    );
  });

  it('neither capability → none (the negative acceptance criterion: no affordance, not disabled)', () => {
    expect(resolveSendEmailAffordance({ canComposeEmail: false, canOpenBrowserWindow: false }, true)).toBe('none');
  });

  it('nothing to link → none even when both capabilities would otherwise allow a choice', () => {
    expect(resolveSendEmailAffordance({ canComposeEmail: false, canOpenBrowserWindow: true }, false)).toBe('none');
    expect(resolveSendEmailAffordance({ canComposeEmail: true, canOpenBrowserWindow: false }, false)).toBe('none');
  });
});
