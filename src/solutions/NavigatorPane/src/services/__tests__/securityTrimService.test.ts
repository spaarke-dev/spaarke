/**
 * securityTrimService Tests (task 080, spec FR-12 / NFR-04)
 *
 * Verifies the task-080 closed acceptance-criteria set:
 *   - A 403 (insufficient privilege) target classifies `denied`.
 *   - A 404 (record no longer exists) target classifies `denied`.
 *   - An accessible retrieve classifies `accessible`.
 *   - A network/timeout error classifies `transient` (NOT denied — the
 *     row must not be permanently dropped on a blip).
 *   - A 5xx error classifies `transient`.
 *   - A weblink / no-Dataverse-target row classifies `accessible` WITHOUT
 *     issuing a retrieve at all (no record identity to leak).
 *   - An EntityList (saved-view bookmark) row ALSO classifies `accessible`
 *     without a retrieve — its `targetid` is a view id, not a record id in
 *     `targetlogicalname`'s entity set, so retrieving it would falsely 404.
 *   - The classifier never throws, even on a bizarre/non-Error rejection.
 *
 * @see ../securityTrimService.ts
 */

import {
  classifyTargetAccess,
  classifyTargets,
  classifyWebApiError,
  isRecordTarget,
  trimTargetFromRow,
  type TrimTarget,
} from '../securityTrimService';

const NavItemPageType = {
  EntityRecord: 100000000,
  EntityList: 100000001,
  Custom: 100000002,
  WebLink: 100000003,
} as const;

const RECORD_ID = '11111111-1111-1111-1111-111111111111';

function buildFakeXrm(retrieveRecordImpl: (entity: string, id: string, options?: string) => Promise<unknown>) {
  return {
    WebApi: {
      retrieveRecord: jest.fn(retrieveRecordImpl),
      retrieveMultipleRecords: jest.fn(),
      createRecord: jest.fn(),
      updateRecord: jest.fn(),
      deleteRecord: jest.fn(),
    },
  } as unknown as import('@spaarke/ui-components').XrmContext;
}

function recordTarget(overrides: Partial<TrimTarget> = {}): TrimTarget {
  return {
    key: 'row-1',
    logicalName: 'sprk_matter',
    id: RECORD_ID,
    pageType: NavItemPageType.EntityRecord,
    ...overrides,
  };
}

describe('securityTrimService', () => {
  // ───────────────────────────────────────────────────────────────────────
  // 403 / 404 -> denied
  // ───────────────────────────────────────────────────────────────────────

  it('a 403 (insufficient privilege) target classifies denied', async () => {
    const xrm = buildFakeXrm(async () => {
      throw new Error('Principal user is missing prvReadsprk_matter privilege (403)');
    });

    const result = await classifyTargetAccess(xrm, recordTarget());
    expect(result).toBe('denied');
    expect(xrm.WebApi.retrieveRecord).toHaveBeenCalledWith('sprk_matter', RECORD_ID, '?$select=sprk_matterid');
  });

  it('a 404 (record no longer exists) target classifies denied', async () => {
    const xrm = buildFakeXrm(async () => {
      throw new Error('sprk_matter With Id = 11111111-1111-1111-1111-111111111111 Does Not Exist');
    });

    const result = await classifyTargetAccess(xrm, recordTarget());
    expect(result).toBe('denied');
  });

  it('a raw 403 status on the error object classifies denied', async () => {
    const xrm = buildFakeXrm(async () => {
      const err = new Error('request failed') as Error & { status?: number };
      err.status = 403;
      throw err;
    });

    expect(await classifyTargetAccess(xrm, recordTarget())).toBe('denied');
  });

  it('a raw 404 status on the error object classifies denied', async () => {
    const xrm = buildFakeXrm(async () => {
      const err = new Error('request failed') as Error & { status?: number };
      err.status = 404;
      throw err;
    });

    expect(await classifyTargetAccess(xrm, recordTarget())).toBe('denied');
  });

  // ───────────────────────────────────────────────────────────────────────
  // Accessible
  // ───────────────────────────────────────────────────────────────────────

  it('an accessible retrieve classifies accessible', async () => {
    const xrm = buildFakeXrm(async () => ({ sprk_matterid: RECORD_ID }));

    const result = await classifyTargetAccess(xrm, recordTarget());
    expect(result).toBe('accessible');
  });

  // ───────────────────────────────────────────────────────────────────────
  // Transient — NEVER permanently drops an accessible row on a blip
  // ───────────────────────────────────────────────────────────────────────

  it('a network error classifies transient (not denied)', async () => {
    const xrm = buildFakeXrm(async () => {
      throw new Error('Network error while syncing navigation history.');
    });

    expect(await classifyTargetAccess(xrm, recordTarget())).toBe('transient');
  });

  it('a fetch/timeout error classifies transient', async () => {
    const xrm = buildFakeXrm(async () => {
      throw new Error('The operation timed out while trying to fetch the resource');
    });

    expect(await classifyTargetAccess(xrm, recordTarget())).toBe('transient');
  });

  it('a 5xx status classifies transient', async () => {
    const xrm = buildFakeXrm(async () => {
      const err = new Error('Internal Server Error') as Error & { status?: number };
      err.status = 503;
      throw err;
    });

    expect(await classifyTargetAccess(xrm, recordTarget())).toBe('transient');
  });

  it('an unrecognized/ambiguous error defaults to transient (never assumed denied)', async () => {
    const xrm = buildFakeXrm(async () => {
      throw new Error('Something unexpected happened');
    });

    expect(await classifyTargetAccess(xrm, recordTarget())).toBe('transient');
  });

  it('missing Xrm.WebApi classifies transient (cannot verify, never assume denial)', async () => {
    const result = await classifyTargetAccess(undefined, recordTarget());
    expect(result).toBe('transient');
  });

  it('a bizarre non-Error rejection never throws and classifies transient', async () => {
    const xrm = buildFakeXrm(async () => {
      // eslint-disable-next-line @typescript-eslint/no-throw-literal -- intentional pathological case
      throw { unexpected: true };
    });

    await expect(classifyTargetAccess(xrm, recordTarget())).resolves.toBe('transient');
  });

  it('classifyWebApiError never throws on a circular object', () => {
    const circular: Record<string, unknown> = {};
    circular.self = circular;
    expect(() => classifyWebApiError(circular)).not.toThrow();
    expect(classifyWebApiError(circular)).toBe('transient');
  });

  // ───────────────────────────────────────────────────────────────────────
  // Exemptions — accessible WITHOUT a retrieve (no record identity to leak)
  // ───────────────────────────────────────────────────────────────────────

  it('a weblink row (no Dataverse target) classifies accessible without a retrieve', async () => {
    const xrm = buildFakeXrm(async () => ({}));
    const target: TrimTarget = {
      key: 'row-link',
      logicalName: null,
      id: null,
      pageType: NavItemPageType.WebLink,
    };

    const result = await classifyTargetAccess(xrm, target);
    expect(result).toBe('accessible');
    expect(xrm.WebApi.retrieveRecord).not.toHaveBeenCalled();
  });

  it('an EntityList (saved-view bookmark) row classifies accessible without a retrieve (targetid is a view id, not a record id)', async () => {
    const xrm = buildFakeXrm(async () => ({}));
    const target: TrimTarget = {
      key: 'row-view',
      logicalName: 'sprk_document',
      id: 'view-id-not-a-record-id',
      pageType: NavItemPageType.EntityList,
    };

    const result = await classifyTargetAccess(xrm, target);
    expect(result).toBe('accessible');
    expect(xrm.WebApi.retrieveRecord).not.toHaveBeenCalled();
  });

  it('a Custom pagetype row (no target at all) classifies accessible without a retrieve', async () => {
    const xrm = buildFakeXrm(async () => ({}));
    const target: TrimTarget = {
      key: 'row-custom',
      logicalName: null,
      id: null,
      pageType: NavItemPageType.Custom,
    };

    const result = await classifyTargetAccess(xrm, target);
    expect(result).toBe('accessible');
    expect(xrm.WebApi.retrieveRecord).not.toHaveBeenCalled();
  });

  it('isRecordTarget is true only for EntityRecord rows with both logicalName and id', () => {
    expect(isRecordTarget(recordTarget())).toBe(true);
    expect(isRecordTarget({ pageType: NavItemPageType.EntityRecord, logicalName: null, id: RECORD_ID })).toBe(false);
    expect(isRecordTarget({ pageType: NavItemPageType.EntityRecord, logicalName: 'sprk_matter', id: null })).toBe(
      false
    );
    expect(isRecordTarget({ pageType: NavItemPageType.WebLink, logicalName: 'sprk_matter', id: RECORD_ID })).toBe(
      false
    );
  });

  // ───────────────────────────────────────────────────────────────────────
  // Batched classification
  // ───────────────────────────────────────────────────────────────────────

  it('classifyTargets batches multiple targets and returns a map keyed by target.key', async () => {
    const inaccessibleId = '22222222-2222-2222-2222-222222222222';
    const xrm = buildFakeXrm(async (_entity: string, id: string) => {
      if (id === inaccessibleId) {
        throw new Error('does not exist');
      }
      return { sprk_matterid: id };
    });

    const targets: TrimTarget[] = [
      recordTarget({ key: 'accessible-row', id: RECORD_ID }),
      recordTarget({ key: 'denied-row', id: inaccessibleId }),
      { key: 'link-row', logicalName: null, id: null, pageType: NavItemPageType.WebLink },
    ];

    const result = await classifyTargets(xrm, targets);
    expect(result.get('accessible-row')).toBe('accessible');
    expect(result.get('denied-row')).toBe('denied');
    expect(result.get('link-row')).toBe('accessible');
    expect(result.size).toBe(3);

    // Only the two EntityRecord targets triggered a retrieve — the weblink
    // row never did (exemption verified at the batch level too).
    expect(xrm.WebApi.retrieveRecord).toHaveBeenCalledTimes(2);
  });

  it('trimTargetFromRow adapts a NavItemRecord row into a TrimTarget', () => {
    const row = {
      sprk_navitemid: 'nav-1',
      sprk_type: 100000000,
      sprk_source: 100000000,
      sprk_targetlogicalname: 'sprk_matter',
      sprk_targetid: RECORD_ID,
      sprk_pagetype: NavItemPageType.EntityRecord,
      sprk_url: null,
      sprk_displayname: 'Acme v. Widget Co',
      sprk_lastvisited: '2026-08-13T00:00:00.000Z',
      sprk_visitcount: 1,
    };

    expect(trimTargetFromRow(row)).toEqual({
      key: 'nav-1',
      logicalName: 'sprk_matter',
      id: RECORD_ID,
      pageType: NavItemPageType.EntityRecord,
    });
  });
});
