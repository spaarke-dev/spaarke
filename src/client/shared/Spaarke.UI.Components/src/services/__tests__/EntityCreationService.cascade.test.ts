/**
 * EntityCreationService cascade helpers — Unit Tests
 *
 * Scope: the INV-5-safe BU-cascade static helpers added for
 * spaarke-multi-container-multi-index-r1 / FR-WIZ-01..08.
 *
 * Covered:
 *   - applyDefaultContainerId: INV-5 (skip when explicit), cascade (set when empty), empty-input no-op
 *   - applyDefaultSearchIndexName: INV-5, cascade, empty-input no-op (NULL BU value scenario)
 *   - applyUserBuDefaults: composes the two helpers, INV-5 honored per-field, null-defaults safe
 *   - resolveUserBuDefaults: systemuser → BU chain, normalizes empty strings to undefined,
 *     handles unset BU fields gracefully (no throw — leaves fields undefined)
 */

import { EntityCreationService, type IUserBuCascadeDefaults } from '../EntityCreationService';
import type { IWebApiLike } from '../../types/WebApiLike';

// ---------------------------------------------------------------------------
// applyDefaultContainerId
// ---------------------------------------------------------------------------

describe('EntityCreationService.applyDefaultContainerId', () => {
  it('sets sprk_containerid on empty payload when BU value is provided (cascade)', () => {
    const entity: Record<string, unknown> = { sprk_mattername: 'Test Matter' };
    const result = EntityCreationService.applyDefaultContainerId(entity, 'bu-container-abc');
    expect(result).toBe(true);
    expect(entity['sprk_containerid']).toBe('bu-container-abc');
  });

  it('does NOT overwrite when payload already has explicit sprk_containerid (INV-5)', () => {
    const entity: Record<string, unknown> = {
      sprk_mattername: 'Test Matter',
      sprk_containerid: 'explicit-override-xyz',
    };
    const result = EntityCreationService.applyDefaultContainerId(entity, 'bu-container-abc');
    expect(result).toBe(false);
    expect(entity['sprk_containerid']).toBe('explicit-override-xyz');
  });

  it('is a no-op when containerId input is undefined / null / empty string', () => {
    const e1: Record<string, unknown> = {};
    expect(EntityCreationService.applyDefaultContainerId(e1, undefined)).toBe(false);
    expect('sprk_containerid' in e1).toBe(false);

    const e2: Record<string, unknown> = {};
    expect(EntityCreationService.applyDefaultContainerId(e2, null)).toBe(false);
    expect('sprk_containerid' in e2).toBe(false);

    const e3: Record<string, unknown> = {};
    expect(EntityCreationService.applyDefaultContainerId(e3, '')).toBe(false);
    expect('sprk_containerid' in e3).toBe(false);
  });

  it('treats whitespace-only existing value as empty (cascade can fill)', () => {
    const entity: Record<string, unknown> = { sprk_containerid: '   ' };
    const result = EntityCreationService.applyDefaultContainerId(entity, 'bu-container-abc');
    expect(result).toBe(true);
    expect(entity['sprk_containerid']).toBe('bu-container-abc');
  });

  it('treats null existing value as empty (cascade can fill)', () => {
    const entity: Record<string, unknown> = { sprk_containerid: null };
    const result = EntityCreationService.applyDefaultContainerId(entity, 'bu-container-abc');
    expect(result).toBe(true);
    expect(entity['sprk_containerid']).toBe('bu-container-abc');
  });
});

// ---------------------------------------------------------------------------
// applyDefaultSearchIndexName
// ---------------------------------------------------------------------------

describe('EntityCreationService.applyDefaultSearchIndexName', () => {
  it('sets sprk_searchindexname on empty payload when BU value is provided (cascade)', () => {
    const entity: Record<string, unknown> = { sprk_mattername: 'Test Matter' };
    const result = EntityCreationService.applyDefaultSearchIndexName(entity, 'spaarke-knowledge-index-v2');
    expect(result).toBe(true);
    expect(entity['sprk_searchindexname']).toBe('spaarke-knowledge-index-v2');
  });

  it('does NOT overwrite when payload already has explicit sprk_searchindexname (INV-5)', () => {
    const entity: Record<string, unknown> = {
      sprk_mattername: 'Protected Matter',
      sprk_searchindexname: 'spaarke-file-index', // operator override
    };
    const result = EntityCreationService.applyDefaultSearchIndexName(entity, 'spaarke-knowledge-index-v2');
    expect(result).toBe(false);
    expect(entity['sprk_searchindexname']).toBe('spaarke-file-index');
  });

  it('leaves field unset when BU has NULL sprk_searchindexname (tenant default takes over server-side)', () => {
    // Spaarke Dev 1 / Test 1 scenario per Phase A.5: BU exists but field is unset.
    const entity: Record<string, unknown> = { sprk_mattername: 'Test Matter' };
    const result = EntityCreationService.applyDefaultSearchIndexName(entity, undefined);
    expect(result).toBe(false);
    expect('sprk_searchindexname' in entity).toBe(false);
  });

  it('is a no-op when searchIndexName input is null / empty string', () => {
    const e1: Record<string, unknown> = {};
    expect(EntityCreationService.applyDefaultSearchIndexName(e1, null)).toBe(false);
    expect('sprk_searchindexname' in e1).toBe(false);

    const e2: Record<string, unknown> = {};
    expect(EntityCreationService.applyDefaultSearchIndexName(e2, '')).toBe(false);
    expect('sprk_searchindexname' in e2).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// applyUserBuDefaults
// ---------------------------------------------------------------------------

describe('EntityCreationService.applyUserBuDefaults', () => {
  it('applies both fields when BU has both populated and payload is empty', () => {
    const entity: Record<string, unknown> = { sprk_mattername: 'Test Matter' };
    const defaults: IUserBuCascadeDefaults = {
      businessUnitId: 'bu-123',
      containerId: 'bu-container-abc',
      searchIndexName: 'spaarke-knowledge-index-v2',
    };
    const result = EntityCreationService.applyUserBuDefaults(entity, defaults);
    expect(result).toEqual({ containerIdSet: true, searchIndexNameSet: true });
    expect(entity['sprk_containerid']).toBe('bu-container-abc');
    expect(entity['sprk_searchindexname']).toBe('spaarke-knowledge-index-v2');
  });

  it('honors INV-5 per-field independently — pre-seeded indexname kept, container filled', () => {
    const entity: Record<string, unknown> = {
      sprk_mattername: 'Protected Matter',
      sprk_searchindexname: 'spaarke-file-index',
    };
    const defaults: IUserBuCascadeDefaults = {
      businessUnitId: 'bu-123',
      containerId: 'bu-container-abc',
      searchIndexName: 'spaarke-knowledge-index-v2',
    };
    const result = EntityCreationService.applyUserBuDefaults(entity, defaults);
    expect(result).toEqual({ containerIdSet: true, searchIndexNameSet: false });
    expect(entity['sprk_containerid']).toBe('bu-container-abc');
    expect(entity['sprk_searchindexname']).toBe('spaarke-file-index'); // override preserved
  });

  it('leaves a field unset when the corresponding BU field is undefined (NULL BU)', () => {
    const entity: Record<string, unknown> = { sprk_mattername: 'Test Matter' };
    const defaults: IUserBuCascadeDefaults = {
      businessUnitId: 'bu-123',
      containerId: 'bu-container-abc',
      searchIndexName: undefined, // BU exists but field is unset
    };
    const result = EntityCreationService.applyUserBuDefaults(entity, defaults);
    expect(result).toEqual({ containerIdSet: true, searchIndexNameSet: false });
    expect(entity['sprk_containerid']).toBe('bu-container-abc');
    expect('sprk_searchindexname' in entity).toBe(false);
  });

  it('is a no-op when defaults is null or undefined', () => {
    const entity: Record<string, unknown> = { sprk_mattername: 'Test Matter' };
    const r1 = EntityCreationService.applyUserBuDefaults(entity, null);
    expect(r1).toEqual({ containerIdSet: false, searchIndexNameSet: false });
    const r2 = EntityCreationService.applyUserBuDefaults(entity, undefined);
    expect(r2).toEqual({ containerIdSet: false, searchIndexNameSet: false });
    expect('sprk_containerid' in entity).toBe(false);
    expect('sprk_searchindexname' in entity).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// resolveUserBuDefaults
// ---------------------------------------------------------------------------

describe('EntityCreationService.resolveUserBuDefaults', () => {
  function makeWebApi(
    userBuId: string | null,
    buFields: { sprk_containerid?: string | null; sprk_searchindexname?: string | null }
  ): IWebApiLike {
    return {
      retrieveRecord: jest.fn().mockImplementation(async (entityType: string, _id: string, _options?: string) => {
        if (entityType === 'systemuser') {
          return { _businessunitid_value: userBuId };
        }
        if (entityType === 'businessunit') {
          return buFields as Record<string, unknown>;
        }
        throw new Error(`Unexpected entityType: ${entityType}`);
      }),
      retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    };
  }

  it('chains systemuser → businessunit and returns both populated fields', async () => {
    const webApi = makeWebApi('bu-guid-1', {
      sprk_containerid: 'bu-container-abc',
      sprk_searchindexname: 'spaarke-knowledge-index-v2',
    });
    const result = await EntityCreationService.resolveUserBuDefaults(webApi, 'user-guid-1');

    expect(result).toEqual({
      businessUnitId: 'bu-guid-1',
      containerId: 'bu-container-abc',
      searchIndexName: 'spaarke-knowledge-index-v2',
    });
    expect(webApi.retrieveRecord).toHaveBeenCalledWith('systemuser', 'user-guid-1', '?$select=_businessunitid_value');
    expect(webApi.retrieveRecord).toHaveBeenCalledWith(
      'businessunit',
      'bu-guid-1',
      '?$select=sprk_containerid,sprk_searchindexname'
    );
  });

  it('strips braces from userId before the lookup', async () => {
    const webApi = makeWebApi('bu-guid-1', { sprk_containerid: 'c', sprk_searchindexname: 'i' });
    await EntityCreationService.resolveUserBuDefaults(webApi, '{user-guid-1}');
    expect(webApi.retrieveRecord).toHaveBeenCalledWith('systemuser', 'user-guid-1', '?$select=_businessunitid_value');
  });

  it('returns all-undefined when user has no business unit', async () => {
    const webApi = makeWebApi(null, {});
    const result = await EntityCreationService.resolveUserBuDefaults(webApi, 'user-guid-1');
    expect(result).toEqual({
      businessUnitId: undefined,
      containerId: undefined,
      searchIndexName: undefined,
    });
    // BU lookup must be skipped when there is no BU id.
    expect(webApi.retrieveRecord).toHaveBeenCalledTimes(1);
  });

  it('normalizes NULL BU fields to undefined (Spaarke Dev 1 / Test 1 scenario)', async () => {
    const webApi = makeWebApi('bu-guid-1', {
      sprk_containerid: 'bu-container-abc',
      sprk_searchindexname: null, // BU exists; index name not yet configured
    });
    const result = await EntityCreationService.resolveUserBuDefaults(webApi, 'user-guid-1');
    expect(result).toEqual({
      businessUnitId: 'bu-guid-1',
      containerId: 'bu-container-abc',
      searchIndexName: undefined,
    });
  });

  it('normalizes empty-string BU fields to undefined', async () => {
    const webApi = makeWebApi('bu-guid-1', {
      sprk_containerid: '   ',
      sprk_searchindexname: '',
    });
    const result = await EntityCreationService.resolveUserBuDefaults(webApi, 'user-guid-1');
    expect(result).toEqual({
      businessUnitId: 'bu-guid-1',
      containerId: undefined,
      searchIndexName: undefined,
    });
  });
});

// ---------------------------------------------------------------------------
// End-to-end: resolveUserBuDefaults + applyUserBuDefaults
// ---------------------------------------------------------------------------

describe('EntityCreationService — resolve + apply (end-to-end INV-5 contract)', () => {
  it('full pipeline: resolve from user BU → apply to payload, INV-5 honored', async () => {
    const webApi: IWebApiLike = {
      retrieveRecord: jest.fn().mockImplementation(async (entityType: string) => {
        if (entityType === 'systemuser') return { _businessunitid_value: 'bu-guid' };
        if (entityType === 'businessunit') {
          return {
            sprk_containerid: 'bu-container-abc',
            sprk_searchindexname: 'spaarke-knowledge-index-v2',
          };
        }
        throw new Error('unexpected');
      }),
      retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    };

    // Payload with explicit indexname override (mimicking an operator-set Protected Matter create)
    const entity: Record<string, unknown> = {
      sprk_mattername: 'Protected Matter',
      sprk_searchindexname: 'spaarke-file-index',
    };

    const defaults = await EntityCreationService.resolveUserBuDefaults(webApi, 'user-guid');
    const applied = EntityCreationService.applyUserBuDefaults(entity, defaults);

    expect(applied).toEqual({ containerIdSet: true, searchIndexNameSet: false });
    expect(entity['sprk_containerid']).toBe('bu-container-abc');
    expect(entity['sprk_searchindexname']).toBe('spaarke-file-index'); // INV-5 preserved
  });
});
