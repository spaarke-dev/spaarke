/**
 * WorkAssignmentService.createFollowOnEvent — Assigned To binds the CONTACT lookup (task 097).
 *
 * Live metadata (spaarkedev1, 2026-10-05): `sprk_event.sprk_assignedto` → contact, nav prop
 * `sprk_AssignedTo`; sprk_event has NO systemuser "assignedto" lookup, and FIVE contact lookups
 * whose column name contains "assignedto" (sprk_assignedto, _1, _2, external, internal).
 *
 * The service used `findNavProp(navProps, 'systemuser', 'assignedto')`: no systemuser column
 * matched the hint, so it fell back to the first systemuser lookup — `createdby` — and bound
 * the picked person there. Reproduced live: the follow-on event was created with
 * `sprk_assignedto` empty. ADR-038 rule 1: the asserted target is pinned to the live-verified
 * docs/data-model/sprk_event-related-tables.md row.
 *
 * Since unified-access-control-r2 task 147 the event is created THROUGH THE BFF (`POST /api/v1/child-records/sprk_event`,
 * withBffChildWrites) with the same Web API payload, so the payload is read from that request.
 */

import * as fs from 'fs';
import * as path from 'path';
import type { IDataService } from '../../../types/serviceInterfaces';
import { WorkAssignmentService } from '../workAssignmentService';
import { EMPTY_FOLLOW_ON_EVENT_STATE } from '../formTypes';
import { _resetNavPropCacheForTests } from '../../../services/PolymorphicResolverService';

const WA_ID = '33333333-3333-3333-3333-333333333333';
const CONTACT_ID = '51a5ddb0-40bf-f111-aaaf-0022482913fc';

/**
 * ManyToOneRelationships shaped like the live sprk_event response. Order is deliberately
 * adversarial: a systemuser lookup and a look-alike contact column come before sprk_assignedto.
 */
const LIVE_SHAPED_RELATIONSHIPS = [
  {
    ReferencingAttribute: 'createdby',
    ReferencingEntityNavigationPropertyName: 'createdby',
    ReferencedEntity: 'systemuser',
  },
  {
    ReferencingAttribute: 'owninguser',
    ReferencingEntityNavigationPropertyName: 'owninguser',
    ReferencedEntity: 'systemuser',
  },
  {
    ReferencingAttribute: 'sprk_assignedtoexternal',
    ReferencingEntityNavigationPropertyName: 'sprk_AssignedToExternal',
    ReferencedEntity: 'contact',
  },
  {
    ReferencingAttribute: 'sprk_assignedto1',
    ReferencingEntityNavigationPropertyName: 'sprk_AssignedTo1',
    ReferencedEntity: 'contact',
  },
  {
    ReferencingAttribute: 'sprk_assignedto',
    ReferencingEntityNavigationPropertyName: 'sprk_AssignedTo',
    ReferencedEntity: 'contact',
  },
  {
    ReferencingAttribute: 'sprk_regardingworkassignment',
    ReferencingEntityNavigationPropertyName: 'sprk_RegardingWorkAssignment',
    ReferencedEntity: 'sprk_workassignment',
  },
];

function makeDataService(): IDataService & { created: Array<{ entity: string; data: Record<string, unknown> }> } {
  const created: Array<{ entity: string; data: Record<string, unknown> }> = [];
  return {
    created,
    createRecord: jest.fn(async (entity: string, data: Record<string, unknown>) => {
      created.push({ entity, data });
      return 'new-event-id';
    }),
    retrieveRecord: jest.fn(async () => ({})),
    retrieveMultipleRecords: jest.fn(async () => ({ entities: [] })),
    updateRecord: jest.fn(async () => undefined),
    deleteRecord: jest.fn(async () => undefined),
  };
}

/** An authenticatedFetch that records every BFF child-record create and answers it with a new id. */
function makeBffFetch(): jest.Mock & { childCreates: Array<{ url: string; body: Record<string, unknown> }> } {
  const childCreates: Array<{ url: string; body: Record<string, unknown> }> = [];
  const fn = jest.fn(async (url: string, init?: { body?: string }) => {
    if (String(url).includes('/api/v1/child-records/')) {
      childCreates.push({ url: String(url), body: JSON.parse(init?.body ?? '{}') });
      return { ok: true, status: 201, json: async () => ({ id: 'new-event-id' }) };
    }
    return { ok: true, status: 200, json: async () => ({}) };
  }) as jest.Mock & { childCreates: typeof childCreates };
  fn.childCreates = childCreates;
  return fn;
}

describe('WorkAssignmentService.createFollowOnEvent — Assigned To (task 097)', () => {
  beforeEach(() => {
    _resetNavPropCacheForTests();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (global as any).fetch = jest.fn(async () => ({
      ok: true,
      status: 200,
      json: async () => ({ value: LIVE_SHAPED_RELATIONSHIPS }),
    }));
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('binds the picked contact to sprk_AssignedTo (exact column), never to a systemuser lookup', async () => {
    const dataService = makeDataService();
    const bff = makeBffFetch();
    const service = new WorkAssignmentService(dataService, bff, 'https://bff.example/api', undefined);

    const result = await service.createFollowOnEvent(WA_ID, {
      ...EMPTY_FOLLOW_ON_EVENT_STATE,
      assignedToId: `{${CONTACT_ID.toUpperCase()}}`,
      assignedToName: 'UAC Child BU Test User',
    });

    expect(result.success).toBe(true);
    const create = bff.childCreates.find(c => c.url.endsWith('/api/v1/child-records/sprk_event'))!;
    expect(create).toBeDefined();
    const payload = create.body;
    expect(payload['sprk_AssignedTo@odata.bind']).toBe(`/contacts(${CONTACT_ID})`);
    expect(payload).not.toHaveProperty('createdby@odata.bind');
    expect(payload).not.toHaveProperty('sprk_AssignedToExternal@odata.bind');
    expect(payload).not.toHaveProperty('sprk_AssignedTo1@odata.bind');
    expect(Object.values(payload).some(v => typeof v === 'string' && v.startsWith('/systemusers('))).toBe(false);
  });

  it('writes no assignee bind when none was picked', async () => {
    const dataService = makeDataService();
    const bff = makeBffFetch();
    const service = new WorkAssignmentService(dataService, bff, 'https://bff.example/api', undefined);

    await service.createFollowOnEvent(WA_ID, { ...EMPTY_FOLLOW_ON_EVENT_STATE });

    const payload = bff.childCreates.find(c => c.url.endsWith('/api/v1/child-records/sprk_event'))!.body;
    expect(Object.keys(payload).some(k => /assignedto/i.test(k))).toBe(false);
  });

  it('pins the bind target to the live-verified sprk_event data-model row', () => {
    const doc = fs.readFileSync(
      path.resolve(__dirname, '../../../../../../../../docs/data-model/sprk_event-related-tables.md'),
      'utf8'
    );
    const row = doc.split(/\r?\n/).find(l => /\|\s*sprk_event\s*\|\s*sprk_assignedto\s*\|/.test(l));
    expect(row).toBeDefined();
    expect(/Targets:<br><br>(\w+)/.exec(row as string)?.[1]).toBe('contact');
  });
});
