/**
 * WorkAssignmentService.createWorkAssignment — optional Assigned To contact picker (field-mapping-server-write-path-r1
 * task 038, FR-37).
 *
 * The server (task 037, FR-36) fills `sprk_workassignment.sprk_assignedto` from the row's own
 * `sprk_assignedtointernal` ONLY when the create leaves the column UNSET (no value, no bind). An explicit value or an
 * explicit null from the caller wins. So the wizard must:
 *   - BLANK picker  -> send NO key for sprk_assignedto (no `@odata.bind`, no null), so the server default can apply;
 *   - chosen contact -> send exactly one `{navProp}@odata.bind = /contacts({guid})` where navProp is the relationship
 *     whose ReferencingAttribute is exactly `sprk_assignedto`;
 *   - nav-prop not found -> send no bind, warn, and still create (same as the follow-on event step, task 097).
 *
 * Live metadata (docs/data-model/sprk_workassignment.md): `sprk_assignedto` -> contact, nav prop `sprk_AssignedTo`.
 * sprk_workassignment has SIX other contact lookups whose names are look-alikes (assigned attorney/paralegal/law firm
 * attorney) plus systemuser lookups (createdby, owninguser); the order below is deliberately adversarial: the
 * look-alikes and a systemuser lookup are listed BEFORE sprk_assignedto.
 *
 * Classification (ADR-038 §7): MAINTAIN — pins the FR-36/FR-37 UNSET-vs-explicit-null contract the client half depends on.
 */

import { bffChildWriteFetch } from '../../../__mocks__/bffChildWriteFake';
import type { IDataService } from '../../../types/serviceInterfaces';
import { _resetNavPropCacheForTests } from '../../../services/PolymorphicResolverService';
import { WorkAssignmentService } from '../workAssignmentService';
import { EMPTY_WORK_ASSIGNMENT_FORM } from '../formTypes';
import type { ICreateWorkAssignmentFormState } from '../formTypes';

const WA_ID = '33333333-3333-3333-3333-333333333333';
const CONTACT_ID = '51a5ddb0-40bf-f111-aaaf-0022482913fc';

const ASSIGNED_TO_REL = {
  ReferencingAttribute: 'sprk_assignedto',
  ReferencingEntityNavigationPropertyName: 'sprk_AssignedTo',
  ReferencedEntity: 'contact',
};

/** ManyToOneRelationships shaped like the live sprk_workassignment response, look-alikes first. */
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
    ReferencingAttribute: 'sprk_assignedattorney1',
    ReferencingEntityNavigationPropertyName: 'sprk_AssignedAttorney1',
    ReferencedEntity: 'contact',
  },
  {
    ReferencingAttribute: 'sprk_assignedparalegal1',
    ReferencingEntityNavigationPropertyName: 'sprk_AssignedParalegal1',
    ReferencedEntity: 'contact',
  },
  {
    ReferencingAttribute: 'sprk_assignedtointernal',
    ReferencingEntityNavigationPropertyName: 'sprk_AssignedToInternal',
    ReferencedEntity: 'contact',
  },
  {
    ReferencingAttribute: 'sprk_assignedto1',
    ReferencingEntityNavigationPropertyName: 'sprk_AssignedTo1',
    ReferencedEntity: 'contact',
  },
  ASSIGNED_TO_REL,
];

function stubFetch(workAssignmentRelationships: unknown[]) {
  (global as unknown as { fetch: unknown }).fetch = jest.fn(async (url: string) => ({
    ok: true,
    status: 200,
    json: async () => ({ value: url.includes("LogicalName='sprk_workassignment'") ? workAssignmentRelationships : [] }),
  }));
}

function makeDataService(): IDataService & { created: Record<string, unknown>[] } {
  const created: Record<string, unknown>[] = [];
  return {
    created,
    createRecord: jest.fn(async (_entity: string, data: Record<string, unknown>) => {
      created.push(data);
      return WA_ID;
    }),
    retrieveRecord: jest.fn(async () => ({})),
    retrieveMultipleRecords: jest.fn(async () => ({ entities: [] })),
    updateRecord: jest.fn(async () => undefined),
    deleteRecord: jest.fn(async () => undefined),
  };
}

function form(over: Partial<ICreateWorkAssignmentFormState> = {}): ICreateWorkAssignmentFormState {
  return { ...EMPTY_WORK_ASSIGNMENT_FORM, name: 'zz-assigned-to', ...over };
}

/** Run a create with the given form and nav-prop metadata; return the payload that reached the BFF and the result. */
async function create(relationships: unknown[], f: ICreateWorkAssignmentFormState) {
  stubFetch(relationships);
  const ds = makeDataService();
  const service = new WorkAssignmentService(
    ds,
    bffChildWriteFetch(
      ds,
      async () => ({ ok: false, status: 404, json: async () => ({}), text: async () => '' }) as Response
    ),
    'https://bff.example',
    undefined
  );
  const result = await service.createWorkAssignment(f, [], []);
  expect(ds.created).toHaveLength(1);
  return { payload: ds.created[0], result };
}

describe('WorkAssignmentService.createWorkAssignment — Assigned To (task 038, FR-37)', () => {
  let warn: jest.SpyInstance;

  beforeEach(() => {
    _resetNavPropCacheForTests();
    warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    jest.spyOn(console, 'info').mockImplementation(() => undefined);
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('binds the chosen contact to sprk_AssignedTo (exact column) with exactly one bind, never a look-alike or systemuser lookup', async () => {
    const { payload, result } = await create(
      LIVE_SHAPED_RELATIONSHIPS,
      form({ assignedToId: `{${CONTACT_ID.toUpperCase()}}`, assignedToName: 'Chosen Contact' })
    );

    expect(result.status).not.toBe('error');
    expect(payload['sprk_AssignedTo@odata.bind']).toBe(`/contacts(${CONTACT_ID})`);
    // Exactly one key names the assigned-to column family, and it is the exact one.
    expect(Object.keys(payload).filter(k => /assignedto/i.test(k))).toEqual(['sprk_AssignedTo@odata.bind']);
    // Exactly one bind points at the chosen contact; none points at a systemuser.
    expect(Object.values(payload).filter(v => v === `/contacts(${CONTACT_ID})`)).toHaveLength(1);
    expect(Object.values(payload).some(v => typeof v === 'string' && v.startsWith('/systemusers('))).toBe(false);
  });

  it.each([
    ['empty id', { assignedToId: '', assignedToName: '' }],
    ['id never set', { assignedToId: undefined, assignedToName: undefined }],
  ])('a blank picker (%s) sends no key for sprk_assignedto: no bind and no null', async (_label, over) => {
    // The nav-prop IS discoverable, so the omission is the blank, not missing metadata.
    const { payload, result } = await create(LIVE_SHAPED_RELATIONSHIPS, form(over));

    expect(result.status).not.toBe('error');
    expect(Object.keys(payload).filter(k => /assignedto/i.test(k))).toEqual([]);
    expect(payload).not.toHaveProperty('sprk_assignedto');
    expect(Object.values(payload).some(v => typeof v === 'string' && v.startsWith('/contacts('))).toBe(false);
    expect(Object.values(payload).includes(null)).toBe(false);
    // A blank picker is not a failure: no warning.
    expect(result.warnings).toEqual([]);
  });

  it('no sprk_assignedto nav-prop on the entity: no bind, a console warning, and the create still proceeds', async () => {
    const withoutAssignedTo = LIVE_SHAPED_RELATIONSHIPS.filter(r => r.ReferencingAttribute !== 'sprk_assignedto');
    const { payload, result } = await create(
      withoutAssignedTo,
      form({ assignedToId: CONTACT_ID, assignedToName: 'X' })
    );

    expect(result.status).not.toBe('error');
    expect(result.workAssignmentId).toBe(WA_ID);
    // The person chose a contact: they are told it was not sent (not only a console warning).
    expect(result.warnings).toContain('Assigned To could not be set; the record uses its Assigned To Internal value.');
    expect(Object.keys(payload).filter(k => /assignedto/i.test(k))).toEqual([]);
    expect(Object.values(payload).some(v => v === `/contacts(${CONTACT_ID})`)).toBe(false);
    expect(
      warn.mock.calls.some(c => String(c[0]).includes('sprk_workassignment.sprk_assignedto nav-prop not found'))
    ).toBe(true);
  });
});
