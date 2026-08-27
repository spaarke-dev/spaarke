/**
 * lookupSearch — the Dataverse half of the inline record-header lookup.
 *
 * ── Why this file exists ───────────────────────────────────────────────────
 * This module replaces R1's hard-coded `LOOKUP_META` table with metadata
 * resolution, and it owns the ONLY `Xrm.Utility.lookupObjects` call site in
 * the shared library. Both of those are places this project has already been
 * burned:
 *
 *   - the target's primary NAME attribute is NOT derivable from a pattern
 *     (`sprk_projecttype_ref` → `sprk_name` but `sprk_mattertype_ref` →
 *     `sprk_mattertypename`), so "read it from metadata" is pinned here with
 *     BOTH conventions;
 *   - `lookupObjects` reads `this._clientApiExecutor`, so a detached call
 *     throws — the bug that cost UAT round 5 (FAILURE-MODES G-14). The stub
 *     below is `this`-sensitive so this suite can fail for that reason.
 *
 * Per ADR-038 this is a KEEP-category suite: it drives the module's public
 * surface and stubs only the host `Xrm` global — the same boundary every other
 * consumer of this code stubs.
 */

import {
  buildLookupSearchOptions,
  escapeODataLiteral,
  openAdvancedLookup,
  resolveLookupTargetKeys,
  searchLookupTarget,
  LOOKUP_SEARCH_PAGE_SIZE,
} from '../lookupSearch';
import { _resetEntityMetadataCacheForTests } from '../../../services/XrmDataverseClient';

// Two REAL Spaarke taxonomy tables whose primary-name attributes follow
// different conventions. Inferring either from the other is the mistake.
const PROJECT_TYPE = {
  entity: 'sprk_projecttype_ref',
  idAttribute: 'sprk_projecttype_refid',
  nameAttribute: 'sprk_name',
};
const MATTER_TYPE = {
  entity: 'sprk_mattertype_ref',
  idAttribute: 'sprk_mattertype_refid',
  nameAttribute: 'sprk_mattertypename',
};

const originalXrm = (window as unknown as { Xrm?: unknown }).Xrm;

function setXrm(value: unknown): void {
  (window as unknown as { Xrm?: unknown }).Xrm = value;
}

/** Install an Xrm shim whose `getEntityMetadata` answers for the given tables. */
function stubMetadata(
  tables: Array<{ entity: string; idAttribute: string; nameAttribute: string }>,
  extra: Record<string, unknown> = {}
): jest.Mock {
  const getEntityMetadata = jest.fn(async (entityName: string) => {
    const match = tables.find(t => t.entity === entityName);
    if (!match) throw new Error(`no metadata for ${entityName}`);
    return { PrimaryIdAttribute: match.idAttribute, PrimaryNameAttribute: match.nameAttribute, Attributes: [] };
  });
  setXrm({ WebApi: { retrieveMultipleRecords: jest.fn() }, Utility: { getEntityMetadata }, ...extra });
  return getEntityMetadata;
}

beforeEach(() => {
  // The client caches metadata for the page session — without this, one test's
  // stub answers the next test's call.
  _resetEntityMetadataCacheForTests();
  jest.spyOn(console, 'warn').mockImplementation(() => undefined);
});

afterEach(() => {
  jest.restoreAllMocks();
  if (originalXrm === undefined) {
    delete (window as unknown as { Xrm?: unknown }).Xrm;
  } else {
    setXrm(originalXrm);
  }
});

describe('escapeODataLiteral', () => {
  it("doubles single quotes so O'Brien cannot terminate the literal", () => {
    expect(escapeODataLiteral("O'Brien")).toBe("O''Brien");
  });

  it('leaves a quote-free term untouched', () => {
    expect(escapeODataLiteral('Litigation')).toBe('Litigation');
  });
});

describe('buildLookupSearchOptions', () => {
  it('OMITS $filter entirely for an empty query — this is what makes browse work', () => {
    // `contains(name,'')` would be a filter that matches everything by accident;
    // omitting it is the deliberate "return the first N rows" browse path.
    const options = buildLookupSearchOptions(PROJECT_TYPE.idAttribute, PROJECT_TYPE.nameAttribute, '');

    expect(options).not.toContain('$filter');
    expect(options).toContain(`$select=${PROJECT_TYPE.idAttribute},${PROJECT_TYPE.nameAttribute}`);
    expect(options).toContain(`$orderby=${PROJECT_TYPE.nameAttribute} asc`);
    expect(options).toContain(`$top=${LOOKUP_SEARCH_PAGE_SIZE}`);
  });

  it('treats a whitespace-only query as empty', () => {
    expect(buildLookupSearchOptions('id', 'name', '   ')).not.toContain('$filter');
  });

  it('filters with contains() on the PRIMARY NAME attribute', () => {
    const options = buildLookupSearchOptions(MATTER_TYPE.idAttribute, MATTER_TYPE.nameAttribute, 'Lit');
    expect(options).toContain(`$filter=contains(${MATTER_TYPE.nameAttribute},'Lit')`);
  });

  it("escapes a quote the ODATA way — doubled, NOT percent-encoded", () => {
    // `encodeURIComponent` deliberately leaves `'` alone (it is in the
    // unescaped set, alongside `-_.!~*()`), so the doubling is the only thing
    // protecting this literal. The two mechanisms are not interchangeable and
    // neither one alone is sufficient — see the sibling test below.
    const options = buildLookupSearchOptions('id', 'name', "O'Brien");
    expect(options).toContain("contains(name,'O''Brien')");
  });

  it('percent-encodes the characters that would otherwise break the QUERY STRING', () => {
    // This is the half `encodeURIComponent` is actually there for: an
    // unencoded `&` would terminate the $filter parameter and the rest of the
    // term would arrive as a bogus query parameter.
    const options = buildLookupSearchOptions('id', 'name', 'M & A');
    expect(options).toContain("contains(name,'M%20%26%20A')");
  });

  it('honours a caller-supplied page size', () => {
    expect(buildLookupSearchOptions('id', 'name', '', 3)).toContain('$top=3');
  });
});

describe('resolveLookupTargetKeys', () => {
  it('reads BOTH primary attributes off the target table', async () => {
    stubMetadata([PROJECT_TYPE]);
    await expect(resolveLookupTargetKeys(PROJECT_TYPE.entity)).resolves.toEqual({
      idAttribute: PROJECT_TYPE.idAttribute,
      nameAttribute: PROJECT_TYPE.nameAttribute,
    });
  });

  it('does not infer the name attribute — a second table with a different convention resolves differently', async () => {
    // The whole reason this round trip exists. `sprk_name` for one taxonomy
    // table, `sprk_mattertypename` for another; there is no pattern to guess.
    stubMetadata([PROJECT_TYPE, MATTER_TYPE]);

    const project = await resolveLookupTargetKeys(PROJECT_TYPE.entity);
    const matter = await resolveLookupTargetKeys(MATTER_TYPE.entity);

    expect(project?.nameAttribute).toBe('sprk_name');
    expect(matter?.nameAttribute).toBe('sprk_mattertypename');
  });

  it('returns null and WARNS when the payload carries no primary attributes', async () => {
    // Silent otherwise: the cell would render an empty dropdown and read as
    // "this table has no rows".
    stubMetadata([{ entity: 'sprk_broken_ref', idAttribute: '', nameAttribute: '' }]);

    await expect(resolveLookupTargetKeys('sprk_broken_ref')).resolves.toBeNull();
    expect(console.warn).toHaveBeenCalled();
  });

  it('returns null rather than throwing when metadata fails', async () => {
    stubMetadata([]);
    await expect(resolveLookupTargetKeys('sprk_missing_ref')).resolves.toBeNull();
  });

  it('returns null for an empty target without touching Xrm', async () => {
    const getEntityMetadata = stubMetadata([PROJECT_TYPE]);
    await expect(resolveLookupTargetKeys('')).resolves.toBeNull();
    expect(getEntityMetadata).not.toHaveBeenCalled();
  });
});

describe('searchLookupTarget', () => {
  function stubSearch(
    table: { entity: string; idAttribute: string; nameAttribute: string },
    rows: Array<Record<string, unknown>>
  ): jest.Mock {
    const retrieveMultipleRecords = jest.fn(async () => ({ entities: rows }));
    stubMetadata([table]);
    setXrm({
      WebApi: { retrieveMultipleRecords },
      Utility: {
        getEntityMetadata: jest.fn(async () => ({
          PrimaryIdAttribute: table.idAttribute,
          PrimaryNameAttribute: table.nameAttribute,
          Attributes: [],
        })),
      },
    });
    return retrieveMultipleRecords;
  }

  it('queries the TARGET table and projects rows onto {id, name}', async () => {
    const retrieve = stubSearch(PROJECT_TYPE, [
      { [PROJECT_TYPE.idAttribute]: 'g1', [PROJECT_TYPE.nameAttribute]: 'Commercial' },
      { [PROJECT_TYPE.idAttribute]: 'g2', [PROJECT_TYPE.nameAttribute]: 'Litigation' },
    ]);

    await expect(searchLookupTarget(PROJECT_TYPE.entity, 'C')).resolves.toEqual([
      { id: 'g1', name: 'Commercial' },
      { id: 'g2', name: 'Litigation' },
    ]);
    expect(retrieve).toHaveBeenCalledWith(PROJECT_TYPE.entity, expect.stringContaining('$filter=contains'));
  });

  it("reads each table's OWN name attribute, not a guessed one", async () => {
    stubSearch(MATTER_TYPE, [{ [MATTER_TYPE.idAttribute]: 'm1', [MATTER_TYPE.nameAttribute]: 'Dispute' }]);

    // Would come back `name: 'undefined'` if the module assumed `sprk_name`.
    await expect(searchLookupTarget(MATTER_TYPE.entity, '')).resolves.toEqual([{ id: 'm1', name: 'Dispute' }]);
  });

  it('drops rows with no id rather than emitting an unselectable option', async () => {
    stubSearch(PROJECT_TYPE, [
      { [PROJECT_TYPE.nameAttribute]: 'Orphan' },
      { [PROJECT_TYPE.idAttribute]: 'g1', [PROJECT_TYPE.nameAttribute]: 'Real' },
    ]);

    await expect(searchLookupTarget(PROJECT_TYPE.entity, '')).resolves.toEqual([{ id: 'g1', name: 'Real' }]);
  });

  it('resolves [] — never rejects — when the query fails', async () => {
    stubMetadata([PROJECT_TYPE]);
    setXrm({
      WebApi: {
        retrieveMultipleRecords: jest.fn(async () => {
          throw new Error('HTTP 400');
        }),
      },
      Utility: {
        getEntityMetadata: jest.fn(async () => ({
          PrimaryIdAttribute: PROJECT_TYPE.idAttribute,
          PrimaryNameAttribute: PROJECT_TYPE.nameAttribute,
          Attributes: [],
        })),
      },
    });

    await expect(searchLookupTarget(PROJECT_TYPE.entity, 'x')).resolves.toEqual([]);
    expect(console.warn).toHaveBeenCalled();
  });

  it('resolves [] when the target metadata cannot be resolved', async () => {
    stubMetadata([]);
    await expect(searchLookupTarget('sprk_missing_ref', 'x')).resolves.toEqual([]);
  });
});

describe('openAdvancedLookup', () => {
  /**
   * ══════════════════════════════════════════════════════════════════════════
   * `this`-SENSITIVE ON PURPOSE. Do not simplify to a plain `jest.fn(impl)`.
   * ══════════════════════════════════════════════════════════════════════════
   * The real `Xrm.Utility.lookupObjects` reads `this._clientApiExecutor`, so a
   * detached alias call throws. This module is the single place that call now
   * lives, so this is the single place that discipline can be pinned. A plain
   * `jest.fn()` neither needs nor checks its receiver — which is exactly how
   * 19 green tests once coexisted with a picker that threw on every click.
   */
  function stubLookupObjects(
    impl: (options: unknown) => Promise<Array<{ id: string; name: string; entityType: string }>>
  ): jest.Mock {
    const utility: Record<string, unknown> = { _clientApiExecutor: {} };
    const lookupObjects = jest.fn(function (this: unknown, options: unknown) {
      if ((this as Record<string, unknown> | undefined)?._clientApiExecutor === undefined) {
        throw new TypeError("Cannot read properties of undefined (reading '_clientApiExecutor')");
      }
      return impl(options);
    });
    utility.lookupObjects = lookupObjects;
    setXrm({ WebApi: {}, Utility: utility });
    return lookupObjects;
  }

  it('opens the dialog scoped to the single target and returns the pick', async () => {
    const lookupObjects = stubLookupObjects(async () => [
      { id: '{ABC-123}', name: 'Litigation', entityType: MATTER_TYPE.entity },
    ]);

    const picked = await openAdvancedLookup(MATTER_TYPE.entity, 'Matter Type');

    expect(lookupObjects).toHaveBeenCalledWith({
      entityTypes: [MATTER_TYPE.entity],
      defaultEntityType: MATTER_TYPE.entity,
      allowMultiSelect: false,
    });
    // Brace-stripped + lowercased, so it compares equal to the projections
    // `useRecordFieldValues` produces.
    expect(picked).toEqual({ id: 'abc-123', name: 'Litigation', entityType: MATTER_TYPE.entity });
  });

  it('resolves null on cancel — a cancel must never stage a clear', async () => {
    stubLookupObjects(async () => []);
    await expect(openAdvancedLookup(MATTER_TYPE.entity, 'Matter Type')).resolves.toBeNull();
  });

  it('resolves null and WARNS when the host exposes no lookupObjects', async () => {
    setXrm({ WebApi: {}, Utility: {} });
    await expect(openAdvancedLookup(MATTER_TYPE.entity, 'Matter Type')).resolves.toBeNull();
    expect(console.warn).toHaveBeenCalledWith(expect.stringContaining('lookupObjects is unavailable'));
  });

  it('resolves null and WARNS when the dialog throws — never propagates', async () => {
    stubLookupObjects(async () => {
      throw new Error('picker exploded');
    });
    await expect(openAdvancedLookup(MATTER_TYPE.entity, 'Matter Type')).resolves.toBeNull();
    expect(console.warn).toHaveBeenCalled();
  });
});
