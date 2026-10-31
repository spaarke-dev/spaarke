/**
 * CreateProjectWizard — a project linked to a matter is filed under it (unified-access-control-r2 task 195, #2352).
 *
 * The Project ↔ Matter N:N is an association and confers nothing; the project's filing root is its regarding pair, which
 * only the BFF writes. The wizard's own `onFinish` runs here (the generic shell is a stub that hands over its config; the
 * project create is a double; the shared filing function is the real one). Pinned:
 *   - a matter association writes the N:N FIRST, then `PATCH /api/v1/child-records/sprk_project/{id}` naming the matter;
 *   - a refused filing is a visible warning carrying the server's words — never swallowed; the link stays;
 *   - a failed N:N write files nothing (its own warning, as before);
 *   - an account association files nothing;
 *   - a launch-context association (the subgrid "New Project" command) reaches the Associate-To step, locked.
 */
import * as React from 'react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import type { ICreateRecordWizardConfig, IFinishContext, IFollowOnState } from '../../CreateRecordWizard';
import type { IDataService, INavigationService } from '../../../types/serviceInterfaces';
import type { AssociationResult } from '../../AssociateToStep/types';
import { CreateProjectWizard } from '../CreateProjectWizard';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';

const mockShell: { config?: ICreateRecordWizardConfig } = {};
jest.mock('../../CreateRecordWizard', () => ({
  CreateRecordWizard: (props: { config: ICreateRecordWizardConfig }) => {
    mockShell.config = props.config;
    return null;
  },
}));

const PROJECT_ID = 'c1950000-0000-4000-8000-000000000001';
const MATTER_ID = 'd1950000-0000-4000-8000-000000000002';
const MATTER_TYPE_ID = 'e8547bb4-8600-f111-8407-7c1e520aa4df';
const BFF = 'https://bff.example.test';

jest.mock('../projectService', () => ({
  ProjectService: jest.fn().mockImplementation(() => ({
    createProject: jest.fn().mockResolvedValue({
      success: true,
      projectId: 'c1950000-0000-4000-8000-000000000001',
      projectName: 'Acme',
      warnings: [],
    }),
  })),
}));

/** Every side effect in order: the N:N write (cookie fetch to Dataverse) and the BFF calls. */
const calls: string[] = [];
let nnStatus = 204;

function dataverseFetch() {
  return jest.fn(async (url: string) => {
    calls.push(`dataverse ${url.replace(window.location.origin, '')}`);
    return { ok: nnStatus < 300, status: nnStatus, text: async () => 'nope' } as unknown as Response;
  });
}

const dataService = {
  createRecord: jest.fn(),
  retrieveRecord: jest.fn(),
  retrieveMultipleRecords: jest.fn(async (entity: string) =>
    entity === 'sprk_recordtype_ref'
      ? { entities: [{ sprk_recordtype_refid: MATTER_TYPE_ID, sprk_recorddisplayname: 'Matter' }] }
      : { entities: [] }
  ),
  updateRecord: jest.fn().mockResolvedValue(undefined),
  deleteRecord: jest.fn(),
} as unknown as IDataService;

function bffFetch(filingResponse: Response) {
  return throwingAuthenticatedFetch(async (url: string, init?: RequestInit) => {
    calls.push(`bff ${init?.method ?? 'GET'} ${url.replace(BFF, '')} ${init?.body ?? ''}`);
    return filingResponse;
  });
}

const noContent = { ok: true, status: 204, json: async () => ({}), text: async () => '' } as unknown as Response;

function contextWith(association: AssociationResult | null): IFinishContext {
  return {
    uploadedFiles: [],
    speContainerId: '',
    selectedActions: [],
    followOn: {} as IFollowOnState,
    association,
    selectedExistingRecord: null,
  };
}

const matter: AssociationResult = {
  entityType: 'sprk_matter',
  recordId: `{${MATTER_ID.toUpperCase()}}`,
  recordName: 'MAT-1',
};

function render(authFetch: jest.Mock, props: Partial<React.ComponentProps<typeof CreateProjectWizard>> = {}) {
  renderWithProviders(
    <CreateProjectWizard
      open
      onClose={jest.fn()}
      dataService={dataService}
      authenticatedFetch={authFetch as never}
      bffBaseUrl={BFF}
      initialFormValues={{ projectName: 'Acme', isSecure: false }}
      {...props}
    />
  );
  return mockShell.config!;
}

describe('CreateProjectWizard — the linked matter becomes the filing root (task 195)', () => {
  let spies: jest.SpyInstance[];
  const realFetch = global.fetch;

  beforeEach(() => {
    mockShell.config = undefined;
    calls.length = 0;
    nnStatus = 204;
    (global as unknown as { fetch: unknown }).fetch = dataverseFetch();
    spies = (['info', 'error', 'warn'] as const).map(m => jest.spyOn(console, m).mockImplementation(() => {}));
  });

  afterEach(() => {
    spies.forEach(s => s.mockRestore());
    (global as unknown as { fetch: unknown }).fetch = realFetch;
  });

  it('writes the N:N, THEN files the project under the matter through the BFF, with no warning', async () => {
    const success = await render(bffFetch(noContent)).onFinish(contextWith(matter));

    expect(calls).toHaveLength(2);
    expect(calls[0]).toBe(`dataverse /api/data/v9.0/sprk_projects(${PROJECT_ID})/sprk_Project_Matter_nn/$ref`);
    expect(calls[1]).toMatch(new RegExp(`^bff PATCH /api/v1/child-records/sprk_project/${PROJECT_ID} `));
    expect(JSON.parse(calls[1].slice(calls[1].indexOf('{')))).toEqual({
      sprk_regardingrecordid: MATTER_ID,
      'sprk_regardingrecordtype@odata.bind': `/sprk_recordtype_refs(${MATTER_TYPE_ID})`,
    });
    expect(success.warnings ?? []).toEqual([]);
    expect(dataService.updateRecord).not.toHaveBeenCalled();
  });

  it("a refused filing is a visible warning with the server's words; the link stays", async () => {
    const refused = {
      ok: false,
      status: 403,
      json: async () => ({ title: 'Forbidden', detail: 'You cannot file records under this matter.' }),
    } as unknown as Response;

    const success = await render(bffFetch(refused)).onFinish(contextWith(matter));

    expect(calls[0]).toMatch(/^dataverse .*sprk_Project_Matter_nn\/\$ref$/);
    const warnings = (success.warnings ?? []).join('\n');
    expect(warnings).toContain('linked to "MAT-1", but it could not be filed under that matter');
    expect(warnings).toContain('You cannot file records under this matter.');
    expect(success.title).toBe('Project created with warnings');
  });

  it('a failed N:N write files nothing and keeps its own warning', async () => {
    nnStatus = 403;

    const success = await render(bffFetch(noContent)).onFinish(contextWith(matter));

    expect(calls.filter(c => c.startsWith('bff '))).toEqual([]);
    expect((success.warnings ?? []).join('\n')).toMatch(/Association with "MAT-1" could not be created/);
  });

  it('an account association files nothing', async () => {
    const success = await render(bffFetch(noContent)).onFinish(
      contextWith({ entityType: 'account', recordId: '11111111-1111-1111-1111-111111111111', recordName: 'Acme Corp' })
    );

    expect(calls.filter(c => c.startsWith('bff '))).toEqual([]);
    expect(success.warnings ?? []).toEqual([]);
  });

  it('a launch-context association (subgrid "New Project") reaches the Associate-To step, locked', () => {
    const navigationService = { openRecord: jest.fn() } as unknown as INavigationService;

    const config = render(bffFetch(noContent), {
      navigationService,
      initialAssociation: { entityType: 'sprk_matter', recordId: MATTER_ID, recordName: 'MAT-1' },
      lockAssociation: true,
    });

    expect(config.associateToStep?.initialAssociation).toEqual({
      entityType: 'sprk_matter',
      recordId: MATTER_ID,
      recordName: 'MAT-1',
    });
    expect(config.associateToStep?.lockAssociation).toBe(true);
  });
});
