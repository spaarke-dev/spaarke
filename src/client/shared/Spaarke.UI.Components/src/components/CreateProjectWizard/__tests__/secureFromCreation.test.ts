/**
 * secureFromCreation.test.ts — field-mapping-server-write-path-r1 task 049 fix round 2: a project the wizard creates as
 * secure is secure from creation.
 *
 *   - `ProjectService.createProject` sends the intent (`secure=true`) with the BFF create when the Secure choice is on
 *     and reports `securedAtCreate`. No intent for an ordinary project. Task 022: every host creates through the BFF
 *     route, so the undecorated data service (the standalone code page) sends the intent too — it no longer creates
 *     through Xrm.WebApi.
 *   - `provisionSecureProject` with `securedAtCreate` treats `already_provisioned` as success (UAC-r2 ruling D2); without
 *     it, and for any other code, the answer is the failure it always was.
 */
import { ProjectService } from '../projectService';
import { provisionSecureProject } from '../provisioningService';
import type { ICreateProjectFormState } from '../projectFormTypes';
import type { IDataService } from '../../../types/serviceInterfaces';
import { withBffChildWrites } from '../../../utils/adapters/bffChildWriteAdapter';
import { ASSIGNED_ACCESS_SYNC_PATH } from '../../../services/assignedAccessSync';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';

const PROJECT_ID = '11111111-1111-1111-1111-111111111111';
const BFF = 'https://bff.example.test';

const FORM: ICreateProjectFormState = {
  projectTypeId: '',
  projectTypeName: '',
  practiceAreaId: '',
  practiceAreaName: '',
  projectName: 'Acme',
  assignedAttorneyId: '',
  assignedAttorneyName: '',
  assignedParalegalId: '',
  assignedParalegalName: '',
  assignedOutsideCounselId: '',
  assignedOutsideCounselName: '',
  description: '',
  isSecure: true,
};

function innerDataService(): { service: IDataService; createSpy: jest.Mock } {
  const createSpy = jest.fn().mockResolvedValue(PROJECT_ID);
  return {
    createSpy,
    service: {
      createRecord: createSpy,
      retrieveRecord: jest.fn().mockResolvedValue({}),
      retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
      updateRecord: jest.fn().mockResolvedValue(undefined),
      deleteRecord: jest.fn().mockResolvedValue(undefined),
    },
  };
}

/** The host's BFF fetch: every URL asked, the create answering 201 with the new id (and a warning), the sync 200. */
function bff(): { fetch: jest.Mock; urls: string[] } {
  const urls: string[] = [];
  const fetch = jest.fn().mockImplementation(async (url: string) => {
    urls.push(String(url));
    if (String(url).includes('/api/v1/child-records/sprk_project')) {
      return { ok: true, status: 201, json: async () => ({ id: PROJECT_ID, warnings: ['mapped value skipped'] }) };
    }
    return { ok: true, status: 200, json: async () => ({}) };
  });
  return { fetch, urls };
}

let consoleSpies: jest.SpyInstance[];

beforeEach(() => {
  // Nav-prop discovery (no lookups are bound in these forms).
  (global as unknown as { fetch: unknown }).fetch = jest
    .fn()
    .mockResolvedValue({ ok: true, json: async () => ({ value: [] }) });
  consoleSpies = (['info', 'error', 'warn'] as const).map(m => jest.spyOn(console, m).mockImplementation(() => {}));
});

afterEach(() => consoleSpies.forEach(s => s.mockRestore()));

describe('ProjectService.createProject — the secure intent travels with the BFF create', () => {
  it('a secure project through the BFF is created with secure=true and reports securedAtCreate', async () => {
    const { service, createSpy } = innerDataService();
    const host = bff();

    const result = await new ProjectService(
      withBffChildWrites(service, host.fetch, BFF),
      host.fetch,
      BFF
    ).createProject(FORM);

    expect(result.success).toBe(true);
    expect(result.projectId).toBe(PROJECT_ID);
    expect(result.securedAtCreate).toBe(true);
    expect(result.warnings).toContain('mapped value skipped');
    expect(host.urls.filter(u => u.includes('/api/v1/child-records/'))).toEqual([
      `${BFF}/api/v1/child-records/sprk_project?secure=true`,
    ]);
    expect(createSpy).not.toHaveBeenCalled();
    expect(host.urls.some(u => u.endsWith(ASSIGNED_ACCESS_SYNC_PATH))).toBe(false);
  });

  it('an ordinary project through the BFF sends no intent and reports none', async () => {
    const { service } = innerDataService();
    const host = bff();

    const result = await new ProjectService(
      withBffChildWrites(service, host.fetch, BFF),
      host.fetch,
      BFF
    ).createProject({
      ...FORM,
      isSecure: false,
    });

    expect(result.success).toBe(true);
    expect(result.securedAtCreate).toBeUndefined();
    expect(host.urls.filter(u => u.includes('/api/v1/child-records/'))).toEqual([
      `${BFF}/api/v1/child-records/sprk_project`,
    ]);
  });

  it('a secure project on an undecorated data service (the standalone code page) is created through the BFF with the intent (task 022)', async () => {
    const { service, createSpy } = innerDataService();
    const host = bff();

    const result = await new ProjectService(service, host.fetch, BFF).createProject(FORM);

    expect(result.success).toBe(true);
    expect(result.securedAtCreate).toBe(true);
    expect(createSpy).not.toHaveBeenCalled();
    expect(host.urls.filter(u => u.includes('/api/v1/child-records/'))).toEqual([
      `${BFF}/api/v1/child-records/sprk_project?secure=true`,
    ]);
  });

  it('a refused secure create (the delegation rule) is a failed create, and nothing else is asked', async () => {
    const { service } = innerDataService();
    const urls: string[] = [];
    const refusing = throwingAuthenticatedFetch(async (url: string) => {
      urls.push(String(url));
      return {
        ok: false,
        status: 403,
        json: async () => ({
          title: 'Forbidden',
          status: 403,
          detail: 'You need Write and Share access.',
          reasonCode: 'sdap.access.deny.share_required',
        }),
      } as unknown as Response;
    });

    const result = await new ProjectService(withBffChildWrites(service, refusing, BFF), refusing, BFF).createProject(
      FORM
    );

    expect(result.success).toBe(false);
    expect(result.securedAtCreate).toBeUndefined();
    expect(urls).toEqual([`${BFF}/api/v1/child-records/sprk_project?secure=true`]);
  });
});

describe('provisionSecureProject — already_provisioned after a secure-from-creation create (UAC-r2 ruling D2)', () => {
  const alreadyProvisioned = (code = 'sdap.provision.already_provisioned') =>
    throwingAuthenticatedFetch(
      async () =>
        ({
          ok: false,
          status: 409,
          json: async () => ({ title: 'Conflict', status: 409, reasonCode: code }),
        }) as unknown as Response
    );

  it('is success with securedAtCreate', async () => {
    const result = await provisionSecureProject(
      { projectId: PROJECT_ID, projectRef: 'Acme' },
      alreadyProvisioned() as unknown as typeof fetch,
      BFF,
      undefined,
      { securedAtCreate: true }
    );

    expect(result).toEqual({ success: true });
  });

  it('is success with securedAtCreate when the fetch returns the 409 instead of throwing it', async () => {
    const returning = jest.fn().mockResolvedValue({
      ok: false,
      status: 409,
      json: async () => ({ reasonCode: 'sdap.provision.already_provisioned' }),
    });

    const result = await provisionSecureProject(
      { projectId: PROJECT_ID, projectRef: 'Acme' },
      returning,
      BFF,
      undefined,
      {
        securedAtCreate: true,
      }
    );

    expect(result.success).toBe(true);
  });

  it('stays the already-provisioned failure without securedAtCreate', async () => {
    const result = await provisionSecureProject(
      { projectId: PROJECT_ID, projectRef: 'Acme' },
      alreadyProvisioned() as unknown as typeof fetch,
      BFF
    );

    expect(result.success).toBe(false);
    expect(result.failureKind).toBe('already-provisioned');
  });

  it('keeps every other code a failure even with securedAtCreate', async () => {
    const result = await provisionSecureProject(
      { projectId: PROJECT_ID, projectRef: 'Acme' },
      alreadyProvisioned('sdap.provision.owned_by_other_secure_team') as unknown as typeof fetch,
      BFF,
      undefined,
      { securedAtCreate: true }
    );

    expect(result.success).toBe(false);
    expect(result.failureKind).toBe('already-provisioned');
  });
});
