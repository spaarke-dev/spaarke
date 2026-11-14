/**
 * CreateMatterWizard — the Create Event follow-on's Assigned To reaches the event (ontology task 180, fix round 1).
 *
 * The follow-on step collected Assigned To but Finish always created the event unassigned. The wizard's own `onFinish`
 * runs here (the generic shell is a stub that hands over its config; the matter create is a double; the event service is
 * the real one, writing through the fake BFF child-record route). Pinned:
 *   - an assignee in `followOn` is bound onto the event's contact-targeted Assigned To lookup;
 *   - an empty assignee still creates the event, with no assignee bind.
 */
import * as React from 'react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { bffChildWriteFetch, FAKE_BFF_BASE_URL } from '../../../__mocks__/bffChildWriteFake';
import type { ICreateRecordWizardConfig, IFinishContext, IFollowOnState } from '../../CreateRecordWizard';
import type { IDataService } from '../../../types/serviceInterfaces';
import { _resetNavPropCacheForTests } from '../../../services/PolymorphicResolverService';
import { CreateMatterWizard } from '../CreateMatterWizard';

const mockShell: { config?: ICreateRecordWizardConfig } = {};
jest.mock('../CreateRecordStep', () => ({ CreateRecordStep: () => null }));
jest.mock('../../CreateRecordWizard', () => ({
  CreateRecordWizard: (props: { config: ICreateRecordWizardConfig }) => {
    mockShell.config = props.config;
    return null;
  },
}));
jest.mock('../matterService', () => {
  const actual = jest.requireActual('../matterService');
  return {
    ...actual,
    MatterService: jest.fn().mockImplementation(() => ({
      createMatter: jest.fn().mockResolvedValue({
        status: 'success',
        matterId: 'e1950000-0000-4000-8000-000000000001',
        matterName: 'New Matter',
        warnings: [],
      }),
    })),
  };
});

const CONTACT_ID_RAW = '{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}';
const CONTACT_ID_CLEAN = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';

const captured: Record<string, Record<string, unknown>[]> = {};
const dataService = {
  createRecord: jest.fn(async (entity: string, data: Record<string, unknown>) => {
    (captured[entity] = captured[entity] ?? []).push(data);
    return '33333333-3333-3333-3333-333333333333';
  }),
  retrieveRecord: jest.fn(async () => ({})),
  retrieveMultipleRecords: jest.fn(async () => ({ entities: [] })),
  updateRecord: jest.fn(async () => undefined),
  deleteRecord: jest.fn(),
} as unknown as IDataService;

function stubNavProps() {
  (global as unknown as { fetch: unknown }).fetch = jest.fn(async () => ({
    ok: true,
    status: 200,
    json: async () => ({
      value: [
        {
          ReferencingAttribute: 'sprk_assignedto',
          ReferencingEntityNavigationPropertyName: 'sprk_AssignedTo',
          ReferencedEntity: 'contact',
        },
      ],
    }),
  }));
}

function finishContext(followOn: Partial<IFollowOnState>): IFinishContext {
  return {
    uploadedFiles: [],
    speContainerId: '',
    selectedActions: ['create-event'],
    followOn: {
      createEventName: 'Kick-off call',
      createEventTypeId: '',
      createEventTypeName: '',
      createEventDueDate: '',
      createEventPriority: 100000001,
      createEventDescription: '',
      createEventAssignedToId: '',
      createEventAssignedToName: '',
      ...followOn,
    } as IFollowOnState,
    association: null,
    selectedExistingRecord: null,
  };
}

async function runFinish(ctx: IFinishContext) {
  renderWithProviders(
    <CreateMatterWizard
      open
      onClose={jest.fn()}
      dataService={dataService}
      authenticatedFetch={bffChildWriteFetch(dataService) as never}
      bffBaseUrl={FAKE_BFF_BASE_URL}
    />
  );
  return mockShell.config!.onFinish(ctx);
}

describe('CreateMatterWizard — Create Event follow-on Assigned To', () => {
  let infoSpy: jest.SpyInstance;
  beforeEach(() => {
    for (const k of Object.keys(captured)) delete captured[k];
    _resetNavPropCacheForTests('sprk_event');
    stubNavProps();
    jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    infoSpy = jest.spyOn(console, 'info').mockImplementation(() => undefined);
  });
  afterEach(() => jest.restoreAllMocks());

  it('binds the follow-on assignee onto the created event', async () => {
    await runFinish(
      finishContext({ createEventAssignedToId: CONTACT_ID_RAW, createEventAssignedToName: 'Jane Attorney' })
    );

    const event = captured['sprk_event']?.[0];
    expect(event).toBeDefined();
    expect(event!['sprk_AssignedTo@odata.bind']).toBe(`/contacts(${CONTACT_ID_CLEAN})`);
  });

  it('with no assignee still creates the event and writes no assignee bind', async () => {
    await runFinish(finishContext({}));

    const event = captured['sprk_event']?.[0];
    expect(event).toBeDefined();
    expect(event!['sprk_eventname']).toBe('Kick-off call');
    expect(Object.keys(event!).some(k => k.startsWith('sprk_AssignedTo'))).toBe(false);
    expect(infoSpy).toHaveBeenCalledWith(
      expect.stringContaining('Event created and linked to matter'),
      expect.anything()
    );
  });
});
