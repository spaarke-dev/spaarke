/**
 * paneEmailService — task 096: the pure wiring between the Word pane and the shared compose engine.
 *
 * Pins: the subject comes from the document name; the body carries the Spaarke RECORD link and never a sharing
 * link; the document is the one attachment, explicitly selected, by its canonical `sprk_document` id; the
 * related record is the association by its LOGICAL name (the server maps it onto `sprk_regarding*`); a
 * contact without an email is not offered as a recipient; the injected fetch attaches the token, retries a 401
 * once, and reports the two outcomes the engine does not (no response at all; sent but not recorded).
 */
import {
  buildPaneEmailAssociations,
  buildPaneEmailAttachment,
  buildPaneEmailBody,
  buildPaneEmailSubject,
  createPaneAuthenticatedFetch,
  toRecipientLookupItems,
} from '../paneEmailService';

const DOC = '11111111-1111-1111-1111-111111111111';
const REC = '22222222-2222-2222-2222-222222222222';
const ORG = 'https://contoso.crm.dynamics.com';
const MATTER = { entityType: 'sprk_matter', id: REC, typeLabel: 'Matter', displayName: 'Smith v. Jones' };

function fakeResponse(status: number, body: unknown): Response {
  const text = JSON.stringify(body);
  const make = (): Response =>
    ({
      ok: status >= 200 && status < 300,
      status,
      headers: { get: (h: string) => (h.toLowerCase() === 'content-type' ? 'application/json' : null) },
      json: async () => JSON.parse(text),
      text: async () => text,
      clone: () => make(),
    }) as unknown as Response;
  return make();
}

describe('subject + body', () => {
  it('subject = the Spaarke document name; else the file name without extension; else a generic subject', () => {
    expect(buildPaneEmailSubject({ documentId: DOC, documentName: 'Master Agreement', fileName: 'x.docx' })).toBe(
      'Master Agreement'
    );
    expect(buildPaneEmailSubject({ documentId: DOC, fileName: 'Master Agreement.docx' })).toBe('Master Agreement');
    expect(buildPaneEmailSubject({ documentId: DOC })).toBe('Document from Spaarke');
  });

  it('body = a short line + the related RECORD link (escaped), never a sharing link', () => {
    const body = buildPaneEmailBody({ documentId: DOC }, MATTER, ORG);
    expect(body).toContain('<p>Please see the attached document.</p>');
    expect(body).toContain(`${ORG}/main.aspx?etn=sprk_matter&amp;id=${REC}&amp;pagetype=entityrecord&amp;navbar=off`);
    expect(body).toContain('Matter: Smith v. Jones');
    expect(body).not.toMatch(/sharepoint|share-link/i);
  });

  it('a document filed to no record → the body links the document’s own Spaarke record', () => {
    const body = buildPaneEmailBody({ documentId: DOC, documentName: 'Master Agreement' }, null, ORG);
    expect(body).toContain(`etn=sprk_document&amp;id=${DOC}`);
    expect(body).toContain('Document: Master Agreement');
  });

  it('ORG_URL unset → the short line only (no broken link)', () => {
    expect(buildPaneEmailBody({ documentId: DOC }, MATTER, undefined)).toBe('<p>Please see the attached document.</p>');
  });
});

describe('attachment + association', () => {
  it('the document is ONE selected attachment, by its canonical sprk_document id', () => {
    expect(buildPaneEmailAttachment({ documentId: `{${DOC.toUpperCase()}}`, fileName: 'Agreement.docx' })).toEqual({
      id: `pane-document-${DOC}`,
      source: 'spe',
      fileName: 'Agreement.docx',
      sizeBytes: 0,
      documentId: DOC,
      selected: true,
    });
  });

  it('the related record is the association, by LOGICAL name, with its name and Spaarke URL', () => {
    expect(buildPaneEmailAssociations(MATTER, ORG)).toEqual([
      {
        entityType: 'sprk_matter',
        entityId: REC,
        entityName: 'Smith v. Jones',
        entityUrl: `${ORG}/main.aspx?etn=sprk_matter&id=${REC}&pagetype=entityrecord&navbar=off`,
      },
    ]);
  });

  it('no related record (an unfiled document) → no association', () => {
    expect(buildPaneEmailAssociations(null, ORG)).toEqual([]);
    expect(buildPaneEmailAssociations({ entityType: 'sprk_matter', id: '' }, ORG)).toEqual([]);
  });
});

describe('recipient search', () => {
  it('maps contacts to "Name (email)" lookup items and drops contacts with no email', () => {
    expect(
      toRecipientLookupItems([
        { id: 'c1', name: 'Jane Cooper', email: 'jane@acme.test' },
        { id: 'c2', name: 'Robert Fox' },
        { id: 'c3', name: 'Wade Warren', email: '  ' },
      ])
    ).toEqual([{ id: 'c1', name: 'Jane Cooper (jane@acme.test)', email: 'jane@acme.test' }]);
  });
});

describe('createPaneAuthenticatedFetch', () => {
  const SEND_URL = 'https://bff.test/api/communications/send';
  let fetchMock: jest.Mock;
  const observer = { onNotSent: jest.fn(), onTransportError: jest.fn(), onSentWithoutRecord: jest.fn() };

  beforeEach(() => {
    fetchMock = jest.fn();
    (global as unknown as { fetch: unknown }).fetch = fetchMock;
    observer.onNotSent.mockReset();
    observer.onTransportError.mockReset();
    observer.onSentWithoutRecord.mockReset();
  });

  it('attaches the bearer token and passes the request through unchanged', async () => {
    fetchMock.mockResolvedValue(fakeResponse(200, { communicationId: 'c-1' }));
    const f = createPaneAuthenticatedFetch({ getAccessToken: async () => 'tok', observer });

    await f(SEND_URL, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' });

    expect(fetchMock).toHaveBeenCalledWith(SEND_URL, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Authorization: 'Bearer tok' },
      body: '{}',
    });
    expect(observer.onSentWithoutRecord).not.toHaveBeenCalled();
    expect(observer.onTransportError).not.toHaveBeenCalled();
  });

  it('a 401 is retried once with a fresh token after clearing the cache', async () => {
    fetchMock
      .mockResolvedValueOnce(fakeResponse(401, {}))
      .mockResolvedValueOnce(fakeResponse(200, { communicationId: 'c-1' }));
    const tokens = ['old', 'new'];
    const clear = jest.fn();
    const f = createPaneAuthenticatedFetch({
      getAccessToken: async () => tokens.shift() ?? null,
      clearTokenCache: clear,
      observer,
    });

    const res = await f(SEND_URL, { method: 'POST' });

    expect(res.status).toBe(200);
    expect(clear).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[1][1].headers.Authorization).toBe('Bearer new');
  });

  it('a 2xx send with no communicationId → onSentWithoutRecord (the email went out; Spaarke did not record it)', async () => {
    fetchMock.mockResolvedValue(fakeResponse(200, { communicationId: null, status: 659490002 }));
    const f = createPaneAuthenticatedFetch({ getAccessToken: async () => 'tok', observer });

    await f(SEND_URL, { method: 'POST' });

    expect(observer.onSentWithoutRecord).toHaveBeenCalledTimes(1);
  });

  it('a refused send (403) is NOT reported as sent — the engine raises the server error itself', async () => {
    fetchMock.mockResolvedValue(fakeResponse(403, { detail: 'nope' }));
    const f = createPaneAuthenticatedFetch({ getAccessToken: async () => 'tok', observer });

    const res = await f(SEND_URL, { method: 'POST' });

    expect(res.status).toBe(403);
    expect(observer.onSentWithoutRecord).not.toHaveBeenCalled();
  });

  it('no response at all (network failure) → onTransportError, and the error still propagates', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
    const f = createPaneAuthenticatedFetch({ getAccessToken: async () => 'tok', observer });

    await expect(f(SEND_URL, { method: 'POST' })).rejects.toThrow('Failed to fetch');
    expect(observer.onTransportError).toHaveBeenCalledWith('Failed to fetch');
  });

  it('no token → onNotSent (certainly not sent), never onTransportError ("may have been sent"), and no request', async () => {
    const f = createPaneAuthenticatedFetch({ getAccessToken: async () => null, observer });

    await expect(f(SEND_URL, { method: 'POST' })).rejects.toThrow('You are not signed in to Spaarke.');
    expect(fetchMock).not.toHaveBeenCalled();
    expect(observer.onNotSent).toHaveBeenCalledWith('You are not signed in to Spaarke.');
    expect(observer.onTransportError).not.toHaveBeenCalled();
  });

  it.each([`${SEND_URL}?trace=1`, `${SEND_URL}/`])(
    'recognises the send route despite a query string or trailing slash (%s)',
    async url => {
      fetchMock.mockResolvedValue(fakeResponse(200, { communicationId: null }));
      const f = createPaneAuthenticatedFetch({ getAccessToken: async () => 'tok', observer });

      await f(url, { method: 'POST' });

      expect(observer.onSentWithoutRecord).toHaveBeenCalledTimes(1);
    }
  );

  it('a 2xx from another route is not taken for a send', async () => {
    fetchMock.mockResolvedValue(fakeResponse(200, {}));
    const f = createPaneAuthenticatedFetch({ getAccessToken: async () => 'tok', observer });

    await f('https://bff.test/api/communications/send-bulk', { method: 'POST' });

    expect(observer.onSentWithoutRecord).not.toHaveBeenCalled();
  });
});
