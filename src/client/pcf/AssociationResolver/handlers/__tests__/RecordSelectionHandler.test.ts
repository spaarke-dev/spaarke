/**
 * RecordSelectionHandler tests (SRFR-051).
 *
 * Asserts the thin-adapter contract:
 *   1. FR-C1-01 / ADR-024 — `resolveRecordType`, `buildRecordUrl`, and
 *      `resolveRecordNumberFieldName` from `@spaarke/ui-components` are the
 *      SOLE denormalized-field-value sources. We mock them and verify they
 *      are called with correct arguments.
 *   2. FR-B5-01 — 5th field (`sprk_regardingrecordnumber`) is written via
 *      delegation to `resolveRecordNumberFieldName` (or explicit override
 *      from `EntityLookupConfig.regardingRecordNumberField`) + target-record
 *      query. NFR-06 graceful-blank when no source-field or no target value.
 *   3. AssociationResolver-specific behavior preserved: clears all N
 *      entity-specific lookup fields before setting the selected one
 *      (the "clear the others" wrap around the shared 5-field write).
 *   4. Auto-detect path: `completeAutoDetectedAssociation` also delegates to
 *      shared primitives; does NOT clear other lookups (only one is set).
 *   5. ADR-038 compliance: no `Mock<HttpMessageHandler>`; inject `webApi` shim.
 *      No pass-through wrapper tests — we verify orchestration + form-write
 *      translation, not delegation-only.
 */

// --- Mock @spaarke/ui-components primitives ---
// Preserve `createLogger` so the handler's logger factory works; mock the
// primitives that this task delegates to.
jest.mock('@spaarke/ui-components', () => ({
  createLogger: () => ({
    logDebug: jest.fn(),
    logInfo: jest.fn(),
    logWarn: jest.fn(),
    logError: jest.fn(),
  }),
  buildRecordUrl: jest.fn((entityLogicalName: string, recordId: string) => {
    const cleanId = recordId.replace(/[{}]/g, '').toLowerCase();
    return `https://test.crm.dynamics.com/main.aspx?pagetype=entityrecord&etn=${entityLogicalName}&id=${cleanId}`;
  }),
  resolveRecordType: jest.fn(),
  resolveRecordNumberFieldName: jest.fn(),
}));

import { buildRecordUrl, resolveRecordType, resolveRecordNumberFieldName } from '@spaarke/ui-components';
import {
  handleRecordSelection,
  completeAutoDetectedAssociation,
  loadEntityConfigs,
  clearAllRegardingFields,
  IRecordSelection,
  IDetectedParentContext,
} from '../RecordSelectionHandler';

const mockedBuildRecordUrl = buildRecordUrl as jest.MockedFunction<typeof buildRecordUrl>;
const mockedResolveRecordType = resolveRecordType as jest.MockedFunction<typeof resolveRecordType>;
const mockedResolveRecordNumberFieldName = resolveRecordNumberFieldName as jest.MockedFunction<
  typeof resolveRecordNumberFieldName
>;

// ---------------------------------------------------------------------------
// Helpers: Xrm.Page attribute mock + webApi shim
// ---------------------------------------------------------------------------

interface AttrMock {
  setValue: jest.Mock;
  getValue: jest.Mock;
  getIsDirty: jest.Mock;
}

function makeAttr(): AttrMock {
  return {
    setValue: jest.fn(),
    getValue: jest.fn().mockReturnValue(null),
    getIsDirty: jest.fn().mockReturnValue(false),
  };
}

function installXrmPageWithFields(fieldNames: string[]): Record<string, AttrMock> {
  const attrs: Record<string, AttrMock> = {};
  for (const f of fieldNames) {
    attrs[f] = makeAttr();
  }
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (global as any).Xrm = {
    Page: {
      getAttribute: (name: string) => attrs[name] ?? null,
    },
  };
  return attrs;
}

const CATALOG_ROWS = [
  {
    sprk_recordtype_refid: 'rt-matter',
    sprk_recordlogicalname: 'sprk_matter',
    sprk_recorddisplayname: 'Matter',
    sprk_regardingfield: 'sprk_regardingmatter',
    sprk_regardingrecordnumberfield: 'sprk_matternumber',
  },
  {
    sprk_recordtype_refid: 'rt-project',
    sprk_recordlogicalname: 'sprk_project',
    sprk_recorddisplayname: 'Project',
    sprk_regardingfield: 'sprk_regardingproject',
    sprk_regardingrecordnumberfield: 'sprk_projectnumber',
  },
  {
    sprk_recordtype_refid: 'rt-contact',
    sprk_recordlogicalname: 'contact',
    sprk_recorddisplayname: 'Contact',
    sprk_regardingfield: 'sprk_regardingcontact',
    // sprk_regardingrecordnumberfield intentionally omitted (Q-06 graceful-blank)
  },
];

const DENORMALIZED_FIELD_NAMES = [
  'sprk_regardingrecordname',
  'sprk_regardingrecordid',
  'sprk_regardingrecordurl',
  'sprk_regardingrecordtype',
  'sprk_regardingrecordnumber',
];

const ENTITY_SPECIFIC_LOOKUP_NAMES = ['sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingcontact'];

// ---------------------------------------------------------------------------
// webApi shim — dispatches queries by first arg
// ---------------------------------------------------------------------------

interface WebApiCall {
  entity: string;
  query: string;
}

function makeWebApi(opts: {
  matterNumber?: string | null;
  projectNumber?: string | null;
  catalogRows?: typeof CATALOG_ROWS;
}) {
  const calls: WebApiCall[] = [];
  const catalogRows = opts.catalogRows ?? CATALOG_ROWS;
  return {
    calls,
    webApi: {
      retrieveMultipleRecords: jest.fn(async (entity: string, query: string) => {
        calls.push({ entity, query });
        if (entity === 'sprk_recordtype_ref') {
          // Config load (SRFR-050 path)
          return { entities: catalogRows };
        }
        if (entity === 'sprk_matter') {
          const val = opts.matterNumber === undefined ? 'M-1001' : opts.matterNumber;
          return {
            entities: val === null ? [{ sprk_matternumber: null }] : [{ sprk_matternumber: val }],
          };
        }
        if (entity === 'sprk_project') {
          const val = opts.projectNumber === undefined ? 'P-2002' : opts.projectNumber;
          return {
            entities: val === null ? [{ sprk_projectnumber: null }] : [{ sprk_projectnumber: val }],
          };
        }
        return { entities: [] };
      }),
      retrieveRecord: jest.fn(),
      createRecord: jest.fn(),
      updateRecord: jest.fn(),
      deleteRecord: jest.fn(),
    } as unknown as ComponentFramework.WebApi,
  };
}

// ---------------------------------------------------------------------------
// Test lifecycle
// ---------------------------------------------------------------------------

beforeEach(() => {
  jest.clearAllMocks();
  // Reset the dynamic-config module cache by unloading + reimporting is
  // heavyweight; the module exposes `loadEntityConfigs` which returns the
  // cached list on subsequent calls. Each test loads via a fresh WebApi so
  // the cache is populated once — clearAllMocks handles the primitives.
  mockedBuildRecordUrl.mockImplementation((entityLogicalName: string, recordId: string) => {
    const cleanId = recordId.replace(/[{}]/g, '').toLowerCase();
    return `https://test.crm.dynamics.com/main.aspx?pagetype=entityrecord&etn=${entityLogicalName}&id=${cleanId}`;
  });
  mockedResolveRecordType.mockResolvedValue({ id: 'rt-matter', name: 'Matter' });
  mockedResolveRecordNumberFieldName.mockResolvedValue(null);
});

// ---------------------------------------------------------------------------
// Suite
// ---------------------------------------------------------------------------

describe('RecordSelectionHandler (SRFR-051 thin adapter)', () => {
  describe('handleRecordSelection - Matter with all 5 fields', () => {
    it('delegates URL construction, record-type lookup, and record-number resolution to @spaarke/ui-components', async () => {
      const { webApi } = makeWebApi({ matterNumber: 'M-1001' });
      await loadEntityConfigs(webApi);

      installXrmPageWithFields([...DENORMALIZED_FIELD_NAMES, ...ENTITY_SPECIFIC_LOOKUP_NAMES]);

      const selection: IRecordSelection = {
        entityType: 'sprk_matter',
        recordId: '{ABC-123}',
        recordName: 'Smith v. Jones',
      };

      const result = await handleRecordSelection(selection, webApi);

      // Shared primitives invoked with correct args (delegation, not duplication)
      expect(mockedBuildRecordUrl).toHaveBeenCalledWith('sprk_matter', 'abc-123');
      expect(mockedResolveRecordType).toHaveBeenCalledWith(webApi, 'sprk_matter');
      // resolveRecordNumberFieldName is NOT called when the config already carries an override
      expect(mockedResolveRecordNumberFieldName).not.toHaveBeenCalled();

      expect(result.success).toBe(true);
      expect(result.denormalizedFieldsSet).toBe(true);
    });

    it('writes all 5 denormalized resolver fields to the form via setValue', async () => {
      const { webApi } = makeWebApi({ matterNumber: 'M-1001' });
      await loadEntityConfigs(webApi);

      const attrs = installXrmPageWithFields([...DENORMALIZED_FIELD_NAMES, ...ENTITY_SPECIFIC_LOOKUP_NAMES]);

      const selection: IRecordSelection = {
        entityType: 'sprk_matter',
        recordId: '{ABC-123}',
        recordName: 'Smith v. Jones',
      };

      await handleRecordSelection(selection, webApi);

      expect(attrs['sprk_regardingrecordname'].setValue).toHaveBeenCalledWith('Smith v. Jones');
      expect(attrs['sprk_regardingrecordid'].setValue).toHaveBeenCalledWith('abc-123');
      expect(attrs['sprk_regardingrecordurl'].setValue).toHaveBeenCalledWith(
        expect.stringContaining('etn=sprk_matter')
      );
      expect(attrs['sprk_regardingrecordtype'].setValue).toHaveBeenCalledWith([
        expect.objectContaining({ id: 'rt-matter', entityType: 'sprk_recordtype_ref' }),
      ]);
      // 5th field per FR-B5-01
      expect(attrs['sprk_regardingrecordnumber'].setValue).toHaveBeenCalledWith('M-1001');
    });
  });

  describe('handleRecordSelection - other-lookup-clearing behavior (AssociationResolver-specific)', () => {
    it('clears all N entity-specific lookups before setting the selected one', async () => {
      const { webApi } = makeWebApi({ matterNumber: 'M-1001' });
      await loadEntityConfigs(webApi);

      const attrs = installXrmPageWithFields([...DENORMALIZED_FIELD_NAMES, ...ENTITY_SPECIFIC_LOOKUP_NAMES]);

      const selection: IRecordSelection = {
        entityType: 'sprk_matter',
        recordId: 'abc-123',
        recordName: 'Smith v. Jones',
      };

      const result = await handleRecordSelection(selection, webApi);

      // Each entity-specific lookup was passed through the clear step
      expect(attrs['sprk_regardingmatter'].setValue).toHaveBeenCalled();
      expect(attrs['sprk_regardingproject'].setValue).toHaveBeenCalledWith(null);
      expect(attrs['sprk_regardingcontact'].setValue).toHaveBeenCalledWith(null);

      // Selected lookup ended up as a lookup value (last setValue call is the SET, not the CLEAR)
      const matterCalls = attrs['sprk_regardingmatter'].setValue.mock.calls;
      const lastCall = matterCalls[matterCalls.length - 1][0];
      expect(lastCall).toEqual([
        expect.objectContaining({ id: 'abc-123', entityType: 'sprk_matter', name: 'Smith v. Jones' }),
      ]);

      // otherLookupsCleared count = configs.length - 1 (subtract the selected one)
      expect(result.otherLookupsCleared).toBe(CATALOG_ROWS.length - 1);
    });
  });

  describe('handleRecordSelection - graceful-blank per NFR-06', () => {
    it('skips 5th-field write when target record has null record-number value', async () => {
      const { webApi } = makeWebApi({ matterNumber: null });
      await loadEntityConfigs(webApi);

      const attrs = installXrmPageWithFields([...DENORMALIZED_FIELD_NAMES, ...ENTITY_SPECIFIC_LOOKUP_NAMES]);

      const selection: IRecordSelection = {
        entityType: 'sprk_matter',
        recordId: 'abc-123',
        recordName: 'Smith v. Jones',
      };

      const result = await handleRecordSelection(selection, webApi);

      // 4-field write succeeds
      expect(attrs['sprk_regardingrecordname'].setValue).toHaveBeenCalled();
      expect(attrs['sprk_regardingrecordid'].setValue).toHaveBeenCalled();
      expect(attrs['sprk_regardingrecordurl'].setValue).toHaveBeenCalled();
      expect(attrs['sprk_regardingrecordtype'].setValue).toHaveBeenCalled();

      // 5th field NOT written when target value is null
      expect(attrs['sprk_regardingrecordnumber'].setValue).not.toHaveBeenCalled();

      // Overall selection still succeeds — graceful-blank is not a failure
      expect(result.success).toBe(true);
    });

    it('skips 5th-field write when config has no regardingRecordNumberField (Contact / Q-06)', async () => {
      const { webApi } = makeWebApi({});
      await loadEntityConfigs(webApi);

      const attrs = installXrmPageWithFields([...DENORMALIZED_FIELD_NAMES, ...ENTITY_SPECIFIC_LOOKUP_NAMES]);

      // Contact config carries NO regardingRecordNumberField; shared resolver
      // will be consulted and returns null (per default mock setup) → graceful-blank.
      const selection: IRecordSelection = {
        entityType: 'contact',
        recordId: 'contact-abc',
        recordName: 'John Doe',
      };

      mockedResolveRecordType.mockResolvedValue({ id: 'rt-contact', name: 'Contact' });
      const result = await handleRecordSelection(selection, webApi);

      // Shared resolver was consulted (no explicit override in config)
      expect(mockedResolveRecordNumberFieldName).toHaveBeenCalledWith(webApi, 'contact');

      // 5th field NOT written
      expect(attrs['sprk_regardingrecordnumber'].setValue).not.toHaveBeenCalled();

      // Selection still succeeds
      expect(result.success).toBe(true);
    });
  });

  describe('handleRecordSelection - error path', () => {
    it('returns error for unknown entity type', async () => {
      const { webApi } = makeWebApi({});
      await loadEntityConfigs(webApi);

      installXrmPageWithFields([...DENORMALIZED_FIELD_NAMES]);

      const selection: IRecordSelection = {
        entityType: 'sprk_unknown',
        recordId: 'x',
        recordName: 'X',
      };

      const result = await handleRecordSelection(selection, webApi);
      expect(result.success).toBe(false);
      expect(result.errors.some(e => e.includes('Unknown entity type'))).toBe(true);
    });
  });

  describe('completeAutoDetectedAssociation - auto-detect path', () => {
    it('writes 5 denormalized fields via shared primitives without clearing others', async () => {
      const { webApi } = makeWebApi({ matterNumber: 'M-1001' });
      await loadEntityConfigs(webApi);

      const attrs = installXrmPageWithFields([...DENORMALIZED_FIELD_NAMES, ...ENTITY_SPECIFIC_LOOKUP_NAMES]);

      const detected: IDetectedParentContext = {
        entityType: 'sprk_matter',
        entityDisplayName: 'Matter',
        recordId: 'abc-123',
        recordName: 'Smith v. Jones',
        regardingField: 'sprk_regardingmatter',
      };

      const result = await completeAutoDetectedAssociation(detected, webApi);

      // Shared primitives delegated to
      expect(mockedBuildRecordUrl).toHaveBeenCalledWith('sprk_matter', 'abc-123');
      expect(mockedResolveRecordType).toHaveBeenCalledWith(webApi, 'sprk_matter');

      // 5 denormalized fields written
      expect(attrs['sprk_regardingrecordname'].setValue).toHaveBeenCalledWith('Smith v. Jones');
      expect(attrs['sprk_regardingrecordid'].setValue).toHaveBeenCalledWith('abc-123');
      expect(attrs['sprk_regardingrecordurl'].setValue).toHaveBeenCalled();
      expect(attrs['sprk_regardingrecordtype'].setValue).toHaveBeenCalled();
      expect(attrs['sprk_regardingrecordnumber'].setValue).toHaveBeenCalledWith('M-1001');

      // Auto-detect does NOT clear other lookups (Dataverse relationship-map
      // has already set the correct one; skipping the clear preserves it)
      // Verify the OTHER lookups were not touched:
      expect(attrs['sprk_regardingproject'].setValue).not.toHaveBeenCalled();
      expect(attrs['sprk_regardingcontact'].setValue).not.toHaveBeenCalled();

      expect(result.success).toBe(true);
      expect(result.otherLookupsCleared).toBe(0);
    });
  });

  describe('clearAllRegardingFields', () => {
    it('nulls all 5 denormalized fields including sprk_regardingrecordnumber', async () => {
      const { webApi } = makeWebApi({});
      await loadEntityConfigs(webApi);

      const attrs = installXrmPageWithFields([...DENORMALIZED_FIELD_NAMES, ...ENTITY_SPECIFIC_LOOKUP_NAMES]);

      clearAllRegardingFields();

      expect(attrs['sprk_regardingrecordname'].setValue).toHaveBeenCalledWith(null);
      expect(attrs['sprk_regardingrecordid'].setValue).toHaveBeenCalledWith(null);
      expect(attrs['sprk_regardingrecordurl'].setValue).toHaveBeenCalledWith(null);
      expect(attrs['sprk_regardingrecordtype'].setValue).toHaveBeenCalledWith(null);
      expect(attrs['sprk_regardingrecordnumber'].setValue).toHaveBeenCalledWith(null);

      // All entity-specific lookups also cleared
      expect(attrs['sprk_regardingmatter'].setValue).toHaveBeenCalledWith(null);
      expect(attrs['sprk_regardingproject'].setValue).toHaveBeenCalledWith(null);
      expect(attrs['sprk_regardingcontact'].setValue).toHaveBeenCalledWith(null);
    });
  });
});
