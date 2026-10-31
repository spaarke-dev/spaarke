/**
 * CreateMatterWizard — the selected project is filed under the new matter (unified-access-control-r2 task 195, #2352).
 *
 * The Project ↔ Matter N:N is an association and confers nothing; the project's filing root is its regarding pair, which
 * only the BFF writes. The wizard's own `onFinish` runs here (the generic shell is a stub that hands over its config; the
 * matter create is a double; the shared filing function is the real one). Pinned:
 *   - a project association writes the N:N FIRST, then `PATCH /api/v1/child-records/sprk_project/{projectId}` naming the
 *     new matter;
 *   - a refused filing is a visible warning carrying the server's words — never swallowed; the link stays;
 *   - a failed N:N write files nothing (its own warning, as before);
 *   - an invoice association files no project;
 *   - a launch-context association (the subgrid "New Matter" command) reaches the Associate-To step, locked;
 *   - the project's pair is read first: a project already filed under another matter stays there unless the user
 *     chooses "Move" (the link is still written); an unreadable pair files nothing and warns; the result says which.
 */
import * as React from 'react';
import { act, fireEvent, screen } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import type { ICreateRecordWizardConfig, IFinishContext, IFollowOnState } from '../../CreateRecordWizard';
import type { IDataService, INavigationService } from '../../../types/serviceInterfaces';
import type { AssociationResult } from '../../AssociateToStep/types';
import { CreateMatterWizard } from '../CreateMatterWizard';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';

const mockShell: { config?: ICreateRecordWizardConfig } = {};
// The matter's details form is not under test here; the filing notice/choice rendered above it is.
jest.mock('../CreateRecordStep', () => ({ CreateRecordStep: () => null }));
jest.mock('../../CreateRecordWizard', () => ({
  CreateRecordWizard: (props: { config: ICreateRecordWizardConfig }) => {
    mockShell.config = props.config;
    return null;
  },
}));

const MATTER_ID = 'e1950000-0000-4000-8000-000000000001';
const PROJECT_ID = 'f1950000-0000-4000-8000-000000000002';
const MATTER_TYPE_ID = 'e8547bb4-8600-f111-8407-7c1e520aa4df';
const BFF = 'https://bff.example.test';
const OTHER_MATTER_ID = 'a1950000-0000-4000-8000-000000000003';

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

/** Every side effect in order: the N:N write (cookie fetch to Dataverse) and the BFF calls. */
const calls: string[] = [];
let nnStatus = 204;

function dataverseFetch() {
  return jest.fn(async (url: string) => {
    calls.push(`dataverse ${url}`);
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

const project: AssociationResult = {
  entityType: 'sprk_project',
  recordId: `{${PROJECT_ID.toUpperCase()}}`,
  recordName: 'PRJ-1',
};

function render(authFetch: jest.Mock, props: Partial<React.ComponentProps<typeof CreateMatterWizard>> = {}) {
  renderWithProviders(
    <CreateMatterWizard
      open
      onClose={jest.fn()}
      dataService={dataService}
      authenticatedFetch={authFetch as never}
      bffBaseUrl={BFF}
      {...props}
    />
  );
  return mockShell.config!;
}

describe('CreateMatterWizard — the linked project is filed under the new matter (task 195)', () => {
  let spies: jest.SpyInstance[];
  const realFetch = global.fetch;

  beforeEach(() => {
    mockShell.config = undefined;
    calls.length = 0;
    nnStatus = 204;
    (global as unknown as { fetch: unknown }).fetch = dataverseFetch();
    spies = (['info', 'error', 'warn'] as const).map(m => jest.spyOn(console, m).mockImplementation(() => {}));
    // Default: the project's pair is empty (not filed under anything).
    (dataService.retrieveRecord as jest.Mock).mockReset().mockResolvedValue({});
  });

  /** The project's pair names another matter. */
  function projectFiledElsewhere() {
    (dataService.retrieveRecord as jest.Mock).mockImplementation(async (entity: string) => {
      calls.push(`read ${entity}`);
      return entity === 'sprk_project'
        ? {
            sprk_regardingrecordid: OTHER_MATTER_ID,
            sprk_regardingrecordname: 'MAT-OLD',
            _sprk_regardingrecordtype_value: MATTER_TYPE_ID,
          }
        : {};
    });
  }

  function bodyText(success: { body?: React.ReactNode }): string {
    const { container } = renderWithProviders(<>{success.body}</>);
    return container.textContent ?? '';
  }

  afterEach(() => {
    spies.forEach(s => s.mockRestore());
    (global as unknown as { fetch: unknown }).fetch = realFetch;
  });

  it('writes the N:N, THEN files the project under the new matter through the BFF, with no warning', async () => {
    const success = await render(bffFetch(noContent)).onFinish(contextWith(project));

    expect(calls).toHaveLength(2);
    expect(calls[0]).toBe(`dataverse /api/data/v9.0/sprk_projects(${PROJECT_ID})/sprk_Project_Matter_nn/$ref`);
    expect(calls[1]).toMatch(new RegExp(`^bff PATCH /api/v1/child-records/sprk_project/${PROJECT_ID} `));
    expect(JSON.parse(calls[1].slice(calls[1].indexOf('{')))).toEqual({
      sprk_regardingrecordid: MATTER_ID,
      'sprk_regardingrecordtype@odata.bind': `/sprk_recordtype_refs(${MATTER_TYPE_ID})`,
    });
    expect(success.warnings ?? []).toEqual([]);
    expect(dataService.updateRecord).not.toHaveBeenCalled();
    expect(bodyText(success)).toContain('"PRJ-1" is linked to this matter and filed under it.');
  });

  it('a project filed under another matter stays there by default: link written, no filing call, result says so', async () => {
    projectFiledElsewhere();

    const success = await render(bffFetch(noContent)).onFinish(contextWith(project));

    expect(calls.filter(c => c.startsWith('dataverse '))).toHaveLength(1);
    expect(calls.filter(c => c.startsWith('bff '))).toEqual([]);
    expect(success.warnings ?? []).toEqual([]);
    expect(bodyText(success)).toContain('"PRJ-1" is linked to this matter and stays filed under MAT-OLD.');
  });

  it('a project filed under another matter is moved when the user chooses "Move" before Finish', async () => {
    projectFiledElsewhere();
    const navigationService = { openRecord: jest.fn() } as unknown as INavigationService;
    render(bffFetch(noContent), { navigationService });

    await act(async () => {
      mockShell.config!.associateToStep!.onAssociationChange!(project);
    });
    renderWithProviders(<>{mockShell.config!.infoStep.renderContent([])}</>);
    expect(screen.getByText(/is filed under MAT-OLD\. Move it to the new matter\?/)).toBeTruthy();
    expect((screen.getByLabelText('Keep it filed under MAT-OLD') as HTMLInputElement).checked).toBe(true);
    await act(async () => {
      fireEvent.click(screen.getByLabelText('Move it to the new matter'));
    });

    const success = await mockShell.config!.onFinish(contextWith(project));

    const bff = calls.filter(c => c.startsWith('bff '));
    expect(bff).toHaveLength(1);
    expect(bff[0]).toMatch(new RegExp(`^bff PATCH /api/v1/child-records/sprk_project/${PROJECT_ID} `));
    expect(JSON.parse(bff[0].slice(bff[0].indexOf('{'))).sprk_regardingrecordid).toBe(MATTER_ID);
    expect(success.warnings ?? []).toEqual([]);
    expect(bodyText(success)).toContain('"PRJ-1" was moved from MAT-OLD to this matter');
  });

  it('an unreadable pair files nothing and warns; the link is still written', async () => {
    (dataService.retrieveRecord as jest.Mock).mockRejectedValue(new Error('read denied'));

    const success = await render(bffFetch(noContent)).onFinish(contextWith(project));

    expect(calls.filter(c => c.startsWith('dataverse '))).toHaveLength(1);
    expect(calls.filter(c => c.startsWith('bff '))).toEqual([]);
    const warnings = (success.warnings ?? []).join(' ');
    expect(warnings).toContain('where that project is filed could not be read (read denied)');
    expect(warnings).toContain('so it was not filed under this matter');
    expect(success.title).toBe('Matter created with warnings');
  });

  it("a refused filing is a visible warning with the server's words; the link stays", async () => {
    const refused = {
      ok: false,
      status: 409,
      json: async () => ({ title: 'Conflict', detail: 'The project changed while it was being filed; try again.' }),
    } as unknown as Response;

    const success = await render(bffFetch(refused)).onFinish(contextWith(project));

    expect(calls[0]).toMatch(/^dataverse .*sprk_Project_Matter_nn\/\$ref$/);
    const warnings = (success.warnings ?? []).join('\n');
    expect(warnings).toContain('linked to "PRJ-1", but that project could not be filed under this matter');
    expect(warnings).toContain('The project changed while it was being filed; try again.');
    expect(success.title).toBe('Matter created with warnings');
  });

  it('a failed N:N write files nothing and keeps its own warning', async () => {
    nnStatus = 403;

    const success = await render(bffFetch(noContent)).onFinish(contextWith(project));

    expect(calls.filter(c => c.startsWith('bff '))).toEqual([]);
    expect((success.warnings ?? []).join('\n')).toMatch(/Matter created, but could not link to "PRJ-1"/);
  });

  it('an invoice association files no project', async () => {
    await render(bffFetch(noContent)).onFinish(
      contextWith({ entityType: 'sprk_invoice', recordId: '22222222-2222-2222-2222-222222222222', recordName: 'INV-1' })
    );

    expect(calls.filter(c => c.includes('sprk_project'))).toEqual([]);
  });

  it('a launch-context association (subgrid "New Matter") reaches the Associate-To step, locked', () => {
    const navigationService = { openRecord: jest.fn() } as unknown as INavigationService;

    const config = render(bffFetch(noContent), {
      navigationService,
      initialAssociation: { entityType: 'sprk_project', recordId: PROJECT_ID, recordName: 'PRJ-1' },
      lockAssociation: true,
    });

    expect(config.associateToStep?.initialAssociation).toEqual({
      entityType: 'sprk_project',
      recordId: PROJECT_ID,
      recordName: 'PRJ-1',
    });
    expect(config.associateToStep?.lockAssociation).toBe(true);
  });
});
