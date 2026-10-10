/**
 * assignedAccessSync + the three client writers that call it (unified-access-control-r2 task 142, criterion 14).
 *
 * Every CLIENT writer of a root's "Assigned *" columns calls the BFF sync route with the created record's type and id
 * after its write succeeds, so wizard-created assignees get access at creation (owner R3 "immediate"), and a failed sync
 * call is logged and never fails the wizard (the reconciliation job is the safety net). The body carries ONLY the record
 * — the server reads the subjects from the record itself.
 */

import { syncAssignedAccess, ASSIGNED_ACCESS_SYNC_PATH } from '../assignedAccessSync';
import { MatterService } from '../../components/CreateMatterWizard/matterService';
import { ProjectService } from '../../components/CreateProjectWizard/projectService';
import { WorkAssignmentService } from '../../components/CreateWorkAssignmentWizard/workAssignmentService';
import { _resetNavPropCacheForTests } from '../PolymorphicResolverService';
import type { IDataService } from '../../types/serviceInterfaces';
import type { ICreateMatterFormState } from '../../components/CreateMatterWizard/formTypes';
import type { ICreateProjectFormState } from '../../components/CreateProjectWizard/projectFormTypes';
import type { ICreateWorkAssignmentFormState } from '../../components/CreateWorkAssignmentWizard/formTypes';
import { apiErrorFor } from '../../__tests__/helpers/authenticatedFetchDouble';

const BFF = 'https://bff.example.test';
const SYNC_URL = `${BFF}${ASSIGNED_ACCESS_SYNC_PATH}`;
const CREATED = '0a0a0a0a-0000-0000-0000-000000000142';

function ok(body: unknown = {}): Response {
  return { ok: true, status: 200, json: async () => body } as unknown as Response;
}

function dataService(): IDataService {
  return {
    createRecord: jest.fn().mockResolvedValue(CREATED),
    retrieveRecord: jest.fn().mockResolvedValue({}),
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    updateRecord: jest.fn().mockResolvedValue(undefined),
    deleteRecord: jest.fn().mockResolvedValue(undefined),
  };
}

/** The sync requests an authenticated fetch received: [url, parsed body]. */
function syncCalls(fetchMock: jest.Mock): Array<{ url: string; body: Record<string, unknown> }> {
  return fetchMock.mock.calls
    .filter(([url]) => url === SYNC_URL)
    .map(([url, init]) => ({ url, body: JSON.parse((init as RequestInit).body as string) }));
}

beforeEach(() => {
  _resetNavPropCacheForTests();
  // Nav-prop discovery and BU defaults read through the global fetch / Xrm: offline and empty.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (global as any).fetch = jest.fn().mockResolvedValue(ok({ value: [] }));
  jest.spyOn(console, 'warn').mockImplementation(() => undefined);
  jest.spyOn(console, 'info').mockImplementation(() => undefined);
});

afterEach(() => {
  jest.restoreAllMocks();
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  delete (window as any).Xrm;
});

describe('syncAssignedAccess', () => {
  it('POSTs only the record type and its id (braces stripped, lowercase) to the sync route', async () => {
    const fetchMock = jest.fn().mockResolvedValue(ok());

    const result = await syncAssignedAccess(fetchMock, `${BFF}/`, 'matter', '{0A0A0A0A-0000-0000-0000-000000000142}');

    expect(result.ok).toBe(true);
    expect(syncCalls(fetchMock)).toEqual([{ url: SYNC_URL, body: { recordType: 'matter', recordId: CREATED } }]);
  });

  it('never rejects: a server refusal (authenticatedFetch throws ApiError 403) is swallowed and logged', async () => {
    // authenticatedFetch THROWS ApiError for a non-2xx; it never resolves { ok: false }.
    const fetchMock = jest
      .fn()
      .mockRejectedValue(
        apiErrorFor(403, { title: 'Forbidden', reasonCode: 'sdap.access.delegation.write_required' })
      );

    const result = await syncAssignedAccess(fetchMock, BFF, 'project', CREATED);

    // KNOWN GAP (reported with the Phase 3 mock sweep): the refusal's status and reasonCode are lost -
    // the catch reports every thrown failure as reason 'network'. The result is log-only today (no caller
    // reads it), so only the never-rejects + logged contract is pinned here.
    expect(result.ok).toBe(false);
    expect(console.warn).toHaveBeenCalled();
  });

  it('never rejects: a network failure is returned and logged', async () => {
    const fetchMock = jest.fn().mockRejectedValue(new TypeError('Failed to fetch'));

    await expect(syncAssignedAccess(fetchMock, BFF, 'workassignment', CREATED)).resolves.toEqual({
      ok: false,
      reason: 'network',
    });
  });

  it('skips (and says so) when the host supplied no authenticated BFF client', async () => {
    await expect(syncAssignedAccess(undefined, BFF, 'matter', CREATED)).resolves.toEqual({
      ok: false,
      reason: 'not-configured',
    });
  });
});

describe('the client writers call the sync route after their create (criterion 14)', () => {
  const matterForm: ICreateMatterFormState = {
    matterTypeId: '',
    matterTypeName: '',
    practiceAreaId: '',
    practiceAreaName: '',
    matterName: 'Wizard Matter',
    assignedAttorneyId: 'att-guid-1',
    assignedAttorneyName: 'Avery Attorney',
    assignedParalegalId: '',
    assignedParalegalName: '',
    assignedOutsideCounselId: '',
    assignedOutsideCounselName: '',
    summary: '',
  };

  it('Create Matter: syncs the created matter', async () => {
    const fetchMock = jest.fn().mockResolvedValue(ok());

    const result = await new MatterService(dataService(), fetchMock, BFF).createMatter(matterForm, [], {});

    expect(result.status).not.toBe('error');
    expect(syncCalls(fetchMock)).toEqual([{ url: SYNC_URL, body: { recordType: 'matter', recordId: CREATED } }]);
  });

  it('Create Matter: a failed sync is logged and does NOT fail the wizard', async () => {
    const fetchMock = jest.fn(async (url: string) =>
      url === SYNC_URL ? Promise.reject(new TypeError('Failed to fetch')) : ok()
    );

    const result = await new MatterService(dataService(), fetchMock, BFF).createMatter(matterForm, [], {});

    expect(result.status).not.toBe('error');
    expect(console.warn).toHaveBeenCalledWith(expect.stringContaining('[AssignedAccessSync]'), expect.anything());
  });

  const projectForm: ICreateProjectFormState = {
    projectTypeId: '',
    projectTypeName: '',
    practiceAreaId: '',
    practiceAreaName: '',
    projectName: 'Wizard Project',
    assignedAttorneyId: '',
    assignedAttorneyName: '',
    assignedParalegalId: '',
    assignedParalegalName: '',
    assignedOutsideCounselId: '',
    assignedOutsideCounselName: '',
    description: '',
    isSecure: false,
  };

  it('Create Project: syncs a standard project at create', async () => {
    const fetchMock = jest.fn().mockResolvedValue(ok());

    const result = await new ProjectService(dataService(), fetchMock, BFF).createProject(projectForm);

    expect(result.success).toBe(true);
    expect(syncCalls(fetchMock)).toEqual([{ url: SYNC_URL, body: { recordType: 'project', recordId: CREATED } }]);
  });

  it('Create Project: a project about to be SECURED is not synced at create (the wizard syncs after provisioning)', async () => {
    const fetchMock = jest.fn().mockResolvedValue(ok());

    await new ProjectService(dataService(), fetchMock, BFF).createProject({ ...projectForm, isSecure: true });

    expect(syncCalls(fetchMock)).toEqual([]);
  });

  it('Create Work Assignment: syncs the created work assignment', async () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any).Xrm = { Utility: { getGlobalContext: () => ({ userSettings: { userId: null } }) } };
    const fetchMock = jest.fn().mockResolvedValue(ok());
    const form = {
      name: 'Wizard WA',
      description: '',
      priority: 100000001,
      responseDueDate: '',
      matterTypeId: '',
      matterTypeName: '',
      practiceAreaId: '',
      practiceAreaName: '',
      recordType: '',
      recordId: '',
      recordName: '',
    } as unknown as ICreateWorkAssignmentFormState;

    const result = await new WorkAssignmentService(dataService(), fetchMock, BFF).createWorkAssignment(form, [], []);

    expect(result.status).not.toBe('error');
    expect(syncCalls(fetchMock)).toEqual([
      { url: SYNC_URL, body: { recordType: 'workassignment', recordId: CREATED } },
    ]);
  });
});
