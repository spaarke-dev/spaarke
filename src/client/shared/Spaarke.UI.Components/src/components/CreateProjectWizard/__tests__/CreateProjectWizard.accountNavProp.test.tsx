/**
 * CreateProjectWizard — the project's account lookup is bound through the shared nav-prop discovery (#2909).
 *
 * The wizard used to send its own metadata query for `sprk_project` and keep no cache. It now calls the shared
 * `discoverNavProps` (`PolymorphicResolverService.ts`), so a page that has already discovered `sprk_project` sends no
 * second request, and the bind key comes from the one cache. Pinned:
 *   - the account association binds through the discovered nav-prop (`sprk_Client@odata.bind` here, not the default);
 *   - a discovery already made on the page is reused: no second metadata request (fails on the private copy);
 *   - with no metadata the known column name `sprk_Account` stands, and the project is still created.
 *
 * The wizard's own `onFinish` runs; the generic shell is a stub that hands over its config and the project create is a
 * double (as in CreateProjectWizard.projectFiling.test.tsx).
 */
import * as React from 'react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import type { ICreateRecordWizardConfig, IFinishContext, IFollowOnState } from '../../CreateRecordWizard';
import type { IDataService } from '../../../types/serviceInterfaces';
import { CreateProjectWizard } from '../CreateProjectWizard';
import { discoverNavProps, _resetNavPropCacheForTests } from '../../../services/PolymorphicResolverService';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';

const mockShell: { config?: ICreateRecordWizardConfig } = {};
jest.mock('../../CreateRecordWizard', () => ({
  CreateRecordWizard: (props: { config: ICreateRecordWizardConfig }) => {
    mockShell.config = props.config;
    return null;
  },
}));

jest.mock('../projectService', () => ({
  ProjectService: jest.fn().mockImplementation(() => ({
    createProject: jest.fn().mockResolvedValue({
      success: true,
      projectId: 'c2909000-0000-4000-8000-000000000001',
      projectName: 'Acme',
      warnings: [],
    }),
  })),
}));

const PROJECT_ID = 'c2909000-0000-4000-8000-000000000001';
const ACCOUNT_ID = 'a2909000-0000-4000-8000-000000000002';

const PROJECT_RELATIONSHIPS = [
  { ReferencingAttribute: 'sprk_matter', ReferencingEntityNavigationPropertyName: 'sprk_Matter', ReferencedEntity: 'sprk_matter' },
  { ReferencingAttribute: 'sprk_client', ReferencingEntityNavigationPropertyName: 'sprk_Client', ReferencedEntity: 'account' },
];

function metadataResponse(value: unknown[]): Response {
  return { ok: true, status: 200, json: async () => ({ value }) } as unknown as Response;
}

const account = { entityType: 'account', recordId: ACCOUNT_ID, recordName: 'Acme Corp' };

function finishContext(): IFinishContext {
  return {
    uploadedFiles: [],
    speContainerId: '',
    selectedActions: [],
    followOn: {} as IFollowOnState,
    association: account,
    selectedExistingRecord: null,
  };
}

function makeDataService(): IDataService {
  return {
    createRecord: jest.fn(),
    retrieveRecord: jest.fn(),
    retrieveMultipleRecords: jest.fn(async () => ({ entities: [] })),
    updateRecord: jest.fn().mockResolvedValue(undefined),
    deleteRecord: jest.fn(),
  } as unknown as IDataService;
}

function finish(dataService: IDataService) {
  renderWithProviders(
    <CreateProjectWizard
      open
      onClose={jest.fn()}
      dataService={dataService}
      authenticatedFetch={throwingAuthenticatedFetch(async () => ({ ok: true, status: 204 }) as unknown as Response) as never}
      bffBaseUrl="https://bff.example.test"
      initialFormValues={{ projectName: 'Acme', isSecure: false }}
    />
  );
  return mockShell.config!.onFinish(finishContext());
}

describe('CreateProjectWizard — the account lookup uses the shared nav-prop discovery (#2909)', () => {
  const realFetch = global.fetch;
  let spies: jest.SpyInstance[];
  let metadataCalls: string[];

  beforeEach(() => {
    mockShell.config = undefined;
    metadataCalls = [];
    _resetNavPropCacheForTests();
    spies = (['info', 'error', 'warn'] as const).map(m => jest.spyOn(console, m).mockImplementation(() => {}));
  });

  afterEach(() => {
    spies.forEach(s => s.mockRestore());
    (global as unknown as { fetch: unknown }).fetch = realFetch;
    _resetNavPropCacheForTests();
  });

  function globalFetch(answer: (url: string) => Response | Promise<Response>) {
    (global as unknown as { fetch: unknown }).fetch = jest.fn(async (url: string) => {
      if (url.includes('EntityDefinitions(')) metadataCalls.push(url);
      return answer(url);
    });
  }

  it('binds the account through the discovered nav-prop, from one lowercase metadata request', async () => {
    globalFetch(() => metadataResponse(PROJECT_RELATIONSHIPS));
    const dataService = makeDataService();

    await finish(dataService);

    expect(dataService.updateRecord).toHaveBeenCalledWith('sprk_project', PROJECT_ID, {
      'sprk_Client@odata.bind': `/accounts(${ACCOUNT_ID})`,
    });
    expect(metadataCalls).toHaveLength(1);
    expect(metadataCalls[0]).toContain("EntityDefinitions(LogicalName='sprk_project')/ManyToOneRelationships");
  });

  it("reuses a discovery the page already made: no second metadata request, the cached nav-prop is bound", async () => {
    await discoverNavProps('sprk_project', jest.fn(async () => metadataResponse(PROJECT_RELATIONSHIPS)) as unknown as typeof fetch);
    globalFetch(() => {
      throw new Error('a second metadata request was sent');
    });
    const dataService = makeDataService();

    await finish(dataService);

    expect(metadataCalls).toEqual([]);
    expect(dataService.updateRecord).toHaveBeenCalledWith('sprk_project', PROJECT_ID, {
      'sprk_Client@odata.bind': `/accounts(${ACCOUNT_ID})`,
    });
  });

  it('with no metadata the known column name stands and the project is still created (must not fire)', async () => {
    globalFetch(() => ({ ok: false, status: 500, json: async () => ({}) }) as unknown as Response);
    const dataService = makeDataService();

    const success = await finish(dataService);

    expect(dataService.updateRecord).toHaveBeenCalledWith('sprk_project', PROJECT_ID, {
      'sprk_Account@odata.bind': `/accounts(${ACCOUNT_ID})`,
    });
    expect(success.warnings ?? []).toEqual([]);
  });
});
