/**
 * Unit tests for `resolveSearchIndexNameAsync` (Phase G — v1.1.75).
 *
 * Verifies the lookup-driven search-index resolution that replaces the
 * v1.1.74 manifest-bound `searchIndexName` property.
 *
 * Contract (per spec §3.1 + §5):
 *   - Reads `_sprk_ai_search_index_value` + `$expand=sprk_ai_search_index($select=sprk_searchindexname)`
 *     via `context.webAPI.retrieveRecord(entityType, entityId, options)`
 *   - Returns the expanded `sprk_searchindexname` string on success
 *   - Returns `null` if entityType OR entityId is missing
 *   - Returns `null` if `context.webAPI.retrieveRecord` throws
 *   - Returns `null` if the lookup is unset (record has no expanded nav property)
 *
 * Downstream contract: `null` triggers BFF tenant default (omit-on-empty per
 * tasks 031/032 + the v1.1.75 manifest change that drops the bound property).
 *
 * @see ../SemanticSearchControl.tsx (resolveSearchIndexNameAsync)
 * @see projects/spaarke-multi-container-multi-index-r1/notes/phase-g/spec.md §3.1, §5
 */

// The SUT (`SearchIndexResolver.ts`) is a small standalone module that does
// not transitively import any ESM-only dependencies, so no jest.mock setup is
// required here — we just import the function directly.

import { resolveSearchIndexNameAsync } from '../services/SearchIndexResolver';
import { IInputs } from '../generated/ManifestTypes';

/**
 * Build a minimal PCF context shape exposing only the fields the resolver
 * reads: `mode.contextInfo.{entityTypeName, entityId}` and `webAPI.retrieveRecord`.
 */
function buildContext(opts: {
  entityTypeName?: string | undefined;
  entityId?: string | undefined;
  retrieveRecord?: jest.Mock;
}): ComponentFramework.Context<IInputs> {
  const retrieveRecord = opts.retrieveRecord ?? jest.fn().mockResolvedValue({ sprk_ai_search_index: null });
  return {
    mode: {
      contextInfo: {
        entityTypeName: opts.entityTypeName,
        entityId: opts.entityId,
      },
    },
    webAPI: {
      retrieveRecord,
    },
  } as unknown as ComponentFramework.Context<IInputs>;
}

describe('resolveSearchIndexNameAsync — Phase G v1.1.75', () => {
  // ---------------------------------------------------------------------------
  // Happy path — lookup populated → resolver returns the linked index name
  // ---------------------------------------------------------------------------
  it('returns the linked sprk_searchindexname when the lookup is set', async () => {
    const retrieveRecord = jest.fn().mockResolvedValue({
      _sprk_ai_search_index_value: '11111111-2222-3333-4444-555555555555',
      sprk_ai_search_index: {
        sprk_searchindexname: 'spaarke-file-index',
      },
    });
    const context = buildContext({
      entityTypeName: 'sprk_matter',
      entityId: 'aaaa-bbbb-cccc-dddd',
      retrieveRecord,
    });

    const result = await resolveSearchIndexNameAsync(context);

    expect(result).toBe('spaarke-file-index');
    expect(retrieveRecord).toHaveBeenCalledTimes(1);
    expect(retrieveRecord).toHaveBeenCalledWith(
      'sprk_matter',
      'aaaa-bbbb-cccc-dddd',
      expect.stringContaining('_sprk_ai_search_index_value')
    );
    // Confirm the $expand clause uses the correct navigation property name
    // (`sprk_ai_search_index`, NOT `sprk_aisearchindexid` — per spec §3.1).
    const optionsArg = retrieveRecord.mock.calls[0][2] as string;
    expect(optionsArg).toContain('$expand=sprk_ai_search_index($select=sprk_searchindexname)');
  });

  // ---------------------------------------------------------------------------
  // Missing context — entityType or entityId absent → null
  // ---------------------------------------------------------------------------
  it('returns null when entityTypeName is missing (not on a record form)', async () => {
    const retrieveRecord = jest.fn();
    const context = buildContext({
      entityTypeName: undefined,
      entityId: 'aaaa-bbbb-cccc-dddd',
      retrieveRecord,
    });

    const result = await resolveSearchIndexNameAsync(context);

    expect(result).toBeNull();
    // No WebAPI call should fire when we can't even identify the host record.
    expect(retrieveRecord).not.toHaveBeenCalled();
  });

  it('returns null when entityId is missing', async () => {
    const retrieveRecord = jest.fn();
    const context = buildContext({
      entityTypeName: 'sprk_matter',
      entityId: undefined,
      retrieveRecord,
    });

    const result = await resolveSearchIndexNameAsync(context);

    expect(result).toBeNull();
    expect(retrieveRecord).not.toHaveBeenCalled();
  });

  // ---------------------------------------------------------------------------
  // Lookup unset — record returns null/missing nav property → null
  // ---------------------------------------------------------------------------
  it('returns null when the lookup column is unset on the host record', async () => {
    // OData returns the record but the nav property is null when the lookup is
    // unset. The resolver should treat this as "no override" and return null
    // so the BFF tenant default applies.
    const retrieveRecord = jest.fn().mockResolvedValue({
      _sprk_ai_search_index_value: null,
      sprk_ai_search_index: null,
    });
    const context = buildContext({
      entityTypeName: 'sprk_matter',
      entityId: 'aaaa-bbbb-cccc-dddd',
      retrieveRecord,
    });

    const result = await resolveSearchIndexNameAsync(context);

    expect(result).toBeNull();
  });

  it('returns null when the expanded record has the nav property but no sprk_searchindexname', async () => {
    // Defensive: the linked catalog row exists but has an empty name field
    // (data-quality issue). Treat as null — BFF tenant default applies.
    const retrieveRecord = jest.fn().mockResolvedValue({
      sprk_ai_search_index: {
        // sprk_searchindexname missing entirely
      },
    });
    const context = buildContext({
      entityTypeName: 'sprk_matter',
      entityId: 'aaaa-bbbb-cccc-dddd',
      retrieveRecord,
    });

    const result = await resolveSearchIndexNameAsync(context);

    expect(result).toBeNull();
  });

  // ---------------------------------------------------------------------------
  // WebAPI failure — retrieveRecord throws → null (fallback path)
  // ---------------------------------------------------------------------------
  it('returns null when context.webAPI.retrieveRecord throws (e.g., lookup column not yet deployed)', async () => {
    const retrieveRecord = jest
      .fn()
      .mockRejectedValue(new Error("Could not find a property named 'sprk_ai_search_index'"));
    const context = buildContext({
      entityTypeName: 'sprk_matter',
      entityId: 'aaaa-bbbb-cccc-dddd',
      retrieveRecord,
    });

    const result = await resolveSearchIndexNameAsync(context);

    expect(result).toBeNull();
    expect(retrieveRecord).toHaveBeenCalledTimes(1);
  });

  it('returns null when context.webAPI.retrieveRecord rejects with a generic network error', async () => {
    const retrieveRecord = jest.fn().mockRejectedValue(new TypeError('NetworkError'));
    const context = buildContext({
      entityTypeName: 'sprk_invoice',
      entityId: 'zzz',
      retrieveRecord,
    });

    const result = await resolveSearchIndexNameAsync(context);

    expect(result).toBeNull();
  });

  // ---------------------------------------------------------------------------
  // Entity-type tolerance — resolver does NOT hardcode entity types; it just
  // forwards entityTypeName through to WebAPI.
  // ---------------------------------------------------------------------------
  it('resolves successfully for any host entity (sprk_project)', async () => {
    const retrieveRecord = jest.fn().mockResolvedValue({
      sprk_ai_search_index: { sprk_searchindexname: 'spaarke-records-index' },
    });
    const context = buildContext({
      entityTypeName: 'sprk_project',
      entityId: 'p-1',
      retrieveRecord,
    });

    const result = await resolveSearchIndexNameAsync(context);

    expect(result).toBe('spaarke-records-index');
    expect(retrieveRecord).toHaveBeenCalledWith('sprk_project', 'p-1', expect.any(String));
  });

  it('resolves successfully for sprk_event', async () => {
    const retrieveRecord = jest.fn().mockResolvedValue({
      sprk_ai_search_index: { sprk_searchindexname: 'spaarke-events-index' },
    });
    const context = buildContext({
      entityTypeName: 'sprk_event',
      entityId: 'e-1',
      retrieveRecord,
    });

    const result = await resolveSearchIndexNameAsync(context);

    expect(result).toBe('spaarke-events-index');
  });
});
