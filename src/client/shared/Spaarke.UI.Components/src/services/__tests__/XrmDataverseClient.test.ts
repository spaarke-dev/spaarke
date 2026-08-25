/**
 * Unit tests for XrmDataverseClient (task 002, FR-DG-02).
 *
 * Coverage:
 *  - All 5 IDataverseClient methods route to Xrm.WebApi / Xrm.Utility correctly
 *  - getXrm() resolves window.Xrm when present
 *  - getXrm() falls back to window.parent.Xrm when window.Xrm is absent
 *  - getXrm() throws the documented error when neither has Xrm
 *  - Metadata projection handles array, collection-accessor, and record shapes
 *  - Paging cookie + moreRecords flags propagate correctly
 */

/* eslint-disable @typescript-eslint/no-explicit-any */

import { XrmDataverseClient, _resetEntityMetadataCacheForTests } from '../XrmDataverseClient';

const XRM_MISSING_MESSAGE = 'XrmDataverseClient requires Xrm context. Use BffDataverseClient outside MDA.';

interface MockXrm {
  WebApi: {
    retrieveRecord: jest.Mock;
    retrieveMultipleRecords: jest.Mock;
  };
  Utility?: {
    getEntityMetadata: jest.Mock;
  };
}

function makeMockXrm(): MockXrm {
  return {
    WebApi: {
      retrieveRecord: jest.fn(),
      retrieveMultipleRecords: jest.fn(),
    },
    Utility: {
      getEntityMetadata: jest.fn(),
    },
  };
}

/**
 * Helper to set window.Xrm. Restores prior value on teardown via afterEach.
 */
function setWindowXrm(xrm: MockXrm | undefined): void {
  (window as any).Xrm = xrm;
}

/**
 * Helper to set window.parent.Xrm. Because in jsdom `window.parent === window`,
 * setting `window.Xrm` to undefined and then `(window.parent as any).Xrm` to a
 * mock actually mutates the same object. To genuinely simulate the iframe case
 * we replace `window.parent` with a distinct object that has its own `Xrm`.
 */
function setParentOnlyXrm(parentXrm: MockXrm | undefined): void {
  // Clear window.Xrm so the "window first" branch misses.
  delete (window as any).Xrm;
  // Define a distinct parent object so window.parent !== window.
  Object.defineProperty(window, 'parent', {
    configurable: true,
    value: { Xrm: parentXrm },
  });
}

/**
 * Restore window.parent to its natural self-reference and clear window.Xrm.
 */
function resetWindowAndParent(): void {
  delete (window as any).Xrm;
  // jsdom default is window.parent === window. Restore that.
  Object.defineProperty(window, 'parent', {
    configurable: true,
    value: window,
  });
}

describe('XrmDataverseClient', () => {
  beforeEach(() => {
    // Entity metadata cache is module-level (page-session lifetime in prod);
    // reset it before every test so tests reusing an entity name (e.g. 'sprk_event')
    // don't observe a prior test's cached result.
    _resetEntityMetadataCacheForTests();
  });

  afterEach(() => {
    resetWindowAndParent();
    jest.clearAllMocks();
  });

  describe('getXrm() resolution', () => {
    it('resolves window.Xrm when present', async () => {
      const xrm = makeMockXrm();
      xrm.WebApi.retrieveRecord.mockResolvedValue({
        returnedtypecode: 'account',
        fetchxml: '<fetch/>',
        layoutxml: '<grid/>',
        name: 'My View',
      });
      setWindowXrm(xrm);

      const client = new XrmDataverseClient();
      await client.retrieveSavedQuery('abc');

      expect(xrm.WebApi.retrieveRecord).toHaveBeenCalledTimes(1);
    });

    it('falls back to window.parent.Xrm when window.Xrm is absent', async () => {
      const parentXrm = makeMockXrm();
      parentXrm.WebApi.retrieveRecord.mockResolvedValue({
        returnedtypecode: 'contact',
        fetchxml: '<fetch/>',
        layoutxml: '<grid/>',
        name: 'Parent View',
      });
      setParentOnlyXrm(parentXrm);

      const client = new XrmDataverseClient();
      const result = await client.retrieveSavedQuery('def');

      expect(parentXrm.WebApi.retrieveRecord).toHaveBeenCalledTimes(1);
      expect(result.entityName).toBe('contact');
      expect(result.name).toBe('Parent View');
    });

    it('throws documented error when neither window nor window.parent has Xrm', async () => {
      resetWindowAndParent();

      const client = new XrmDataverseClient();
      await expect(client.retrieveSavedQuery('xyz')).rejects.toThrow(XRM_MISSING_MESSAGE);
    });

    it('throws when window.Xrm is present but missing WebApi (sanity check)', async () => {
      setWindowXrm({ WebApi: undefined as any } as any);
      // parent === window in jsdom default so the parent branch also misses.
      const client = new XrmDataverseClient();
      await expect(client.retrieveSavedQuery('xyz')).rejects.toThrow(XRM_MISSING_MESSAGE);
    });
  });

  describe('retrieveSavedQuery', () => {
    it('queries savedquery with the documented $select clause', async () => {
      const xrm = makeMockXrm();
      xrm.WebApi.retrieveRecord.mockResolvedValue({
        returnedtypecode: 'sprk_event',
        fetchxml: '<fetch top="50"/>',
        layoutxml: '<grid><row><cell name="name"/></row></grid>',
        name: 'Active Events',
      });
      setWindowXrm(xrm);

      const client = new XrmDataverseClient();
      const result = await client.retrieveSavedQuery('guid-1');

      expect(xrm.WebApi.retrieveRecord).toHaveBeenCalledWith(
        'savedquery',
        'guid-1',
        expect.stringContaining('returnedtypecode')
      );
      const optionsArg = xrm.WebApi.retrieveRecord.mock.calls[0][2] as string;
      expect(optionsArg).toContain('$select=name');
      expect(optionsArg).toContain('fetchxml');
      expect(optionsArg).toContain('layoutxml');

      expect(result).toEqual({
        entityName: 'sprk_event',
        fetchXml: '<fetch top="50"/>',
        layoutXml: '<grid><row><cell name="name"/></row></grid>',
        name: 'Active Events',
      });
    });

    it('returns empty-string defaults when fields are missing', async () => {
      const xrm = makeMockXrm();
      xrm.WebApi.retrieveRecord.mockResolvedValue({});
      setWindowXrm(xrm);

      const client = new XrmDataverseClient();
      const result = await client.retrieveSavedQuery('guid-2');

      expect(result).toEqual({
        entityName: '',
        fetchXml: '',
        layoutXml: '',
        name: '',
      });
    });
  });

  describe('retrieveSavedQueriesForEntity', () => {
    it('filters by statecode/querytype/returnedtypecode and projects rows', async () => {
      const xrm = makeMockXrm();
      xrm.WebApi.retrieveMultipleRecords.mockResolvedValue({
        entities: [
          { savedqueryid: 'v1', name: 'All Events', isdefault: true, querytype: 0 },
          { savedqueryid: 'v2', name: 'My Events', isdefault: false, querytype: 0 },
        ],
      });
      setWindowXrm(xrm);

      const client = new XrmDataverseClient();
      const summaries = await client.retrieveSavedQueriesForEntity('sprk_event');

      expect(xrm.WebApi.retrieveMultipleRecords).toHaveBeenCalledTimes(1);
      const [logicalName, options] = xrm.WebApi.retrieveMultipleRecords.mock.calls[0];
      expect(logicalName).toBe('savedquery');
      expect(options).toContain('statecode eq 0');
      expect(options).toContain('querytype eq 0');
      expect(options).toContain("returnedtypecode eq 'sprk_event'");
      expect(options).toContain('$select=savedqueryid,name,isdefault,querytype');

      expect(summaries).toEqual([
        { id: 'v1', name: 'All Events', isDefault: true, queryType: 0 },
        { id: 'v2', name: 'My Events', isDefault: false, queryType: 0 },
      ]);
    });

    it('returns empty array when no views match', async () => {
      const xrm = makeMockXrm();
      xrm.WebApi.retrieveMultipleRecords.mockResolvedValue({ entities: [] });
      setWindowXrm(xrm);

      const client = new XrmDataverseClient();
      const summaries = await client.retrieveSavedQueriesForEntity('sprk_event');

      expect(summaries).toEqual([]);
    });
  });

  describe('retrieveEntityMetadata', () => {
    it('calls Xrm.Utility.getEntityMetadata with [Attributes] expansion and projects shape', async () => {
      const xrm = makeMockXrm();
      xrm.Utility!.getEntityMetadata.mockResolvedValue({
        PrimaryIdAttribute: 'sprk_eventid',
        PrimaryNameAttribute: 'sprk_name',
        Attributes: [
          {
            LogicalName: 'sprk_name',
            AttributeType: 'String',
            Format: 'Text',
            IsPrimaryName: true,
          },
          {
            LogicalName: 'sprk_status',
            AttributeType: 'Picklist',
            OptionSet: {
              Options: [
                {
                  Value: 1,
                  Label: { UserLocalizedLabel: { Label: 'Active' } },
                  Color: '#00aa00',
                },
                {
                  Value: 2,
                  Label: { UserLocalizedLabel: { Label: 'Inactive' } },
                },
              ],
            },
          },
        ],
      });
      setWindowXrm(xrm);

      const client = new XrmDataverseClient();
      const meta = await client.retrieveEntityMetadata('sprk_event');

      // NOTE (pre-existing fix, unrelated to FR-21): the source intentionally omits
      // the second arg — `getEntityMetadata`'s 2nd param is an attribute FILTER, not
      // an "include this section" hint (see XrmDataverseClient.ts comment above the
      // call). This assertion was stale against that behavior; corrected here since
      // task 020 already owns this file and the shared-lib suite must be green.
      expect(xrm.Utility!.getEntityMetadata).toHaveBeenCalledWith('sprk_event');
      expect(meta.primaryIdAttribute).toBe('sprk_eventid');
      expect(meta.primaryNameAttribute).toBe('sprk_name');
      expect(meta.attributes.sprk_name).toEqual({
        attributeType: 'String',
        format: 'Text',
        isPrimaryName: true,
        isPrimaryId: undefined,
        optionSet: undefined,
      });
      expect(meta.attributes.sprk_status.attributeType).toBe('Picklist');
      expect(meta.attributes.sprk_status.optionSet).toEqual([
        { value: 1, label: 'Active', color: '#00aa00' },
        { value: 2, label: 'Inactive', color: undefined },
      ]);
    });

    it('tolerates a record-shape Attributes payload', async () => {
      const xrm = makeMockXrm();
      xrm.Utility!.getEntityMetadata.mockResolvedValue({
        PrimaryIdAttribute: 'accountid',
        PrimaryNameAttribute: 'name',
        Attributes: {
          name: { LogicalName: 'name', AttributeType: 'String', IsPrimaryName: true },
          revenue: { LogicalName: 'revenue', AttributeType: 'Money' },
        },
      });
      setWindowXrm(xrm);

      const client = new XrmDataverseClient();
      const meta = await client.retrieveEntityMetadata('account');

      expect(Object.keys(meta.attributes).sort()).toEqual(['name', 'revenue']);
      expect(meta.attributes.revenue.attributeType).toBe('Money');
    });

    it('throws when Xrm.Utility is unavailable', async () => {
      const xrm = makeMockXrm();
      xrm.Utility = undefined;
      setWindowXrm(xrm);

      const client = new XrmDataverseClient();
      await expect(client.retrieveEntityMetadata('sprk_event')).rejects.toThrow(/Xrm\.Utility/);
    });
  });

  describe('retrieveEntityMetadata — targets projection (FR-21)', () => {
    it('projects targets from a Lookup attribute carrying Targets', async () => {
      const xrm = makeMockXrm();
      xrm.Utility!.getEntityMetadata.mockResolvedValue({
        PrimaryIdAttribute: 'sprk_matterid',
        PrimaryNameAttribute: 'sprk_matternumber',
        Attributes: [
          {
            LogicalName: 'sprk_mattertype_refid',
            AttributeType: 'Lookup',
            Targets: ['sprk_mattertype_ref'],
          },
        ],
      });
      xrm.WebApi.retrieveMultipleRecords.mockResolvedValue({ entities: [] });
      setWindowXrm(xrm);

      const client = new XrmDataverseClient();
      const meta = await client.retrieveEntityMetadata('sprk_matter');

      expect(meta.attributes.sprk_mattertype_refid.targets).toEqual(['sprk_mattertype_ref']);
    });

    it('leaves targets undefined (not []) for a non-lookup attribute', async () => {
      const xrm = makeMockXrm();
      xrm.Utility!.getEntityMetadata.mockResolvedValue({
        PrimaryIdAttribute: 'sprk_matterid',
        PrimaryNameAttribute: 'sprk_matternumber',
        Attributes: [{ LogicalName: 'sprk_matternumber', AttributeType: 'String', IsPrimaryName: true }],
      });
      xrm.WebApi.retrieveMultipleRecords.mockResolvedValue({ entities: [] });
      setWindowXrm(xrm);

      const client = new XrmDataverseClient();
      const meta = await client.retrieveEntityMetadata('sprk_matter');

      expect(meta.attributes.sprk_matternumber.targets).toBeUndefined();
    });
  });

  describe('retrieveEntityMetadata — page-session cache (FR-21 / NFR-01)', () => {
    it('issues zero additional network calls on a second call for the same entity', async () => {
      const xrm = makeMockXrm();
      xrm.Utility!.getEntityMetadata.mockResolvedValue({
        PrimaryIdAttribute: 'sprk_matterid',
        PrimaryNameAttribute: 'sprk_matternumber',
        Attributes: [{ LogicalName: 'sprk_matternumber', AttributeType: 'String', IsPrimaryName: true }],
      });
      xrm.WebApi.retrieveMultipleRecords.mockResolvedValue({ entities: [] });
      setWindowXrm(xrm);

      const client = new XrmDataverseClient();
      const first = await client.retrieveEntityMetadata('sprk_matter');
      const second = await client.retrieveEntityMetadata('sprk_matter');

      expect(xrm.Utility!.getEntityMetadata).toHaveBeenCalledTimes(1);
      expect(xrm.WebApi.retrieveMultipleRecords).toHaveBeenCalledTimes(1);
      expect(second).toBe(first);
    });

    it('misses the cache for a different entity name (per-entity keying)', async () => {
      const xrm = makeMockXrm();
      xrm.Utility!.getEntityMetadata
        .mockResolvedValueOnce({
          PrimaryIdAttribute: 'sprk_matterid',
          PrimaryNameAttribute: 'sprk_matternumber',
          Attributes: [],
        })
        .mockResolvedValueOnce({
          PrimaryIdAttribute: 'sprk_projectid',
          PrimaryNameAttribute: 'sprk_projectnumber',
          Attributes: [],
        });
      xrm.WebApi.retrieveMultipleRecords.mockResolvedValue({ entities: [] });
      setWindowXrm(xrm);

      const client = new XrmDataverseClient();
      const matter = await client.retrieveEntityMetadata('sprk_matter');
      const project = await client.retrieveEntityMetadata('sprk_project');

      expect(xrm.Utility!.getEntityMetadata).toHaveBeenCalledTimes(2);
      expect(xrm.Utility!.getEntityMetadata).toHaveBeenNthCalledWith(1, 'sprk_matter');
      expect(xrm.Utility!.getEntityMetadata).toHaveBeenNthCalledWith(2, 'sprk_project');
      expect(matter.primaryIdAttribute).toBe('sprk_matterid');
      expect(project.primaryIdAttribute).toBe('sprk_projectid');
    });

    it('does not cache a rejected first call — a retry re-fetches and the error is unchanged', async () => {
      const xrm = makeMockXrm();
      const networkError = new Error('network down');
      xrm.Utility!.getEntityMetadata.mockRejectedValueOnce(networkError).mockResolvedValueOnce({
        PrimaryIdAttribute: 'sprk_eventid',
        PrimaryNameAttribute: 'sprk_name',
        Attributes: [],
      });
      xrm.WebApi.retrieveMultipleRecords.mockResolvedValue({ entities: [] });
      setWindowXrm(xrm);

      const client = new XrmDataverseClient();

      await expect(client.retrieveEntityMetadata('sprk_event')).rejects.toThrow('network down');

      const retried = await client.retrieveEntityMetadata('sprk_event');

      expect(xrm.Utility!.getEntityMetadata).toHaveBeenCalledTimes(2);
      expect(retried.primaryIdAttribute).toBe('sprk_eventid');
    });
  });

  describe('retrieveMultipleRecords', () => {
    it('passes FetchXML in the encoded ?fetchXml= parameter and projects paging flags', async () => {
      const xrm = makeMockXrm();
      const fetchXml = '<fetch top="50"><entity name="sprk_event"/></fetch>';
      xrm.WebApi.retrieveMultipleRecords.mockResolvedValue({
        entities: [{ sprk_eventid: 'r1' }, { sprk_eventid: 'r2' }],
        '@Microsoft.Dynamics.CRM.morerecords': true,
        '@Microsoft.Dynamics.CRM.fetchxmlpagingcookie': 'cookie-xyz',
      });
      setWindowXrm(xrm);

      const client = new XrmDataverseClient();
      const result = await client.retrieveMultipleRecords<{ sprk_eventid: string }>('sprk_event', fetchXml);

      expect(xrm.WebApi.retrieveMultipleRecords).toHaveBeenCalledWith(
        'sprk_event',
        `?fetchXml=${encodeURIComponent(fetchXml)}`
      );
      expect(result.entities).toHaveLength(2);
      expect(result.entities[0].sprk_eventid).toBe('r1');
      expect(result.moreRecords).toBe(true);
      expect(result.pagingCookie).toBe('cookie-xyz');
    });

    it('returns moreRecords=false and no pagingCookie when last page', async () => {
      const xrm = makeMockXrm();
      xrm.WebApi.retrieveMultipleRecords.mockResolvedValue({ entities: [] });
      setWindowXrm(xrm);

      const client = new XrmDataverseClient();
      const result = await client.retrieveMultipleRecords('sprk_event', '<fetch/>');

      expect(result.moreRecords).toBe(false);
      expect(result.pagingCookie).toBeUndefined();
      expect(result.entities).toEqual([]);
    });

    it('infers moreRecords from @odata.nextLink when morerecords flag absent', async () => {
      const xrm = makeMockXrm();
      xrm.WebApi.retrieveMultipleRecords.mockResolvedValue({
        entities: [{ id: '1' }],
        '@odata.nextLink': 'https://example/next',
      });
      setWindowXrm(xrm);

      const client = new XrmDataverseClient();
      const result = await client.retrieveMultipleRecords('sprk_event', '<fetch/>');

      expect(result.moreRecords).toBe(true);
    });
  });

  describe('retrieveRecord', () => {
    it('builds $select clause when fields are provided', async () => {
      const xrm = makeMockXrm();
      xrm.WebApi.retrieveRecord.mockResolvedValue({ sprk_eventid: 'r1', sprk_name: 'Hello' });
      setWindowXrm(xrm);

      const client = new XrmDataverseClient();
      const result = await client.retrieveRecord<{ sprk_eventid: string; sprk_name: string }>('sprk_event', 'r1', [
        'sprk_eventid',
        'sprk_name',
      ]);

      expect(xrm.WebApi.retrieveRecord).toHaveBeenCalledWith('sprk_event', 'r1', '?$select=sprk_eventid,sprk_name');
      expect(result.sprk_name).toBe('Hello');
    });

    it('omits options when no select fields are provided', async () => {
      const xrm = makeMockXrm();
      xrm.WebApi.retrieveRecord.mockResolvedValue({ sprk_eventid: 'r1' });
      setWindowXrm(xrm);

      const client = new XrmDataverseClient();
      await client.retrieveRecord('sprk_event', 'r1');

      expect(xrm.WebApi.retrieveRecord).toHaveBeenCalledWith('sprk_event', 'r1', undefined);
    });
  });
});

/* eslint-enable @typescript-eslint/no-explicit-any */
