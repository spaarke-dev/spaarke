/**
 * Regression suite for the RecordHeader v1.1.0 UAT defect: entity metadata
 * never reached the resolver, so every field derived the `text` renderer, every
 * label humanized its logical name, and a Lookup got `$select`ed by its BARE
 * name — which 400s the whole read and turns every cell into an em-dash.
 *
 * Two independent root causes are pinned here, each with the evidence that
 * proved it:
 *
 * **RC-1 — the label/type rescue call could never succeed.**
 * `fetchAttributeDisplayNames` issued
 * `Xrm.WebApi.retrieveMultipleRecords('EntityDefinition', ...)`. `Xrm.WebApi`
 * resolves its first argument to an entity SET name through the client's entity
 * catalog, and `entitydefinition` is not an entity: a live query against
 * spaarkedev1 for `EntityDefinitions?$filter=LogicalName eq 'entitydefinition'`
 * returns `{"value":[]}`. The repo states the same constraint independently in
 * `SemanticSearchControl/services/DataverseMetadataService.ts` ("Xrm.WebApi
 * doesn't support metadata entities like EntityDefinitions"), and R2's own
 * spec.md calls EntityDefinitions "unreachable by `Xrm.WebApi`". The call threw
 * every time and its `.catch()` swallowed the throw, so the rescue map was
 * ALWAYS empty.
 *
 * **RC-2 — `projectAttribute` parsed only Web-API shapes.**
 * The CLIENT API (`Xrm.Utility.getEntityMetadata`) returns a different payload,
 * documented by Microsoft at
 * learn.microsoft.com/.../xrm-utility/getentitymetadata under "Attribute
 * objects": `AttributeType` is a **Number** (`AttributeTypeCode`) and
 * `DisplayName` is a plain **String**. The old code required a string
 * `AttributeType` and a `DisplayName.UserLocalizedLabel.Label` object, so every
 * attribute of every entity projected as `String` with no label.
 *
 * Attribute types below are the live-verified `sprk_project` schema.
 */

import { XrmDataverseClient, _resetEntityMetadataCacheForTests } from '../XrmDataverseClient';

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
      retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    },
    Utility: { getEntityMetadata: jest.fn() },
  };
}

/**
 * The `sprk_project` metadata EXACTLY as the client API hands it over: numeric
 * `AttributeTypeCode` values and plain-string display names.
 *
 * Codes: 6=Lookup, 2=DateTime, 0=Boolean, 7=Memo, 14=String, 11=Picklist.
 */
const CLIENT_API_PAYLOAD = {
  PrimaryIdAttribute: 'sprk_projectid',
  PrimaryNameAttribute: 'sprk_projectnumber',
  Attributes: [
    { LogicalName: 'sprk_projecttype_ref', AttributeType: 6, DisplayName: 'Project Type' },
    { LogicalName: 'sprk_openeddate', AttributeType: 2, DisplayName: 'Opened Date' },
    { LogicalName: 'sprk_highpriority', AttributeType: 0, DisplayName: 'High Priority' },
    { LogicalName: 'sprk_projectdescription', AttributeType: 7, DisplayName: 'Project Description' },
    { LogicalName: 'sprk_recordsummary', AttributeType: 7, DisplayName: 'Record Summary' },
    { LogicalName: 'sprk_projectnumber', AttributeType: 14, DisplayName: 'Project Number', IsPrimaryName: true },
  ],
};

let originalXrm: unknown;

beforeEach(() => {
  originalXrm = (window as any).Xrm;
  _resetEntityMetadataCacheForTests();
  jest.restoreAllMocks();
});

afterEach(() => {
  (window as any).Xrm = originalXrm;
  _resetEntityMetadataCacheForTests();
});

describe('XrmDataverseClient — client-API metadata shape (RC-2)', () => {
  it('maps NUMERIC AttributeTypeCode values to their type names', async () => {
    const xrm = makeMockXrm();
    xrm.Utility!.getEntityMetadata.mockResolvedValue(CLIENT_API_PAYLOAD);
    (window as any).Xrm = xrm;

    const meta = await new XrmDataverseClient().retrieveEntityMetadata('sprk_project');

    // The defect: every one of these used to come back as 'String'.
    expect(meta.attributes.sprk_projecttype_ref.attributeType).toBe('Lookup');
    expect(meta.attributes.sprk_openeddate.attributeType).toBe('DateTime');
    expect(meta.attributes.sprk_highpriority.attributeType).toBe('Boolean');
    expect(meta.attributes.sprk_projectdescription.attributeType).toBe('Memo');
    expect(meta.attributes.sprk_projectnumber.attributeType).toBe('String');
  });

  it('reads a plain-string DisplayName (client API) as the attribute label', async () => {
    const xrm = makeMockXrm();
    xrm.Utility!.getEntityMetadata.mockResolvedValue(CLIENT_API_PAYLOAD);
    (window as any).Xrm = xrm;

    const meta = await new XrmDataverseClient().retrieveEntityMetadata('sprk_project');

    // The defect: undefined displayName -> the resolver humanized the logical
    // name, which is where "Openeddate" and "Highpriority" came from.
    expect(meta.attributes.sprk_openeddate.displayName).toBe('Opened Date');
    expect(meta.attributes.sprk_highpriority.displayName).toBe('High Priority');
  });

  it('still reads the Web-API label object shape', async () => {
    const xrm = makeMockXrm();
    xrm.Utility!.getEntityMetadata.mockResolvedValue({
      PrimaryIdAttribute: 'id',
      PrimaryNameAttribute: 'name',
      Attributes: [
        {
          LogicalName: 'sprk_openeddate',
          AttributeType: 'DateTime',
          DisplayName: { UserLocalizedLabel: { Label: 'Opened Date' } },
        },
      ],
    });
    (window as any).Xrm = xrm;

    const meta = await new XrmDataverseClient().retrieveEntityMetadata('sprk_project');

    expect(meta.attributes.sprk_openeddate.attributeType).toBe('DateTime');
    expect(meta.attributes.sprk_openeddate.displayName).toBe('Opened Date');
  });

  it('marks a genuinely unknown AttributeType as Unknown, never as String', async () => {
    const xrm = makeMockXrm();
    xrm.Utility!.getEntityMetadata.mockResolvedValue({
      Attributes: [{ LogicalName: 'weird', AttributeType: { nope: true } }],
    });
    (window as any).Xrm = xrm;

    const meta = await new XrmDataverseClient().retrieveEntityMetadata('sprk_project');

    // Asserting 'String' here would be a lie about the schema — and asserting a
    // type we do not have is precisely how a Lookup became a text cell.
    expect(meta.attributes.weird.attributeType).toBe('Unknown');
  });

  it('projects an option set from all three shapes', async () => {
    const xrm = makeMockXrm();
    xrm.Utility!.getEntityMetadata.mockResolvedValue({
      Attributes: [
        // Web API
        {
          LogicalName: 'a',
          AttributeType: 11,
          OptionSet: { Options: [{ Value: 1, Label: { UserLocalizedLabel: { Label: 'Open' } } }] },
        },
        // Client API — OptionSet IS the array (@types/xrm declares this shape)
        { LogicalName: 'b', AttributeType: 11, OptionSet: [{ Value: 2, Label: 'Closed' }] },
        // Client API — documented "key:value pair" bag
        { LogicalName: 'c', AttributeType: 0, OptionSet: { 1: 'Yes', 0: 'No' } },
      ],
    });
    (window as any).Xrm = xrm;

    const meta = await new XrmDataverseClient().retrieveEntityMetadata('sprk_project');

    expect(meta.attributes.a.optionSet).toEqual([{ value: 1, label: 'Open', color: undefined }]);
    expect(meta.attributes.b.optionSet).toEqual([{ value: 2, label: 'Closed', color: undefined }]);
    expect(meta.attributes.c.optionSet).toEqual([
      { value: 0, label: 'No' },
      { value: 1, label: 'Yes' },
    ]);
  });

  it('flattens an Xrm collection whose members are only reachable via getAll()', async () => {
    const xrm = makeMockXrm();
    const items = [{ LogicalName: 'sprk_projecttype_ref', AttributeType: 6 }];
    xrm.Utility!.getEntityMetadata.mockResolvedValue({
      // A real StringIndexableItemCollection: accessor methods live alongside
      // the string keys, so a naive Object.values() would yield the FUNCTIONS.
      Attributes: {
        getAll: () => items,
        get: (n?: unknown) => (n === undefined ? items : items[0]),
        getLength: () => items.length,
        forEach: (cb: (i: unknown) => void) => items.forEach(cb),
      },
    });
    (window as any).Xrm = xrm;

    const meta = await new XrmDataverseClient().retrieveEntityMetadata('sprk_project');

    expect(Object.keys(meta.attributes)).toEqual(['sprk_projecttype_ref']);
    expect(meta.attributes.sprk_projecttype_ref.attributeType).toBe('Lookup');
  });
});

describe('XrmDataverseClient — metadata transport (RC-1)', () => {
  it('never routes metadata through Xrm.WebApi (EntityDefinitions is unreachable there)', async () => {
    const xrm = makeMockXrm();
    xrm.Utility!.getEntityMetadata.mockResolvedValue(CLIENT_API_PAYLOAD);
    (window as any).Xrm = xrm;

    await new XrmDataverseClient().retrieveEntityMetadata('sprk_project');

    // Xrm.WebApi cannot serve metadata entities; any call here is dead code
    // whose failure is swallowed by a catch. Proven live: a query for
    // `LogicalName eq 'entitydefinition'` returns an empty set.
    expect(xrm.WebApi.retrieveMultipleRecords).not.toHaveBeenCalled();
  });

  it('forwards an explicit attribute list to getEntityMetadata so Attributes is populated', async () => {
    const xrm = makeMockXrm();
    xrm.Utility!.getEntityMetadata.mockResolvedValue(CLIENT_API_PAYLOAD);
    (window as any).Xrm = xrm;

    await new XrmDataverseClient().retrieveEntityMetadata('sprk_project', ['sprk_openeddate', 'sprk_projecttype_ref']);

    expect(xrm.Utility!.getEntityMetadata).toHaveBeenCalledWith('sprk_project', [
      // normalized: de-duplicated + sorted
      'sprk_openeddate',
      'sprk_projecttype_ref',
    ]);
  });

  it('omits the second argument when no attributes are requested', async () => {
    const xrm = makeMockXrm();
    xrm.Utility!.getEntityMetadata.mockResolvedValue(CLIENT_API_PAYLOAD);
    (window as any).Xrm = xrm;

    await new XrmDataverseClient().retrieveEntityMetadata('sprk_project');

    expect(xrm.Utility!.getEntityMetadata).toHaveBeenCalledWith('sprk_project');
  });

  it('keys the cache on the requested attribute set, not the entity alone', async () => {
    const xrm = makeMockXrm();
    xrm.Utility!.getEntityMetadata.mockResolvedValue(CLIENT_API_PAYLOAD);
    (window as any).Xrm = xrm;
    const client = new XrmDataverseClient();

    await client.retrieveEntityMetadata('sprk_project', ['sprk_openeddate']);
    await client.retrieveEntityMetadata('sprk_project', ['sprk_openeddate']);
    // A narrow cached entry must NOT satisfy a broader request.
    await client.retrieveEntityMetadata('sprk_project', ['sprk_openeddate', 'sprk_highpriority']);

    expect(xrm.Utility!.getEntityMetadata).toHaveBeenCalledTimes(2);
  });

  it('treats a reordered attribute list as the same cache entry', async () => {
    const xrm = makeMockXrm();
    xrm.Utility!.getEntityMetadata.mockResolvedValue(CLIENT_API_PAYLOAD);
    (window as any).Xrm = xrm;
    const client = new XrmDataverseClient();

    await client.retrieveEntityMetadata('sprk_project', ['b', 'a']);
    await client.retrieveEntityMetadata('sprk_project', ['a', 'b']);

    expect(xrm.Utility!.getEntityMetadata).toHaveBeenCalledTimes(1);
  });
});
