/**
 * documentRecordLink.test.ts — spaarkeai-word-add-in-r1 task 098 (owner decision 2026-10-05).
 *
 * A document linked in an email carries the SPAARKE RECORD link
 * (`{org}/main.aspx?appname=sprk_MatterManagement&pagetype=entityrecord&etn=sprk_document&id={id}`), never a
 * file sharing link. Every Spaarke document lives in SharePoint Embedded, where Graph refuses sharing links;
 * external recipients open files through the external access platform. The shared handler therefore builds the
 * link offline and never calls the BFF `share-link` route.
 */
import { buildSpaarkeRecordLink, createXrmEmailComposeHandlers } from '../createXrmEmailComposeHandlers';

const mockGetXrm = jest.fn();
jest.mock('../../../services/xrmGlobal', () => ({ getXrm: () => mockGetXrm() }));
// The upload path is not under test; stubbing it keeps this suite off the SDAP client package graph.
jest.mock('../../../services/EntityCreationService', () => ({ EntityCreationService: class {} }));

const ORG = 'https://contoso.crm.dynamics.com';
const DOC = '11111111-2222-3333-4444-555555555555';

describe('buildSpaarkeRecordLink', () => {
  it('builds the record link in the Spaarke app', () => {
    expect(buildSpaarkeRecordLink(ORG, 'sprk_document', DOC)).toBe(
      `${ORG}/main.aspx?appname=sprk_MatterManagement&pagetype=entityrecord&etn=sprk_document&id=${DOC}`
    );
  });

  it('tolerates a trailing slash on the org URL', () => {
    expect(buildSpaarkeRecordLink(`${ORG}/`, 'sprk_document', DOC)).toContain(`${ORG}/main.aspx?`);
  });

  it.each([undefined, '', '   ', '/relative', 'http://insecure.example', 'not a url'])(
    'returns null (never a broken link) for org URL %p',
    org => {
      expect(buildSpaarkeRecordLink(org, 'sprk_document', DOC)).toBeNull();
    }
  );

  it('returns null for an empty record id', () => {
    expect(buildSpaarkeRecordLink(ORG, 'sprk_document', '')).toBeNull();
  });
});

describe('createXrmEmailComposeHandlers — onResolveShareLink (record link)', () => {
  beforeEach(() => {
    mockGetXrm.mockReset();
  });

  it('resolves the Spaarke record link from the host org URL and makes NO request to /share-link', async () => {
    mockGetXrm.mockReturnValue({ Utility: { getGlobalContext: () => ({ getClientUrl: () => ORG }) } });
    const authenticatedFetch = jest.fn();
    const handlers = createXrmEmailComposeHandlers({ authenticatedFetch, bffBaseUrl: 'https://bff.example' });

    const url = await handlers.onResolveShareLink(`{${DOC.toUpperCase()}}`);

    expect(url).toBe(
      `${ORG}/main.aspx?appname=sprk_MatterManagement&pagetype=entityrecord&etn=sprk_document&id=${DOC}`
    );
    expect(authenticatedFetch).not.toHaveBeenCalled();
  });

  it('is present even without auth/BFF (it needs only the host org URL)', async () => {
    mockGetXrm.mockReturnValue({ Utility: { getGlobalContext: () => ({ getClientUrl: () => ORG }) } });
    const handlers = createXrmEmailComposeHandlers();
    expect(await handlers.onResolveShareLink(DOC)).toContain('etn=sprk_document');
  });

  it('honours an explicit clientUrl over the host', async () => {
    mockGetXrm.mockReturnValue(undefined);
    const handlers = createXrmEmailComposeHandlers({ clientUrl: ORG });
    expect(await handlers.onResolveShareLink(DOC)).toBe(buildSpaarkeRecordLink(ORG, 'sprk_document', DOC));
  });

  it('resolves null when no org URL is available (the engine then omits the link)', async () => {
    mockGetXrm.mockReturnValue(undefined);
    const handlers = createXrmEmailComposeHandlers();
    expect(await handlers.onResolveShareLink(DOC)).toBeNull();
  });

  it('picked records (onLookupRecord) carry the same app-qualified record link', async () => {
    mockGetXrm.mockReturnValue({
      Utility: {
        getGlobalContext: () => ({ getClientUrl: () => ORG }),
        lookupObjects: async () => [{ id: `{${DOC}}`, name: 'Brief' }],
      },
    });
    const picked = await createXrmEmailComposeHandlers().onLookupRecord('sprk_document');
    expect(picked?.url).toBe(buildSpaarkeRecordLink(ORG, 'sprk_document', DOC));
  });
});
